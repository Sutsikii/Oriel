using Oriel.Bridge;

namespace Oriel.Tests.Bridge;

public class BridgeScriptTests
{
    [Fact]
    public void IsEmbeddedInTheAssembly()
    {
        Assert.Contains("Object.defineProperty(root, \"oriel\"", BridgeScript.Source);
    }

    [Fact]
    public void IsTheFileOfTheRepository()
    {
        var file = Path.Combine(RepositoryRoot(), "src", "Oriel", "Bridge", "bridge.js");
        Assert.Equal(File.ReadAllText(file), BridgeScript.Source);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Oriel.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Oriel.slnx not found above " + AppContext.BaseDirectory);
    }
}
