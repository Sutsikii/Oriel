using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Oriel.Generators;

/// <summary>Everything the emitters write from: the services of a project and the types they use.</summary>
internal sealed record Project(
    EquatableArray<ServiceDef> Services,
    EquatableArray<EnumDef> Enums,
    EquatableArray<ObjectDef> Objects,
    EquatableArray<ErrorDef> Errors);

/// <summary>
/// The Oriel source generator. For the <c>[Service]</c> classes of a project,
/// it writes their dispatchers, the JSON codecs of their types and their
/// TypeScript client, and registers them with Oriel as the assembly loads.
/// docs/architecture.md walks through what it generates.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class OrielGenerator : IIncrementalGenerator
{
    /// <summary>The name of the generated C# file.</summary>
    public const string SourceName = "Oriel.Services.g.cs";

    /// <summary>The name of the pipeline step that parses services, for tests of caching.</summary>
    internal const string ParseStep = "ParseServices";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var services = context.SyntaxProvider.ForAttributeWithMetadataName(
            ServiceParser.ServiceAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            ServiceParser.Parse)
            .WithTrackingName(ParseStep);

        var errors = context.SyntaxProvider.ForAttributeWithMetadataName(
            ErrorParser.ServiceErrorAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            ErrorParser.Parse);

        var assembly = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "the app");

        // OrielTypeScriptRuntime: the package the client imports its runtime from, such as oriel-runtime; empty, none.
        var runtime = context.AnalyzerConfigOptionsProvider.Select(static (o, _) =>
            o.GlobalOptions.TryGetValue("build_property.OrielTypeScriptRuntime", out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null);

        context.RegisterSourceOutput(services.Collect().Combine(errors.Collect()).Combine(assembly).Combine(runtime), static (spc, input) =>
        {
            var (((results, errorResults), assemblyName), runtimePackage) = input;
            var project = Merge(results, errorResults, out var diagnostics);
            foreach (var diagnostic in diagnostics)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
            if (project.Services.Count + project.Errors.Count > 0)
                spc.AddSource(SourceName, CSharpEmitter.Emit(project, TypeScriptEmitter.Emit(project, assemblyName, runtimePackage)));
        });
    }

    /// <summary>
    /// Merges what each service brought into one project, with its types
    /// once each and in a stable order, and checks names are unique.
    /// </summary>
    internal static Project Merge(ImmutableArray<ServiceResult> results, ImmutableArray<ErrorResult> errorResults, out List<DiagnosticInfo> diagnostics)
    {
        diagnostics = results.SelectMany(r => r.Diagnostics).Concat(errorResults.SelectMany(r => r.Diagnostics)).ToList();

        var services = new List<ServiceDef>();
        foreach (var group in results.Select(r => r.Service).OfType<ServiceDef>().GroupBy(s => s.Name).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            services.Add(group.First());
            if (group.Count() > 1)
                diagnostics.Add(new DiagnosticInfo(Diagnostics.DuplicateName, null, new[] { $"Two services are named {group.Key}: {string.Join(" and ", group.Select(s => s.CSharp))}; give one another name with [Service(Name = \"...\")]" }.ToEquatableArray()));
        }

        var enums = results.SelectMany(r => r.Enums).Concat(errorResults.SelectMany(r => r.Enums)).GroupBy(e => e.Id).Select(g => g.First()).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        var objects = results.SelectMany(r => r.Objects).Concat(errorResults.SelectMany(r => r.Objects)).GroupBy(o => o.Id).Select(g => g.First()).OrderBy(o => o.Id, StringComparer.Ordinal).ToList();

        var errors = errorResults.Select(r => r.Error).OfType<ErrorDef>().OrderBy(e => e.Code, StringComparer.Ordinal).ToList();
        foreach (var clash in errors.GroupBy(e => e.Code).Where(g => g.Count() > 1))
            diagnostics.Add(new DiagnosticInfo(Diagnostics.DuplicateName, null, new[] { $"{string.Join(" and ", clash.Select(e => e.CSharp))} share the error code \"{clash.Key}\"; give each its own" }.ToEquatableArray()));

        // TypeScript knows types by their short names.
        var named = enums.Select(e => (e.TypeScript, e.CSharp))
            .Concat(objects.Select(o => (o.TypeScript, o.CSharp)))
            .Concat(errors.Select(e => (e.TypeScript, e.CSharp)))
            .Concat(services.Select(s => (TypeScript: s.Name, s.CSharp)));
        foreach (var clash in named.GroupBy(n => n.TypeScript).Where(g => g.Count() > 1).OrderBy(g => g.Key, StringComparer.Ordinal))
            diagnostics.Add(new DiagnosticInfo(Diagnostics.DuplicateName, null, new[] { $"{string.Join(" and ", clash.Select(c => c.CSharp))} would all be named {clash.Key} in TypeScript; rename one of them" }.ToEquatableArray()));

        return new Project(services.ToEquatableArray(), enums.ToEquatableArray(), objects.ToEquatableArray(), errors.ToEquatableArray());
    }
}
