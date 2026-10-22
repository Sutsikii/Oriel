using Oriel.Platform;

namespace Oriel;

/// <summary>A kind of file a file dialog offers, such as <c>new FileFilter("Images", "*.png", "*.jpg")</c>.</summary>
public sealed class FileFilter
{
    /// <param name="name">What the dialog shows for it.</param>
    /// <param name="patterns">The names it matches, such as <c>*.png</c>; <c>*.*</c> for every file.</param>
    /// <exception cref="ArgumentException">The name is empty, there is no pattern, or a pattern is empty or has a <c>;</c>.</exception>
    public FileFilter(string name, params string[] patterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(patterns);
        if (patterns.Length == 0)
            throw new ArgumentException("A filter needs at least one pattern, such as \"*.txt\".", nameof(patterns));
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains(';', StringComparison.Ordinal))
                throw new ArgumentException($"\"{pattern}\" is not a pattern: patterns are not empty and have no ';'.", nameof(patterns));
        }
        Name = name;
        Patterns = [.. patterns];
    }

    /// <summary>What the dialog shows for it.</summary>
    public string Name { get; }

    /// <summary>The names it matches.</summary>
    public IReadOnlyList<string> Patterns { get; }
}

/// <summary>How <see cref="Dialogs.OpenFileAsync"/> and <see cref="Dialogs.OpenFilesAsync"/> ask for files.</summary>
public sealed class OpenFileOptions
{
    /// <summary>The title of the dialog. Defaults to the system's.</summary>
    public string? Title { get; init; }

    /// <summary>The kinds of files offered, the first one selected. Defaults to every file.</summary>
    public IReadOnlyList<FileFilter> Filters { get; init; } = [];

    /// <summary>The folder the dialog shows first, when it exists. Defaults to the last one the person used.</summary>
    public string? Directory { get; init; }
}

/// <summary>How <see cref="Dialogs.SaveFileAsync"/> asks where to save.</summary>
public sealed class SaveFileOptions
{
    /// <summary>The title of the dialog. Defaults to the system's.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The kinds of files offered, the first one selected. A name typed
    /// without an extension gets the one of the first filter's first
    /// pattern, such as <c>png</c> for <c>*.png</c>.
    /// </summary>
    public IReadOnlyList<FileFilter> Filters { get; init; } = [];

    /// <summary>The folder the dialog shows first, when it exists. Defaults to the last one the person used.</summary>
    public string? Directory { get; init; }

    /// <summary>The name the dialog proposes, such as <c>Untitled.txt</c>.</summary>
    public string? FileName { get; init; }
}

/// <summary>How <see cref="Dialogs.PickFolderAsync"/> asks for a folder.</summary>
public sealed class FolderOptions
{
    /// <summary>The title of the dialog. Defaults to the system's.</summary>
    public string? Title { get; init; }

    /// <summary>The folder the dialog shows first, when it exists. Defaults to the last one the person used.</summary>
    public string? Directory { get; init; }
}

/// <summary>What a message box is about: it picks the icon and the sound.</summary>
public enum MessageKind
{
    /// <summary>Something to know.</summary>
    Info,

    /// <summary>Something that may go wrong.</summary>
    Warning,

    /// <summary>Something that went wrong.</summary>
    Error,

    /// <summary>A question to answer.</summary>
    Question,
}

/// <summary>The buttons of a message box.</summary>
public enum MessageButtons
{
    /// <summary>OK.</summary>
    Ok,

    /// <summary>OK and Cancel.</summary>
    OkCancel,

    /// <summary>Yes and No.</summary>
    YesNo,

    /// <summary>Yes, No and Cancel.</summary>
    YesNoCancel,
}

/// <summary>The button a message box was closed with.</summary>
public enum MessageResult
{
    /// <summary>OK.</summary>
    Ok,

    /// <summary>Cancel, or the box was closed without a button, or its window was closed.</summary>
    Cancel,

    /// <summary>Yes.</summary>
    Yes,

    /// <summary>No.</summary>
    No,
}

/// <summary>What <see cref="Dialogs.MessageAsync"/> shows.</summary>
public sealed class MessageOptions
{
    /// <summary>The message.</summary>
    public required string Text { get; init; }

    /// <summary>The title of the box. Defaults to the title of its window.</summary>
    public string? Title { get; init; }

    /// <summary>What it is about. Defaults to <see cref="MessageKind.Info"/>.</summary>
    public MessageKind Kind { get; init; }

    /// <summary>Its buttons. Defaults to <see cref="MessageButtons.Ok"/>.</summary>
    public MessageButtons Buttons { get; init; }
}

