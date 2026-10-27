namespace Oriel.Ipc;

/// <summary>The bytes of a binary result.</summary>
internal delegate ReadOnlySpan<byte> Bytes();

/// <summary>
/// A binary result: bytes the page gets as a typed array. The window sends
/// them apart from the JSON reply, in a buffer it shares with the page, or
/// at a URL the page fetches once.
/// </summary>
/// <param name="TypedArray">The page's type of array: <c>Float32Array</c>.</param>
/// <param name="Bytes">Reads the bytes, which stay alive with the result.</param>
internal sealed record BinaryData(string TypedArray, Bytes Bytes);
