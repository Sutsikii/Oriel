using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Oriel.Platform.Windows;

/// <summary>
/// A <see cref="Menu"/> as a Win32 menu: a bar, or a popup to show at a
/// point. Items are numbered from 1 in the order they are added, so a
/// <c>WM_COMMAND</c>, or what <c>TrackPopupMenuEx</c> returns, finds them.
/// Each popup is refreshed as it opens, so it shows what its items are then.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class NativeMenu : IDisposable
{
    private readonly List<MenuItem> _commands = [];
    private readonly Dictionary<nint, IReadOnlyList<MenuItem>> _popups = [];

    public NativeMenu(Menu menu, bool bar)
    {
        Handle = bar ? Win32.CreateMenu() : Win32.CreatePopupMenu();
        if (Handle == 0)
            throw new InvalidOperationException("Creating a menu failed.");
        try
        {
            Fill(Handle, menu.Items);
        }
        catch
        {
            Win32.DestroyMenu(Handle);
            throw;
        }
    }

    public nint Handle { get; }

    /// <summary>The item numbered <paramref name="id"/>, or null.</summary>
    public MenuItem? Command(int id) => id >= 1 && id <= _commands.Count ? _commands[id - 1] : null;

    /// <summary>
    /// Updates the texts and states of <paramref name="popup"/>, which is
    /// about to open, from its items. False when it is not one of this menu's.
    /// </summary>
    public bool Refresh(nint popup)
    {
        if (!_popups.TryGetValue(popup, out var items))
            return false;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsSeparator)
                continue;
            fixed (char* text = Label(items[i]))
            {
                var info = new Win32.MENUITEMINFOW
                {
                    cbSize = (uint)sizeof(Win32.MENUITEMINFOW),
                    fMask = Win32.MIIM_STRING | Win32.MIIM_STATE,
                    fState = State(items[i]),
                    dwTypeData = text,
                };
                Win32.SetMenuItemInfoW(popup, (uint)i, true, &info);
            }
        }
        return true;
    }

    /// <summary>Destroys the menu and its submenus. A bar must be off its window first.</summary>
    public void Dispose() => Win32.DestroyMenu(Handle);

    private void Fill(nint menu, IReadOnlyList<MenuItem> items)
    {
        _popups[menu] = items;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var info = new Win32.MENUITEMINFOW { cbSize = (uint)sizeof(Win32.MENUITEMINFOW) };
            if (item.IsSeparator)
            {
                info.fMask = Win32.MIIM_FTYPE;
                info.fType = Win32.MFT_SEPARATOR;
                Insert(menu, i, &info);
                continue;
            }
            info.fMask = Win32.MIIM_ID | Win32.MIIM_STRING | Win32.MIIM_STATE;
            info.fState = State(item);
            if (item.Items.Count > 0)
            {
                var submenu = Win32.CreatePopupMenu();
                if (submenu == 0)
                    throw new InvalidOperationException("Creating a menu failed.");
                info.fMask |= Win32.MIIM_SUBMENU;
                info.hSubMenu = submenu;
                try
                {
                    Fill(submenu, item.Items);
                }
                catch
                {
                    Win32.DestroyMenu(submenu);
                    throw;
                }
            }
            else
            {
                _commands.Add(item);
                info.wID = (uint)_commands.Count;
            }
            fixed (char* text = Label(item))
            {
                info.dwTypeData = text;
                Insert(menu, i, &info);
            }
        }
    }

    private static void Insert(nint menu, int position, Win32.MENUITEMINFOW* info)
    {
        if (!Win32.InsertMenuItemW(menu, (uint)position, true, info))
            throw new InvalidOperationException("Adding an item to a menu failed.");
    }

    /// <summary>The item's text, and its accelerator after a tab, which Windows aligns on the right.</summary>
    private static string Label(MenuItem item) => item.Accelerator is { } accelerator ? item.Text + "\t" + accelerator : item.Text;

    private static uint State(MenuItem item) =>
        (item.Enabled ? 0 : Win32.MFS_DISABLED) | (item.IsCheckable && item.IsChecked ? Win32.MFS_CHECKED : 0);
}

