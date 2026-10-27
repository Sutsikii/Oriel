using System.Globalization;
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
    private bool _fullScreen;
    private bool _leavingFullScreen;
    private Win32.WINDOWPLACEMENT _beforeFullScreen;
    private nint _styleBeforeFullScreen;

    public WindowsWebWindow(WindowsPlatform platform, PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        _platform = platform;
        _options = options;
        _events = events;
        // A frameless window has no frame to show a bar in.
        _bar = options.Menu is null || options.Frameless ? null : new NativeMenu(options.Menu, bar: true);

        fixed (char* cls = "OrielWindow")
        fixed (char* title = options.Title)
        {
            Hwnd = Win32.CreateWindowExW(
                options.AlwaysOnTop ? Win32.WS_EX_TOPMOST : 0, cls, title, Win32.WS_OVERLAPPEDWINDOW,
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
        Win32.RECT where;
        Win32.GetWindowRect(Hwnd, &where);
        var x = options.X is { } dx ? Scaled(dx, scale) : where.Left;
        var y = options.Y is { } dy ? Scaled(dy, scale) : where.Top;
        Win32.SetWindowPos(Hwnd, 0, x, y, Scaled(options.Width, scale), Scaled(options.Height, scale), Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        if (options.Center)
            Center();
        ApplyTheme();
    }

    public nint Hwnd { get; }

    private double Scale => Win32.GetDpiForWindow(Hwnd) / 96.0;

    /// <summary>Shows the window and starts creating its webview.</summary>
    public void Start()
    {
        var show = _options.State switch
        {
            WindowState.Maximized => Win32.SW_SHOWMAXIMIZED,
            WindowState.Minimized => Win32.SW_SHOWMINIMIZED,
            _ => Win32.SW_SHOWNORMAL,
        };
        // A frameless window computes its frame anew (WM_NCCALCSIZE), now
        // that the window procedure knows it.
        if (_options.Frameless)
            Win32.SetWindowPos(Hwnd, 0, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
        if (_options.Placement is { } saved && RestorePlacement(saved) is { } maximized)
            show = maximized ? Win32.SW_SHOWMAXIMIZED : Win32.SW_SHOWNORMAL;
        Win32.ShowWindow(Hwnd, show);
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
        if (_destroyed || _options.Frameless)
            return;
        var old = _bar;
        _bar = menu is null ? null : new NativeMenu(menu, bar: true);
        if (!_fullScreen)
            Win32.SetMenu(Hwnd, _bar?.Handle ?? 0);
        old?.Dispose(); // a window destroys only the menu it has
        Resize();
    }

    public void SetBounds(int? x, int? y, int? width, int? height)
    {
        if (_destroyed)
            return;
        if (Win32.IsIconic(Hwnd) || Win32.IsZoomed(Hwnd))
            Win32.ShowWindow(Hwnd, Win32.SW_RESTORE);
        var scale = Scale;
        Win32.RECT r;
        Win32.GetWindowRect(Hwnd, &r);
        Win32.SetWindowPos(Hwnd, 0,
            x is { } nx ? Scaled(nx, scale) : r.Left,
            y is { } ny ? Scaled(ny, scale) : r.Top,
            width is { } w ? Scaled(w, scale) : r.Right - r.Left,
            height is { } h ? Scaled(h, scale) : r.Bottom - r.Top,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    public void Center()
    {
        if (_destroyed)
            return;
        var info = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        if (!Win32.GetMonitorInfoW(Win32.MonitorFromWindow(Hwnd, Win32.MONITOR_DEFAULTTONEAREST), &info))
            return;
        Win32.RECT r;
        Win32.GetWindowRect(Hwnd, &r);
        var work = info.rcWork;
        var x = work.Left + (work.Right - work.Left - (r.Right - r.Left)) / 2;
        var y = work.Top + (work.Bottom - work.Top - (r.Bottom - r.Top)) / 2;
        Win32.SetWindowPos(Hwnd, 0, x, Math.Max(work.Top, y), 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    public void SetState(WindowState state)
    {
        if (!_destroyed)
        {
            Win32.ShowWindow(Hwnd, state switch
            {
                WindowState.Minimized => Win32.SW_MINIMIZE,
                WindowState.Maximized => Win32.SW_SHOWMAXIMIZED,
                _ => Win32.SW_RESTORE,
            });
        }
    }

    /// <summary>Takes the frame and the bar off, and fills the monitor; or puts them back, where the window was.</summary>
    public void SetFullScreen(bool fullScreen)
    {
        if (_destroyed || fullScreen == _fullScreen)
            return;
        if (fullScreen)
        {
            var info = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
            if (!Win32.GetMonitorInfoW(Win32.MonitorFromWindow(Hwnd, Win32.MONITOR_DEFAULTTONEAREST), &info))
                return;
            _beforeFullScreen = Placement(out _);
            _styleBeforeFullScreen = Win32.GetWindowLongPtrW(Hwnd, Win32.GWL_STYLE);
            _fullScreen = true;
            Win32.SetMenu(Hwnd, 0);
            Win32.SetWindowLongPtrW(Hwnd, Win32.GWL_STYLE, _styleBeforeFullScreen & ~(nint)Win32.WS_OVERLAPPEDWINDOW);
            var m = info.rcMonitor;
            Win32.SetWindowPos(Hwnd, 0, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, Win32.SWP_NOOWNERZORDER | Win32.SWP_FRAMECHANGED);
        }
        else
        {
            // The window passes through sizes of no interest on its way back: it tells only where it ends.
            _leavingFullScreen = true;
            Win32.SetWindowLongPtrW(Hwnd, Win32.GWL_STYLE, _styleBeforeFullScreen);
            Win32.SetMenu(Hwnd, _bar?.Handle ?? 0);
            var before = _beforeFullScreen;
            Win32.SetWindowPlacement(Hwnd, &before);
            _fullScreen = false;
            Win32.SetWindowPos(Hwnd, 0, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOOWNERZORDER | Win32.SWP_FRAMECHANGED);
            _leavingFullScreen = false;
            ReportBounds();
        }
    }

    public void SetAlwaysOnTop(bool onTop)
    {
        if (!_destroyed)
            Win32.SetWindowPos(Hwnd, onTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public void Focus()
    {
        if (_destroyed)
            return;
        if (Win32.IsIconic(Hwnd))
            Win32.ShowWindow(Hwnd, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(Hwnd); // refused when another app has the keyboard: Windows flashes it instead
    }

    /// <summary>
    /// <c>left,top,right,bottom,maximized</c>: the window's normal rectangle,
    /// in the workspace pixels of <c>GetWindowPlacement</c>, and 1 when it is
    /// maximized, or would be once restored.
    /// </summary>
    public string Placement()
    {
        var placement = _fullScreen ? _beforeFullScreen : Placement(out _);
        var r = placement.rcNormalPosition;
        var maximized = placement.showCmd == Win32.SW_SHOWMAXIMIZED
            || (placement.showCmd == Win32.SW_SHOWMINIMIZED && (placement.flags & Win32.WPF_RESTORETOMAXIMIZED) != 0);
        return string.Create(CultureInfo.InvariantCulture, $"{r.Left},{r.Top},{r.Right},{r.Bottom},{(maximized ? 1 : 0)}");
    }

    private Win32.WINDOWPLACEMENT Placement(out bool ok)
    {
        var placement = new Win32.WINDOWPLACEMENT { length = (uint)sizeof(Win32.WINDOWPLACEMENT) };
        ok = Win32.GetWindowPlacement(Hwnd, &placement);
        return placement;
    }

    /// <summary>
    /// Puts the window where <paramref name="saved"/> says, while it is still
    /// hidden; returns whether it was maximized, or null when the placement is
    /// not one, or its monitor is gone.
    /// </summary>
    private bool? RestorePlacement(string saved)
    {
        var parts = saved.Split(',');
        if (parts.Length != 5)
            return null;
        var n = new int[5];
        for (var i = 0; i < 5; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n[i]))
                return null;
        }
        var rect = new Win32.RECT { Left = n[0], Top = n[1], Right = n[2], Bottom = n[3] };
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top || Win32.MonitorFromRect(&rect, Win32.MONITOR_DEFAULTTONULL) == 0)
            return null;
        var placement = new Win32.WINDOWPLACEMENT
        {
            length = (uint)sizeof(Win32.WINDOWPLACEMENT),
            showCmd = Win32.SW_HIDE,
            rcNormalPosition = rect,
        };
        // Twice: moving to a monitor of another scale resizes the window
        // (WM_DPICHANGED); the second time, it is at that scale already.
        Win32.SetWindowPlacement(Hwnd, &placement);
        Win32.SetWindowPlacement(Hwnd, &placement);
        Tracing.Write($"window {Hwnd:X} restored to {saved}");
        return n[4] == 1;
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

    /// <summary>
    /// Calls <paramref name="method"/> of the DevTools protocol on the page,
    /// for the tests: <paramref name="done"/> gets the HRESULT and the result's JSON.
    /// </summary>
    internal void CallDevTools(string method, string parameters, Action<int, string?> done)
    {
        if (_webview == 0)
        {
            done(-1, null);
            return;
        }
        var handler = ComCallback.ForResult(WebView2.IID_CallDevToolsProtocolMethodCompletedHandler,
            (hr, json) => done(hr, Marshal.PtrToStringUni(json)));
        try
        {
            WebView2.WebView.CallDevToolsProtocolMethod(_webview, method, parameters, handler);
        }
        finally
        {
            Com.Release(handler);
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
                if ((int)wParam is Win32.SIZE_MINIMIZED or Win32.SIZE_MAXIMIZED or 0)
                {
                    _events.StateChanged((int)wParam switch
                    {
                        Win32.SIZE_MINIMIZED => WindowState.Minimized,
                        Win32.SIZE_MAXIMIZED => WindowState.Maximized,
                        _ => WindowState.Normal,
                    });
                }
                ReportBounds();
                return true;

            case Win32.WM_MOVE:
                ReportBounds();
                return true;

            case Win32.WM_ACTIVATE:
                _events.FocusChanged(Win32.LoWord((nint)wParam) != Win32.WA_INACTIVE);
                return false; // Windows gives the focus back, then WM_SETFOCUS hands it to the webview

            case Win32.WM_NCCALCSIZE when _options.Frameless && wParam != 0 && !_fullScreen:
                // No caption: the client area goes up to the window's top. The
                // sides and bottom keep their frame, which is invisible and resizes.
                var calc = (Win32.NCCALCSIZE_PARAMS*)lParam;
                var dpi = Win32.GetDpiForWindow(Hwnd);
                var padding = Win32.GetSystemMetricsForDpi(Win32.SM_CXPADDEDBORDER, dpi);
                var frameX = Win32.GetSystemMetricsForDpi(Win32.SM_CXSIZEFRAME, dpi) + padding;
                var frameY = Win32.GetSystemMetricsForDpi(Win32.SM_CYSIZEFRAME, dpi) + padding;
                calc->rgrc0.Left += frameX;
                calc->rgrc0.Right -= frameX;
                calc->rgrc0.Bottom -= frameY;
                if (Win32.IsZoomed(Hwnd))
                    calc->rgrc0.Top += frameY; // a maximized window's frame is off the monitor, the top's too
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
            WebView2.Settings.EnableNonClientRegions(settings);
        }
        finally
        {
            Com.Release(settings);
        }

        if (_options.EmbeddedAssets is null && _options.AssetsFolder is not null)
            WebView2.WebView.SetVirtualHostNameToFolderMapping(_webview, Frontend.AppHost, Path.GetFullPath(_options.AssetsFolder));
        Serve(_options.EmbeddedAssets, _options.Streams);

        Subscribe(WebView2.IID_WebMessageReceivedEventHandler, WebView2.WebView.AddWebMessageReceived, (_, args) =>
        {
            var message = WebView2.WebMessageReceivedEventArgs.TryGetString(args);
            Tracing.Write($"window {Hwnd:X} received {message}");
            if (message is null)
                return;
            // Only a drop carries files; other messages skip the lookup.
            var files = message.StartsWith("{\"t\":\"drop\"", StringComparison.Ordinal)
                ? WebView2.WebMessageReceivedEventArgs.GetFilePaths(args)
                : null;
            _events.MessageReceived(WebView2.WebMessageReceivedEventArgs.GetSource(args), message, files);
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
    /// Answers the requests for the app's shared streams, at streams.oriel,
    /// and for its files when the frontend is embedded in it: the webview
    /// asks before going to the network, which has neither host anyway.
    /// Requests for a folder's files never come: its mapping serves them.
    /// </summary>
    private void Serve(EmbeddedFrontend? embedded, StreamServer? streams)
    {
        if (embedded is null && streams is null)
            return;
        if (embedded is not null)
            WebView2.WebView.AddWebResourceRequestedFilter(_webview, Frontend.AppOrigin + "/*", WebView2.WebResourceContextAll);
        if (streams is not null)
            WebView2.WebView.AddWebResourceRequestedFilter(_webview, "https://" + StreamServer.Host + "/*", WebView2.WebResourceContextAll);
        Subscribe(WebView2.IID_WebResourceRequestedEventHandler, WebView2.WebView.AddWebResourceRequested, (_, args) =>
        {
            var uri = new Uri(WebView2.WebResourceRequestedEventArgs.GetUri(args));
            if (streams is not null && StreamServer.TryGetId(uri, out var id))
            {
                ServeStream(args, streams.Serve(id, WebView2.WebResourceRequestedEventArgs.GetHeader(args, "Range"), WebView2.WebResourceRequestedEventArgs.GetMethod(args)));
                return;
            }
            if (embedded is null)
                return;
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

    /// <summary>Answers with a shared stream, which WebView2 reads as the page takes it.</summary>
    private void ServeStream(nint args, StreamResponse answer)
    {
        Tracing.Write($"window {Hwnd:X} serves a shared stream: {answer.Status}");
        var stream = answer.Body is null ? 0 : ComStream.Create(answer.Body, answer.Length);
        try
        {
            var response = WebView2.Environment.CreateWebResourceResponse(_environment, stream, answer.Status, answer.Reason, answer.HeaderLines);
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

    /// <summary>Tells where the window is, when it is neither minimized, maximized nor full screen.</summary>
    private void ReportBounds()
    {
        if (_destroyed || _fullScreen || _leavingFullScreen || Win32.IsIconic(Hwnd) || Win32.IsZoomed(Hwnd))
            return;
        Win32.RECT r;
        Win32.GetWindowRect(Hwnd, &r);
        var scale = Scale;
        _events.BoundsChanged(new WindowBounds(Dips(r.Left, scale), Dips(r.Top, scale), Dips(r.Right - r.Left, scale), Dips(r.Bottom - r.Top, scale)));
    }

    private static int Dips(int pixels, double scale) => (int)Math.Round(pixels / scale);

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
