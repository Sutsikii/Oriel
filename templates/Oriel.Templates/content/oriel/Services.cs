using Oriel;

namespace OrielTemplate;

/// <summary>A greeting, and when it was made.</summary>
public record Greeting(string Text, DateTimeOffset At);

/// <summary>Greets people. Its public methods and events are what pages can use.</summary>
[Service]
public class Greeter
{
    /// <summary>Greets <paramref name="name"/>.</summary>
    /// <param name="name">Who to greet.</param>
    public Greeting Greet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Give a name to greet.");
        Greeted.Emit(name);
        return new Greeting($"Hello, {name}!", DateTimeOffset.Now);
    }

    /// <summary>Fires when someone was greeted.</summary>
    public Event<string> Greeted { get; } = new();
}
