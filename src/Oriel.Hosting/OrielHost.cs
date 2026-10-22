using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oriel.Platform;

namespace Oriel.Hosting;

/// <summary>Starts an Oriel app on the .NET Generic Host: <c>OrielApp.CreateBuilder(args)</c>.</summary>
public static class OrielAppHosting
{
    extension(OrielApp)
    {
        /// <summary>
        /// Starts building an app on the .NET Generic Host, with its
        /// configuration (appsettings.json, environment variables, the command
        /// line), logging and dependency injection.
        /// </summary>
        /// <example>
        /// <code>
        /// var builder = OrielApp.CreateBuilder(args);
        /// builder.Services.AddDbContext&lt;Store&gt;();
        /// builder.Services.AddOrielService&lt;Invoices&gt;(OrielServiceScope.Call);
        /// var app = builder.Build();
        /// app.OpenWindow(new() { Url = "/" });
        /// return await app.RunAsync();
        /// </code>
        /// </example>
        public static OrielAppBuilder CreateBuilder(string[]? args = null) => new(args);
    }
}

/// <summary>Builds an <see cref="OrielHost"/>: see <c>OrielApp.CreateBuilder</c>.</summary>
public sealed class OrielAppBuilder
{
    private readonly HostApplicationBuilder _host;

    internal OrielAppBuilder(string[]? args)
    {
        _host = Host.CreateApplicationBuilder(args);
        Options = new AppOptions { Name = _host.Environment.ApplicationName };
    }

    /// <summary>The services of the container. Add the ones pages call with <see cref="OrielServiceCollectionExtensions.AddOrielService{T}"/>.</summary>
    public IServiceCollection Services => _host.Services;

    /// <summary>The app's configuration.</summary>
    public ConfigurationManager Configuration => _host.Configuration;

    /// <summary>Where the app's logs go.</summary>
    public ILoggingBuilder Logging => _host.Logging;

    /// <summary>The environment the app runs in: development, production.</summary>
    public IHostEnvironment Environment => _host.Environment;

    /// <summary>
    /// How the app runs. Its name defaults to the host's application name,
    /// the name of the entry assembly.
    /// </summary>
    public AppOptions Options { get; set; }

    /// <summary>The backend, for tests; the system's by default.</summary>
    internal IPlatform? Platform { get; set; }

    /// <summary>
    /// Builds the host and the app, and binds the services added with
    /// <see cref="OrielServiceCollectionExtensions.AddOrielService{T}"/>. The
    /// container has the <see cref="OrielApp"/>, for services that open windows.
    /// </summary>
    public OrielHost Build()
    {
        var app = Platform is null
            ? new OrielApp(Options)
            : new OrielApp(Options, Platform, System.Environment.GetEnvironmentVariable(Frontend.DevUrlVariable));
        _host.Services.AddSingleton(app);
        return new OrielHost(app, _host.Build());
    }
}

/// <summary>
/// An Oriel app on the .NET Generic Host: its windows, its container and its
/// hosted services, which start before the app runs and stop after it quit.
/// </summary>
public sealed class OrielHost : IHost, IAsyncDisposable
{
    private readonly IHost _host;
    private readonly WindowScopes _windows;
    private readonly ILogger _logger;

    internal OrielHost(OrielApp app, IHost host)
    {
        App = app;
        _host = host;
        _logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<OrielApp>();
        _windows = new WindowScopes(host.Services, _logger);
        // Logged instead of written to the standard error stream.
        app.Error += e => _logger.LogError(e, "{Message}", e.Message);
        app.CallFailed += (method, e) => _logger.LogError(e, "{Method} failed: {Message}", method, e.Message);
        foreach (var registration in host.Services.GetServices<OrielServiceRegistration>())
            registration.Bind(app, host.Services, _windows);
    }

    /// <summary>The app: its windows, events and options.</summary>
    public OrielApp App { get; }

    /// <summary>The container's services.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>Opens a window, before the app runs or while it runs: see <see cref="OrielApp.OpenWindow"/>.</summary>
    public OrielWindow OpenWindow(WindowOptions? options = null) => App.OpenWindow(options);

    /// <summary>
    /// Starts the hosted services, runs the app until it quits, then stops
    /// them. Stopping the host (<see cref="IHostApplicationLifetime.StopApplication"/>,
    /// Ctrl+C in a console) or <paramref name="cancellation"/> quits the app.
    /// </summary>
    /// <returns>The exit code, 0.</returns>
    public async Task<int> RunAsync(CancellationToken cancellation = default)
    {
        await _host.StartAsync(cancellation).ConfigureAwait(false);
        int exit;
        try
        {
            var lifetime = Services.GetRequiredService<IHostApplicationLifetime>();
            using var stopping = lifetime.ApplicationStopping.Register(App.Quit);
            using var canceled = cancellation.Register(App.Quit);
            // Run blocks until the app quits, on a thread of its own.
            exit = await Task.Factory.StartNew(() => App.Run(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .ConfigureAwait(false);
        }
        finally
        {
            await _host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        return exit;
    }

    /// <summary>Runs as <see cref="RunAsync"/> does, blocking the calling thread.</summary>
    public int Run() => RunAsync().GetAwaiter().GetResult();

    Task IHost.StartAsync(CancellationToken cancellationToken) => _host.StartAsync(cancellationToken);

    Task IHost.StopAsync(CancellationToken cancellationToken) => _host.StopAsync(cancellationToken);

    /// <summary>Disposes the scopes of the windows, then the host and its container.</summary>
    public async ValueTask DisposeAsync()
    {
        await _windows.DisposeAsync().ConfigureAwait(false);
        if (_host is IAsyncDisposable disposable)
            await disposable.DisposeAsync().ConfigureAwait(false);
        else
            _host.Dispose();
    }

    /// <inheritdoc cref="DisposeAsync"/>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
