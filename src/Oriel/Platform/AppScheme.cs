namespace Oriel.Platform;

/// <summary>
/// The app's files at <c>oriel://app.oriel/</c>, for the WebKit backends:
/// WebKit serves custom schemes only, never a custom https host. The rest
/// of Oriel keeps seeing <see cref="Frontend.AppOrigin"/>: windows map one
/// to the other (<see cref="ToPage"/>, <see cref="FromPage"/>).
/// </summary>
internal static class AppScheme
{
    public const string Scheme = "oriel";

    /// <summary>The URL the webview loads for <paramref name="uri"/>: the app's files at oriel://, anything else as is.</summary>
    public static string ToPage(Uri uri) =>
        uri.Scheme == "https" && uri.IsDefaultPort && string.Equals(uri.Host, Frontend.AppHost, StringComparison.OrdinalIgnoreCase)
            ? Scheme + "://" + Frontend.AppHost + uri.PathAndQuery + uri.Fragment
            : uri.AbsoluteUri;

    /// <summary>The URL Oriel knows for <paramref name="uri"/>, one of the webview's: https://app.oriel for the app's files.</summary>
    public static string FromPage(string uri) =>
        uri.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase) ? "https://" + uri[(Scheme.Length + 3)..] : uri;

    /// <summary>The path of the file a URL path names: a folder's is its index.html.</summary>
    public static string FilePath(string path) =>
        path.Length == 0 || path.EndsWith('/') ? path + "index.html" : path;

    /// <summary>
    /// The app's file at <paramref name="path"/>, from <see cref="FilePath"/>,
    /// from the executable or the folder; null when there is none.
    /// </summary>
    public static byte[]? Read(string path, string? assetsFolder, EmbeddedFrontend? embedded)
    {
        if (embedded is not null)
            return embedded.Read(path);
        if (assetsFolder is null)
            return null;
        var root = Path.GetFullPath(assetsFolder);
        var full = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(path).TrimStart('/')));
        // Nothing outside the folder, whatever the path says.
        if (!full.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal) || !File.Exists(full))
            return null;
        return File.ReadAllBytes(full);
    }
}
