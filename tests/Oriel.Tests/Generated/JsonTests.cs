using System.Buffers;
using System.Text;
using System.Text.Json;
using Oriel.Generated;

namespace Oriel.Tests.Generated;

public class JsonTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string Written(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
            write(w);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [Fact]
    public void ReadsTheValuesOfTheirKind()
    {
        Assert.Equal("hé", Json.ReadString(Parse("\"hé\"")));
        Assert.True(Json.ReadBoolean(Parse("true")));
        Assert.Equal(42, Json.ReadInt32(Parse("42")));
        Assert.Equal(2.5, Json.ReadDouble(Parse("2.5")));
        Assert.Equal(1.25m, Json.ReadDecimal(Parse("1.25")));
        Assert.Equal('x', Json.ReadChar(Parse("\"x\"")));
        Assert.Equal(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"), Json.ReadGuid(Parse("\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"")));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.FromHours(2)), Json.ReadDateTimeOffset(Parse("\"2026-10-21T12:00:00+02:00\"")));
        Assert.Equal([1, 2, 3], Json.ReadBytes(Parse("\"AQID\"")));
    }

    [Theory]
    [InlineData("12", "expected a string, got a number")]
    [InlineData("null", "expected a string, got null")]
    [InlineData("{}", "expected a string, got an object")]
    public void SaysWhatAStringWasNot(string json, string message)
    {
        var e = Assert.Throws<CodecException>(() => Json.ReadString(Parse(json)));
        Assert.Equal(message, e.Message);
    }

    [Fact]
    public void RefusesIntegersOutOfRange()
    {
        Assert.Equal("300 does not fit Byte", Assert.Throws<CodecException>(() => Json.ReadByte(Parse("300"))).Message);
        Assert.Equal("1.5 does not fit Int32", Assert.Throws<CodecException>(() => Json.ReadInt32(Parse("1.5"))).Message);
        Assert.Equal("expected a number, got a string", Assert.Throws<CodecException>(() => Json.ReadInt32(Parse("\"1\""))).Message);
    }

    [Fact]
    public void WritesNonFiniteNumbersAsNull()
    {
        Assert.Equal("[null,null,1.5]", Written(w =>
        {
            w.WriteStartArray();
            Json.Write(w, double.NaN);
            Json.Write(w, float.PositiveInfinity);
            Json.Write(w, 1.5);
            w.WriteEndArray();
        }));
    }

    [Fact]
    public void KeepsAnElementBeyondItsDocument()
    {
        JsonElement kept;
        using (var doc = JsonDocument.Parse("{\"a\":[1,2]}"))
            kept = Json.ReadElement(doc.RootElement);
        Assert.Equal("{\"a\":[1,2]}", Written(w => Json.Write(w, kept)));
    }

    [Fact]
    public void PathsGrowFromTheInside()
    {
        var e = new CodecException("expected a string, got a number").Under(".title").Under(Json.Index(2)).Under(".slides");
        Assert.Equal("$.slides[2].title: expected a string, got a number", e.Message);
    }
}
