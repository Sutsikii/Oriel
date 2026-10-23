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
    private bool _dark;
    private NativeMenu? _bar;
    private NativeMenu? _contextMenu;
    private nint _menuTheme;

    public WindowsWebWindow(WindowsPlatform platform, PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        _platform = platform;
        _options = options;
        _events = events;
        _bar = options.Menu is null ? null : new NativeMenu(options.Menu, bar: true);

        fixed (char* cls = "OrielWindow")
        fixed (char* title = options.Title)
        {
            Hwnd = Win32.CreateWindowExW(
                0, cls, title, Win32.WS_OVERLAPPEDWINDOW,
                Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, options.Width, options.Height,
                0, _bar?.Handle ?? 0, platform.Instance, 0);
        }
        if (Hwnd == 0)
        {
            var error = Marshal.GetHRForLastWin32Error();
            _bar?.Dispose();
            throw new COMException("Creating the window failed", error);
        }

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

    public void SetMenu(Menu? menu)
    {
        if (_destroyed)
            return;
        var old = _bar;
        _bar = menu is null ? null : new NativeMenu(menu, bar: true);
        Win32.SetMenu(Hwnd, _bar?.Handle ?? 0);
        old?.Dispose(); // a window destroys only the menu it has
        Resize();
    }

    public MenuItem? ShowContextMenu(Menu menu, PagePoint? at)
    {
        if (_destroyed)
            return null;
        Win32.POINT point;
        if (at is { } p)
        {
            // The webview fills the client area: CSS pixels are scaled, from its corner.
            var scale = Scale;
            point = new Win32.POINT { X = (int)Math.Round(p.X * scale), Y = (int)Math.Round(p.Y * scale) };
            Win32.ClientToScreen(Hwnd, &point);
        }
        else
        {
            Win32.GetCursorPos(&point);
        }
        using var native = new NativeMenu(menu, bar: false);
        _contextMenu = native;
        try
        {
            var id = Win32.TrackPopupMenuEx(native.Handle, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON, point.X, point.Y, Hwnd, 0);
            return native.Command(id);
        }
        finally
        {
            _contextMenu = null;
        }
    }

    public void ActivateMenuBar(char mnemonic)
    {
        // Posted: the menu's loop runs once this key is done with.
        if (_bar is not null && !_destroyed)
            Win32.PostMessageW(Hwnd, Win32.WM_SYSCOMMAND, Win32.SC_KEYMENU, mnemonic);
    }

    public void RunEditCommand(string command)
    {
        if (_webview == 0)
            return;
        // A key event carrying an editing command runs it as the keyboard
        // would, in the focused element: pages cannot paste by themselves.
        var handler = ComCallback.ForResult(WebView2.IID_CallDevToolsProtocolMethodCompletedHandler, (hr, _) =>
        {
            if (hr < 0)
                Tracing.Write($"window {Hwnd:X}: the editing command {command} failed with 0x{hr:X8}");
        });
        try
        {
            WebView2.WebView.CallDevToolsProtocolMethod(_webview, "Input.dispatchKeyEvent", $$"""{"type":"keyDown","commands":["{{command}}"]}""", handler);
        }
        finally
        {
            Com.Release(handler);
        }
    }

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

            case Win32.WM_COMMAND:
                // From the bar: accelerators come from the page, not as WM_COMMAND.
                if (Win32.HiWord((nint)wParam) == 0 && lParam == 0 && _bar?.Command(Win32.LoWord((nint)wParam)) is { } item)
                {
                    _events.MenuItemClicked(item);
                    return true;
                }
                return false;

            case Win32.WM_INITMENUPOPUP:
                return _contextMenu?.Refresh((nint)wParam) == true || _bar?.Refresh((nint)wParam) == true;

            case Win32.WM_UAHDRAWMENU when _dark && _bar is not null:
                DarkMenus.PaintBar(Hwnd, (Win32.UAHMENU*)lParam);
                return true;

            case Win32.WM_UAHDRAWMENUITEM when _dark && _bar is not null:
                if (_menuTheme == 0)
                    _menuTheme = Win32.OpenThemeData(Hwnd, "Menu");
                DarkMenus.PaintItem(_menuTheme, (Win32.UAHDRAWMENUITEM*)lParam);
                return true;

            case Win32.WM_NCPAINT or Win32.WM_NCACTIVATE when _dark && _bar is not null:
                result = Win32.DefWindowProcW(Hwnd, msg, wParam, lParam);
                DarkMenus.PaintLineUnderBar(Hwnd);
                return true;

            case Win32.WM_CLOSE:
                Win32.DestroyWindow(Hwnd);
                return true;

            case Win32.WM_DESTROY:
                _destroyed = true;
                _bar = null; // destroyed with the window
                if (_menuTheme != 0)
                    Win32.CloseThemeData(_menuTheme);
                _menuTheme = 0;
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

        // The page gets the keys first: an accelerator of the bar only keeps
        // the webview from using it, as it would Ctrl+P or F5, and runs once
        // the page left it alone (bridge.js says so).
        var keys = ComCallback.ForEvent(WebView2.IID_AcceleratorKeyPressedEventHandler, (_, args) =>
        {
            if (WebView2.AcceleratorKeyPressedEventArgs.GetKind(args) is not (WebView2.KeyDown or WebView2.SystemKeyDown))
                return;
            var chord = new KeyChord(WebView2.AcceleratorKeyPressedEventArgs.GetVirtualKey(args), Modifiers());
            if (_events.IsAccelerator(chord))
                WebView2.AcceleratorKeyPressedEventArgs.DisableBrowserAccelerator(args);
        });
        try
        {
            WebView2.Controller.AddAcceleratorKeyPressed(_controller, keys);
        }
        finally
        {
            Com.Release(keys);
        }

        Resize();
        WebView2.Controller.SetVisible(_controller, true);
        // The window took the focus as it showed, before there was a webview to give it to.
        if (Win32.GetFocus() == Hwnd)
            WebView2.Controller.MoveFocus(_controller);

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

    private static KeyModifiers Modifiers()
    {
        var modifiers = KeyModifiers.None;
        if (Win32.GetKeyState(Win32.VK_CONTROL) < 0)
            modifiers |= KeyModifiers.Ctrl;
        if (Win32.GetKeyState(Win32.VK_SHIFT) < 0)
            modifiers |= KeyModifiers.Shift;
        if (Win32.GetKeyState(Win32.VK_MENU) < 0)
            modifiers |= KeyModifiers.Alt;
        return modifiers;
    }

    /// <summary>Gives the title bar and the menus the dark look when Windows is set to dark apps.</summary>
    private void ApplyTheme()
    {
        _dark = UsesDarkTheme();
        int dark = _dark ? 1 : 0;
        Win32.DwmSetWindowAttribute(Hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        DarkMenus.Apply(Hwnd, _dark);
        if (_bar is not null)
            Win32.DrawMenuBar(Hwnd);
    }

    private static bool UsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private static int Scaled(int dips, double scale) => (int)Math.Round(dips * scale);
}
