using System.ComponentModel;

namespace Oriel.Generated;

/// <summary>Delivers event messages to the pages of an app.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEventSink
{
    /// <summary>
    /// Sends <paramref name="message"/>, a whole protocol message, to every
    /// page. Called from any thread.
    /// </summary>
    void Broadcast(string message);
}

/// <summary>A sink that keeps only the latest message of a coalesced event per page.</summary>
internal interface ICoalescingSink : IEventSink
{
    /// <summary>
    /// Sends <paramref name="message"/>, of the coalesced event
    /// <paramref name="name"/>, to every page, in place of the one of that
    /// event still waiting there. Called from any thread.
    /// </summary>
    void BroadcastLatest(string name, string message);
}
