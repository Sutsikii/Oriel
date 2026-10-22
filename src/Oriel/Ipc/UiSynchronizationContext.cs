namespace Oriel.Ipc;

/// <summary>
/// Posts the continuations of a <see cref="ServiceConcurrency.UiThread"/>
/// service's calls back to the UI thread, so that code after an
/// <c>await</c> runs there too.
/// </summary>
internal sealed class UiSynchronizationContext(Action<Action> dispatch) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => dispatch(() => d(state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Calls cannot wait for the UI thread: they run on it.");

    public override SynchronizationContext CreateCopy() => this;
}
