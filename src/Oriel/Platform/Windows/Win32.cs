using System.Runtime.InteropServices;

namespace Oriel.Platform.Windows;

/// <summary>
/// The Win32 functions, structures and constants the Windows backend uses,
/// declared with source-generated marshalling so they work under NativeAOT.
/// Names follow the Windows SDK, so they can be looked up there.
/// </summary>
internal static unsafe partial class Win32
{
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;

    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public static readonly nint HWND_MESSAGE = -3;

    public const int SW_SHOWNORMAL = 1;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_NCPAINT = 0x0085;
    public const uint WM_NCACTIVATE = 0x0086;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_INITMENUPOPUP = 0x0117;
    public const uint WM_DPICHANGED = 0x02E0;

    /// <summary>Undocumented: the menu bar is about to paint its background, and an item. See <see cref="UAHMENU"/>.</summary>
    public const uint WM_UAHDRAWMENU = 0x0091;
    public const uint WM_UAHDRAWMENUITEM = 0x0092;
    public const uint WM_APP = 0x8000;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOMOVE = 0x0002;

    public const int SIZE_MINIMIZED = 1;

    public const nuint SC_KEYMENU = 0xF100;

    public const uint MIIM_STATE = 0x0001;
    public const uint MIIM_ID = 0x0002;
    public const uint MIIM_SUBMENU = 0x0004;
    public const uint MIIM_STRING = 0x0040;
    public const uint MIIM_FTYPE = 0x0100;
    public const uint MFT_SEPARATOR = 0x0800;
    public const uint MFS_DISABLED = 0x0003;
    public const uint MFS_CHECKED = 0x0008;

    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;

    public const int OBJID_MENU = -3;

    public const uint ODS_SELECTED = 0x0001;
    public const uint ODS_GRAYED = 0x0002;
    public const uint ODS_DISABLED = 0x0004;
    public const uint ODS_HOTLIGHT = 0x0040;
    public const uint ODS_INACTIVE = 0x0080;
    public const uint ODS_NOACCEL = 0x0100;

    public const uint DT_CENTER = 0x0001;
    public const uint DT_VCENTER = 0x0004;
    public const uint DT_SINGLELINE = 0x0020;
    public const uint DT_HIDEPREFIX = 0x00100000;
    public const uint DTT_TEXTCOLOR = 0x0001;
    public const int MENU_BARITEM = 8;
    public const int MBI_NORMAL = 1;

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;

    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public const uint MB_OK = 0x0;
    public const uint MB_OKCANCEL = 0x1;
    public const uint MB_YESNOCANCEL = 0x3;
    public const uint MB_YESNO = 0x4;
    public const uint MB_ICONERROR = 0x10;
    public const uint MB_ICONQUESTION = 0x20;
    public const uint MB_ICONWARNING = 0x30;
    public const uint MB_ICONINFORMATION = 0x40;

    public const int IDOK = 1;
    public const int IDCANCEL = 2;
    public const int IDYES = 6;
    public const int IDNO = 7;

    public const int IDC_ARROW = 32512;
    public const int IDI_APPLICATION = 32512;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MENUITEMINFOW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nuint dwItemData;
        public char* dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MENUBARINFO
    {
        public uint cbSize;
        public RECT rcBar;
        public nint hMenu;
        public nint hwndMenu;
        public int fFocused;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWITEMSTRUCT
    {
        public uint CtlType;
        public uint CtlID;
        public uint itemID;
        public uint itemAction;
        public uint itemState;
        public nint hwndItem;
        public nint hDC;
        public RECT rcItem;
        public nuint itemData;
    }

    /// <summary>
    /// Undocumented, what <see cref="WM_UAHDRAWMENU"/> points to: the menu bar
    /// paints with the light theme only, so dark windows paint it themselves.
    /// The layout is the one Windows has had since Vista.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UAHMENU
    {
        public nint hmenu;
        public nint hdc;
        public uint dwFlags;
    }

    /// <summary>What <see cref="WM_UAHDRAWMENUITEM"/> points to; the item's metrics after its position are not needed.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UAHDRAWMENUITEM
    {
        public DRAWITEMSTRUCT dis;
        public UAHMENU um;
        public int iPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DTTOPTS
    {
        public uint dwSize;
        public uint dwFlags;
        public uint crText;
        public uint crBorder;
        public uint crShadow;
        public int iTextShadowType;
        public POINT ptShadowOffset;
        public int iBorderSize;
        public int iFontPropId;
        public int iColorPropId;
        public int iStateId;
        public int fApplyOverlay;
        public int iGlowSize;
        public nint pfnDrawTextCallback;
        public nint lParam;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll")]
    public static partial nint CreateMenu();

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InsertMenuItemW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, MENUITEMINFOW* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenuItemInfoW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, MENUITEMINFOW* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMenuItemInfoW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, MENUITEMINFOW* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenu(nint hwnd, nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawMenuBar(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMenuBarInfo(nint hwnd, int idObject, int idItem, MENUBARINFO* info);

    /// <summary>With <see cref="TPM_RETURNCMD"/>, returns the id of the item clicked, or 0.</summary>
    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(POINT* point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, POINT* point);

    [LibraryImport("user32.dll")]
    public static partial int MapWindowPoints(nint from, nint to, POINT* points, uint count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, RECT* rect);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int key);

    [LibraryImport("user32.dll")]
    public static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    public static partial nint GetWindowDC(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("user32.dll")]
    public static partial int FillRect(nint hdc, RECT* rect, nint brush);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenThemeData(nint hwnd, string classList);

    [LibraryImport("uxtheme.dll")]
    public static partial int CloseThemeData(nint theme);

    [LibraryImport("uxtheme.dll")]
    public static partial int DrawThemeTextEx(nint theme, nint hdc, int part, int state, char* text, int length, uint flags, RECT* rect, DTTOPTS* options);

    /// <summary>A function of <paramref name="module"/> by its ordinal, for undocumented ones; 0 when it has none.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial nint GetProcAddress(nint module, nint ordinal);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmdShow);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* msg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hwnd, RECT* rect);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hwnd, string text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial nint LoadIconW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetModuleHandleW(char* name);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecuteW(nint hwnd, string? operation, string file, string? parameters, string? directory, int showCommand);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint p);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);

    [LibraryImport("shell32.dll")]
    public static partial int SHCreateItemFromParsingName(char* path, nint bindContext, Guid* iid, nint* result);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);

    /// <summary>An IStream over a copy of <paramref name="size"/> bytes; 0 when memory runs out.</summary>
    [LibraryImport("shlwapi.dll")]
    public static partial nint SHCreateMemStream(byte* init, uint size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, void* value, int size);

    public static int LoWord(nint value) => (int)(value & 0xFFFF);

    public static int HiWord(nint value) => (int)((value >> 16) & 0xFFFF);
}
