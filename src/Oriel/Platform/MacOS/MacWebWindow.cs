using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Encodings.Web;
using System.Text.Json;
using static Oriel.Platform.MacOS.MacPlatform;
using static Oriel.Platform.MacOS.ObjC;

namespace Oriel.Platform.MacOS;

/// <summary>
/// An NSWindow filled by a WKWebView. Pages post to the script message
/// handler <c>oriel</c>; messages reach them through
/// <c>window.__orielReceive</c>, as bridge.js expects of WebKit.
/// </summary>
/// <remarks>
/// One object of the class <c>OrielWebWindow</c>, made at run time, is the
/// window's delegate, the view's navigation and UI delegates, its script
/// message handler, the handler of the oriel scheme and the observer of
/// its title; its methods find their window in <see cref="s_windows"/>.
/// Positions are Oriel's, from the top left of the main screen; AppKit's
/// start at its bottom left.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacWebWindow : IPlatformWindow
{
    // NSWindowStyleMask
    private const nint Titled = 1, Closable = 2, Miniaturizable = 4, Resizable = 8, FullSizeContentView = 1 << 15;

    private static readonly Dictionary<nint, MacWebWindow> s_windows = [];

    // The scheme tasks sending a shared stream, which WebKit may stop. Main thread only.
    private static readonly HashSet<nint> s_streaming = [];
    private static nint s_class;

    private readonly PlatformWindowOptions _options;
    private readonly IPlatformWindowEvents _events;
    private nint _handler;
    private nint _window;
    private nint _webview;
    private bool _destroyed;
    private WindowBounds _normal;
    private WindowState _state;
    private bool _fullScreen;

    public MacWebWindow(PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        _options = options;
        _events = events;
    }

    private static nint HandlerClass() => s_class != 0 ? s_class : s_class = DefineClass("OrielWebWindow",
        ["NSWindowDelegate", "WKNavigationDelegate", "WKUIDelegate", "WKScriptMessageHandler", "WKURLSchemeHandler"],
        ("windowWillClose:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnWillClose, "v@:@"),
        ("windowShouldClose:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&OnShouldClose, "c@:@"),
        ("windowDidMove:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMoved, "v@:@"),
        ("windowDidResize:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMoved, "v@:@"),
        ("windowDidMiniaturize:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnStateChanged, "v@:@"),
        ("windowDidDeminiaturize:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnStateChanged, "v@:@"),
        ("windowWillEnterFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnWillEnterFullScreen, "v@:@"),
        ("windowDidExitFullScreen:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnDidExitFullScreen, "v@:@"),
        ("windowDidBecomeKey:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnBecameKey, "v@:@"),
        ("windowDidResignKey:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnResignedKey, "v@:@"),
        ("userContentController:didReceiveScriptMessage:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&OnScriptMessage, "v@:@@"),
        ("observeValueForKeyPath:ofObject:change:context:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, void>)&OnTitle, "v@:@@@^v"),
        ("webView:decidePolicyForNavigationAction:decisionHandler:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&OnDecidePolicy, "v@:@@@?"),
        ("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint>)&OnCreateWebView, "@@:@@@@"),
        ("webView:requestMediaCapturePermissionForOrigin:initiatedByFrame:type:decisionHandler:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, void>)&OnMediaPermission, "v@:@@@q@?"),
        ("webViewWebContentProcessDidTerminate:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnProcessTerminated, "v@:@"),
        ("webView:startURLSchemeTask:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&OnStartTask, "v@:@@"),
        ("webView:stopURLSchemeTask:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&OnStopTask, "v@:@@"));

    public void Start()
    {
        _handler = Send(Send(HandlerClass(), Sel("alloc")), Sel("init"));
        s_windows[_handler] = this;
        MacPlatform.Windows.Add(this);

        var style = Titled | Closable | Miniaturizable | Resizable | (_options.Frameless ? FullSizeContentView : 0);
        var content = new NSRect(0, 0, _options.Width, _options.Height);
        _window = Send(Send(Class("NSWindow"), Sel("alloc")), Sel("initWithContentRect:styleMask:backing:defer:"), content, style, (nint)2, NO); // NSBackingStoreBuffered
        Send(_window, Sel("setReleasedWhenClosed:"), NO);
        Send(_window, Sel("setTitle:"), Str(_options.Title));
        if (_options.Frameless)
        {
            // The frame stays, for its shadow, resizing and the keyboard; its bar and buttons go.
            Send(_window, Sel("setTitlebarAppearsTransparent:"), YES);
            Send(_window, Sel("setTitleVisibility:"), (nint)1); // NSWindowTitleHidden
            for (var button = 0; button < 3; button++) // close, miniaturize, zoom
                Send(Send(_window, Sel("standardWindowButton:"), (nint)button), Sel("setHidden:"), YES);
        }
        if (_options.MinWidth > 0 || _options.MinHeight > 0)
            Send(_window, Sel("setMinSize:"), new NSSize(_options.MinWidth, _options.MinHeight));
        if (_options.AlwaysOnTop)
            SetAlwaysOnTop(true);

        var configuration = New("WKWebViewConfiguration");
        var scripts = Send(configuration, Sel("userContentController"));
        Send(scripts, Sel("addScriptMessageHandler:name:"), _handler, Str("oriel"));
        var bridge = Send(Send(Class("WKUserScript"), Sel("alloc")), Sel("initWithSource:injectionTime:forMainFrameOnly:"),
            Str(_options.BridgeScript), (nint)0, YES); // WKUserScriptInjectionTimeAtDocumentStart
        Send(scripts, Sel("addUserScript:"), bridge);
        Release(bridge);
        Send(configuration, Sel("setURLSchemeHandler:forURLScheme:"), _handler, Str(AppScheme.Scheme));
        var preferences = Send(configuration, Sel("preferences"));
        // So that window.open reaches createWebView, and Oriel, which decides: WebKit opens nothing itself.
        Send(preferences, Sel("setJavaScriptCanOpenWindowsAutomatically:"), YES);
        if (_options.DevTools)
            Send(preferences, Sel("setValue:forKey:"), Send(Class("NSNumber"), Sel("numberWithBool:"), YES), Str("developerExtrasEnabled"));

        _webview = Send(Send(Class("WKWebView"), Sel("alloc")), Sel("initWithFrame:configuration:"), content, configuration);
        Release(configuration);
        if (_options.DevTools && RespondsTo(_webview, "setInspectable:"))
            Send(_webview, Sel("setInspectable:"), YES); // macOS 13.3 and later: Safari's Develop menu lists the page
        Send(_webview, Sel("setNavigationDelegate:"), _handler);
        Send(_webview, Sel("setUIDelegate:"), _handler);
        Send(_webview, Sel("addObserver:forKeyPath:options:context:"), _handler, Str("title"), (nint)1, (nint)0); // NSKeyValueObservingOptionNew
        Send(_window, Sel("setContentView:"), _webview);
        Send(_window, Sel("makeFirstResponder:"), _webview); // the keyboard goes to the page
        Send(_window, Sel("setDelegate:"), _handler);

        var restored = Restore(_options.Placement, out var zoomed);
        if (!restored)
        {
            // Sizes are the window's, frame included, as on Windows.
            if (_options.X is { } x && _options.Y is { } y)
            {
                SetFrame(x, y, _options.Width, _options.Height);
            }
            else
            {
                SetFrame(0, 0, _options.Width, _options.Height);
                Send(_window, Sel("center"));
            }
        }
        _normal = Bounds();
        Send(_window, Sel("makeKeyAndOrderFront:"), (nint)0);
        if (restored)
        {
            if (zoomed)
                SetState(WindowState.Maximized);
        }
        else if (_options.State != WindowState.Normal)
        {
            SetState(_options.State);
        }
        Navigate();
    }

    private void Navigate()
    {
        if (_options.ErrorPage is { } errorPage)
        {
            Tracing.Write($"window cannot show {_options.Url}: shows an error page");
            ShowErrorPage(errorPage);
            return;
        }
        var url = AppScheme.ToPage(_options.Url);
        Tracing.Write($"window navigates to {url}");
        Send(_webview, Sel("loadRequest:"), Send(Class("NSURLRequest"), Sel("requestWithURL:"), Url(url)));
    }

    // ---- Delegate methods, on the main thread ----

    /// <summary>Runs a delegate method of a window; an exception must not unwind into AppKit.</summary>
    private static void Guard(nint self, Action<MacWebWindow> handle)
    {
        if (!s_windows.TryGetValue(self, out var window) || window._destroyed)
            return;
        try
        {
            handle(window);
        }
        catch (Exception e)
        {
            window._events.Failed(e);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWillClose(nint self, nint sel, nint notification) => Guard(self, w =>
    {
        w._destroyed = true;
        MacPlatform.Windows.Remove(w);
        var scripts = Send(Send(w._webview, Sel("configuration")), Sel("userContentController"));
        // The controller keeps its handler, which would keep the window.
        Send(scripts, Sel("removeScriptMessageHandlerForName:"), Str("oriel"));
        Send(w._webview, Sel("removeObserver:forKeyPath:"), w._handler, Str("title"));
        Send(w._webview, Sel("setNavigationDelegate:"), (nint)0);
        Send(w._webview, Sel("setUIDelegate:"), (nint)0);
        Send(w._window, Sel("setDelegate:"), (nint)0);
        try
        {
            w._events.Closed();
        }
        finally
        {
            s_windows.Remove(w._handler);
            // Released once AppKit is done closing: the pool of this event drains them.
            Send(w._webview, Sel("autorelease"));
            Send(w._window, Sel("autorelease"));
            Send(w._handler, Sel("autorelease"));
        }
    });

    [UnmanagedCallersOnly]
    private static void OnMoved(nint self, nint sel, nint notification) => Guard(self, w =>
    {
        // Full screen, and the animations into it and out of it, are not where the window is.
        if (w._fullScreen)
            return;
        var bounds = w.Bounds();
        w.UpdateState();
        if (w._state == WindowState.Normal)
            w._normal = bounds;
        w._events.BoundsChanged(bounds);
    });

    [UnmanagedCallersOnly]
    private static void OnStateChanged(nint self, nint sel, nint notification) => Guard(self, w => w.UpdateState());

    [UnmanagedCallersOnly]
    private static void OnWillEnterFullScreen(nint self, nint sel, nint notification) => Guard(self, w => w._fullScreen = true);

    [UnmanagedCallersOnly]
    private static void OnDidExitFullScreen(nint self, nint sel, nint notification) => Guard(self, w =>
    {
        w._fullScreen = false;
        w.UpdateState();
    });

    private void UpdateState()
    {
        var state = SendBool(_window, Sel("isMiniaturized")) ? WindowState.Minimized
            : !_fullScreen && SendBool(_window, Sel("isZoomed")) ? WindowState.Maximized
            : WindowState.Normal;
        if (state == _state)
            return;
        _state = state;
        _events.StateChanged(state);
    }

    [UnmanagedCallersOnly]
    private static void OnBecameKey(nint self, nint sel, nint notification) => Guard(self, w => w._events.FocusChanged(true));

    [UnmanagedCallersOnly]
    private static void OnResignedKey(nint self, nint sel, nint notification) => Guard(self, w => w._events.FocusChanged(false));

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint self, nint sel, nint controller, nint message) => Guard(self, w =>
    {
        var body = Send(message, Sel("body"));
        if (!IsString(body))
            return;
        var text = Text(body)!;
        // The handler is the main frame's only, where the bridge runs: its document is the view's.
        var source = AppScheme.FromPage(UrlText(Send(w._webview, Sel("URL"))) ?? "");
        Tracing.Write($"window received {text}");
        w._events.MessageReceived(source, text);
    });

    [UnmanagedCallersOnly]
    private static void OnTitle(nint self, nint sel, nint keyPath, nint obj, nint change, nint context) => Guard(self, w =>
        w._events.TitleChanged(Text(Send(w._webview, Sel("title"))) ?? ""));

    [UnmanagedCallersOnly]
    private static void OnDecidePolicy(nint self, nint sel, nint webview, nint action, nint decisionHandler)
    {
        var allow = true;
        Guard(self, w =>
        {
            var frame = Send(action, Sel("targetFrame"));
            // No frame: a new window, which createWebView decides. A frame's own navigations are its page's.
            if (frame == 0 || !SendBool(frame, Sel("isMainFrame")))
                return;
            var uri = RequestUri(action);
            // An error page loaded as HTML has no URL of its own, and is the app's.
            if (uri.Length == 0 || uri == "about:blank")
                return;
            allow = w._events.NavigationStarting(uri, IsUserInitiated(action)) == NavigationAction.Allow;
        });
        CallBlock(decisionHandler, allow ? 1 : 0); // WKNavigationActionPolicyAllow, Cancel
    }

    private static string RequestUri(nint action) =>
        AppScheme.FromPage(UrlText(Send(Send(action, Sel("request")), Sel("URL"))) ?? "");

    // A link clicked or a form sent; WebKit says no more of gestures.
    private static bool IsUserInitiated(nint action) => Send(action, Sel("navigationType")) is 0 or 1;

    /// <summary>window.open: Oriel opens a window, the browser or nothing; the view opens none.</summary>
    [UnmanagedCallersOnly]
    private static nint OnCreateWebView(nint self, nint sel, nint webview, nint configuration, nint action, nint features)
    {
        Guard(self, w => w._events.NewWindowRequested(RequestUri(action), IsUserInitiated(action)));
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void OnMediaPermission(nint self, nint sel, nint webview, nint origin, nint frame, nint type, nint decisionHandler) =>
        // Pages get no permission: the same as WebPermissions.None on Windows.
        CallBlock(decisionHandler, 2); // WKPermissionDecisionDeny

    [UnmanagedCallersOnly]
    private static void OnProcessTerminated(nint self, nint sel, nint webview) => Guard(self, w =>
        w._events.WebViewFailed(WebViewFailure.RendererExited));

    [UnmanagedCallersOnly]
    private static void OnStartTask(nint self, nint sel, nint webview, nint task)
    {
        // Answered whether the window is still there or not, since WebKit waits for it.
        s_windows.TryGetValue(self, out var window);
        try
        {
            Serve(task, window?._options);
        }
        catch (Exception e)
        {
            MacPlatform.Report(e);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnStopTask(nint self, nint sel, nint webview, nint task)
    {
        // Files are answered at once; shared streams stop sending.
        s_streaming.Remove(task);
    }

    private static void Serve(nint task, PlatformWindowOptions? options)
    {
        var request = Send(task, Sel("request"));
        var url = Send(request, Sel("URL"));
        if (options?.Streams is { } streams && Uri.TryCreate(UrlText(url), UriKind.Absolute, out var asked) && StreamServer.TryGetId(asked, out var id))
        {
            var range = Send(request, Sel("valueForHTTPHeaderField:"), Str("Range"));
            var method = Send(request, Sel("HTTPMethod"));
            ServeStream(task, url, streams.Serve(id, range == 0 ? null : Text(range), method == 0 ? "GET" : Text(method) ?? "GET"));
            return;
        }
        var path = AppScheme.FilePath(Uri.TryCreate(UrlText(url), UriKind.Absolute, out var uri) ? uri.AbsolutePath : "/");
        var file = options is null ? null : AppScheme.Read(path, options.AssetsFolder, options.EmbeddedAssets);
        Tracing.Write($"serves {path}{(file is null ? ": not found" : "")}");
        // A missing file is a 404, as from a server, rather than a failed load.
        var body = file ?? "Not Found"u8.ToArray();
        var type = file is null ? "text/plain; charset=utf-8" : EmbeddedFrontend.ContentType(path);
        // Headers, which fetch() and WebAssembly.compileStreaming read.
        var keys = stackalloc nint[] { Str("Content-Type"), Str("Content-Length"), Str("Cache-Control") };
        var values = stackalloc nint[] { Str(type), Str(body.Length.ToString(CultureInfo.InvariantCulture)), Str("no-cache") };
        var headers = Send(Class("NSDictionary"), Sel("dictionaryWithObjects:forKeys:count:"), (nint)values, (nint)keys, (nint)3);
        var response = Send(Send(Class("NSHTTPURLResponse"), Sel("alloc")), Sel("initWithURL:statusCode:HTTPVersion:headerFields:"),
            url, file is null ? 404 : 200, Str("HTTP/1.1"), headers);
        Send(task, Sel("didReceiveResponse:"), response);
        Release(response);
        nint data;
        fixed (byte* p = body)
            data = Send(Class("NSData"), Sel("dataWithBytes:length:"), (nint)p, body.Length); // copies
        Send(task, Sel("didReceiveData:"), data);
        Send(task, Sel("didFinish"));
    }

    /// <summary>
    /// Answers with a shared stream: a task reads it, and sends it in chunks
    /// from the main thread, a few at a time, until WebKit stops the task.
    /// </summary>
    private static void ServeStream(nint task, nint url, StreamResponse answer)
    {
        Tracing.Write($"serves a shared stream: {answer.Status}");
        var count = answer.Headers.Count;
        var keys = stackalloc nint[count];
        var values = stackalloc nint[count];
        for (var i = 0; i < count; i++)
        {
            keys[i] = Str(answer.Headers[i].Key);
            values[i] = Str(answer.Headers[i].Value);
        }
        var headers = Send(Class("NSDictionary"), Sel("dictionaryWithObjects:forKeys:count:"), (nint)values, (nint)keys, (nint)count);
        var response = Send(Send(Class("NSHTTPURLResponse"), Sel("alloc")), Sel("initWithURL:statusCode:HTTPVersion:headerFields:"),
            url, answer.Status, Str("HTTP/1.1"), headers);
        Send(task, Sel("didReceiveResponse:"), response);
        Release(response);
        if (answer.Body is not { } body)
        {
            Send(task, Sel("didFinish"));
            return;
        }

        Send(task, Sel("retain"));
        s_streaming.Add(task);
        var sending = new SemaphoreSlim(4); // chunks on their way to the main thread
        var stopped = false;
        _ = Task.Run(() =>
        {
            try
            {
                using (body)
                {
                    while (!Volatile.Read(ref stopped))
                    {
                        var chunk = new byte[256 * 1024];
                        var read = body.Read(chunk);
                        if (read == 0)
                            break;
                        sending.Wait();
                        MacPlatform.PostToRunning(() =>
                        {
                            sending.Release();
                            if (!s_streaming.Contains(task))
                            {
                                Volatile.Write(ref stopped, true);
                                return;
                            }
                            nint data;
                            fixed (byte* p = chunk)
                                data = Send(Class("NSData"), Sel("dataWithBytes:length:"), (nint)p, read); // copies
                            Send(task, Sel("didReceiveData:"), data);
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Tracing.Write($"a shared stream failed to read: {e.Message}");
            }
            MacPlatform.PostToRunning(() =>
            {
                if (s_streaming.Remove(task))
                    Send(task, Sel("didFinish"));
                Release(task);
            });
        });
    }

    public void Fail(Exception error) => _events.Failed(error);

    // ---- IPlatformWindow ----

    public void PostMessage(string message)
    {
        if (_destroyed)
            return;
        // Before the bridge runs, __orielReceive is missing and the message is dropped, as on Windows.
        var literal = JsonEncodedText.Encode(message, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
        Send(_webview, Sel("evaluateJavaScript:completionHandler:"), Str($"window.__orielReceive && window.__orielReceive(\"{literal}\")"), (nint)0);
    }

    public void SetTitle(string title)
    {
        if (!_destroyed)
            Send(_window, Sel("setTitle:"), Str(title));
    }

    public void OpenDevTools() =>
        // WKWebView has no public way to open its inspector: the page's context menu has Inspect Element.
        Tracing.Write("macOS opens the Web Inspector from the page's context menu, with Inspect Element");

    public void Close()
    {
        if (!_destroyed)
            Send(_window, Sel("close")); // asks no windowShouldClose:, as the app chose
    }

    public void SetVisible(bool visible)
    {
        if (!_destroyed)
            Send(_window, Sel(visible ? "makeKeyAndOrderFront:" : "orderOut:"), (nint)0);
    }

    /// <summary>The close button: whether the window closes.</summary>
    [UnmanagedCallersOnly]
    private static byte OnShouldClose(nint self, nint sel, nint window)
    {
        var close = true;
        Guard(self, w => close = w._events.CloseRequested());
        return close ? (byte)1 : (byte)0;
    }

    public void Reload()
    {
        if (!_destroyed)
            Send(_webview, Sel("reload"));
    }

    public void Recreate()
    {
        if (!_destroyed)
            Navigate(); // WebKit starts a new web content process for the next load
    }

    public void ShowErrorPage(string html)
    {
        if (!_destroyed)
            Send(_webview, Sel("loadHTMLString:baseURL:"), Str(html), (nint)0);
    }

    public IReadOnlyList<string>? ShowFileDialog(FileDialogRequest request) => throw NotYet("File dialogs");

    public MessageResult ShowMessage(MessageRequest request) => throw NotYet("Message boxes");

    public void ShowPrintDialog(PrintDialog dialog) => throw NotYet("Printing");

    public void PrintToPdf(string path, PdfOptions? options, Action<bool> done) => throw NotYet("Printing to PDF");

    public void SetMenu(Menu? menu)
    {
        if (menu is not null)
            Tracing.Write("menus are not shown on macOS yet");
    }

    public MenuItem? ShowContextMenu(Menu menu, PagePoint? at) => throw NotYet("Context menus");

    public void ActivateMenuBar(char mnemonic)
    {
    }

    public void SetBounds(int? x, int? y, int? width, int? height)
    {
        if (_destroyed)
            return;
        if (_state != WindowState.Normal)
            SetState(WindowState.Normal);
        var current = Bounds();
        SetFrame(x ?? current.X, y ?? current.Y, width ?? current.Width, height ?? current.Height);
    }

    public void Center() => Send(_window, Sel("center"));

    public void SetState(WindowState state)
    {
        var miniaturized = SendBool(_window, Sel("isMiniaturized"));
        switch (state)
        {
            case WindowState.Minimized:
                if (!miniaturized)
                    Send(_window, Sel("miniaturize:"), (nint)0);
                break;
            case WindowState.Maximized:
                if (miniaturized)
                    Send(_window, Sel("deminiaturize:"), (nint)0);
                if (!SendBool(_window, Sel("isZoomed")))
                    Send(_window, Sel("zoom:"), (nint)0);
                break;
            default:
                if (miniaturized)
                    Send(_window, Sel("deminiaturize:"), (nint)0);
                if (SendBool(_window, Sel("isZoomed")))
                    Send(_window, Sel("zoom:"), (nint)0);
                break;
        }
        UpdateState();
    }

    public void SetFullScreen(bool fullScreen)
    {
        if (fullScreen != _fullScreen)
            Send(_window, Sel("toggleFullScreen:"), (nint)0);
    }

    public void SetAlwaysOnTop(bool onTop) => Send(_window, Sel("setLevel:"), onTop ? 3 : 0); // NSFloatingWindowLevel, NSNormalWindowLevel

    public void Focus()
    {
        if (SendBool(_window, Sel("isMiniaturized")))
            Send(_window, Sel("deminiaturize:"), (nint)0);
        Send(_window, Sel("makeKeyAndOrderFront:"), (nint)0);
        Send(Send(Class("NSApplication"), Sel("sharedApplication")), Sel("activateIgnoringOtherApps:"), YES);
    }

    /// <summary>The normal bounds and whether the window is maximized: <c>x,y,width,height,maximized</c>.</summary>
    public string Placement() => string.Create(CultureInfo.InvariantCulture,
        $"{_normal.X},{_normal.Y},{_normal.Width},{_normal.Height},{(_state == WindowState.Maximized ? 1 : 0)}");

    private bool Restore(string? placement, out bool zoomed)
    {
        zoomed = false;
        var parts = placement?.Split(',');
        if (parts is not { Length: 5 }
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out var width) || width <= 0
            || !int.TryParse(parts[3], CultureInfo.InvariantCulture, out var height) || height <= 0)
            return false;
        SetFrame(x, y, width, height);
        zoomed = parts[4] == "1"; // once shown
        return true;
    }

    /// <summary>The window's frame, from the top left of the main screen.</summary>
    private WindowBounds Bounds()
    {
        var frame = SendRect(_window, Sel("frame"));
        return new WindowBounds((int)frame.X, (int)(MainScreenHeight() - frame.Y - frame.Height), (int)frame.Width, (int)frame.Height);
    }

    private void SetFrame(int x, int y, int width, int height) =>
        Send(_window, Sel("setFrame:display:"), new NSRect(x, MainScreenHeight() - y - height, width, height), YES);

    /// <summary>The height of the screen with the menu bar, whose bottom left is AppKit's origin.</summary>
    private static double MainScreenHeight()
    {
        var screens = Send(Class("NSScreen"), Sel("screens"));
        return Send(screens, Sel("count")) == 0 ? 0 : SendRect(Send(screens, Sel("objectAtIndex:"), (nint)0), Sel("frame")).Height;
    }

    public void RunEditCommand(string command)
    {
        // Copy is copy:, SelectAll selectAll:, sent along the responder chain from the view's focus.
        var action = char.ToLowerInvariant(command[0]) + command[1..] + ":";
        var responder = Send(_window, Sel("firstResponder"));
        Send(responder != 0 ? responder : _webview, Sel("tryToPerform:with:"), Sel(action), (nint)0);
    }

    private static NotSupportedException NotYet(string what) =>
        new($"Not available on macOS yet: {what}. See https://github.com/Sutsikii/Oriel/issues/31.");
}
