namespace Oriel.Generators.Tests;

/// <summary>What the generator reports about code it cannot generate for.</summary>
public class DiagnosticTests
{
    private static Microsoft.CodeAnalysis.Diagnostic Single(string source, string id)
    {
        var harness = Harness.Generate(source);
        var diagnostic = Assert.Single(harness.GeneratorDiagnostics);
        Assert.Equal(id, diagnostic.Id);
        return diagnostic;
    }

    [Theory]
    [InlineData("public Uri Get() => null!;", "the result of S.Get cannot be sent between C# and pages: System.Uri is not a type Oriel maps")]
    [InlineData("public void Set(IDisposable d) { }", "parameter 'd' of S.Set cannot be sent between C# and pages: IDisposable is an interface; Oriel sends concrete classes, records and structs")]
    [InlineData("public void Set(int[,] grid) { }", "parameter 'grid' of S.Set cannot be sent between C# and pages: multidimensional arrays are not supported; use an array of arrays")]
    [InlineData("public void Set(Dictionary<int, string> d) { }", "parameter 'd' of S.Set cannot be sent between C# and pages: dictionaries need string keys, as JSON objects have")]
    [InlineData("public (int, int) Get() => default;", "the result of S.Get cannot be sent between C# and pages: tuples have no property names; use a record")]
    [InlineData("public IAsyncEnumerable<int> Get() => null!;", "the result of S.Get cannot be sent between C# and pages: streams (IAsyncEnumerable) are not supported yet")]
    [InlineData("public Task<Task<int>> Get() => null!;", "the result of S.Get cannot be sent between C# and pages: tasks can only be returned, not nested")]
    [InlineData("public void Set(Action a) { }", "parameter 'a' of S.Set cannot be sent between C# and pages: delegates cannot be sent")]
    public void ReportsUnsupportedTypes(string member, string message)
    {
        var diagnostic = Single($"[Service] public class S {{ {member} }}", "ORIEL001");
        Assert.Equal(message, diagnostic.GetMessage());
        Assert.Equal("Services.cs", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public void ReportsUnsupportedTypesAtThePropertyThatHasThem()
    {
        var diagnostic = Single("""
            public class Deck { public Uri Link { get; set; } = null!; }
            [Service] public class S { public void Save(Deck deck) { } }
            """, "ORIEL001");
        Assert.Equal("property Deck.Link cannot be sent between C# and pages: System.Uri is not a type Oriel maps", diagnostic.GetMessage());
        // The test sources start with five lines of usings: the property is on line 6.
        Assert.Equal(5, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void ReportsABrokenTypeOnce()
    {
        var harness = Harness.Generate("""
            public class Deck { public Uri Link { get; set; } = null!; }
            [Service] public class S { public void Save(Deck deck) { } public Deck Load() => null!; }
            """);
        Assert.Equal(["ORIEL001"], harness.Ids);
    }

    [Theory]
    [InlineData("[Flags] public enum E { A = 1, B = 2 }", "E is a [Flags] enum, which has no single name per value")]
    [InlineData("public abstract class E { }", "E is abstract; Oriel sends concrete classes, records and structs")]
    [InlineData("public class E<T> { }", "E is generic, which is not supported yet")]
    [InlineData("public class E { public E(int a) { } public E(string b) { } }", "E has several constructors; mark the one to read it with [JsonConstructor]")]
    [InlineData("public class E { public E(int other) { } public int A { get; set; } }", "parameter 'other' of the constructor of E matches none of its public properties")]
    public void ReportsTypesThatCannotBeRead(string type, string reason)
    {
        var parameter = type.Contains("<T>") ? "E<int>" : "E";
        var diagnostic = Single($"{type}\n[Service] public class S {{ public void Set({parameter} e) {{ }} }}", "ORIEL001");
        Assert.EndsWith(reason, diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("[Service] public static class S { }", "S cannot be a service: it is static; services are instances, bound with OrielApp.Bind")]
    [InlineData("[Service] public abstract class S { }", "S cannot be a service: it is abstract")]
    [InlineData("[Service] public class S<T> { }", "S cannot be a service: it is generic")]
    [InlineData("public class Outer { [Service] private class S { } }", "S cannot be a service: it is not accessible to generated code; make it public or internal")]
    [InlineData("[Service(Name = \"my.service\")] public class S { }", "S cannot be a service: its name 'my.service' is not an identifier")]
    public void ReportsClassesThatCannotBeServices(string source, string message)
    {
        Assert.Equal(message, Single(source, "ORIEL002").GetMessage());
    }

    [Fact]
    public void ReportsOverloadsOnEachOfThem()
    {
        var harness = Harness.Generate("[Service] public class S { public void Go() { } public void Go(int n) { } }");
        Assert.Equal(["ORIEL004", "ORIEL004"], harness.Ids);
        Assert.StartsWith("S.Go is overloaded", harness.GeneratorDiagnostics[0].GetMessage());
    }

    [Theory]
    [InlineData("public T Get<T>() => default!;", "S.Get cannot be called by pages: it is generic; hide it with [ServiceIgnore]")]
    [InlineData("public void Get(out int n) { n = 0; }", "S.Get cannot be called by pages: parameter 'n' is passed by reference; hide it with [ServiceIgnore]")]
    public void ReportsMethodsPagesCannotCall(string member, string message)
    {
        Assert.Equal(message, Single($"[Service] public class S {{ {member} }}", "ORIEL005").GetMessage());
    }

    [Fact]
    public void IgnoredMethodsAreNotChecked()
    {
        var harness = Harness.Compile("[Service] public class S { [ServiceIgnore] public T Get<T>() => default!; }");
        Assert.Empty(harness.GeneratorDiagnostics);
    }

    [Fact]
    public void ReportsServicesOfOneName()
    {
        var harness = Harness.Generate("""
            namespace A { [Service] public class Files { } }
            namespace B { [Service] public class Files { } }
            """);
        var diagnostic = Assert.Single(harness.GeneratorDiagnostics);
        Assert.Equal("ORIEL003", diagnostic.Id);
        Assert.StartsWith("Two services are named Files: global::A.Files and global::B.Files", diagnostic.GetMessage());
    }

    [Fact]
    public void ReportsTypesOfOneNameInTypeScript()
    {
        var harness = Harness.Generate("""
            namespace A { public class Item { } }
            namespace B { public class Item { } }
            [Service] public class S { public void Set(A.Item a, B.Item b) { } }
            """);
        var diagnostic = Assert.Single(harness.GeneratorDiagnostics);
        Assert.Equal("ORIEL003", diagnostic.Id);
        Assert.Equal("global::A.Item and global::B.Item would all be named Item in TypeScript; rename one of them", diagnostic.GetMessage());
    }

    [Fact]
    public void KeepsTheMethodsThatAreFine()
    {
        var harness = Harness.Generate("[Service] public class S { public Uri Bad() => null!; public int Good() => 1; }");
        Assert.Equal(["ORIEL001"], harness.Ids);
        Assert.Contains("case \"Good\":", harness.Source);
        Assert.DoesNotContain("case \"Bad\":", harness.Source);
    }
}
