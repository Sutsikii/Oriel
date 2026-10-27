using Velopack;
using Velopack.Sources;

namespace Oriel.Updates;

/// <summary>Where the app stands: installed or not, its version, an update waiting for a restart.</summary>
/// <param name="Installed">Whether the app runs as installed by its Velopack setup; a build run from the IDE is not, and does not update.</param>
/// <param name="Version">The version running, or null when not installed.</param>
/// <param name="PendingRestart">The version downloaded and waiting for the app to restart, or null.</param>
public sealed record UpdateStatus(bool Installed, string? Version, string? PendingRestart);

/// <summary>A newer version, found by <see cref="Updates.Check"/>.</summary>
/// <param name="Version">Its version, such as <c>1.4.0</c>.</param>
/// <param name="Notes">Its release notes, in Markdown, when the release has them.</param>
public sealed record AvailableUpdate(string Version, string? Notes);

/// <summary>The app is not installed by its Velopack setup, so it cannot update: it runs from a build.</summary>
[ServiceError("not-installed")]
public sealed class NotInstalledException() : Exception("The app runs from a build, not as installed: it does not update.");

/// <summary>
/// Updates of the app, with Velopack: pages ask whether there is one, show
/// its download, and restart into it. Releases are made with Velopack's
/// <c>vpk pack</c> and published to a folder, a web server or GitHub; see
/// docs/updates.md.
/// </summary>
/// <example>
/// <code>
/// Updates.Run(); // first in Main
/// var app = new OrielApp(...);
/// app.Bind(new Updates("https://example.com/releases"));
/// </code>
/// <code>
/// const update = await Updates.check();
/// if (update) {
///   Updates.onProgress((percent) => (bar.value = percent));
///   await Updates.download();
///   await Updates.restart();
/// }
/// </code>
/// </example>
[Service(Name = "Updates", Concurrency = ServiceConcurrency.Serial)]
public sealed class Updates
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _found;
    private bool _downloaded;

    /// <summary>
    /// Updates from <paramref name="source"/>: the URL of a folder of
    /// releases on a web server, a local or network folder, or a GitHub
    /// repository's URL (<c>https://github.com/owner/repo</c>), whose
    /// releases hold them.
    /// </summary>
    public Updates(string source)
        : this(new UpdateManager(Source(source)))
    {
    }

    /// <summary>Updates through <paramref name="manager"/>, made as the app needs: a channel, a source of its own.</summary>
    public Updates(UpdateManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    /// <summary>
    /// Fires with the percent of a download, while <see cref="Download"/>
    /// runs. Coalesced: a page gets the latest only.
    /// </summary>
    public Event<int> Progress { get; } = new() { Coalesce = true };

    /// <summary>
    /// Handles what Velopack's setup and updates ask of the app as it
    /// starts (install, update and uninstall hooks), and returns. Call it
    /// first in <c>Main</c>, before anything else runs.
    /// </summary>
    [ServiceIgnore]
    public static void Run() => VelopackApp.Build().Run();

    /// <summary>Where the app stands.</summary>
    public UpdateStatus Status() => new(
        _manager.IsInstalled,
        _manager.IsInstalled ? _manager.CurrentVersion?.ToString() : null,
        _manager.UpdatePendingRestart?.Version.ToString());

    /// <summary>A newer version, or null when the app has the latest.</summary>
    /// <exception cref="NotInstalledException">The app runs from a build.</exception>
    public async Task<AvailableUpdate?> Check()
    {
        Installed();
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        _downloaded = false;
        return _found is null ? null : new AvailableUpdate(_found.TargetFullRelease.Version.ToString(), _found.TargetFullRelease.NotesMarkdown);
    }

    /// <summary>
    /// Downloads the version <see cref="Check"/> found, raising
    /// <see cref="Progress"/>: only what changed, when the release has deltas.
    /// </summary>
    /// <exception cref="InvalidOperationException">No update was found to download.</exception>
    public async Task Download(CancellationToken cancellation = default)
    {
        Installed();
        var found = _found ?? throw new InvalidOperationException("No update to download: Check finds one first.");
        await _manager.DownloadUpdatesAsync(found, Progress.Emit, cancellation).ConfigureAwait(false);
        _downloaded = true;
        Progress.Emit(100);
    }

    /// <summary>
    /// Closes the app, installs the version downloaded, and starts it again.
    /// The page goes with the app: whatever it holds should be saved first.
    /// </summary>
    /// <exception cref="InvalidOperationException">No update was downloaded.</exception>
    public void Restart()
    {
        Installed();
        var target = _downloaded ? _found!.TargetFullRelease : _manager.UpdatePendingRestart
            ?? throw new InvalidOperationException("No update downloaded: Download takes it first.");
        _manager.ApplyUpdatesAndRestart(target);
    }

    private void Installed()
    {
        if (!_manager.IsInstalled)
            throw new NotInstalledException();
    }

    /// <summary>The source of <paramref name="source"/>: GitHub, a web server, or a folder.</summary>
    internal static IUpdateSource Source(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(source, accessToken: null, prerelease: false)
                : new SimpleWebSource(uri);
        }
        return new SimpleFileSource(new DirectoryInfo(source));
    }
}
