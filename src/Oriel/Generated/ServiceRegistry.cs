using System.Collections.Concurrent;
using System.ComponentModel;

namespace Oriel.Generated;

/// <summary>
/// The dispatchers the source generator wrote, by service type. Generated
/// module initializers fill it as their assembly loads, so binding a service
/// needs no reflection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ServiceRegistry
{
    private static readonly ConcurrentDictionary<Type, Func<object, ServiceDispatcher>> s_factories = new();

    /// <summary>Records how to make the dispatcher of services of type <typeparamref name="T"/>.</summary>
    public static void Register<T>(Func<T, ServiceDispatcher> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        s_factories[typeof(T)] = service => factory((T)service);
    }

    /// <summary>Makes the dispatcher of <paramref name="service"/>, by its exact type.</summary>
    internal static bool TryCreate(object service, out ServiceDispatcher dispatcher)
    {
        if (s_factories.TryGetValue(service.GetType(), out var factory))
        {
            dispatcher = factory(service);
            return true;
        }
        dispatcher = null!;
        return false;
    }
}
