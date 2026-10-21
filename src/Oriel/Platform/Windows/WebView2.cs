using System.Runtime.InteropServices;

namespace Oriel.Platform.Windows;

/// <summary>
/// The parts of WebView2 the backend uses, as thin wrappers over vtable
/// slots. Each slot is the index of the method in its interface's vtable in
/// WebView2.h, IUnknown's three methods first; keep the comments naming
/// the methods in step when adding one.
/// </summary>
internal static unsafe class WebView2
{
    // Interfaces Oriel queries for.
    public static readonly Guid IID_ICoreWebView2_3 = new("A0D6DF20-3B92-416D-AA0C-437A9C727857");

    // Handlers Oriel implements.
    public static readonly Guid IID_EnvironmentCompletedHandler = new("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d");
    public static readonly Guid IID_ControllerCompletedHandler = new("6c4819f3-c9b7-4260-8127-c9f5bde7f68c");
    public static readonly Guid IID_WebMessageReceivedEventHandler = new("57213f19-00e6-49fa-8e07-898ea01ecbd2");
    public static readonly Guid IID_DocumentTitleChangedEventHandler = new("f5f2b923-953e-4042-9f95-f3a118e1afd4");
    public static readonly Guid IID_AddScriptCompletedHandler = new("b99369f3-9b11-47b5-bc6f-8e7895fcea17");

    /// <summary>COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_ALLOW.</summary>
    public const int HostResourceAccessAllow = 1;

    /// <summary>COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC.</summary>
    public const int MoveFocusProgrammatic = 0;

    /// <summary>
    /// Starts creating an environment with the runtime's client library:
    /// <c>CreateWebViewEnvironmentWithOptionsInternal</c>, what WebView2Loader.dll calls.
    /// </summary>
    public static void CreateEnvironment(string clientLibrary, bool fixedRuntime, string userDataFolder, nint handler)
    {
        var library = NativeLibrary.Load(clientLibrary);
        var create = (delegate* unmanaged[Stdcall]<int, int, char*, nint, nint, int>)NativeLibrary.GetExport(library, "CreateWebViewEnvironmentWithOptionsInternal");
        fixed (char* folder = userDataFolder)
            Com.Check(create(1, fixedRuntime ? 1 : 0, folder, 0, handler), "Creating the WebView2 environment");
    }

