using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace Oriel.Generated;

/// <summary>
/// Reads and writes the JSON of the types Oriel maps to TypeScript. Generated
/// codecs call these for built-in types and compose them for the rest.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Json
{
    /// <summary>Reads a string.</summary>
    public static string ReadString(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw Expected("a string", e);

    /// <summary>Reads a boolean.</summary>
    public static bool ReadBoolean(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Expected("a boolean", e),
    };

    /// <summary>Reads a string of exactly one UTF-16 code unit.</summary>
    public static char ReadChar(JsonElement e)
    {
        var s = ReadString(e);
        return s.Length == 1 ? s[0] : throw new CodecException("expected a string of one character");
    }

    /// <summary>Reads an integer that fits a <see cref="byte"/>.</summary>
    public static byte ReadByte(JsonElement e) => Number(e).TryGetByte(out var v) ? v : throw OutOfRange("Byte", e);

    /// <summary>Reads an integer that fits an <see cref="sbyte"/>.</summary>
    public static sbyte ReadSByte(JsonElement e) => Number(e).TryGetSByte(out var v) ? v : throw OutOfRange("SByte", e);

    /// <summary>Reads an integer that fits a <see cref="short"/>.</summary>
    public static short ReadInt16(JsonElement e) => Number(e).TryGetInt16(out var v) ? v : throw OutOfRange("Int16", e);

    /// <summary>Reads an integer that fits a <see cref="ushort"/>.</summary>
    public static ushort ReadUInt16(JsonElement e) => Number(e).TryGetUInt16(out var v) ? v : throw OutOfRange("UInt16", e);

    /// <summary>Reads an integer that fits an <see cref="int"/>.</summary>
    public static int ReadInt32(JsonElement e) => Number(e).TryGetInt32(out var v) ? v : throw OutOfRange("Int32", e);

    /// <summary>Reads an integer that fits a <see cref="uint"/>.</summary>
    public static uint ReadUInt32(JsonElement e) => Number(e).TryGetUInt32(out var v) ? v : throw OutOfRange("UInt32", e);

    /// <summary>Reads an integer that fits a <see cref="long"/>.</summary>
    public static long ReadInt64(JsonElement e) => Number(e).TryGetInt64(out var v) ? v : throw OutOfRange("Int64", e);

    /// <summary>Reads an integer that fits a <see cref="ulong"/>.</summary>
    public static ulong ReadUInt64(JsonElement e) => Number(e).TryGetUInt64(out var v) ? v : throw OutOfRange("UInt64", e);

    /// <summary>Reads a number as a <see cref="float"/>.</summary>
    public static float ReadSingle(JsonElement e) => Number(e).GetSingle();

    /// <summary>Reads a number as a <see cref="double"/>.</summary>
    public static double ReadDouble(JsonElement e) => Number(e).GetDouble();

    /// <summary>Reads a number that fits a <see cref="decimal"/>.</summary>
    public static decimal ReadDecimal(JsonElement e) => Number(e).TryGetDecimal(out var v) ? v : throw OutOfRange("Decimal", e);

    /// <summary>Reads a GUID from its string form.</summary>
    public static Guid ReadGuid(JsonElement e) =>
        Guid.TryParse(ReadString(e), out var v) ? v : throw new CodecException("expected a GUID");

    /// <summary>Reads a date and time from an ISO 8601 string.</summary>
    public static DateTime ReadDateTime(JsonElement e) =>
        e.ValueKind == JsonValueKind.String && e.TryGetDateTime(out var v) ? v : throw Expected("an ISO 8601 date", e);

    /// <summary>Reads a date and time with its offset from an ISO 8601 string.</summary>
    public static DateTimeOffset ReadDateTimeOffset(JsonElement e) =>
        e.ValueKind == JsonValueKind.String && e.TryGetDateTimeOffset(out var v) ? v : throw Expected("an ISO 8601 date", e);

    /// <summary>Reads bytes from a base64 string.</summary>
    public static byte[] ReadBytes(JsonElement e) =>
        e.ValueKind == JsonValueKind.String && e.TryGetBytesFromBase64(out var v) ? v : throw Expected("a base64 string", e);

    /// <summary>Keeps any JSON value as it is, beyond the life of its document.</summary>
    public static JsonElement ReadElement(JsonElement e) => e.Clone();

    /// <summary>Writes a string.</summary>
    public static void Write(Utf8JsonWriter w, string value) => w.WriteStringValue(value);

    /// <summary>Writes a boolean.</summary>
    public static void Write(Utf8JsonWriter w, bool value) => w.WriteBooleanValue(value);

    /// <summary>Writes a character as a string of one.</summary>
    public static void Write(Utf8JsonWriter w, char value) => w.WriteStringValue(value.ToString());

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, byte value) => w.WriteNumberValue(value);

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, sbyte value) => w.WriteNumberValue(value);

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, short value) => w.WriteNumberValue(value);

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, ushort value) => w.WriteNumberValue(value);

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, int value) => w.WriteNumberValue(value);

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, uint value) => w.WriteNumberValue(value);

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, long value) => w.WriteNumberValue(value);

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, ulong value) => w.WriteNumberValue(value);

    /// <summary>Writes a number; NaN and infinities, which JSON lacks, as null.</summary>
    public static void Write(Utf8JsonWriter w, float value)
    {
        if (float.IsFinite(value))
            w.WriteNumberValue(value);
        else
            w.WriteNullValue();
    }

    /// <summary>Writes a number; NaN and infinities, which JSON lacks, as null.</summary>
    public static void Write(Utf8JsonWriter w, double value)
    {
        if (double.IsFinite(value))
            w.WriteNumberValue(value);
        else
            w.WriteNullValue();
    }

    /// <summary>Writes a number.</summary>
    public static void Write(Utf8JsonWriter w, decimal value) => w.WriteNumberValue(value);

    /// <summary>Writes a GUID as its string form.</summary>
    public static void Write(Utf8JsonWriter w, Guid value) => w.WriteStringValue(value);

    /// <summary>Writes a date and time as an ISO 8601 string.</summary>
    public static void Write(Utf8JsonWriter w, DateTime value) => w.WriteStringValue(value);

    /// <summary>Writes a date and time with its offset as an ISO 8601 string.</summary>
    public static void Write(Utf8JsonWriter w, DateTimeOffset value) => w.WriteStringValue(value);

    /// <summary>Writes bytes as a base64 string.</summary>
    public static void Write(Utf8JsonWriter w, byte[] value) => w.WriteBase64StringValue(value);

    /// <summary>Writes a JSON value as it is.</summary>
    public static void Write(Utf8JsonWriter w, JsonElement value) => value.WriteTo(w);

    /// <summary>Reads an array, each item with <paramref name="read"/>.</summary>
    public static T[] ReadArray<T>(JsonElement e, Func<JsonElement, T> read)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw Expected("an array", e);
        var items = new T[e.GetArrayLength()];
        var i = 0;
        foreach (var item in e.EnumerateArray())
        {
            items[i] = ReadItem(item, i, read);
            i++;
        }
        return items;
    }

    /// <summary>Reads an array into a list, each item with <paramref name="read"/>.</summary>
    public static List<T> ReadList<T>(JsonElement e, Func<JsonElement, T> read)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw Expected("an array", e);
        var items = new List<T>(e.GetArrayLength());
        foreach (var item in e.EnumerateArray())
            items.Add(ReadItem(item, items.Count, read));
        return items;
    }

    /// <summary>Reads an object into a dictionary, each value with <paramref name="read"/>.</summary>
    public static Dictionary<string, T> ReadDictionary<T>(JsonElement e, Func<JsonElement, T> read)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw Expected("an object", e);
        var items = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var property in e.EnumerateObject())
            items[property.Name] = ReadValue(property.Value, property.Name, read);
        return items;
    }

    /// <summary>
    /// Reads the property <paramref name="name"/> of an object when it is there,
    /// <see langword="undefined"/> counting as missing.
    /// </summary>
    public static bool TryReadProperty<T>(JsonElement obj, string name, Func<JsonElement, T> read, out T value)
    {
        if (obj.TryGetProperty(name, out var e) && e.ValueKind != JsonValueKind.Undefined)
        {
            value = ReadValue(e, name, read);
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Checks <paramref name="e"/> is an object, the JSON of a C# class or struct.</summary>
    public static void ExpectObject(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw Expected("an object", e);
    }

    /// <summary>The error for a property an object must have.</summary>
    public static CodecException Missing(string name) => new($"missing property '{name}'");

    /// <summary>Writes the items of a collection as an array.</summary>
    public static void WriteArray<T>(Utf8JsonWriter w, IEnumerable<T> items, Action<Utf8JsonWriter, T> write)
    {
        w.WriteStartArray();
        foreach (var item in items)
            write(w, item);
        w.WriteEndArray();
    }

    /// <summary>Writes a dictionary with string keys as an object.</summary>
    public static void WriteDictionary<T>(Utf8JsonWriter w, IEnumerable<KeyValuePair<string, T>> items, Action<Utf8JsonWriter, T> write)
    {
        w.WriteStartObject();
        foreach (var item in items)
        {
            w.WritePropertyName(item.Key);
            write(w, item.Value);
        }
        w.WriteEndObject();
    }

    private static T ReadItem<T>(JsonElement e, int index, Func<JsonElement, T> read)
    {
        try
        {
            return read(e);
        }
        catch (CodecException err)
        {
            throw err.Under(Index(index));
        }
    }

    private static T ReadValue<T>(JsonElement e, string name, Func<JsonElement, T> read)
    {
        try
        {
            return read(e);
        }
        catch (CodecException err)
        {
            throw err.Under(IsIdentifier(name) ? "." + name : "[" + JsonSerializer.Serialize(name, JsonContext.Default.String) + "]");
        }
    }

    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]))
            return false;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '$')
                return false;
        }
        return true;
    }

    /// <summary>Whether <paramref name="e"/> is JSON null, or missing (<c>undefined</c> in the page).</summary>
    public static bool IsNull(JsonElement e) => e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    /// <summary>The error for a value of the wrong kind.</summary>
    public static CodecException Expected(string what, JsonElement e) =>
        new($"expected {what}, got {Describe(e)}");

    /// <summary>The path segment of an array index: <c>[2]</c>.</summary>
    public static string Index(int index) => "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    private static JsonElement Number(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e : throw Expected("a number", e);

    private static CodecException OutOfRange(string type, JsonElement e) =>
        new($"{e.GetRawText()} does not fit {type}");

    private static string Describe(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "nothing",
    };
}
