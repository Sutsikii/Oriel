namespace Oriel;

/// <summary>
/// Where an app's pages come from: its own files, served at
/// <see cref="AppOrigin"/>, or a dev server while developing. Only these two
/// origins may call services.
/// </summary>
internal sealed class Frontend
{
    /// <summary>The host the app's files are served at, from <see cref="AssetsFolder"/>.</summary>
    public const string AppHost = "app.oriel";

    /// <summary>The origin of the app's own pages.</summary>
    public const string AppOrigin = "https://" + AppHost;

    /// <summary>The variable that names the dev server, which <c>oriel dev</c> and the templates set.</summary>
    public const string DevUrlVariable = "ORIEL_DEV_URL";

    private readonly Uri? _devServer;

    public Frontend(string? devUrl, string assetsFolder, EmbeddedFrontend? embedded = null)
    {
        if (!string.IsNullOrEmpty(devUrl))
        {
            if (!Uri.TryCreate(devUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new ArgumentException($"{DevUrlVariable}={devUrl} is not an http(s) URL", nameof(devUrl));
            _devServer = uri;
        }
        AssetsFolder = assetsFolder;
        Embedded = embedded;
    }

    /// <summary>Whether pages come from a dev server.</summary>
    public bool IsDev => _devServer is not null;

    /// <summary>The folder of the app's built frontend.</summary>
    public string AssetsFolder { get; }

    /// <summary>The frontend embedded in the app, served instead of <see cref="AssetsFolder"/> when there is one.</summary>
    public EmbeddedFrontend? Embedded { get; }

    /// <summary>
    /// The URL a window opens: <paramref name="url"/> as is when absolute,
    /// else relative to the dev server or the app's files.
    /// </summary>
    public Uri Resolve(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return absolute;
        if (_devServer is not null)
            return new Uri(_devServer, url);
        // WebView2 serves the files of a folder, but no index of a directory.
        var resolved = new Uri(new Uri(AppOrigin + "/"), url);
        if (!resolved.AbsolutePath.EndsWith('/'))
            return resolved;
        return new UriBuilder(resolved) { Path = resolved.AbsolutePath + "index.html" }.Uri;
    }

    /// <summary>
    /// The file the app's own page <paramref name="url"/> would be served from
    /// when that file does not exist; null when it exists, or when the page
    /// is not one of the app's files (a dev server's, or another site's).
    /// </summary>
    public string? MissingFile(Uri url)
    {
        if (IsDev || url.Scheme != "https" || !string.Equals(url.Host, AppHost, StringComparison.OrdinalIgnoreCase))
            return null;
        if (Embedded is not null)
            return Embedded.Contains(url.AbsolutePath) ? null : "the embedded resource " + EmbeddedFrontend.ResourceName(url.AbsolutePath);
        var relative = Uri.UnescapeDataString(url.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(AssetsFolder, relative));
        return File.Exists(path) ? null : path;
    }

    /// <summary>Whether a document at <paramref name="source"/> may call services: it is one of the app's pages.</summary>
    public bool IsTrusted(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;
        if (_devServer is not null)
            return SameOrigin(uri, _devServer);
        return uri.Scheme == "https" && uri.IsDefaultPort && string.Equals(uri.Host, AppHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        a.Scheme == b.Scheme && a.Port == b.Port && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
}
