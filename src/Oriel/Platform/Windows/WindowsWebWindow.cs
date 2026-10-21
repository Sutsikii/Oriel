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

    private void CreateController(nint environment)
    {
        if (_destroyed)
            return;
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

        if (_options.AssetsFolder is not null)
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

        Resize();
        WebView2.Controller.SetVisible(_controller, true);

        // The bridge must be in place before the first page starts.
        var added = ComCallback.ForResult(WebView2.IID_AddScriptCompletedHandler, (hr, _) =>
        {
            if (hr < 0)
                Fail(new COMException($"Injecting the page runtime failed with 0x{hr:X8}", hr));
            else if (!_destroyed)
            {
                Tracing.Write($"window {Hwnd:X} navigates to {_options.Url}");
                WebView2.WebView.Navigate(_webview, _options.Url.AbsoluteUri);
            }
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
