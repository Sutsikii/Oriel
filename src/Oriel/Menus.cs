namespace Oriel;

/// <summary>
/// A menu: the bar of a window (<see cref="OrielWindow.Menu"/>), or one shown
/// at a point (<see cref="OrielWindow.ShowContextMenuAsync(Menu)"/>). A menu
/// can be the bar of several windows; its items know which window they were
/// clicked in.
/// </summary>
/// <example>
/// <code>
/// window.Menu = new Menu(
///     new MenuItem("&amp;File")
///     {
///         Items =
///         [
///             new MenuItem("&amp;Open…", "Ctrl+O") { Clicked = w => Open(w) },
///             MenuItem.Separator(),
///             new MenuItem(MenuRole.CloseWindow),
///         ],
///     },
///     new MenuItem("&amp;Edit") { Items = [new(MenuRole.Undo), new(MenuRole.Redo)] });
/// </code>
/// </example>
public sealed class Menu
{
    /// <param name="items">Its items, in order. The items of a bar have submenus: <c>File</c>, <c>Edit</c>…</param>
    public Menu(params IReadOnlyList<MenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
            ArgumentNullException.ThrowIfNull(item, nameof(items));
        Items = [.. items];
    }

    /// <summary>Its items, in order.</summary>
    public IReadOnlyList<MenuItem> Items { get; }

    /// <summary>The enabled item, at any depth, whose accelerator is <paramref name="chord"/>: the first one, depth first.</summary>
    internal MenuItem? Find(KeyChord chord) => Find(Items, chord);

    private static MenuItem? Find(IReadOnlyList<MenuItem> items, KeyChord chord)
    {
        foreach (var item in items)
        {
            if (!item.Enabled)
                continue;
            if (item.Chord == chord && item.RoutesAccelerator)
                return item;
            if (Find(item.Items, chord) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>The position of the item of this menu whose mnemonic, the letter after <c>&amp;</c>, is <paramref name="letter"/>; -1 when none.</summary>
    internal int FindMnemonic(char letter)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Enabled && Items[i].Mnemonic is { } m && char.ToUpperInvariant(m) == char.ToUpperInvariant(letter))
                return i;
        }
        return -1;
    }
}

/// <summary>
/// An item of a <see cref="Menu"/>: a command, a check item, a submenu, or a
/// separator. <see cref="Text"/>, <see cref="Enabled"/> and
/// <see cref="IsChecked"/> can change at any time, from any thread: menus
/// show what they are when they open.
/// </summary>
public sealed class MenuItem
{
    private volatile string _text;
    private volatile bool _enabled = true;
    private volatile bool _checked;

    /// <param name="text">What the item shows. <c>&amp;</c> marks the letter that picks it from the keyboard, as in <c>&amp;Open</c>; <c>&amp;&amp;</c> shows an <c>&amp;</c>.</param>
    /// <param name="accelerator">
    /// The keys that run it from the window, such as <c>Ctrl+S</c>,
    /// <c>Ctrl+Shift+Z</c> or <c>F5</c>; see <see cref="Accelerator"/>.
    /// </param>
    /// <exception cref="ArgumentException">The text is empty, or the accelerator is not one.</exception>
    public MenuItem(string text, string? accelerator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _text = text;
        Accelerator = accelerator;
    }

    /// <summary>An item that does what <paramref name="role"/> does, with its text and accelerator in English; both can be changed.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="role"/> is not a role.</exception>
    public MenuItem(MenuRole role)
    {
        var (text, accelerator) = Roles.Defaults(role);
        _text = text;
        Accelerator = accelerator;
        Role = role;
    }

    private MenuItem()
    {
        _text = "";
        IsSeparator = true;
    }

    /// <summary>A line between items.</summary>
    public static MenuItem Separator() => new();

