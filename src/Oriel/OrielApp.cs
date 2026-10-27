using System.Diagnostics.CodeAnalysis;
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
public sealed class OrielApp : ICoalescingSink
{
    /// <summary>
    /// The variable that makes <see cref="Run"/> write the TypeScript client to
    /// the path it names and return, without opening anything. The build sets
    /// it; see docs/bindings.md.
    /// </summary>
    public const string GenerateVariable = "ORIEL_GENERATE";

    private readonly IPlatform _platform;
    private readonly List<OrielWindow> _windows = [];
    private int _lastWindowId;
    private readonly Lock _lock = new();
    private int _running;

    /// <summary>Creates the app for the operating system it runs on.</summary>
    public OrielApp(AppOptions options)
        : this(options, Platforms.Current(), Environment.GetEnvironmentVariable(Frontend.DevUrlVariable))
    {
    }

    internal OrielApp(AppOptions options, IPlatform platform, string? devUrl, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Name) || options.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"The app's name '{options.Name}' cannot name a folder.", nameof(options));
        if (options.MaxMessageLength <= 0 || options.MaxMessageDepth <= 0 || options.MaxCallsInFlight <= 0)
            throw new ArgumentException("The limits of pages' messages must be positive.", nameof(options));
        Options = options;
        _platform = platform;
        Time = time ?? TimeProvider.System;
        Frontend = new Frontend(
            devUrl,
            options.AssetsDirectory ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            options.AssetsDirectory is null ? EmbeddedFrontend.From(options.AssetsAssembly ?? Assembly.GetEntryAssembly()) : null);
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

    /// <summary>The open window named <paramref name="name"/> (see <see cref="WindowOptions.Name"/>), or null.</summary>
    public OrielWindow? FindWindow(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_lock)
            return _windows.Find(w => w.Name == name);
    }

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
    [Experimental(Experiments.CallFailed, UrlFormat = Experiments.Url)]
    public event Action<string, Exception>? CallFailed;

    internal Frontend Frontend { get; }

    internal ServiceTable Services { get; } = new();

    /// <summary>The clock windows count failures with; tests give their own.</summary>
    internal TimeProvider Time { get; }

    /// <summary>Where the windows with a <see cref="WindowOptions.StateKey"/> remember their placement: <c>windows.json</c> in <see cref="DataDirectory"/>.</summary>
    internal PlacementStore Placements
    {
        get => field ??= new PlacementStore(Path.Combine(DataDirectory, "windows.json"));
        set;
    }

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
    /// Lets pages call the methods of <typeparamref name="T"/>, a
    /// <see cref="ServiceAttribute"/> class, with an instance made for each
    /// call by <paramref name="lease"/>, which gets the calling window. The
    /// lease's release runs once the call is done and its result written.
    /// For instances per call or per window, such as services of a dependency
    /// injection container in a scope; Oriel.Hosting binds services so.
    /// </summary>
    /// <remarks>
    /// Events are not connected: an instance per call has no one to tell.
    /// Declare events on a service bound once, with <see cref="Bind(object)"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The class has no generated dispatcher, or another service has its name.
    /// </exception>
    public void Bind<T>(Func<OrielWindow?, ServiceLease<T>> lease)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(lease);
        Services.Add(new LeasedDispatcher<T>(ServiceRegistry.Create(typeof(T), null), lease));
    }

    /// <summary>
    /// Runs the app until it quits: calls <paramref name="ready"/> on the UI
    /// thread once it can open windows, then handles events. Windows run on a
    /// UI thread of Oriel's, not on the calling thread; on macOS, where AppKit
    /// runs on the main thread only, on the calling thread, which must be the
    /// main one: call Run from <c>Main</c>, before any <c>await</c>.
    /// </summary>
    /// <returns>The exit code, 0.</returns>
    /// <exception cref="WebViewUnavailableException">The system webview is missing.</exception>
    /// <exception cref="InvalidOperationException">On macOS, the calling thread is not the main one.</exception>
    public int Run(Action? ready = null)
    {
        if (Environment.GetEnvironmentVariable(GenerateVariable) is { Length: > 0 } path)
            return WriteTypeScript(path);
        if (Interlocked.Exchange(ref _running, 1) == 1)
            throw new InvalidOperationException("The app is already running.");

        if (RunsOnCallingThread)
        {
            _platform.Run(() => ready?.Invoke());
            return 0;
        }

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

    /// <summary>Whether <see cref="Run"/> runs the app on the calling thread: on macOS.</summary>
    internal bool RunsOnCallingThread => _platform.RunsOnCallingThread;

    /// <summary>Opens a window. Any thread may call it; the window opens on the UI thread.</summary>
    /// <exception cref="InvalidOperationException">A window of <see cref="WindowOptions.Name"/> is open already.</exception>
    public OrielWindow OpenWindow(WindowOptions? options = null)
    {
        options ??= new WindowOptions();
        var url = Frontend.Resolve(options.Url);
        OrielWindow window;
        lock (_lock)
        {
            // Checked and numbered together: a window refused takes no id.
            if (options.Name is not null && _windows.Exists(w => w.Name == options.Name))
                throw new InvalidOperationException($"A window named \"{options.Name}\" is open already: find it with FindWindow.");
            window = new OrielWindow(this, options, url, ++_lastWindowId);
            _windows.Add(window);
        }
        // A page missing from the app's files would show as a blank window:
        // say what is missing instead, in the window and to Error.
        var missing = Frontend.MissingFile(window.Url);
        if (missing is not null)
            ReportError(new FileNotFoundException($"The page {window.Url} is not in the app's files: {missing} does not exist.", missing));
        var native = new PlatformWindowOptions(
            options.Title ?? Options.Name,
            Math.Max(1, options.Width),
            Math.Max(1, options.Height),
            Math.Max(0, options.MinWidth),
            Math.Max(0, options.MinHeight),
            window.Url,
            BridgeScript.Source,
            options.DevTools ?? IsDev,
            IsDev || Frontend.Embedded is not null ? null : Frontend.AssetsFolder,
            IsDev ? null : Frontend.Embedded,
            missing is null ? null : MissingPage(window.Url, missing),
            Path.Combine(DataFolder(Environment.SpecialFolder.LocalApplicationData), "WebView"),
            options.Menu)
        {
            X = options.X,
            Y = options.Y,
            Center = options.Center,
            State = options.State,
            AlwaysOnTop = options.AlwaysOnTop,
            Frameless = options.Frameless,
            Placement = options.StateKey is null ? null : Placements.Load(options.StateKey),
            Streams = Streams,
        };
        Dispatch(() => window.Open(_platform, native));
        return window;
    }

    /// <summary>The app's shared streams, which windows serve.</summary>
    internal StreamServer Streams { get; } = new();

    /// <summary>
    /// Lets pages load data by URL, as from a web server: a video or a song to
    /// play, an image, an export to <c>fetch</c>. <paramref name="open"/> is
    /// called for each request, on any thread, and the data read as the page
    /// takes it. When the stream it returns can seek, pages may ask for parts
    /// of it, as videos do to seek. Any thread may call it.
    /// </summary>
    /// <param name="open">Opens the data, once per request; the stream is disposed after.</param>
    /// <param name="contentType">The data's type, such as <c>video/mp4</c>.</param>
    /// <returns>The shared stream: give its <see cref="SharedStream.Url"/> to pages, and dispose it once they are done.</returns>
    public SharedStream Share(Func<Stream> open, string contentType) => Streams.Share(open, contentType);

    /// <summary>
    /// Lets pages load the file at <paramref name="path"/> by URL, with the
    /// type of its extension (<c>.mp4</c> is <c>video/mp4</c>): see
    /// <see cref="Share(Func{Stream}, string)"/>. The file is read for each
    /// request, so pages see it as it is then.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="contentType">The file's type, when its extension does not tell it.</param>
    public SharedStream ShareFile(string path, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path);
        return Streams.Share(
            () => new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            contentType ?? EmbeddedFrontend.ContentType(full));
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

    void ICoalescingSink.BroadcastLatest(string name, string message)
    {
        foreach (var window in Windows)
            window.Connection.SendLatest(name, message);
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

    /// <summary>Opens <paramref name="uri"/> with the system's default app. UI thread only.</summary>
    internal void OpenExternally(Uri uri)
    {
        try
        {
            _platform.OpenExternally(uri);
        }
        catch (Exception e)
        {
            ReportError(e);
        }
    }

    private string DataFolder(Environment.SpecialFolder root)
    {
        var dir = Path.Combine(Environment.GetFolderPath(root), IsDev ? Options.Name + " Dev" : Options.Name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>What a window shows when its page is missing from the app's files.</summary>
    internal static string MissingPage(Uri url, string file)
    {
        static string e(string text) => System.Net.WebUtility.HtmlEncode(text);
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><meta name="color-scheme" content="light dark"><title>Page not found</title>
            <style>
              body { font: 15px/1.5 "Segoe UI", system-ui, sans-serif; margin: 0; padding: 40px; }
              h1 { font-size: 20px; margin: 0 0 12px; }
              code { font: 13px Consolas, monospace; background: #8882; padding: 2px 5px; border-radius: 4px; word-break: break-all; }
              li { margin: 6px 0; }
            </style></head>
            <body>
              <h1>This window has no page to show</h1>
              <p>It opens <code>{{e(url.ToString())}}</code>, which is served from <code>{{e(file)}}</code>, and that file does not exist.</p>
              <ul>
                <li>Run the published app: <code>dotnet publish -c Release</code> builds the frontend and copies it to <code>wwwroot</code>.</li>
                <li>In a Debug build, build the frontend first (<code>npm run build</code>), then the app, so that <code>wwwroot</code> is copied beside it.</li>
                <li>While developing, use <code>npm run dev</code>: pages then come from the dev server.</li>
              </ul>
            </body></html>
            """;
    }

    /// <summary>What a window shows when its page keeps failing.</summary>
    internal static string FailedPage(Uri url)
    {
        static string e(string text) => System.Net.WebUtility.HtmlEncode(text);
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><meta name="color-scheme" content="light dark"><title>This window stopped working</title>
            <style>
              body { font: 15px/1.5 "Segoe UI", system-ui, sans-serif; margin: 0; padding: 40px; }
              h1 { font-size: 20px; margin: 0 0 12px; }
              a { display: inline-block; margin-top: 8px; padding: 6px 14px; border-radius: 6px; background: #3b5bdb; color: white; text-decoration: none; }
            </style></head>
            <body>
              <h1>This window stopped working</h1>
              <p>Its page failed several times in a row, so it was not loaded again.</p>
              <a href="{{e(url.ToString())}}">Try again</a>
            </body></html>
            """;
    }

    private static int WriteTypeScript(string path)
    {
        var assembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("No entry assembly to write the TypeScript client of.");
        TypeScriptClient.Write(assembly, path);
        return 0;
    }
}
