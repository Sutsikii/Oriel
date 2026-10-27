using System.Collections.Concurrent;

namespace Oriel.Tests;

public class SingleInstanceTests
{
    private readonly string _app = "Oriel test " + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Runs <paramref name="work"/> on a thread of its own that stays until
    /// <paramref name="until"/>: a mutex belongs to the thread that took it,
    /// as to a process.
    /// </summary>
    private static Task<T> OnThread<T>(Func<T> work, Task? until = null)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
            until?.Wait();
        });
        thread.Start();
        return done.Task;
    }

    [Fact]
    public async Task SendsTheArgumentsOfLaterInstancesToTheFirst()
    {
        var received = new BlockingCollection<SecondInstanceEventArgs>();
        var release = new TaskCompletionSource();
        var cleanup = new TaskCompletionSource();
        var claimed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Thread(() =>
        {
            // Claimed and released by one thread, as by a process whose Run returns.
            var instance = SingleInstance.Claim(_app, ["first"], "/", received.Add);
            claimed.SetResult(instance is not null);
            release.Task.Wait();
            instance?.Dispose();
            cleanup.SetResult();
        });
        first.Start();
        Assert.True(await claimed.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        var second = await OnThread(() => SingleInstance.Claim(_app, ["myapp://open?id=4", "notes.txt"], @"C:\work", _ => { }));
        Assert.Null(second);
        Assert.True(received.TryTake(out var started, TimeSpan.FromSeconds(10)));
        Assert.Equal(["myapp://open?id=4", "notes.txt"], started.Arguments);
        Assert.Equal(@"C:\work", started.WorkingDirectory);

        Assert.Null(await OnThread(() => SingleInstance.Claim(_app, [], "/", _ => { })));
        Assert.True(received.TryTake(out var third, TimeSpan.FromSeconds(10)));
        Assert.Empty(third.Arguments);

        // Once the first one ends, the next launch is first.
        release.SetResult();
        await cleanup.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var next = await OnThread(() => SingleInstance.Claim(_app, [], "/", _ => { }), Task.Delay(100));
        Assert.NotNull(next);
    }

    [Fact]
    public async Task ARunningAppHearsOfTheLaunchesAfterIt()
    {
        var app = new OrielApp(new AppOptions { Name = _app, AssetsDirectory = Path.GetTempPath(), SingleInstance = true }, new Fakes.FakePlatform(), null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heard = new TaskCompletionSource<(SecondInstanceEventArgs, List<OpenRequestedEventArgs>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new List<OpenRequestedEventArgs>();
        SecondInstanceEventArgs? second = null;
        app.OpenRequested += (_, e) =>
        {
            opened.Add(e);
            if (second is not null)
                heard.TrySetResult((second, opened));
        };
        app.SecondInstanceStarted += (_, e) => second = e;
        var run = Task.Run(() => app.Run(started.SetResult));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(await OnThread(() => SingleInstance.Claim(_app, ["notes://open?id=4"], "/", _ => { })));
        var (args, links) = await heard.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["notes://open?id=4"], args.Arguments);
        Assert.Equal(new Uri("notes://open?id=4"), Assert.Single(links).Url);

        app.Quit();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        // The place is free again.
        Assert.NotNull(await OnThread(() => SingleInstance.Claim(_app, [], "/", _ => { }), Task.Delay(100)));
    }

    [Fact]
    public void ReadsOnlyTheMessagesItWrites()
    {
        var message = SingleInstance.Message(["a", "é ✓"], "/home/ada");
        var read = SingleInstance.Read(message)!;
        Assert.Equal(["a", "é ✓"], read.Arguments);
        Assert.Equal("/home/ada", read.WorkingDirectory);
        Assert.Null(SingleInstance.Read("not json"u8));
        Assert.Null(SingleInstance.Read("""{"cwd":"/","args":[1]}"""u8));
        Assert.Null(SingleInstance.Read("""{"args":[]}"""u8));
        Assert.Null(SingleInstance.Read("[]"u8));
    }

    [Fact]
    public void NamesItsMutexAfterTheAppAndTheUser()
    {
        Assert.Equal(SingleInstance.Key("Notes"), SingleInstance.Key("Notes"));
        Assert.NotEqual(SingleInstance.Key("Notes"), SingleInstance.Key("Notes 2"));
        Assert.Matches("^oriel-[0-9a-f]{32}$", SingleInstance.Key("Notes"));
    }

    [Fact]
    public void FindsTheLinksAndFilesToOpenInArguments()
    {
        var folder = Directory.CreateTempSubdirectory("oriel-open-").FullName;
        try
        {
            var file = Path.Combine(folder, "a note.note");
            File.WriteAllText(file, "x");
            var requests = OpenRequestedEventArgs.From(
                ["--verbose", "myapp://open?id=4", "a note.note", new Uri(file).AbsoluteUri, "missing.note", "", "-x"],
                folder);
            Assert.Equal(3, requests.Count);
            Assert.Equal(new Uri("myapp://open?id=4"), requests[0].Url);
            Assert.Null(requests[0].Path);
            Assert.Equal(file, requests[1].Path);
            Assert.Null(requests[1].Url);
            Assert.Equal(file, requests[2].Path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
