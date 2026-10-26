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
    public static readonly Guid IID_ICoreWebView2_7 = new("79C24D83-09A3-45AE-9418-487F32A58740");
    public static readonly Guid IID_ICoreWebView2_16 = new("0EB34DC9-9F91-41E1-8639-95CD5943906B");
    public static readonly Guid IID_ICoreWebView2Environment6 = new("E59EE362-ACBD-4857-9A8E-D3644D9459A9");
    public static readonly Guid IID_ICoreWebView2Settings9 = new("0528a73b-e92d-49f4-927a-e547dddaa37d");
    public static readonly Guid IID_ICoreWebView2AcceleratorKeyPressedEventArgs2 = new("03b2c8c8-7799-4e34-bd66-ed26aa85f2bf");
    public static readonly Guid IID_ICoreWebView2WebMessageReceivedEventArgs2 = new("06fc7ab7-c90c-4297-9389-33ca01cf6d5e");
    public static readonly Guid IID_ICoreWebView2File = new("f2c19559-6bc1-4583-a757-90021be9afec");

    // Handlers Oriel implements.
    public static readonly Guid IID_EnvironmentCompletedHandler = new("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d");
    public static readonly Guid IID_ControllerCompletedHandler = new("6c4819f3-c9b7-4260-8127-c9f5bde7f68c");
    public static readonly Guid IID_WebMessageReceivedEventHandler = new("57213f19-00e6-49fa-8e07-898ea01ecbd2");
    public static readonly Guid IID_DocumentTitleChangedEventHandler = new("f5f2b923-953e-4042-9f95-f3a118e1afd4");
    public static readonly Guid IID_AddScriptCompletedHandler = new("b99369f3-9b11-47b5-bc6f-8e7895fcea17");
    public static readonly Guid IID_ProcessFailedEventHandler = new("79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1");
    public static readonly Guid IID_NavigationStartingEventHandler = new("9adbe429-f36d-432b-9ddc-f8881fbd76e3");
    public static readonly Guid IID_NewWindowRequestedEventHandler = new("d4c185fe-c81c-4989-97af-2d3fa7ab5651");
    public static readonly Guid IID_PermissionRequestedEventHandler = new("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd");
    public static readonly Guid IID_WebResourceRequestedEventHandler = new("ab00b74c-15f1-4646-80e8-e76341d25d71");
    public static readonly Guid IID_PrintToPdfCompletedHandler = new("ccf1ef04-fd8e-4d5f-b2de-0983e41b8c36");
    public static readonly Guid IID_AcceleratorKeyPressedEventHandler = new("b29c7e28-fa79-41a8-8e44-65811c76dcb2");
    public static readonly Guid IID_CallDevToolsProtocolMethodCompletedHandler = new("5c4889f0-5ef6-4c5a-952c-d8f1b92d0574");

    /// <summary>COREWEBVIEW2_PRINT_ORIENTATION_LANDSCAPE.</summary>
    public const int PrintLandscape = 1;

    /// <summary>COREWEBVIEW2_PERMISSION_STATE_ALLOW and _DENY.</summary>
    public const int PermissionAllow = 1;
    public const int PermissionDeny = 2;

    /// <summary>COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_ALLOW.</summary>
    public const int HostResourceAccessAllow = 1;

    /// <summary>COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC.</summary>
    public const int MoveFocusProgrammatic = 0;

    /// <summary>COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN and _SYSTEM_KEY_DOWN, a key pressed with Alt.</summary>
    public const int KeyDown = 0;
    public const int SystemKeyDown = 2;

    /// <summary>COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL.</summary>
    public const int WebResourceContextAll = 0;

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

        // 4: CreateWebResourceResponse(IStream* content, int statusCode, LPCWSTR reasonPhrase, LPCWSTR headers, ICoreWebView2WebResourceResponse**)
        public static nint CreateWebResourceResponse(nint env, nint content, int status, string reason, string headers)
        {
            nint response;
            fixed (char* r = reason)
            fixed (char* h = headers)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, char*, char*, nint*, int>)Com.Slot(env, 4))(env, content, status, r, h, &response), "CreateWebResourceResponse");
            return response;
        }
    }

    /// <summary>ICoreWebView2Environment6, for print settings.</summary>
    public static class Environment6
    {
        // 14: CreatePrintSettings(ICoreWebView2PrintSettings**)
        public static nint CreatePrintSettings(nint env6)
        {
            nint settings;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(env6, 14))(env6, &settings), "CreatePrintSettings");
            return settings;
        }
    }

    /// <summary>ICoreWebView2PrintSettings: its put_ methods, in slot order.</summary>
    public static class PrintSettings
    {
        // 4: put_Orientation(COREWEBVIEW2_PRINT_ORIENTATION)
        public static void SetOrientation(nint s, int orientation) => SetInt(s, 4, orientation, "put_Orientation");

        // 6: put_ScaleFactor(double)
        public static void SetScale(nint s, double value) => SetDouble(s, 6, value, "put_ScaleFactor");

        // 8: put_PageWidth(double), 10: put_PageHeight(double), in inches
        public static void SetPageSize(nint s, double width, double height)
        {
            SetDouble(s, 8, width, "put_PageWidth");
            SetDouble(s, 10, height, "put_PageHeight");
        }

        // 12: put_MarginTop, 14: put_MarginBottom, 16: put_MarginLeft, 18: put_MarginRight (double, inches)
        public static void SetMargins(nint s, double top, double bottom, double left, double right)
        {
            SetDouble(s, 12, top, "put_MarginTop");
            SetDouble(s, 14, bottom, "put_MarginBottom");
            SetDouble(s, 16, left, "put_MarginLeft");
            SetDouble(s, 18, right, "put_MarginRight");
        }

        // 20: put_ShouldPrintBackgrounds(BOOL)
        public static void SetBackgrounds(nint s, bool value) => SetInt(s, 20, value ? 1 : 0, "put_ShouldPrintBackgrounds");

        // 24: put_ShouldPrintHeaderAndFooter(BOOL)
        public static void SetHeaderAndFooter(nint s, bool value) => SetInt(s, 24, value ? 1 : 0, "put_ShouldPrintHeaderAndFooter");

        // 26: put_HeaderTitle(LPCWSTR)
        public static void SetHeaderTitle(nint s, string title)
        {
            fixed (char* p = title)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Com.Slot(s, 26))(s, p), "put_HeaderTitle");
        }

        private static void SetInt(nint s, int slot, int value, string name) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(s, slot))(s, value), name);

        private static void SetDouble(nint s, int slot, double value, string name) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, double, int>)Com.Slot(s, slot))(s, value), name);
    }

    /// <summary>ICoreWebView2_7 and ICoreWebView2_16, for printing.</summary>
    public static class Printing
    {
        // ICoreWebView2_7, 80: PrintToPdf(LPCWSTR resultFilePath, ICoreWebView2PrintSettings*, handler)
        public static void PrintToPdf(nint w7, string path, nint settings, nint handler)
        {
            fixed (char* p = path)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, nint, int>)Com.Slot(w7, 80))(w7, p, settings, handler), "PrintToPdf");
        }

        // ICoreWebView2_16, 114: ShowPrintUI(COREWEBVIEW2_PRINT_DIALOG_KIND), BROWSER 0 and SYSTEM 1
        public static void ShowPrintUI(nint w16, int kind) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(w16, 114))(w16, kind), "ShowPrintUI");
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

        // 19: add_AcceleratorKeyPressed(handler, EventRegistrationToken*)
        public static void AddAcceleratorKeyPressed(nint c, nint handler)
        {
            long token;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Com.Slot(c, 19))(c, handler, &token), "add_AcceleratorKeyPressed");
        }

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

        // 7: add_NavigationStarting(handler, EventRegistrationToken*)
        public static void AddNavigationStarting(nint w, nint handler) => Add(w, 7, handler, "add_NavigationStarting");

        // 23: add_PermissionRequested(handler, EventRegistrationToken*)
        public static void AddPermissionRequested(nint w, nint handler) => Add(w, 23, handler, "add_PermissionRequested");

        // 44: add_NewWindowRequested(handler, EventRegistrationToken*)
        public static void AddNewWindowRequested(nint w, nint handler) => Add(w, 44, handler, "add_NewWindowRequested");

        // 55: add_WebResourceRequested(handler, EventRegistrationToken*)
        public static void AddWebResourceRequested(nint w, nint handler) => Add(w, 55, handler, "add_WebResourceRequested");

        // 57: AddWebResourceRequestedFilter(LPCWSTR uri, COREWEBVIEW2_WEB_RESOURCE_CONTEXT)
        public static void AddWebResourceRequestedFilter(nint w, string uri, int context)
        {
            fixed (char* p = uri)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Com.Slot(w, 57))(w, p, context), "AddWebResourceRequestedFilter");
        }

        /// <summary>Calls the add_ method of an event in <paramref name="slot"/>; Oriel never removes its handlers, which go with the webview.</summary>
        private static void Add(nint w, int slot, nint handler, string name)
        {
            long token;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Com.Slot(w, slot))(w, handler, &token), name);
        }

        // 25: add_ProcessFailed(handler, EventRegistrationToken*)
        public static void AddProcessFailed(nint w, nint handler)
        {
            long token;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Com.Slot(w, 25))(w, handler, &token), "add_ProcessFailed");
        }

        // 27: AddScriptToExecuteOnDocumentCreated(LPCWSTR javaScript, handler)
        public static void AddScriptToExecuteOnDocumentCreated(nint w, string script, nint handler)
        {
            fixed (char* p = script)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Com.Slot(w, 27))(w, p, handler), "AddScriptToExecuteOnDocumentCreated");
        }

        // 31: Reload()
        public static void Reload(nint w) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(w, 31))(w), "Reload");

        // 33: PostWebMessageAsString(LPCWSTR)
        public static int PostWebMessageAsString(nint w, string message)
        {
            fixed (char* p = message)
                return ((delegate* unmanaged[Stdcall]<nint, char*, int>)Com.Slot(w, 33))(w, p);
        }

        // 36: CallDevToolsProtocolMethod(LPCWSTR methodName, LPCWSTR parametersAsJson, handler)
        public static void CallDevToolsProtocolMethod(nint w, string method, string parameters, nint handler)
        {
            fixed (char* m = method)
            fixed (char* p = parameters)
                Com.Check(((delegate* unmanaged[Stdcall]<nint, char*, char*, nint, int>)Com.Slot(w, 36))(w, m, p, handler), "CallDevToolsProtocolMethod");
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

        // ICoreWebView2Settings9, 38: put_IsNonClientRegionSupportEnabled(BOOL), for app-region: drag.
        // An older runtime lacks it: the page's drag regions do nothing.
        public static void EnableNonClientRegions(nint s)
        {
            var s9 = Com.QueryInterface(s, IID_ICoreWebView2Settings9);
            if (s9 == 0)
                return;
            try
            {
                Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(s9, 38))(s9, 1), "put_IsNonClientRegionSupportEnabled");
            }
            finally
            {
                Com.Release(s9);
            }
        }

        // 14: put_AreDefaultContextMenusEnabled(BOOL)
        public static void SetDefaultContextMenusEnabled(nint s, bool value) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(s, 14))(s, value ? 1 : 0), "put_AreDefaultContextMenusEnabled");
    }

    /// <summary>Getters shared by event arguments: a string or a BOOL in a slot.</summary>
    private static string GetString(nint a, int slot, string name)
    {
        nint value;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(a, slot))(a, &value), name);
        return Com.TakeString(value) ?? "";
    }

    private static bool GetBool(nint a, int slot, string name)
    {
        int value;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Slot(a, slot))(a, &value), name);
        return value != 0;
    }

    private static void SetInt(nint a, int slot, int value, string name) =>
        Com.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(a, slot))(a, value), name);

    /// <summary>ICoreWebView2AcceleratorKeyPressedEventArgs, and its 2 for the browser's own accelerators.</summary>
    public static class AcceleratorKeyPressedEventArgs
    {
        // 3: get_KeyEventKind(COREWEBVIEW2_KEY_EVENT_KIND*)
        public static int GetKind(nint a)
        {
            int kind;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Slot(a, 3))(a, &kind), "get_KeyEventKind");
            return kind;
        }

        // 4: get_VirtualKey(UINT*)
        public static int GetVirtualKey(nint a)
        {
            uint key;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(a, 4))(a, &key), "get_VirtualKey");
            return (int)key;
        }

        // ICoreWebView2AcceleratorKeyPressedEventArgs2, 10: put_IsBrowserAcceleratorKeyEnabled(BOOL).
        // The page still gets the key; an older runtime lacks it.
        public static void DisableBrowserAccelerator(nint a)
        {
            var a2 = Com.QueryInterface(a, IID_ICoreWebView2AcceleratorKeyPressedEventArgs2);
            if (a2 == 0)
                return;
            try
            {
                SetInt(a2, 10, 0, "put_IsBrowserAcceleratorKeyEnabled");
            }
            finally
            {
                Com.Release(a2);
            }
        }
    }

    /// <summary>ICoreWebView2NavigationStartingEventArgs.</summary>
    public static class NavigationStartingEventArgs
    {
        // 3: get_Uri(LPWSTR*)
        public static string GetUri(nint a) => GetString(a, 3, "get_Uri");

        // 4: get_IsUserInitiated(BOOL*)
        public static bool IsUserInitiated(nint a) => GetBool(a, 4, "get_IsUserInitiated");

        // 8: put_Cancel(BOOL)
        public static void Cancel(nint a) => SetInt(a, 8, 1, "put_Cancel");
    }

    /// <summary>ICoreWebView2NewWindowRequestedEventArgs.</summary>
    public static class NewWindowRequestedEventArgs
    {
        // 3: get_Uri(LPWSTR*)
        public static string GetUri(nint a) => GetString(a, 3, "get_Uri");

        // 6: put_Handled(BOOL): no window of WebView2's own
        public static void SetHandled(nint a) => SetInt(a, 6, 1, "put_Handled");

        // 8: get_IsUserInitiated(BOOL*)
        public static bool IsUserInitiated(nint a) => GetBool(a, 8, "get_IsUserInitiated");
    }

    /// <summary>ICoreWebView2PermissionRequestedEventArgs.</summary>
    public static class PermissionRequestedEventArgs
    {
        // 3: get_Uri(LPWSTR*)
        public static string GetUri(nint a) => GetString(a, 3, "get_Uri");

        // 4: get_PermissionKind(COREWEBVIEW2_PERMISSION_KIND*)
        public static WebPermissions GetPermission(nint a)
        {
            int kind;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Slot(a, 4))(a, &kind), "get_PermissionKind");
            return Permission(kind);
        }

        // 7: put_State(COREWEBVIEW2_PERMISSION_STATE)
        public static void SetState(nint a, bool allow) => SetInt(a, 7, allow ? PermissionAllow : PermissionDeny, "put_State");

        /// <summary>The permission of a COREWEBVIEW2_PERMISSION_KIND; None for kinds Oriel does not know.</summary>
        public static WebPermissions Permission(int kind) => kind switch
        {
            1 => WebPermissions.Microphone,
            2 => WebPermissions.Camera,
            3 => WebPermissions.Geolocation,
            4 => WebPermissions.Notifications,
            5 => WebPermissions.OtherSensors,
            6 => WebPermissions.ClipboardRead,
            7 => WebPermissions.MultipleDownloads,
            8 => WebPermissions.FileSystem,
            9 => WebPermissions.Autoplay,
            10 => WebPermissions.LocalFonts,
            11 => WebPermissions.MidiSystemExclusive,
            12 => WebPermissions.WindowManagement,
            13 => WebPermissions.PersistentStorage,
            _ => WebPermissions.None,
        };
    }

    /// <summary>ICoreWebView2ProcessFailedEventArgs.</summary>
    public static class ProcessFailedEventArgs
    {
        // 3: get_ProcessFailedKind(COREWEBVIEW2_PROCESS_FAILED_KIND*)
        public static WebViewFailure GetFailure(nint a)
        {
            int kind;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Slot(a, 3))(a, &kind), "get_ProcessFailedKind");
            return Failure(kind);
        }

        /// <summary>The failure of a COREWEBVIEW2_PROCESS_FAILED_KIND.</summary>
        public static WebViewFailure Failure(int kind) => kind switch
        {
            0 => WebViewFailure.BrowserExited,
            1 => WebViewFailure.RendererExited,
            2 => WebViewFailure.RendererUnresponsive,
            3 => WebViewFailure.FrameRendererExited,
            6 => WebViewFailure.GpuExited,
            _ => WebViewFailure.Other,
        };
    }

    /// <summary>ICoreWebView2WebResourceRequestedEventArgs.</summary>
    public static class WebResourceRequestedEventArgs
    {
        // 3: get_Request(ICoreWebView2WebResourceRequest**); its 3: get_Uri(LPWSTR*)
        public static string GetUri(nint a)
        {
            nint request;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(a, 3))(a, &request), "get_Request");
            try
            {
                return GetString(request, 3, "get_Uri");
            }
            finally
            {
                Com.Release(request);
            }
        }

        // 5: put_Response(ICoreWebView2WebResourceResponse*)
        public static void SetResponse(nint a, nint response) =>
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(a, 5))(a, response), "put_Response");
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

        // ICoreWebView2WebMessageReceivedEventArgs2, 6: get_AdditionalObjects(ICoreWebView2ObjectCollectionView**),
        // whose 3: get_Count(UINT32*) and 4: GetValueAtIndex(UINT32, IUnknown**);
        // ICoreWebView2File, 3: get_Path(LPWSTR*).
        /// <summary>The paths of the <c>File</c> objects the page sent with the message; none from an older runtime.</summary>
        public static List<string> GetFilePaths(nint a)
        {
            var paths = new List<string>();
            var a2 = Com.QueryInterface(a, IID_ICoreWebView2WebMessageReceivedEventArgs2);
            if (a2 == 0)
                return paths;
            nint view = 0;
            try
            {
                Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(a2, 6))(a2, &view), "get_AdditionalObjects");
                if (view == 0)
                    return paths;
                uint count;
                Com.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Slot(view, 3))(view, &count), "get_Count");
                for (uint i = 0; i < count; i++)
                {
                    nint item;
                    Com.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Com.Slot(view, 4))(view, i, &item), "GetValueAtIndex");
                    if (item == 0)
                        continue;
                    var file = Com.QueryInterface(item, IID_ICoreWebView2File);
                    Com.Release(item);
                    if (file == 0)
                        continue; // not a File
                    try
                    {
                        nint path;
                        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(file, 3))(file, &path), "get_Path");
                        if (Com.TakeString(path) is { Length: > 0 } p)
                            paths.Add(p);
                    }
                    finally
                    {
                        Com.Release(file);
                    }
                }
                return paths;
            }
            finally
            {
                Com.Release(view);
                Com.Release(a2);
            }
        }
    }
}
