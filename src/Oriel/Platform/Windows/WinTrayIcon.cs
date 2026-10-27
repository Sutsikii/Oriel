using System.Runtime.InteropServices;

namespace Oriel.Platform.Windows;

/// <summary>
/// An icon in the notification area, through Shell_NotifyIcon (version 4).
/// Its messages go to <c>owner</c>, a hidden top-level window of the
/// platform: the menu needs a window that can come to the foreground, to
/// close when the person clicks elsewhere. UI thread only.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe partial class WinTrayIcon : IPlatformTrayIcon
{
    /// <summary>The message the icons send their owner, with the event in the low word of lParam and the icon in the high one.</summary>
    public const uint WM_APP_TRAY = Win32.WM_APP + 2;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401;
    private const uint WM_CONTEXTMENU = 0x7B;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10, LR_SHARED = 0x8000;
    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    private readonly nint _owner;
    private readonly uint _id;
    private readonly IPlatformTrayIconEvents _events;
    private readonly Action<uint> _removed;
    private readonly nint _icon;
    private readonly bool _ownsIcon;
    private string? _tooltip;
    private bool _removedAlready;

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    public WinTrayIcon(nint owner, nint instance, uint id, TrayIconSpec spec, IPlatformTrayIconEvents events, Action<uint> removed)
    {
        _owner = owner;
        _id = id;
        _events = events;
        _removed = removed;
        _tooltip = spec.Tooltip;
        (_icon, _ownsIcon) = LoadIcon(instance, spec.IconPath);
        Add();
    }

    /// <summary>Puts the icon in the notification area: when created, and again when Explorer restarts.</summary>
    public void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!Shell_NotifyIconW(NIM_ADD, &data))
            throw new InvalidOperationException("The notification area did not take the icon.");
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    /// <summary>Handles a message of the icon; true when it was one.</summary>
    public void Handle(uint message)
    {
        switch (message)
        {
            case NIN_SELECT or NIN_KEYSELECT:
                _events.Clicked();
                break;
            case WM_CONTEXTMENU:
                _events.MenuRequested();
                break;
        }
    }

    public void SetTooltip(string? tooltip)
    {
        _tooltip = tooltip;
        var data = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    public MenuItem? ShowMenu(Menu menu)
    {
        Win32.POINT point;
        Win32.GetCursorPos(&point);
        // In front, so that the menu closes when the person clicks elsewhere.
        Win32.SetForegroundWindow(_owner);
        using var native = new NativeMenu(menu, bar: false);
        var id = Win32.TrackPopupMenuEx(native.Handle, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON, point.X, point.Y, _owner, 0);
        Win32.PostMessageW(_owner, 0, 0, 0); // WM_NULL, as Shell_NotifyIcon's documentation asks after the menu
        return native.Command(id);
    }

    public void Remove()
    {
        if (_removedAlready)
            return;
        _removedAlready = true;
        var data = Data(0);
        Shell_NotifyIconW(NIM_DELETE, &data);
        if (_ownsIcon)
            DestroyIcon(_icon);
        _removed(_id);
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _owner,
            uID = _id,
            uFlags = flags,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _icon,
        };
        var tip = (_tooltip ?? "").AsSpan();
        tip = tip[..Math.Min(tip.Length, 127)];
        tip.CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    /// <summary>The icon of the .ico file at <paramref name="path"/>, or of the executable.</summary>
    private static (nint Icon, bool Owned) LoadIcon(nint instance, string? path)
    {
        int cx = Win32.GetSystemMetrics(SM_CXSMICON), cy = Win32.GetSystemMetrics(SM_CYSMICON);
        if (path is not null)
        {
            nint icon;
            fixed (char* p = Path.GetFullPath(path))
                icon = LoadImageW(0, p, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
            return icon != 0 ? (icon, true) : throw new FileNotFoundException($"{path} is not an icon file (.ico) Windows reads.", path);
        }
        var own = LoadImageW(instance, (char*)Win32.IDI_APPLICATION, IMAGE_ICON, cx, cy, LR_SHARED);
        return (own != 0 ? own : Win32.LoadIconW(0, Win32.IDI_APPLICATION), false);
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("user32.dll")]
    private static partial nint LoadImageW(nint instance, char* name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);
}
