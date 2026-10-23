using Oriel.Platform;
using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public sealed class MenuTests : IDisposable
{
    private readonly FakePlatform _platform = new();
    private readonly OrielApp _app;
    private readonly Task<int> _run;
    private readonly OrielWindow _window;
    private readonly List<Exception> _errors = [];
    private readonly List<string> _clicks = [];
    private readonly MenuItem _save;
    private readonly MenuItem _wrap;
    private readonly MenuItem _disabled;

    public MenuTests()
    {
        _save = new MenuItem("&Save", "Ctrl+S") { Clicked = w => Click("save", w) };
        _wrap = new MenuItem("&Word Wrap", "Alt+Z") { IsCheckable = true, Clicked = w => Click("wrap", w) };
        _disabled = new MenuItem("&Export", "Ctrl+E") { Enabled = false, Clicked = w => Click("export", w) };
        var menu = new Menu(
            new MenuItem("&File") { Items = [_save, _disabled, MenuItem.Separator(), new MenuItem(MenuRole.CloseWindow)] },
            new MenuItem("&Edit") { Items = [new MenuItem(MenuRole.Undo), new MenuItem(MenuRole.Copy)] },
            new MenuItem("&View") { Items = [_wrap, new MenuItem(MenuRole.DevTools)] });

        _app = new OrielApp(new AppOptions { Name = "MenuTests", AssetsDirectory = @"C:\app\wwwroot", QuitWhenLastWindowCloses = false }, _platform, null);
        _app.Error += e =>
        {
            lock (_errors)
                _errors.Add(e);
        };
        var opened = new TaskCompletionSource<OrielWindow>();
        _run = Task.Run(() => _app.Run(() => opened.SetResult(_app.OpenWindow(new WindowOptions { Title = "Notes", Menu = menu }))));
        _window = opened.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        for (var i = 0; i < 1000 && _platform.Windows.IsEmpty; i++)
            Thread.Sleep(5);
    }

    public void Dispose()
    {
        _app.Quit();
        _run.Wait(TimeSpan.FromSeconds(10));
    }

    private FakeWindow Native => Assert.Single(_platform.Windows);

    private void Click(string what, OrielWindow window)
    {
        Assert.Same(_window, window);
        Assert.Equal(_platform.UiThread, Environment.CurrentManagedThreadId);
        lock (_clicks)
            _clicks.Add(what);
    }

    /// <summary>Runs <paramref name="act"/> on the UI thread, and waits for what it dispatched in turn.</summary>
    private T OnUi<T>(Func<T> act)
    {
        var done = new TaskCompletionSource<T>();
        _app.Dispatch(() => done.SetResult(act()));
        var result = done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        var drained = new TaskCompletionSource();
        _app.Dispatch(drained.SetResult);
        drained.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        return result;
    }

    private void OnUi(Action act) => OnUi(() =>
    {
        act();
        return 0;
    });

    /// <summary>The page left the key <paramref name="code"/> alone, with modifiers Ctrl 1, Shift 2, Alt 4.</summary>
    private void PageKey(int code, int modifiers) =>
        OnUi(() => Native.Events.MessageReceived("https://app.oriel/", $$"""{"t":"key","k":"p","c":{{code}},"m":{{modifiers}}}"""));

    [Theory]
    [InlineData("Ctrl+S", "Ctrl+S")]
    [InlineData("ctrl+shift+z", "Ctrl+Shift+Z")]
    [InlineData("Shift+Control+Z", "Ctrl+Shift+Z")]
    [InlineData("Alt+F4", "Alt+F4")]
    [InlineData("F5", "F5")]
    [InlineData("Shift+F10", "Shift+F10")]
    [InlineData("Ctrl+Plus", "Ctrl+Plus")]
    [InlineData("Ctrl + 1", "Ctrl+1")]
    [InlineData("Alt+Shift+Left", "Shift+Alt+Left")]
    [InlineData("Ctrl+Del", "Ctrl+Delete")]
    [InlineData("Ctrl+Esc", "Ctrl+Escape")]
    public void ReadsAccelerators(string text, string shown) => Assert.Equal(shown, new MenuItem("x", text).Accelerator);

    [Theory]
    [InlineData("S")] // the page's
    [InlineData("Shift+S")]
    [InlineData("Delete")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+S")]
    [InlineData("Win+S")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Semicolon")]
    [InlineData("")]
    public void RefusesWhatIsNoAccelerator(string text) =>
        Assert.Throws<ArgumentException>(() => new MenuItem("x", text));

    [Fact]
    public void RolesHaveTheirTextAndAccelerator()
    {
        var copy = new MenuItem(MenuRole.Copy);
        Assert.Equal(("&Copy", "Ctrl+C", MenuRole.Copy), (copy.Text, copy.Accelerator, copy.Role));
        var renamed = new MenuItem(MenuRole.Copy) { Text = "&Copier", Accelerator = null };
        Assert.Equal(("&Copier", null), (renamed.Text, renamed.Accelerator));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MenuItem(MenuRole.None));
        foreach (var role in Enum.GetValues<MenuRole>().Where(r => r != MenuRole.None))
            Assert.NotEmpty(new MenuItem(role).Text);
    }

    [Fact]
    public void TheBarGoesToTheWindowAndChanges()
    {
        Assert.Same(_window.Menu, Native.Menu);
        var other = new Menu(new MenuItem("&Help") { Items = [new MenuItem("&About")] });
        _window.Menu = other;
        OnUi(() => { });
        Assert.Same(other, Native.Menu);
        _window.Menu = null;
        OnUi(() => { });
        Assert.Null(Native.Menu);
    }

    [Fact]
    public void AClickRunsTheItemOnTheUiThread()
    {
        OnUi(() => Native.Events.MenuItemClicked(_save));
        Assert.Equal(["save"], _clicks);
    }

    [Fact]
    public void ACheckItemTogglesBeforeItRuns()
    {
        var seen = new List<bool>();
        MenuItem? grid = null;
        grid = new MenuItem("&Grid") { IsCheckable = true, Clicked = _ => seen.Add(grid!.IsChecked) };
        OnUi(() => Native.Events.MenuItemClicked(grid));
        OnUi(() => Native.Events.MenuItemClicked(grid));
        Assert.Equal([true, false], seen);
    }

    [Fact]
    public void KeysThePageLeftAloneRunTheItemsOfTheBar()
    {
        PageKey('S', 1);
        PageKey('Z', 4);
        Assert.Equal(["save", "wrap"], _clicks);
        Assert.True(_wrap.IsChecked);
    }

    [Fact]
    public void OtherKeysRunNothing()
    {
        PageKey('S', 3); // Ctrl+Shift+S
        PageKey('E', 1); // disabled
        PageKey('Q', 1);
        Assert.Empty(_clicks);
    }

    [Fact]
    public void TheEditingKeysStayThePages()
    {
        // The page does Ctrl+C itself: running Copy again would copy twice.
        PageKey('C', 1);
        Assert.Empty(Native.EditCommands);
        Assert.False(OnUi(() => Native.Events.IsAccelerator(new KeyChord('C', KeyModifiers.Ctrl))));
    }

    [Fact]
    public void AltAndAMnemonicOpenTheMenuOfTheBar()
    {
        PageKey('E', 4);
        PageKey('X', 4); // no menu of that letter
        PageKey(0x79, 0); // F10
        Assert.Equal(['E', '\0'], Native.BarActivations);
    }

    [Fact]
    public void TheWebviewKeepsOffTheKeysOfTheBar()
    {
        Assert.True(OnUi(() => Native.Events.IsAccelerator(new KeyChord('S', KeyModifiers.Ctrl))));
        Assert.True(OnUi(() => Native.Events.IsAccelerator(new KeyChord('F', KeyModifiers.Alt))));
        Assert.True(OnUi(() => Native.Events.IsAccelerator(new KeyChord('I', KeyModifiers.Ctrl | KeyModifiers.Shift))));
        Assert.False(OnUi(() => Native.Events.IsAccelerator(new KeyChord('P', KeyModifiers.Ctrl))));
        Assert.False(OnUi(() => Native.Events.IsAccelerator(new KeyChord('E', KeyModifiers.Ctrl)))); // disabled
    }

    [Fact]
    public void NoBarNoKeys()
    {
        _window.Menu = null;
        PageKey('S', 1);
        PageKey('F', 4);
        Assert.Empty(_clicks);
        Assert.Empty(Native.BarActivations);
    }

    [Fact]
    public void RolesDoWhatTheySay()
    {
        OnUi(() =>
        {
            Native.Events.MenuItemClicked(new MenuItem(MenuRole.Paste));
            Native.Events.MenuItemClicked(new MenuItem(MenuRole.SelectAll));
            Native.Events.MenuItemClicked(new MenuItem(MenuRole.DevTools));
            Native.Events.MenuItemClicked(new MenuItem(MenuRole.Reload));
            Native.Events.MenuItemClicked(new MenuItem(MenuRole.Print));
        });
        Assert.Equal(["Paste", "SelectAll"], Native.EditCommands);
        Assert.Equal(1, Native.DevToolsOpened);
        Assert.Equal(1, Native.Reloads);
        Assert.Equal([PrintDialog.Browser], Native.PrintDialogs);
    }

    [Fact]
    public void AHandlerReplacesTheRole()
    {
        var item = new MenuItem(MenuRole.Copy) { Clicked = w => Click("copy", w) };
        OnUi(() => Native.Events.MenuItemClicked(item));
        Assert.Equal(["copy"], _clicks);
        Assert.Empty(Native.EditCommands);
    }

    [Fact]
    public void CloseWindowClosesIt()
    {
        PageKey('W', 1);
        Assert.True(_window.IsClosed);
    }

    [Fact]
    public void AFailingHandlerGoesToError()
    {
        var item = new MenuItem("&Boom") { Clicked = _ => throw new InvalidOperationException("boom") };
        OnUi(() => Native.Events.MenuItemClicked(item));
        Assert.Single(_errors, e => e.Message == "boom");
    }

    [Fact]
    public async Task AContextMenuShowsAtAPointAndRunsTheItemClicked()
    {
        var copy = new MenuItem("&Copy link") { Clicked = w => Click("link", w) };
        var menu = new Menu(copy, new MenuItem("&Open"));
        Native.ContextMenuAnswer = m => m.Items[0];

        Assert.Same(copy, await _window.ShowContextMenuAsync(menu, 12.5, 40));
        Assert.Equal(["link"], _clicks);
        Assert.Equal((menu, new PagePoint(12.5, 40)), Assert.Single(Native.ContextMenus));
    }

    [Fact]
    public async Task AContextMenuClosedWithoutAClickReturnsNull()
    {
        Assert.Null(await _window.ShowContextMenuAsync(new Menu(new MenuItem("&Open"))));
        Assert.Null(Assert.Single(Native.ContextMenus).At); // at the mouse pointer
    }

    [Fact]
    public async Task AClosedWindowShowsNoContextMenu()
    {
        _window.Close();
        OnUi(() => { });
        Assert.Null(await _window.ShowContextMenuAsync(new Menu(new MenuItem("&Open"))));
        Assert.Empty(Native.ContextMenus);
    }

    [Fact]
    public void ItemsCanChangeFromAnyThread()
    {
        _save.Text = "&Save All";
        _save.Enabled = false;
        PageKey('S', 1);
        Assert.Empty(_clicks);
        Assert.Throws<ArgumentException>(() => _save.Text = " ");
    }
}
