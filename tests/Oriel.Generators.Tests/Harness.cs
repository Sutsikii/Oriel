using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Oriel.Generated;
using Oriel.Generators;

namespace Oriel.Generators.Tests;

/// <summary>
/// Runs the generator on C# source, as a project's build would, and loads
/// what it compiled so tests can call the generated dispatchers.
/// </summary>
internal sealed class Harness
{
    private static readonly ImmutableArray<MetadataReference> s_references =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(ServiceAttribute).Assembly.Location),
    ];

    private Assembly? _assembly;

    private Harness(Compilation compilation, GeneratorDriverRunResult run, ImmutableArray<Diagnostic> diagnostics)
    {
        Compilation = compilation;
        Run = run;
        GeneratorDiagnostics = diagnostics;
    }

    /// <summary>The compilation with the generated sources.</summary>
    public Compilation Compilation { get; }

    public GeneratorDriverRunResult Run { get; }

    /// <summary>What the generator reported.</summary>
    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

    /// <summary>The generated C#, or "" when nothing was generated.</summary>
    public string Source =>
        Run.Results.Single().GeneratedSources.FirstOrDefault(s => s.HintName == OrielGenerator.SourceName).SourceText?.ToString() ?? "";

    public static Harness Generate(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "using Oriel;\nusing System;\nusing System.Collections.Generic;\nusing System.Threading;\nusing System.Threading.Tasks;\n" + source,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: "Services.cs");
        var compilation = CSharpCompilation.Create(
            "App" + Guid.NewGuid().ToString("N"),
            [tree],
            s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new OrielGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new Harness(output, driver.GetRunResult(), diagnostics);
    }

    /// <summary>Generates from <paramref name="source"/>, checking the generator and the compiler report nothing.</summary>
    public static Harness Compile(string source)
    {
        var harness = Generate(source);
        Assert.Empty(harness.GeneratorDiagnostics);
        var problems = harness.Compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems) + "\n\n" + harness.Source);
        return harness;
    }

    /// <summary>The ids of what the generator reported, in order.</summary>
    public IEnumerable<string> Ids => GeneratorDiagnostics.Select(d => d.Id);

    /// <summary>Creates an instance of <paramref name="type"/> from the compiled code and its dispatcher.</summary>
    public Service Bind(string type)
    {
        _assembly ??= Load();
        var instance = Activator.CreateInstance(_assembly.GetType(type, throwOnError: true)!)!;
        Assert.True(ServiceRegistry.TryCreate(instance, out var dispatcher), $"no dispatcher registered for {type}");
        return new Service(instance, dispatcher);
    }

    private Assembly Load()
    {
        using var stream = new MemoryStream();
        var emitted = Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return assembly;
    }
}

/// <summary>A bound service of a test, called as a page would.</summary>
internal sealed class Service(object instance, ServiceDispatcher dispatcher)
{
    public object Instance { get; } = instance;

    public ServiceDispatcher Dispatcher { get; } = dispatcher;

    /// <summary>Calls <paramref name="method"/> with the JSON array <paramref name="args"/>; returns the result's JSON, or null for none.</summary>
    public async Task<string?> Call(string method, string args = "[]", CancellationToken cancellation = default)
    {
        using var doc = JsonDocument.Parse(args);
        var writer = await Dispatcher.InvokeAsync(method, doc.RootElement, new CallContext(cancellation));
        if (writer is null)
            return null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, Oriel.Ipc.Protocol.WriterOptions))
            writer(w);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Calls a method that must fail, and returns the error's message.</summary>
    public async Task<string> Fail(string method, string args = "[]")
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(() => Call(method, args));
        return e.Message;
    }
}
