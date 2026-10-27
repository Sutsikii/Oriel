using Oriel.Platform;
using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public class TrayIconTests
{
    private readonly FakePlatform _platform = new();

    private OrielApp App() => new(new AppOptions { Name = "OrielTests", AssetsDirectory = @"C:\app\wwwroot" }, _platform, null);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }

    /// <summary>Runs <paramref name="act"/> on the UI thread of <paramref name="app"/>, and waits for it.</summary>
    private static Task OnUi(OrielApp app, Action act)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Dispatch(() =>
        {
            act();
            done.SetResult();
        });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShowsAnIconThatTellsItsClicksAndMenu()
    {
        var app = App();
        var run = Task.Run(() => app.Run());
        var quit = new MenuItem("Quit");
        var tray = app.AddTrayIcon(new TrayIconOptions { Tooltip = "Notes", Icon = "notes.ico", Menu = new Menu(new MenuItem("Open"), quit) });
        await WaitUntil(() => _platform.TrayIcons.Count == 1);
        var icon = Assert.Single(_platform.TrayIcons);
        Assert.Equal(new TrayIconSpec("Notes", "notes.ico"), icon.Spec);

        var clicks = 0;
        var picked = new List<MenuItem>();
        tray.Clicked += (_, _) => clicks++;
        tray.MenuItemClicked += (_, e) => picked.Add(e.Item);
        icon.Pick = menu => menu.Items[1];
        await OnUi(app, () =>
        {
            icon.Events.Clicked();
            icon.Events.MenuRequested();
        });
        Assert.Equal(1, clicks);
        Assert.Equal([quit], picked);

        // Nothing picked, or no menu: nothing raised.
        icon.Pick = _ => null;
        await OnUi(app, icon.Events.MenuRequested);
        tray.Menu = null;
        await OnUi(app, icon.Events.MenuRequested);
        Assert.Single(picked);
        Assert.Equal(2, icon.Menus.Count);

        tray.Tooltip = "Notes (3)";
        await WaitUntil(() => icon.Tooltip == "Notes (3)");
        tray.Dispose();
        await WaitUntil(() => icon.Removed);
        app.Quit();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReportsWhatItsHandlersThrow()
    {
        var app = App();
        var errors = new List<Exception>();
        app.Error += errors.Add;
        var run = Task.Run(() => app.Run());
        var tray = app.AddTrayIcon();
        tray.Clicked += (_, _) => throw new InvalidOperationException("handler bug");
        await WaitUntil(() => _platform.TrayIcons.Count == 1);
        await OnUi(app, Assert.Single(_platform.TrayIcons).Events.Clicked);
        Assert.Equal("handler bug", Assert.Single(errors).Message);
        app.Quit();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void SaysWhereThereAreNoTrayIcons()
    {
        _platform.SupportsTrayIcons = false;
        Assert.Throws<NotSupportedException>(() => App().AddTrayIcon());
    }

    [Fact]
    public async Task AsksBeforeClosingAWindowThePersonCloses()
    {
        var app = App();
        OrielWindow? window = null;
        var run = Task.Run(() => app.Run(() => window = app.OpenWindow()));
        await WaitUntil(() => _platform.Windows.Count == 1);
        var native = Assert.Single(_platform.Windows);

        var asked = 0;
        window!.Closing += (_, e) =>
        {
            asked++;
            e.Cancel = true;
            window.Hide();
        };
        var close = true;
        await OnUi(app, () => close = native.Events.CloseRequested());
        Assert.False(close);
        Assert.Equal(1, asked);
        await WaitUntil(() => !native.Visible);
        Assert.False(window.IsVisible);

        window.Show();
        await WaitUntil(() => native.Visible);
        Assert.True(window.IsVisible);
        Assert.Contains("focus", native.Placements);

        app.Quit();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
