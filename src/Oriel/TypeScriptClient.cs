using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace Oriel;

/// <summary>
/// The TypeScript clients the source generator wrote: one per assembly with
/// <see cref="ServiceAttribute"/> classes, registered as the assembly loads.
/// </summary>
public static class TypeScriptClient
{
    private static readonly ConcurrentDictionary<Assembly, string> s_sources = new();

    /// <summary>Records the client of <paramref name="assembly"/>. Generated code calls it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register(Assembly assembly, string source)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(source);
        s_sources[assembly] = source;
    }

    /// <summary>The client of the services of <paramref name="assembly"/>, or null when it has none.</summary>
    public static string? For(Assembly assembly) =>
        s_sources.TryGetValue(assembly, out var source) ? source : null;

    /// <summary>
    /// Writes the client of <paramref name="assembly"/> to <paramref name="path"/>,
    /// creating its directory. The file is left untouched when it already
    /// holds the client, so that dev servers watching it do not reload for nothing.
    /// </summary>
    /// <returns>Whether the file changed.</returns>
    /// <exception cref="InvalidOperationException">The assembly has no services.</exception>
    public static bool Write(Assembly assembly, string path)
    {
        var source = For(assembly)
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} has no [Service] classes, so no TypeScript client.");
        var full = Path.GetFullPath(path);
        if (File.Exists(full) && File.ReadAllText(full) == source)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }
}
