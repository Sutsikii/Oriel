using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Oriel.Generators.Tests;

/// <summary>The generator does no work again for edits that leave services as they are.</summary>
public class IncrementalTests
{
    [Fact]
    public void ReusesTheParsedServicesWhenOtherCodeChanges()
    {
        var first = Harness.Generate("""
            public record Deck(string Title);
            [Service] public class Decks { public Deck Open(string path) => new(path); }
            """);
        Assert.Empty(first.GeneratorDiagnostics);

        var driver = CSharpGeneratorDriver.Create(
            [new OrielGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        var original = first.Compilation.RemoveSyntaxTrees(first.Compilation.SyntaxTrees.Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal)));
        GeneratorDriver ran = driver.RunGenerators(original);

        var edited = original.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Unrelated { }", new CSharpParseOptions(LanguageVersion.Latest)));
        ran = ran.RunGenerators(edited);

        var steps = ran.GetRunResult().Results.Single().TrackedSteps[OrielGenerator.ParseStep];
        Assert.All(steps.SelectMany(s => s.Outputs), output =>
            Assert.Contains(output.Reason, new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged }));
    }
}
