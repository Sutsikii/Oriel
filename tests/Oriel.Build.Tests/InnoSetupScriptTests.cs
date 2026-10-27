namespace Oriel.Build.Tests;

public class InnoSetupScriptTests
{
    private static InstallerSettings Settings(string name = "My App", string? icon = null, params string[] languages) => new(
        name,
        "1.2.0",
        "7c3e9a51-2b4d-4f6e-9c8a-1d5b3e7f2a90",
        "Me",
        "MyApp.exe",
        @"C:\app\publish\",
        @"C:\app\installer",
        icon,
        languages.Length == 0 ? ["english"] : languages);

    [Fact]
    public void RegistersTheSchemesAndFileTypesItIsGiven()
    {
        var script = InnoSetupScript.Create(Settings() with { UrlSchemes = ["myapp"], FileTypes = [".note"] });
        Assert.Contains("ChangesAssociations=yes\n", script);
        Assert.Contains("\n[Registry]\n", script);
        Assert.Contains("Root: HKA; Subkey: \"Software\\Classes\\myapp\"; ValueType: string; ValueName: \"\"; ValueData: \"URL:My App\"; Flags: uninsdeletekey\n", script);
        Assert.Contains("Root: HKA; Subkey: \"Software\\Classes\\myapp\"; ValueType: string; ValueName: \"URL Protocol\"; ValueData: \"\"\n", script);
        Assert.Contains("Root: HKA; Subkey: \"Software\\Classes\\myapp\\shell\\open\\command\"; ValueType: string; ValueName: \"\"; ValueData: \"\"\"{app}\\MyApp.exe\"\" \"\"%1\"\"\"\n", script);
        Assert.Contains("Root: HKA; Subkey: \"Software\\Classes\\.note\\OpenWithProgids\"; ValueType: string; ValueName: \"My-App.note\"; ValueData: \"\"; Flags: uninsdeletevalue\n", script);
        Assert.Contains("Root: HKA; Subkey: \"Software\\Classes\\My-App.note\\shell\\open\\command\"; ValueType: string; ValueName: \"\"; ValueData: \"\"\"{app}\\MyApp.exe\"\" \"\"%1\"\"\"\n", script);
    }

    [Fact]
    public void RegistersNothingWithoutSchemesOrFileTypes()
    {
        var script = InnoSetupScript.Create(Settings());
        Assert.DoesNotContain("[Registry]", script);
        Assert.DoesNotContain("ChangesAssociations", script);
    }

    [Theory]
    [InlineData("myapp", true)]
    [InlineData("my-app+x.y", true)]
    [InlineData("1app", false)]
    [InlineData("my app", false)]
    [InlineData("m", false)]
    [InlineData("https", false)]
    [InlineData("File", false)]
    public void TakesSchemesAppsMayHave(string scheme, bool ok) => Assert.Equal(ok, InnoSetupScript.IsScheme(scheme));

    [Theory]
    [InlineData(".note", true)]
    [InlineData(".my-notes_2", true)]
    [InlineData("note", false)]
    [InlineData(".", false)]
    [InlineData(".no te", false)]
    [InlineData(".a;b", false)]
    public void TakesExtensions(string type, bool ok) => Assert.Equal(ok, InnoSetupScript.IsFileType(type));

    [Fact]
    public void InstallsForTheUserWithoutAdministratorRights()
    {
        var script = InnoSetupScript.Create(Settings());
        Assert.Contains("PrivilegesRequired=lowest\n", script);
        Assert.Contains("DefaultDirName={autopf}\\My App\n", script);
    }

    [Fact]
    public void DoublesTheBraceThatStartsTheAppId()
    {
        Assert.Contains("AppId={{7C3E9A51-2B4D-4F6E-9C8A-1D5B3E7F2A90}\n", InnoSetupScript.Create(Settings()));
    }

    [Fact]
    public void InstallsThePublishedFilesButTheirSymbols()
    {
        Assert.Contains(
            "Source: \"C:\\app\\publish\\*\"; DestDir: \"{app}\"; Excludes: \"*.pdb\"; Flags: ignoreversion recursesubdirs createallsubdirs\n",
            InnoSetupScript.Create(Settings()));
    }

