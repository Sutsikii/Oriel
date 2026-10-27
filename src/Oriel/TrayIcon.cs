using Oriel.Platform;

namespace Oriel;

/// <summary>How <see cref="OrielApp.AddTrayIcon"/> shows an icon.</summary>
public sealed class TrayIconOptions
{
    /// <summary>What shows when the pointer rests on it. Defaults to none.</summary>
    public string? Tooltip { get; init; }

    /// <summary>The path of an .ico file. Defaults to the icon of the executable (its ApplicationIcon).</summary>
    public string? Icon { get; init; }

    /// <summary>The menu a right click opens. Defaults to none.</summary>
    public Menu? Menu { get; init; }
}

/// <summary>A menu item of a tray icon was clicked: see <see cref="TrayIcon.MenuItemClicked"/>.</summary>
public sealed class TrayMenuItemClickedEventArgs(MenuItem item) : EventArgs
{
    /// <summary>The item clicked.</summary>
    public MenuItem Item { get; } = item;
}

/// <summary>
/// An icon of the app in the notification area of the taskbar, with a
/// menu: for apps that keep running with their windows hidden. Made by
/// <see cref="OrielApp.AddTrayIcon"/>; dispose it to remove it. Its events
/// are raised on the UI thread.
/// </summary>
/// <example>
/// Closing the window hides it; the icon shows it again:
/// <code>
/// var tray = app.AddTrayIcon(new TrayIconOptions { Tooltip = "Notes", Menu = new Menu(new MenuItem("Quit")) });
/// window.Closing += (_, e) => { e.Cancel = true; window.Hide(); };
/// tray.Clicked += (_, _) => window.Show();
/// tray.MenuItemClicked += (_, e) => app.Quit();
/// </code>
/// </example>
public sealed class TrayIcon : IDisposable, IPlatformTrayIconEvents
{
    private readonly OrielApp _app;
    private IPlatformTrayIcon? _native;
    private string? _tooltip;
    private Menu? _menu;
    private bool _disposed;

    internal TrayIcon(OrielApp app, TrayIconOptions options)
    {
        _app = app;
        _tooltip = options.Tooltip;
        _menu = options.Menu;
        app.Dispatch(() =>
        {
            if (!_disposed)
                _native = app.Platform.AddTrayIcon(new TrayIconSpec(options.Tooltip, options.Icon), this);
        });
    }

    /// <summary>Raised when the icon is clicked, or chosen with the keyboard: often, to show the app's window.</summary>
    public event EventHandler? Clicked;

    /// <summary>
    /// Raised when an item of <see cref="Menu"/> is clicked. The items'
    /// <see cref="MenuItem.Clicked"/> do not run: there is no window.
    /// </summary>
    public event EventHandler<TrayMenuItemClickedEventArgs>? MenuItemClicked;

    /// <summary>What shows when the pointer rests on the icon. Any thread may set it.</summary>
    public string? Tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value;
            _app.Dispatch(() => _native?.SetTooltip(value));
        }
    }

    /// <summary>The menu a right click opens, or null for none. Any thread may set it.</summary>
    public Menu? Menu
    {
        get => Volatile.Read(ref _menu);
        set => Volatile.Write(ref _menu, value);
    }

    /// <summary>Removes the icon. Any thread may call it.</summary>
    public void Dispose()
    {
        _disposed = true;
        _app.Dispatch(() =>
        {
            _native?.Remove();
            _native = null;
        });
    }

    void IPlatformTrayIconEvents.Clicked() => Raise(() => Clicked?.Invoke(this, EventArgs.Empty));

    void IPlatformTrayIconEvents.MenuRequested()
    {
        if (Menu is not { } menu || _native is null)
            return;
        if (_native.ShowMenu(menu) is { } item)
            Raise(() => MenuItemClicked?.Invoke(this, new TrayMenuItemClickedEventArgs(item)));
    }

    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception e)
        {
            _app.ReportError(e);
        }
    }
}
