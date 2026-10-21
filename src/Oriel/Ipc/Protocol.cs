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

    /// <summary>A call returned: <c>{"t":"result","id":1,"k":token,"v":value}</c>, without <c>v</c> for nothing.</summary>
    public const string Result = "result";

    /// <summary>A call failed: <c>{"t":"error","id":1,"k":token,"e":message}</c>.</summary>
    public const string Error = "error";

    /// <summary>The app emits an event: <c>{"t":"event","n":"Clock.Tick","v":value}</c>.</summary>
    public const string Event = "event";

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

    public static string ErrorMessage(long id, string token, string message) =>
        Write(w =>
        {
            w.WriteString("t", Error);
            w.WriteNumber("id", id);
            w.WriteString("k", token);
            w.WriteString("e", message);
        });

    public static string EventMessage(string name, ValueWriter value) =>
        Write(w =>
        {
            w.WriteString("t", Event);
            w.WriteString("n", name);
            w.WritePropertyName("v");
            value(w);
        });

    private static string Write(Action<Utf8JsonWriter> properties)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            properties(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
