using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Oriel.Platform.MacOS.ObjC;

namespace Oriel.Platform.MacOS;

/// <summary>
/// Oriel's menus as AppKit's. The menu bar is the app's, not a window's: a
/// window's menu shows while it is the key window, after the app menu (Hide,
/// Quit) and, when the window's menu has none of its own, the Edit menu.
/// macOS menus have no mnemonics; accelerators are key equivalents, Ctrl
/// becoming Command. Main thread only.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacMenus
{
    private const nint NSEventModifierFlagShift = 1 << 17, NSEventModifierFlagOption = 1 << 19, NSEventModifierFlagCommand = 1 << 20;

    // What each item of the menus shown runs, by its tag.
    private static readonly Dictionary<nint, (MenuItem Item, Action<MenuItem> Clicked)> s_items = [];
    private static nint s_lastTag;
    private static nint s_target;

    // The context menu open, while ShowContext waits: for tests.
    private static nint s_context;

    /// <summary>The app's menu bar with <paramref name="menu"/>, whose clicks go to <paramref name="clicked"/>.</summary>
    public static nint Bar(Menu? menu, Action<MenuItem> clicked)
    {
        var bar = MacPlatform.MainMenu(withEdit: menu is null || !HasEditing(menu.Items));
        if (menu is not null)
        {
            foreach (var top in menu.Items)
            {
                var holder = Item(top, clicked);
                Send(bar, Sel("addItem:"), holder);
                Release(holder);
            }
        }
        return bar;
    }

    /// <summary>Shows <paramref name="menu"/> at a point of <paramref name="view"/>, or at the pointer, until it closes: the item clicked, or null.</summary>
    public static MenuItem? ShowContext(Menu menu, nint view, (double X, double Y)? at)
    {
        MenuItem? chosen = null;
        var popup = Submenu(menu.Items, item => chosen = item, "");
        NSPoint location;
        if (at is var (x, y))
        {
            location = new NSPoint(x, y); // WKWebView is flipped: its points are the page's
        }
        else
        {
            var window = Send(view, Sel("window"));
            location = SendPoint(view, Sel("convertPoint:fromView:"), SendPoint(window, Sel("mouseLocationOutsideOfEventStream")), 0);
        }
        s_context = popup;
        try
        {
            Send(popup, Sel("popUpMenuPositioningItem:atLocation:inView:"), 0, location, view);
        }
        finally
        {
            s_context = 0;
            Forget(popup);
        }
        return chosen;
    }

    /// <summary>Clicks the item <paramref name="text"/> of the context menu open, as a person would: for tests. Main thread.</summary>
    internal static bool ClickInContext(string text)
    {
        if (s_context == 0)
            return false;
        var count = (int)Send(s_context, Sel("numberOfItems"));
        for (var i = 0; i < count; i++)
        {
            var item = Send(s_context, Sel("itemAtIndex:"), i);
            if (s_items.TryGetValue(Send(item, Sel("tag")), out var entry) && entry.Item.Text == text)
            {
                Send(s_context, Sel("performActionForItemAtIndex:"), i);
                Send(s_context, Sel("cancelTracking"));
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Clicks the item <paramref name="text"/> of the app's menu bar, through
    /// its NSMenu as a click does: for tests. Main thread.
    /// </summary>
    internal static bool ClickInBar(string text) =>
        Click(Send(Send(Class("NSApplication"), Sel("sharedApplication")), Sel("mainMenu")), text);

    private static bool Click(nint menu, string text)
    {
        var count = (int)Send(menu, Sel("numberOfItems"));
        for (var i = 0; i < count; i++)
        {
            var item = Send(menu, Sel("itemAtIndex:"), i);
            if (s_items.TryGetValue(Send(item, Sel("tag")), out var entry) && entry.Item.Text == text)
            {
                Send(menu, Sel("performActionForItemAtIndex:"), i);
                return true;
            }
            if (Send(item, Sel("submenu")) is var submenu and not 0 && Click(submenu, text))
                return true;
        }
        return false;
    }

    /// <summary>The item of <paramref name="item"/>, with its submenu.</summary>
    private static nint Item(MenuItem item, Action<MenuItem> clicked)
    {
        if (item.IsSeparator)
            return Send(Send(Class("NSMenuItem"), Sel("separatorItem")), Sel("retain"));
        var title = Title(item.Text);
        var (key, modifiers) = item.Chord is { } chord ? KeyEquivalent(chord) : ("", 0);
        var native = Send(Send(Class("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"), Str(title), item.Items.Count > 0 ? 0 : Sel("orielMenuItem:"), Str(key));
        if (key.Length > 0)
            Send(native, Sel("setKeyEquivalentModifierMask:"), modifiers);
        Send(native, Sel("setEnabled:"), (byte)(item.Enabled ? 1 : 0));
        if (item.IsCheckable)
            Send(native, Sel("setState:"), item.IsChecked ? 1 : 0);
        if (item.Items.Count > 0)
        {
            Send(native, Sel("setSubmenu:"), Submenu(item.Items, clicked, title));
        }
        else
        {
            var tag = ++s_lastTag;
            s_items[tag] = (item, clicked);
            Send(native, Sel("setTag:"), tag);
            Send(native, Sel("setTarget:"), Target());
        }
        return native;
    }

    private static nint Submenu(IReadOnlyList<MenuItem> items, Action<MenuItem> clicked, string title)
    {
        var menu = Send(Send(Send(Class("NSMenu"), Sel("alloc")), Sel("initWithTitle:"), Str(title)), Sel("autorelease"));
        Send(menu, Sel("setAutoenablesItems:"), (byte)0); // enabled as Oriel says
        foreach (var item in items)
        {
            var native = Item(item, clicked);
            Send(menu, Sel("addItem:"), native);
            Release(native);
        }
        return menu;
    }

    /// <summary>Forgets the items of a menu no longer shown.</summary>
    internal static void Forget(nint menu)
    {
        var count = (int)Send(menu, Sel("numberOfItems"));
        for (var i = 0; i < count; i++)
        {
            var item = Send(menu, Sel("itemAtIndex:"), i);
            s_items.Remove(Send(item, Sel("tag")));
            if (Send(item, Sel("submenu")) is var submenu and not 0)
                Forget(submenu);
        }
    }

    /// <summary>The object menu items call: one for the app.</summary>
    private static nint Target()
    {
        if (s_target == 0)
        {
            var cls = DefineClass("OrielMenuTarget", [], ("orielMenuItem:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnItem, "v@:@"));
            s_target = Send(Send(cls, Sel("alloc")), Sel("init"));
        }
        return s_target;
    }

    [UnmanagedCallersOnly]
    private static void OnItem(nint self, nint sel, nint sender)
    {
        if (!s_items.TryGetValue(Send(sender, Sel("tag")), out var entry))
            return;
        try
        {
            entry.Clicked(entry.Item);
        }
        catch (Exception e)
        {
            MacPlatform.Report(e);
        }
    }

    private static bool HasEditing(IReadOnlyList<MenuItem> items) =>
        items.Any(i => (i.Role != MenuRole.None && Roles.IsEditing(i.Role)) || HasEditing(i.Items));

    /// <summary>A title without the mnemonic marks of Windows: <c>&amp;File</c> is <c>File</c>.</summary>
    internal static string Title(string text) => text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");

    /// <summary>The key equivalent of an accelerator: its key as a character, Ctrl becoming Command.</summary>
    internal static (string Key, nint Modifiers) KeyEquivalent(KeyChord chord)
    {
        var key = chord.Key switch
        {
            >= 0x41 and <= 0x5A => ((char)(chord.Key + 0x20)).ToString(), // a to z
            >= 0x30 and <= 0x39 => ((char)chord.Key).ToString(),
            >= 0x70 and <= 0x87 => ((char)(0xF704 + chord.Key - 0x70)).ToString(), // NSF1FunctionKey and on
            0x2E => ((char)0x7F).ToString(), // Delete: forward delete
            0x0D => "\r",
            _ => "",
        };
        nint modifiers = 0;
        if (chord.Modifiers.HasFlag(KeyModifiers.Ctrl))
            modifiers |= NSEventModifierFlagCommand;
        if (chord.Modifiers.HasFlag(KeyModifiers.Shift))
            modifiers |= NSEventModifierFlagShift;
        if (chord.Modifiers.HasFlag(KeyModifiers.Alt))
            modifiers |= NSEventModifierFlagOption;
        return (key, modifiers);
    }
}
