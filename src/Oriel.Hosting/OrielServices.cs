using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Oriel.Hosting;

/// <summary>How long an instance of a service pages call lives: see <see cref="OrielServiceCollectionExtensions.AddOrielService{T}"/>.</summary>
public enum OrielServiceScope
{
    /// <summary>One instance for the app, the only kind whose events reach pages.</summary>
    Singleton,

    /// <summary>One instance per window, in a scope disposed when the window closes.</summary>
    Window,

    /// <summary>One instance per call, in a scope disposed once the call is done: for EF Core's DbContext and the like.</summary>
    Call,
}

/// <summary>Adds services pages call to a <see cref="IServiceCollection"/>.</summary>
public static class OrielServiceCollectionExtensions
{
    /// <summary>
    /// Adds <typeparamref name="T"/>, a <see cref="ServiceAttribute"/> class,
    /// to the container, and binds it for pages once the app is built. Its
    /// constructor gets what it needs from the container, unless
    /// <typeparamref name="T"/> was added before, with a factory for instance:
    /// that registration stays.
    /// </summary>
    /// <param name="services">The container's services.</param>
    /// <param name="scope">How long an instance lives. Defaults to one for the app.</param>
    public static IServiceCollection AddOrielService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IServiceCollection services, OrielServiceScope scope = OrielServiceScope.Singleton)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        if (scope == OrielServiceScope.Singleton)
            services.TryAddSingleton<T>();
        else
            services.TryAddScoped<T>();
        services.AddSingleton(new OrielServiceRegistration((app, provider, windows) =>
        {
            switch (scope)
            {
                case OrielServiceScope.Singleton:
                    app.Bind(provider.GetRequiredService<T>());
                    break;
                case OrielServiceScope.Window:
                    app.Bind<T>(window => window is null
                        ? InScopeOfItsOwn<T>(provider)
                        : new ServiceLease<T>(windows.For(window).ServiceProvider.GetRequiredService<T>()));
                    break;
                default:
                    app.Bind<T>(_ => InScopeOfItsOwn<T>(provider));
                    break;
            }
        }));
        return services;
    }

    private static ServiceLease<T> InScopeOfItsOwn<T>(IServiceProvider provider)
        where T : class
    {
        var scope = provider.CreateAsyncScope();
        try
        {
            return new ServiceLease<T>(scope.ServiceProvider.GetRequiredService<T>(), scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}

/// <summary>A service added with <see cref="OrielServiceCollectionExtensions.AddOrielService{T}"/>, bound as the app is built.</summary>
internal sealed class OrielServiceRegistration(Action<OrielApp, IServiceProvider, WindowScopes> bind)
{
    public void Bind(OrielApp app, IServiceProvider provider, WindowScopes windows) => bind(app, provider, windows);
}

/// <summary>The scope of each window's <see cref="OrielServiceScope.Window"/> services, disposed as the window closes.</summary>
internal sealed class WindowScopes(IServiceProvider provider, ILogger logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<OrielWindow, Lazy<AsyncServiceScope>> _scopes = new();

    public AsyncServiceScope For(OrielWindow window)
    {
        var created = false;
        var scope = _scopes.GetOrAdd(window, _ => new Lazy<AsyncServiceScope>(() =>
        {
            created = true;
            return provider.CreateAsyncScope();
        })).Value;
        if (created)
            window.Closed += (_, _) => _ = Close(window);
        return scope;
    }

    private async Task Close(OrielWindow window)
    {
        if (!_scopes.TryRemove(window, out var scope) || !scope.IsValueCreated)
            return;
        try
        {
            await scope.Value.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Disposing the services of the window \"{Title}\" failed", window.Title);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var window in _scopes.Keys)
            await Close(window).ConfigureAwait(false);
    }
}
