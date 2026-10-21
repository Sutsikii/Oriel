namespace Oriel.Tests;

public class FrontendTests
{
    private static readonly Frontend s_app = new(null, "wwwroot");
    private static readonly Frontend s_dev = new("http://localhost:5173", "wwwroot");

    [Theory]
    [InlineData("/", "https://app.oriel/index.html")]
    [InlineData("", "https://app.oriel/index.html")]
    [InlineData("/about.html", "https://app.oriel/about.html")]
    [InlineData("settings/", "https://app.oriel/settings/index.html")]
    [InlineData("/?tab=2", "https://app.oriel/index.html?tab=2")]
    [InlineData("https://example.com/x", "https://example.com/x")]
    public void ResolvesUrlsToTheAppsFiles(string url, string expected)
    {
        Assert.Equal(new Uri(expected), s_app.Resolve(url));
    }

    [Theory]
    [InlineData("/", "http://localhost:5173/")]
    [InlineData("/settings/", "http://localhost:5173/settings/")]
    public void ResolvesUrlsToTheDevServer(string url, string expected)
    {
        Assert.Equal(new Uri(expected), s_dev.Resolve(url));
    }

    [Theory]
    [InlineData("https://app.oriel/index.html", true)]
    [InlineData("https://APP.ORIEL/deep/page.html", true)]
    [InlineData("http://app.oriel/", false)]
    [InlineData("https://app.oriel:8443/", false)]
    [InlineData("https://app.oriel.evil.example/", false)]
    [InlineData("http://localhost:5173/", false)]
    [InlineData("about:blank", false)]
    [InlineData("not a url", false)]
    public void TrustsOnlyTheAppsOrigin(string source, bool trusted)
    {
        Assert.Equal(trusted, s_app.IsTrusted(source));
    }

    [Theory]
    [InlineData("http://localhost:5173/", true)]
    [InlineData("http://localhost:5173/nested/x.html", true)]
    [InlineData("http://localhost:5174/", false)]
    [InlineData("https://localhost:5173/", false)]
    [InlineData("https://app.oriel/", false)]
    public void TrustsOnlyTheDevServerInDevelopment(string source, bool trusted)
    {
        Assert.Equal(trusted, s_dev.IsTrusted(source));
    }

    [Fact]
    public void FindsThePagesMissingFromTheAppsFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "my pages"));
        File.WriteAllText(Path.Combine(folder, "index.html"), "");
        File.WriteAllText(Path.Combine(folder, "my pages", "about.html"), "");
        try
        {
            var app = new Frontend(null, folder);
            Assert.Null(app.MissingFile(app.Resolve("/")));
            Assert.Null(app.MissingFile(app.Resolve("/my%20pages/about.html")));
            Assert.Equal(Path.Combine(folder, "settings.html"), app.MissingFile(app.Resolve("/settings.html")));

            // Pages of a dev server or of other sites are not the app's files.
            Assert.Null(app.MissingFile(new Uri("https://example.com/")));
            Assert.Null(new Frontend("http://localhost:5173", folder).MissingFile(new Uri("http://localhost:5173/x.html")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("localhost:5173")]
    [InlineData("file:///C:/app")]
    public void RefusesDevUrlsThatAreNotHttp(string url)
    {
        Assert.Throws<ArgumentException>(() => new Frontend(url, "wwwroot"));
    }
}
