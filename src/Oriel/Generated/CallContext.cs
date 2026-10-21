using System.ComponentModel;

namespace Oriel.Generated;

/// <summary>
/// What a call knows besides its arguments. Generated dispatchers hand its
/// members to the parameters of a service method that ask for them.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CallContext
{
    internal CallContext(CancellationToken cancellation)
    {
        Cancellation = cancellation;
    }

    /// <summary>
    /// Canceled when the page that made the call goes away: it navigated,
    /// reloaded or its window closed.
    /// </summary>
    public CancellationToken Cancellation { get; }
}
