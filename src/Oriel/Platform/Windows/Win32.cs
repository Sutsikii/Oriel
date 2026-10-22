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
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APP = 0x8000;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOMOVE = 0x0002;

    public const int SIZE_MINIMIZED = 1;

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

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

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
