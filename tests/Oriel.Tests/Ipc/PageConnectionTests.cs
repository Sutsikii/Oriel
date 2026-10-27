using System.Collections.Concurrent;
using System.Text.Json;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel.Tests.Ipc;

public class PageConnectionTests
{
    /// <summary>A dispatcher written by hand, as the generator would write it.</summary>
    private sealed class Calculator : ServiceDispatcher
    {
        public TaskCompletionSource<int> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken LastCancellation { get; private set; }

        public Calculator()
            : base("Calc")
        {
        }

        public override async ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            LastCancellation = context.Cancellation;
            switch (method)
            {
                case "Add":
                    Arguments.Count(args, "Calc.Add", 2, 2);
                    var sum = Arguments.Read(args, 0, "a", Json.ReadInt32) + Arguments.Read(args, 1, "b", Json.ReadInt32);
                    return w => Json.Write(w, sum);
                case "Reset":
                    return null;
                case "Fail":
                    throw new InvalidOperationException("no luck");
                case "Wait":
                    var value = await Pending.Task.WaitAsync(context.Cancellation);
                    return w => Json.Write(w, value);
                default:
                    throw new MethodNotFoundException(Name, method);
            }
        }
    }

    private readonly ConcurrentQueue<string> _posted = new();
    private readonly Calculator _calc = new();
    private readonly PageConnection _page;

    public PageConnectionTests()
    {
        var services = new ServiceTable();
        services.Add(_calc);
        _page = new PageConnection(services, _posted.Enqueue);
    }

    private static string Call(long id, string method, string args, string token = "page1") =>
        $$"""{"t":"call","id":{{id}},"k":"{{token}}","m":"{{method}}","a":{{args}}}""";

    private string Single()
    {
        Assert.True(_posted.TryDequeue(out var message));
        Assert.Empty(_posted);
        return message;
    }

    [Fact]
    public async Task RepliesWithTheResult()
    {
        await _page.ReceiveAsync(Call(1, "Calc.Add", "[2,3]"));
        Assert.Equal("""{"t":"result","id":1,"k":"page1","v":5}""", Single());
    }

    [Fact]
    public async Task RepliesWithoutAValueToMethodsReturningNothing()
    {
        await _page.ReceiveAsync(Call(7, "Calc.Reset", "[]"));
        Assert.Equal("""{"t":"result","id":7,"k":"page1"}""", Single());
    }

    [Theory]
    [InlineData("Calc.Fail", "[]", "no luck")]
    [InlineData("Calc.Nope", "[]", "Calc has no method Nope")]
    [InlineData("Other.Add", "[]", "No service named Other is bound")]
    [InlineData("Add", "[]", "'Add' does not name a method as Service.Method")]
    [InlineData("Calc.Add", "[1,\"2\"]", "argument 'b': expected a number, got a string")]
    public async Task RepliesWithTheErrorOfAFailedCall(string method, string args, string error)
    {
        await _page.ReceiveAsync(Call(2, method, args));
        using var reply = JsonDocument.Parse(Single());
        Assert.Equal("error", reply.RootElement.GetProperty("t").GetString());
        Assert.Equal(2, reply.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(error, reply.RootElement.GetProperty("e").GetString());
    }

    /// <summary>An error pages can tell apart, registered as the generator would.</summary>
    private sealed class LockedException(string user) : Exception("C:\\data\\deck.json is locked")
    {
        public string User { get; } = user;
    }

    static PageConnectionTests()
    {
        ServiceRegistry.RegisterError<LockedException>("locked", (w, e) =>
        {
            w.WriteStartObject();
            w.WriteString("user", e.User);
            w.WriteEndObject();
        });
    }

    [Fact]
    public async Task RepliesWithTheCodeAndDataOfServiceErrors()
    {
        var services = new ServiceTable();
        services.Add(new Throwing(new LockedException("ada")));
        using var page = new PageConnection(services, _posted.Enqueue, showExceptionMessages: false);

        await page.ReceiveAsync(Call(1, "Throwing.Run", "[]"));
        Assert.Equal("""{"t":"error","id":1,"k":"page1","e":"C:\\data\\deck.json is locked","c":"locked","d":{"user":"ada"}}""", Single());
    }

    [Theory]
    [InlineData(true, "no luck")]
    [InlineData(false, "Throwing.Run failed")]
    public async Task HidesTheMessagesOfUnexpectedExceptionsOutsideDevelopment(bool show, string message)
    {
        var services = new ServiceTable();
        services.Add(new Throwing(new InvalidOperationException("no luck")));
        using var page = new PageConnection(services, _posted.Enqueue, showExceptionMessages: show);
        var failures = new List<Exception>();
        page.CallFailed += (_, e) => failures.Add(e);

        await page.ReceiveAsync(Call(1, "Throwing.Run", "[]"));
        Assert.Equal($$"""{"t":"error","id":1,"k":"page1","e":"{{message}}"}""", Single());
        Assert.Equal("no luck", Assert.Single(failures).Message); // the app still gets everything
    }

    [Theory]
    [InlineData("Calc.Nope", "[]", "Calc has no method Nope")]
    [InlineData("Other.Add", "[]", "No service named Other is bound")]
    [InlineData("Add", "[]", "'Add' does not name a method as Service.Method")]
    [InlineData("Calc.Add", "[1,\"2\"]", "argument 'b': expected a number, got a string")]
    public async Task KeepsTheMessagesOfCallsThatDidNotFitOutsideDevelopment(string method, string args, string error)
    {
        using var page = new PageConnection(Services(), _posted.Enqueue, showExceptionMessages: false);
        await page.ReceiveAsync(Call(1, method, args));
        using var reply = JsonDocument.Parse(Single());
        Assert.Equal(error, reply.RootElement.GetProperty("e").GetString());
    }

    private static ServiceTable Services()
    {
        var services = new ServiceTable();
        services.Add(new Calculator());
        return services;
    }

    private sealed class Throwing(Exception error) : ServiceDispatcher("Throwing")
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context) => throw error;
    }

    [Fact]
    public async Task ReportsFailedCalls()
    {
        var failures = new List<string>();
        _page.CallFailed += (method, e) => failures.Add($"{method}: {e.Message}");
        await _page.ReceiveAsync(Call(1, "Calc.Fail", "[]"));
        Assert.Equal(["Calc.Fail: no luck"], failures);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"t":"call","id":1,"m":"Calc.Add","a":[1,2]}""")]
    [InlineData("""{"t":"call","k":"p","m":"Calc.Add","a":[1,2]}""")]
    [InlineData("""{"t":"future","k":"p"}""")]
    [InlineData("""{"t":"call","id":"1","k":"p","m":"Calc.Add","a":[1,2]}""")]
    [InlineData("""{"t":"call","id":1,"k":"\ud800","m":"Calc.Add","a":[1,2]}""")]
    [InlineData("""{"t":"call","id":1,"k":"p","m":"Calc.\udc00","a":[1,2]}""")]
    [InlineData("""{"t":"call","id":1,"k":"p","m":"Calc.Add","a":[1,2],"\ud800":0}""")]
    [InlineData("""{"id":1,"\ud800":0,"t":"call","k":"p","m":"Calc.Add","a":[1,2]}""")]
    [InlineData("""{"\ud800":0,"t":"call","id":1,"k":"p","m":"Calc.Add","a":[1,2]}""")]
    public async Task IgnoresMalformedMessages(string message)
    {
        await _page.ReceiveAsync(message);
        Assert.Empty(_posted);
    }

    [Fact]
    public async Task RefusesMessagesThatAreNotJson()
    {
        var refused = new List<string>();
        _page.MessageRefused += refused.Add;
        await _page.ReceiveAsync("not json");
        Assert.StartsWith("a message that is not JSON", Assert.Single(refused));
    }

    [Fact]
    public async Task RefusesMessagesThatAreNotValidUtf16()
    {
        var refused = new List<string>();
        _page.MessageRefused += refused.Add;
        await _page.ReceiveAsync(Call(1, "Calc\uD800.Add", "[1,2]")); // a lone surrogate, not escaped
        Assert.StartsWith("a message that is not valid UTF-16", Assert.Single(refused));
        Assert.Empty(_posted);
    }

    [Fact]
    public async Task RefusesMessagesLongerThanTheLimit()
    {
        var refused = new List<string>();
        using var page = new PageConnection(Services(), _posted.Enqueue, limits: new PageLimits(64, 64, 10));
        page.MessageRefused += refused.Add;

        var tooLong = Call(2, "Calc.Add", $"[1,2,\"{new string('x', 20)}\"]");
        await page.ReceiveAsync(Call(1, "Calc.Add", "[1,2]")); // 56 characters
        await page.ReceiveAsync(tooLong);

        Assert.Contains("\"v\":3", Single());
        Assert.Equal($"a message of {tooLong.Length} characters, more than the 64 allowed", Assert.Single(refused));
    }

    [Fact]
    public async Task RefusesMessagesNestedDeeperThanTheLimit()
    {
        var refused = new List<string>();
        using var page = new PageConnection(Services(), _posted.Enqueue, limits: new PageLimits(1 << 20, 8, 10));
        page.MessageRefused += refused.Add;

        await page.ReceiveAsync(Call(1, "Calc.Add", "[[[[[[[[1]]]]]]]],2]"));
        await page.ReceiveAsync(Call(2, "Calc.Add", new string('[', 10_000) + new string(']', 10_000)));

        Assert.Equal(2, refused.Count);
        Assert.All(refused, r => Assert.Contains("at most 8 levels", r));
        Assert.Empty(_posted);
    }

    [Fact]
    public async Task FailsCallsBeyondTheLimitOfCallsInFlight()
    {
        var refused = new List<string>();
        var services = new ServiceTable();
        var calc = new Calculator();
        services.Add(calc);
        using var page = new PageConnection(services, _posted.Enqueue, limits: new PageLimits(1 << 20, 64, 2));
        page.MessageRefused += refused.Add;

        var first = page.ReceiveAsync(Call(1, "Calc.Wait", "[]"));
        var second = page.ReceiveAsync(Call(2, "Calc.Wait", "[]"));
        await page.ReceiveAsync(Call(3, "Calc.Add", "[1,2]"));

        using (var reply = JsonDocument.Parse(Single()))
        {
            Assert.Equal("error", reply.RootElement.GetProperty("t").GetString());
            Assert.Equal(3, reply.RootElement.GetProperty("id").GetInt64());
            Assert.Equal("Too many calls at once: 2 are running", reply.RootElement.GetProperty("e").GetString());
        }
        Assert.Equal("a call of Calc.Add beyond the 2 calls allowed to run at once", Assert.Single(refused));

        // Once the calls return, there is room again.
        calc.Pending.SetResult(4);
        await Task.WhenAll(first, second);
        Assert.Equal(2, _posted.Count);
        _posted.Clear();
        await page.ReceiveAsync(Call(4, "Calc.Add", "[1,2]"));
        Assert.Contains("\"v\":3", Single());
    }

    [Fact]
    public async Task CancelsTheCallsOfAPageThatWentAway()
    {
        var waiting = _page.ReceiveAsync(Call(1, "Calc.Wait", "[]"));
        await WaitUntil(() => _calc.LastCancellation.CanBeCanceled);
        var first = _calc.LastCancellation;

        await _page.ReceiveAsync("""{"t":"hello","k":"page2"}""");
        await waiting;

        Assert.True(first.IsCancellationRequested);
        Assert.Empty(_posted);
    }

    [Fact]
    public async Task DropsResultsAddressedToAPreviousPage()
    {
        // The call ignores its cancellation and finishes after the reload.
        var services = new ServiceTable();
        var slow = new Slow();
        services.Add(slow);
        using var page = new PageConnection(services, _posted.Enqueue);

        var call = page.ReceiveAsync(Call(1, "Slow.Run", "[]"));
        await page.ReceiveAsync("""{"t":"hello","k":"page2"}""");
        slow.Release.SetResult();
        await call;

        Assert.Empty(_posted);
    }

    [Fact]
    public async Task SendsNothingOnceDisposed()
    {
        await _page.ReceiveAsync("""{"t":"hello","k":"page1"}""");
        _page.Dispose();
        _page.Send("x");
        await _page.ReceiveAsync(Call(1, "Calc.Add", "[1,2]"));
        Assert.Empty(_posted);
    }

    [Fact]
    public async Task SendsOnlyOnceAPageSaidHello()
    {
        _page.Send("early");
        await _page.ReceiveAsync("""{"t":"hello","k":"page1"}""");
        _page.Send("late");
        Assert.Equal("late", Single());
    }

    private static string Ack(string token, params string[] names) =>
        $$"""{"t":"ack","k":"{{token}}","n":{{JsonSerializer.Serialize(names)}}}""";

    [Fact]
    public async Task HoldsTheLatestCoalescedPayloadUntilThePageTakesTheOneBefore()
    {
        await _page.ReceiveAsync("""{"t":"hello","k":"page1"}""");
        _page.SendLatest("Pointer.Moved", "1");
        _page.SendLatest("Pointer.Moved", "2");
        _page.SendLatest("Pointer.Moved", "3");
        _page.SendLatest("Zoom.Changed", "z"); // another event: a slot of its own
        Assert.Equal(["1", "z"], _posted);
        _posted.Clear();

        await _page.ReceiveAsync(Ack("page1", "Pointer.Moved"));
        Assert.Equal("3", Single());

        // Nothing waits now: the next acknowledgement frees the event, and
        // its next payload goes at once.
        await _page.ReceiveAsync(Ack("page1", "Pointer.Moved", "Zoom.Changed"));
        Assert.Empty(_posted);
        _page.SendLatest("Pointer.Moved", "4");
        Assert.Equal("4", Single());
    }

    [Fact]
    public async Task IgnoresAcknowledgementsOfAnotherPageOrEventOrShape()
    {
        await _page.ReceiveAsync("""{"t":"hello","k":"page1"}""");
        _page.SendLatest("Pointer.Moved", "1");
        _page.SendLatest("Pointer.Moved", "2");
        _posted.Clear();

        await _page.ReceiveAsync(Ack("page1", "Other.Event"));
        await _page.ReceiveAsync("""{"t":"ack","k":"page1","n":"Pointer.Moved"}""");
        await _page.ReceiveAsync("""{"t":"ack","k":"page1","n":[1,null,{"a":"Pointer.Moved"}]}""");
        await _page.ReceiveAsync("""{"t":"ack","k":"page1","n":["\ud800"]}"""); // an escaped lone surrogate
        Assert.Empty(_posted);

        // A new page: what waited was for the one before, and the event is free.
        await _page.ReceiveAsync(Ack("page2", "Pointer.Moved"));
        Assert.Empty(_posted);
        _page.SendLatest("Pointer.Moved", "3");
        Assert.Equal("3", Single());
    }

    [Fact]
    public void SendsNoCoalescedPayloadBeforeThePageSaysHello()
    {
        _page.SendLatest("Pointer.Moved", "1");
        Assert.Empty(_posted);
    }

    private sealed class Slow : ServiceDispatcher
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Slow()
            : base("Slow")
        {
        }

        public override async ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            await Release.Task;
            return w => Json.Write(w, 1);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }
}
