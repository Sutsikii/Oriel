using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Oriel.Platform.Linux;

/// <summary>
/// Oriel's menus as GTK's: a GtkMenuBar for the window, GtkMenus for
/// context menus. Labels keep their mnemonics (<c>&amp;File</c> is
/// <c>_File</c>) and show their accelerators; the page sees the keys first,
/// and Oriel routes them, so GTK binds none. UI thread only.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class GtkMenus
{
    // The items of the menus shown, by their widgets: what a click runs.
    private static readonly Dictionary<nint, (MenuItem Item, Action<MenuItem> Clicked)> s_items = [];

    // The context menu open, while ShowContext waits: for tests.
    private static nint s_context;

    /// <summary>A menu bar of <paramref name="menu"/>; <paramref name="clicked"/> gets the items clicked.</summary>
    public static nint Bar(Menu menu, Action<MenuItem> clicked)
    {
        var bar = gtk_menu_bar_new();
        Fill(bar, menu.Items, clicked);
        Gtk.gtk_widget_show_all(bar);
        return bar;
    }

    /// <summary>Forgets the items of <paramref name="shell"/> and destroys it.</summary>
    public static void Destroy(nint shell)
    {
        foreach (var widget in s_items.Keys.Where(w => IsInside(w, shell)).ToList())
            s_items.Remove(widget);
        Gtk.gtk_widget_destroy(shell);
    }

    /// <summary>
    /// Shows <paramref name="menu"/> at a point of <paramref name="widget"/>,
    /// or at the pointer, and waits until it closes: the item clicked, or null.
    /// </summary>
    public static MenuItem? ShowContext(Menu menu, nint widget, (int X, int Y)? at)
    {
        MenuItem? chosen = null;
        var popup = gtk_menu_new();
        Fill(popup, menu.Items, item => chosen = item);
        Gtk.gtk_widget_show_all(popup);
        gtk_menu_attach_to_widget(popup, widget, 0);
        var loop = g_main_loop_new(0, 0);
        Gtk.g_signal_connect_data(popup, "deactivate", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnDeactivate, loop, 0, 0);
        s_context = popup;
        try
        {
            if (at is var (x, y))
            {
                var rect = new GdkRectangle { X = x, Y = y, Width = 1, Height = 1 };
                gtk_menu_popup_at_rect(popup, gtk_widget_get_window(widget), &rect, 1 /* NORTH_WEST */, 1, 0);
            }
            else
            {
                gtk_menu_popup_at_pointer(popup, 0);
            }
            g_main_loop_run(loop);
        }
        finally
        {
            s_context = 0;
            g_main_loop_unref(loop);
            Destroy(popup);
        }
        return chosen;
    }

    /// <summary>
    /// Clicks the item <paramref name="text"/> of the context menu open, as
    /// a person would: for tests. UI thread.
    /// </summary>
    /// <returns>Whether a context menu with such an item was open.</returns>
    internal static bool ClickInContext(string text)
    {
        if (s_context == 0)
            return false;
        foreach (var (widget, (item, _)) in s_items)
        {
            if (item.Text == text && IsInside(widget, s_context))
            {
                gtk_menu_shell_activate_item(s_context, widget, 1);
                return true;
            }
        }
        return false;
    }

    /// <summary>Clicks the item <paramref name="text"/> of a menu bar: for tests. UI thread.</summary>
    internal static bool ClickInBar(nint bar, string text)
    {
        foreach (var (widget, (item, _)) in s_items)
        {
            if (item.Text == text && IsInside(widget, bar))
            {
                gtk_menu_item_activate(widget);
                return true;
            }
        }
        return false;
    }

    /// <summary>Opens the menu of the bar whose mnemonic is <paramref name="mnemonic"/>, as Alt and its letter do.</summary>
    public static void Open(nint bar, Menu menu, char mnemonic)
    {
        var index = menu.FindMnemonic(mnemonic);
        if (index < 0)
            return;
        var children = gtk_container_get_children(bar);
        try
        {
            var node = (GList*)children;
            for (var i = 0; node is not null && i < index; i++)
                node = node->Next;
            if (node is not null)
                gtk_menu_shell_select_item(bar, node->Data);
        }
        finally
        {
            g_list_free(children);
        }
    }

    private static void Fill(nint shell, IReadOnlyList<MenuItem> items, Action<MenuItem> clicked)
    {
        foreach (var item in items)
        {
            nint widget;
            if (item.IsSeparator)
            {
                widget = gtk_separator_menu_item_new();
            }
            else
            {
                var label = GtkLabels.Label(item.Text);
                widget = item.IsCheckable ? gtk_check_menu_item_new_with_mnemonic(label) : gtk_menu_item_new_with_mnemonic(label);
                if (item.IsCheckable)
                    gtk_check_menu_item_set_active(widget, item.IsChecked ? 1 : 0); // before the signal: setting it activates
                if (!item.Enabled)
                    Gtk.gtk_widget_set_sensitive(widget, 0);
                if (item.Chord is { } chord && GtkLabels.Keyval(chord.Key) is { } key)
                    gtk_accel_label_set_accel(gtk_bin_get_child(widget), key, Modifiers(chord.Modifiers));
                if (item.Items.Count > 0)
                {
                    var submenu = gtk_menu_new();
                    Fill(submenu, item.Items, clicked);
                    gtk_menu_item_set_submenu(widget, submenu);
                }
                else
                {
                    s_items[widget] = (item, clicked);
                    Gtk.g_signal_connect_data(widget, "activate", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnActivate, 0, 0, 0);
                }
            }
            gtk_menu_shell_append(shell, widget);
        }
    }

    private static uint Modifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? 1u : 0) | (modifiers.HasFlag(KeyModifiers.Ctrl) ? 4u : 0) | (modifiers.HasFlag(KeyModifiers.Alt) ? 8u : 0);

    /// <summary>Whether <paramref name="widget"/> is in <paramref name="shell"/>, or in one of its submenus.</summary>
    private static bool IsInside(nint widget, nint shell)
    {
        for (var w = widget; w != 0;)
        {
            if (w == shell)
                return true;
            // Up a menu to the item it is the submenu of.
            var parent = gtk_widget_get_parent(w);
            w = parent != 0 && g_type_check_instance_is_a(parent, gtk_menu_get_type()) != 0 && gtk_menu_get_attach_widget(parent) is var attached and not 0 && parent != shell
                ? attached
                : parent;
        }
        return false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnActivate(nint widget, nint data)
    {
        if (!s_items.TryGetValue(widget, out var entry))
            return;
        try
        {
            entry.Clicked(entry.Item);
        }
        catch (Exception e)
        {
            LinuxPlatform.Report(e);
        }
    }

    /// <summary>
    /// The context menu closed: the loop ends once GTK is idle, after the
    /// item clicked, which comes after this, has run.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDeactivate(nint menu, nint loop) =>
        g_idle_add((nint)(delegate* unmanaged[Cdecl]<nint, int>)&QuitLoop, loop);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int QuitLoop(nint loop)
    {
        g_main_loop_quit(loop);
        return 0; // G_SOURCE_REMOVE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkRectangle
    {
        public int X, Y, Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GList
    {
        public nint Data;
        public GList* Next;
        public GList* Prev;
    }

    private const string Gtk3 = "libgtk-3.so.0", GLib = "libglib-2.0.so.0", GObject = "libgobject-2.0.so.0";

    [LibraryImport(Gtk3)] private static partial nint gtk_menu_bar_new();
    [LibraryImport(Gtk3)] private static partial nint gtk_menu_new();
    [LibraryImport(Gtk3)] private static partial nint gtk_menu_get_type();
    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)] private static partial nint gtk_menu_item_new_with_mnemonic(string label);
    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)] private static partial nint gtk_check_menu_item_new_with_mnemonic(string label);
    [LibraryImport(Gtk3)] private static partial nint gtk_separator_menu_item_new();
    [LibraryImport(Gtk3)] private static partial void gtk_check_menu_item_set_active(nint item, int active);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_item_set_submenu(nint item, nint submenu);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_item_activate(nint item);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_shell_append(nint shell, nint item);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_shell_activate_item(nint shell, nint item, int forceDeactivate);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_shell_select_item(nint shell, nint item);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_attach_to_widget(nint menu, nint widget, nint detacher);
    [LibraryImport(Gtk3)] private static partial nint gtk_menu_get_attach_widget(nint menu);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_popup_at_pointer(nint menu, nint triggerEvent);
    [LibraryImport(Gtk3)] private static partial void gtk_menu_popup_at_rect(nint menu, nint window, GdkRectangle* rect, int rectAnchor, int menuAnchor, nint triggerEvent);
    [LibraryImport(Gtk3)] private static partial void gtk_accel_label_set_accel(nint label, uint key, uint modifiers);
    [LibraryImport(Gtk3)] private static partial nint gtk_bin_get_child(nint bin);
    [LibraryImport(Gtk3)] private static partial nint gtk_widget_get_parent(nint widget);
    [LibraryImport(Gtk3)] private static partial nint gtk_widget_get_window(nint widget);
    [LibraryImport(Gtk3)] private static partial nint gtk_container_get_children(nint container);
    [LibraryImport(GObject)] private static partial int g_type_check_instance_is_a(nint instance, nint type);
    [LibraryImport(GLib)] private static partial nint g_main_loop_new(nint context, int running);
    [LibraryImport(GLib)] private static partial void g_main_loop_run(nint loop);
    [LibraryImport(GLib)] private static partial void g_main_loop_quit(nint loop);
    [LibraryImport(GLib)] private static partial void g_main_loop_unref(nint loop);
    [LibraryImport(GLib)] private static partial uint g_idle_add(nint function, nint data);
    [LibraryImport(GLib)] private static partial void g_list_free(nint list);
}

