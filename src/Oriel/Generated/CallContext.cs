using System.ComponentModel;
using System.Text.Json;
using Oriel.Ipc;

namespace Oriel.Generated;

/// <summary>
/// What a call knows besides its arguments. Generated dispatchers hand its
/// members to the parameters of a service method that ask for them.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CallContext
{
    internal CallContext(CancellationToken cancellation, OrielWindow? window = null)
    {
        Cancellation = cancellation;
        Window = window;
    }

    /// <summary>
    /// Canceled when the page that made the call goes away: it navigated,
    /// reloaded or its window closed.
    /// </summary>
    public CancellationToken Cancellation { get; }

    /// <summary>The window of the page that made the call.</summary>
    public OrielWindow? Window { get; }

    /// <summary>The binary data the method returned, which the call sends as bytes.</summary>
    internal BinaryData? Binary { get; private set; }

    /// <summary>
    /// Makes <paramref name="data"/> the result of the call: bytes the page
    /// gets as a typed array, <paramref name="typedArray"/>, rather than JSON.
    /// Generated code calls it for methods returning <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    /// <param name="data">The numbers, which must stay as they are until the page has them.</param>
    /// <param name="typedArray">The page's type: <c>Float32Array</c> for <see cref="float"/>.</param>
    /// <returns>Nothing to write: the bytes go apart.</returns>
    public ValueWriter? Return<T>(ReadOnlyMemory<T> data, string typedArray) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(typedArray);
        Binary = new BinaryData(typedArray, () => System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.Span));
        return null;
    }

    /// <summary>The stream the method returned, which the call sends once it returns.</summary>
    internal StreamPump? Stream { get; private set; }

    /// <summary>
    /// Makes <paramref name="items"/> the result of the call: a stream the
    /// page iterates, each item written by <paramref name="write"/>. The
    /// page pulls items as it takes them, so the stream runs no further ahead
    /// than the page lets it. Generated code calls it for methods returning
    /// <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <returns>Nothing to write now: the items follow.</returns>
    public ValueWriter? Return<T>(IAsyncEnumerable<T> items, Action<Utf8JsonWriter, T> write)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(write);
        Stream = async (ready, send, cancellation) =>
        {
            // No ConfigureAwait(false): the stream of a UiThread service
            // stays on the UI thread.
            var e = items.GetAsyncEnumerator(cancellation);
            await using (e)
            {
                while (true)
                {
                    await ready();
                    if (!await e.MoveNextAsync())
                        return;
                    var item = e.Current;
                    send(w => write(w, item));
                }
            }
        };
        return null;
    }
}