    [Fact]
    public void AddsShortcutsAndALaunchAtTheEnd()
    {
        var script = InnoSetupScript.Create(Settings());
        Assert.Contains("Name: \"{autoprograms}\\My App\"; Filename: \"{app}\\MyApp.exe\"\n", script);
        Assert.Contains("Name: \"{autodesktop}\\My App\"; Filename: \"{app}\\MyApp.exe\"; Tasks: desktopicon\n", script);
        Assert.Contains("Description: \"{cm:LaunchProgram,My App}\"; Flags: nowait postinstall skipifsilent", script);
    }

    [Fact]
    public void KeepsTheUsersDataOnUninstall()
    {
        var script = InnoSetupScript.Create(Settings());
        Assert.Contains("Type: filesandordirs; Name: \"{localappdata}\\My App\\WebView\"\n", script);
        Assert.DoesNotContain("{userappdata}", script);
        Assert.DoesNotContain("{appdata}", script);
    }

    [Fact]
    public void ChecksForTheWebView2Runtime()
    {
        var script = InnoSetupScript.Create(Settings());
        Assert.Contains("function InitializeSetup: Boolean;", script);
        Assert.Contains("{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", script);
    }

    [Fact]
    public void EscapesWhatInnoSetupWouldReadAsSyntax()
    {
        var script = InnoSetupScript.Create(Settings("Tom & \"Jerry\" {Pro}, Inc"));
        Assert.Contains("AppName=Tom & \"Jerry\" {{Pro}, Inc\n", script);
        Assert.Contains("Name: \"{autoprograms}\\Tom & \"\"Jerry\"\" {{Pro}, Inc\"", script);
        Assert.Contains("{cm:LaunchProgram,Tom && \"\"Jerry\"\" {{Pro}%2c Inc}", script);
        Assert.Contains("OutputBaseFilename=Tom-&--Jerry--{Pro},-Inc-Setup-1.2.0\n", script);
    }

    [Fact]
    public void UsesTheIconWhenThereIsOne()
    {
        Assert.DoesNotContain("SetupIconFile", InnoSetupScript.Create(Settings()));
        Assert.Contains("SetupIconFile=C:\\app\\app.ico\n", InnoSetupScript.Create(Settings(icon: @"C:\app\app.ico")));
    }

    [Fact]
    public void ListsTheWizardsLanguages()
    {
        var script = InnoSetupScript.Create(Settings(languages: ["english", "french"]));
        Assert.Contains("Name: \"english\"; MessagesFile: \"compiler:Default.isl\"\n", script);
        Assert.Contains("Name: \"french\"; MessagesFile: \"compiler:Languages\\French.isl\"\n", script);
        Assert.Throws<ArgumentException>(() => InnoSetupScript.Create(Settings() with { Languages = [] }));
    }

    [Fact]
    public void RefusesAnAppIdThatIsNotAGuid()
    {
        Assert.Throws<ArgumentException>(() => InnoSetupScript.Create(Settings() with { AppId = "my-app" }));
    }

    [Fact]
    public void DerivesTheSameAppIdFromTheSameName()
    {
        var id = InnoSetupScript.StableAppId("My App");
        Assert.Equal(id, InnoSetupScript.StableAppId("My App"));
        Assert.NotEqual(id, InnoSetupScript.StableAppId("My Other App"));
        Assert.Equal('5', id.ToString()[14]); // a version 5 UUID
        Assert.Contains(id.ToString()[19], "89ab");
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.0-beta.4", "1.2.0")]
    [InlineData("2", "2.0")]
    [InlineData("1.2.3.4.5", "1.2.3.4")]
    [InlineData("1.x", "1.0")]
    public void GivesWindowsAVersionOfNumbers(string version, string expected)
    {
        Assert.Equal(expected, InnoSetupScript.NumericVersion(version));
    }
}
