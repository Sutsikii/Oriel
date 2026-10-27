using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Oriel.Platform;

namespace Oriel;

/// <summary>
/// Data the app lets its pages load by URL, as from a web server: a video
/// for a <c>&lt;video&gt;</c>, an image, an export to <c>fetch</c>. Made by
/// <see cref="OrielApp.Share"/>; pages can load it until it is disposed.
/// </summary>
/// <remarks>
/// Its data is read as pages ask for it, never all in memory. When the
/// stream can seek, pages may ask for parts of it (HTTP range requests),
/// which videos do to seek.
/// </remarks>
public sealed class SharedStream : IDisposable
{
    private readonly StreamServer _server;

    internal SharedStream(StreamServer server, string id, Func<Stream> open, string contentType)
    {
        _server = server;
        Id = id;
        Open = open;
        ContentType = contentType;
        Url = StreamServer.UrlOf(id);
    }

    /// <summary>
    /// The URL pages load it by, with a random part no page can guess. Give it
    /// to a page as the result of a call: <c>video.src = await Media.open(path)</c>.
    /// It may be of another origin than the app's pages (on Windows), and lets
    /// any origin fetch it.
    /// </summary>
    public string Url { get; }

    /// <summary>The <c>Content-Type</c> pages get it with.</summary>
    public string ContentType { get; }

    internal string Id { get; }

    internal Func<Stream> Open { get; }

    /// <summary>Stops serving it: pages that load it later get a 404. Loads under way finish.</summary>
    public void Dispose() => _server.Remove(Id);
}

/// <summary>An answer to a request for a shared stream.</summary>
/// <param name="Status">The HTTP status: 200, 206, 404, 416 or 500.</param>
/// <param name="Reason">The status's reason phrase.</param>
/// <param name="Headers">The response's headers.</param>
/// <param name="Body">The data to send, or null for none; whoever sends it disposes it.</param>
/// <param name="Length">How many bytes <paramref name="Body"/> has, or -1 when it does not say.</param>
internal sealed record StreamResponse(int Status, string Reason, IReadOnlyList<KeyValuePair<string, string>> Headers, Stream? Body, long Length)
{
    /// <summary>The headers as one string, <c>Name: value</c> lines, as WebView2 takes them.</summary>
    public string HeaderLines => string.Join("\r\n", Headers.Select(h => h.Key + ": " + h.Value));
}

/// <summary>
/// The shared streams of an app, and the answers to the requests for them:
/// with range requests when a stream can seek. The backends hand over the
/// requests of the URLs <see cref="TryGetId"/> takes.
/// </summary>
/// <remarks>
/// On Windows, streams are at <c>https://streams.oriel/{id}</c>: WebView2
/// serves app.oriel from the app's folder itself, and tells nothing of its
/// requests. On WebKit, at <c>oriel://app.oriel/.oriel/streams/{id}</c>,
/// where the scheme's handler sees every request.
/// </remarks>
internal sealed class StreamServer
{
    /// <summary>The host of shared streams on Windows.</summary>
    public const string Host = "streams.oriel";

    /// <summary>The path shared streams are served under, on the app's origin, on WebKit.</summary>
    public const string Prefix = "/.oriel/streams/";

    public static string UrlOf(string id) =>
        OperatingSystem.IsWindows() ? "https://" + Host + "/" + id : AppScheme.ToPage(new Uri(Frontend.AppOrigin + Prefix + id));

    private readonly ConcurrentDictionary<string, SharedStream> _streams = new(StringComparer.Ordinal);

