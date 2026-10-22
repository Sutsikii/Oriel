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

    public ConcurrentQueue<Uri> OpenedExternally { get; } = new();

    public void OpenExternally(Uri uri) => OpenedExternally.Enqueue(uri);

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

    public int Reloads { get; private set; }

    public int Recreations { get; private set; }

    public string? ErrorPage { get; private set; }

    public void Reload() => Reloads++;

    public void Recreate() => Recreations++;

    public void ShowErrorPage(string html) => ErrorPage = html;

    /// <summary>The dialogs shown, in order.</summary>
    public ConcurrentQueue<object> Dialogs { get; } = new();

    /// <summary>What the next file dialogs answer; canceled by default.</summary>
    public Func<FileDialogRequest, IReadOnlyList<string>?> FileDialogAnswer { get; set; } = _ => null;

    /// <summary>What the next message boxes answer.</summary>
    public MessageResult MessageAnswer { get; set; } = MessageResult.Ok;

    /// <summary>The thread the last dialog showed on.</summary>
    public int? DialogThread { get; private set; }

    public IReadOnlyList<string>? ShowFileDialog(FileDialogRequest request)
    {
        DialogThread = Environment.CurrentManagedThreadId;
        Dialogs.Enqueue(request);
        return FileDialogAnswer(request);
    }

    public MessageResult ShowMessage(MessageRequest request)
    {
        DialogThread = Environment.CurrentManagedThreadId;
        Dialogs.Enqueue(request);
        return MessageAnswer;
    }
}
