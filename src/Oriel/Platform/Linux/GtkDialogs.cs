using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Oriel.Platform.Linux;

/// <summary>
/// File dialogs and message boxes with GTK: modal to their window, run in a
/// loop of their own until the person answers, as on Windows. File dialogs
/// are GtkFileChooserNative, which uses the desktop's portal when the app
/// runs in a sandbox (Flatpak, Snap).
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class GtkDialogs
{
    private const int GTK_RESPONSE_ACCEPT = -3, GTK_RESPONSE_DELETE_EVENT = -4, GTK_RESPONSE_OK = -5, GTK_RESPONSE_CANCEL = -6, GTK_RESPONSE_YES = -8, GTK_RESPONSE_NO = -9;
    private const int GTK_FILE_CHOOSER_ACTION_OPEN = 0, GTK_FILE_CHOOSER_ACTION_SAVE = 1, GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER = 2;
    private const int GTK_DIALOG_MODAL = 1, GTK_DIALOG_DESTROY_WITH_PARENT = 2;
    private const int GTK_BUTTONS_NONE = 0;

    public static IReadOnlyList<string>? ShowFile(nint parent, FileDialogRequest request)
    {
        var (action, accept) = request.Kind switch
        {
            FileDialogKind.Save => (GTK_FILE_CHOOSER_ACTION_SAVE, "_Save"),
            FileDialogKind.Folder => (GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER, "_Select"),
            _ => (GTK_FILE_CHOOSER_ACTION_OPEN, "_Open"),
        };
        var title = request.Title ?? request.Kind switch
        {
            FileDialogKind.Save => "Save",
            FileDialogKind.Folder => "Select a folder",
            _ => "Open",
        };
        var dialog = gtk_file_chooser_native_new(title, parent, action, accept, "_Cancel");
        try
        {
            gtk_native_dialog_set_modal(dialog, 1);
            if (request.Kind == FileDialogKind.OpenMany)
                gtk_file_chooser_set_select_multiple(dialog, 1);
            if (request.Kind == FileDialogKind.Save)
                gtk_file_chooser_set_do_overwrite_confirmation(dialog, 1);
            var folder = request.Directory;
            if (request.Kind == FileDialogKind.Save && request.FileName is { } name)
            {
                folder ??= Path.IsPathRooted(name) ? Path.GetDirectoryName(name) : null;
                gtk_file_chooser_set_current_name(dialog, Path.GetFileName(name));
            }
            if (folder is not null && Directory.Exists(folder))
                gtk_file_chooser_set_current_folder(dialog, folder);
            foreach (var filter in request.Filters)
            {
                var f = gtk_file_filter_new();
                gtk_file_filter_set_name(f, filter.Name);
                foreach (var pattern in filter.Spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    gtk_file_filter_add_pattern(f, pattern);
                gtk_file_chooser_add_filter(dialog, f); // takes it
            }

            if (gtk_native_dialog_run(dialog) != GTK_RESPONSE_ACCEPT)
                return null;
            var paths = new List<string>();
            var list = gtk_file_chooser_get_filenames(dialog);
            for (var node = (GSList*)list; node is not null; node = node->Next)
            {
                if (Gtk.TakeText(node->Data) is { } path)
                    paths.Add(path);
            }
            g_slist_free(list);
            if (paths.Count == 0)
                return null;
            // A name typed without an extension gets the filter's, as on Windows.
            if (request.Kind == FileDialogKind.Save && request.DefaultExtension is { Length: > 0 } extension && !Path.HasExtension(paths[0]))
                paths[0] += "." + extension;
            return paths;
        }
        finally
        {
            Gtk.g_object_unref(dialog);
        }
    }

    public static MessageResult ShowMessage(nint parent, MessageRequest request)
    {
        var type = request.Kind switch
        {
            MessageKind.Warning => 1,
            MessageKind.Question => 2,
            MessageKind.Error => 3,
            _ => 0, // GTK_MESSAGE_INFO
        };
        // The text goes as the argument of "%s", never as a format.
        var dialog = gtk_message_dialog_new(parent, GTK_DIALOG_MODAL | GTK_DIALOG_DESTROY_WITH_PARENT, type, GTK_BUTTONS_NONE, "%s", request.Text);
        try
        {
            Gtk.gtk_window_set_title(dialog, request.Title);
            switch (request.Buttons)
            {
                case MessageButtons.OkCancel:
                    gtk_dialog_add_button(dialog, "_Cancel", GTK_RESPONSE_CANCEL);
                    gtk_dialog_add_button(dialog, "_OK", GTK_RESPONSE_OK);
                    break;
                case MessageButtons.YesNo:
                    gtk_dialog_add_button(dialog, "_No", GTK_RESPONSE_NO);
                    gtk_dialog_add_button(dialog, "_Yes", GTK_RESPONSE_YES);
                    break;
                case MessageButtons.YesNoCancel:
                    gtk_dialog_add_button(dialog, "_Cancel", GTK_RESPONSE_CANCEL);
                    gtk_dialog_add_button(dialog, "_No", GTK_RESPONSE_NO);
                    gtk_dialog_add_button(dialog, "_Yes", GTK_RESPONSE_YES);
                    break;
                default:
                    gtk_dialog_add_button(dialog, "_OK", GTK_RESPONSE_OK);
                    break;
            }
            return gtk_dialog_run(dialog) switch
            {
                GTK_RESPONSE_OK => MessageResult.Ok,
                GTK_RESPONSE_YES => MessageResult.Yes,
                GTK_RESPONSE_NO => MessageResult.No,
                // Escape, or the box closed: what Cancel means; an OK-only box answers OK.
                _ when request.Buttons == MessageButtons.Ok => MessageResult.Ok,
                _ when request.Buttons == MessageButtons.YesNo => MessageResult.No,
                _ => MessageResult.Cancel,
            };
        }
        finally
        {
            Gtk.gtk_widget_destroy(dialog);
        }
    }

    /// <summary>
    /// Answers the dialog of this process titled <paramref name="title"/>
    /// with <paramref name="response"/>, as a person would: for tests. UI thread.
    /// </summary>
    /// <returns>Whether there was one.</returns>
    internal static bool Respond(string title, int response)
    {
        var windows = gtk_window_list_toplevels();
        try
        {
            for (var node = (GList*)windows; node is not null; node = node->Next)
            {
                if (Gtk.Text(gtk_window_get_title(node->Data)) == title && g_type_check_instance_is_a(node->Data, gtk_dialog_get_type()) != 0)
                {
                    gtk_dialog_response(node->Data, response);
                    return true;
                }
            }
            return false;
        }
        finally
        {
            g_list_free(windows);
        }
    }

    internal const int Accept = GTK_RESPONSE_ACCEPT, Cancel = GTK_RESPONSE_CANCEL, No = GTK_RESPONSE_NO, Closed = GTK_RESPONSE_DELETE_EVENT;

    [StructLayout(LayoutKind.Sequential)]
    private struct GSList
    {
        public nint Data;
        public GSList* Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GList
    {
        public nint Data;
        public GList* Next;
        public GList* Prev;
    }

    private const string Gtk3 = "libgtk-3.so.0", GLib = "libglib-2.0.so.0", GObject = "libgobject-2.0.so.0";

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint gtk_file_chooser_native_new(string title, nint parent, int action, string acceptLabel, string cancelLabel);

    [LibraryImport(Gtk3)]
    private static partial void gtk_native_dialog_set_modal(nint dialog, int modal);

    [LibraryImport(Gtk3)]
    private static partial int gtk_native_dialog_run(nint dialog);

    [LibraryImport(Gtk3)]
    private static partial void gtk_file_chooser_set_select_multiple(nint chooser, int multiple);

    [LibraryImport(Gtk3)]
    private static partial void gtk_file_chooser_set_do_overwrite_confirmation(nint chooser, int confirm);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void gtk_file_chooser_set_current_name(nint chooser, string name);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int gtk_file_chooser_set_current_folder(nint chooser, string folder);

    [LibraryImport(Gtk3)]
    private static partial nint gtk_file_chooser_get_filenames(nint chooser);

    [LibraryImport(Gtk3)]
    private static partial void gtk_file_chooser_add_filter(nint chooser, nint filter);

    [LibraryImport(Gtk3)]
    private static partial nint gtk_file_filter_new();

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void gtk_file_filter_set_name(nint filter, string name);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void gtk_file_filter_add_pattern(nint filter, string pattern);

    // Variadic in C: one string after the format, which x86-64 and arm64 Linux pass as they pass any argument.
    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint gtk_message_dialog_new(nint parent, int flags, int type, int buttons, string format, string text);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint gtk_dialog_add_button(nint dialog, string text, int response);

    [LibraryImport(Gtk3)]
    private static partial int gtk_dialog_run(nint dialog);

    [LibraryImport(Gtk3)]
    private static partial void gtk_dialog_response(nint dialog, int response);

    [LibraryImport(Gtk3)]
    private static partial nint gtk_dialog_get_type();

    [LibraryImport(Gtk3)]
    private static partial nint gtk_window_list_toplevels();

    [LibraryImport(Gtk3)]
    private static partial nint gtk_window_get_title(nint window);

    [LibraryImport(GObject)]
    private static partial int g_type_check_instance_is_a(nint instance, nint type);

    [LibraryImport(GLib)]
    private static partial void g_slist_free(nint list);

    [LibraryImport(GLib)]
    private static partial void g_list_free(nint list);
}
