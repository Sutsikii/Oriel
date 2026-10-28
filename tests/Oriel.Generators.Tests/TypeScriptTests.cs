namespace Oriel.Generators.Tests;

/// <summary>The TypeScript client the generator writes.</summary>
public class TypeScriptTests
{
    /// <summary>
    /// Services using every kind of type. Its client is kept in
    /// tests/typescript/generated/client.ts, which tests/typescript type-checks
    /// and runs; set ORIEL_UPDATE_SNAPSHOTS=1 to rewrite it after a change.
    /// </summary>
    private const string Sample = """
        namespace Sample;

        /// <summary>How a slide looks.</summary>
        public enum Theme
        {
            /// <summary>Dark text on white.</summary>
            Light,
            Dark,
            HighContrast,
        }

        /// <summary>One page of a <see cref="Deck"/>.</summary>
        public record Slide(string Title, Theme Theme, int? Rating);

        /// <summary>A presentation.</summary>
        public class Deck
        {
            /// <summary>The title, shown on the first slide.</summary>
            public string Title { get; set; } = "";
            public List<Slide> Slides { get; set; } = new();
            public Dictionary<string, string?> Meta { get; set; } = new();
            public Guid Id { get; init; }
            public DateTimeOffset Created { get; init; }
            public byte[] Thumbnail { get; set; } = [];
            public int SlideCount => Slides.Count;
        }

        /// <summary>The deck changed on disk since it was opened.</summary>
        [ServiceError("conflict")]
        public class ConflictException(string path, int version) : Exception($"{path} changed")
        {
            public string Path { get; } = path;
            public int Version { get; } = version;
        }

        /// <summary>Opens, saves and searches decks.</summary>
        [Service]
        public class Decks
        {
            /// <summary>Reads the deck at <paramref name="path"/>.</summary>
            /// <param name="path">Where the deck is, absolute.</param>
            /// <returns>The deck, or null when there is none.</returns>
            public Task<Deck?> Open(string path) => Task.FromResult<Deck?>(null);

            /// <summary>
            /// Writes <paramref name="deck"/>.
            /// <para>The file is replaced atomically.</para>
            /// </summary>
            public Task Save(string path, Deck deck, CancellationToken cancellation = default) => Task.CompletedTask;

            public List<string?> Search(string query, int limit = 10, Theme? theme = null) => new();

            public System.Text.Json.JsonElement Raw(System.Text.Json.JsonElement value) => value;

            /// <summary>The thumbnail's pixels, as RGBA bytes.</summary>
            public Task<ReadOnlyMemory<byte>> Pixels(string path) => Task.FromResult(ReadOnlyMemory<byte>.Empty);

            public Memory<float> Levels() => default;

            /// <summary>The slides of the deck at <paramref name="path"/>, as they are read.</summary>
            public IAsyncEnumerable<Slide> Slides(string path, CancellationToken cancellation = default) => null!;

            /// <summary>Fires when a deck changed on disk.</summary>
            public Event<Deck> Changed { get; } = new();
        }

        [Service]
        public class Greeter
        {
            /// <summary>Returns a greeting.</summary>
            public string Greet(string name) => "Hello, " + name;

            public void Delete(string @default, bool @new) { }
        }
        """;

    [Fact]
    public void MatchesTheSnapshot()
    {
        var client = System.Text.RegularExpressions.Regex.Replace(Harness.Compile(Sample).TypeScript, "App[0-9a-f]{32}", "Sample");
        var snapshot = Path.Combine(RepositoryRoot(), "tests", "typescript", "generated", "client.ts");
        if (Environment.GetEnvironmentVariable("ORIEL_UPDATE_SNAPSHOTS") == "1")
            File.WriteAllText(snapshot, Normalize(client));
        Assert.True(File.Exists(snapshot), $"{snapshot} is missing; run the tests with ORIEL_UPDATE_SNAPSHOTS=1");
        Assert.Equal(File.ReadAllText(snapshot), Normalize(client));
    }

    /// <summary>
    /// The clients of an app and of a library it uses: the index and its two
    /// modules are kept in tests/typescript/generated/multi, which
    /// tests/typescript type-checks and runs.
    /// </summary>
    [Fact]
    public void WritesAModulePerAssemblyAndAnIndex()
    {
        var app = Harness.Compile("""
            namespace Notes;
            [ServiceError("locked")]
            public class LockedException(string by) : Exception("locked") { public string By { get; } = by; }
            public record Note(string Title);
            [Service]
            public class Notes { public Note[] All() => []; public void Lock() => throw new LockedException("ada"); }
            """).TypeScript;
        var library = Harness.Compile("""
            namespace Sync;
            public enum State { Idle, Busy }
            [Service]
            public class Sync { public State Now() => State.Idle; public Event<State> Changed { get; } = new(); }
            """).TypeScript;
        var root = Path.Combine(RepositoryRoot(), "tests", "typescript", "generated", "multi");
        var files = TypeScriptClient.Files([("Notes.App", Rename(app, "Notes.App")), ("Sync", Rename(library, "Sync"))], Path.Combine(root, "oriel.ts"));
        Assert.Equal(3, files.Count);
        var index = files[Path.Combine(root, "oriel.ts")];
        Assert.Equal(
            "// Generated by Oriel: the clients of Notes.App, Sync. Do not edit.\n"
            + "export { CallError, Notes, onFilesDropped } from \"./oriel/Notes.App.js\";\n"
            + "export type { LockedException, Note } from \"./oriel/Notes.App.js\";\n"
            + "export { Sync } from \"./oriel/Sync.js\";\n"
            + "export type { State } from \"./oriel/Sync.js\";\n",
            index);
        foreach (var (path, source) in files)
        {
            if (Environment.GetEnvironmentVariable("ORIEL_UPDATE_SNAPSHOTS") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Normalize(source));
            }
            Assert.True(File.Exists(path), $"{path} is missing; run the tests with ORIEL_UPDATE_SNAPSHOTS=1");
            Assert.Equal(File.ReadAllText(path), Normalize(source));
        }

