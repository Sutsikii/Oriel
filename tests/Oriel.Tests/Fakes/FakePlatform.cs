using System.Collections.Concurrent;
using Oriel.Platform;

namespace Oriel.Tests.Fakes;

/// <summary>A backend without windows: its event loop runs posted actions, and its windows record what they get.</summary>
internal sealed class FakePlatform : IPlatform
{
    private readonly BlockingCollection<Action> _queue = new();
    private volatile bool _quit;

    public ConcurrentQueue<FakeWindow> Windows { get; } = new();

    public int? UiThread { get; private set; }

    public void Run(Action started)
    {
        UiThread = Environment.CurrentManagedThreadId;
        started();
        while (!_quit)
        {
            if (_queue.TryTake(out var action, TimeSpan.FromSeconds(10)))
                action();
            else
                throw new TimeoutException("the app did not quit");
        }
    }

    public void Post(Action action) => _queue.Add(action);

    public void Quit()
    {
        _quit = true;
        _queue.Add(() => { });
    }

    public IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        var window = new FakeWindow(options, events);
        Windows.Enqueue(window);
        return window;
    }
}

internal sealed class FakeWindow(PlatformWindowOptions options, IPlatformWindowEvents events) : IPlatformWindow
{
    public PlatformWindowOptions Options { get; } = options;

    public IPlatformWindowEvents Events { get; } = events;

    public ConcurrentQueue<string> Posted { get; } = new();

    public string Title { get; private set; } = options.Title;

    public void PostMessage(string message) => Posted.Enqueue(message);

    public void SetTitle(string title) => Title = title;

    public void OpenDevTools()
    {
    }

    public void Close() => Events.Closed();
}
