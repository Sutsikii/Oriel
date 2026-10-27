using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Oriel.Cli;

/// <summary>
/// oriel dev: the frontend's dev server (Vite), then the app with
/// <c>dotnet watch</c> pointed at it through ORIEL_DEV_URL. Editing the page
/// reloads it; editing C# restarts the app, whose build rewrites the client.
/// The same as the templates' <c>npm run dev</c>, without a script of the
/// project's.
/// </summary>
internal static partial class Dev
{
    public static async Task<int> RunAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        var projects = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.csproj") : [];
        if (projects.Length != 1)
        {
            Console.Error.WriteLine(projects.Length == 0
                ? $"oriel dev: {folder} has no .csproj: run it in the app's folder, or give the folder."
                : $"oriel dev: {folder} has several .csproj files; give the folder of one app.");
            return 1;
        }
        var frontend = Path.Combine(folder, "frontend");
        if (!File.Exists(Path.Combine(frontend, "package.json")))
        {
            Console.Error.WriteLine($"oriel dev: no frontend/package.json in {folder}.");
            return 1;
        }
        if (!Directory.Exists(Path.Combine(frontend, "node_modules")))
        {
            Console.WriteLine("oriel dev: npm install, once");
            using var install = Start(Npm("install"), frontend, redirect: false);
            await install.WaitForExitAsync();
            if (install.ExitCode != 0)
                return install.ExitCode;
        }

        using var vite = Start(Npm("exec", "--", "vite"), frontend, redirect: true);
        var url = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        vite.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            Console.WriteLine(e.Data);
            if (FindUrl(e.Data) is { } found)
                url.TrySetResult(found);
        };
        vite.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Console.Error.WriteLine(e.Data);
        };
        vite.BeginOutputReadLine();
        vite.BeginErrorReadLine();
        var started = await Task.WhenAny(url.Task, vite.WaitForExitAsync(), Task.Delay(TimeSpan.FromMinutes(1)));
        if (started != url.Task)
        {
            Console.Error.WriteLine("oriel dev: the dev server did not start.");
            Stop(vite);
            return 1;
        }

        var app = new ProcessStartInfo("dotnet", ["watch", "run", "--project", projects[0]])
        {
            UseShellExecute = false,
            WorkingDirectory = folder,
        };
        app.Environment["ORIEL_DEV_URL"] = url.Task.Result;
        app.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "true";
        using var watch = Process.Start(app)!;
        // Ctrl+C reaches both, which share the console; the dev server goes with the app.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        await watch.WaitForExitAsync();
        Stop(vite);
        return watch.ExitCode;
    }

    /// <summary>The URL of the dev server in a line it writes, such as <c>  ➜  Local:   http://localhost:5173/</c>.</summary>
    internal static string? FindUrl(string line)
    {
        var match = LocalUrl().Match(Ansi().Replace(line, ""));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>npm with <paramref name="arguments"/>: through cmd on Windows, where npm is a script.</summary>
    private static ProcessStartInfo Npm(params string[] arguments) =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", ["/c", "npm", .. arguments])
            : new ProcessStartInfo("npm", arguments);

    private static Process Start(ProcessStartInfo start, string folder, bool redirect)
    {
        start.WorkingDirectory = folder;
        start.UseShellExecute = false;
        start.RedirectStandardOutput = redirect;
        start.RedirectStandardError = redirect;
        return Process.Start(start) ?? throw new InvalidOperationException($"{start.FileName} did not start.");
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // it ended
        }
    }

    [GeneratedRegex(@"Local:\s+(https?://\S+)")]
    private static partial Regex LocalUrl();

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]")]
    private static partial Regex Ansi();
}
