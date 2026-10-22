using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;

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
    private static readonly ConcurrentDictionary<Type, (string Code, Action<Utf8JsonWriter, Exception> Write)> s_errors = new();

    /// <summary>Records how to make the dispatcher of services of type <typeparamref name="T"/>.</summary>
    public static void Register<T>(Func<T, ServiceDispatcher> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        s_factories[typeof(T)] = service => factory((T)service);
    }

    /// <summary>
    /// Records that exceptions of type <typeparamref name="T"/> reach pages as
    /// errors of <paramref name="code"/>, with the data <paramref name="write"/> writes.
    /// </summary>
    public static void RegisterError<T>(string code, Action<Utf8JsonWriter, T> write)
        where T : Exception
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(write);
        s_errors[typeof(T)] = (code, (writer, e) => write(writer, (T)e));
    }

    /// <summary>
    /// The code and data of <paramref name="error"/>, when its type or a base
    /// type of it is a <see cref="ServiceErrorAttribute"/> exception.
    /// </summary>
    internal static bool TryGetError(Exception error, out string code, out ValueWriter data)
    {
        for (var type = error.GetType(); type is not null && type != typeof(Exception); type = type.BaseType)
        {
            if (s_errors.TryGetValue(type, out var registered))
            {
                code = registered.Code;
                var write = registered.Write;
                data = writer => write(writer, error);
                return true;
            }
        }
        code = "";
        data = null!;
        return false;
    }

    /// <summary>
    /// Makes the dispatcher of services of <paramref name="type"/>, for
    /// <paramref name="service"/>; null makes one that only tells the
    /// service's name and concurrency, as the generated constructors just
    /// keep the instance.
    /// </summary>
    internal static ServiceDispatcher Create(Type type, object? service) =>
        s_factories.TryGetValue(type, out var factory)
            ? factory(service!)
            : throw new InvalidOperationException(
                $"{type.FullName} cannot be bound: it is not a [Service] class, or its project does not run the Oriel source generator.");

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
