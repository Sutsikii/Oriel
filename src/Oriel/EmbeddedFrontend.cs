using System.Reflection;

namespace Oriel;

/// <summary>
/// A built frontend embedded in an assembly, as <c>OrielEmbedFrontend</c>
/// makes it: one resource per file, named <c>oriel-frontend/</c> and the
/// file's path. Pages are served from it instead of from <c>wwwroot</c>.
/// </summary>
internal sealed class EmbeddedFrontend
{
    /// <summary>The prefix of the resources' names; Oriel.targets writes it too.</summary>
    public const string Prefix = "oriel-frontend/";

    private readonly Assembly _assembly;

    // Paths compare as Windows compares the files of wwwroot, ignoring case.
    private readonly Dictionary<string, string> _resources = new(StringComparer.OrdinalIgnoreCase);

    private EmbeddedFrontend(Assembly assembly)
    {
        _assembly = assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // MSBuild keeps the separators of the file's folder: backslashes on Windows.
            if (name.StartsWith(Prefix, StringComparison.Ordinal))
                _resources[name[Prefix.Length..].Replace('\\', '/')] = name;
        }
    }

    /// <summary>The frontend embedded in <paramref name="assembly"/>, or null when it has none.</summary>
    public static EmbeddedFrontend? From(Assembly? assembly)
    {
        if (assembly is null)
            return null;
        var frontend = new EmbeddedFrontend(assembly);
        return frontend._resources.Count > 0 ? frontend : null;
    }

    /// <summary>The name of the resource that holds the file at the URL path <paramref name="path"/>.</summary>
    public static string ResourceName(string path) => Prefix + Relative(path);

    /// <summary>Whether the file at the URL path <paramref name="path"/> is embedded.</summary>
    public bool Contains(string path) => _resources.ContainsKey(Relative(path));

    /// <summary>The bytes of the file at the URL path <paramref name="path"/>, or null when it is not embedded.</summary>
    public byte[]? Read(string path)
    {
        if (!_resources.TryGetValue(Relative(path), out var name))
            return null;
        using var stream = _assembly.GetManifestResourceStream(name)!;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>The path relative to the frontend's root; a folder's is its index.html.</summary>
    private static string Relative(string path)
    {
        var relative = Uri.UnescapeDataString(path).TrimStart('/');
        return relative.Length == 0 || relative.EndsWith('/') ? relative + "index.html" : relative;
    }

    /// <summary>The Content-Type of a file, by its extension.</summary>
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".webmanifest" => "application/manifest+json; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        ".xml" => "application/xml; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        ".wasm" => "application/wasm",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };
}
