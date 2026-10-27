# The clipboard

`OrielApp.Clipboard` reads and writes the system's clipboard: text, HTML,
images and files, as other apps put them there and take them.

```csharp
[Service]
public sealed class Editor(OrielApp app)
{
    /// <summary>Copies the chart as an image, to paste into a document.</summary>
    public Task CopyChart(byte[] png) => app.Clipboard.SetImageAsync(png);

    /// <summary>The files copied in Explorer, to import.</summary>
    public Task<IReadOnlyList<string>?> PastedFiles() => app.Clipboard.GetFilesAsync();
}
```

| Method | Gets or puts |
|---|---|
| `GetTextAsync`, `SetTextAsync(text)` | Text |
| `GetHtmlAsync`, `SetHtmlAsync(html, text?)` | An HTML fragment, such as `<b>bold</b>`, and text for the apps that take text only |
| `GetImageAsync`, `SetImageAsync(png)` | An image, as the bytes of a PNG file |
| `GetFilesAsync`, `SetFilesAsync(paths)` | Files, by their full paths, as a file manager copies them |
| `ClearAsync` | Nothing: empties it |

Each `Set` replaces what was on the clipboard. Gets return null when the
clipboard has nothing of that kind. Any thread may call them; the work runs
on the UI thread.

Pages can read and write text themselves with `navigator.clipboard`, in
answer to a click or a key. The clipboard of `OrielApp` is for the rest:
images, files, HTML, and C# code.

## Images

`GetImageAsync` returns a PNG: the image as another app put it, or the
bitmap of a screenshot (Print Screen, the Snipping Tool) turned into one.
`SetImageAsync` puts the PNG there, and a bitmap of it for the apps that
read bitmaps only (Paint, older Office). It does so for 8-bit RGB and RGBA
PNGs without interlacing, which is what browsers and most apps write; other
PNGs go as PNG only. A page's canvas becomes PNG bytes with
`canvas.toBlob`, then a `Uint8Array` of `await blob.arrayBuffer()`.

## Platforms

The clipboard is in the Windows backend. On Linux and macOS, its methods
throw `NotSupportedException` for now.
