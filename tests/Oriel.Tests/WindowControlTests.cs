using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public sealed class WindowControlTests : IDisposable
{
    private readonly FakePlatform _platform = new();
    private readonly OrielApp _app;
    private readonly Task<int> _run;
    private readonly string _folder = Directory.CreateTempSubdirectory("oriel-windows-").FullName;
    private readonly List<Exception> _errors = [];

    public WindowControlTests()
    {
        _app = new OrielApp(new AppOptions { Name = "WindowControlTests", AssetsDirectory = @"C:\app\wwwroot", QuitWhenLastWindowCloses = false }, _platform, null)
        {
            Placements = new PlacementStore(Path.Combine(_folder, "windows.json")),
        };
        _app.Error += e =>
        {
            lock (_errors)
                _errors.Add(e);
        };
        var started = new TaskCompletionSource();
        _run = Task.Run(() => _app.Run(started.SetResult));
        started.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _app.Quit();
        _run.Wait(TimeSpan.FromSeconds(10));
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Opens a window, and waits for its backend window.</summary>
    private (OrielWindow Window, FakeWindow Native) Open(WindowOptions? options = null)
    {
        var count = _platform.Windows.Count;
        var window = _app.OpenWindow(options);
        for (var i = 0; i < 1000 && _platform.Windows.Count == count; i++)
            Thread.Sleep(5);
        return (window, _platform.Windows.Last());
    }

    /// <summary>Waits for what was dispatched so far to run.</summary>
    private void Settle()
    {
        var done = new TaskCompletionSource();
        _app.Dispatch(done.SetResult);
        done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    private void OnUi(Action act)
    {
        _app.Dispatch(act);
        Settle();
    }

    [Fact]
    public void TheOptionsGoToTheBackend()
    {
        var (_, native) = Open(new WindowOptions { X = 40, Y = 60, Center = true, State = WindowState.Maximized, AlwaysOnTop = true, Frameless = true });
        var o = native.Options;
        Assert.Equal((40, 60, true, WindowState.Maximized, true, true, null), (o.X, o.Y, o.Center, o.State, o.AlwaysOnTop, o.Frameless, o.Placement));
    }

    [Fact]
    public void TheMethodsReachTheBackendInOrder()
    {
        var (window, native) = Open();
        window.Move(10, 20);
        window.Resize(800, 600);
        window.Center();
        window.Minimize();
        window.Maximize();
        window.Restore();
        window.IsFullScreen = true;
        window.IsAlwaysOnTop = true;
        window.Focus();
        Settle();

        Assert.Equal(
            ["bounds 10,20,,", "bounds ,,800,600", "center", "state Minimized", "state Maximized", "state Normal", "full screen True", "on top True", "focus"],
            native.Placements);
        Assert.True(window.IsFullScreen);
        Assert.True(window.IsAlwaysOnTop);
    }

    [Fact]
    public void SizesArePositive()
    {
        var (window, _) = Open();
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Resize(0, 600));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Resize(800, -1));
    }

    [Fact]
    public void MovesAndResizesRaiseTheirEvents()
    {
        var (window, native) = Open(new WindowOptions { Width = 800, Height = 600 });
        var raised = new List<string>();
        window.Moved += (_, _) => raised.Add("moved " + window.Bounds);
        window.Resized += (_, _) => raised.Add("resized");
        OnUi(() =>
        {
            native.Events.BoundsChanged(new WindowBounds(100, 50, 800, 600));
            native.Events.BoundsChanged(new WindowBounds(100, 50, 900, 600));
            native.Events.BoundsChanged(new WindowBounds(100, 50, 900, 600));
            native.Events.BoundsChanged(new WindowBounds(0, 0, 640, 480));
        });

        Assert.Equal(["moved WindowBounds { X = 100, Y = 50, Width = 800, Height = 600 }", "resized", "moved WindowBounds { X = 0, Y = 0, Width = 640, Height = 480 }", "resized"], raised);
        Assert.Equal(new WindowBounds(0, 0, 640, 480), window.Bounds);
    }

    [Fact]
    public void StateAndFocusChangesRaiseTheirEventsOnce()
    {
        var (window, native) = Open();
        var raised = new List<string>();
        window.StateChanged += (_, _) => raised.Add(window.State.ToString());
        window.FocusChanged += (_, _) => raised.Add(window.IsFocused ? "focused" : "blurred");
        OnUi(() =>
        {
            native.Events.StateChanged(WindowState.Normal); // what it was
            native.Events.StateChanged(WindowState.Maximized);
            native.Events.StateChanged(WindowState.Maximized);
            native.Events.FocusChanged(true);
            native.Events.FocusChanged(true);
            native.Events.StateChanged(WindowState.Minimized);
            native.Events.FocusChanged(false);
        });

        Assert.Equal(["Maximized", "focused", "Minimized", "blurred"], raised);
    }

    [Fact]
    public void AFailingHandlerGoesToError()
    {
        var (window, native) = Open();
        window.Moved += (_, _) => throw new InvalidOperationException("boom");
        OnUi(() => native.Events.BoundsChanged(new WindowBounds(1, 2, 3, 4)));
        Assert.Single(_errors, e => e.Message == "boom");
    }

    [Fact]
    public void APlacementIsRememberedByItsKey()
    {
        var (first, native) = Open(new WindowOptions { StateKey = "main" });
        Assert.Null(native.Options.Placement);
        native.CurrentPlacement = "10,20,810,620,1";
        first.Close();
        Settle();

        Assert.Equal("10,20,810,620,1", Open(new WindowOptions { StateKey = "main" }).Native.Options.Placement);
        Assert.Null(Open(new WindowOptions { StateKey = "settings" }).Native.Options.Placement);
        Assert.Null(Open().Native.Options.Placement);
    }

    [Fact]
    public void WindowsWithoutAKeyRememberNothing()
    {
        var (window, _) = Open();
        window.Close();
        Settle();
        Assert.False(File.Exists(_app.Placements.File));
    }

    [Fact]
    public void KeysKeepTheirOwnPlacements()
    {
        var (main, mainNative) = Open(new WindowOptions { StateKey = "main" });
        var (settings, settingsNative) = Open(new WindowOptions { StateKey = "settings" });
        mainNative.CurrentPlacement = "m";
        settingsNative.CurrentPlacement = "s";
        main.Close();
        settings.Close();
        Settle();

        Assert.Equal("m", _app.Placements.Load("main"));
        Assert.Equal("s", _app.Placements.Load("settings"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{"main": 3}""")]
    public void ABrokenFileIsAsGoodAsNone(string content)
    {
        File.WriteAllText(_app.Placements.File, content);
        var (window, native) = Open(new WindowOptions { StateKey = "main" });
        Assert.Null(native.Options.Placement);

        native.CurrentPlacement = "p";
        window.Close();
        Settle();
        Assert.Equal("p", _app.Placements.Load("main"));
    }

    [Fact]
    public void AClosedWindowIgnoresTheMethods()
    {
        var (window, native) = Open();
        window.Close();
        Settle();
        window.Maximize();
        window.Move(1, 2);
        Settle();
        Assert.Empty(native.Placements);
    }
}
