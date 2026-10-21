using System.Text.Json;
using Oriel.Generated;

namespace Oriel.Tests.Generated;

public class ArgumentsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("[]", 0, 0, 0)]
    [InlineData("[1,2]", 1, 3, 2)]
    public void CountsTheArguments(string args, int min, int max, int count)
    {
        Assert.Equal(count, Arguments.Count(Parse(args), "S.M", min, max));
    }

    [Theory]
    [InlineData("[]", 1, 1, "S.M takes 1 argument(s), got 0")]
    [InlineData("[1,2,3]", 1, 2, "S.M takes 1 to 2 argument(s), got 3")]
    [InlineData("{}", 0, 0, "S.M: the arguments are not an array")]
    public void RefusesTheWrongNumber(string args, int min, int max, string message)
    {
        var e = Assert.Throws<CodecException>(() => Arguments.Count(Parse(args), "S.M", min, max));
        Assert.Equal(message, e.Message);
    }

    [Fact]
    public void NamesTheArgumentInErrors()
    {
        var e = Assert.Throws<CodecException>(() => Arguments.Read(Parse("[\"a\", 3]"), 1, "title", Json.ReadString));
        Assert.Equal("argument 'title': expected a string, got a number", e.Message);
    }
}