    /// <summary>ICoreWebView2Environment.</summary>
    public static class Environment
    {
        // 3: CreateCoreWebView2Controller(HWND parentWindow, handler)
        public static void CreateController(nint env, nint hwnd, nint handler) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Com.Slot(env, 3))(env, hwnd, handler), "CreateCoreWebView2Controller");
    }

    /// <summary>ICoreWebView2Controller.</summary>
    public static class Controller
    {
        // 4: put_IsVisible(BOOL)
        public static void SetVisible(nint c, bool visible) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(c, 4))(c, visible ? 1 : 0), "put_IsVisible");

        // 6: put_Bounds(RECT)
        public static void SetBounds(nint c, Win32.RECT bounds) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, Win32.RECT, int>)Com.Slot(c, 6))(c, bounds), "put_Bounds");

        // 12: MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON)
        public static void MoveFocus(nint c) =>
            ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(c, 12))(c, MoveFocusProgrammatic);

        // 23: NotifyParentWindowPositionChanged()
        public static void NotifyParentWindowPositionChanged(nint c) =>
            ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(c, 23))(c);

        // 24: Close()
        public static void Close(nint c) =>
            ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(c, 24))(c);

        // 25: get_CoreWebView2(ICoreWebView2**)
        public static nint GetWebView(nint c)
        {
            nint webview;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(c, 25))(c, &webview), "get_CoreWebView2");
            return webview;
        }
    }

    /// <summary>ICoreWebView2, and ICoreWebView2_3 for folder mappings.</summary>
    public static class WebView
    {
        // 3: get_Settings(ICoreWebView2Settings**)
        public static nint GetSettings(nint w)
        {
            nint settings;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(w, 3))(w, &settings), "get_Settings");
            return settings;
        }

        // 5: Navigate(LPCWSTR uri)
        public static void Navigate(nint w, string uri)
        {
            fixed (char* p = uri)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Com.Slot(w, 5))(w, p), "Navigate");
        }

        // 6: NavigateToString(LPCWSTR htmlContent)
        public static void NavigateToString(nint w, string html)
        {
            fixed (char* p = html)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Com.Slot(w, 6))(w, p), "NavigateToString");
        }

        // 27: AddScriptToExecuteOnDocumentCreated(LPCWSTR javaScript, handler)
        public static void AddScriptToExecuteOnDocumentCreated(nint w, string script, nint handler)
        {
            fixed (char* p = script)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Com.Slot(w, 27))(w, p, handler), "AddScriptToExecuteOnDocumentCreated");
        }

        // 33: PostWebMessageAsString(LPCWSTR)
        public static int PostWebMessageAsString(nint w, string message)
        {
            fixed (char* p = message)
                return ((delegate* unmanaged[Stdcall]<nint, char*, int>)Com.Slot(w, 33))(w, p);
        }

        // 34: add_WebMessageReceived(handler, EventRegistrationToken*)
        public static void AddWebMessageReceived(nint w, nint handler)
        {
            long token;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Com.Slot(w, 34))(w, handler, &token), "add_WebMessageReceived");
        }

        // 46: add_DocumentTitleChanged(handler, EventRegistrationToken*)
        public static void AddDocumentTitleChanged(nint w, nint handler)
        {
            long token;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Com.Slot(w, 46))(w, handler, &token), "add_DocumentTitleChanged");
        }

        // 48: get_DocumentTitle(LPWSTR*)
        public static string GetDocumentTitle(nint w)
        {
            nint title;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(w, 48))(w, &title), "get_DocumentTitle");
            return Com.TakeString(title) ?? "";
        }

        // 51: OpenDevToolsWindow()
        public static void OpenDevToolsWindow(nint w) =>
            ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(w, 51))(w);

        // ICoreWebView2_3, 71: SetVirtualHostNameToFolderMapping(LPCWSTR hostName, LPCWSTR folderPath, kind)
        public static void SetVirtualHostNameToFolderMapping(nint w, string host, string folder)
        {
            var w3 = Com.QueryInterface(w, IID_ICoreWebView2_3);
            if (w3 == 0)
                throw new WebViewUnavailableException("The WebView2 Runtime is too old to serve app files; update it from " + WebView2Runtime.Download);
            try
            {
                fixed (char* h = host)
                fixed (char* f = folder)
                    Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, char*, int, int>)Com.Slot(w3, 71))(w3, h, f, HostResourceAccessAllow), "SetVirtualHostNameToFolderMapping");
            }
            finally
            {
                Com.Release(w3);
            }
        }
    }

    /// <summary>ICoreWebView2Settings.</summary>
    public static class Settings
    {
        // 10: put_IsStatusBarEnabled(BOOL)
        public static void SetStatusBarEnabled(nint s, bool value) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(s, 10))(s, value ? 1 : 0), "put_IsStatusBarEnabled");

        // 12: put_AreDevToolsEnabled(BOOL)
        public static void SetDevToolsEnabled(nint s, bool value) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(s, 12))(s, value ? 1 : 0), "put_AreDevToolsEnabled");

        // 14: put_AreDefaultContextMenusEnabled(BOOL)
        public static void SetDefaultContextMenusEnabled(nint s, bool value) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(s, 14))(s, value ? 1 : 0), "put_AreDefaultContextMenusEnabled");
    }

    /// <summary>ICoreWebView2WebMessageReceivedEventArgs.</summary>
    public static class WebMessageReceivedEventArgs
    {
        // 3: get_Source(LPWSTR*)
        public static string GetSource(nint a)
        {
            nint source;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(a, 3))(a, &source), "get_Source");
            return Com.TakeString(source) ?? "";
        }

        // 5: TryGetWebMessageAsString(LPWSTR*); fails for messages that are not strings.
        public static string? TryGetString(nint a)
        {
            nint message;
            var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(a, 5))(a, &message);
            return hr < 0 ? null : Com.TakeString(message);
        }
    }
}
