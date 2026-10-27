using System.Buffers;
using System.Text;
using System.Text.Json;
using Oriel.Generated;

namespace Oriel.Generators.Tests;

/// <summary>How generated dispatchers call service methods.</summary>
public class DispatcherTests
{
    [Fact]
    public async Task CallsAMethodWithItsArguments()
    {
        var greeter = Harness.Compile("""
            namespace App;
            [Service]
            public class Greeter
            {
                public string Greet(string name) => "Hello, " + name;
            }
            """).Bind("App.Greeter");

        Assert.Equal("Greeter", greeter.Dispatcher.Name);
        Assert.Equal("\"Hello, Ada\"", await greeter.Call("Greet", """["Ada"]"""));
    }

    [Theory]
    [InlineData("", ServiceConcurrency.Parallel)]
    [InlineData("(Concurrency = ServiceConcurrency.Serial)", ServiceConcurrency.Serial)]
    [InlineData("(Name = \"Windows\", Concurrency = ServiceConcurrency.UiThread)", ServiceConcurrency.UiThread)]
    public void PassesTheConcurrencyOfTheService(string arguments, ServiceConcurrency concurrency)
    {
        var service = Harness.Compile($$"""
            [Service{{arguments}}]
            public class Shell
            {
                public void Hide() { }
            }
            """).Bind("Shell");

        Assert.Equal(concurrency, service.Dispatcher.Concurrency);
    }

    [Fact]
    public async Task ReturnsNothingForVoidAndTask()
    {
        var service = Harness.Compile("""
            [Service]
            public class Counter
            {
                public int Count { get; private set; }
                public void Increment() => Count++;
                public async Task IncrementLater() { await Task.Yield(); Count++; }
                public ValueTask IncrementNow() { Count++; return default; }
            }
            """).Bind("Counter");

        Assert.Null(await service.Call("Increment"));
        Assert.Null(await service.Call("IncrementLater"));
        Assert.Null(await service.Call("IncrementNow"));
        Assert.Equal(3, (int)service.Instance.GetType().GetProperty("Count")!.GetValue(service.Instance)!);
    }

    [Fact]
    public async Task AwaitsTheResultsOfTasks()
    {
        var service = Harness.Compile("""
            [Service]
            public class Slow
            {
                public async Task<string> Text() { await Task.Delay(1); return "done"; }
                public ValueTask<int> Number() => new(7);
            }
            """).Bind("Slow");

        Assert.Equal("\"done\"", await service.Call("Text"));
        Assert.Equal("7", await service.Call("Number"));
    }

