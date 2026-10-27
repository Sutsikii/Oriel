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
    private readonly Action<Action>? _dispatch;
    private readonly bool _showExceptionMessages;
    private readonly Lock _lock = new();
    private string? _token;
    private CancellationTokenSource _page = new();
    private bool _disposed;
    private int _callsInFlight;

    /// <summary>
    /// The coalesced events of the current page, by name: whether a payload
    /// is on its way, not acknowledged yet, and the one waiting behind it.
    /// </summary>
    private readonly Dictionary<string, Coalesced> _coalesced = new(StringComparer.Ordinal);

    /// <param name="services">The services pages may call.</param>
    /// <param name="post">
    /// Delivers a message to the page. It is called from any thread, and must
    /// be safe to call after the page is gone.
    /// </param>
    /// <param name="window">The window the page is in, passed to the methods that ask for it.</param>
    /// <param name="limits">What pages may send. Defaults to the defaults of <see cref="AppOptions"/>.</param>
    /// <param name="dispatch">
    /// Runs an action on the UI thread, for the calls of
    /// <see cref="ServiceConcurrency.UiThread"/> services.
    /// </param>
    /// <param name="showExceptionMessages">
    /// Whether pages get the messages of unexpected exceptions, which may tell
    /// about the app's internals. Only in development.
    /// </param>
    public PageConnection(ServiceTable services, Action<string> post, OrielWindow? window = null, PageLimits? limits = null,
        Action<Action>? dispatch = null, bool showExceptionMessages = true)
    {
        _services = services;
        _post = post;
        _window = window;
        _limits = limits ?? PageLimits.Default;
        _dispatch = dispatch;
        _showExceptionMessages = showExceptionMessages;
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
    /// Raised when the page let a key with Ctrl or Alt, or a function key, go
    /// by without <c>preventDefault()</c>: it may be the accelerator of a menu.
    /// </summary>
    public event Action<KeyChord>? KeyPressed;

    /// <summary>The current page reported files dropped on it, with their paths: see <see cref="Protocol.Drop"/>.</summary>
    public event Action<FilesDroppedEventArgs>? FilesDropped;

    /// <summary>
    /// Handles a message from the page. Calls run on the thread pool; the task
    /// completes once the reply, if any, is posted. Messages that are not JSON
    /// or break a limit are refused, other malformed ones ignored.
    /// <paramref name="files"/> are the paths of the files the page sent
    /// with the message, which only a drop uses.
    /// </summary>
    public Task ReceiveAsync(string message, IReadOnlyList<string>? files = null)
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
        catch (ArgumentException e)
        {
            Refuse($"a message that is not valid UTF-16: {e.Message}"); // a lone surrogate, not escaped
            return Task.CompletedTask;
        }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !HasValidNames(root)
            || !TryGetString(root, "t", out var type)
            || !TryGetString(root, "k", out var token))
        {
            doc.Dispose();
            return Task.CompletedTask;
        }

        var cancellation = Enter(token);
        if (type == Protocol.Call
            && root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var id)
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
            // A serial service's turn is taken now, so calls run in the order they arrived.
            var service = Resolve(method, out var problem);
            var turn = service?.Concurrency == ServiceConcurrency.Serial ? service.Queue.Enter() : null;
            return Task.Run(async () =>
            {
                try
                {
                    await CallAsync(doc, id, token, method, service, problem, turn, args, cancellation).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _callsInFlight);
                }
            });
        }

        if (type == Protocol.Key
            && root.TryGetProperty("c", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var key)
            && root.TryGetProperty("m", out var mods) && mods.ValueKind == JsonValueKind.Number && mods.TryGetInt32(out var modifiers)
            && !cancellation.IsCancellationRequested)
        {
            KeyPressed?.Invoke(new KeyChord(key, (KeyModifiers)modifiers & (KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Alt)));
        }

        if (type == Protocol.Drop
            && files is { Count: > 0 }
            && root.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number
            && root.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number
            && !cancellation.IsCancellationRequested)
        {
            FilesDropped?.Invoke(new FilesDroppedEventArgs(files, x.GetDouble(), y.GetDouble()));
        }

        if (type == Protocol.Ack
            && root.TryGetProperty("n", out var names) && names.ValueKind == JsonValueKind.Array
            && !cancellation.IsCancellationRequested)
        {
            foreach (var name in names.EnumerateArray())
            {
                if (TryGetString(name, out var acked))
                    Acknowledge(token, acked);
            }
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

    /// <summary>
    /// Sends <paramref name="message"/>, of the coalesced event
    /// <paramref name="name"/>, to the current page: now if the page took
    /// the event's previous payload, else once it does, in place of any
    /// message of the event still waiting.
    /// </summary>
    public void SendLatest(string name, string message)
    {
        lock (_lock)
        {
            if (_disposed || _token is null)
                return;
            if (_coalesced.TryGetValue(name, out var slot))
            {
                slot.Waiting = message;
                return;
            }
            _coalesced.Add(name, new Coalesced());
            // Posted under the lock, so that an acknowledgement on another
            // thread cannot send a later payload before this one.
            _post(message);
        }
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
                _coalesced.Clear(); // what waited was for the page before
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

    /// <summary>
    /// The page of <paramref name="token"/> took the coalesced event
    /// <paramref name="name"/>: sends the payload waiting, if any.
    /// </summary>
    private void Acknowledge(string token, string name)
    {
        lock (_lock)
        {
            if (_disposed || _token != token || !_coalesced.TryGetValue(name, out var slot))
                return;
            if (slot.Waiting is not { } waiting)
            {
                _coalesced.Remove(name);
                return;
            }
            slot.Waiting = null;
            _post(waiting);
        }
    }

    /// <summary>The service <paramref name="method"/> belongs to, or null and why.</summary>
    private ServiceDispatcher? Resolve(string method, out Exception? problem)
    {
        problem = null;
        var dot = method.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
            problem = new CodecException($"'{method}' does not name a method as Service.Method");
        else if (_services.TryGet(method[..dot], out var service))
            return service;
        else
            problem = new ServiceNotFoundException(method[..dot]);
        return null;
    }

    private async Task CallAsync(JsonDocument doc, long id, string token, string method, ServiceDispatcher? service, Exception? problem,
        Task<IDisposable>? turn, JsonElement args, CancellationToken cancellation)
    {
        string reply;
        using (doc)
        using (turn is null ? null : await turn.ConfigureAwait(false))
        {
            try
            {
                if (service is null)
                    throw problem!;
                cancellation.ThrowIfCancellationRequested(); // the page went away while the call waited its turn
                var context = new CallContext(cancellation, _window);
                var name = method[(service.Name.Length + 1)..];
                var result = service.Concurrency == ServiceConcurrency.UiThread && _dispatch is not null
                    ? await OnUiThread(() => service.InvokeAsync(name, args, context)).ConfigureAwait(false)
                    : await service.InvokeAsync(name, args, context).ConfigureAwait(false);
                reply = Protocol.ResultMessage(id, token, result);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return; // the page went away
            }
            catch (Exception e)
            {
                CallFailed?.Invoke(method, e);
                reply = ErrorReply(id, token, method, e);
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

    /// <summary>
    /// Starts <paramref name="call"/> on the UI thread, with a context that
    /// brings its continuations back there.
    /// </summary>
    private Task<ValueWriter?> OnUiThread(Func<ValueTask<ValueWriter?>> call)
    {
        var done = new TaskCompletionSource<ValueWriter?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new UiSynchronizationContext(_dispatch!);
        _dispatch!(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Complete(call(), done);
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });
        return done.Task;

        static async void Complete(ValueTask<ValueWriter?> pending, TaskCompletionSource<ValueWriter?> done)
        {
            try
            {
                done.TrySetResult(await pending);
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
        }
    }

    /// <summary>
    /// The reply to a call that threw <paramref name="error"/>: with its code and
    /// data when it is a service error, without its message when it is
    /// unexpected and messages are hidden.
    /// </summary>
    private string ErrorReply(long id, string token, string method, Exception error)
    {
        if (ServiceRegistry.TryGetError(error, out var code, out var data))
        {
            try
            {
                return Protocol.ErrorMessage(id, token, error.Message, code, data);
            }
            catch (Exception e)
            {
                CallFailed?.Invoke(method, e); // a property of the error threw
            }
        }
        else if (_showExceptionMessages || error is CodecException or MethodNotFoundException or ServiceNotFoundException)
        {
            return Protocol.ErrorMessage(id, token, error.Message);
        }
        return Protocol.ErrorMessage(id, token, $"{method} failed");
    }

    /// <summary>
    /// Whether the names of <paramref name="obj"/> are valid UTF-16. Looking
    /// up a property unescapes the names it passes, and throws on a lone
    /// surrogate; checked first, the message is ignored wherever it is.
    /// </summary>
    private static bool HasValidNames(JsonElement obj)
    {
        try
        {
            foreach (var property in obj.EnumerateObject())
                _ = property.Name;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = "";
        try
        {
            return obj.TryGetProperty(name, out var e) && TryGetString(e, out value);
        }
        catch (InvalidOperationException)
        {
            return false; // an escaped lone surrogate
        }
    }

    private static bool TryGetString(JsonElement e, out string value)
    {
        value = "";
        if (e.ValueKind != JsonValueKind.String)
            return false;
        try
        {
            value = e.GetString()!;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // an escaped lone surrogate
        }
    }

    /// <summary>A coalesced event with a payload on its way to the page.</summary>
    private sealed class Coalesced
    {
        /// <summary>The message to send once the page takes the one on its way.</summary>
        public string? Waiting { get; set; }
    }
}
