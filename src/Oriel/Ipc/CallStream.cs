using Oriel.Generated;

namespace Oriel.Ipc;

/// <summary>
/// A call of the page that iterates a stream: the items the page is ready
/// for, and the cancellation that stops the stream when the page stops
/// iterating or goes away.
/// </summary>
/// <remarks>
/// The page grants items in advance (its window) and grants more as it
/// takes them, so the stream never gets further ahead of the page than the
/// window: a slow page slows the producer.
/// </remarks>
internal sealed class CallStream
{
    private readonly SemaphoreSlim _credit;
    private readonly CancellationTokenSource _cancellation;
    private readonly Lock _lock = new();
    private bool _canceling;
    private bool _disposed;

    /// <param name="window">The items the page is ready for at first.</param>
    /// <param name="page">Canceled when the page goes away.</param>
    public CallStream(int window, CancellationToken page)
    {
        _credit = new SemaphoreSlim(window);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(page);
        Cancellation = _cancellation.Token;
    }

    /// <summary>Canceled when the page stops iterating, or goes away.</summary>
    public CancellationToken Cancellation { get; }

    /// <summary>Completes once the page is ready for one more item, which it takes.</summary>
    public Task WaitAsync() => _credit.WaitAsync(Cancellation);

    /// <summary>The page is ready for <paramref name="items"/> more.</summary>
    public void Grant(int items)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            try
            {
                _credit.Release(items);
            }
            catch (SemaphoreFullException)
            {
                // More than int.MaxValue items granted: plenty already.
            }
        }
    }

    /// <summary>The page stopped iterating.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            if (_disposed || _canceling)
                return;
            _canceling = true;
        }
        // Outside the lock: the stream's own code runs in the callbacks, and
        // may end the call, which disposes the stream, on this thread.
        _cancellation.Cancel();
        lock (_lock)
        {
            _canceling = false;
            if (_disposed)
                Release();
        }
    }

    /// <summary>The call ended.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!_canceling)
                Release();
        }
    }

    private void Release()
    {
        _cancellation.Dispose();
        _credit.Dispose();
    }
}

/// <summary>Writes the items of a stream, each once the page is ready for it.</summary>
/// <param name="ready">Completes once the page is ready for one more item.</param>
/// <param name="send">Sends an item to the page.</param>
/// <param name="cancellation">Canceled when the page stops iterating, or goes away.</param>
internal delegate Task StreamPump(Func<Task> ready, Action<ValueWriter> send, CancellationToken cancellation);
