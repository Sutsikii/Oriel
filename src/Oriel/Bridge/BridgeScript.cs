namespace Oriel.Bridge;

/// <summary>The page runtime, bridge.js, which windows inject into their pages.</summary>
internal static class BridgeScript
{
    private static readonly Lazy<string> s_source = new(Load);

    /// <summary>The source of bridge.js, embedded in the assembly.</summary>
    public static string Source => s_source.Value;

    private static string Load()
    {
        using var stream = typeof(BridgeScript).Assembly.GetManifestResourceStream("Oriel.Bridge.bridge.js")
            ?? throw new InvalidOperationException("bridge.js is not embedded in Oriel.dll");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
