using System.Collections.Concurrent;
using Oriel.Platform;

namespace Oriel.Tests.Fakes;

/// <summary>A backend without windows: its event loop runs posted actions, and its windows record what they get.</summary>
internal sealed class FakePlatform : IPlatform
{
    private readonly BlockingCollection<Action> _queue = new();
    private volatile bool _quit;

    public ConcurrentQueue<FakeWindow> Windows { get; } = new();

    public int? UiThread { get; private set; }

    public void Run(Action started)
    {
        UiThread = Environment.CurrentManagedThreadId;
        started();
        while (!_quit)
        {
            if (_queue.TryTake(out var action, TimeSpan.FromSeconds(10)))
                action();
            else
                throw new TimeoutException("the app did not quit");
        }
    }

    public void Post(Action action) => _queue.Add(action);

    public ConcurrentQueue<Uri> OpenedExternally { get; } = new();

    public void OpenExternally(Uri uri) => OpenedExternally.Enqueue(uri);

    public void Quit()
    {
        _quit = true;
        _queue.Add(() => { });
    }

    public IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        var window = new FakeWindow(options, events);
        Windows.Enqueue(window);
        return window;
    }

    public bool SupportsNotifications { get; set; } = true;

    public ConcurrentQueue<(string AppId, string AppName, string Xml, Action<string> Activated)> Notifications { get; } = new();

    public void ShowNotification(string appId, string appName, string xml, Action<string> activated) =>
        Notifications.Enqueue((appId, appName, xml, activated));

    public bool SupportsTrayIcons { get; set; } = true;

    public ConcurrentQueue<FakeTrayIcon> TrayIcons { get; } = new();

    public IPlatformTrayIcon AddTrayIcon(TrayIconSpec spec, IPlatformTrayIconEvents events)
    {
        var icon = new FakeTrayIcon(spec, events);
        TrayIcons.Enqueue(icon);
        return icon;
    }
}

/// <summary>A tray icon that records what it gets; its menu picks <see cref="Pick"/>.</summary>
internal sealed class FakeTrayIcon(TrayIconSpec spec, IPlatformTrayIconEvents events) : IPlatformTrayIcon
{
    public TrayIconSpec Spec { get; } = spec;

    public IPlatformTrayIconEvents Events { get; } = events;

    public string? Tooltip { get; private set; } = spec.Tooltip;

    public Func<Menu, MenuItem?> Pick { get; set; } = _ => null;

    public ConcurrentQueue<Menu> Menus { get; } = new();

    public bool Removed { get; private set; }

    public void SetTooltip(string? tooltip) => Tooltip = tooltip;

    public MenuItem? ShowMenu(Menu menu)
    {
        Menus.Enqueue(menu);
        return Pick(menu);
    }

    public void Remove() => Removed = true;
}

internal sealed class FakeWindow(PlatformWindowOptions options, IPlatformWindowEvents events) : IPlatformWindow
{
    public PlatformWindowOptions Options { get; } = options;

    public IPlatformWindowEvents Events { get; } = events;

    public ConcurrentQueue<string> Posted { get; } = new();

    public string Title { get; private set; } = options.Title;

    public void PostMessage(string message) => Posted.Enqueue(message);

    public void SetTitle(string title) => Title = title;

    public void OpenDevTools() => DevToolsOpened++;

    public void Close() => Events.Closed();

    public bool Visible { get; private set; } = true;

    public void SetVisible(bool visible) => Visible = visible;

    public int Reloads { get; private set; }

    public int Recreations { get; private set; }

    public string? ErrorPage { get; private set; }

    public void Reload() => Reloads++;

    public void Recreate() => Recreations++;

    public void ShowErrorPage(string html) => ErrorPage = html;

    /// <summary>The dialogs shown, in order.</summary>
    public ConcurrentQueue<object> Dialogs { get; } = new();

    /// <summary>What the next file dialogs answer; canceled by default.</summary>
    public Func<FileDialogRequest, IReadOnlyList<string>?> FileDialogAnswer { get; set; } = _ => null;

    /// <summary>What the next message boxes answer.</summary>
    public MessageResult MessageAnswer { get; set; } = MessageResult.Ok;

    /// <summary>The thread the last dialog showed on.</summary>
    public int? DialogThread { get; private set; }

    public IReadOnlyList<string>? ShowFileDialog(FileDialogRequest request)
    {
        DialogThread = Environment.CurrentManagedThreadId;
        Dialogs.Enqueue(request);
        return FileDialogAnswer(request);
    }

    public ConcurrentQueue<PrintDialog> PrintDialogs { get; } = new();

    /// <summary>The PDFs asked for, with how to complete them.</summary>
    public ConcurrentQueue<(string Path, PdfOptions? Options, Action<bool> Done)> Pdfs { get; } = new();

    public void ShowPrintDialog(PrintDialog dialog) => PrintDialogs.Enqueue(dialog);

    public void PrintToPdf(string path, PdfOptions? options, Action<bool> done) => Pdfs.Enqueue((path, options, done));

    /// <summary>The menu of the bar, as the backend last got it.</summary>
    public Menu? Menu { get; private set; } = options.Menu;

    public void SetMenu(Menu? menu) => Menu = menu;

    /// <summary>The context menus shown, in order, with where.</summary>
    public ConcurrentQueue<(Menu Menu, PagePoint? At)> ContextMenus { get; } = new();

    /// <summary>What the next context menus answer: the item clicked. None by default.</summary>
    public Func<Menu, MenuItem?> ContextMenuAnswer { get; set; } = _ => null;

    public MenuItem? ShowContextMenu(Menu menu, PagePoint? at)
    {
        ContextMenus.Enqueue((menu, at));
        return ContextMenuAnswer(menu);
    }

    /// <summary>The menus of the bar opened from the keyboard, by mnemonic.</summary>
    public ConcurrentQueue<char> BarActivations { get; } = new();

    public void ActivateMenuBar(char mnemonic) => BarActivations.Enqueue(mnemonic);

    /// <summary>What the window was asked to do with its placement, in order, such as <c>"bounds 10,20,,"</c> or <c>"state Maximized"</c>.</summary>
    public ConcurrentQueue<string> Placements { get; } = new();

    public void SetBounds(int? x, int? y, int? width, int? height) => Placements.Enqueue($"bounds {x},{y},{width},{height}");

    public void Center() => Placements.Enqueue("center");

    public void SetState(WindowState state) => Placements.Enqueue($"state {state}");

    public void SetFullScreen(bool fullScreen) => Placements.Enqueue($"full screen {fullScreen}");

    public void SetAlwaysOnTop(bool onTop) => Placements.Enqueue($"on top {onTop}");

    public void Focus() => Placements.Enqueue("focus");

    /// <summary>What <see cref="Placement"/> answers, as the app closes the window.</summary>
    public string CurrentPlacement { get; set; } = "fake placement";

    public string Placement() => CurrentPlacement;

    public ConcurrentQueue<string> EditCommands { get; } = new();

    public void RunEditCommand(string command) => EditCommands.Enqueue(command);

    public int DevToolsOpened { get; private set; }

    public MessageResult ShowMessage(MessageRequest request)
    {
        DialogThread = Environment.CurrentManagedThreadId;
        Dialogs.Enqueue(request);
        return MessageAnswer;
    }
}
