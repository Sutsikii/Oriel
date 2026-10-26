using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public sealed class FilesDroppedTests : IDisposable
{
    private readonly FakePlatform _platform = new();
    private readonly OrielApp _app;
    private readonly Task<int> _run;
    private readonly OrielWindow _window;
    private readonly List<Exception> _errors = [];
    private readonly List<(FilesDroppedEventArgs Drop, int Thread)> _drops = [];

    public FilesDroppedTests()
    {
        _app = new OrielApp(new AppOptions { Name = "FilesDroppedTests", AssetsDirectory = @"C:\app\wwwroot", QuitWhenLastWindowCloses = false }, _platform, null);
        _app.Error += e =>
        {
            if (e is FileNotFoundException)
                return; // the fake app has no files to show
            lock (_errors)
                _errors.Add(e);
        };
        var opened = new TaskCompletionSource<OrielWindow>();
        _run = Task.Run(() => _app.Run(() =>
        {
            var window = _app.OpenWindow(new WindowOptions());
            window.FilesDropped += (_, e) =>
            {
                lock (_drops)
                    _drops.Add((e, Environment.CurrentManagedThreadId));
            };
            opened.SetResult(window);
        }));
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

    /// <summary>The page sends <paramref name="message"/> with <paramref name="files"/>, from <paramref name="source"/>; waits for what it dispatched.</summary>
    private void Receive(string message, IReadOnlyList<string>? files, string source = "https://app.oriel/")
    {
        var done = new TaskCompletionSource();
        _app.Dispatch(() =>
        {
            Native.Events.MessageReceived(source, message, files);
            _app.Dispatch(done.SetResult);
        });
        done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        var drained = new TaskCompletionSource();
        _app.Dispatch(drained.SetResult);
        drained.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    private void Hello() => Receive("""{"t":"hello","k":"p"}""", null);

    [Fact]
    public void TheAppAndThePageGetThePathsOfDroppedFiles()
    {
        Hello();
        Receive("""{"t":"drop","k":"p","x":12.5,"y":34}""", [@"C:\docs\a.txt", @"C:\docs\b c.png"]);

        var (drop, thread) = Assert.Single(_drops);
        Assert.Equal([@"C:\docs\a.txt", @"C:\docs\b c.png"], drop.Paths);
        Assert.Equal((12.5, 34.0), (drop.X, drop.Y));
        Assert.Equal(_platform.UiThread, thread);
        Assert.True(Native.Posted.TryDequeue(out var message));
        Assert.Equal("""{"t":"event","n":"oriel:filesDropped","v":{"paths":["C:\\docs\\a.txt","C:\\docs\\b c.png"],"x":12.5,"y":34}}""", message);
        Assert.Empty(_errors);
    }

    [Fact]
    public void IgnoresDropsWithoutFilesOrPositions()
    {
        Hello();
        Receive("""{"t":"drop","k":"p","x":1,"y":2}""", null); // an older runtime sends no files
        Receive("""{"t":"drop","k":"p","x":1,"y":2}""", []);
        Receive("""{"t":"drop","k":"p","x":"1","y":2}""", [@"C:\a.txt"]);
        Receive("""{"t":"call","id":1,"k":"p","m":"Nothing.Here","a":[]}""", [@"C:\a.txt"]); // files only come with drops
        Assert.Empty(_drops);
    }

    [Fact]
    public void IgnoresDropsOfPagesOfOtherOrigins()
    {
        Receive("""{"t":"drop","k":"p","x":1,"y":2}""", [@"C:\a.txt"], source: "https://example.com/");
        Assert.Empty(_drops);
        Assert.True(Native.Posted.IsEmpty);
    }

    [Fact]
    public void AFailingHandlerIsReportedAndThePageStillGetsTheFiles()
    {
        _window.FilesDropped += (_, _) => throw new InvalidOperationException("handler failed");
        Hello();
        Receive("""{"t":"drop","k":"p","x":1,"y":2}""", [@"C:\a.txt"]);

        Assert.Single(_drops);
        Assert.Equal("handler failed", Assert.Single(_errors).Message);
        Assert.True(Native.Posted.TryDequeue(out var message));
        Assert.Contains("oriel:filesDropped", message);
    }
}
