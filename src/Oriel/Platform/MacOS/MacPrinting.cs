using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Oriel.Platform.MacOS.ObjC;

namespace Oriel.Platform.MacOS;

/// <summary>
/// Printing with WKWebView's print operation (macOS 11 and later): the
/// system's print dialog, and pages to PDF files, saved by the operation
/// itself (NSPrintSaveJob), laid out by <see cref="PdfOptions"/>. A
/// WKWebView prints only through <c>runOperationModalForWindow:</c>, which
/// tells its end to a delegate. Main thread only.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacPrinting
{
    private const double PointsPerInch = 72;

    private static nint s_delegate;
    private static long s_lastJob;
    private static readonly Dictionary<long, (string Path, Action<bool> Done)> s_jobs = [];

    public static void ShowDialog(nint webview, nint window)
    {
        var info = Send(Send(Class("NSPrintInfo"), Sel("sharedPrintInfo")), Sel("copy"));
        var operation = Operation(webview, info);
        Send(operation, Sel("setShowsPrintPanel:"), (byte)1);
        Send(operation, Sel("setShowsProgressPanel:"), (byte)1);
        Send(operation, Sel("runOperationModalForWindow:delegate:didRunSelector:contextInfo:"), window, 0, 0, 0);
        Release(info);
    }

    public static void ToPdf(nint webview, nint window, string path, PdfOptions? options, Action<bool> done)
    {
        options ??= new PdfOptions();
        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            done(false);
            return;
        }
        var info = Send(Send(Class("NSPrintInfo"), Sel("sharedPrintInfo")), Sel("copy"));
        Send(info, Sel("setPaperSize:"), new NSSize(options.PageWidth * PointsPerInch, options.PageHeight * PointsPerInch));
        Send(info, Sel("setOrientation:"), options.Landscape ? 1 : 0); // NSPaperOrientationLandscape 1
        Send(info, Sel("setTopMargin:"), options.MarginTop * PointsPerInch);
        Send(info, Sel("setBottomMargin:"), options.MarginBottom * PointsPerInch);
        Send(info, Sel("setLeftMargin:"), options.MarginLeft * PointsPerInch);
        Send(info, Sel("setRightMargin:"), options.MarginRight * PointsPerInch);
        Send(info, Sel("setHorizontalPagination:"), 0); // NSPrintingPaginationModeAutomatic
        Send(info, Sel("setVerticalPagination:"), 0);
        Send(info, Sel("setJobDisposition:"), Constant("NSPrintSaveJob"));
        var dictionary = Send(info, Sel("dictionary"));
        Send(dictionary, Sel("setObject:forKey:"), Send(Class("NSURL"), Sel("fileURLWithPath:"), Str(path)), Constant("NSPrintJobSavingURL"));
        if (options.Scale != 1)
            Send(dictionary, Sel("setObject:forKey:"), Send(Class("NSNumber"), Sel("numberWithDouble:"), options.Scale), Constant("NSPrintScalingFactor"));

        var operation = Operation(webview, info);
        Send(operation, Sel("setShowsPrintPanel:"), (byte)0);
        Send(operation, Sel("setShowsProgressPanel:"), (byte)0);
        var job = ++s_lastJob;
        s_jobs[job] = (path, done);
        Send(operation, Sel("runOperationModalForWindow:delegate:didRunSelector:contextInfo:"), window, Delegate(), Sel("printOperationDidRun:success:contextInfo:"), (nint)job);
        Release(info);
    }

    /// <summary>The webview's print operation of <paramref name="info"/>, its view as large as the webview, which it needs to lay pages out.</summary>
    private static nint Operation(nint webview, nint info)
    {
        var operation = Send(webview, Sel("printOperationWithPrintInfo:"), info);
        Send(Send(operation, Sel("view")), Sel("setFrame:"), SendRect(webview, Sel("bounds")));
        return operation;
    }

    private static nint Delegate()
    {
        if (s_delegate == 0)
        {
            var cls = DefineClass("OrielPrintDelegate", [], ("printOperationDidRun:success:contextInfo:", (nint)(delegate* unmanaged<nint, nint, nint, byte, nint, void>)&OnRan, "v@:@c^v"));
            s_delegate = Send(Send(cls, Sel("alloc")), Sel("init"));
        }
        return s_delegate;
    }

    [UnmanagedCallersOnly]
    private static void OnRan(nint self, nint sel, nint operation, byte success, nint context)
    {
        if (!s_jobs.Remove(context, out var job))
            return;
        try
        {
            job.Done(success != 0 && File.Exists(job.Path));
        }
        catch (Exception e)
        {
            MacPlatform.Report(e);
        }
    }

    /// <summary>An NSString constant of AppKit, such as <c>NSPrintSaveJob</c>.</summary>
    private static nint Constant(string name) =>
        *(nint*)NativeLibrary.GetExport(NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"), name);
}
