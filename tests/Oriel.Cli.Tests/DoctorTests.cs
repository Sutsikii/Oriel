using System.Runtime.InteropServices;

namespace Oriel.Cli.Tests;

public class DoctorTests
{
    /// <summary>A machine of answers given by the test.</summary>
    private sealed class Machine(OSPlatform os) : IProbe
    {
        public OSPlatform Os { get; } = os;

        public Dictionary<string, string> Outputs { get; } = [];

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Libraries { get; } = [];

        public Dictionary<string, string> Variables { get; } = [];

        public string? WebView2Directory { get; set; }

        public string? Output(string file, string arguments) => Outputs.GetValueOrDefault(file);

        public bool FileExists(string path) => Files.Contains(path);

        public bool CanLoad(string name) => Libraries.Contains(name);

        public string? Environment(string name) => Variables.GetValueOrDefault(name);

        public (string? Directory, string? Problem) WebView2() => (WebView2Directory, WebView2Directory is null ? "no WebView2 Runtime" : null);
    }

    private static (int Code, string Output) Run(Machine machine)
    {
        var output = new StringWriter();
        var code = Doctor.Run(output, machine);
        return (code, output.ToString());
    }

    [Fact]
    public void PassesAWindowsMachineWithEverything()
    {
        var machine = new Machine(OSPlatform.Windows)
        {
            WebView2Directory = @"C:\EdgeWebView\141.0",
            Variables = { ["ProgramFiles(x86)"] = @"C:\PF86", ["LOCALAPPDATA"] = @"C:\Users\ada\AppData\Local" },
            Outputs = { ["dotnet"] = "10.0.100", ["node"] = "v22.11.0", [@"C:\PF86\Microsoft Visual Studio\Installer\vswhere.exe"] = @"C:\VS\BuildTools" },
            Files = { @"C:\PF86\Microsoft Visual Studio\Installer\vswhere.exe", @"C:\Users\ada\AppData\Local\Programs\Inno Setup 6\ISCC.exe" },
        };
        var (code, output) = Run(machine);
        Assert.Equal(0, code);
        Assert.DoesNotContain("MISSING", output);
        Assert.Contains(@"ok   WebView2 Runtime: C:\EdgeWebView\141.0", output);
        Assert.Contains(@"ok   C++ build tools, for native executables (PublishAot): C:\VS\BuildTools", output);
        Assert.Contains("ok   Inno Setup 6", output);
    }

    [Fact]
    public void FailsWithoutWhatAppsNeedAndSaysHowToGetIt()
    {
        var machine = new Machine(OSPlatform.Windows) { Outputs = { ["dotnet"] = "9.0.300", ["node"] = "v22.11.0" } };
        var (code, output) = Run(machine);
        Assert.Equal(1, code);
        Assert.Contains("MISSING .NET SDK: 9.0.300, older than 10", output);
        Assert.Contains("MISSING WebView2 Runtime: no WebView2 Runtime", output);
        Assert.Contains("https://go.microsoft.com/fwlink/p/?LinkId=2124703", output);
        // Only some steps need these: not missing, but said.
        Assert.Contains("--   C++ build tools, for native executables (PublishAot): no Visual Studio installer", output);
        Assert.Contains("winget install JRSoftware.InnoSetup", output);
        Assert.Contains("2 things are missing", output);
    }

    [Fact]
    public void ChecksGtkAndWebKitOnLinux()
    {
        var machine = new Machine(OSPlatform.Linux) { Outputs = { ["dotnet"] = "10.0.100", ["node"] = "v20.0.0" } };
        Assert.Contains(Doctor.Checks(machine), c => c.Name == "GTK 3 and WebKitGTK 4.1" && c.Required && !c.Ok);
        machine.Libraries.UnionWith(["libgtk-3.so.0", "libwebkit2gtk-4.1.so.0"]);
        Assert.Equal(0, Run(machine).Code);
        Assert.DoesNotContain(Doctor.Checks(machine), c => c.Name.Contains("WebView2"));
    }

    [Fact]
    public void ChecksTheXcodeToolsOnMacOS()
    {
        var machine = new Machine(OSPlatform.OSX) { Outputs = { ["dotnet"] = "10.0.100", ["node"] = "v24.1.0" } };
        var xcode = Assert.Single(Doctor.Checks(machine), c => c.Name.StartsWith("Xcode", StringComparison.Ordinal));
        Assert.False(xcode.Required);
        Assert.Equal(0, Run(machine).Code);
    }

    [Theory]
    [InlineData("  \u001b[32m➜\u001b[39m  \u001b[1mLocal\u001b[22m:   \u001b[36mhttp://localhost:\u001b[1m5173\u001b[22m/\u001b[39m", "http://localhost:5173/")]
    [InlineData("  ➜  Local:   http://127.0.0.1:5174/", "http://127.0.0.1:5174/")]
    [InlineData("  ➜  Network: use --host to expose", null)]
    public void FindsTheUrlOfTheDevServer(string line, string? url) => Assert.Equal(url, Dev.FindUrl(line));
}
