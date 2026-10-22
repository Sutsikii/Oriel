using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Oriel.Platform.Windows;

/// <summary>
/// A Win32 window filled by a WebView2 control. The control is created
/// asynchronously: until it is, the window shows its background and drops
/// messages to the page, which only exist once the page said hello anyway.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class WindowsWebWindow : IPlatformWindow
{
    private readonly WindowsPlatform _platform;
    private readonly PlatformWindowOptions _options;
    private readonly IPlatformWindowEvents _events;
    private nint _controller;
    private nint _webview;
    private nint _environment;
    private string? _errorPage;
    private bool _browserGone;
    private bool _destroyed;

    public WindowsWebWindow(WindowsPlatform platform, PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        _platform = platform;
        _options = options;
        _events = events;

        fixed (char* cls = "OrielWindow")
        fixed (char* title = options.Title)
        {
            Hwnd = Win32.CreateWindowExW(
                0, cls, title, Win32.WS_OVERLAPPEDWINDOW,
                Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, options.Width, options.Height,
                0, 0, platform.Instance, 0);
        }
        if (Hwnd == 0)
            throw new COMException("Creating the window failed", Marshal.GetHRForLastWin32Error());

        // Sizes are in device-independent pixels: scale them to the window's display.
        var scale = Scale;
        Win32.SetWindowPos(Hwnd, 0, 0, 0, Scaled(options.Width, scale), Scaled(options.Height, scale), Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        ApplyTheme();
    }

    public nint Hwnd { get; }

    private double Scale => Win32.GetDpiForWindow(Hwnd) / 96.0;

    /// <summary>Shows the window and starts creating its webview.</summary>
    public void Start()
    {
        Win32.ShowWindow(Hwnd, Win32.SW_SHOWNORMAL);
        Tracing.Write($"window {Hwnd:X} shown, creating its webview");
        _platform.WithEnvironment(_options.UserDataFolder, CreateController, Fail);
    }

    public void PostMessage(string message)
    {
        if (_webview != 0)
            WebView2.WebView.PostWebMessageAsString(_webview, message);
    }

    public void SetTitle(string title)
    {
        if (!_destroyed)
            Win32.SetWindowTextW(Hwnd, title);
    }

    public void OpenDevTools()
    {
        if (_webview != 0 && _options.DevTools)
            WebView2.WebView.OpenDevToolsWindow(_webview);
    }

    public void Close()
    {
        if (!_destroyed)
            Win32.PostMessageW(Hwnd, Win32.WM_CLOSE, 0, 0);
    }

    public IReadOnlyList<string>? ShowFileDialog(FileDialogRequest request) => ShellDialogs.Show(Hwnd, request);

    public MessageResult ShowMessage(MessageRequest request) => ShellDialogs.Message(Hwnd, request);

    public void ShowPrintDialog(PrintDialog dialog)
    {
        if (_webview == 0)
            return;
        var w16 = Query(_webview, WebView2.IID_ICoreWebView2_16, "print dialogs");
        try
        {
            WebView2.Printing.ShowPrintUI(w16, dialog == PrintDialog.System ? 1 : 0);
        }
        finally
        {
            Com.Release(w16);
        }
    }

    public void PrintToPdf(string path, PdfOptions? options, Action<bool> done)
    {
        if (_webview == 0)
        {
            done(false);
            return;
        }
        var w7 = Query(_webview, WebView2.IID_ICoreWebView2_7, "printing to PDF");
        nint settings = 0;
        nint handler = 0;
        try
        {
            settings = options is null ? 0 : PrintSettings(options);
            // Invoke(HRESULT, BOOL): the BOOL is 32 bits, whatever is above it in the register.
            handler = ComCallback.ForResult(WebView2.IID_PrintToPdfCompletedHandler, (hr, written) => done(hr >= 0 && (int)written != 0));
            WebView2.Printing.PrintToPdf(w7, path, settings, handler);
        }
        finally
        {
            Com.Release(handler);
            Com.Release(settings);
            Com.Release(w7);
        }
    }

    private nint PrintSettings(PdfOptions options)
    {
        var env6 = Query(_environment, WebView2.IID_ICoreWebView2Environment6, "print settings");
        nint settings;
        try
        {
            settings = WebView2.Environment6.CreatePrintSettings(env6);
        }
        finally
        {
            Com.Release(env6);
        }
        try
        {
            WebView2.PrintSettings.SetOrientation(settings, options.Landscape ? WebView2.PrintLandscape : 0);
            WebView2.PrintSettings.SetScale(settings, options.Scale);
            WebView2.PrintSettings.SetPageSize(settings, options.PageWidth, options.PageHeight);
            WebView2.PrintSettings.SetMargins(settings, options.MarginTop, options.MarginBottom, options.MarginLeft, options.MarginRight);
            WebView2.PrintSettings.SetBackgrounds(settings, options.Backgrounds);
            WebView2.PrintSettings.SetHeaderAndFooter(settings, options.HeaderAndFooter);
            if (options.HeaderTitle is not null)
                WebView2.PrintSettings.SetHeaderTitle(settings, options.HeaderTitle);
            return settings;
        }
        catch
        {
            Com.Release(settings);
            throw;
        }
    }

    /// <summary>The interface <paramref name="iid"/> of <paramref name="obj"/>, which an older runtime lacks.</summary>
    private static nint Query(nint obj, in Guid iid, string what)
    {
        var result = Com.QueryInterface(obj, iid);
        return result != 0 ? result : throw new NotSupportedException($"The WebView2 Runtime is too old for {what}: update it.");
    }

    public void Fail(Exception error) => _events.Failed(error);

    /// <summary>Destroys the window at once, as the app quits.</summary>
    public void Destroy()
    {
        if (!_destroyed)
            Win32.DestroyWindow(Hwnd);
    }

    /// <summary>Handles a window message; false lets Windows handle it.</summary>
    public bool HandleMessage(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case Win32.WM_SIZE:
                if ((int)wParam != Win32.SIZE_MINIMIZED)
                    Resize();
                return true;

            case Win32.WM_SETFOCUS:
                if (_controller != 0)
                    WebView2.Controller.MoveFocus(_controller);
                return true;

            case Win32.WM_DPICHANGED:
                // Windows suggests the size the window has at the new scale.
                var suggested = (Win32.RECT*)lParam;
                Win32.SetWindowPos(Hwnd, 0, suggested->Left, suggested->Top, suggested->Right - suggested->Left, suggested->Bottom - suggested->Top, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                return true;

            case Win32.WM_GETMINMAXINFO:
                var info = (Win32.MINMAXINFO*)lParam;
                var scale = Scale;
                if (_options.MinWidth > 0)
                    info->ptMinTrackSize.X = Scaled(_options.MinWidth, scale);
                if (_options.MinHeight > 0)
                    info->ptMinTrackSize.Y = Scaled(_options.MinHeight, scale);
                return true;

            case Win32.WM_SETTINGCHANGE:
                ApplyTheme();
                return false;

            case Win32.WM_CLOSE:
                Win32.DestroyWindow(Hwnd);
                return true;

            case Win32.WM_DESTROY:
                _destroyed = true;
                ReleaseWebView();
                _platform.Forget(Hwnd);
                _events.Closed();
                return true;
        }
        return false;
    }

    public void Reload()
    {
        if (_webview != 0 && !_browserGone)
            WebView2.WebView.Reload(_webview);
    }

    public void Recreate()
    {
        if (_destroyed)
            return;
        Tracing.Write($"window {Hwnd:X} recreates its webview");
        ReleaseWebView();
        // The environment died with the browser process; the next window to ask creates another.
        _platform.ForgetEnvironment(_environment);
        _environment = 0;
        _browserGone = false;
        _platform.WithEnvironment(_options.UserDataFolder, CreateController, Fail);
    }

    public void ShowErrorPage(string html)
    {
        _errorPage = html;
        if (_webview == 0 || _browserGone)
            Recreate();
        else
            Navigate();
    }

    private void CreateController(nint environment)
    {
        if (_destroyed)
            return;
        _environment = environment;
        var handler = ComCallback.ForResult(WebView2.IID_ControllerCompletedHandler, (hr, controller) =>
        {
            if (hr < 0 || controller == 0)
            {
                Fail(new COMException($"Creating the webview of a window failed with 0x{hr:X8}", hr));
                return;
            }
            if (_destroyed)
                return; // closed while the webview was being created
            Com.AddRef(controller);
            _controller = controller;
            Tracing.Write($"window {Hwnd:X} has its webview");
            SetUpWebView();
        });
        try
        {
            WebView2.Environment.CreateController(environment, Hwnd, handler);
        }
        finally
        {
            Com.Release(handler);
        }
    }

    private void SetUpWebView()
    {
        _webview = WebView2.Controller.GetWebView(_controller);

        var settings = WebView2.WebView.GetSettings(_webview);
        try
        {
            WebView2.Settings.SetStatusBarEnabled(settings, false);
            WebView2.Settings.SetDevToolsEnabled(settings, _options.DevTools);
            WebView2.Settings.SetDefaultContextMenusEnabled(settings, _options.DevTools);
        }
        finally
        {
            Com.Release(settings);
        }

        if (_options.EmbeddedAssets is { } embedded)
            ServeEmbedded(embedded);
        else if (_options.AssetsFolder is not null)
            WebView2.WebView.SetVirtualHostNameToFolderMapping(_webview, Frontend.AppHost, Path.GetFullPath(_options.AssetsFolder));

        Subscribe(WebView2.IID_WebMessageReceivedEventHandler, WebView2.WebView.AddWebMessageReceived, (_, args) =>
        {
            var message = WebView2.WebMessageReceivedEventArgs.TryGetString(args);
            Tracing.Write($"window {Hwnd:X} received {message}");
            if (message is not null)
                _events.MessageReceived(WebView2.WebMessageReceivedEventArgs.GetSource(args), message);
        });
        Subscribe(WebView2.IID_DocumentTitleChangedEventHandler, WebView2.WebView.AddDocumentTitleChanged, (sender, _) =>
            _events.TitleChanged(WebView2.WebView.GetDocumentTitle(sender)));
        Subscribe(WebView2.IID_NavigationStartingEventHandler, WebView2.WebView.AddNavigationStarting, (_, args) =>
        {
            var uri = WebView2.NavigationStartingEventArgs.GetUri(args);
            if (_events.NavigationStarting(uri, WebView2.NavigationStartingEventArgs.IsUserInitiated(args)) != NavigationAction.Allow)
                WebView2.NavigationStartingEventArgs.Cancel(args);
        });
        Subscribe(WebView2.IID_NewWindowRequestedEventHandler, WebView2.WebView.AddNewWindowRequested, (_, args) =>
        {
            // WebView2 never opens a window of its own: Oriel decides.
            WebView2.NewWindowRequestedEventArgs.SetHandled(args);
            _events.NewWindowRequested(WebView2.NewWindowRequestedEventArgs.GetUri(args), WebView2.NewWindowRequestedEventArgs.IsUserInitiated(args));
        });
        Subscribe(WebView2.IID_PermissionRequestedEventHandler, WebView2.WebView.AddPermissionRequested, (_, args) =>
            WebView2.PermissionRequestedEventArgs.SetState(args, _events.PermissionRequested(
                WebView2.PermissionRequestedEventArgs.GetUri(args),
                WebView2.PermissionRequestedEventArgs.GetPermission(args))));
        Subscribe(WebView2.IID_ProcessFailedEventHandler, WebView2.WebView.AddProcessFailed, (_, args) =>
        {
            var failure = WebView2.ProcessFailedEventArgs.GetFailure(args);
            Tracing.Write($"window {Hwnd:X}: webview failed ({failure})");
            if (failure == WebViewFailure.BrowserExited)
                _browserGone = true;
            _events.WebViewFailed(failure);
        });

        Resize();
        WebView2.Controller.SetVisible(_controller, true);

        // The bridge must be in place before the first page starts.
        var added = ComCallback.ForResult(WebView2.IID_AddScriptCompletedHandler, (hr, _) =>
        {
            if (hr < 0)
                Fail(new COMException($"Injecting the page runtime failed with 0x{hr:X8}", hr));
            else if (!_destroyed)
                Navigate();
        });
        try
        {
            WebView2.WebView.AddScriptToExecuteOnDocumentCreated(_webview, _options.BridgeScript, added);
        }
        finally
        {
            Com.Release(added);
        }
    }

    /// <summary>Shows the page, or the error page that replaces a page that cannot be shown.</summary>
    private void Navigate()
    {
        var errorPage = _errorPage ?? _options.ErrorPage;
        if (errorPage is not null)
        {
            Tracing.Write($"window {Hwnd:X} cannot show {_options.Url}: shows an error page");
            WebView2.WebView.NavigateToString(_webview, errorPage);
            return;
        }
        Tracing.Write($"window {Hwnd:X} navigates to {_options.Url}");
        WebView2.WebView.Navigate(_webview, _options.Url.AbsoluteUri);
    }

    /// <summary>
    /// Answers the requests for the app's files with the frontend embedded in
    /// it: the webview asks before going to the network, which has no
    /// app.oriel anyway.
    /// </summary>
    private void ServeEmbedded(EmbeddedFrontend embedded)
    {
        WebView2.WebView.AddWebResourceRequestedFilter(_webview, Frontend.AppOrigin + "/*", WebView2.WebResourceContextAll);
        Subscribe(WebView2.IID_WebResourceRequestedEventHandler, WebView2.WebView.AddWebResourceRequested, (_, args) =>
        {
            var uri = new Uri(WebView2.WebResourceRequestedEventArgs.GetUri(args));
            var file = embedded.Read(uri.AbsolutePath);
            Tracing.Write($"window {Hwnd:X} serves {uri.AbsolutePath} from the executable{(file is null ? ": not found" : "")}");

            nint stream = 0;
            if (file is not null)
            {
                fixed (byte* bytes = file)
                    stream = Win32.SHCreateMemStream(bytes, (uint)file.Length);
                if (stream == 0)
                    throw new OutOfMemoryException($"No memory for {uri.AbsolutePath}");
            }
            try
            {
                // The files change only with the executable: no-cache makes the webview ask again, which costs nothing.
                var headers = file is null
                    ? "Content-Type: text/plain; charset=utf-8"
                    : $"Content-Type: {EmbeddedFrontend.ContentType(uri.AbsolutePath)}\r\nCache-Control: no-cache";
                var response = WebView2.Environment.CreateWebResourceResponse(
                    _environment, stream, file is null ? 404 : 200, file is null ? "Not Found" : "OK", headers);
                try
                {
                    WebView2.WebResourceRequestedEventArgs.SetResponse(args, response);
                }
                finally
                {
                    Com.Release(response);
                }
            }
            finally
            {
                Com.Release(stream);
            }
        });
    }

    private void Subscribe(in Guid iid, Action<nint, nint> add, Action<nint, nint> handle)
    {
        var handler = ComCallback.ForEvent(iid, handle);
        try
        {
            add(_webview, handler);
        }
        finally
        {
            Com.Release(handler);
        }
    }

    private void Resize()
    {
        if (_controller == 0)
            return;
        Win32.RECT bounds;
        Win32.GetClientRect(Hwnd, &bounds);
        WebView2.Controller.SetBounds(_controller, bounds);
    }

    private void ReleaseWebView()
    {
        if (_controller != 0)
            WebView2.Controller.Close(_controller);
        Com.Release(_webview);
        Com.Release(_controller);
        _webview = 0;
        _controller = 0;
    }

    /// <summary>Gives the title bar the dark look when Windows is set to dark apps.</summary>
    private void ApplyTheme()
    {
        int dark = UsesDarkTheme() ? 1 : 0;
        Win32.DwmSetWindowAttribute(Hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
    }

    private static bool UsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private static int Scaled(int dips, double scale) => (int)Math.Round(dips * scale);
}
