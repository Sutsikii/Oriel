using System.Runtime.Versioning;
using static Oriel.Platform.MacOS.ObjC;

namespace Oriel.Platform.MacOS;

/// <summary>
/// File dialogs and message boxes with AppKit: NSOpenPanel, NSSavePanel and
/// NSAlert, run modal (<c>runModal</c>) until the person answers, as on the
/// other systems. Main thread only.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacDialogs
{
    private const nint NSModalResponseOK = 1;
    private const nint NSAlertFirstButtonReturn = 1000;

    // The panel or alert running, for tests that answer it.
    private static nint s_panel;
    private static nint s_alert;

    public static IReadOnlyList<string>? ShowFile(FileDialogRequest request)
    {
        var save = request.Kind == FileDialogKind.Save;
        var panel = Send(Class(save ? "NSSavePanel" : "NSOpenPanel"), Sel(save ? "savePanel" : "openPanel"));
        if (request.Title is { } title)
        {
            Send(panel, Sel("setTitle:"), Str(title));
            Send(panel, Sel("setMessage:"), Str(title)); // panels of recent macOS show the message, not the title
        }
        Send(panel, Sel("setCanCreateDirectories:"), (byte)1);
        if (!save)
        {
            var folder = request.Kind == FileDialogKind.Folder;
            Send(panel, Sel("setCanChooseFiles:"), (byte)(folder ? 0 : 1));
            Send(panel, Sel("setCanChooseDirectories:"), (byte)(folder ? 1 : 0));
            Send(panel, Sel("setAllowsMultipleSelection:"), (byte)(request.Kind == FileDialogKind.OpenMany ? 1 : 0));
        }
        var directory = request.Directory;
        if (save && request.FileName is { } name)
        {
            directory ??= Path.IsPathRooted(name) ? Path.GetDirectoryName(name) : null;
            Send(panel, Sel("setNameFieldStringValue:"), Str(Path.GetFileName(name)));
        }
        if (directory is not null && Directory.Exists(directory))
            Send(panel, Sel("setDirectoryURL:"), Send(Class("NSURL"), Sel("fileURLWithPath:"), Str(directory)));
        if (Extensions(request.Filters) is { Count: > 0 } extensions)
            Send(panel, Sel("setAllowedFileTypes:"), Array(extensions));

        s_panel = panel;
        nint response;
        try
        {
            response = Send(panel, Sel("runModal"));
        }
        finally
        {
            s_panel = 0;
        }
        if (response != NSModalResponseOK)
            return null;

        var paths = new List<string>();
        if (save)
        {
            if (Text(Send(Send(panel, Sel("URL")), Sel("path"))) is { } path)
                paths.Add(path);
        }
        else
        {
            var urls = Send(panel, Sel("URLs"));
            var count = (int)Send(urls, Sel("count"));
            for (var i = 0; i < count; i++)
            {
                if (Text(Send(Send(urls, Sel("objectAtIndex:"), i), Sel("path"))) is { } path)
                    paths.Add(path);
            }
        }
        if (paths.Count == 0)
            return null;
        // A name typed without an extension gets the filter's, as on Windows.
        if (save && request.DefaultExtension is { Length: > 0 } extension && !Path.HasExtension(paths[0]))
            paths[0] += "." + extension;
        return paths;
    }

    public static MessageResult ShowMessage(MessageRequest request)
    {
        var alert = New("NSAlert");
        try
        {
            Send(alert, Sel("setMessageText:"), Str(request.Title));
            Send(alert, Sel("setInformativeText:"), Str(request.Text));
            // NSAlertStyleWarning 0, Informational 1, Critical 2.
            Send(alert, Sel("setAlertStyle:"), request.Kind switch { MessageKind.Error => 2, MessageKind.Warning => 0, _ => (nint)1 });
            // The first button is the default one, on the right.
            MessageResult[] results = request.Buttons switch
            {
                MessageButtons.OkCancel => [MessageResult.Ok, MessageResult.Cancel],
                MessageButtons.YesNo => [MessageResult.Yes, MessageResult.No],
                MessageButtons.YesNoCancel => [MessageResult.Yes, MessageResult.No, MessageResult.Cancel],
                _ => [MessageResult.Ok],
            };
            foreach (var result in results)
                Send(alert, Sel("addButtonWithTitle:"), Str(result.ToString()));
            s_alert = alert;
            nint response;
            try
            {
                response = Send(alert, Sel("runModal"));
            }
            finally
            {
                s_alert = 0;
            }
            var index = (int)(response - NSAlertFirstButtonReturn);
            return index >= 0 && index < results.Length ? results[index] : MessageResult.Cancel;
        }
        finally
        {
            Release(alert);
        }
    }

    /// <summary>Answers the panel running with OK, or Cancel, as a person would: for tests. Main thread.</summary>
    internal static bool AnswerPanel(bool ok)
    {
        if (s_panel == 0)
            return false;
        Send(s_panel, Sel(ok ? "ok:" : "cancel:"), (nint)0);
        return true;
    }

    /// <summary>Clicks button <paramref name="index"/> of the alert running, from the right: for tests. Main thread.</summary>
    internal static bool ClickAlert(int index)
    {
        if (s_alert == 0)
            return false;
        Send(Send(Send(s_alert, Sel("buttons")), Sel("objectAtIndex:"), index), Sel("performClick:"), (nint)0);
        return true;
    }

    /// <summary>The extensions of the filters' patterns (<c>*.txt</c> is <c>txt</c>); none for <c>*.*</c>, which takes every file.</summary>
    internal static List<string>? Extensions(IReadOnlyList<FileTypeSpec> filters)
    {
        var extensions = new List<string>();
        foreach (var pattern in filters.SelectMany(f => f.Spec.Split(';', StringSplitOptions.RemoveEmptyEntries)))
        {
            if (pattern is "*" or "*.*")
                return null;
            if (pattern.StartsWith("*.", StringComparison.Ordinal) && pattern.Length > 2)
                extensions.Add(pattern[2..]);
        }
        return extensions;
    }

    private static unsafe nint Array(List<string> items)
    {
        var objects = stackalloc nint[items.Count];
        for (var i = 0; i < items.Count; i++)
            objects[i] = Str(items[i]);
        return Send(Class("NSArray"), Sel("arrayWithObjects:count:"), (nint)objects, items.Count);
    }
}
