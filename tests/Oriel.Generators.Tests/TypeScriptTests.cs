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
