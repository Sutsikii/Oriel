using Oriel;

namespace TasksApp;

/// <summary>Something to do.</summary>
public sealed record TaskItem(int Id, string Title, bool Done);

/// <summary>A title that cannot be a task's.</summary>
[ServiceError("invalid-title")]
public sealed class InvalidTitleException() : Exception("A task needs a title of 1 to 120 characters.");

/// <summary>A list of tasks, in memory: every window of the app sees the same one.</summary>
[Service(Concurrency = ServiceConcurrency.Serial)]
public sealed class Tasks
{
    private readonly List<TaskItem> _items = [new(1, "Try Oriel with React", false)];
    private int _lastId = 1;

    /// <summary>Fires with the number of tasks left, after each change, so that every window refreshes.</summary>
    public Event<int> Changed { get; } = new();

    /// <summary>The tasks, in the order they were added.</summary>
    public TaskItem[] All() => [.. _items];

    /// <summary>Adds a task titled <paramref name="title"/>.</summary>
    /// <exception cref="InvalidTitleException">The title is empty or too long.</exception>
    public TaskItem Add(string title)
    {
        title = title.Trim();
        if (title.Length is 0 or > 120)
            throw new InvalidTitleException();
        var task = new TaskItem(++_lastId, title, false);
        _items.Add(task);
        Changed.Emit(Left());
        return task;
    }

    /// <summary>Marks the task <paramref name="id"/> done, or not done.</summary>
    public void Toggle(int id)
    {
        var i = _items.FindIndex(t => t.Id == id);
        if (i < 0)
            return;
        _items[i] = _items[i] with { Done = !_items[i].Done };
        Changed.Emit(Left());
    }

    /// <summary>Removes the tasks done.</summary>
    public void Clear()
    {
        _items.RemoveAll(t => t.Done);
        Changed.Emit(Left());
    }

    private int Left() => _items.Count(t => !t.Done);
}
