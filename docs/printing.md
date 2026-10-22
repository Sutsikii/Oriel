# Printing

A window prints its page as the page's print styles lay it out
(`@media print`): to a printer, through a print dialog, or to a PDF file.

## A print dialog

```csharp
window.ShowPrintDialog();                    // the webview's, with a preview: what Ctrl+P shows
window.ShowPrintDialog(PrintDialog.System);  // the system's, with every option of the printers
```

`ShowPrintDialog` returns at once: the person prints, or not, and the app
is not told which. Pages can also call `window.print()` themselves.

## A PDF file

```csharp
var written = await window.PrintToPdfAsync(@"C:\Invoices\42.pdf", new PdfOptions
{
    PageWidth = 8.27,   // A4, in inches
    PageHeight = 11.69,
    Backgrounds = true, // colors and images of the page's backgrounds
});
```

`PrintToPdfAsync` replaces a file that exists. It returns false when the
file was not written: the folder does not exist, the page could not be
printed, or the window closed meanwhile.

`PdfOptions`, all optional:

| Option | Default | |
|---|---|---|
| `Landscape` | false | pages wider than tall |
| `Scale` | 1 | from 0.1 to 2 |
| `PageWidth`, `PageHeight` | 8.5 × 11 (US Letter) | inches; A4 is 8.27 × 11.69 |
| `MarginTop`, `MarginBottom`, `MarginLeft`, `MarginRight` | 0.4 | inches |
| `Backgrounds` | false | backgrounds are left out, as browsers do |
| `HeaderAndFooter` | false | date and title above, URL and page number below |
| `HeaderTitle` | the page's title | the title in the header |

A scale, size or margin that cannot be printed throws
`ArgumentOutOfRangeException` at once.

## From a page

Pages ask a service, which asks where to save and writes the file:

```csharp
[Service]
public class Invoices
{
    public async Task<bool> SaveAsPdf(OrielWindow window, string number)
    {
        var path = await window.Dialogs.SaveFileAsync(new()
        {
            FileName = $"Invoice {number}.pdf",
            Filters = [new("PDF", "*.pdf")],
        });
        return path is not null && await window.PrintToPdfAsync(path);
    }
}
```

```ts
await Invoices.saveAsPdf("42");
```

On Windows, these use `ICoreWebView2_7.PrintToPdf` and
`ICoreWebView2_16.ShowPrintUI`, in WebView2 Runtimes from 2022 on. An
older runtime throws `NotSupportedException`.
