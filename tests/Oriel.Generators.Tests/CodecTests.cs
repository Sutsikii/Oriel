namespace Oriel.Generators.Tests;

/// <summary>How generated codecs read and write the types services use.</summary>
public class CodecTests
{
    /// <summary>A service echoing each type back: what was read is written again.</summary>
    private static Service Echo(string types, string echoes) => Harness.Compile($$"""
        namespace App;
        {{types}}
        [Service]
        public class Echo
        {
            {{echoes}}
        }
        """).Bind("App.Echo");

    [Theory]
    [InlineData("string", "\"hé \\\"x\\\" </script>\"")]
    [InlineData("bool", "false")]
    [InlineData("int", "-12")]
    [InlineData("long", "9007199254740991")]
    [InlineData("double", "2.5")]
    [InlineData("decimal", "1.25")]
    [InlineData("byte[]", "\"AQID\"")]
    [InlineData("Guid", "\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"")]
    [InlineData("DateTimeOffset", "\"2026-10-21T12:00:00+02:00\"")]
    [InlineData("System.Text.Json.JsonElement", "{\"any\":[1,\"thing\"]}")]
    [InlineData("int[]", "[1,2,3]")]
    [InlineData("List<string>", "[\"a\",\"b\"]")]
    [InlineData("IReadOnlyList<int>", "[]")]
    [InlineData("Dictionary<string, int>", "{\"a\":1,\"b\":2}")]
    [InlineData("IReadOnlyDictionary<string, List<int>>", "{\"a\":[1]}")]
    [InlineData("int?", "null")]
    [InlineData("int?", "3")]
    [InlineData("string?", "null")]
    [InlineData("List<int?>", "[1,null]")]
    public async Task RoundTripsBuiltInTypes(string type, string json)
    {
        var echo = Echo("", $"public {type} Same({type} value) => value;");
        Assert.Equal(json, await echo.Call("Same", $"[{json}]"));
    }

    [Fact]
    public async Task RoundTripsRecordsEnumsAndNesting()
    {
        var echo = Echo("""
            public enum Theme { Light, Dark, HighContrast }
            public record Slide(string Title, Theme Theme, int? Rating);
            public record Deck(string Title, List<Slide> Slides, Dictionary<string, string> Meta);
            """, "public Deck Same(Deck deck) => deck;");

        const string Deck = """{"title":"Demo","slides":[{"title":"One","theme":"dark","rating":null},{"title":"Two","theme":"highContrast","rating":5}],"meta":{"by":"me"}}""";
        Assert.Equal(Deck, await echo.Call("Same", $"[{Deck}]"));
    }

    [Fact]
    public async Task RoundTripsTypesThatReferToThemselves()
    {
        var echo = Echo("public class Node { public string Name { get; set; } = \"\"; public List<Node> Children { get; set; } = new(); }",
            "public Node Same(Node node) => node;");

        const string Tree = """{"name":"root","children":[{"name":"leaf","children":[]}]}""";
        Assert.Equal(Tree, await echo.Call("Same", $"[{Tree}]"));
    }

    [Fact]
    public async Task KeepsTheDefaultsOfPropertiesLeftOut()
    {
        var echo = Echo("""
            public class Options
            {
                public string Color { get; set; } = "red";
                public int Size { get; init; } = 12;
                public bool Bold { get; set; }
            }
            """, "public Options Same(Options o) => o;");

        Assert.Equal("""{"color":"red","size":12,"bold":true}""", await echo.Call("Same", """[{"bold":true}]"""));
        Assert.Equal("""{"color":"blue","size":30,"bold":false}""", await echo.Call("Same", """[{"color":"blue","size":30}]"""));
    }

    [Fact]
    public async Task SetsInitAndRequiredPropertiesWithoutReflection()
    {
        var echo = Echo("""
            public class User
            {
                public required string Name { get; init; }
                public string? Email { get; init; }
            }
            public struct Point
            {
                public int X { get; init; }
                public int Y { get; init; }
            }
            """, """
            public User SameUser(User u) => u;
            public Point SamePoint(Point p) => p;
            """);

        Assert.Equal("""{"name":"Ada","email":null}""", await echo.Call("SameUser", """[{"name":"Ada"}]"""));
        Assert.Equal("argument 'u': missing property 'name'", await echo.Fail("SameUser", """[{"email":"a@b.c"}]"""));
        Assert.Equal("""{"x":1,"y":2}""", await echo.Call("SamePoint", """[{"x":1,"y":2}]"""));
    }

    [Fact]
    public async Task RecordParametersCanBeLeftOutWhenTheyHaveADefault()
    {
        var echo = Echo("public record Page(string Title, int Number = 1, string? Note = null);",
            "public Page Same(Page p) => p;");

        Assert.Equal("""{"title":"A","number":1,"note":null}""", await echo.Call("Same", """[{"title":"A"}]"""));
        Assert.Equal("argument 'p': missing property 'title'", await echo.Fail("Same", """[{}]"""));
    }

    [Fact]
    public async Task HonorsSystemTextJsonAttributes()
    {
        var echo = Echo("""
            using System.Text.Json.Serialization;
            public enum Level { [JsonStringEnumMemberName("lo")] Low, High }
            public class Item
            {
                [JsonPropertyName("item_id")] public int Id { get; set; }
                [JsonIgnore] public string Cache { get; set; } = "kept out";
                public Level Level { get; set; }
            }
            """, "public Item Same(Item i) => i;");

        Assert.Equal("""{"item_id":4,"level":"lo"}""", await echo.Call("Same", """[{"item_id":4,"level":"lo","cache":"ignored"}]"""));
    }

    [Fact]
    public async Task WritesNamesInCamelCase()
    {
        var echo = Echo("public class Names { public int URLPath { get; set; } public int ID { get; set; } public int fooBar { get; set; } }",
            "public Names Same(Names n) => n;");

        Assert.Equal("""{"urlPath":0,"id":0,"fooBar":0}""", await echo.Call("Same", "[{}]"));
    }

    [Fact]
    public async Task WritesNullForAMissingObject()
    {
        var echo = Echo("public class Thing { public int A { get; set; } }",
            "public Thing? Nothing() => null; public List<Thing> Holes() => new() { null! };");

        Assert.Equal("null", await echo.Call("Nothing"));
        Assert.Equal("[null]", await echo.Call("Holes"));
    }

    [Theory]
    [InlineData("""[{"title":"A","slides":[{"title":1}]}]""", "argument 'deck': $.slides[0].title: expected a string, got a number")]
    [InlineData("""[{"title":"A","slides":{}}]""", "argument 'deck': $.slides: expected an array, got an object")]
    [InlineData("""[[]]""", "argument 'deck': expected an object, got an array")]
    [InlineData("""[{"title":null,"slides":[]}]""", "argument 'deck': $.title: expected a string, got null")]
    public async Task SaysWhereAValueDoesNotFit(string args, string message)
    {
        var echo = Echo("""
            public record Slide(string Title);
            public record Deck(string Title, List<Slide> Slides);
            """, "public int Count(Deck deck) => deck.Slides.Count;");

        Assert.Equal(message, await echo.Fail("Count", args));
    }

    [Fact]
    public async Task RefusesNamesThatAreNotOfAnEnum()
    {
        var echo = Echo("public enum Mode { Read, Write }", "public Mode Same(Mode m) => m;");
        Assert.Equal("argument 'm': \"Append\" is not a Mode: expected one of \"read\", \"write\"", await echo.Fail("Same", """["Append"]"""));
    }

    [Fact]
    public async Task WritesEnumAliasesWithTheirFirstName()
    {
        var echo = Echo("public enum Size { Small = 1, Tiny = 1, Large = 2 }", "public Size Same(Size s) => s;");
        Assert.Equal("\"small\"", await echo.Call("Same", """["small"]"""));
        Assert.Equal("argument 's': \"tiny\" is not a Size: expected one of \"small\", \"large\"", await echo.Fail("Same", """["tiny"]"""));
    }
}
