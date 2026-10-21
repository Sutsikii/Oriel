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