/// <summary>How Oriel's menu texts and keys read in GTK: pure, for every system's tests.</summary>
internal static class GtkLabels
{
    /// <summary>GTK's mnemonic label of <paramref name="text"/>: <c>&amp;</c> before the letter becomes <c>_</c>.</summary>
    internal static string Label(string text)
    {
        var label = new System.Text.StringBuilder(text.Length + 2);
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '&' when i + 1 < text.Length && text[i + 1] == '&':
                    label.Append('&');
                    i++;
                    break;
                case '&':
                    label.Append('_');
                    break;
                case '_':
                    label.Append("__");
                    break;
                default:
                    label.Append(text[i]);
                    break;
            }
        }
        return label.ToString();
    }

    /// <summary>The GDK keyval of a Windows virtual-key code, for the accelerators a label shows; null for keys it does not show.</summary>
    internal static uint? Keyval(int key) => key switch
    {
        >= 0x41 and <= 0x5A => (uint)(key + 0x20), // A to Z: their lower case
        >= 0x30 and <= 0x39 => (uint)key, // 0 to 9
        >= 0x70 and <= 0x87 => (uint)(0xFFBE + key - 0x70), // F1 to F24
        0x2E => 0xFFFF, // Delete
        0x0D => 0xFF0D, // Enter
        0x20 => 0x20, // Space
        _ => null,
    };

}
