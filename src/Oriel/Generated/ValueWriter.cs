using System.Text.Json;

namespace Oriel.Generated;

/// <summary>
/// Writes one JSON value: the result of a call or the payload of an event.
/// </summary>
/// <param name="writer">The writer the value goes to.</param>
public delegate void ValueWriter(Utf8JsonWriter writer);
