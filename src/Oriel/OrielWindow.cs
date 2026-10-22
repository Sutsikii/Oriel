using Oriel.Ipc;
using Oriel.Platform;

namespace Oriel;

/// <summary>
/// A native window showing one of the app's pages. Open one with
/// <see cref="OrielApp.OpenWindow"/>. Its members can be used from any
/// thread; they take effect on the UI thread.
/// </summary>
public sealed class OrielWindow : IPlatformWindowEvents
{
    private readonly OrielApp _app;
    private readonly bool _titleFollowsPage;
    private IPlatformWindow? _native;
    private volatile string _title;
    private volatile bool _closed;
    private readonly RecoveryPolicy _recovery;

    internal OrielWindow(OrielApp app, WindowOptions options, Uri url)
    {
        _app = app;
        Options = options;
        Url = url;
        _titleFollowsPage = options.Title is null;
        _title = options.Title ?? app.Options.Name;
        _recovery = new RecoveryPolicy(app.Time);
        Connection = new PageConnection(app.Services, message => app.Dispatch(() => _native?.PostMessage(message)), this, PageLimits.From(app.Options), app.Dispatch);
        Connection.CallFailed += (method, error) => app.ReportCallFailure(method, error);
        Connection.MessageRefused += reason =>
            app.ReportError(new InvalidDataException($"The page of the window \"{Title}\" sent {reason}."));
    }

    /// <summary>The options the window opened with.</summary>
    public WindowOptions Options { get; }

    /// <summary>The URL of the page the window opened.</summary>
    public Uri Url { get; }

    /// <summary>The title of the window.</summary>
    public string Title
    {
        get => _title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _title = value;
            _app.Dispatch(() => _native?.SetTitle(value));
        }
    }

    /// <summary>Whether the window is closed.</summary>
    public bool IsClosed => _closed;

    /// <summary>Raised on the UI thread once the window is closed.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Raised on the UI thread when a process of the webview fails. Oriel
    /// recovers by itself: it reloads the page when its renderer failed,
    /// recreates the webview when the browser process did, and shows an error
    /// page when the page failed three times in a minute. Handlers can log the
    /// failure, or set <see cref="WebViewFailedEventArgs.Recovery"/> to do
    /// otherwise.
    /// </summary>
    public event EventHandler<WebViewFailedEventArgs>? WebViewFailed;

    /// <summary>
    /// Raised on the UI thread when the page navigates or opens a window.
    /// By default, the app's own pages stay in the window (or open in a new
    /// <see cref="OrielWindow"/>), other web pages and mail links open in the
    /// system's apps, and everything else is refused. Handlers may set
    /// <see cref="NavigationRequestedEventArgs.Action"/> to do otherwise.
    /// </summary>
    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;

    internal PageConnection Connection { get; }

    /// <summary>Closes the window.</summary>
    public void Close() => _app.Dispatch(() => _native?.Close());

    /// <summary>Opens the developer tools of the page, when <see cref="WindowOptions.DevTools"/> allows them.</summary>
    public void OpenDevTools() => _app.Dispatch(() => _native?.OpenDevTools());

    /// <summary>Opens the native window. UI thread only.</summary>
    internal void Open(IPlatform platform, PlatformWindowOptions options)
    {
        _native = platform.OpenWindow(options, this);
    }

    void IPlatformWindowEvents.MessageReceived(string source, string message)
    {
        if (!_app.Frontend.IsTrusted(source))
            return; // a page of another origin, which may not call services
        var received = Connection.ReceiveAsync(message);
        if (!received.IsCompleted)
            received.ContinueWith(t => _app.ReportError(t.Exception!), TaskContinuationOptions.OnlyOnFaulted);
    }

    void IPlatformWindowEvents.TitleChanged(string title)
    {
        if (!_titleFollowsPage || title.Length == 0)
            return;
        _title = title;
        _native?.SetTitle(title);
    }

    void IPlatformWindowEvents.Closed()
    {
        if (_closed)
            return;
        _closed = true;
        _native = null;
        Connection.Dispose();
        _app.WindowClosed(this);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    void IPlatformWindowEvents.Failed(Exception error) => _app.ReportError(error);

    NavigationAction IPlatformWindowEvents.NavigationStarting(string uri, bool isUserInitiated) =>
        Navigate(uri, isNewWindow: false, isUserInitiated);

    void IPlatformWindowEvents.NewWindowRequested(string uri, bool isUserInitiated)
    {
        if (Navigate(uri, isNewWindow: true, isUserInitiated) == NavigationAction.Allow)
        {
            _app.OpenWindow(new WindowOptions
            {
                Url = uri,
                Width = Options.Width,
                Height = Options.Height,
                DevTools = Options.DevTools,
                Permissions = Options.Permissions,
            });
        }
    }

    bool IPlatformWindowEvents.PermissionRequested(string uri, WebPermissions permission)
    {
        var granted = permission != WebPermissions.None
            && (Options.Permissions & permission) == permission
            && _app.Frontend.IsTrusted(uri);
        Tracing.Write($"{uri} asks for {permission}: {(granted ? "granted" : "denied")}");
        return granted;
    }

    /// <summary>
    /// Decides what a navigation or a new window does, lets handlers change
    /// it, and opens external pages. Returns what the backend should do.
    /// </summary>
    private NavigationAction Navigate(string uri, bool isNewWindow, bool isUserInitiated)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var target))
            return NavigationAction.Deny;
        var args = new NavigationRequestedEventArgs(target, isNewWindow, isUserInitiated, NavigationPolicy.Decide(_app.Frontend, target, isNewWindow));
        try
        {
            NavigationRequested?.Invoke(this, args);
        }
        catch (Exception e)
        {
            _app.ReportError(e);
            args.Action = NavigationAction.Deny; // a broken handler lets nothing through
        }

        Tracing.Write($"{(isNewWindow ? "new window" : "navigation")} to {target}: {args.Action}");
        if (args.Action == NavigationAction.OpenExternally)
        {
            // Only links the system opens safely: no file: or custom schemes, whatever a handler said.
            if (target.Scheme is "http" or "https" or "mailto")
                _app.OpenExternally(target);
            return NavigationAction.Deny;
        }
        return args.Action;
    }

    void IPlatformWindowEvents.WebViewFailed(WebViewFailure failure)
    {
        var args = new WebViewFailedEventArgs(failure, _recovery.Decide(failure));
        try
        {
            WebViewFailed?.Invoke(this, args);
        }
        catch (Exception e)
        {
            _app.ReportError(e); // the recovery happens all the same
        }

        if (failure is not (WebViewFailure.GpuExited or WebViewFailure.Other))
            _app.ReportError(new InvalidOperationException($"The webview of the window \"{Title}\" failed ({failure}); recovery: {args.Recovery}."));

        switch (args.Recovery)
        {
            case WebViewRecovery.Reload:
                _native?.Reload();
                break;
            case WebViewRecovery.Recreate:
                _native?.Recreate();
                break;
            case WebViewRecovery.ErrorPage:
                _native?.ShowErrorPage(OrielApp.FailedPage(Url));
                break;
        }
    }
}
