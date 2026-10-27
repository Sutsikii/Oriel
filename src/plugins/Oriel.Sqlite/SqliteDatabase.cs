using System.Buffers;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Oriel.Sqlite;

/// <summary>
/// A SQLite database of the app, which pages query as <c>Sqlite</c>: SQL
/// with named parameters, rows as objects of their columns. C# code can use
/// <see cref="Connection"/> as it would any SQLite connection.
/// </summary>
/// <remarks>
/// Calls run one at a time, in the order they came, so that a page's
/// statements see each other's writes. Pages run what SQL they send: the
/// app's own pages only can, but keep data pages should not change in
/// another database.
/// </remarks>
/// <example>
/// <code>
/// app.Bind(new SqliteDatabase(Path.Combine(app.DataDirectory, "notes.db")));
/// </code>
/// <code>
/// await Sqlite.execute("insert into notes (title) values (@title)", { title: "Groceries" });
/// const notes = await Sqlite.query("select id, title from notes where title like @q", { q: "%gro%" });
/// </code>
/// </example>
[Service(Name = "Sqlite", Concurrency = ServiceConcurrency.Serial)]
public sealed class SqliteDatabase : IDisposable
{
    /// <summary>
    /// Opens the database file at <paramref name="path"/>, creating it and
    /// its folder when there is none; <c>:memory:</c> is a database in
    /// memory, gone with the app.
    /// </summary>
    public SqliteDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var source = path;
        if (path != ":memory:")
        {
            source = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        }
        Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        Connection.Open();
    }

    /// <summary>The connection, for C# code; the calls of pages go through it too.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>
    /// Runs <paramref name="sql"/>, such as <c>select * from notes where id = @id</c>,
    /// with the values of <paramref name="parameters"/>, an object such as
    /// <c>{ id: 4 }</c>, and returns its rows as objects of their columns.
    /// Integers and reals are numbers, text strings, blobs base64 strings,
    /// and NULL null.
    /// </summary>
    public JsonElement[] Query(string sql, JsonElement? parameters = null)
    {
        using var command = Command(sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<JsonElement>();
        var buffer = new ArrayBufferWriter<byte>();
        while (reader.Read())
        {
            buffer.ResetWrittenCount();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    w.WritePropertyName(reader.GetName(i));
                    switch (reader.GetValue(i))
                    {
                        case DBNull:
                            w.WriteNullValue();
                            break;
                        case long n:
                            w.WriteNumberValue(n);
                            break;
                        case double d when double.IsFinite(d):
                            w.WriteNumberValue(d);
                            break;
                        case double:
                            w.WriteNullValue(); // JSON has no NaN or infinity
                            break;
                        case byte[] bytes:
                            w.WriteBase64StringValue(bytes);
                            break;
                        case var other:
                            w.WriteStringValue(Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture));
                            break;
                    }
                }
                w.WriteEndObject();
            }
            using var row = JsonDocument.Parse(buffer.WrittenMemory);
            rows.Add(row.RootElement.Clone());
        }
        return [.. rows];
    }

    /// <summary>
    /// Runs <paramref name="sql"/>, such as <c>insert</c>, <c>update</c> or
    /// <c>create table</c>, with the values of <paramref name="parameters"/>.
    /// </summary>
    /// <returns>How many rows it changed.</returns>
    public int Execute(string sql, JsonElement? parameters = null)
    {
        using var command = Command(sql, parameters);
        return command.ExecuteNonQuery();
    }

    /// <summary>Closes the database. Not for pages.</summary>
    [ServiceIgnore]
    public void Dispose() => Connection.Dispose();

    /// <summary>
    /// The command of <paramref name="sql"/>; each property of
    /// <paramref name="parameters"/> is the parameter of its name, with
    /// <c>@</c> before it unless it starts with <c>@</c>, <c>$</c> or <c>:</c>.
    /// </summary>
    private SqliteCommand Command(string sql, JsonElement? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        if (parameters is not { } values || values.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return command;
        if (values.ValueKind != JsonValueKind.Object)
        {
            command.Dispose();
            throw new ArgumentException("Parameters are an object of their names and values, such as { id: 4 }.", nameof(parameters));
        }
        foreach (var property in values.EnumerateObject())
        {
            var name = property.Name.Length > 0 && property.Name[0] is '@' or '$' or ':' ? property.Name : "@" + property.Name;
            command.Parameters.AddWithValue(name, Value(property.Value, property.Name));
        }
        return command;
    }

    private static object Value(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.Null => DBNull.Value,
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when value.TryGetInt64(out var n) => n,
        JsonValueKind.Number => value.GetDouble(),
        _ => throw new ArgumentException($"Parameter '{name}' is an {value.ValueKind}: SQLite takes numbers, strings, booleans and null.", nameof(value)),
    };
}
