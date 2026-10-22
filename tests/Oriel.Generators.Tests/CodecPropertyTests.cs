using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsCheck;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel.Generators.Tests;

/// <summary>
/// Properties of generated codecs over many generated values, where
/// <see cref="CodecTests"/> checks chosen examples.
/// </summary>
public class CodecPropertyTests
{
    /// <summary>A sample of the types services use: every kind, nested.</summary>
    private static readonly Lazy<Service> s_echo = new(() => Harness.Compile("""
        namespace App;
        public enum Color { Red, Green, DarkBlue }
        public record Inner(string Name, int? Count, Color Color);
        public record Sample(
            string Text, bool Flag, int Int, long Long, double Double, decimal Decimal,
            Guid Id, DateTimeOffset At, byte[] Bytes, int? MaybeInt, string? MaybeText,
            List<Inner> Items, Dictionary<string, double> Weights, Inner? Child, Color[] Colors,
            System.Text.Json.JsonElement Any);
        [Service]
        public class Echo
        {
            public Sample Same(Sample s) => s;
            public string Text(string s) => s;
            public char Char(char c) => c;
            public byte Byte(byte b) => b;
            public int Int(int i) => i;
            public ulong ULong(ulong u) => u;
            public float Float(float f) => f;
            public double Double(double d) => d;
            public decimal Decimal(decimal d) => d;
            public Guid Guid(Guid g) => g;
            public DateTime DateTime(DateTime d) => d;
            public byte[] Bytes(byte[] b) => b;
            public Color Color(Color c) => c;
            public int Optional(int a, string b = "b", int? c = null) => a;
            public Dictionary<string, List<int?>> Nested(Dictionary<string, List<int?>> d) => d;
            public Inner? Maybe(Inner? i) => i;
        }
        """).Bind("App.Echo"));

    private static readonly string[] s_methods =
        ["Same", "Text", "Char", "Byte", "Int", "ULong", "Float", "Double", "Decimal", "Guid", "DateTime", "Bytes", "Color", "Optional", "Nested", "Maybe"];

    /// <summary>Text of any valid UTF-16: controls, quotes, escapes, astral characters.</summary>
    private static readonly Gen<string> s_text = Gen.OneOf(
            Gen.Char['\0', '\u007f'].Array[1, 6].Select(c => new string(c)),
            Gen.OneOfConst("\"", "\\", "</script>", "\u2028", "é", "\uFEFF"),
            Gen.Char['\u0080', '\uD7FF'].Array[1, 3].Select(c => new string(c)),
            Gen.Char['\uE000', '\uFFFF'].Array[1, 3].Select(c => new string(c)),
            Gen.Int[0x10000, 0x10FFFF].Select(char.ConvertFromUtf32))
        .Array[0, 5].Select(string.Concat);

    private static readonly Gen<JsonNode?> s_color = Gen.OneOfConst("red", "green", "darkBlue").Select(c => (JsonNode?)c);

    private static readonly Gen<JsonNode?> s_inner = Gen.Select(s_text, Gen.Int.Nullable(), s_color,
        (name, count, color) => (JsonNode?)new JsonObject { ["name"] = name, ["count"] = count, ["color"] = color });

    /// <summary>Any finite double, which JSON can hold.</summary>
    private static readonly Gen<double> s_double = Gen.Double.Where(double.IsFinite);

    private static readonly Gen<JsonNode?> s_sample =
        from text in s_text
        from flag in Gen.Bool
        from i in Gen.Int
        from l in Gen.Long
        from d in s_double
        from m in Gen.Decimal
        from id in Gen.Guid
        from at in Gen.DateTimeOffset
        from bytes in Gen.Byte.Array[0, 12]
        from maybeInt in Gen.Int.Nullable()
        from maybeText in s_text.Null()
        from items in s_inner.Array[0, 3]
        from weights in Gen.Select(s_text, s_double).Array[0, 3]
        from child in Gen.OneOf(s_inner, Gen.Const<JsonNode?>((JsonNode?)null))
        from colors in s_color.Array[0, 3]
        from any in AnyJson(2)
        select (JsonNode?)new JsonObject
        {
            ["text"] = text,
            ["flag"] = flag,
            ["int"] = i,
            ["long"] = l,
            ["double"] = d,
            ["decimal"] = m,
            ["id"] = id,
            ["at"] = at,
            ["bytes"] = Convert.ToBase64String(bytes),
            ["maybeInt"] = maybeInt,
            ["maybeText"] = maybeText,
            ["items"] = new JsonArray(items),
            ["weights"] = Dictionary(weights),
            ["child"] = child,
            ["colors"] = new JsonArray(colors),
            ["any"] = any,
        };

    [Fact]
    public void RoundTripsAnyValueOfTheSample()
    {
        s_sample.Sample(sample =>
        {
            var json = Write(sample);
            Assert.Equal(json, Call("Same", $"[{json}]"));
        }, iter: 2000, threads: 1, print: Write);
    }

