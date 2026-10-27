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

/// <summary>A test that needs Inno Setup 6 and the Windows SDK's signtool: skipped without them.</summary>
public sealed class SignToolFactAttribute : FactAttribute
{
    public SignToolFactAttribute()
    {
        if (WriteInnoSetupScriptTests.Compiler() is null || SignTool() is null)
            Skip = "Inno Setup 6 or the Windows SDK's signtool is not installed";
    }

    /// <summary>The newest x64 signtool.exe of the Windows SDK, or null.</summary>
    public static string? SignTool()
    {
        var kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        return Directory.Exists(kits)
            ? Directory.GetDirectories(kits).OrderByDescending(d => d, StringComparer.Ordinal).Select(d => Path.Combine(d, "x64", "signtool.exe")).FirstOrDefault(File.Exists)
            : null;
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
    public void RegistersTheSchemesAndFileTypesOfTheProject()
    {
        var task = Task();
        task.UrlSchemes = " MyApp ; ";
        task.FileTypes = "note;.Notes";
        Assert.True(task.Execute(), string.Join("\n", _engine.Errors));
        var script = File.ReadAllText(task.ScriptPath);
        Assert.Contains("Software\\Classes\\myapp\"", script);
        Assert.Contains("Software\\Classes\\.note\\OpenWithProgids", script);
        Assert.Contains("Software\\Classes\\.notes\\OpenWithProgids", script);
    }

    [Theory]
    [InlineData("https", null, "OrielUrlSchemes 'https' is not a scheme")]
    [InlineData(null, ".no te", "OrielFileTypes '.no te' is not an extension")]
    public void RefusesSchemesAndFileTypesThatAreNot(string? schemes, string? types, string error)
    {
        var task = Task();
        task.UrlSchemes = schemes;
        task.FileTypes = types;
        Assert.False(task.Execute());
        Assert.Contains(error, Assert.Single(_engine.Errors));
    }

    [Fact]
    public void SignsWithTheCommandItIsGiven()
    {
        var task = Task();
        task.SignCommand = "signtool sign /f \"cert.pfx\" {file}";
        Assert.True(task.Execute(), string.Join("\n", _engine.Errors));
        Assert.Equal("/Soriel=signtool sign /f $qcert.pfx$q $f", task.SignToolArgument);
        Assert.Contains("SignTool=oriel", File.ReadAllText(task.ScriptPath));

        var unsigned = Task();
        Assert.True(unsigned.Execute());
        Assert.Equal("", unsigned.SignToolArgument);
    }

    [Fact]
    public void RefusesASignCommandWithoutTheFile()
    {
        var task = Task();
        task.SignCommand = "signtool sign /f cert.pfx";
        Assert.False(task.Execute());
        Assert.Contains("OrielSignCommand has no {file}", Assert.Single(_engine.Errors));
    }

    /// <summary>
    /// A signed installer builds: Inno Setup runs the sign command on the
    /// installer and the uninstaller, and checks they are signed. With a
    /// certificate made for the test, in memory and a file of its own, never
    /// in the user's store.
    /// </summary>
    [SignToolFact]
    public void ItsSignedScriptSignsTheInstaller()
    {
        var pfx = Path.Combine(_dir, "test.pfx");
        using (var key = System.Security.Cryptography.RSA.Create(2048))
        {
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=Oriel test signing", key, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension(
                [new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.3")], critical: false)); // code signing
            using var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
            File.WriteAllBytes(pfx, certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "test"));
        }
        var task = Task();
        task.SignCommand = $"\"{SignToolFactAttribute.SignTool()}\" sign /fd SHA256 /f \"{pfx}\" /p test {{file}}";
        Assert.True(task.Execute(), string.Join("\n", _engine.Errors));
        using var iscc = Process.Start(new ProcessStartInfo(Compiler()!)
        {
            ArgumentList = { "/Q", task.SignToolArgument, task.ScriptPath },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;
        var errors = iscc.StandardError.ReadToEnd();
        iscc.WaitForExit();
        Assert.True(iscc.ExitCode == 0, errors);
        var installer = Path.Combine(_dir, "installer", "Oriel-Demo-Setup-0.3.0.exe");
#pragma warning disable SYSLIB0057 // the signer of a signed file has no other API
        using var signed = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(installer);
#pragma warning restore SYSLIB0057
        using var signer = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(signed.GetRawCertData());
        Assert.Equal("CN=Oriel test signing", signer.Subject);
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
        var task = Task();
        task.UrlSchemes = "oriel-demo";
        task.FileTypes = ".orieldemo";
        Assert.True(task.Execute());
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
