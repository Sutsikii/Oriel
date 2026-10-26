using Oriel.Platform.MacOS;
using Xunit.Runners;

namespace Oriel.EndToEnd.Tests;

/// <summary>
/// Runs the tests on macOS, where AppKit runs on the main thread only and
/// <c>dotnet test</c> keeps it: <c>dotnet run</c> runs the tests on other
/// threads, and the loops of their apps on this one. Arguments filter the
/// tests by name. Windows and Linux use <c>dotnet test</c>, which does not
/// call this.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("Run these tests with dotnet test; dotnet run is for macOS.");
            return 2;
        }

        var failed = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runner = AssemblyRunner.WithoutAppDomain(typeof(Program).Assembly.Location);
        if (args.Length > 0)
            runner.TestCaseFilter = test => args.Any(name => test.DisplayName.Contains(name, StringComparison.Ordinal));
        runner.OnTestPassed = test => Console.WriteLine($"passed  {test.TestDisplayName} ({test.ExecutionTime:0.0} s)");
        runner.OnTestSkipped = test => Console.WriteLine($"skipped {test.TestDisplayName}: {test.SkipReason}");
        runner.OnTestFailed = test =>
        {
            Interlocked.Increment(ref failed);
            Console.WriteLine($"FAILED  {test.TestDisplayName}\n{test.ExceptionType}: {test.ExceptionMessage}\n{test.ExceptionStackTrace}");
        };
        runner.OnExecutionComplete = end =>
        {
            Console.WriteLine($"{end.TotalTests} tests, {end.TestsFailed} failed, {end.TestsSkipped} skipped, in {end.ExecutionTime:0.0} s");
            done.TrySetResult();
        };
        runner.Start();
        MacPlatform.ServeMainThread(done.Task);
        // The runner is done a moment after it says so; it cannot be disposed before.
        while (runner.Status != AssemblyRunnerStatus.Idle)
            Thread.Sleep(50);
        return failed == 0 ? 0 : 1;
    }
}
