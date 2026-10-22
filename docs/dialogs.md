# Dialogs

The system's dialogs, modal to a window: a file to open, files, where to
save, a folder, a message. They are in `OrielWindow.Dialogs`.

## From a service

A service method that takes an `OrielWindow` gets the window of the page
that called it (see [bindings.md](bindings.md#parameters-oriel-passes)),
so the dialog shows over that window:

```csharp
[Service]
public class Decks
{
    public async Task<string?> Open(OrielWindow window)
    {
        var path = await window.Dialogs.OpenFileAsync(new()
        {
            Title = "Open a deck",
            Filters = [new("Decks", "*.deck"), new("Every file", "*.*")],
        });
        return path is null ? null : await File.ReadAllTextAsync(path);
    }

    public async Task<bool> Discard(OrielWindow window) =>
        await window.Dialogs.MessageAsync(new()
        {
            Text = "Discard the changes?",
            Kind = MessageKind.Warning,
            Buttons = MessageButtons.YesNo,
        }) == MessageResult.Yes;
}
```

Pages never get a path they did not ask for: the dialog is the person's
choice, and the service decides what to do with it.

## What there is

| Method | Shows | Returns |
|---|---|---|
| `OpenFileAsync(OpenFileOptions?)` | a file that exists | its full path, or `null` when canceled |
| `OpenFilesAsync(OpenFileOptions?)` | files that exist, one or more | their full paths, none when canceled |
| `SaveFileAsync(SaveFileOptions?)` | where to save, asking before replacing a file | its full path, or `null` |
| `PickFolderAsync(FolderOptions?)` | a folder that exists | its full path, or `null` |
| `MessageAsync(MessageOptions)` | a message with buttons | the button pressed |

Options:

- **`Title`**: the dialog's title. File dialogs default to the system's
  title, and message boxes to their window's title.
- **`Filters`** (files): `new FileFilter("Images", "*.png", "*.jpg")`, the
  first one selected. When a person saves with a name that has no
  extension, the dialog adds the extension of the first pattern of the
  first filter (`png` here).
- **`Directory`**: the folder shown first, when it exists. Otherwise the
  dialog shows the last folder the person used.
- **`FileName`** (save): the name proposed. It may be a full path.
- **`Kind`** (message): `Info`, `Warning`, `Error` or `Question`, for the
  icon and the sound. **`Buttons`**: `Ok`, `OkCancel`, `YesNo` or
  `YesNoCancel`. A box closed without a button gives `Cancel`.

## How they behave

- **Threads.** The methods can be called from any thread. The dialog
  shows on the UI thread, and the task completes once it is closed. While
  a dialog is open, the window cannot be used, but pages go on running,
  and so do calls and events.
- **Closed windows.** A window that is closed shows nothing, and its
  dialogs complete as canceled (`null`, none or `Cancel`).
- **Paths.** Only file system paths are returned. Libraries and other
  virtual places are resolved to their folders, or cannot be chosen.

On Windows, files and folders use the shell's `IFileOpenDialog` and
`IFileSaveDialog`, and messages use `MessageBox`.
