using System.Collections.Concurrent;
using Oriel.Generated;

namespace Oriel.Tests;

public class EventTests
{
    private sealed class Sink : IEventSink
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public void Broadcast(string message) => Messages.Enqueue(message);
    }

    [Fact]
    public void DoesNothingBeforeItsServiceIsBound()
    {
        var tick = new Event<int>();
        tick.Emit(1); // no sink yet: nothing to do, nothing thrown
    }

    [Fact]
    public void BroadcastsEventMessages()
    {
        var tick = new Event<int>();
        var sink = new Sink();
        tick.Attach("Clock.Tick", Json.Write, sink);
        tick.Emit(1);
        tick.Emit(2);
        Assert.Equal(["""{"t":"event","n":"Clock.Tick","v":1}""", """{"t":"event","n":"Clock.Tick","v":2}"""], sink.Messages);
    }

    [Fact]
    public void MarksCoalescedEventsForPagesToAcknowledge()
    {
        var moved = new Event<int> { Coalesce = true };
        var sink = new Sink(); // not coalescing: every message goes
        moved.Attach("Pointer.Moved", Json.Write, sink);
        moved.Emit(1);
        moved.Emit(2);
        Assert.Equal(["""{"t":"event","n":"Pointer.Moved","v":1,"c":1}""", """{"t":"event","n":"Pointer.Moved","v":2,"c":1}"""], sink.Messages);
    }

    [Fact]
    public void BindsOnce()
    {
        var tick = new Event<int>();
        tick.Attach("Clock.Tick", Json.Write, new Sink());
        var e = Assert.Throws<InvalidOperationException>(() => tick.Attach("Clock.Tick", Json.Write, new Sink()));
        Assert.Equal("The event Clock.Tick is already bound; a service instance can be bound once.", e.Message);
    }
}
