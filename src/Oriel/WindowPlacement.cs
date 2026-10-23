using System.Text;
using System.Text.Json;

namespace Oriel;

/// <summary>Whether a window is minimized, maximized, or neither.</summary>
public enum WindowState
{
    /// <summary>Neither: at its own position and size, or full screen.</summary>
    Normal,

    /// <summary>In the taskbar.</summary>
    Minimized,

    /// <summary>Filling its monitor's work area.</summary>
    Maximized,
}

/// <summary>
/// Where a window is, frame included, in device-independent pixels: its
/// position on the screen at the scale of its monitor, and its size.
/// </summary>
public readonly record struct WindowBounds(int X, int Y, int Width, int Height);

/// <summary>
/// The placements of the windows that have a <see cref="WindowOptions.StateKey"/>,
/// in a file of the app's data: what the backend said of each, by key. A file
/// that cannot be read is as good as none, so a broken file never keeps a
/// window from opening.
/// </summary>
internal sealed class PlacementStore(string file)
{
    private readonly Lock _lock = new();

    public string File { get; } = file;

    public string? Load(string key)
    {
        lock (_lock)
            return Read().GetValueOrDefault(key);
    }

    public void Save(string key, string placement)
    {
        lock (_lock)
        {
            var all = Read();
            all[key] = placement;
            var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                foreach (var (k, v) in all.OrderBy(p => p.Key, StringComparer.Ordinal))
                    w.WriteString(k, v);
                w.WriteEndObject();
            }
            // Written aside, then moved: a crash leaves the old file or the new one.
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            var temporary = File + ".tmp";
            System.IO.File.WriteAllBytes(temporary, buffer.ToArray());
            System.IO.File.Move(temporary, File, overwrite: true);
        }
    }

    private Dictionary<string, string> Read()
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(File, Encoding.UTF8));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return all;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                    all[p.Name] = p.Value.GetString()!;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // No file yet, or not one Oriel wrote: windows open where they would.
        }
        return all;
    }
}
