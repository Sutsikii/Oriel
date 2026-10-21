using System.Collections.Concurrent;
using Oriel.Generated;

namespace Oriel.Generators.Tests;

/// <summary>How generated dispatchers connect the events of services.</summary>
public class EventTests
{
    private sealed class Sink : IEventSink
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public void Broadcast(string message) => Messages.Enqueue(message);
    }

    private static void Emit(Service service, string @event, object? payload)
    {
        var value = service.Instance.GetType().GetProperty(@event)!.GetValue(service.Instance)!;
        value.GetType().GetMethod("Emit")!.Invoke(value, [payload]);
    }

    [Fact]
    public void SendsThePayloadsOfEventsToTheSink()
    {
        var service = Harness.Compile("""
            namespace App;
            public record Progress(string Task, double Done);
            [Service]
            public class Jobs
            {
                public Event<Progress> Advanced { get; } = new();
                public Event<int?> Count { get; } = new();
            }
            """).Bind("App.Jobs");

        var sink = new Sink();
        service.Dispatcher.Attach(sink);
        Emit(service, "Advanced", Activator.CreateInstance(service.Instance.GetType().Assembly.GetType("App.Progress")!, "build", 0.5));
        Emit(service, "Count", null);

        Assert.Equal(
            [
                """{"t":"event","n":"Jobs.Advanced","v":{"task":"build","done":0.5}}""",
                """{"t":"event","n":"Jobs.Count","v":null}""",
            ],
            sink.Messages);
    }

    [Fact]
    public void NamesTheEventsOfARenamedService()
    {
        var service = Harness.Compile("""
            [Service(Name = "Clock")]
            public class SystemClock { public Event<string> Tick { get; } = new(); }
            """).Bind("SystemClock");

        var sink = new Sink();
        service.Dispatcher.Attach(sink);
        Emit(service, "Tick", "now");
        Assert.Equal("""{"t":"event","n":"Clock.Tick","v":"now"}""", Assert.Single(sink.Messages));
    }

    [Fact]
    public void SaysWhichEventWasLeftWithoutAValue()
    {
        var service = Harness.Compile("""
            [Service]
            public class Clock { public Event<int> Tick { get; set; } = null!; }
            """).Bind("Clock");

        var e = Assert.Throws<InvalidOperationException>(() => service.Dispatcher.Attach(new Sink()));
        Assert.Equal("Clock.Tick is null: give the event a value, as in `public Event<T> Tick { get; } = new();`", e.Message);
    }

    [Fact]
    public void ReportsPayloadsThatCannotBeSent()
    {
        var harness = Harness.Generate("[Service] public class Clock { public Event<Uri> Moved { get; } = new(); }");
        var diagnostic = Assert.Single(harness.GeneratorDiagnostics);
        Assert.Equal("ORIEL001", diagnostic.Id);
        Assert.Equal("the payload of Clock.Moved cannot be sent between C# and pages: System.Uri is not a type Oriel maps", diagnostic.GetMessage());
    }

    [Fact]
    public void WritesAListenerFunctionPerEvent()
    {
        var ts = Harness.Compile("""
            [Service]
            public class Clock
            {
                /// <summary>Fires every second.</summary>
                public Event<DateTimeOffset> Tick { get; } = new();
            }
            """).TypeScript;

        Assert.Contains("""
              /**
               * Fires every second.
               * Calls listener with each payload of Clock.Tick; returns a function that stops listening.
               */
              onTick(listener: (payload: string) => void): () => void {
                return on<string>("Clock.Tick", listener);
              },
            """.Replace("\r", ""), ts);
        Assert.DoesNotContain("async function call", ts); // no methods, no call helper
    }
}