    public SharedStream Share(Func<Stream> open, string contentType)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var shared = new SharedStream(this, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), open, contentType);
        _streams[shared.Id] = shared;
        return shared;
    }

    public void Remove(string id) => _streams.TryRemove(id, out _);

    /// <summary>
    /// Whether <paramref name="uri"/>, as the webview has it, is a shared
    /// stream's, and of which id; the stream may be gone.
    /// </summary>
    public static bool TryGetId(Uri uri, out string id)
    {
        id = "";
        if (string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
            id = uri.AbsolutePath.TrimStart('/');
        else if (string.Equals(uri.Host, Frontend.AppHost, StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith(Prefix, StringComparison.Ordinal))
            id = uri.AbsolutePath[Prefix.Length..];
        else
            return false;
        return true;
    }

    /// <summary>
    /// The answer to a request for the stream <paramref name="id"/>, with
    /// the request's <c>Range</c> header, if any, and its method: a
    /// <c>HEAD</c> gets no body, an <c>OPTIONS</c> what other origins may
    /// ask (a CORS preflight, which a <c>Range</c> header may need).
    /// </summary>
    public StreamResponse Serve(string id, string? range, string method = "GET")
    {
        if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return new StreamResponse(204, "No Content",
            [
                new("Access-Control-Allow-Origin", "*"),
                new("Access-Control-Allow-Methods", "GET, HEAD"),
                new("Access-Control-Allow-Headers", "Range"),
                new("Access-Control-Max-Age", "600"),
            ], null, 0);
        }
        var response = Get(id, range);
        if (!string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) || response.Body is null)
            return response;
        response.Body.Dispose();
        return response with { Body = null };
    }

    private StreamResponse Get(string id, string? range)
    {
        if (!_streams.TryGetValue(id, out var shared))
            return Text(404, "Not Found");
        Stream stream;
        try
        {
            stream = shared.Open() ?? throw new InvalidOperationException("The function of a shared stream returned null.");
        }
        catch (Exception e)
        {
            Tracing.Write($"could not open the shared stream {shared.Id}: {e.Message}");
            return Text(500, "Internal Server Error");
        }

        var headers = new List<KeyValuePair<string, string>>
        {
            new("Content-Type", shared.ContentType),
            new("Cache-Control", "no-store"),
            // Pages of the dev server, on another origin, may fetch it too: the URL is the secret.
            new("Access-Control-Allow-Origin", "*"),
            new("Access-Control-Expose-Headers", "Content-Range, Content-Length, Accept-Ranges"),
        };
        if (!stream.CanSeek)
            return new StreamResponse(200, "OK", headers, stream, -1);

        var length = stream.Length;
        headers.Add(new("Accept-Ranges", "bytes"));
        if (range is null || ParseRange(range, length) is not var (start, end))
        {
            headers.Add(new("Content-Length", length.ToString(CultureInfo.InvariantCulture)));
            return new StreamResponse(200, "OK", headers, stream, length);
        }
        if (start >= length)
        {
            stream.Dispose();
            headers.Add(new("Content-Range", $"bytes */{length}"));
            return new StreamResponse(416, "Range Not Satisfiable", headers, null, 0);
        }
        var count = end - start + 1;
        stream.Seek(start, SeekOrigin.Begin);
        headers.Add(new("Content-Length", count.ToString(CultureInfo.InvariantCulture)));
        headers.Add(new("Content-Range", $"bytes {start}-{end}/{length}"));
        return new StreamResponse(206, "Partial Content", headers, new Slice(stream, count), count);
    }

    /// <summary>
    /// The first and last byte of a <c>Range</c> header of one range
    /// (<c>bytes=0-99</c>, <c>bytes=100-</c>, <c>bytes=-100</c>), within
    /// <paramref name="length"/>; a start past the end is kept, for a 416.
    /// Null for anything else, which gets the whole stream, as HTTP allows.
    /// </summary>
    internal static (long Start, long End)? ParseRange(string range, long length)
    {
        range = range.Trim();
        if (!range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || range.Contains(','))
            return null;
        var spec = range["bytes=".Length..].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0)
            return null;
        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();
        const NumberStyles digits = NumberStyles.None;
        if (first.Length == 0)
        {
            // The last n bytes.
            if (!long.TryParse(last, digits, CultureInfo.InvariantCulture, out var suffix) || suffix == 0)
                return null;
            return (Math.Max(0, length - suffix), length - 1);
        }
        if (!long.TryParse(first, digits, CultureInfo.InvariantCulture, out var start))
            return null;
        if (last.Length == 0)
            return (start, length - 1);
        if (!long.TryParse(last, digits, CultureInfo.InvariantCulture, out var end) || end < start)
            return null;
        return (start, Math.Min(end, length - 1));
    }

    private static StreamResponse Text(int status, string reason) =>
        new(status, reason, [new("Content-Type", "text/plain; charset=utf-8"), new("Access-Control-Allow-Origin", "*")], new MemoryStream(System.Text.Encoding.UTF8.GetBytes(reason)), reason.Length);

    /// <summary>The next <c>count</c> bytes of a stream, which it disposes.</summary>
    private sealed class Slice(Stream inner, long count) : Stream
    {
        private long _left = count;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_left <= 0)
                return 0;
            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            _left -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
