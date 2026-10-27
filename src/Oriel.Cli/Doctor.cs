using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Oriel.Platform.Windows;

namespace Oriel.Cli;

/// <summary>What <see cref="Doctor"/> asks of the machine, so that tests can answer.</summary>
internal interface IProbe
{
    OSPlatform Os { get; }

    /// <summary>The first line a command writes, or null when it does not run or fails.</summary>
    string? Output(string file, string arguments);

    bool FileExists(string path);

    /// <summary>Whether the native library <paramref name="name"/> loads.</summary>
    bool CanLoad(string name);

    string? Environment(string name);

    /// <summary>Where the WebView2 Runtime is, or why there is none.</summary>
    (string? Directory, string? Problem) WebView2();
}

/// <summary>A check of <see cref="Doctor"/>, and what to do when it fails.</summary>
/// <param name="Required">Whether apps cannot be built or run without it; else, only some steps need it.</param>
internal sealed record Check(string Name, bool Required, bool Ok, string Detail, string? Fix);

/// <summary>
/// oriel doctor: checks what building, running and shipping an Oriel app
/// needs on this machine, and says how to get what is missing.
/// </summary>
internal static partial class Doctor
{
    public static int Run(TextWriter output, IProbe probe)
    {
        var checks = Checks(probe);
        foreach (var check in checks)
        {
            var mark = check.Ok ? "ok  " : check.Required ? "MISSING" : "--  ";
            output.WriteLine($"{mark} {check.Name}: {check.Detail}");
            if (!check.Ok && check.Fix is not null)
                output.WriteLine($"       {check.Fix}");
        }
        var missing = checks.Count(c => c.Required && !c.Ok);
        output.WriteLine();
        output.WriteLine(missing == 0
            ? "Everything an Oriel app needs is here. Lines marked -- are only for some steps."
            : $"{missing} thing{(missing == 1 ? " is" : "s are")} missing to build or run Oriel apps.");
        return missing == 0 ? 0 : 1;
    }

    public static List<Check> Checks(IProbe probe)
    {
        var checks = new List<Check>
        {
            Versioned(probe, ".NET SDK", "dotnet", "--version", 10, required: true, "Install the .NET 10 SDK: https://dotnet.microsoft.com/download"),
            Versioned(probe, "Node.js", "node", "--version", 20, required: true, "Install Node.js 20 or later, for the frontend: https://nodejs.org"),
        };
        if (probe.Os == OSPlatform.Windows)
        {
            var (directory, problem) = probe.WebView2();
            checks.Add(new Check("WebView2 Runtime", true, directory is not null, directory ?? problem ?? "not found",
                "Install it: https://go.microsoft.com/fwlink/p/?LinkId=2124703 (Windows 11 has it)"));
            checks.Add(CppTools(probe));
            var inno = InnoSetup(probe);
            checks.Add(new Check("Inno Setup 6, for installers (OrielInstaller)", false, inno is not null, inno ?? "not found",
                "winget install JRSoftware.InnoSetup"));
        }
        else if (probe.Os == OSPlatform.Linux)
        {
            var gtk = probe.CanLoad("libgtk-3.so.0") && probe.CanLoad("libwebkit2gtk-4.1.so.0");
            checks.Add(new Check("GTK 3 and WebKitGTK 4.1", true, gtk, gtk ? "found" : "not found",
                "Install them: sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 (Debian, Ubuntu), or your system's packages"));
            var clang = probe.Output("clang", "--version");
            checks.Add(new Check("clang, for native executables (PublishAot)", false, clang is not null, clang ?? "not found",
                "sudo apt install clang zlib1g-dev, or your system's packages"));
        }
        else if (probe.Os == OSPlatform.OSX)
        {
            var tools = probe.Output("xcode-select", "-p");
            checks.Add(new Check("Xcode command line tools, for native executables (PublishAot)", false, tools is not null, tools ?? "not found",
                "xcode-select --install"));
        }
        return checks;
    }

    private static Check Versioned(IProbe probe, string name, string file, string arguments, int major, bool required, string fix)
    {
        var output = probe.Output(file, arguments);
        if (output is null)
            return new Check(name, required, false, "not found", fix);
        var match = VersionPattern().Match(output);
        var ok = match.Success && int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= major;
        return new Check(name, required, ok, ok ? output.Trim() : $"{output.Trim()}, older than {major}", fix);
    }

    /// <summary>The C++ build tools NativeAOT links with, through vswhere.</summary>
    private static Check CppTools(IProbe probe)
    {
        const string Name = "C++ build tools, for native executables (PublishAot)";
        const string Fix = "Install Visual Studio, or its Build Tools, with \"Desktop development with C++\"";
        var vswhere = Path.Combine(probe.Environment("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!probe.FileExists(vswhere))
            return new Check(Name, false, false, "no Visual Studio installer", Fix);
        var path = probe.Output(vswhere, "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath");
        return new Check(Name, false, !string.IsNullOrWhiteSpace(path), string.IsNullOrWhiteSpace(path) ? "not found" : path!, Fix);
    }

    /// <summary>ISCC.exe, where Oriel.targets looks for it.</summary>
    private static string? InnoSetup(IProbe probe)
    {
        var configured = probe.Environment("OrielInnoSetupCompiler");
        string?[] candidates =
        [
            configured,
            Path.Combine(probe.Environment("LOCALAPPDATA") ?? "", "Programs", "Inno Setup 6", "ISCC.exe"),
            Path.Combine(probe.Environment("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Inno Setup 6", "ISCC.exe"),
            Path.Combine(probe.Environment("ProgramW6432") ?? @"C:\Program Files", "Inno Setup 6", "ISCC.exe"),
        ];
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && probe.FileExists(c!));
    }

    [GeneratedRegex(@"v?(\d+)\.\d+")]
    private static partial Regex VersionPattern();
}

/// <summary>The machine as it is.</summary>
internal sealed class SystemProbe : IProbe
{
    public OSPlatform Os =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;

    public string? Output(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000) || process.ExitCode != 0)
                return null;
            return output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null; // not installed
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool CanLoad(string name)
    {
        if (!NativeLibrary.TryLoad(name, out var handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }

    public string? Environment(string name) => System.Environment.GetEnvironmentVariable(name);

    public (string? Directory, string? Problem) WebView2()
    {
        if (!OperatingSystem.IsWindows())
            return (null, "Windows only");
        try
        {
            return (WebView2Runtime.Find().Directory, null);
        }
        catch (WebViewUnavailableException e)
        {
            return (null, e.Message);
        }
    }
}
