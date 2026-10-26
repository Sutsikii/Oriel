using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Oriel.Platform.Linux;

/// <summary>
/// A GTK window filled by a WebKitGTK view. Pages post to the script
/// message handler <c>oriel</c>; messages reach them through
/// <c>window.__orielReceive</c>, as bridge.js expects of WebKit.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxWebWindow : IPlatformWindow
{
    private readonly PlatformWindowOptions _options;
    private readonly IPlatformWindowEvents _events;
    private GCHandle _self;
    private nint _window;
    private nint _webview;
    private nint _content;
    private bool _destroyed;
    private WindowBounds _normal;
    private WindowState _state;
    private bool _fullScreen;
    private bool _focused;

    public LinuxWebWindow(PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        _options = options;
        _events = events;
    }

    public void Start()
    {
        _self = GCHandle.Alloc(this);
        var self = GCHandle.ToIntPtr(_self);
        LinuxPlatform.Windows.Add(this);

        _window = Gtk.gtk_window_new(Gtk.GTK_WINDOW_TOPLEVEL);
        Gtk.gtk_window_set_title(_window, _options.Title);
        if (!Restore(_options.Placement))
        {
            Gtk.gtk_window_set_default_size(_window, _options.Width, _options.Height);
            if (_options.X is { } x && _options.Y is { } y)
                Gtk.gtk_window_move(_window, x, y);
            else if (_options.Center)
                Gtk.gtk_window_set_position(_window, Gtk.GTK_WIN_POS_CENTER);
        }
        if (_options.MinWidth > 0 || _options.MinHeight > 0)
            Gtk.gtk_widget_set_size_request(_window, Math.Max(_options.MinWidth, 1), Math.Max(_options.MinHeight, 1));
        if (_options.Frameless)
            Gtk.gtk_window_set_decorated(_window, 0);
        if (_options.AlwaysOnTop)
            Gtk.gtk_window_set_keep_above(_window, 1);

        _content = Gtk.webkit_user_content_manager_new();
        Gtk.webkit_user_content_manager_register_script_message_handler(_content, "oriel");
        var script = Gtk.webkit_user_script_new(_options.BridgeScript, Gtk.WEBKIT_USER_CONTENT_INJECT_TOP_FRAME, Gtk.WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START, 0, 0);
        Gtk.webkit_user_content_manager_add_script(_content, script);
        Gtk.webkit_user_script_unref(script);

        _webview = Gtk.webkit_web_view_new_with_user_content_manager(_content);
        var settings = Gtk.webkit_web_view_get_settings(_webview);
        Gtk.webkit_settings_set_enable_developer_extras(settings, _options.DevTools ? 1 : 0);
        // So that window.open reaches the create signal, and Oriel, which decides: WebKit opens nothing itself.
        Gtk.webkit_settings_set_javascript_can_open_windows_automatically(settings, 1);
        Gtk.gtk_container_add(_window, _webview);

        Connect(_window, "destroy", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnDestroy, self);
        Connect(_window, "configure-event", (nint)(delegate* unmanaged[Cdecl]<nint, Gtk.GdkEventConfigure*, nint, int>)&OnConfigure, self);
        Connect(_window, "window-state-event", (nint)(delegate* unmanaged[Cdecl]<nint, Gtk.GdkEventWindowState*, nint, int>)&OnWindowState, self);
        Connect(_content, "script-message-received::oriel", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnScriptMessage, self);
        Connect(_webview, "notify::title", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnTitle, self);
        Connect(_webview, "decide-policy", (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, int>)&OnDecidePolicy, self);
        Connect(_webview, "create", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&OnCreate, self);
        Connect(_webview, "permission-request", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnPermission, self);
        Connect(_webview, "web-process-terminated", (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, void>)&OnWebProcessTerminated, self);

        Gtk.gtk_widget_show_all(_window);
        switch (_options.State)
        {
            case WindowState.Maximized:
                Gtk.gtk_window_maximize(_window);
                break;
            case WindowState.Minimized:
                Gtk.gtk_window_iconify(_window);
                break;
        }
        Gtk.gtk_widget_grab_focus(_webview);
        Navigate();
    }

    private static void Connect(nint instance, string signal, nint handler, nint self) =>
        Gtk.g_signal_connect_data(instance, signal, handler, self, 0, 0);

    private static LinuxWebWindow? From(nint self) =>
        self == 0 ? null : GCHandle.FromIntPtr(self).Target as LinuxWebWindow;

    private void Navigate()
    {
        if (_options.ErrorPage is { } errorPage)
        {
            Tracing.Write($"window cannot show {_options.Url}: shows an error page");
            Gtk.webkit_web_view_load_html(_webview, errorPage, null);
            return;
        }
        var url = AppScheme.ToPage(_options.Url);
        Tracing.Write($"window navigates to {url}");
        Gtk.webkit_web_view_load_uri(_webview, url);
    }

    // ---- Signals, on the UI thread ----

    /// <summary>Runs a handler of a signal; an exception must not unwind into GTK.</summary>
    private static void Guard(nint self, Action<LinuxWebWindow> handle)
    {
        // After its destroy signal, the view's last signals find nothing to do.
        if (From(self) is not { } window || window._destroyed)
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDestroy(nint widget, nint self) => Guard(self, w =>
    {
        w._destroyed = true;
        LinuxPlatform.Windows.Remove(w);
        try
        {
            w._events.Closed();
        }
        finally
        {
            // The view, destroyed after the window, still signals with this handle: free it once that is over.
            Gtk.g_idle_add(&FreeHandle, GCHandle.ToIntPtr(w._self));
        }
    });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FreeHandle(nint self)
    {
        GCHandle.FromIntPtr(self).Free();
        return 0; // G_SOURCE_REMOVE
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnConfigure(nint widget, Gtk.GdkEventConfigure* e, nint self)
    {
        var bounds = new WindowBounds(e->X, e->Y, e->Width, e->Height);
        Guard(self, w =>
        {
            if (w._state == WindowState.Normal && !w._fullScreen)
                w._normal = bounds;
            w._events.BoundsChanged(bounds);
        });
        return 0; // GTK goes on
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnWindowState(nint widget, Gtk.GdkEventWindowState* e, nint self)
    {
        var flags = e->NewWindowState;
        Guard(self, w =>
        {
            w._fullScreen = (flags & Gtk.GDK_WINDOW_STATE_FULLSCREEN) != 0;
            var state = (flags & Gtk.GDK_WINDOW_STATE_ICONIFIED) != 0 ? WindowState.Minimized
                : (flags & Gtk.GDK_WINDOW_STATE_MAXIMIZED) != 0 ? WindowState.Maximized
                : WindowState.Normal;
            if (state != w._state)
            {
                w._state = state;
                w._events.StateChanged(state);
            }
            var focused = (flags & Gtk.GDK_WINDOW_STATE_FOCUSED) != 0;
            if (focused != w._focused)
            {
                w._focused = focused;
                w._events.FocusChanged(focused);
            }
        });
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnScriptMessage(nint manager, nint result, nint self) => Guard(self, w =>
    {
        var message = Gtk.TakeText(Gtk.jsc_value_to_string(Gtk.webkit_javascript_result_get_js_value(result)));
        // The handler is the top frame's only, where the bridge runs: its document is the view's.
        var source = AppScheme.FromPage(Gtk.Text(Gtk.webkit_web_view_get_uri(w._webview)) ?? "");
        Tracing.Write($"window received {message}");
        if (message is not null)
            w._events.MessageReceived(source, message);
    });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTitle(nint webview, nint spec, nint self) => Guard(self, w =>
        w._events.TitleChanged(Gtk.Text(Gtk.webkit_web_view_get_title(webview)) ?? ""));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnDecidePolicy(nint webview, nint decision, int type, nint self)
    {
        if (type is not (Gtk.WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION or Gtk.WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION))
            return 0; // responses: WebKit decides
        var handled = 0;
        Guard(self, w =>
        {
            var action = Gtk.webkit_navigation_policy_decision_get_navigation_action(decision);
            var uri = AppScheme.FromPage(Gtk.Text(Gtk.webkit_uri_request_get_uri(Gtk.webkit_navigation_action_get_request(action))) ?? "");
            var user = Gtk.webkit_navigation_action_is_user_gesture(action) != 0;
            if (type == Gtk.WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION)
            {
                Gtk.webkit_policy_decision_ignore(decision);
                handled = 1;
                w._events.NewWindowRequested(uri, user);
                return;
            }
            // An error page loaded as HTML has no URL of its own, and is the app's.
            if (uri.Length == 0 || uri == "about:blank")
                return;
            if (w._events.NavigationStarting(uri, user) != NavigationAction.Allow)
            {
                Gtk.webkit_policy_decision_ignore(decision);
                handled = 1;
            }
        });
        return handled;
    }

    /// <summary>window.open: Oriel opens a window, the browser or nothing; the view opens none.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint OnCreate(nint webview, nint action, nint self)
    {
        Guard(self, w =>
        {
            var uri = AppScheme.FromPage(Gtk.Text(Gtk.webkit_uri_request_get_uri(Gtk.webkit_navigation_action_get_request(action))) ?? "");
            w._events.NewWindowRequested(uri, Gtk.webkit_navigation_action_is_user_gesture(action) != 0);
        });
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPermission(nint webview, nint request, nint self)
    {
        // Pages get no permission: the same as WebPermissions.None on Windows.
        Gtk.webkit_permission_request_deny(request);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWebProcessTerminated(nint webview, int reason, nint self) => Guard(self, w =>
        w._events.WebViewFailed(WebViewFailure.RendererExited));

    public void Fail(Exception error) => _events.Failed(error);

    // ---- IPlatformWindow ----

    public void PostMessage(string message)
    {
        if (_destroyed)
            return;
        // Before the bridge runs, __orielReceive is missing and the message is dropped, as on Windows.
        var literal = JsonEncodedText.Encode(message, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
        Gtk.webkit_web_view_run_javascript(_webview, $"window.__orielReceive && window.__orielReceive(\"{literal}\")", 0, 0, 0);
    }

    public void SetTitle(string title)
    {
        if (!_destroyed)
            Gtk.gtk_window_set_title(_window, title);
    }

    public void OpenDevTools() => Gtk.webkit_web_inspector_show(Gtk.webkit_web_view_get_inspector(_webview));

    public void Close()
    {
        if (!_destroyed)
            Gtk.gtk_window_close(_window);
    }

    public void Reload() => Gtk.webkit_web_view_reload(_webview);

    public void Recreate() => Navigate(); // WebKitGTK starts a new web process for the next load

    public void ShowErrorPage(string html) => Gtk.webkit_web_view_load_html(_webview, html, null);

    public IReadOnlyList<string>? ShowFileDialog(FileDialogRequest request) => throw NotYet("File dialogs");

    public MessageResult ShowMessage(MessageRequest request) => throw NotYet("Message boxes");

    public void ShowPrintDialog(PrintDialog dialog) => throw NotYet("Printing");

    public void PrintToPdf(string path, PdfOptions? options, Action<bool> done) => throw NotYet("Printing to PDF");

    public void SetMenu(Menu? menu)
    {
        if (menu is not null)
            Tracing.Write("menus are not shown on Linux yet");
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
        int cx, cy, cw, ch;
        Gtk.gtk_window_get_position(_window, &cx, &cy);
        Gtk.gtk_window_get_size(_window, &cw, &ch);
        if (x is not null || y is not null)
            Gtk.gtk_window_move(_window, x ?? cx, y ?? cy);
        if (width is not null || height is not null)
            Gtk.gtk_window_resize(_window, width ?? cw, height ?? ch);
    }

    public void Center() => Gtk.gtk_window_set_position(_window, Gtk.GTK_WIN_POS_CENTER);

    public void SetState(WindowState state)
    {
        switch (state)
        {
            case WindowState.Maximized:
                Gtk.gtk_window_deiconify(_window);
                Gtk.gtk_window_maximize(_window);
                break;
            case WindowState.Minimized:
                Gtk.gtk_window_iconify(_window);
                break;
            default:
                Gtk.gtk_window_deiconify(_window);
                Gtk.gtk_window_unmaximize(_window);
                break;
        }
    }

    public void SetFullScreen(bool fullScreen)
    {
        if (fullScreen)
            Gtk.gtk_window_fullscreen(_window);
        else
            Gtk.gtk_window_unfullscreen(_window);
    }

    public void SetAlwaysOnTop(bool onTop) => Gtk.gtk_window_set_keep_above(_window, onTop ? 1 : 0);

    public void Focus() => Gtk.gtk_window_present(_window);

    /// <summary>The normal bounds and whether the window is maximized: <c>x,y,width,height,maximized</c>.</summary>
    public string Placement() => string.Create(CultureInfo.InvariantCulture,
        $"{_normal.X},{_normal.Y},{_normal.Width},{_normal.Height},{(_state == WindowState.Maximized ? 1 : 0)}");

    private bool Restore(string? placement)
    {
        var parts = placement?.Split(',');
        if (parts is not { Length: 5 }
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out var width) || width <= 0
            || !int.TryParse(parts[3], CultureInfo.InvariantCulture, out var height) || height <= 0)
            return false;
        Gtk.gtk_window_set_default_size(_window, width, height);
        Gtk.gtk_window_move(_window, x, y);
        if (parts[4] == "1")
            Gtk.gtk_window_maximize(_window);
        return true;
    }

    public void RunEditCommand(string command) => Gtk.webkit_web_view_execute_editing_command(_webview, command);

    private static NotSupportedException NotYet(string what) =>
        new($"Not available on Linux yet: {what}. See https://github.com/Sutsikii/Oriel/issues/30.");
}
