using System.ComponentModel;
using System.Text.Json;

namespace Oriel.Generated;

/// <summary>
/// Calls the methods of one bound service by name, with arguments read from
/// JSON. The source generator writes one subclass per <see cref="ServiceAttribute"/>
/// class; nothing in Oriel calls a service through reflection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ServiceDispatcher
{
    /// <summary>Creates a dispatcher for the service called <paramref name="name"/>.</summary>
    /// <param name="name">The name pages call the service by.</param>
    protected ServiceDispatcher(string name)
    {
        Name = name;
    }

    /// <summary>The name pages call the service by, as in <c>Greeter.Greet</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Calls <paramref name="method"/> with the JSON array <paramref name="args"/>.
    /// </summary>
    /// <returns>
    /// Writes the method's result, or <see langword="null"/> when it returns nothing.
    /// </returns>
    /// <exception cref="MethodNotFoundException">The service has no such method.</exception>
    /// <exception cref="CodecException">An argument is not of the method's type.</exception>
    public abstract ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context);
}