    /// <summary>
    /// What the item shows. <c>&amp;</c> marks the letter that picks it from
    /// the keyboard; <c>&amp;&amp;</c> shows an <c>&amp;</c>.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _text = value;
        }
    }

    /// <summary>
    /// The keys that run it, shown beside its text: a key with <c>Ctrl</c>,
    /// <c>Alt</c> or both, and maybe <c>Shift</c>, such as <c>Ctrl+S</c> or
    /// <c>Alt+Shift+Left</c>; or a function key, <c>F1</c> to <c>F24</c>,
    /// with or without them. Keys are letters, digits, <c>Plus</c>,
    /// <c>Minus</c>, <c>Comma</c>, <c>Period</c>, the arrows (<c>Left</c>…),
    /// <c>Home</c>, <c>End</c>, <c>PageUp</c>, <c>PageDown</c>,
    /// <c>Insert</c>, <c>Delete</c>, <c>Backspace</c>, <c>Tab</c>,
    /// <c>Enter</c>, <c>Space</c> and <c>Escape</c>. Keys without a modifier
    /// are the page's.
    /// </summary>
    /// <remarks>
    /// The page gets the keys first: when its <c>keydown</c> listener calls
    /// <c>preventDefault()</c>, the item does not run, so an editor can keep
    /// Ctrl+Z. Only the accelerators of the window's bar run items; a context
    /// menu shows them.
    /// </remarks>
    /// <exception cref="ArgumentException">It is not an accelerator.</exception>
    public string? Accelerator
    {
        get => Chord?.ToString();
        init => Chord = value is null ? null : KeyChord.Parse(value);
    }

    /// <summary>Whether it can be clicked. Defaults to true.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>Whether it shows a check mark that clicking it toggles, before <see cref="Clicked"/> runs.</summary>
    public bool IsCheckable { get; init; }

    /// <summary>Whether its check mark shows.</summary>
    public bool IsChecked
    {
        get => _checked;
        set => _checked = value;
    }

    /// <summary>What it does when there is no <see cref="Clicked"/>. Defaults to <see cref="MenuRole.None"/>.</summary>
    public MenuRole Role { get; }

    /// <summary>The items of its submenu; an item with some opens it rather than being clicked.</summary>
    public IReadOnlyList<MenuItem> Items
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var item in value)
                ArgumentNullException.ThrowIfNull(item, nameof(Items));
            field = [.. value];
        }
    } = [];

    /// <summary>
    /// Runs on the UI thread when the item is clicked, or its accelerator
    /// pressed, with the window it happened in. It replaces what its
    /// <see cref="Role"/> does. An exception goes to <see cref="OrielApp.Error"/>.
    /// </summary>
    public Action<OrielWindow>? Clicked { get; init; }

    /// <summary>Whether it is a line between items.</summary>
    public bool IsSeparator { get; }

    internal KeyChord? Chord { get; private init; }

    /// <summary>
    /// Whether its accelerator runs it from the bar. The page's editing keys
    /// do what they do in the page, even without a listener: the menu only shows them.
    /// </summary>
    internal bool RoutesAccelerator => Clicked is not null || !Roles.IsEditing(Role);

    /// <summary>The letter after its <c>&amp;</c>, or null.</summary>
    internal char? Mnemonic
    {
        get
        {
            var text = _text;
            for (var i = 0; i < text.Length - 1; i++)
            {
                if (text[i] != '&')
                    continue;
                if (text[i + 1] != '&')
                    return text[i + 1];
                i++; // && is an &
            }
            return null;
        }
    }
}

/// <summary>What an item does by itself, with the text and accelerator it has by default.</summary>
public enum MenuRole
{
    /// <summary>Nothing: the item does what <see cref="MenuItem.Clicked"/> does.</summary>
    None,

    /// <summary>Undoes the last edit of the page. Ctrl+Z.</summary>
    Undo,

    /// <summary>Redoes it. Ctrl+Y.</summary>
    Redo,

    /// <summary>Cuts the page's selection. Ctrl+X.</summary>
    Cut,

    /// <summary>Copies the page's selection. Ctrl+C.</summary>
    Copy,

    /// <summary>Pastes into the page. Ctrl+V.</summary>
    Paste,

    /// <summary>Deletes the page's selection.</summary>
    Delete,

    /// <summary>Selects everything in the page, or the field. Ctrl+A.</summary>
    SelectAll,

    /// <summary>Reloads the page. Ctrl+R.</summary>
    Reload,

    /// <summary>Opens the developer tools, when <see cref="WindowOptions.DevTools"/> allows them. Ctrl+Shift+I.</summary>
    DevTools,

    /// <summary>Opens the print dialog. Ctrl+P.</summary>
    Print,

    /// <summary>Closes the window. Ctrl+W.</summary>
    CloseWindow,

    /// <summary>Quits the app.</summary>
    Quit,
}

/// <summary>What the roles show and do.</summary>
internal static class Roles
{
    public static (string Text, string? Accelerator) Defaults(MenuRole role) => role switch
    {
        MenuRole.None => throw new ArgumentOutOfRangeException(nameof(role), "MenuRole.None is no role: give the item a text."),
        MenuRole.Undo => ("&Undo", "Ctrl+Z"),
        MenuRole.Redo => ("&Redo", "Ctrl+Y"),
        MenuRole.Cut => ("Cu&t", "Ctrl+X"),
        MenuRole.Copy => ("&Copy", "Ctrl+C"),
        MenuRole.Paste => ("&Paste", "Ctrl+V"),
        MenuRole.Delete => ("&Delete", null),
        MenuRole.SelectAll => ("Select &All", "Ctrl+A"),
        MenuRole.Reload => ("&Reload", "Ctrl+R"),
        MenuRole.DevTools => ("&Developer Tools", "Ctrl+Shift+I"),
        MenuRole.Print => ("&Print…", "Ctrl+P"),
        MenuRole.CloseWindow => ("&Close", "Ctrl+W"),
        MenuRole.Quit => ("E&xit", null),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a menu role."),
    };

