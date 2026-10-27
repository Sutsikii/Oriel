using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Oriel.Platform.Linux;

/// <summary>
/// Printing with WebKitGTK: its print dialog, which is GTK's, and pages to
/// PDF files through GTK's "Print to File" printer, laid out by
/// <see cref="PdfOptions"/>. UI thread only.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class GtkPrinting
{
    private const int GTK_UNIT_INCH = 2;
    private const int GTK_PAGE_ORIENTATION_PORTRAIT = 0, GTK_PAGE_ORIENTATION_LANDSCAPE = 1;

    // The operations under way, with what to call when each ends; they hold their reference until then.
    private static readonly Dictionary<nint, (string Path, Action<bool> Done)> s_printing = [];

    /// <summary>Shows GTK's print dialog for the page; it prints what the person chooses.</summary>
    public static void ShowDialog(nint webview, nint window)
    {
        var operation = webkit_print_operation_new(webview);
        webkit_print_operation_run_dialog(operation, window); // returns once the dialog closes; printing goes on
        Gtk.g_object_unref(operation);
    }

    /// <summary>Prints the page to the PDF at <paramref name="path"/>; <paramref name="done"/> gets whether the file was written.</summary>
    public static void ToPdf(nint webview, string path, PdfOptions? options, Action<bool> done)
    {
        options ??= new PdfOptions();
        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            done(false);
            return;
        }

        var settings = gtk_print_settings_new();
        gtk_print_settings_set_printer(settings, "Print to File");
        gtk_print_settings_set(settings, "output-file-format", "pdf");
        gtk_print_settings_set(settings, "output-uri", new Uri(path).AbsoluteUri);
        gtk_print_settings_set_orientation(settings, options.Landscape ? GTK_PAGE_ORIENTATION_LANDSCAPE : GTK_PAGE_ORIENTATION_PORTRAIT);
        gtk_print_settings_set_scale(settings, options.Scale * 100);

        var setup = gtk_page_setup_new();
        var paper = gtk_paper_size_new_custom("oriel", "Oriel", options.PageWidth, options.PageHeight, GTK_UNIT_INCH);
        gtk_page_setup_set_paper_size(setup, paper);
        gtk_paper_size_free(paper);
        gtk_page_setup_set_orientation(setup, options.Landscape ? GTK_PAGE_ORIENTATION_LANDSCAPE : GTK_PAGE_ORIENTATION_PORTRAIT);
        gtk_page_setup_set_top_margin(setup, options.MarginTop, GTK_UNIT_INCH);
        gtk_page_setup_set_bottom_margin(setup, options.MarginBottom, GTK_UNIT_INCH);
        gtk_page_setup_set_left_margin(setup, options.MarginLeft, GTK_UNIT_INCH);
        gtk_page_setup_set_right_margin(setup, options.MarginRight, GTK_UNIT_INCH);

        // WebKit prints backgrounds as its settings say; header and footer are the browser's, which WebKitGTK has not.
        webkit_settings_set_print_backgrounds(Gtk.webkit_web_view_get_settings(webview), options.Backgrounds ? 1 : 0);

        var operation = webkit_print_operation_new(webview);
        webkit_print_operation_set_print_settings(operation, settings);
        webkit_print_operation_set_page_setup(operation, setup);
        Gtk.g_object_unref(settings);
        Gtk.g_object_unref(setup);
        s_printing[operation] = (path, done);
        Gtk.g_signal_connect_data(operation, "finished", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnFinished, 0, 0, 0);
        Gtk.g_signal_connect_data(operation, "failed", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnFailed, 0, 0, 0);
        Tracing.Write(string.Create(CultureInfo.InvariantCulture, $"printing to {path}"));
        webkit_print_operation_print(operation);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFinished(nint operation, nint data) => End(operation, failed: false);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFailed(nint operation, nint error, nint data)
    {
        Tracing.Write($"printing to PDF failed: {(error == 0 ? "" : Gtk.Text(((GError*)error)->Message))}");
        End(operation, failed: true);
    }

    /// <summary>"failed" comes before "finished": the first one tells, and the operation goes with the last.</summary>
    private static void End(nint operation, bool failed)
    {
        if (!s_printing.TryGetValue(operation, out var printing))
            return;
        if (failed)
        {
            s_printing[operation] = (printing.Path, _ => { });
            Report(printing.Done, false);
            return;
        }
        s_printing.Remove(operation);
        Report(printing.Done, File.Exists(printing.Path));
        Gtk.g_object_unref(operation);
    }

    private static void Report(Action<bool> done, bool written)
    {
        try
        {
            done(written);
        }
        catch (Exception e)
        {
            LinuxPlatform.Report(e);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GError
    {
        public uint Domain;
        public int Code;
        public nint Message;
    }

    private const string Gtk3 = "libgtk-3.so.0", WebKit = "libwebkit2gtk-4.1.so.0";

    [LibraryImport(WebKit)]
    private static partial nint webkit_print_operation_new(nint webview);

    [LibraryImport(WebKit)]
    private static partial int webkit_print_operation_run_dialog(nint operation, nint parent);

    [LibraryImport(WebKit)]
    private static partial void webkit_print_operation_print(nint operation);

    [LibraryImport(WebKit)]
    private static partial void webkit_print_operation_set_print_settings(nint operation, nint settings);

    [LibraryImport(WebKit)]
    private static partial void webkit_print_operation_set_page_setup(nint operation, nint setup);

    [LibraryImport(WebKit)]
    private static partial void webkit_settings_set_print_backgrounds(nint settings, int print);

    [LibraryImport(Gtk3)]
    private static partial nint gtk_print_settings_new();

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void gtk_print_settings_set(nint settings, string key, string value);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void gtk_print_settings_set_printer(nint settings, string printer);

    [LibraryImport(Gtk3)]
    private static partial void gtk_print_settings_set_orientation(nint settings, int orientation);

    [LibraryImport(Gtk3)]
    private static partial void gtk_print_settings_set_scale(nint settings, double scale);

    [LibraryImport(Gtk3)]
    private static partial nint gtk_page_setup_new();

    [LibraryImport(Gtk3)]
    private static partial void gtk_page_setup_set_paper_size(nint setup, nint size);

    [LibraryImport(Gtk3)]
    private static partial void gtk_page_setup_set_orientation(nint setup, int orientation);

    [LibraryImport(Gtk3)]
    private static partial void gtk_page_setup_set_top_margin(nint setup, double margin, int unit);

    [LibraryImport(Gtk3)]
    private static partial void gtk_page_setup_set_bottom_margin(nint setup, double margin, int unit);

    [LibraryImport(Gtk3)]
    private static partial void gtk_page_setup_set_left_margin(nint setup, double margin, int unit);

    [LibraryImport(Gtk3)]
    private static partial void gtk_page_setup_set_right_margin(nint setup, double margin, int unit);

    [LibraryImport(Gtk3, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint gtk_paper_size_new_custom(string name, string displayName, double width, double height, int unit);

    [LibraryImport(Gtk3)]
    private static partial void gtk_paper_size_free(nint size);
}
