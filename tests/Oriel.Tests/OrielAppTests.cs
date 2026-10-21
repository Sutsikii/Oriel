using System.Text.Json;
using Oriel.Generated;
using Oriel.Platform;
using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public class OrielAppTests
{
    /// <summary>A service, with the dispatcher the generator would write for it.</summary>
    public sealed class Greeter
    {
        public Event<string> Greeted { get; } = new();

        public string Greet(string name)
        {
            Greeted.Emit(name);
            return "Hello, " + name;
        }
    }

    private sealed class GreeterDispatcher(Greeter greeter) : ServiceDispatcher("Greeter")
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            if (method != "Greet")
                throw new MethodNotFoundException(Name, method);
            var result = greeter.Greet(Arguments.Read(args, 0, "name", Json.ReadString));
            return ValueTask.FromResult<ValueWriter?>(w => Json.Write(w, result));
        }

        public override void Attach(IEventSink sink) => greeter.Greeted.Attach("Greeter.Greeted", Json.Write, sink);
    }

    static OrielAppTests()
    {
        ServiceRegistry.Register<Greeter>(g => new GreeterDispatcher(g));
    }

    private readonly FakePlatform _platform = new();

    private OrielApp App(string? devUrl = null, string assets = @"C:\app\wwwroot") =>
        new(new AppOptions { Name = "OrielTests", AssetsDirectory = assets }, _platform, devUrl);

    /// <summary>Runs <paramref name="app"/> on its UI thread, calling <paramref name="ready"/> there.</summary>
    private static Task<int> RunAsync(OrielApp app, Action ready) =>
        Task.Run(() => app.Run(ready)).WaitAsync(TimeSpan.FromSeconds(10));

    private FakeWindow OnlyWindow() => Assert.Single(_platform.Windows);

    private static async Task<string> NextPosted(FakeWindow window)
    {
        for (var i = 0; i < 400; i++)
        {
            if (window.Posted.TryDequeue(out var message))
                return message;
            await Task.Delay(5);
        }
        throw new TimeoutException("nothing was posted to the page");
    }

    /// <summary>The next reply to a call posted to the page, past the events.</summary>
    private static async Task<string> NextReply(FakeWindow window)
    {
        while (true)
        {
            var message = await NextPosted(window);
            if (!message.StartsWith("""{"t":"event""", StringComparison.Ordinal))
                return message;
        }
    }

    [Fact]
    public async Task RunsReadyOnTheUiThreadAndQuitsWithTheLastWindow()
    {
        var app = App();
        var readyThread = 0;
        var run = RunAsync(app, () =>
        {
            readyThread = Environment.CurrentManagedThreadId;
            app.OpenWindow();
        });

        await WaitUntil(() => _platform.Windows.Count == 1);
        Assert.Equal(_platform.UiThread, readyThread);
        app.Windows[0].Close();
        Assert.Equal(0, await run);
        Assert.Empty(app.Windows);
    }

    [Fact]
    public async Task OpensTheAppsOwnPagesWithTheBridge()
    {
        var app = App();
        var run = RunAsync(app, () => app.OpenWindow(new WindowOptions { Url = "/settings/", Width = 640, Height = 480 }));
        await WaitUntil(() => _platform.Windows.Count == 1);

        var options = OnlyWindow().Options;
        Assert.Equal(new Uri("https://app.oriel/settings/index.html"), options.Url);
        Assert.Equal(@"C:\app\wwwroot", options.AssetsFolder);
        Assert.Equal((640, 480), (options.Width, options.Height));
        Assert.Contains("window.oriel", options.BridgeScript.Replace("`window.oriel`", "window.oriel"));
        Assert.False(options.DevTools);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task OpensPagesOfTheDevServerInDevelopment()
    {
        var app = App(devUrl: "http://localhost:5173");
        Assert.True(app.IsDev);
        var run = RunAsync(app, () => app.OpenWindow(new WindowOptions { Url = "/" }));
        await WaitUntil(() => _platform.Windows.Count == 1);

        var options = OnlyWindow().Options;
        Assert.Equal(new Uri("http://localhost:5173/"), options.Url);
        Assert.Null(options.AssetsFolder);
        Assert.True(options.DevTools);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task AnswersTheCallsOfItsPages()
    {
        var app = App();
        app.Bind(new Greeter());
        var run = RunAsync(app, () => app.OpenWindow());
        await WaitUntil(() => _platform.Windows.Count == 1);
        var window = OnlyWindow();

        Post(app, window, "https://app.oriel/index.html", """{"t":"call","id":1,"k":"p","m":"Greeter.Greet","a":["Ada"]}""");
        Assert.Equal("""{"t":"result","id":1,"k":"p","v":"Hello, Ada"}""", await NextReply(window));

        app.Quit();
        await run;
    }

    /// <summary>A service whose method asks for the window that called it.</summary>
    public sealed class Caller
    {
        public OrielWindow? Last { get; set; }
    }

    private sealed class CallerDispatcher(Caller caller) : ServiceDispatcher("Caller")
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            caller.Last = context.Window;
            return ValueTask.FromResult<ValueWriter?>(null);
        }
    }

    [Fact]
    public async Task TellsServicesWhichWindowCalled()
    {
        ServiceRegistry.Register<Caller>(c => new CallerDispatcher(c));
        var app = App();
        var caller = new Caller();
        app.Bind(caller);
        OrielWindow? first = null, second = null;
        var run = RunAsync(app, () =>
        {
            first = app.OpenWindow();
            second = app.OpenWindow();
        });
        await WaitUntil(() => _platform.Windows.Count == 2);
        var windows = _platform.Windows.ToArray();

        Post(app, windows[1], "https://app.oriel/", """{"t":"call","id":1,"k":"p","m":"Caller.Who","a":[]}""");
        await NextReply(windows[1]);
        Assert.Same(second, caller.Last);
        Post(app, windows[0], "https://app.oriel/", """{"t":"call","id":1,"k":"q","m":"Caller.Who","a":[]}""");
        await NextReply(windows[0]);
        Assert.Same(first, caller.Last);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task IgnoresMessagesOfOtherOrigins()
    {
        var app = App();
        app.Bind(new Greeter());
        var run = RunAsync(app, () => app.OpenWindow());
        await WaitUntil(() => _platform.Windows.Count == 1);
        var window = OnlyWindow();

        Post(app, window, "https://evil.example/", """{"t":"call","id":1,"k":"p","m":"Greeter.Greet","a":["Ada"]}""");
        Post(app, window, "https://app.oriel/", """{"t":"call","id":2,"k":"p","m":"Greeter.Greet","a":["Bob"]}""");
        Assert.Equal("""{"t":"result","id":2,"k":"p","v":"Hello, Bob"}""", await NextReply(window));

        app.Quit();
        await run;
    }

    [Fact]
    public async Task SendsEventsToEveryPage()
    {
        var app = App();
        var greeter = new Greeter();
        app.Bind(greeter);
        var run = RunAsync(app, () =>
        {
            app.OpenWindow();
            app.OpenWindow();
        });
        await WaitUntil(() => _platform.Windows.Count == 2);
        var windows = _platform.Windows.ToArray();
        foreach (var w in windows)
            Post(app, w, "https://app.oriel/", """{"t":"hello","k":"p"}""");
        await Task.Delay(50);

        greeter.Greeted.Emit("Ada");
        foreach (var w in windows)
            Assert.Equal("""{"t":"event","n":"Greeter.Greeted","v":"Ada"}""", await NextPosted(w));

        app.Quit();
        await run;
    }

    [Fact]
    public async Task ShowsThePagesTitleUnlessGivenOne()
    {
        var app = App();
        OrielWindow? following = null, fixedTitle = null;
        var run = RunAsync(app, () =>
        {
            following = app.OpenWindow();
            fixedTitle = app.OpenWindow(new WindowOptions { Title = "Fixed" });
        });
        await WaitUntil(() => _platform.Windows.Count == 2);
        var windows = _platform.Windows.ToArray();

        Assert.Equal("OrielTests", following!.Title);
        app.Dispatch(() =>
        {
            windows[0].Events.TitleChanged("Inbox (3)");
            windows[1].Events.TitleChanged("Ignored");
        });
        await WaitUntil(() => following.Title == "Inbox (3)");
        Assert.Equal("Inbox (3)", windows[0].Title);
        Assert.Equal("Fixed", fixedTitle!.Title);

        app.Quit();
        await run;
    }

    [Fact]
    public void RefusesToBindWhatIsNotAService()
    {
        var e = Assert.Throws<InvalidOperationException>(() => App().Bind(new object()));
        Assert.Equal("System.Object cannot be bound: it is not a [Service] class, or its project does not run the Oriel source generator.", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    public void RefusesNamesThatCannotNameAFolder(string name)
    {
        Assert.Throws<ArgumentException>(() => new OrielApp(new AppOptions { Name = name }, _platform, null));
    }

    [Fact]
    public void KeepsTheDataOfDevelopmentApart()
    {
        var name = "OrielTests" + Guid.NewGuid().ToString("N");
        var prod = new OrielApp(new AppOptions { Name = name }, _platform, null).DataDirectory;
        var dev = new OrielApp(new AppOptions { Name = name }, _platform, "http://localhost:5173").DataDirectory;
        try
        {
            Assert.EndsWith(name, prod);
            Assert.EndsWith(name + " Dev", dev);
            Assert.True(Directory.Exists(prod));
        }
        finally
        {
            Directory.Delete(prod);
            Directory.Delete(dev);
        }
    }

    private static void Post(OrielApp app, FakeWindow window, string source, string message) =>
        app.Dispatch(() => window.Events.MessageReceived(source, message));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }
}
