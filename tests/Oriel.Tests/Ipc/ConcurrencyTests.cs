using System.Collections.Concurrent;
using System.Text.Json;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel.Tests.Ipc;

/// <summary>How calls run under each <see cref="ServiceConcurrency"/>.</summary>
public class ConcurrencyTests
{
    /// <summary>
    /// A service whose calls wait for a gate of their own, and record when
    /// they start and how many run at once.
    /// </summary>
    private sealed class Gated(ServiceConcurrency concurrency) : ServiceDispatcher("Gated", concurrency)
    {
        private int _running;

        public ConcurrentDictionary<int, TaskCompletionSource> Gates { get; } = new();

        public ConcurrentQueue<int> Started { get; } = new();

        public int MostAtOnce { get; private set; }

        public List<int> Threads { get; } = [];

        public TaskCompletionSource Gate(int n) => Gates.GetOrAdd(n, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

        public override async ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            var n = args[0].GetInt32();
            lock (Threads)
                Threads.Add(Environment.CurrentManagedThreadId);
            Started.Enqueue(n);
            var running = Interlocked.Increment(ref _running);
            MostAtOnce = Math.Max(MostAtOnce, running);
            try
            {
                await Gate(n).Task.WaitAsync(context.Cancellation);
                lock (Threads)
                    Threads.Add(Environment.CurrentManagedThreadId); // after the await
                return w => Json.Write(w, n);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private readonly ConcurrentQueue<string> _posted = new();

    private PageConnection Connect(Gated service, Action<Action>? dispatch = null)
    {
        var services = new ServiceTable();
        services.Add(service);
        return new PageConnection(services, _posted.Enqueue, dispatch: dispatch);
    }

    private static string Call(long n, string token = "p") =>
        $$"""{"t":"call","id":{{n}},"k":"{{token}}","m":"Gated.Run","a":[{{n}}]}""";

    [Fact]
    public async Task ParallelCallsRunTogether()
    {
        var service = new Gated(ServiceConcurrency.Parallel);
        using var page = Connect(service);

        var calls = Enumerable.Range(1, 3).Select(n => page.ReceiveAsync(Call(n))).ToArray();
        await WaitUntil(() => service.Started.Count == 3);
        foreach (var gate in Enumerable.Range(1, 3))
            service.Gate(gate).SetResult();
        await Task.WhenAll(calls);

        Assert.Equal(3, service.MostAtOnce);
    }

    [Fact]
    public async Task SerialCallsRunOneAtATimeInTheOrderTheyArrived()
    {
        var service = new Gated(ServiceConcurrency.Serial);
        using var page = Connect(service);

        var calls = Enumerable.Range(1, 20).Select(n => page.ReceiveAsync(Call(n))).ToArray();
        for (var n = 1; n <= 20; n++)
        {
            await WaitUntil(() => service.Started.Count == n);
            await Task.Delay(n == 1 ? 20 : 0); // time for a call to start out of turn
            Assert.Equal(n, service.Started.Count);
            service.Gate(n).SetResult();
        }
        await Task.WhenAll(calls);

        Assert.Equal(Enumerable.Range(1, 20), service.Started);
        Assert.Equal(1, service.MostAtOnce);
        Assert.Equal(20, _posted.Count);
    }

    [Fact]
    public async Task SerialCallsOfSeveralWindowsShareTheirTurns()
    {
        var service = new Gated(ServiceConcurrency.Serial);
        var services = new ServiceTable();
        services.Add(service);
        using var first = new PageConnection(services, _posted.Enqueue);
        using var second = new PageConnection(services, _posted.Enqueue);

        var a = first.ReceiveAsync(Call(1));
        var b = second.ReceiveAsync(Call(2));
        await WaitUntil(() => service.Started.Count == 1);
        await Task.Delay(20);
        Assert.Equal([1], service.Started);

        service.Gate(1).SetResult();
        service.Gate(2).SetResult();
        await Task.WhenAll(a, b);
        Assert.Equal(1, service.MostAtOnce);
    }

    [Fact]
    public async Task ASerialCallOfAPageThatWentAwayGivesItsTurnUp()
    {
        var service = new Gated(ServiceConcurrency.Serial);
        using var page = Connect(service);

        var running = page.ReceiveAsync(Call(1));
        var waiting = page.ReceiveAsync(Call(2));
        await WaitUntil(() => service.Started.Count == 1);

        await page.ReceiveAsync("""{"t":"hello","k":"reloaded"}""");
        await Task.WhenAll(running, waiting);
        Assert.Equal([1], service.Started); // the second call never ran

        // And the next page's calls run.
        service.Gate(3).SetResult();
        await page.ReceiveAsync(Call(3, "reloaded"));
        Assert.Contains(_posted, m => m.Contains("\"v\":3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UiThreadCallsRunOnTheUiThreadAfterAwaitsToo()
    {
        using var ui = new UiThread();
        var service = new Gated(ServiceConcurrency.UiThread);
        using var page = Connect(service, ui.Post);

        service.Gate(1).SetResult();
        await page.ReceiveAsync(Call(1));
        var call = page.ReceiveAsync(Call(2));
        await WaitUntil(() => service.Started.Count == 2);
        service.Gate(2).SetResult();
        await call;

        Assert.Equal(4, service.Threads.Count);
        Assert.All(service.Threads, t => Assert.Equal(ui.ThreadId, t));
        Assert.Equal(2, _posted.Count);
    }

    [Fact]
    public async Task UiThreadCallsAreCanceledWithTheirPage()
    {
        using var ui = new UiThread();
        var service = new Gated(ServiceConcurrency.UiThread);
        using var page = Connect(service, ui.Post);

        var call = page.ReceiveAsync(Call(1));
        await WaitUntil(() => service.Started.Count == 1);
        await page.ReceiveAsync("""{"t":"hello","k":"reloaded"}""");
        await call;

        Assert.Empty(_posted);
    }

    /// <summary>A thread that runs what is posted to it, in order, as an app's UI thread does.</summary>
    private sealed class UiThread : IDisposable
    {
        private readonly BlockingCollection<Action> _work = [];
        private readonly Thread _thread;

        public UiThread()
        {
            _thread = new Thread(() =>
            {
                foreach (var action in _work.GetConsumingEnumerable())
                    action();
            });
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public void Post(Action action) => _work.Add(action);

        public void Dispose()
        {
            _work.CompleteAdding();
            _thread.Join();
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }
}
