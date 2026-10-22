using Oriel.Platform;
using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public sealed class DialogsTests : IDisposable
{
    private readonly FakePlatform _platform = new();
    private readonly OrielApp _app;
    private readonly Task<int> _run;
    private readonly OrielWindow _window;
    private readonly string _folder = Directory.CreateTempSubdirectory("oriel-dialogs-").FullName;

    public DialogsTests()
    {
        _app = new OrielApp(new AppOptions { Name = "DialogsTests", AssetsDirectory = @"C:\app\wwwroot" }, _platform, null);
        var opened = new TaskCompletionSource<OrielWindow>();
        _run = Task.Run(() => _app.Run(() => opened.SetResult(_app.OpenWindow(new WindowOptions { Title = "Decks" }))));
        _window = opened.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        // OpenWindow returns before the backend's window exists: it opens on the UI thread.
        for (var i = 0; i < 1000 && _platform.Windows.IsEmpty; i++)
            Thread.Sleep(5);
    }

    public void Dispose()
    {
        _app.Quit();
        _run.Wait(TimeSpan.FromSeconds(10));
        Directory.Delete(_folder, recursive: true);
    }

    private FakeWindow Native => Assert.Single(_platform.Windows);

    private T Shown<T>() => Assert.IsType<T>(Assert.Single(Native.Dialogs));

    [Fact]
    public async Task OpensAFileWithItsFiltersOnTheUiThread()
    {
        Native.FileDialogAnswer = _ => [@"C:\decks\a.deck"];
        var path = await _window.Dialogs.OpenFileAsync(new()
        {
            Title = "Open a deck",
            Filters = [new("Decks", "*.deck", "*.deck.json"), new("Every file", "*.*")],
            Directory = _folder,
        });

        Assert.Equal(@"C:\decks\a.deck", path);
        var request = Shown<FileDialogRequest>();
        Assert.Equal(FileDialogKind.Open, request.Kind);
        Assert.Equal("Open a deck", request.Title);
        Assert.Equal([new FileTypeSpec("Decks", "*.deck;*.deck.json"), new FileTypeSpec("Every file", "*.*")], request.Filters);
        Assert.Equal(_folder, request.Directory);
        Assert.Equal(_platform.UiThread, Native.DialogThread);
    }

    [Fact]
    public async Task ReturnsNullOrNothingWhenCanceled()
    {
        Assert.Null(await _window.Dialogs.OpenFileAsync());
        Assert.Empty(await _window.Dialogs.OpenFilesAsync());
        Assert.Null(await _window.Dialogs.SaveFileAsync());
        Assert.Null(await _window.Dialogs.PickFolderAsync());
    }

    [Fact]
    public async Task OpensSeveralFiles()
    {
        Native.FileDialogAnswer = _ => [@"C:\a.txt", @"C:\b.txt"];
        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], await _window.Dialogs.OpenFilesAsync());
        Assert.Equal(FileDialogKind.OpenMany, Shown<FileDialogRequest>().Kind);
    }

    [Theory]
    [InlineData("*.png", "png")]
    [InlineData("*.tar.gz", "tar.gz")]
    [InlineData("*.*", null)]
    [InlineData("*", null)]
    [InlineData("report-*.csv", null)]
    public async Task SavesWithTheExtensionOfTheFirstFilter(string pattern, string? extension)
    {
        Native.FileDialogAnswer = _ => [@"C:\out\image.png"];
        var path = await _window.Dialogs.SaveFileAsync(new() { FileName = "Untitled", Filters = [new("Files", pattern, "*.other")] });

        Assert.Equal(@"C:\out\image.png", path);
        var request = Shown<FileDialogRequest>();
        Assert.Equal((FileDialogKind.Save, "Untitled", extension), (request.Kind, request.FileName, request.DefaultExtension));
    }

    [Fact]
    public async Task PicksAFolder()
    {
        Native.FileDialogAnswer = _ => [_folder];
        Assert.Equal(_folder, await _window.Dialogs.PickFolderAsync(new() { Title = "Where?" }));
        var request = Shown<FileDialogRequest>();
        Assert.Equal((FileDialogKind.Folder, "Where?"), (request.Kind, request.Title));
        Assert.Empty(request.Filters);
    }

    [Fact]
    public async Task StartsInAFolderOnlyWhenItExists()
    {
        await _window.Dialogs.OpenFileAsync(new() { Directory = Path.Combine(_folder, "missing") });
        await _window.Dialogs.OpenFileAsync(new() { Directory = Path.GetRelativePath(Environment.CurrentDirectory, _folder) });

        var requests = Native.Dialogs.Cast<FileDialogRequest>().ToList();
        Assert.Null(requests[0].Directory);
        Assert.Equal(_folder, requests[1].Directory); // a relative path, made full
    }

    [Theory]
    [InlineData(MessageResult.Yes)]
    [InlineData(MessageResult.Cancel)]
    public async Task ShowsAMessageTitledAfterItsWindow(MessageResult answer)
    {
        Native.MessageAnswer = answer;
        var result = await _window.Dialogs.MessageAsync(new() { Text = "Save the changes?", Kind = MessageKind.Question, Buttons = MessageButtons.YesNoCancel });

        Assert.Equal(answer, result);
        Assert.Equal(new MessageRequest("Decks", "Save the changes?", MessageKind.Question, MessageButtons.YesNoCancel), Shown<MessageRequest>());
        Assert.Equal(_platform.UiThread, Native.DialogThread);
    }

    [Fact]
    public async Task ShowsNothingForAClosedWindow()
    {
        var closed = new TaskCompletionSource();
        _window.Closed += (_, _) => closed.SetResult();
        _window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(await _window.Dialogs.OpenFileAsync());
        Assert.Equal(MessageResult.Cancel, await _window.Dialogs.MessageAsync(new() { Text = "Gone?" }));
        Assert.Empty(Native.Dialogs);
    }

    [Fact]
    public async Task FailsWithTheBackendsError()
    {
        Native.FileDialogAnswer = _ => throw new InvalidOperationException("no dialog today");
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => _window.Dialogs.OpenFileAsync());
        Assert.Equal("no dialog today", e.Message);
    }

    [Theory]
    [InlineData("", new[] { "*.txt" })]
    [InlineData("Text", new string[0])]
    [InlineData("Text", new[] { "" })]
    [InlineData("Text", new[] { "*.txt;*.md" })]
    public void RefusesFiltersTheSystemCouldNotShow(string name, string[] patterns)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FileFilter(name, patterns));
    }
}