    /// <summary>Whether the role edits the page, as its keys do there already.</summary>
    public static bool IsEditing(MenuRole role) => role is >= MenuRole.Undo and <= MenuRole.SelectAll;

    /// <summary>The editing command of the webview for <paramref name="role"/>, as Chromium names it.</summary>
    public static string EditCommand(MenuRole role) => role switch
    {
        MenuRole.Undo => "Undo",
        MenuRole.Redo => "Redo",
        MenuRole.Cut => "Cut",
        MenuRole.Copy => "Copy",
        MenuRole.Paste => "Paste",
        MenuRole.Delete => "Delete",
        MenuRole.SelectAll => "SelectAll",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

/// <summary>The modifiers of a <see cref="KeyChord"/>, with the bits bridge.js sends.</summary>
[Flags]
internal enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// An accelerator: a key, by its Windows virtual-key code (which is also
/// <c>KeyboardEvent.keyCode</c> in the webview), and its modifiers.
/// </summary>
internal readonly record struct KeyChord(int Key, KeyModifiers Modifiers)
{
    private static readonly (string Name, int Key)[] s_named =
    [
        ("Backspace", 0x08), ("Tab", 0x09), ("Enter", 0x0D), ("Escape", 0x1B), ("Space", 0x20),
        ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23), ("Home", 0x24),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
        ("Insert", 0x2D), ("Delete", 0x2E),
        ("Plus", 0xBB), ("Comma", 0xBC), ("Minus", 0xBD), ("Period", 0xBE),
    ];

    private static readonly (string Name, int Key)[] s_aliases =
    [
        ("Esc", 0x1B), ("Return", 0x0D), ("Del", 0x2E), ("Ins", 0x2D), ("PgUp", 0x21), ("PgDn", 0x22),
        ("ArrowLeft", 0x25), ("ArrowUp", 0x26), ("ArrowRight", 0x27), ("ArrowDown", 0x28),
    ];

    /// <summary>Whether the key is F1 to F24.</summary>
    public bool IsFunctionKey => Key is >= 0x70 and <= 0x87;

    /// <exception cref="ArgumentException"><paramref name="text"/> is not an accelerator.</exception>
    public static KeyChord Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Ctrl,
                "SHIFT" => KeyModifiers.Shift,
                "ALT" => KeyModifiers.Alt,
                _ => throw Invalid(text, $"\"{parts[i]}\" is not Ctrl, Shift or Alt"),
            };
            if ((modifiers & modifier) != 0)
                throw Invalid(text, $"{modifier} is there twice");
            modifiers |= modifier;
        }
        var key = KeyCode(parts[^1]) ?? throw Invalid(text, $"\"{parts[^1]}\" is not a key Oriel knows");
        var chord = new KeyChord(key, modifiers);
        if (!chord.IsFunctionKey && (modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
            throw Invalid(text, "keys without Ctrl or Alt are the page's, except F1 to F24");
        return chord;
    }

    private static int? KeyCode(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
            return char.ToUpperInvariant(name[0]);
        if (name.Length is 2 or 3 && name[0] is 'F' or 'f' && int.TryParse(name.AsSpan(1), out var n) && n is >= 1 and <= 24 && name[1] != '0')
            return 0x70 + n - 1;
        foreach (var (named, key) in s_named)
        {
            if (string.Equals(named, name, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        foreach (var (alias, key) in s_aliases)
        {
            if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    private static ArgumentException Invalid(string text, string why) =>
        new($"\"{text}\" is not an accelerator: {why}. Accelerators look like \"Ctrl+S\", \"Ctrl+Shift+Z\" or \"F5\".", "accelerator");

    /// <summary>How menus show it: <c>Ctrl+Shift+S</c>, modifiers in that order.</summary>
    public override string ToString()
    {
        var text = "";
        if ((Modifiers & KeyModifiers.Ctrl) != 0)
            text += "Ctrl+";
        if ((Modifiers & KeyModifiers.Shift) != 0)
            text += "Shift+";
        if ((Modifiers & KeyModifiers.Alt) != 0)
            text += "Alt+";
        return text + KeyName();
    }

    private string KeyName()
    {
        if (IsFunctionKey)
            return "F" + (Key - 0x70 + 1);
        foreach (var (name, key) in s_named)
        {
            if (key == Key)
                return name;
        }
        return ((char)Key).ToString();
    }
}
