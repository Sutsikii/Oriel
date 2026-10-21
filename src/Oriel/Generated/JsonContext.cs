using System.Text.Json.Serialization;

namespace Oriel.Generated;

/// <summary>The few types Oriel itself serializes with System.Text.Json, without reflection.</summary>
[JsonSerializable(typeof(string))]
internal sealed partial class JsonContext : JsonSerializerContext
{
}