        static string Rename(string client, string assembly) =>
            System.Text.RegularExpressions.Regex.Replace(client, "App[0-9a-f]{32}", assembly);
    }

    /// <summary>
    /// A client that imports its runtime from the oriel-runtime package
    /// (OrielTypeScriptRuntime): kept in packages/runtime/test, which the
    /// packages' tests type-check and run against the package.
    /// </summary>
    [Fact]
    public void ImportsTheRuntimeFromItsPackageWhenAsked()
    {
        var client = Harness.Compile(Sample).TypeScript;
        var imported = System.Text.RegularExpressions.Regex.Replace(
            Harness.Generate(Sample, new Dictionary<string, string> { ["OrielTypeScriptRuntime"] = "oriel-runtime" }).TypeScript, "App[0-9a-f]{32}", "Sample");
        Assert.Contains("import { CallErrorBase, bridge, call, on, stream } from \"oriel-runtime\";", imported);
        Assert.DoesNotContain("class CallErrorBase", imported);
        Assert.DoesNotContain("function call<T>", imported);
        Assert.Contains("export const CallError = CallErrorBase as unknown as {", imported);
        Assert.Contains("export const Decks = {", imported);
        Assert.Contains("class CallErrorBase", client); // without the property, as before

        var snapshot = Path.Combine(RepositoryRoot(), "packages", "runtime", "test", "client.ts");
        if (Environment.GetEnvironmentVariable("ORIEL_UPDATE_SNAPSHOTS") == "1")
            File.WriteAllText(snapshot, Normalize(imported));
        Assert.True(File.Exists(snapshot), $"{snapshot} is missing; run the tests with ORIEL_UPDATE_SNAPSHOTS=1");
        Assert.Equal(File.ReadAllText(snapshot), Normalize(imported));
    }

    [Fact]
    public void ReportsNamesTwoAssembliesExport()
    {
        var a = Harness.Compile("[Service] public class Files { public void Open() { } }").TypeScript;
        var b = Harness.Compile("namespace B; public record Files(int N); [Service] public class Store { public Files Get() => new(1); }").TypeScript;
        var e = Assert.Throws<InvalidOperationException>(() => TypeScriptClient.Files([("A", a), ("B", b)], "oriel.ts"));
        Assert.Contains("export the same names: Files (in A and B)", e.Message);
    }

    [Fact]
    public void WritesOneClientAsItIs()
    {
        var a = Harness.Compile("[Service] public class Files { public void Open() { } }").TypeScript;
        var files = TypeScriptClient.Files([("A", a)], "oriel.ts");
        Assert.Equal(a, Assert.Single(files).Value);
    }

    [Fact]
    public void MapsEnumsToUnionsOfTheirNames()
    {
        var ts = Harness.Compile(Sample).TypeScript;
        Assert.Contains("export type Theme = \"light\" | \"dark\" | \"highContrast\";", ts);
    }

    [Fact]
    public void MapsNullableTypesToUnionsWithNull()
    {
        var ts = Harness.Compile(Sample).TypeScript;
        Assert.Contains("rating: number | null;", ts);
        Assert.Contains("meta: Record<string, string | null>;", ts);
        Assert.Contains("search(query: string, limit?: number, theme?: Theme | null): Promise<(string | null)[]>", ts);
    }

    [Fact]
    public void MarksPropertiesWithoutSettersReadOnly()
    {
        var ts = Harness.Compile(Sample).TypeScript;
        Assert.Contains("readonly slideCount: number;", ts);
        Assert.Contains("  title: string;", ts);
    }

    [Fact]
    public void LeavesOutWhatOrielPasses()
    {
        var ts = Harness.Compile(Sample).TypeScript;
        Assert.Contains("save(path: string, deck: Deck): Promise<void> {", ts);
        Assert.Contains("return call(\"Decks.Save\", path, deck);", ts);
    }

    [Fact]
    public void RenamesParametersNamedWithReservedWords()
    {
        var ts = Harness.Compile(Sample).TypeScript;
        Assert.Contains("delete(default_: string, new_: boolean): Promise<void> {", ts);
    }

    [Fact]
    public void TurnsDocCommentsIntoTsDoc()
    {
        var ts = Harness.Compile(Sample).TypeScript;
        Assert.Contains("""
              /**
               * Reads the deck at path.
               * @param path Where the deck is, absolute.
               * @returns The deck, or null when there is none.
               */
              open(path: string): Promise<Deck | null> {
            """.Replace("\r", ""), ts);
        Assert.Contains("/** One page of a Deck. */", ts);
        Assert.Contains("""
              /**
               * Writes deck.
               *
               * The file is replaced atomically.
               */
            """.Replace("\r", ""), ts);
    }

    [Fact]
    public void ListsTypesAndServicesInAStableOrder()
    {
        var ts = Harness.Compile(Sample).TypeScript;
        Assert.True(ts.IndexOf("interface Deck", StringComparison.Ordinal) < ts.IndexOf("interface Slide", StringComparison.Ordinal));
        Assert.True(ts.IndexOf("type Theme", StringComparison.Ordinal) > ts.IndexOf("interface Slide", StringComparison.Ordinal));
        Assert.True(ts.IndexOf("const Decks", StringComparison.Ordinal) < ts.IndexOf("const Greeter", StringComparison.Ordinal));
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Oriel.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Oriel.slnx not found");
    }
}
