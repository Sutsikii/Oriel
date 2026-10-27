using System.Runtime.InteropServices;

namespace Oriel.Platform.Linux;

/// <summary>
/// The functions of GLib, GTK 3 and WebKitGTK 4.1 the Linux backend calls.
/// The libraries are the system's, loaded by name on first use, so apps
/// carry none of their own. Strings go in as UTF-8; strings that come back
/// are pointers, read with <see cref="Text"/>.
/// </summary>
internal static unsafe partial class Gtk
{
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0";
    private const string Gtk3 = "libgtk-3.so.0";
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Jsc = "libjavascriptcoregtk-4.1.so.0";
    private const string Soup = "libsoup-3.0.so.0";

    /// <summary>A UTF-8 string GTK owns, or null.</summary>
    public static string? Text(nint p) => p == 0 ? null : Marshal.PtrToStringUTF8(p);

    /// <summary>A UTF-8 string the caller owns: read, then freed with g_free.</summary>
    public static string? TakeText(nint p)
    {
        var text = Text(p);
        g_free(p);
        return text;
    }

    // ---- GLib, GObject, GIO ----

    [LibraryImport(GLib)]
    public static partial uint g_idle_add(delegate* unmanaged[Cdecl]<nint, int> function, nint data);

    [LibraryImport(GLib)]
    public static partial void g_free(nint mem);

    [LibraryImport(GLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void g_set_prgname(string name);

    [LibraryImport(GLib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(GLib)]
    public static partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(GLib)]
    public static partial void g_bytes_unref(nint bytes);

    [LibraryImport(GObject, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nuint g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);

    [LibraryImport(GObject)]
    public static partial void g_object_unref(nint obj);

    [LibraryImport(Gio)]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);

    /// <summary>A stream reading the file descriptor <paramref name="fd"/>, which it closes.</summary>
    [LibraryImport(Gio)]
    public static partial nint g_unix_input_stream_new(int fd, int closeFd);

    // ---- libc ----

    [LibraryImport("libc", SetLastError = true)]
    public static partial int pipe(int* fds);

    // ---- GTK ----

    [LibraryImport(Gtk3)]
    public static partial int gtk_init_check(nint argc, nint argv);

    [LibraryImport(Gtk3)]
    public static partial void gtk_main();

    [LibraryImport(Gtk3)]
    public static partial void gtk_main_quit();

    public const int GTK_WINDOW_TOPLEVEL = 0;
    public const int GTK_WIN_POS_CENTER = 1;

    [LibraryImport(Gtk3)]
    public static partial nint gtk_window_new(int type);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Gtk3)]
    public static partial void gtk_widget_set_size_request(nint widget, int width, int height);

    [LibraryImport(Gtk3)]
    public static partial void gtk_container_add(nint container, nint widget);

    [LibraryImport(Gtk3)]
    public static partial void gtk_widget_show_all(nint widget);

    [LibraryImport(Gtk3)]
    public static partial void gtk_widget_show(nint widget);

    [LibraryImport(Gtk3)]
    public static partial void gtk_widget_set_sensitive(nint widget, int sensitive);

    [LibraryImport(Gtk3)]
    public static partial void gtk_widget_hide(nint widget);

    [LibraryImport(Gtk3)]
    public static partial void gtk_widget_destroy(nint widget);