/// <summary>
/// The system's dialogs, modal to a window: files to open, where to save, a
/// folder, a message. Use <see cref="OrielWindow.Dialogs"/>; services get the
/// window that called them as a parameter of type <see cref="OrielWindow"/>.
/// The methods can be called from any thread: the dialog shows on the UI
/// thread, and the task completes once it is closed. A window that is
/// closed shows nothing, and its dialogs complete as canceled.
/// </summary>
/// <example>
/// <code>
/// public async Task&lt;string?&gt; Open(OrielWindow window)
/// {
///     var path = await window.Dialogs.OpenFileAsync(new() { Filters = [new("Decks", "*.deck")] });
///     return path is null ? null : await File.ReadAllTextAsync(path);
/// }
/// </code>
/// </example>
public sealed class Dialogs
{
    private readonly OrielWindow _window;

    internal Dialogs(OrielWindow window)
    {
        _window = window;
    }

    /// <summary>Asks for a file to open.</summary>
    /// <returns>Its full path, or null when the dialog was canceled.</returns>
    public async Task<string?> OpenFileAsync(OpenFileOptions? options = null)
    {
        options ??= new();
        var paths = await ShowFileDialog(new FileDialogRequest(FileDialogKind.Open, options.Title, Specs(options.Filters), Folder(options.Directory)))
            .ConfigureAwait(false);
        return paths is [var path, ..] ? path : null;
    }

    /// <summary>Asks for files to open, one or more.</summary>
    /// <returns>Their full paths, none when the dialog was canceled.</returns>
    public async Task<IReadOnlyList<string>> OpenFilesAsync(OpenFileOptions? options = null)
    {
        options ??= new();
        return await ShowFileDialog(new FileDialogRequest(FileDialogKind.OpenMany, options.Title, Specs(options.Filters), Folder(options.Directory)))
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>Asks where to save a file. The dialog asks before replacing one.</summary>
    /// <returns>Its full path, or null when the dialog was canceled.</returns>
    public async Task<string?> SaveFileAsync(SaveFileOptions? options = null)
    {
        options ??= new();
        var request = new FileDialogRequest(FileDialogKind.Save, options.Title, Specs(options.Filters), Folder(options.Directory))
        {
            FileName = options.FileName,
            DefaultExtension = DefaultExtension(options.Filters),
        };
        var paths = await ShowFileDialog(request).ConfigureAwait(false);
        return paths is [var path, ..] ? path : null;
    }

    /// <summary>Asks for a folder.</summary>
    /// <returns>Its full path, or null when the dialog was canceled.</returns>
    public async Task<string?> PickFolderAsync(FolderOptions? options = null)
    {
        options ??= new();
        var paths = await ShowFileDialog(new FileDialogRequest(FileDialogKind.Folder, options.Title, [], Folder(options.Directory)))
            .ConfigureAwait(false);
        return paths is [var path, ..] ? path : null;
    }

    /// <summary>Shows a message, and waits for a button.</summary>
    /// <returns>The button pressed.</returns>
    public Task<MessageResult> MessageAsync(MessageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Text);
        return _window.ShowModal(
            native => native.ShowMessage(new MessageRequest(options.Title ?? _window.Title, options.Text, options.Kind, options.Buttons)),
            MessageResult.Cancel);
    }

    private Task<IReadOnlyList<string>?> ShowFileDialog(FileDialogRequest request) =>
        _window.ShowModal(native => native.ShowFileDialog(request), null);

    /// <summary>The filters as the system takes them: a name, and patterns joined by <c>;</c>.</summary>
    internal static IReadOnlyList<FileTypeSpec> Specs(IReadOnlyList<FileFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return [.. filters.Select(f => new FileTypeSpec(f.Name, string.Join(';', f.Patterns)))];
    }

    /// <summary>The full path of <paramref name="directory"/>, or null when it is not given or does not exist.</summary>
    internal static string? Folder(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        var full = Path.GetFullPath(directory);
        return System.IO.Directory.Exists(full) ? full : null;
    }

    /// <summary>The extension of the first filter's first pattern, <c>png</c> for <c>*.png</c>; null for <c>*.*</c> and the like.</summary>
    internal static string? DefaultExtension(IReadOnlyList<FileFilter> filters)
    {
        if (filters is not [var first, ..])
            return null;
        var pattern = first.Patterns[0];
        if (!pattern.StartsWith("*.", StringComparison.Ordinal))
            return null;
        var extension = pattern[2..];
        return extension.Length == 0 || extension.AsSpan().ContainsAny('*', '?') ? null : extension;
    }
}