    [Fact]
    public async Task ReturnsStreamsThroughTheContext()
    {
        var service = Harness.Compile("""
            [Service]
            public class Counter
            {
                public async IAsyncEnumerable<string> Count(int to, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation = default)
                {
                    for (var i = 1; i <= to; i++)
                    {
                        await Task.Yield();
                        yield return "#" + i;
                    }
                }
            }
            """).Bind("Counter");

        using var args = JsonDocument.Parse("[3]");
        var context = new CallContext(CancellationToken.None);
        Assert.Null(await service.Dispatcher.InvokeAsync("Count", args.RootElement, context));
        var items = new List<string>();
        await context.Stream!.Invoke(() => Task.CompletedTask, item =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer))
                item(w);
            items.Add(Encoding.UTF8.GetString(buffer.WrittenSpan));
        }, CancellationToken.None);
        Assert.Equal(["\"#1\"", "\"#2\"", "\"#3\""], items);
    }

    [Fact]
    public async Task ReturnsBinaryDataThroughTheContext()
    {
        var service = Harness.Compile("""
            [Service]
            public class Samples
            {
                public ReadOnlyMemory<float> Levels() => new float[] { 0.5f, -1 };
                public async Task<Memory<ushort>> Later() { await Task.Yield(); return new ushort[] { 1, 513 }; }
            }
            """).Bind("Samples");

        using var none = JsonDocument.Parse("[]");
        var levels = new CallContext(CancellationToken.None);
        Assert.Null(await service.Dispatcher.InvokeAsync("Levels", none.RootElement, levels));
        Assert.Equal("Float32Array", levels.Binary!.TypedArray);
        Assert.Equal(new byte[] { 0, 0, 0, 0x3f, 0, 0, 0x80, 0xbf }, levels.Binary.Bytes().ToArray());

        var later = new CallContext(CancellationToken.None);
        await service.Dispatcher.InvokeAsync("Later", none.RootElement, later);
        Assert.Equal("Uint16Array", later.Binary!.TypedArray);
        Assert.Equal(new byte[] { 1, 0, 1, 2 }, later.Binary.Bytes().ToArray());
    }

    [Fact]
    public async Task LeavesOutTheOptionalArgumentsAPageLeftOut()
    {
        var service = Harness.Compile("""
            [Service]
            public class Calc
            {
                public int Add(int a, int b = 10, int c = 100) => a + b + c;
            }
            """).Bind("Calc");

        Assert.Equal("111", await service.Call("Add", "[1]"));
        Assert.Equal("103", await service.Call("Add", "[1,2]"));
        Assert.Equal("6", await service.Call("Add", "[1,2,3]"));
        Assert.Equal("Calc.Add takes 1 to 3 argument(s), got 0", await service.Fail("Add", "[]"));
    }

    [Fact]
    public async Task TakesANullForAnOptionalArgumentThatCannotBeNull()
    {
        // f(1, undefined, 3) in a page is sent as [1, null, 3].
        var service = Harness.Compile("""
            [Service]
            public class Calc
            {
                public int Add(int a, int b = 10, int c = 100) => a + b + c;
                public string Label(string? text = "none") => text ?? "null";
                public int Count(params int[] values) => values.Length;
            }
            """).Bind("Calc");

        Assert.Equal("14", await service.Call("Add", "[1,null,3]"));
        Assert.Equal("\"null\"", await service.Call("Label", "[null]"));
        Assert.Equal("\"none\"", await service.Call("Label", "[]"));
        Assert.Equal("0", await service.Call("Count", "[]"));
        Assert.Equal("2", await service.Call("Count", "[[4,5]]"));
    }

    [Fact]
    public async Task PassesTheCallsCancellation()
    {
        var service = Harness.Compile("""
            [Service]
            public class Waiter
            {
                public async Task<bool> Wait(int ms, CancellationToken cancellation = default)
                {
                    await Task.Delay(ms, cancellation);
                    return true;
                }
            }
            """).Bind("Waiter");

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Call("Wait", "[60000]", canceled.Token));
        Assert.Equal("true", await service.Call("Wait", "[0]"));
    }

    [Fact]
    public async Task PassesTheCallersWindowWithoutThePageSendingIt()
    {
        var harness = Harness.Compile("""
            [Service]
            public class Windows
            {
                public string Where(OrielWindow window, string label) => label + (window is null ? ": no window" : ": a window");
            }
            """);

        // The test calls with no window; the app passes the page's.
        Assert.Equal("\"x: no window\"", await harness.Bind("Windows").Call("Where", """["x"]"""));
        Assert.Contains("where(label: string): Promise<string> {", harness.TypeScript);
    }

    [Fact]
    public async Task ExposesInheritedMethods()
    {
        var service = Harness.Compile("""
            public class Base
            {
                public virtual string Name() => "base";
                public string Shared() => "shared";
            }
            [Service]
            public class Derived : Base
            {
                public override string Name() => "derived";
            }
            """).Bind("Derived");

        Assert.Equal("\"derived\"", await service.Call("Name"));
        Assert.Equal("\"shared\"", await service.Call("Shared"));
    }

    [Fact]
    public async Task HidesWhatIsNotPublicOrIsIgnored()
    {
        var service = Harness.Compile("""
            [Service]
            public class Vault
            {
                public string Open() => "open";
                [ServiceIgnore] public string Secret() => "secret";
                internal string Internal() => "internal";
                public static string Static() => "static";
            }
            """).Bind("Vault");

        Assert.Equal("\"open\"", await service.Call("Open"));
        foreach (var hidden in new[] { "Secret", "Internal", "Static", "ToString", "GetHashCode" })
            Assert.Equal($"Vault has no method {hidden}", await service.Fail(hidden));
    }

    [Fact]
    public void UsesTheNameGivenToTheService()
    {
        var service = Harness.Compile("""
            [Service(Name = "Files")]
            public class FileService { public void Touch() { } }
            """).Bind("FileService");

        Assert.Equal("Files", service.Dispatcher.Name);
    }

    [Fact]
    public async Task CallsMethodsNamedWithKeywords()
    {
        var service = Harness.Compile("""
            [Service]
            public class Keywords { public int @event(int @class) => @class; }
            """).Bind("Keywords");

        Assert.Equal("4", await service.Call("event", "[4]"));
    }

    [Fact]
    public async Task LetsExceptionsOfServicesThrough()
    {
        var service = Harness.Compile("""
            [Service]
            public class Grumpy { public void No() => throw new InvalidOperationException("not today"); }
            """).Bind("Grumpy");

        Assert.Equal("not today", await service.Fail("No"));
    }

    [Fact]
    public void GeneratesNothingWithoutServices()
    {
        var harness = Harness.Compile("public class Plain { }");
        Assert.Equal("", harness.Source);
    }
}
