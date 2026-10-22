using System.Buffers;
using System.Text.Json;
using Oriel.Generated;
using Oriel.Ipc;

namespace Oriel;

/// <summary>
/// An instance of a service for one call, and what releases it once the
/// call is done: see <see cref="OrielApp.Bind{T}(Func{OrielWindow?, ServiceLease{T}})"/>.
/// </summary>
/// <param name="service">The instance that runs the call.</param>
/// <param name="release">
/// Disposed once the call returned and its result was written, such as the
/// scope of a dependency injection container; null when there is nothing to release.
/// </param>
public readonly struct ServiceLease<T>(T service, IAsyncDisposable? release = null)
    where T : class
{
    /// <summary>The instance that runs the call.</summary>
    public T Service { get; } = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>What releases the instance once the call is done, or null.</summary>
    public IAsyncDisposable? Release { get; } = release;
}

/// <summary>
/// The dispatcher of a service leased for each call: it gets an instance,
/// calls it through the generated dispatcher, writes the result, and only
/// then releases it, so that results may use what the release disposes.
/// </summary>
internal sealed class LeasedDispatcher<T>(ServiceDispatcher prototype, Func<OrielWindow?, ServiceLease<T>> lease)
    : ServiceDispatcher(prototype.Name, prototype.Concurrency)
    where T : class
{
    public override async ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
    {
        var leased = lease(context.Window);
        try
        {
            if (leased.Service is null)
                throw new InvalidOperationException($"The lease of {typeof(T).Name} gave no instance: a default ServiceLease has none.");
            var dispatcher = ServiceRegistry.Create(typeof(T), leased.Service);
            var writer = await dispatcher.InvokeAsync(method, args, context).ConfigureAwait(false);
            if (writer is null)
                return null;
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer, Protocol.WriterOptions))
                writer(w);
            var json = buffer.WrittenMemory;
            return w => w.WriteRawValue(json.Span, skipInputValidation: true);
        }
        finally
        {
            if (leased.Release is { } release)
                await release.DisposeAsync().ConfigureAwait(false);
        }
    }
}