    [Fact]
    public void RoundTripsAnyValidString()
    {
        s_text.Sample(text =>
        {
            var json = Write(text);
            Assert.Equal(json, Call("Text", $"[{json}]"));
        }, iter: 5000, threads: 1);
    }

    /// <summary>
    /// Whatever JSON a page sends, the dispatcher returns a result or says
    /// what did not fit with a <see cref="CodecException"/>: nothing else
    /// escapes, so pages always get a useful error.
    /// </summary>
    [Fact]
    public void ReadsAnyJsonOrSaysWhatDoesNotFit()
    {
        var args = Gen.Frequency(
            (8, ArgumentsJson()),
            (1, AnyJsonText(2)));
        Gen.Select(Gen.OneOfConst(s_methods), args).Sample((method, json) =>
        {
            try
            {
                Call(method, json);
            }
            catch (CodecException)
            {
            }
        }, iter: 20000, threads: 1, print: t => $"{t.Item1}({t.Item2})");
    }

    /// <summary>An array of up to three values, more often of the shapes the sample expects.</summary>
    private static Gen<string> ArgumentsJson() =>
        Gen.Frequency(
                (3, AnyJsonText(3)),
                (2, s_sample.Select(Write)),
                (1, s_inner.Select(Write)))
            .Array[0, 3].Select(values => "[" + string.Join(",", values) + "]");

    /// <summary>
    /// Any JSON text, written by hand to reach what a writer would not write:
    /// numbers too large for any type, lone surrogates in strings and names,
    /// duplicate names, escapes.
    /// </summary>
    private static Gen<string> AnyJsonText(int depth)
    {
        var name = Gen.OneOf(
            Gen.OneOfConst("name", "count", "color", "text", "items", "weights", "child", "any", "t", "k").Select(n => $"\"{n}\""),
            StringText());
        var leaf = Gen.OneOf(
            Gen.OneOfConst("null", "true", "false", "0", "-0", "1e400", "-1e400", "1e39", "1E-400", "2.5", "18446744073709551616",
                "-9223372036854775809", "79228162514264337593543950336", "0.0000000000000000000000000000001", "255", "256", "-1"),
            Gen.Long.Select(l => l.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            s_double.Select(d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
            StringText(),
            Gen.OneOfConst("\"red\"", "\"darkBlue\"", "\"AQID\"", "\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"", "\"2026-10-21T12:00:00+02:00\"", "\"2026-13-45\""));
        if (depth == 0)
            return leaf;
        var inner = AnyJsonText(depth - 1);
        return Gen.Frequency(
            (4, leaf),
            (1, inner.Array[0, 4].Select(items => "[" + string.Join(",", items) + "]")),
            (1, Gen.Select(name, inner).Array[0, 4].Select(props => "{" + string.Join(",", props.Select(p => p.Item1 + ":" + p.Item2)) + "}")));
    }

    /// <summary>A JSON string, possibly with escapes no valid UTF-16 has.</summary>
    private static Gen<string> StringText() =>
        Gen.OneOf(
                s_text.Select(Write),
                Gen.OneOfConst("\"\\ud800\"", "\"a\\udc00\"", "\"\\ud83d\\ude00\"", "\"\\ud800\\ud800\"", "\"\\u0000\"", "\"x\""));

    private static Gen<JsonNode?> AnyJson(int depth)
    {
        var leaf = Gen.OneOf(
            Gen.Const<JsonNode?>((JsonNode?)null),
            Gen.Bool.Select(b => (JsonNode?)b),
            s_double.Select(d => (JsonNode?)d),
            Gen.Long.Select(l => (JsonNode?)l),
            s_text.Select(s => (JsonNode?)s));
        if (depth == 0)
            return leaf;
        var inner = AnyJson(depth - 1);
        return Gen.Frequency(
            (4, leaf),
            (1, inner.Array[0, 3].Select(items => (JsonNode?)new JsonArray(items))),
            (1, Gen.Select(s_text, inner).Array[0, 3].Select(props => (JsonNode?)Dictionary(props))));
    }

    /// <summary>An object of distinct names, the last value of a name kept, as reading it would.</summary>
    private static JsonObject Dictionary<T>((string Key, T Value)[] pairs)
    {
        var o = new JsonObject();
        foreach (var (key, value) in pairs)
            o[key] = value is JsonNode node ? node.DeepClone() : JsonValue.Create(value);
        return o;
    }

    /// <summary>The JSON of <paramref name="node"/>, written as Oriel writes replies.</summary>
    private static string Write(JsonNode? node)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, Protocol.WriterOptions))
        {
            if (node is null)
                w.WriteNullValue();
            else
                node.WriteTo(w);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string Write(string text) => Write(JsonValue.Create(text));

    private static string? Call(string method, string args) =>
        s_echo.Value.Call(method, args).GetAwaiter().GetResult();
}
