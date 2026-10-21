using System.ComponentModel;

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
}
