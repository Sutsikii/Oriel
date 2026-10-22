using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public sealed class PrintingTests : IDisposable
{
    private readonly FakePlatform _platform = new();
    private readonly OrielApp _app;
    private readonly Task<int> _run;
    private readonly OrielWindow _window;

    public PrintingTests()
    {
        _app = new OrielApp(new AppOptions { Name = "PrintingTests", AssetsDirectory = @"C:\app\wwwroot" }, _platform, null);
        var opened = new TaskCompletionSource<OrielWindow>();
        _run = Task.Run(() => _app.Run(() => opened.SetResult(_app.OpenWindow())));
        _window = opened.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _app.Quit();
        _run.Wait(TimeSpan.FromSeconds(10));
    }

    private FakeWindow Native => Assert.Single(_platform.Windows);

    private async Task<(string Path, PdfOptions? Options, Action<bool> Done)> NextPdf()
    {
        for (var i = 0; i < 400; i++)
        {
            if (Native.Pdfs.TryDequeue(out var pdf))
                return pdf;
            await Task.Delay(5);
        }
        throw new TimeoutException("no PDF was asked for");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrintsToAPdfAtAFullPath(bool written)
    {
        var options = new PdfOptions { Landscape = true, PageWidth = 8.27, PageHeight = 11.69, Backgrounds = true };
        var printing = _window.PrintToPdfAsync("invoice.pdf", options);

        var (path, given, done) = await NextPdf();
        Assert.Equal(Path.GetFullPath("invoice.pdf"), path);
        Assert.Same(options, given);
        Assert.False(printing.IsCompleted);
        done(written);
        Assert.Equal(written, await printing.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task LeavesTheLayoutToTheWebviewWithoutOptions()
    {
        var printing = _window.PrintToPdfAsync(@"C:\out\a.pdf");
        var (_, options, done) = await NextPdf();
        Assert.Null(options);
        done(true);
        Assert.True(await printing);
    }

    [Fact]
    public async Task GivesUpThePdfsOfAWindowThatCloses()
    {
        var printing = _window.PrintToPdfAsync(@"C:\out\a.pdf");
        await NextPdf(); // started, never completed by the webview
        Native.Close();

        Assert.False(await printing.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(await _window.PrintToPdfAsync(@"C:\out\b.pdf")); // closed: at once
        Assert.Empty(Native.Pdfs);
    }

    [Theory]
    [InlineData(0.05, 8.5, 0.4)]
    [InlineData(2.5, 8.5, 0.4)]
    [InlineData(1, 0, 0.4)]
    [InlineData(1, 8.5, -1)]
    [InlineData(1, 0.8, 0.4)] // margins of 0.8 on a page 0.8 wide
    public void RefusesLayoutsThatCannotPrint(double scale, double width, double margin)
    {
        var options = new PdfOptions { Scale = scale, PageWidth = width, MarginLeft = margin, MarginRight = margin };
        // Thrown at once, not in the task: a bug of the caller.
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = _window.PrintToPdfAsync("a.pdf", options); });
    }

    [Fact]
    public async Task OpensPrintDialogs()
    {
        _window.ShowPrintDialog();
        _window.ShowPrintDialog(PrintDialog.System);
        for (var i = 0; i < 400 && Native.PrintDialogs.Count < 2; i++)
            await Task.Delay(5);
        Assert.Equal([PrintDialog.Browser, PrintDialog.System], Native.PrintDialogs);
    }
}