/// <summary>
/// Menus in the dark theme. Windows has no documented way: popup menus
/// follow the app's preferred mode, which uxtheme sets through functions it
/// exports by ordinal only, and the menu bar paints in the light theme
/// whatever happens, so dark windows paint it themselves. This is what
/// Windows' own apps and Notepad++ do. Without the functions, menus stay light.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class DarkMenus
{
    // uxtheme's ordinals, the same since Windows 10 1809.
    private const int RefreshImmersiveColorPolicyStateOrdinal = 104;
    private const int AllowDarkModeForWindowOrdinal = 133;
    private const int SetPreferredAppModeOrdinal = 135; // AllowDarkModeForApp(BOOL) in 1809: 1 means the same
    private const int FlushMenuThemesOrdinal = 136;

    private const int AllowDark = 1;

    private static readonly uint s_bar = Rgb(0x20, 0x20, 0x20);
    private static readonly uint s_hot = Rgb(0x3D, 0x3D, 0x3D);
    private static readonly uint s_text = Rgb(0xFF, 0xFF, 0xFF);
    private static readonly uint s_grayed = Rgb(0x6D, 0x6D, 0x6D);

    private static readonly nint s_barBrush = Win32.CreateSolidBrush(s_bar);
    private static readonly nint s_hotBrush = Win32.CreateSolidBrush(s_hot);

    private static readonly delegate* unmanaged[Stdcall]<void> s_refreshPolicy;
    private static readonly delegate* unmanaged[Stdcall]<nint, int, int> s_allowForWindow;
    private static readonly delegate* unmanaged[Stdcall]<void> s_flushMenuThemes;

    static DarkMenus()
    {
        if (!NativeLibrary.TryLoad("uxtheme.dll", out var uxtheme))
            return;
        var setMode = (delegate* unmanaged[Stdcall]<int, int>)Win32.GetProcAddress(uxtheme, SetPreferredAppModeOrdinal);
        s_refreshPolicy = (delegate* unmanaged[Stdcall]<void>)Win32.GetProcAddress(uxtheme, RefreshImmersiveColorPolicyStateOrdinal);
        s_allowForWindow = (delegate* unmanaged[Stdcall]<nint, int, int>)Win32.GetProcAddress(uxtheme, AllowDarkModeForWindowOrdinal);
        s_flushMenuThemes = (delegate* unmanaged[Stdcall]<void>)Win32.GetProcAddress(uxtheme, FlushMenuThemesOrdinal);
        if (setMode is null || s_refreshPolicy is null || s_allowForWindow is null || s_flushMenuThemes is null)
        {
            s_allowForWindow = null;
            return;
        }
        setMode(AllowDark); // menus follow the system's mode from now on
    }

    /// <summary>Lets the menus of <paramref name="hwnd"/> be dark, or not, and makes them show it.</summary>
    public static void Apply(nint hwnd, bool dark)
    {
        if (s_allowForWindow is null)
            return;
        s_allowForWindow(hwnd, dark ? 1 : 0);
        s_refreshPolicy();
        s_flushMenuThemes();
    }

    /// <summary>Paints the background of the bar, for <see cref="Win32.WM_UAHDRAWMENU"/>.</summary>
    public static void PaintBar(nint hwnd, Win32.UAHMENU* menu)
    {
        var info = new Win32.MENUBARINFO { cbSize = (uint)sizeof(Win32.MENUBARINFO) };
        if (!Win32.GetMenuBarInfo(hwnd, Win32.OBJID_MENU, 0, &info))
            return;
        Win32.RECT window;
        Win32.GetWindowRect(hwnd, &window);
        var bar = Offset(info.rcBar, -window.Left, -window.Top);
        Win32.FillRect(menu->hdc, &bar, s_barBrush);
    }

    /// <summary>Paints an item of the bar, for <see cref="Win32.WM_UAHDRAWMENUITEM"/>.</summary>
    public static void PaintItem(nint theme, Win32.UAHDRAWMENUITEM* item)
    {
        var text = stackalloc char[256];
        var info = new Win32.MENUITEMINFOW
        {
            cbSize = (uint)sizeof(Win32.MENUITEMINFOW),
            fMask = Win32.MIIM_STRING,
            dwTypeData = text,
            cch = 255,
        };
        if (!Win32.GetMenuItemInfoW(item->um.hmenu, (uint)item->iPosition, true, &info))
            return;

        var state = item->dis.itemState;
        var hot = (state & (Win32.ODS_HOTLIGHT | Win32.ODS_SELECTED)) != 0;
        var grayed = (state & (Win32.ODS_GRAYED | Win32.ODS_DISABLED)) != 0;
        var rect = item->dis.rcItem;
        Win32.FillRect(item->um.hdc, &rect, hot ? s_hotBrush : s_barBrush);

        var flags = Win32.DT_CENTER | Win32.DT_SINGLELINE | Win32.DT_VCENTER;
        if ((state & Win32.ODS_NOACCEL) != 0)
            flags |= Win32.DT_HIDEPREFIX; // the mnemonics show once Alt is pressed
        var options = new Win32.DTTOPTS
        {
            dwSize = (uint)sizeof(Win32.DTTOPTS),
            dwFlags = Win32.DTT_TEXTCOLOR,
            crText = grayed ? s_grayed : s_text,
        };
        Win32.DrawThemeTextEx(theme, item->um.hdc, Win32.MENU_BARITEM, Win32.MBI_NORMAL, text, (int)info.cch, flags, &rect, &options);
    }

    /// <summary>
    /// Paints over the light line Windows draws under the bar, after the
    /// frame painted (<c>WM_NCPAINT</c>, <c>WM_NCACTIVATE</c>).
    /// </summary>
    public static void PaintLineUnderBar(nint hwnd)
    {
        var info = new Win32.MENUBARINFO { cbSize = (uint)sizeof(Win32.MENUBARINFO) };
        if (!Win32.GetMenuBarInfo(hwnd, Win32.OBJID_MENU, 0, &info))
            return;
        Win32.RECT client;
        Win32.GetClientRect(hwnd, &client);
        Win32.MapWindowPoints(hwnd, 0, (Win32.POINT*)&client, 2);
        Win32.RECT window;
        Win32.GetWindowRect(hwnd, &window);
        client = Offset(client, -window.Left, -window.Top);
        var line = client with { Top = client.Top - 1, Bottom = client.Top };
        var dc = Win32.GetWindowDC(hwnd);
        Win32.FillRect(dc, &line, s_barBrush);
        Win32.ReleaseDC(hwnd, dc);
    }

    private static Win32.RECT Offset(Win32.RECT r, int dx, int dy) =>
        new() { Left = r.Left + dx, Top = r.Top + dy, Right = r.Right + dx, Bottom = r.Bottom + dy };

    private static uint Rgb(byte r, byte g, byte b) => r | ((uint)g << 8) | ((uint)b << 16);
}
