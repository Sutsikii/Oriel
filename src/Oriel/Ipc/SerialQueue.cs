namespace Oriel.Ipc;

/// <summary>
/// Runs work one at a time, in the order it was queued. Unlike a semaphore,
/// which lets waiters in any order, each piece of work waits for the one
/// queued before it.
/// </summary>
internal sealed class SerialQueue
{
    private readonly Lock _lock = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>
    /// Takes a turn now, in the caller's thread, so that the order is the
    /// order of the calls to <see cref="Enter"/>. The returned task completes
    /// when the turn comes; dispose the turn once the work is done.
    /// </summary>
    public Task<IDisposable> Enter()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;
        lock (_lock)
        {
            previous = _tail;
            _tail = done.Task;
        }
        return previous.ContinueWith<IDisposable>(_ => new Turn(done), TaskScheduler.Default);
    }

    private sealed class Turn(TaskCompletionSource done) : IDisposable
    {
        public void Dispose() => done.TrySetResult();
    }
}
