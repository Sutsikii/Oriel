using System.ComponentModel;
using System.Text.Json;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel;

/// <summary>
/// Something a service tells pages, with a payload of type <typeparamref name="T"/>.
/// Declare one as a property of a <see cref="ServiceAttribute"/> class, and
/// pages listen to it through the generated client: an event <c>Tick</c> of a
/// service <c>Clock</c> is <c>Clock.onTick(listener)</c>.
/// </summary>
/// <example>
/// <code>
/// [Service]
/// public class Clock
/// {
///     /// &lt;summary&gt;Fires every second with the time.&lt;/summary&gt;
///     public Event&lt;DateTimeOffset&gt; Tick { get; } = new();
/// }
/// </code>
/// </example>
public sealed class Event<T>
{
    private volatile Binding? _binding;

    /// <summary>
    /// Sends <paramref name="payload"/> to every page of the app. Any thread may
    /// call it; before the service is bound, it does nothing.
    /// </summary>
    public void Emit(T payload)
    {
        var binding = _binding;
        if (binding is null)
            return;
        binding.Sink.Broadcast(Protocol.EventMessage(binding.Name, w => binding.Write(w, payload)));
    }

    /// <summary>
    /// Sends <paramref name="payload"/> to the page of <paramref name="window"/>
    /// only, such as the window a call came from. Any thread may call it;
    /// before the service is bound, or once the window closed, it does nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The window is of another app than the service.</exception>
    public void EmitTo(OrielWindow window, T payload)
    {
        ArgumentNullException.ThrowIfNull(window);
        var binding = _binding;
        if (binding is null)
            return;
        if (!ReferenceEquals(window.App, binding.Sink))
            throw new ArgumentException($"The window is of another app than the one {binding.Name} is bound to.", nameof(window));
        window.Connection.Send(Protocol.EventMessage(binding.Name, w => binding.Write(w, payload)));
    }

    /// <summary>Connects the event to the app that bound its service. Generated code calls it.</summary>
    /// <param name="name">The name pages know it by: <c>Service.Event</c>.</param>
    /// <param name="write">Writes the payload's JSON.</param>
    /// <param name="sink">Delivers messages to the pages.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void Attach(string name, Action<Utf8JsonWriter, T> write, IEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(sink);
        if (Interlocked.CompareExchange(ref _binding, new Binding(name, write, sink), null) is not null)
            throw new InvalidOperationException($"The event {name} is already bound; a service instance can be bound once.");
    }

    private sealed record Binding(string Name, Action<Utf8JsonWriter, T> Write, IEventSink Sink);
}
