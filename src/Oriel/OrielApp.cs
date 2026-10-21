using System.Reflection;
using Oriel.Bridge;
using Oriel.Generated;
using Oriel.Ipc;
using Oriel.Platform;

namespace Oriel;

/// <summary>
/// A desktop app: its services, its windows and its event loop.
/// </summary>
/// <example>
/// <code>
/// var app = new OrielApp(new AppOptions { Name = "Hello" });
/// app.Bind(new Greeter());
/// return app.Run(() =&gt; app.OpenWindow(new WindowOptions { Title = "Hello" }));
/// </code>
/// </example>
public sealed class OrielApp : IEventSink
{
    /// <summary>
    /// The variable that makes <see cref="Run"/> write the TypeScript client to
    /// the path it names and return, without opening anything. The build sets
    /// it; see docs/bindings.md.
    /// </summary>
    public const string GenerateVariable = "ORIEL_GENERATE";

    private readonly IPlatform _platform;
    private readonly List<OrielWindow> _windows = [];
    private readonly Lock _lock = new();
    private int _running;

    /// <summary>Creates the app for the operating system it runs on.</summary>
    public OrielApp(AppOptions options)
        : this(options, Platforms.Current(), Environment.GetEnvironmentVariable(Frontend.DevUrlVariable))
    {
    }

    internal OrielApp(AppOptions options, IPlatform platform, string? devUrl)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Name) || options.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"The app's name '{options.Name}' cannot name a folder.", nameof(options));
        Options = options;
        _platform = platform;
        Frontend = new Frontend(devUrl, options.AssetsDirectory ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"));
    }

    /// <summary>The options the app was created with.</summary>
    public AppOptions Options { get; }

    /// <summary>
    /// Whether the app runs in development: its pages come from a dev server,
    /// named by <c>ORIEL_DEV_URL</c>.
    /// </summary>
    public bool IsDev => Frontend.IsDev;

    /// <summary>
    /// The folder for the app's data, created on first use: <c>%APPDATA%\Name</c>,
    /// or <c>%APPDATA%\Name Dev</c> in development so the two never mix.
    /// </summary>
    public string DataDirectory => DataFolder(Environment.SpecialFolder.ApplicationData);

    /// <summary>The windows that are open.</summary>
    public IReadOnlyList<OrielWindow> Windows
    {
        get
        {
            lock (_lock)
                return [.. _windows];
        }
    }

    /// <summary>
    /// Raised when something fails outside of a call: a webview cannot be
    /// created, or a callback throws. Without a handler, errors are written to
    /// the standard error stream.
    /// </summary>
    public event Action<Exception>? Error;

    /// <summary>
    /// Raised when a service method throws, before the page that called it
    /// gets the error.
    /// </summary>
    public event Action<string, Exception>? CallFailed;

    internal Frontend Frontend { get; }

    internal ServiceTable Services { get; } = new();

    /// <summary>
    /// Lets pages call the methods of <paramref name="service"/>, an instance of
    /// a <see cref="ServiceAttribute"/> class, and connects its events.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The class has no generated dispatcher, or another service has its name.
    /// </exception>
    public void Bind(object service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!ServiceRegistry.TryCreate(service, out var dispatcher))
        {
            throw new InvalidOperationException(
                $"{service.GetType().FullName} cannot be bound: it is not a [Service] class, or its project does not run the Oriel source generator.");
        }
        Services.Add(dispatcher);
        dispatcher.Attach(this);
    }

    /// <summary>
    /// Runs the app until it quits: calls <paramref name="ready"/> on the UI
    /// thread once it can open windows, then handles events. Windows run on a
    /// UI thread of Oriel's, not on the calling thread.
    /// </summary>
    /// <returns>The exit code, 0.</returns>
    /// <exception cref="WebViewUnavailableException">The system webview is missing.</exception>
    public int Run(Action? ready = null)
    {
        if (Environment.GetEnvironmentVariable(GenerateVariable) is { Length: > 0 } path)
            return WriteTypeScript(path);
        if (Interlocked.Exchange(ref _running, 1) == 1)
            throw new InvalidOperationException("The app is already running.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _platform.Run(() => ready?.Invoke());
            }
            catch (Exception e)
            {
                failure = e;
            }
        })
        {
            Name = "Oriel UI",
        };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA); // WebView2 needs a single-threaded apartment
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        return 0;
    }

    /// <summary>Opens a window. Any thread may call it; the window opens on the UI thread.</summary>
    public OrielWindow OpenWindow(WindowOptions? options = null)
    {
        options ??= new WindowOptions();
        var window = new OrielWindow(this, options, Frontend.Resolve(options.Url));
        lock (_lock)
            _windows.Add(window);
        var native = new PlatformWindowOptions(
            options.Title ?? Options.Name,
            Math.Max(1, options.Width),
            Math.Max(1, options.Height),
            Math.Max(0, options.MinWidth),
            Math.Max(0, options.MinHeight),
            window.Url,
            BridgeScript.Source,
            options.DevTools ?? IsDev,
            IsDev ? null : Frontend.AssetsFolder,
            Path.Combine(DataFolder(Environment.SpecialFolder.LocalApplicationData), "WebView"));
        Dispatch(() => window.Open(_platform, native));
        return window;
    }

    /// <summary>Quits: closes the event loop, and <see cref="Run"/> returns.</summary>
    public void Quit() => Dispatch(_platform.Quit);

    /// <summary>Runs <paramref name="action"/> on the UI thread. Any thread may call it.</summary>
    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _platform.Post(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ReportError(e);
            }
        });
    }

    void IEventSink.Broadcast(string message)
    {
        foreach (var window in Windows)
            window.Connection.Send(message);
    }

    internal void WindowClosed(OrielWindow window)
    {
        bool last;
        lock (_lock)
        {
            _windows.Remove(window);
            last = _windows.Count == 0;
        }
        if (last && Options.QuitWhenLastWindowCloses)
            _platform.Quit();
    }

    internal void ReportError(Exception error)
    {
        var handler = Error;
        if (handler is null)
            Console.Error.WriteLine($"Oriel: {error}");
        else
            handler(error);
    }

    internal void ReportCallFailure(string method, Exception error) => CallFailed?.Invoke(method, error);

    private string DataFolder(Environment.SpecialFolder root)
    {
        var dir = Path.Combine(Environment.GetFolderPath(root), IsDev ? Options.Name + " Dev" : Options.Name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static int WriteTypeScript(string path)
    {
        var assembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("No entry assembly to write the TypeScript client of.");
        TypeScriptClient.Write(assembly, path);
        return 0;
    }
}
