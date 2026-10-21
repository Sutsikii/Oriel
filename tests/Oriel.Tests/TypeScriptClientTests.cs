using System.Reflection;
using System.Reflection.Emit;

namespace Oriel.Tests;

public class TypeScriptClientTests
{
    private static Assembly NewAssembly() =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Client" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);

    [Fact]
    public void WritesTheClientOnlyWhenItChanged()
    {
        var assembly = NewAssembly();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "src", "oriel.ts");
        try
        {
            TypeScriptClient.Register(assembly, "export const a = 1;\n");
            Assert.True(TypeScriptClient.Write(assembly, path));
            Assert.Equal("export const a = 1;\n", File.ReadAllText(path));

            var written = File.GetLastWriteTimeUtc(path);
            Assert.False(TypeScriptClient.Write(assembly, path));
            Assert.Equal(written, File.GetLastWriteTimeUtc(path));

            TypeScriptClient.Register(assembly, "export const a = 2;\n");
            Assert.True(TypeScriptClient.Write(assembly, path));
            Assert.Equal("export const a = 2;\n", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path))!, recursive: true);
        }
    }

    [Fact]
    public void SaysWhenAnAssemblyHasNoServices()
    {
        var assembly = NewAssembly();
        Assert.Null(TypeScriptClient.For(assembly));
        var e = Assert.Throws<InvalidOperationException>(() => TypeScriptClient.Write(assembly, "x.ts"));
        Assert.EndsWith("has no [Service] classes, so no TypeScript client.", e.Message);
    }
}
