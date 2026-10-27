using Oriel.Platform;

namespace Oriel;

/// <summary>
/// The system's clipboard: text, HTML, images and files, as other apps put
/// them there and take them. Any thread may call it; the work runs on the
/// UI thread. Get it from <see cref="OrielApp.Clipboard"/>.
/// </summary>
/// <remarks>
/// Pages can read and write text themselves with <c>navigator.clipboard</c>;
/// this is for the rest, and for C#. Windows only for now: elsewhere the
/// methods throw <see cref="NotSupportedException"/>.
/// </remarks>
public sealed class Clipboard
{
    private readonly OrielApp _app;

    internal Clipboard(OrielApp app) => _app = app;

    /// <summary>The text on the clipboard, or null when there is none.</summary>
    public Task<string?> GetTextAsync() => Run(p => p.GetClipboardText());

    /// <summary>
    /// The HTML on the clipboard, such as a part of a page or a document
    /// copied, or null when there is none: the fragment copied, without the
    /// page around it.
    /// </summary>
    public Task<string?> GetHtmlAsync() => Run(p => p.GetClipboardHtml());

    /// <summary>
    /// The image on the clipboard, as the bytes of a PNG file, or null when
    /// there is none: a screenshot, or an image copied from another app.
    /// </summary>
    public Task<byte[]?> GetImageAsync() => Run(p => p.GetClipboardImage());

    /// <summary>The full paths of the files on the clipboard, copied in a file manager, or null when there are none.</summary>
    public Task<IReadOnlyList<string>?> GetFilesAsync() => Run(p => p.GetClipboardFiles());

    /// <summary>Puts <paramref name="text"/> on the clipboard, in place of what was there.</summary>
    public Task SetTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Set(new ClipboardContent(text, null, null, null));
    }

    /// <summary>
    /// Puts <paramref name="html"/>, a fragment such as <c>&lt;b&gt;bold&lt;/b&gt;</c>,
    /// on the clipboard, with <paramref name="text"/> for the apps that take
    /// text only.
    /// </summary>
    public Task SetHtmlAsync(string html, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        return Set(new ClipboardContent(text, html, null, null));
    }

    /// <summary>Puts an image, the bytes of a PNG file, on the clipboard.</summary>
    /// <exception cref="ArgumentException">The bytes are not of a PNG file.</exception>
    public Task SetImageAsync(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (!Png.IsPng(png))
            throw new ArgumentException("The image is not a PNG file.", nameof(png));
        return Set(new ClipboardContent(null, null, png, null));
    }

    /// <summary>Puts files on the clipboard, as a file manager copies them: they paste in other apps' folders.</summary>
    /// <exception cref="ArgumentException">There is no path, or one is not a full path.</exception>
    public Task SetFilesAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var files = paths.ToList();
        if (files.Count == 0 || files.Exists(p => string.IsNullOrEmpty(p) || !Path.IsPathFullyQualified(p)))
            throw new ArgumentException("Files are put on the clipboard by their full paths, at least one.", nameof(paths));
        return Set(new ClipboardContent(null, null, null, files));
    }

    /// <summary>Empties the clipboard.</summary>
    public Task ClearAsync() => Set(new ClipboardContent(null, null, null, null));

    private Task Set(ClipboardContent content) => Run(p =>
    {
        p.SetClipboard(content);
        return true;
    });

    private Task<T> Run<T>(Func<IPlatform, T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _app.Platform.Post(() =>
        {
            try
            {
                done.TrySetResult(work(_app.Platform));
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
        });
        return done.Task;
    }
}

/// <summary>What goes on the clipboard at once, in each format given; nothing empties it.</summary>
internal sealed record ClipboardContent(string? Text, string? Html, byte[]? Png, IReadOnlyList<string>? Files);
