using System.Text;

namespace Oriel.Tests;

public class EmbeddedFrontendTests
{
    // This assembly embeds tests/Oriel.Tests/Embedded as OrielEmbedFrontend would.
    private readonly EmbeddedFrontend _frontend = EmbeddedFrontend.From(typeof(EmbeddedFrontendTests).Assembly)!;

    [Theory]
    [InlineData("/index.html", "Home")]
    [InlineData("/", "Home")]
    [InlineData("/docs/", "Docs")]
    [InlineData("/DOCS/Index.html", "Docs")]
    public void ReadsTheFileOfAPath(string path, string title)
    {
        var html = Encoding.UTF8.GetString(_frontend.Read(path)!);
        Assert.Contains($"<title>{title}</title>", html);
    }

    [Fact]
    public void ReadsFilesOfSubfoldersWithEscapedNames()
    {
        // MSBuild names the resource after the folder with a backslash, and the URL escapes the space.
        Assert.Equal("export {};\n", Encoding.UTF8.GetString(_frontend.Read("/docs/a%20b.js")!));
    }

    [Theory]
    [InlineData("/missing.html")]
    [InlineData("/docs")]
    public void ReadsNothingForFilesItHasNot(string path)
    {
        Assert.Null(_frontend.Read(path));
        Assert.False(_frontend.Contains(path));
    }

    [Fact]
    public void IsNullForAssembliesWithoutAFrontend()
    {
        Assert.Null(EmbeddedFrontend.From(typeof(OrielApp).Assembly));
        Assert.Null(EmbeddedFrontend.From(null));
    }

    [Theory]
    [InlineData("/index.html", "text/html; charset=utf-8")]
    [InlineData("/assets/app-1a2b.js", "text/javascript; charset=utf-8")]
    [InlineData("/worker.mjs", "text/javascript; charset=utf-8")]
    [InlineData("/assets/index.CSS", "text/css; charset=utf-8")]
    [InlineData("/logo.svg", "image/svg+xml")]
    [InlineData("/engine.wasm", "application/wasm")]
    [InlineData("/fonts/inter.woff2", "font/woff2")]
    [InlineData("/fonts/inter.ttf", "font/ttf")]
    [InlineData("/data.bin", "application/octet-stream")]
    public void GivesContentTypesByExtension(string path, string type)
    {
        Assert.Equal(type, EmbeddedFrontend.ContentType(path));
    }

    [Fact]
    public void TellsTheMissingPagesOfTheApp()
    {
        var frontend = new Frontend(null, "unused", _frontend);
        Assert.Null(frontend.MissingFile(new Uri("https://app.oriel/docs/index.html")));
        Assert.Equal("the embedded resource oriel-frontend/nope.html", frontend.MissingFile(new Uri("https://app.oriel/nope.html")));
    }
}
