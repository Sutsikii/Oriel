namespace Oriel;

/// <summary>How the calls of a service run: see <see cref="ServiceAttribute.Concurrency"/>.</summary>
public enum ServiceConcurrency
{
    /// <summary>Calls run on the thread pool, several at once.</summary>
    Parallel,

    /// <summary>
    /// One call at a time, in the order they arrived, from every window. For
    /// services with state, which then need no locks.
    /// </summary>
    Serial,

    /// <summary>
    /// Calls run on the UI thread, as do their continuations after an
    /// <c>await</c>. For services that drive windows.
    /// </summary>
    UiThread,
}
