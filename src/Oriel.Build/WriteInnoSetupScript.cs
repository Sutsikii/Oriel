using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Oriel.Build;

/// <summary>
/// Writes the Inno Setup script of the app's installer, which the
/// <c>OrielBuildInstaller</c> target then compiles. Oriel.targets sets its
/// parameters from the project's properties.
/// </summary>
public sealed class WriteInnoSetupScript : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// The AppIds of the examples, with the names they ship under. An app
    /// made by copying an example keeps its AppId, and its installs then
    /// replace the example's, or another copy's.
    /// </summary>
    private static readonly Dictionary<Guid, string> ExampleAppIds = new()
    {
        [new Guid("B13EF445-3A27-45B7-82B4-62BA76E87C5B")] = "Oriel Hello",
        [new Guid("40CE192C-19D9-4610-8190-F2186238026E")] = "Chess Next Move",
    };

    /// <summary>The app's name, as users see it.</summary>
    [Required]
    public string AppName { get; set; } = "";

    /// <summary>The app's version.</summary>
    [Required]
    public string AppVersion { get; set; } = "";

    /// <summary>The app's AppId; derived from <see cref="AppName"/> when empty.</summary>
    public string? AppId { get; set; }

    /// <summary>Who makes the app.</summary>
    [Required]
    public string Publisher { get; set; } = "";

    /// <summary>The file name of the app's executable.</summary>
    [Required]
    public string Executable { get; set; } = "";

    /// <summary>The folder of the published app.</summary>
    [Required]
    public string PublishDirectory { get; set; } = "";

    /// <summary>Where the installer goes.</summary>
    [Required]
    public string OutputDirectory { get; set; } = "";

    /// <summary>The installer's icon, or empty.</summary>
    public string? Icon { get; set; }

    /// <summary>The wizard's languages, separated by semicolons.</summary>
    public string Languages { get; set; } = "english";

    /// <summary>The schemes of links the app opens, separated by semicolons: <c>myapp</c>.</summary>
    public string? UrlSchemes { get; set; }

    /// <summary>The extensions of files the app opens, separated by semicolons: <c>.note;.notes</c>.</summary>
    public string? FileTypes { get; set; }

    /// <summary>The command that signs a file, its <c>{file}</c> the file: OrielSignCommand. Empty for none.</summary>
    public string? SignCommand { get; set; }

    /// <summary>The compiler's argument that defines the sign tool of <see cref="SignCommand"/>, or empty.</summary>
    [Output]
    public string SignToolArgument { get; private set; } = "";

    /// <summary>Where the script is written.</summary>
    [Required]
    public string ScriptPath { get; set; } = "";

    private static IEnumerable<string> List(string? items) =>
        (items ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(i => i.Trim()).Where(i => i.Length > 0);

    /// <inheritdoc />
    public override bool Execute()
    {
        var publish = Path.GetFullPath(PublishDirectory);
        var executable = Path.Combine(publish, Executable);
        if (!File.Exists(executable))
        {
            Log.LogError($"Oriel: the installer needs the app's executable, and {executable} does not exist.");
            return false;
        }
        if (!string.IsNullOrEmpty(AppId) && !Guid.TryParse(AppId, out _))
        {
            Log.LogError($"Oriel: OrielAppId '{AppId}' is not a GUID.");
            return false;
        }
        if (!string.IsNullOrEmpty(AppId) && ExampleAppIds.TryGetValue(Guid.Parse(AppId), out var example) && AppName != example)
        {
            Log.LogWarning(
                $"Oriel: OrielAppId {AppId} is the AppId of the example '{example}'. Installing '{AppName}' would replace that " +
                $"example, or any other app copied from it: set OrielAppId to a GUID of your own, such as {Guid.NewGuid().ToString().ToUpperInvariant()}.");
        }

        var appId = string.IsNullOrEmpty(AppId) ? InnoSetupScript.StableAppId(AppName).ToString() : AppId!;
        var languages = Languages.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => l.Length > 0)
            .ToList();
        var schemes = List(UrlSchemes).Select(l => l.ToLowerInvariant()).ToList();
        var types = List(FileTypes).Select(t => (t.StartsWith(".", StringComparison.Ordinal) ? t : "." + t).ToLowerInvariant()).ToList();
        foreach (var scheme in schemes.Where(x => !InnoSetupScript.IsScheme(x)))
        {
            Log.LogError($"Oriel: OrielUrlSchemes '{scheme}' is not a scheme an app can take: letters first, then letters, digits, '+', '-' or '.', and not http, file or another the system has.");
            return false;
        }
        foreach (var type in types.Where(x => !InnoSetupScript.IsFileType(x)))
        {
            Log.LogError($"Oriel: OrielFileTypes '{type}' is not an extension, such as .note.");
            return false;
        }
        var signed = !string.IsNullOrWhiteSpace(SignCommand);
        if (signed && !SignCommand!.Contains("{file}"))
        {
            Log.LogError("Oriel: OrielSignCommand has no {file}, where the file to sign goes, such as: signtool sign /fd SHA256 /f cert.pfx {file}.");
            return false;
        }
        SignToolArgument = signed ? InnoSetupScript.SignToolArgument(SignCommand!) : "";
        var settings = new InstallerSettings(
            AppName,
            AppVersion,
            appId,
            Publisher,
            Executable,
            publish,
            Path.GetFullPath(OutputDirectory),
            string.IsNullOrEmpty(Icon) ? null : Path.GetFullPath(Icon),
            languages.Count == 0 ? new[] { "english" } : languages,
            schemes,
            types,
            signed);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScriptPath))!);
        File.WriteAllText(ScriptPath, InnoSetupScript.Create(settings));
        Log.LogMessage(MessageImportance.Low, $"Oriel: installer script at {ScriptPath}, AppId {appId}");
        return true;
    }
}
