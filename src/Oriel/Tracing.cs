using System.Diagnostics;

namespace Oriel;

/// <summary>
/// Traces what Oriel does to the standard error stream when <c>ORIEL_TRACE=1</c>:
/// windows, webviews, navigations and messages. For finding out where things
/// stop; see docs/contributing.md.
/// </summary>
internal static class Tracing
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("ORIEL_TRACE") == "1";

    private static readonly long s_start = Stopwatch.GetTimestamp();

    public static void Write(string message)
    {
        if (Enabled)
            Console.Error.WriteLine($"[oriel {Stopwatch.GetElapsedTime(s_start).TotalMilliseconds,8:F1}ms] {message}");
    }
}
