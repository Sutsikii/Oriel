using Oriel.Platform.Windows;

namespace Oriel.Tests.Platform;

public class WebView2RuntimeTests
{
    private const string Stable = @"SOFTWARE\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string Beta = @"SOFTWARE\Microsoft\EdgeUpdate\ClientState\{2CD8A007-E189-409D-A2C8-9AF4EF3C72AA}";

    /// <summary>A machine with the runtimes in <paramref name="installed"/> and the files of <paramref name="complete"/>.</summary>
    private static WebView2Runtime.Found Find(Dictionary<(bool Machine, string Key), string> installed, string[] complete, string? fixedFolder = null)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in complete)
        {
            files.Add(Path.Combine(dir, "msedgewebview2.exe"));
            files.Add(Path.Combine(dir, "EBWebView", "x64", "EmbeddedBrowserWebView.dll"));
        }
        return WebView2Runtime.Find(
            fixedFolder,
            (machine, key, name) => name == "EBWebView" && installed.TryGetValue((machine, key), out var dir) ? dir : null,
            files.Contains,
            "x64");
    }

    [Fact]
    public void FindsTheInstalledRuntime()
    {
        var found = Find(new() { [(true, Stable)] = @"C:\EdgeWebView\155.0.1" }, [@"C:\EdgeWebView\155.0.1"]);
        Assert.Equal(new WebView2Runtime.Found(@"C:\EdgeWebView\155.0.1", IsFixed: false), found);
    }

    [Fact]
    public void PrefersTheNewerOfTheMachineAndUserRuntimes()
    {
        var found = Find(
            new() { [(true, Stable)] = @"C:\Machine\120.0.1", [(false, Stable)] = @"C:\User\155.0.2" },
            [@"C:\Machine\120.0.1", @"C:\User\155.0.2"]);
        Assert.Equal(@"C:\User\155.0.2", found.Directory);
    }

    [Fact]
    public void PrefersTheMostStableChannel()
    {
        var found = Find(
            new() { [(true, Beta)] = @"C:\Beta\160.0.0", [(false, Stable)] = @"C:\Stable\150.0.0" },
            [@"C:\Beta\160.0.0", @"C:\Stable\150.0.0"]);
        Assert.Equal(@"C:\Stable\150.0.0", found.Directory);
    }

    [Fact]
    public void SkipsRecordsOfRuntimesWhoseFilesAreGone()
    {
        var found = Find(
            new() { [(true, Stable)] = @"C:\Gone\155.0.0", [(true, Beta)] = @"C:\Beta\156.0.0" },
            [@"C:\Beta\156.0.0"]);
        Assert.Equal(@"C:\Beta\156.0.0", found.Directory);
    }

    [Fact]
    public void SaysWhereToGetTheRuntimeWhenThereIsNone()
    {
        var e = Assert.Throws<WebViewUnavailableException>(() => Find(new(), []));
        Assert.Equal("The Microsoft Edge WebView2 Runtime is not installed; get it from https://go.microsoft.com/fwlink/p/?LinkId=2124703", e.Message);
    }

    [Fact]
    public void SaysWhenTheOnlyRuntimeIsIncomplete()
    {
        var e = Assert.Throws<WebViewUnavailableException>(() => Find(new() { [(true, Stable)] = @"C:\Gone\1.0" }, []));
        Assert.StartsWith(@"The WebView2 Runtime registered at C:\Gone\1.0 is missing or incomplete", e.Message);
    }

    [Fact]
    public void UsesAFixedVersionRuntimeWhenNamed()
    {
        var found = Find(new() { [(true, Stable)] = @"C:\Installed\1.0" }, [@"C:\Installed\1.0", @"D:\Fixed"], fixedFolder: @"D:\Fixed");
        Assert.Equal(new WebView2Runtime.Found(@"D:\Fixed", IsFixed: true), found);
    }

    [Fact]
    public void RefusesAFixedFolderWithoutARuntime()
    {
        var e = Assert.Throws<WebViewUnavailableException>(() => Find(new(), [], fixedFolder: @"D:\Empty"));
        Assert.StartsWith(@"WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=D:\Empty holds no WebView2 Runtime", e.Message);
    }

    [Fact]
    public void NeedsTheBrowserBesideTheLibrary()
    {
        // Edge keeps a client library beside itself, which is no runtime.
        var files = new HashSet<string> { @"C:\Edge\EBWebView\x64\EmbeddedBrowserWebView.dll" };
        Assert.Null(WebView2Runtime.ClientLibraryPath(@"C:\Edge", files.Contains, "x64"));
    }

    [Fact]
    public void FindsTheRuntimeOfThisMachine()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // CI machines and developer machines of Windows have the runtime.
        var found = WebView2Runtime.Find();
        Assert.NotNull(WebView2Runtime.ClientLibraryPath(found.Directory, File.Exists, WebView2Runtime.Architecture()));
    }
}
