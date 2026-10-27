using System.Buffers;
using System.Text;
using System.Text.Json;
using Oriel.Generated;

namespace Oriel.Ipc;

/// <summary>
/// The messages pages and the app exchange, as JSON strings. See
/// docs/protocol.md for the full description; the page side is bridge.js.
/// </summary>
internal static class Protocol
{
    /// <summary>A page started: <c>{"t":"hello","k":token}</c>.</summary>
    public const string Hello = "hello";

    /// <summary>A page calls a method: <c>{"t":"call","id":1,"k":token,"m":"Greeter.Greet","a":[...]}</c>.</summary>
    public const string Call = "call";

    /// <summary>
    /// The page left a key alone that may be a menu's accelerator:
    /// <c>{"t":"key","k":token,"c":keyCode,"m":modifiers}</c>, the modifiers Ctrl 1, Shift 2, Alt 4.
    /// </summary>
    public const string Key = "key";

    /// <summary>
    /// Files were dropped on the page: <c>{"t":"drop","k":token,"x":clientX,"y":clientY}</c>,
    /// sent with the dropped <c>File</c> objects, whose paths the backend reads.
    /// </summary>
    public const string Drop = "drop";

    /// <summary>
    /// The page took the coalesced events <c>n</c> and is ready for their
    /// next payloads: <c>{"t":"ack","k":token,"n":["Pointer.Moved"]}</c>,
    /// sent once per frame at most.
    /// </summary>
    public const string Ack = "ack";

    /// <summary>
    /// The page is ready for <c>n</c> more items of the stream of call <c>id</c>:
    /// <c>{"t":"pull","id":1,"k":token,"n":8}</c>.
    /// </summary>
    public const string Pull = "pull";

    /// <summary>
    /// The page stopped iterating the stream of call <c>id</c>:
    /// <c>{"t":"cancel","id":1,"k":token}</c>.
    /// </summary>
    public const string Cancel = "cancel";

    /// <summary>
    /// An item of the stream of call <c>id</c>: <c>{"t":"item","id":1,"k":token,"v":value}</c>.
    /// A <see cref="Result"/> ends the stream, an <see cref="Error"/> fails it.
    /// </summary>
    public const string Item = "item";

    /// <summary>A call returned: <c>{"t":"result","id":1,"k":token,"v":value}</c>, without <c>v</c> for nothing.</summary>
    public const string Result = "result";

    /// <summary>
    /// A call failed: <c>{"t":"error","id":1,"k":token,"e":message}</c>, with the
    /// code <c>"c"</c> and data <c>"d"</c> of a <see cref="ServiceErrorAttribute"/> exception.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// The app emits an event: <c>{"t":"event","n":"Clock.Tick","v":value}</c>,
    /// with <c>"c":1</c> when it is coalesced and the page acknowledges it with an <see cref="Ack"/>.
    /// </summary>
    public const string Event = "event";

    /// <summary>
    /// The event of a window's page for files dropped on it:
    /// <c>{"paths":[...],"x":clientX,"y":clientY}</c>. No service has this
    /// name, since a C# name has no colon.
    /// </summary>
    public const string FilesDroppedEvent = "oriel:filesDropped";

    public static string ResultMessage(long id, string token, ValueWriter? value) =>
        Write(w =>
        {
            w.WriteString("t", Result);
            w.WriteNumber("id", id);
            w.WriteString("k", token);
            if (value is not null)
            {
                w.WritePropertyName("v");
                value(w);
            }
        });

    public static string ItemMessage(long id, string token, ValueWriter value) =>
        Write(w =>
        {
            w.WriteString("t", Item);
            w.WriteNumber("id", id);
            w.WriteString("k", token);
            w.WritePropertyName("v");
            value(w);
        });

    public static string ErrorMessage(long id, string token, string message, string? code = null, ValueWriter? data = null) =>
        Write(w =>
        {
            w.WriteString("t", Error);
            w.WriteNumber("id", id);
            w.WriteString("k", token);
            w.WriteString("e", message);
            if (code is not null)
            {
                w.WriteString("c", code);
                w.WritePropertyName("d");
                if (data is null)
                    w.WriteNullValue();
                else
                    data(w);
            }
        });

    public static string EventMessage(string name, ValueWriter value, bool coalesced = false) =>
        Write(w =>
        {
            w.WriteString("t", Event);
            w.WriteString("n", name);
            w.WritePropertyName("v");
            value(w);
            if (coalesced)
                w.WriteNumber("c", 1);
        });

    /// <summary>
    /// How messages are written. Messages reach pages through postMessage,
    /// never through HTML, so only what JSON itself requires is escaped.
    /// </summary>
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Write(Action<Utf8JsonWriter> properties)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            properties(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
