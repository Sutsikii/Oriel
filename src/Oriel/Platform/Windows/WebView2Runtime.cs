using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Oriel.Platform.Windows;

/// <summary>
/// Finds the Microsoft Edge WebView2 Runtime as WebView2Loader.dll does, so
/// that Oriel apps need no DLL of their own besides their executable.
/// </summary>
internal static class WebView2Runtime
{
    /// <summary>Where people get the runtime.</summary>
    public const string Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>The variable naming a fixed version runtime, as for the official loader.</summary>
    public const string FixedFolderVariable = "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER";

    // The runtime's EdgeUpdate channels, most stable first: the order the
    // loader tries them in.
    private static readonly string[] s_channels =
    [
        "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", // Stable (Evergreen)
        "{2CD8A007-E189-409D-A2C8-9AF4EF3C72AA}", // Beta
        "{0D50BFEC-CD6A-4F9A-964C-C7416E3ACB10}", // Dev
        "{65C35B14-6C1D-4122-AC46-7148CC9D6497}", // Canary
    ];

    private const string BrowserExe = "msedgewebview2.exe";
    private const string ClientDirectory = "EBWebView";
    private const string ClientLibrary = "EmbeddedBrowserWebView.dll";

    /// <summary>A runtime: its directory, and whether it is a fixed version one rather than an installed one.</summary>
    public readonly record struct Found(string Directory, bool IsFixed);

    /// <summary>Reads a string value of the machine's (HKLM) or the user's (HKCU) registry.</summary>
    public delegate string? ReadRegistry(bool machine, string key, string name);

    /// <summary>Finds the runtime this machine has.</summary>
    /// <exception cref="WebViewUnavailableException">There is none.</exception>
    [SupportedOSPlatform("windows")]
    public static Found Find() =>
        Find(Environment.GetEnvironmentVariable(FixedFolderVariable), ReadRegistry32, File.Exists, Architecture());

    /// <summary>
    /// Finds the runtime: the fixed version one in <paramref name="fixedFolder"/>
    /// when given, else the newest installed one of the most stable channel.
    /// </summary>
    public static Found Find(string? fixedFolder, ReadRegistry read, Func<string, bool> exists, string architecture)
    {
        if (!string.IsNullOrEmpty(fixedFolder))
        {
            if (ClientLibraryPath(fixedFolder, exists, architecture) is null)
                throw new WebViewUnavailableException($"{FixedFolderVariable}={fixedFolder} holds no WebView2 Runtime: no {BrowserExe}, or no {Path.Combine(ClientDirectory, architecture, ClientLibrary)}");
            return new Found(fixedFolder, IsFixed: true);
        }

        // EdgeUpdate records where it installed each channel, per machine and
        // per user, and keeps the record after the files are gone.
        string? stale = null;
        foreach (var channel in s_channels)
        {
            string? best = null;
            Version? bestVersion = null;
            foreach (var machine in new[] { true, false })
            {
                var dir = read(machine, $@"SOFTWARE\Microsoft\EdgeUpdate\ClientState\{channel}", ClientDirectory);
                if (string.IsNullOrEmpty(dir))
                    continue;
                if (ClientLibraryPath(dir, exists, architecture) is null)
                {
                    stale ??= dir;
                    continue;
                }
                var version = Version.TryParse(Path.GetFileName(dir.TrimEnd('\\', '/')), out var v) ? v : new Version(0, 0);
                if (best is null || version > bestVersion)
                {
                    best = dir;
                    bestVersion = version;
                }
            }
            if (best is not null)
                return new Found(best, IsFixed: false);
        }

        throw new WebViewUnavailableException(stale is null
            ? $"The Microsoft Edge WebView2 Runtime is not installed; get it from {Download}"
            : $"The WebView2 Runtime registered at {stale} is missing or incomplete; reinstall it from {Download}");
    }

    /// <summary>
    /// The client library of the runtime in <paramref name="dir"/>, or null
    /// when it holds none. The library alone is not a runtime: Edge keeps one
    /// beside itself whose environments fail, so the browser process must be
    /// there too.
    /// </summary>
    public static string? ClientLibraryPath(string dir, Func<string, bool> exists, string architecture)
    {
        if (!exists(Path.Combine(dir, BrowserExe)))
            return null;
        var library = Path.Combine(dir, ClientDirectory, architecture, ClientLibrary);
        return exists(library) ? library : null;
    }

    /// <summary>The runtime's name for the architecture of this process.</summary>
    public static string Architecture() => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        _ => "x64",
    };

    /// <summary>Reads the 32-bit view of the registry, where EdgeUpdate records its clients.</summary>
    [SupportedOSPlatform("windows")]
    private static string? ReadRegistry32(bool machine, string key, string name)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry32);
            using var sub = root.OpenSubKey(key);
            return sub?.GetValue(name) as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