    [LibraryImport(Gtk3)]
    public static partial void gtk_widget_grab_focus(nint widget);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_close(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_present(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_move(nint window, int x, int y);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_resize(nint window, int width, int height);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_get_position(nint window, int* x, int* y);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_get_size(nint window, int* width, int* height);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_set_position(nint window, int position);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_maximize(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_unmaximize(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_iconify(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_deiconify(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_fullscreen(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_unfullscreen(nint window);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_set_keep_above(nint window, int setting);

    [LibraryImport(Gtk3)]
    public static partial void gtk_window_set_decorated(nint window, int setting);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gtk_show_uri_on_window(nint parent, string uri, uint timestamp, nint* error);

    // GdkEventConfigure: type, window, send_event (int8, padded), x, y, width, height.
    [StructLayout(LayoutKind.Sequential)]
    public struct GdkEventConfigure
    {
        public int Type;
        public nint Window;
        public sbyte SendEvent;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    // GdkEventWindowState: type, window, send_event, changed_mask, new_window_state.
    [StructLayout(LayoutKind.Sequential)]
    public struct GdkEventWindowState
    {
        public int Type;
        public nint Window;
        public sbyte SendEvent;
        public int ChangedMask;
        public int NewWindowState;
    }

    public const int GDK_WINDOW_STATE_ICONIFIED = 1 << 1;
    public const int GDK_WINDOW_STATE_MAXIMIZED = 1 << 2;
    public const int GDK_WINDOW_STATE_FULLSCREEN = 1 << 4;
    public const int GDK_WINDOW_STATE_FOCUSED = 1 << 7;

    // ---- WebKitGTK ----

    public const int WEBKIT_USER_CONTENT_INJECT_TOP_FRAME = 1;
    public const int WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START = 0;
    public const int WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION = 0;
    public const int WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION = 1;

    [LibraryImport(WebKit)]
    public static partial nint webkit_user_content_manager_new();

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new(string source, int injectedFrames, int injectionTime, nint allowList, nint blockList);

    [LibraryImport(WebKit)]
    public static partial void webkit_user_content_manager_add_script(nint manager, nint script);

    [LibraryImport(WebKit)]
    public static partial void webkit_user_script_unref(nint script);

    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_new_with_user_content_manager(nint manager);

    [LibraryImport(WebKit)]
    public static partial nint webkit_web_context_get_default();

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_context_register_uri_scheme(nint context, string scheme, delegate* unmanaged[Cdecl]<nint, nint, void> callback, nint data, nint destroy);

    [LibraryImport(WebKit)]
    public static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_cors_enabled(nint manager, string scheme);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint webview, string uri);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_html(nint webview, string html, string? baseUri);

    [LibraryImport(WebKit)]
    public static partial void webkit_web_view_reload(nint webview);

    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_get_uri(nint webview);

    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_get_title(nint webview);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_run_javascript(nint webview, string script, nint cancellable, nint callback, nint data);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_execute_editing_command(nint webview, string command);

    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_get_settings(nint webview);

    [LibraryImport(WebKit)]
    public static partial void webkit_settings_set_enable_developer_extras(nint settings, int enabled);

    [LibraryImport(WebKit)]
    public static partial void webkit_settings_set_javascript_can_open_windows_automatically(nint settings, int enabled);

    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_get_inspector(nint webview);

    [LibraryImport(WebKit)]
    public static partial void webkit_web_inspector_show(nint inspector);

    [LibraryImport(WebKit)]
    public static partial nint webkit_javascript_result_get_js_value(nint result);

    [LibraryImport(WebKit)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(WebKit)]
    public static partial nint webkit_uri_scheme_request_get_uri(nint request);

    [LibraryImport(WebKit)]
    public static partial nint webkit_uri_scheme_request_get_http_method(nint request);

    [LibraryImport(WebKit)]
    public static partial nint webkit_uri_scheme_request_get_http_headers(nint request);

    [LibraryImport(WebKit)]
    public static partial nint webkit_uri_scheme_response_new(nint stream, long length);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_status(nint response, uint status, string reason);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_content_type(nint response, string contentType);

    [LibraryImport(WebKit)]
    public static partial void webkit_uri_scheme_request_finish_with_response(nint request, nint response);

    [LibraryImport(WebKit)]
    public static partial nint webkit_navigation_policy_decision_get_navigation_action(nint decision);

    [LibraryImport(WebKit)]
    public static partial nint webkit_navigation_action_get_request(nint action);

    [LibraryImport(WebKit)]
    public static partial int webkit_navigation_action_is_user_gesture(nint action);

    [LibraryImport(WebKit)]
    public static partial nint webkit_uri_request_get_uri(nint request);

    [LibraryImport(WebKit)]
    public static partial void webkit_policy_decision_ignore(nint decision);

    [LibraryImport(WebKit)]
    public static partial void webkit_permission_request_deny(nint request);

    [LibraryImport(WebKit)]
    public static partial void webkit_uri_scheme_response_set_http_headers(nint response, nint headers);

    // ---- libsoup, for the headers of the app's files ----

    public const int SOUP_MESSAGE_HEADERS_RESPONSE = 1;

    [LibraryImport(Soup)]
    public static partial nint soup_message_headers_new(int type);

    [LibraryImport(Soup, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void soup_message_headers_append(nint headers, string name, string value);

    [LibraryImport(Soup, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint soup_message_headers_get_one(nint headers, string name);

    // ---- JavaScriptCore ----

    [LibraryImport(Jsc)]
    public static partial nint jsc_value_to_string(nint value);
}
