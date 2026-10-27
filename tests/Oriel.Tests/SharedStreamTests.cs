namespace Oriel.Tests;

public class SharedStreamTests
{
    private readonly StreamServer _server = new();

    private static byte[] Bytes(int length) => Enumerable.Range(0, length).Select(i => (byte)i).ToArray();

    private static string Header(StreamResponse response, string name) =>
        response.Headers.Single(h => h.Key == name).Value;

    private static byte[] Body(StreamResponse response)
    {
        using var body = response.Body!;
        using var copy = new MemoryStream();
        body.CopyTo(copy);
        return copy.ToArray();
    }

    private string Id(SharedStream shared) => Assert.IsType<string>(StreamServer.TryGetId(new Uri(shared.Url), out var id) ? id : null);

    [Theory]
    [InlineData("bytes=0-9", 0L, 9L)]
    [InlineData("bytes=10-", 10L, 99L)]
    [InlineData("bytes=-5", 95L, 99L)]
    [InlineData("bytes=-500", 0L, 99L)]
    [InlineData("bytes=90-500", 90L, 99L)]
    [InlineData("Bytes=1-2", 1L, 2L)]
    [InlineData("bytes=200-", 200L, 99L)] // kept, for a 416
    public void ReadsRangesOfOnePart(string range, long start, long end) =>
        Assert.Equal((start, end), StreamServer.ParseRange(range, 100));

    [Theory]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("items=0-1")]
    [InlineData("bytes=5-1")]
    [InlineData("bytes=-0")]
    [InlineData("bytes=a-b")]
    [InlineData("bytes=+1-2")]
    [InlineData("bytes=")]
    public void IgnoresOtherRanges(string range) => Assert.Null(StreamServer.ParseRange(range, 100));

    [Fact]
    public void ServesTheWholeStreamWithoutARange()
    {
        var shared = _server.Share(() => new MemoryStream(Bytes(100)), "video/mp4");
        var response = _server.Serve(Id(shared), null);
        Assert.Equal(200, response.Status);
        Assert.Equal("video/mp4", Header(response, "Content-Type"));
        Assert.Equal("100", Header(response, "Content-Length"));
        Assert.Equal("bytes", Header(response, "Accept-Ranges"));
        Assert.Equal("*", Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal(Bytes(100), Body(response));
    }

    [Fact]
    public void ServesAPartOfASeekableStream()
    {
        var shared = _server.Share(() => new MemoryStream(Bytes(100)), "video/mp4");
        var response = _server.Serve(Id(shared), "bytes=10-14");
        Assert.Equal(206, response.Status);
        Assert.Equal("bytes 10-14/100", Header(response, "Content-Range"));
        Assert.Equal("5", Header(response, "Content-Length"));
        Assert.Equal(5, response.Length);
        Assert.Equal(new byte[] { 10, 11, 12, 13, 14 }, Body(response));
    }

    [Fact]
    public void RefusesARangePastTheEnd()
    {
        var shared = _server.Share(() => new MemoryStream(Bytes(100)), "video/mp4");
        var response = _server.Serve(Id(shared), "bytes=100-");
        Assert.Equal(416, response.Status);
        Assert.Equal("bytes */100", Header(response, "Content-Range"));
        Assert.Null(response.Body);
    }

    [Fact]
    public void ServesAStreamThatCannotSeekWhole()
    {
        var shared = _server.Share(() => new System.IO.Compression.GZipStream(new MemoryStream(Zipped(Bytes(50))), System.IO.Compression.CompressionMode.Decompress), "application/octet-stream");
        var response = _server.Serve(Id(shared), "bytes=0-9");
        Assert.Equal(200, response.Status);
        Assert.Equal(-1, response.Length);
        Assert.DoesNotContain(response.Headers, h => h.Key is "Accept-Ranges" or "Content-Length");
        Assert.Equal(Bytes(50), Body(response));

        static byte[] Zipped(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest))
                zip.Write(data);
            return output.ToArray();
        }
    }

    [Fact]
    public void AnswersHeadsAndPreflights()
    {
        var opened = 0;
        var shared = _server.Share(() => { opened++; return new MemoryStream(Bytes(100)); }, "video/mp4");
        var head = _server.Serve(Id(shared), null, "HEAD");
        Assert.Equal(200, head.Status);
        Assert.Equal("100", Header(head, "Content-Length"));
        Assert.Null(head.Body);

        var preflight = _server.Serve(Id(shared), null, "OPTIONS");
        Assert.Equal(204, preflight.Status);
        Assert.Equal("Range", Header(preflight, "Access-Control-Allow-Headers"));
        Assert.Equal(1, opened); // a preflight opens nothing
    }

    [Fact]
    public void OpensTheStreamForEachRequestUntilDisposed()
    {
        var opened = 0;
        var shared = _server.Share(() => { opened++; return new MemoryStream(Bytes(3)); }, "text/plain");
        var id = Id(shared);
        Body(_server.Serve(id, null));
        Body(_server.Serve(id, null));
        Assert.Equal(2, opened);

        shared.Dispose();
        var gone = _server.Serve(id, null);
        Assert.Equal(404, gone.Status);
        Assert.Equal("*", Header(gone, "Access-Control-Allow-Origin"));
        Assert.Equal(2, opened);
    }

    [Fact]
    public void AnswersAFailureToOpenWithA500()
    {
        var shared = _server.Share(() => throw new FileNotFoundException("gone"), "text/plain");
        Assert.Equal(500, _server.Serve(Id(shared), null).Status);
    }

    [Fact]
    public void GivesEachStreamAUrlOfItsOwnThatNoPageGuesses()
    {
        var a = _server.Share(() => Stream.Null, "text/plain");
        var b = _server.Share(() => Stream.Null, "text/plain");
        Assert.NotEqual(a.Url, b.Url);
        Assert.Matches(OperatingSystem.IsWindows() ? "^https://streams\\.oriel/[0-9a-f]{32}$" : "^oriel://app\\.oriel/\\.oriel/streams/[0-9a-f]{32}$", a.Url);
        Assert.False(StreamServer.TryGetId(new Uri("https://app.oriel/index.html"), out _));
        Assert.True(StreamServer.TryGetId(new Uri("oriel://app.oriel/.oriel/streams/abc"), out var id));
        Assert.Equal("abc", id);
    }
}
