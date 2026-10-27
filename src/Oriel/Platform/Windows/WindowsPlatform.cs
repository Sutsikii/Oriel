using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Oriel.Platform.Windows;

/// <summary>
/// The Windows backend: a Win32 message loop on the UI thread, and windows
/// whose page is a WebView2 control. WebView2 is reached through COM vtables
/// (see <see cref="WebView2"/>), without WebView2Loader.dll or the WebView2
/// NuGet package.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class WindowsPlatform : IPlatform
{
    private const string WindowClass = "OrielWindow";
    private const uint WM_APP_DISPATCH = Win32.WM_APP + 1;

    // The window procedure is static: it finds windows by handle.
    private static readonly Dictionary<nint, WindowsWebWindow> s_windows = [];
    private static WindowsPlatform? s_running;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly List<Action<nint>> _waitingForEnvironment = [];
    private nint _dispatchWindow;
    private nint _trayWindow;
    private readonly Dictionary<uint, WinTrayIcon> _trayIcons = [];
    private uint _lastTrayId;
    private uint _taskbarCreated;

    /// <summary>The window tray icons send their messages to, once there is one: for tests.</summary>
    internal nint TrayWindow => _trayWindow;
    private nint _environment;
    private Exception? _environmentError;
    private bool _creatingEnvironment;
    private WebView2Runtime.Found _runtime;

    public nint Instance { get; private set; }

    public void Run(Action started)
    {
        // Fail before anything shows when pages cannot.
        _runtime = WebView2Runtime.Find();

        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        var hr = Win32.CoInitializeEx(0, Win32.COINIT_APARTMENTTHREADED);
        if (hr < 0 && hr != Win32.RPC_E_CHANGED_MODE)
            Com.Check(hr, "CoInitializeEx");
        s_running = this;
        ComCallback.Failed += ReportCallbackFailure;
        try
        {
            Instance = Win32.GetModuleHandleW(null);
            RegisterWindowClass();
            fixed (char* cls = WindowClass)
                _dispatchWindow = Win32.CreateWindowExW(0, cls, null, 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, Instance, 0);
            if (_dispatchWindow == 0)
                throw new COMException("Creating the dispatch window failed", Marshal.GetHRForLastWin32Error());

            started();
            Drain();

            Win32.MSG msg;
            while (Win32.GetMessageW(&msg, 0, 0, 0) > 0)
            {
                Win32.TranslateMessage(&msg);
                Win32.DispatchMessageW(&msg);
            }
        }
        finally
        {
            foreach (var window in s_windows.Values.ToList())
                window.Destroy();
            // Left there, an icon stays until the pointer passes over it.
            foreach (var icon in _trayIcons.Values.ToList())
                icon.Remove();
            if (_trayWindow != 0)
                Win32.DestroyWindow(_trayWindow);
            _trayWindow = 0;
            if (_dispatchWindow != 0)
                Win32.DestroyWindow(_dispatchWindow);
            Com.Release(_environment);
            _environment = 0;
            ComCallback.Failed -= ReportCallbackFailure;
            s_running = null;
            if (hr >= 0)
                Win32.CoUninitialize();
        }
    }

    public void Post(Action action)
    {
        _queue.Enqueue(action);
        var target = _dispatchWindow;
        if (target != 0)
            Win32.PostMessageW(target, WM_APP_DISPATCH, 0, 0);
        // Before the loop runs, Run drains the queue once it starts.
    }

    public void Quit() => Win32.PostQuitMessage(0);

    public bool SupportsTrayIcons => true;

    public IPlatformTrayIcon AddTrayIcon(TrayIconSpec spec, IPlatformTrayIconEvents events)
    {
        if (_trayWindow == 0)
        {
            // Top-level and never shown: a message-only window cannot come
            // to the foreground, which the icon's menu needs.
            fixed (char* cls = WindowClass)
                _trayWindow = Win32.CreateWindowExW(0, cls, null, Win32.WS_POPUP, 0, 0, 0, 0, 0, 0, Instance, 0);
            if (_trayWindow == 0)
                throw new COMException("Creating the window of tray icons failed", Marshal.GetHRForLastWin32Error());
            fixed (char* name = "TaskbarCreated")
                _taskbarCreated = Win32.RegisterWindowMessageW(name);
        }
        var id = ++_lastTrayId;
        var icon = new WinTrayIcon(_trayWindow, Instance, id, spec, events, removed => _trayIcons.Remove(removed));
        _trayIcons[id] = icon;
        return icon;
    }

    /// <summary>A message of the window of tray icons: from an icon, or Explorer restarting, which forgot them.</summary>
    private bool HandleTrayMessage(uint msg, nint lParam)
    {
        if (msg == WinTrayIcon.WM_APP_TRAY)
        {
            if (_trayIcons.TryGetValue((uint)((lParam >> 16) & 0xFFFF), out var icon))
                icon.Handle((uint)(lParam & 0xFFFF));
            return true;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            foreach (var each in _trayIcons.Values)
                each.Add();
            return true;
        }
        return false;
    }

    public string? GetClipboardText() => WinClipboard.GetText(_dispatchWindow);

    public string? GetClipboardHtml() => WinClipboard.GetHtml(_dispatchWindow);

    public byte[]? GetClipboardImage() => WinClipboard.GetImage(_dispatchWindow);

    public IReadOnlyList<string>? GetClipboardFiles() => WinClipboard.GetFiles(_dispatchWindow);

    public void SetClipboard(ClipboardContent content) => WinClipboard.Set(_dispatchWindow, content);

    public void OpenExternally(Uri uri)
    {
        // ShellExecute returns a value above 32 when it succeeds.
        if (Win32.ShellExecuteW(0, "open", uri.AbsoluteUri, null, null, Win32.SW_SHOWNORMAL) <= 32)
            throw new InvalidOperationException($"Opening {uri} with the system's default app failed.");
    }

    public IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        var window = new WindowsWebWindow(this, options, events);
        s_windows[window.Hwnd] = window;
        window.Start();
        return window;
    }

    internal void Forget(nint hwnd) => s_windows.Remove(hwnd);

    /// <summary>
    /// Calls <paramref name="ready"/> with the WebView2 environment, which all
    /// windows share, creating it on first use. On failure, <paramref name="failed"/>.
    /// </summary>
    internal void WithEnvironment(string userDataFolder, Action<nint> ready, Action<Exception> failed)
    {
        if (_environment != 0)
        {
            ready(_environment);
            return;
        }
        if (_environmentError is not null)
        {
            failed(_environmentError);
            return;
        }
        _waitingForEnvironment.Add(env =>
        {
            if (env == 0)
                failed(_environmentError!);
            else
                ready(env);
        });
        if (_creatingEnvironment)
            return;
        _creatingEnvironment = true;

        var library = WebView2Runtime.ClientLibraryPath(_runtime.Directory, File.Exists, WebView2Runtime.Architecture())!;
        var handler = ComCallback.ForResult(WebView2.IID_EnvironmentCompletedHandler, (result, env) =>
        {
            if (result < 0 || env == 0)
                _environmentError = new WebViewUnavailableException($"Creating the WebView2 environment failed with 0x{result:X8}");
            else
            {
                Com.AddRef(env); // borrowed for the call: keep it
                _environment = env;
            }
            Tracing.Write($"WebView2 environment: 0x{result:X8}");
            var waiting = _waitingForEnvironment.ToList();
            _waitingForEnvironment.Clear();
            foreach (var w in waiting)
                w(_environment);
        });
        try
        {
            Tracing.Write($"creating the WebView2 environment of {library}, data in {userDataFolder}");
            Directory.CreateDirectory(userDataFolder);
            WebView2.CreateEnvironment(library, _runtime.IsFixed, userDataFolder, handler);
        }
        catch (Exception e)
        {
            _environmentError = e;
            _waitingForEnvironment.Clear();
            failed(e);
        }
        finally
        {
            Com.Release(handler);
        }
    }

    /// <summary>
    /// Drops <paramref name="environment"/>, whose browser process exited, so
    /// that the next window asking for one creates it anew. Windows that
    /// recover one after the other drop only the one they used.
    /// </summary>
    internal void ForgetEnvironment(nint environment)
    {
        if (environment == 0 || environment != _environment)
            return;
        Com.Release(_environment);
        _environment = 0;
        _environmentError = null;
        _creatingEnvironment = false;
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var action))
            action();
    }

    private void RegisterWindowClass()
    {
        fixed (char* cls = WindowClass)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc = &WindowProcedure,
                hInstance = Instance,
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                hIcon = AppIcon(),
                lpszClassName = cls,
            };
            // A second app in the process finds the class registered already.
            Win32.RegisterClassExW(&wc);
        }
    }

    /// <summary>The icon of the executable (its ApplicationIcon), or Windows' default one.</summary>
    private nint AppIcon()
    {
        var icon = Win32.LoadIconW(Instance, Win32.IDI_APPLICATION);
        return icon != 0 ? icon : Win32.LoadIconW(0, Win32.IDI_APPLICATION);
    }

    private static void ReportCallbackFailure(Exception e)
    {
        foreach (var window in s_windows.Values)
        {
            window.Fail(e);
            return;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            var platform = s_running;
            if (msg == WM_APP_DISPATCH && platform is not null && hwnd == platform._dispatchWindow)
            {
                platform.Drain();
                return 0;
            }
            if (platform is not null && hwnd == platform._trayWindow && hwnd != 0 && platform.HandleTrayMessage(msg, lParam))
                return 0;
            if (s_windows.TryGetValue(hwnd, out var window) && window.HandleMessage(msg, wParam, lParam, out var result))
                return result;
        }
        catch (Exception e)
        {
            // Nothing may unwind into Win32.
            if (s_windows.TryGetValue(hwnd, out var window))
                window.Fail(e);
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
