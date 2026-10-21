using System.ComponentModel;
using System.Text.Json;

namespace Oriel.Generated;

/// <summary>
/// Reads the arguments of a call: the JSON array a page sent. Generated
/// dispatchers check how many there are, then read each by position.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Arguments
{
    /// <summary>
    /// Returns how many arguments <paramref name="args"/> holds, after checking
    /// it is an array of <paramref name="min"/> to <paramref name="max"/> of them.
    /// </summary>
    public static int Count(JsonElement args, string method, int min, int max)
    {
        if (args.ValueKind != JsonValueKind.Array)
            throw new CodecException($"{method}: the arguments are not an array");
        var n = args.GetArrayLength();
        if (n < min || n > max)
        {
            var expected = min == max ? $"{min}" : $"{min} to {max}";
            throw new CodecException($"{method} takes {expected} argument(s), got {n}");
        }
        return n;
    }

    /// <summary>
    /// Reads the optional argument at <paramref name="index"/>, or returns
    /// <paramref name="fallback"/>, its default value, when the page left it
    /// out. A null stands for a left-out argument too where the parameter
    /// cannot be null: pages write <c>f(a, undefined, c)</c> as <c>[a, null, c]</c>.
    /// </summary>
    public static T ReadOptional<T>(JsonElement args, int index, string name, Func<JsonElement, T> read, T fallback, bool nullable)
    {
        if (index >= args.GetArrayLength())
            return fallback;
        if (!nullable && Json.IsNull(args[index]))
            return fallback;
        return Read(args, index, name, read);
    }

    /// <summary>Reads the argument at <paramref name="index"/>, naming it in errors.</summary>
    public static T Read<T>(JsonElement args, int index, string name, Func<JsonElement, T> read)
    {
        try
        {
            return read(args[index]);
        }
        catch (CodecException e)
        {
            throw new CodecException($"argument '{name}': {e.Message}");
        }
    }
}
