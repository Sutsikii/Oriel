namespace Oriel.Ipc;

/// <summary>What a page may send: see the limits of <see cref="AppOptions"/>.</summary>
internal sealed record PageLimits(int MaxMessageLength, int MaxMessageDepth, int MaxCallsInFlight)
{
    public static readonly PageLimits Default = From(new AppOptions { Name = "Default" });

    public static PageLimits From(AppOptions options) =>
        new(options.MaxMessageLength, options.MaxMessageDepth, options.MaxCallsInFlight);
}
