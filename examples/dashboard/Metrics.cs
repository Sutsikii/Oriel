using System.Diagnostics;
using System.Runtime.CompilerServices;
using Oriel;

namespace Dashboard;

/// <summary>What the app measured, 120 times a second.</summary>
/// <param name="Seconds">Since the app started.</param>
/// <param name="Cpu">The app's share of the processors, in percent.</param>
/// <param name="MemoryMb">The app's working set.</param>
/// <param name="HeapMb">What the garbage collector holds.</param>
/// <param name="Signal">A waveform, to see 120 Hz move.</param>
public sealed record Sample(double Seconds, double Cpu, double MemoryMb, double HeapMb, int Threads, double Signal);

/// <summary>How bad a log entry is.</summary>
public enum Level
{
    Info,
    Warning,
    Error,
}

/// <summary>A line of the log.</summary>
public sealed record LogEntry(DateTimeOffset At, Level Level, string Message);

/// <summary>The app's metrics, as a coalesced event, and its log, as a stream.</summary>
[Service]
public sealed class Metrics
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// Fires 120 times a second. Coalesced: a page busy drawing, or hidden,
    /// gets the latest sample when it is ready, not a queue of old ones.
    /// </summary>
    public Event<Sample> Sampled { get; } = new() { Coalesce = true };

    /// <summary>Starts sampling, until the app ends. C#'s, not the pages'.</summary>
    [ServiceIgnore]
    public void Start() => _ = Task.Run(SampleAsync);

    /// <summary>
    /// The log, as it is written: a stream the page iterates. It stops when
    /// the page stops iterating, and runs no further ahead than the page reads.
    /// </summary>
    public async IAsyncEnumerable<LogEntry> Log([EnumeratorCancellation] CancellationToken cancellation = default)
    {
        string[] paths = ["/api/orders", "/api/customers", "/api/stock", "/health", "/api/invoices"];
        var random = new Random();
        var request = 0;
        while (true)
        {
            await Task.Delay(random.Next(20, 400), cancellation);
            var status = random.Next(100) switch { < 85 => 200, < 95 => 404, _ => 500 };
            var level = status switch { 200 => Level.Info, 404 => Level.Warning, _ => Level.Error };
            yield return new LogEntry(DateTimeOffset.Now, level, $"#{++request} GET {paths[random.Next(paths.Length)]} {status} in {random.Next(2, 180)} ms");
        }
    }

    private async Task SampleAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 120));
        using var process = Process.GetCurrentProcess();
        var lastCpu = process.TotalProcessorTime;
        var lastAt = _clock.Elapsed;
        var cpu = 0.0;
        var n = 0;
        while (await timer.WaitForNextTickAsync())
        {
            var now = _clock.Elapsed;
            // Processor time moves in steps: measured over a quarter of a second, smoothed.
            if (++n % 30 == 0)
            {
                process.Refresh();
                var used = process.TotalProcessorTime;
                var share = (used - lastCpu).TotalSeconds / (now - lastAt).TotalSeconds / Environment.ProcessorCount * 100;
                cpu = cpu * 0.5 + share * 0.5;
                (lastCpu, lastAt) = (used, now);
            }
            var t = now.TotalSeconds;
            Sampled.Emit(new Sample(
                t,
                Math.Round(cpu, 2),
                process.WorkingSet64 / 1048576.0,
                GC.GetTotalMemory(false) / 1048576.0,
                process.Threads.Count,
                Math.Sin(t * 2 * Math.PI * 0.5) * 0.6 + Math.Sin(t * 2 * Math.PI * 3.1) * 0.25 + (Random.Shared.NextDouble() - 0.5) * 0.1));
        }
    }
}
