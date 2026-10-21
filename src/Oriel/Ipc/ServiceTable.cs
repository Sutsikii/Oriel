using Oriel.Generated;

namespace Oriel.Ipc;

/// <summary>The services an app bound, by the name pages call them by.</summary>
internal sealed class ServiceTable
{
    private readonly Dictionary<string, ServiceDispatcher> _services = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public void Add(ServiceDispatcher dispatcher)
    {
        lock (_lock)
        {
            if (!_services.TryAdd(dispatcher.Name, dispatcher))
                throw new InvalidOperationException($"A service named {dispatcher.Name} is already bound.");
        }
    }

    public bool TryGet(string name, out ServiceDispatcher dispatcher)
    {
        lock (_lock)
            return _services.TryGetValue(name, out dispatcher!);
    }
}
