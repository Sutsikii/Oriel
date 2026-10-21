using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Oriel;

namespace Hello;

/// <summary>How a greeting sounds.</summary>
public enum Tone
{
    Friendly,
    Formal,
    Pirate,
}

/// <summary>A greeting, and when it was made.</summary>
public record Greeting(string Text, Tone Tone, DateTimeOffset At);

/// <summary>Greets people.</summary>
[Service]
public class Greeter
{
    /// <summary>Greets <paramref name="name"/>.</summary>
    /// <param name="name">Who to greet.</param>
    /// <param name="tone">How the greeting sounds.</param>
    public Greeting Greet(string name, Tone tone = Tone.Friendly)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Give a name to greet.");
        var text = tone switch
        {
            Tone.Formal => $"Good day, {name}.",
            Tone.Pirate => $"Ahoy, {name}!",
            _ => $"Hello, {name}!",
        };
        return new Greeting(text, tone, DateTimeOffset.Now);
    }
}

/// <summary>Tells pages the time.</summary>
[Service]
public sealed class Clock : IDisposable
{
    private readonly Timer _timer;

    public Clock()
    {
        _timer = new Timer(_ => Tick.Emit(DateTimeOffset.Now), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    /// <summary>Fires every second with the time.</summary>
    public Event<DateTimeOffset> Tick { get; } = new();

    [ServiceIgnore]
    public void Dispose() => _timer.Dispose();
}

/// <summary>What runs the app.</summary>
public record MachineInfo(string System, string Runtime, int Processors, bool NativeAot);

/// <summary>Describes the machine.</summary>
[Service]
public class Machine
{
    /// <summary>The system and runtime the app runs on.</summary>
    public MachineInfo Describe() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.FrameworkDescription,
        Environment.ProcessorCount,
        NativeAot: !RuntimeFeature.IsDynamicCodeSupported);
}
