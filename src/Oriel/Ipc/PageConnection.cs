using System.Text.Json;
using Oriel.Generated;

namespace Oriel.Ipc;

/// <summary>
/// The app's end of the connection with the page a window shows. It runs the
/// calls the page makes and sends their results back, and drops whatever
/// belongs to a page that went away.
/// </summary>
/// <remarks>
/// Each page picks a random token when it starts and sends it with every
/// message. A new token means a new page (a navigation or a reload): the
/// calls of the previous one are canceled and their results never sent, so
/// they cannot resolve the promises of the new page.
/// </remarks>
internal sealed class PageConnection : IDisposable
{
    private readonly ServiceTable _services;
    private readonly Action<string> _post;
    private readonly OrielWindow? _window;
    private readonly PageLimits _limits;
    private readonly Lock _lock = new();
    private string? _token;
    private CancellationTokenSource _page = new();
    private bool _disposed;
    private int _callsInFlight;

    /// <param name="services">The services pages may call.</param>
    /// <param name="post">
    /// Delivers a message to the page. It is called from any thread, and must
    /// be safe to call after the page is gone.
    /// </param>
    /// <param name="window">The window the page is in, passed to the methods that ask for it.</param>
    /// <param name="limits">What pages may send. Defaults to the defaults of <see cref="AppOptions"/>.</param>
    public PageConnection(ServiceTable services, Action<string> post, OrielWindow? window = null, PageLimits? limits = null)
    {
        _services = services;
        _post = post;
        _window = window;
        _limits = limits ?? PageLimits.Default;
    }

    /// <summary>Raised when a call fails, with the method and the exception, before the page gets the error.</summary>
    public event Action<string, Exception>? CallFailed;

    /// <summary>
    /// Raised when a message breaks a limit or is not JSON, with the reason.
    /// A page of the app never sends such messages: it has a bug, or was made
    /// to send them.
    /// </summary>
    public event Action<string>? MessageRefused;

    /// <summary>
    /// Handles a message from the page. Calls run on the thread pool; the task
    /// completes once the reply, if any, is posted. Messages that are not JSON
    /// or break a limit are refused, other malformed ones ignored.
    /// </summary>
    public Task ReceiveAsync(string message)
    {
        if (message.Length > _limits.MaxMessageLength)
        {
            Refuse($"a message of {message.Length} characters, more than the {_limits.MaxMessageLength} allowed");
            return Task.CompletedTask;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = _limits.MaxMessageDepth });
        }
        catch (JsonException e)
        {
            // Parsing stops at the first level too deep, so a deep message is
            // never read whole either.
            Refuse($"a message that is not JSON of at most {_limits.MaxMessageDepth} levels: {e.Message}");
            return Task.CompletedTask;
        }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetString(root, "t", out var type)
            || !TryGetString(root, "k", out var token))
        {
            doc.Dispose();
            return Task.CompletedTask;
        }

        var cancellation = Enter(token);
        if (type == Protocol.Call
            && root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id)
            && TryGetString(root, "m", out var method)
            && root.TryGetProperty("a", out var args))
        {
            if (Interlocked.Increment(ref _callsInFlight) > _limits.MaxCallsInFlight)
            {
                Interlocked.Decrement(ref _callsInFlight);
                doc.Dispose();
                Refuse($"a call of {method} beyond the {_limits.MaxCallsInFlight} calls allowed to run at once");
                _post(Protocol.ErrorMessage(id, token, $"Too many calls at once: {_limits.MaxCallsInFlight} are running"));
                return Task.CompletedTask;
            }
            return Task.Run(async () =>
            {
                try
                {
                    await CallAsync(doc, id, token, method, args, cancellation).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _callsInFlight);
                }
            });
        }

        doc.Dispose(); // a hello, or a message of a later protocol
        return Task.CompletedTask;
    }

    /// <summary>Sends a message to the current page, if there is one.</summary>
    public void Send(string message)
    {
        lock (_lock)
        {
            if (_disposed || _token is null)
                return;
        }
        _post(message);
    }

    /// <summary>The page is gone for good: its window closed.</summary>
    public void Dispose()
    {
        CancellationTokenSource page;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            page = _page;
        }
        page.Cancel();
        page.Dispose();
    }

    /// <summary>Makes <paramref name="token"/> the current page's, canceling the calls of the one before.</summary>
    private CancellationToken Enter(string token)
    {
        CancellationTokenSource? previous = null;
        CancellationToken current;
        lock (_lock)
        {
            if (_disposed)
                return new CancellationToken(canceled: true);
            if (_token != token)
            {
                if (_token is not null)
                {
                    previous = _page;
                    _page = new CancellationTokenSource();
                }
                _token = token;
            }
            current = _page.Token;
        }
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }
        return current;
    }

    private async Task CallAsync(JsonDocument doc, long id, string token, string method, JsonElement args, CancellationToken cancellation)
    {
        string reply;
        using (doc)
        {
            try
            {
                var dot = method.IndexOf('.', StringComparison.Ordinal);
                if (dot <= 0)
                    throw new CodecException($"'{method}' does not name a method as Service.Method");
                if (!_services.TryGet(method[..dot], out var service))
                    throw new InvalidOperationException($"No service named {method[..dot]} is bound");
                var result = await service.InvokeAsync(method[(dot + 1)..], args, new CallContext(cancellation, _window)).ConfigureAwait(false);
                reply = Protocol.ResultMessage(id, token, result);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return; // the page went away
            }
            catch (Exception e)
            {
                CallFailed?.Invoke(method, e);
                reply = Protocol.ErrorMessage(id, token, e.Message);
            }
        }

        lock (_lock)
        {
            if (_disposed || _token != token)
                return; // the reply belongs to a page that went away
        }
        _post(reply);
    }

    private void Refuse(string reason)
    {
        Tracing.Write($"refused {reason}");
        MessageRefused?.Invoke(reason);
    }

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String)
        {
            value = e.GetString()!;
            return true;
        }
        value = "";
        return false;
    }
}
