using System.Collections;
using System.Diagnostics;
using Microsoft.Build.Framework;

namespace Oriel.Build.Tests;

/// <summary>A test that needs Inno Setup 6 installed: skipped without it.</summary>
public sealed class InnoSetupFactAttribute : FactAttribute
{
    public InnoSetupFactAttribute()
    {
        if (WriteInnoSetupScriptTests.Compiler() is null)
            Skip = "Inno Setup 6 is not installed (winget install JRSoftware.InnoSetup)";
    }
}

public sealed class WriteInnoSetupScriptTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oriel-installer-" + Guid.NewGuid().ToString("N"));
    private readonly Engine _engine = new();

    public WriteInnoSetupScriptTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "publish", "wwwroot"));
        File.WriteAllText(Path.Combine(_dir, "publish", "Demo.exe"), "not really an executable");
        File.WriteAllText(Path.Combine(_dir, "publish", "Demo.pdb"), "symbols");
        File.WriteAllText(Path.Combine(_dir, "publish", "wwwroot", "index.html"), "<!doctype html>");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The installed Inno Setup compiler, as Oriel.targets looks for it.</summary>
    public static string? Compiler() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inno Setup 6", "ISCC.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe"),
    }.FirstOrDefault(File.Exists);

    private WriteInnoSetupScript Task(string? appId = null, string appName = "Oriel Demo") => new()
    {
        BuildEngine = _engine,
        AppName = appName,
        AppVersion = "0.3.0",
        AppId = appId,
        Publisher = "Oriel contributors",
        Executable = "Demo.exe",
        PublishDirectory = Path.Combine(_dir, "publish"),
        OutputDirectory = Path.Combine(_dir, "installer"),
        Languages = "english; French",
        ScriptPath = Path.Combine(_dir, "obj", "installer.iss"),
    };

    [Fact]
    public void WritesTheScriptWithAnAppIdOfTheName()
    {
        Assert.True(Task().Execute(), string.Join("\n", _engine.Errors));
        var script = File.ReadAllText(Path.Combine(_dir, "obj", "installer.iss"));
        Assert.Contains($"AppId={{{{{InnoSetupScript.StableAppId("Oriel Demo").ToString().ToUpperInvariant()}}}\n", script);
        Assert.Contains("Name: \"french\"", script);
    }

    [Fact]
    public void UsesTheAppIdItIsGiven()
    {
        Assert.True(Task("{0F8C2B9E-1D3A-4C5B-8E7F-6A5B4C3D2E1F}").Execute());
        Assert.Contains("AppId={{0F8C2B9E-1D3A-4C5B-8E7F-6A5B4C3D2E1F}\n", File.ReadAllText(Path.Combine(_dir, "obj", "installer.iss")));
    }

    [Fact]
    public void WarnsAboutTheAppIdOfAnExample()
    {
        Assert.True(Task("B13EF445-3A27-45B7-82B4-62BA76E87C5B").Execute());
        var warning = Assert.Single(_engine.Warnings);
        Assert.StartsWith("Oriel: OrielAppId B13EF445-3A27-45B7-82B4-62BA76E87C5B is the AppId of the example 'Oriel Hello'.", warning);
        Assert.Contains("set OrielAppId to a GUID of your own", warning);
    }

    [Fact]
    public void TheExampleItselfKeepsItsAppIdWithoutAWarning()
    {
        Assert.True(Task("{b13ef445-3a27-45b7-82b4-62ba76e87c5b}", "Oriel Hello").Execute());
        Assert.Empty(_engine.Warnings);
    }

    [Fact]
    public void FailsWithoutThePublishedExecutable()
    {
        File.Delete(Path.Combine(_dir, "publish", "Demo.exe"));
        Assert.False(Task().Execute());
        Assert.Contains("Demo.exe does not exist", Assert.Single(_engine.Errors));
    }

    [Fact]
    public void FailsWithAnAppIdThatIsNotAGuid()
    {
        Assert.False(Task("not-a-guid").Execute());
        Assert.Equal("Oriel: OrielAppId 'not-a-guid' is not a GUID.", Assert.Single(_engine.Errors));
    }

    [InnoSetupFact]
    public void ItsScriptCompilesWithInnoSetup()
    {
        Assert.True(Task().Execute());
        using var iscc = Process.Start(new ProcessStartInfo(Compiler()!, $"/Q \"{Path.Combine(_dir, "obj", "installer.iss")}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;
        var errors = iscc.StandardError.ReadToEnd();
        iscc.WaitForExit();
        Assert.True(iscc.ExitCode == 0, errors);
        Assert.True(File.Exists(Path.Combine(_dir, "installer", "Oriel-Demo-Setup-0.3.0.exe")));
    }

    /// <summary>The build engine of the tests: it keeps the errors tasks log.</summary>
    private sealed class Engine : IBuildEngine
    {
        public List<string> Errors { get; } = [];

        public List<string> Warnings { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "";

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message!);

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e.Message!);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => false;
    }
}
