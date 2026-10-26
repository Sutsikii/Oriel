using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Oriel.Platform.Linux;

/// <summary>
/// The Linux backend: the GTK 3 main loop on the UI thread, and windows
/// whose page is a WebKitGTK view. The libraries are the system's, reached
/// through <see cref="Gtk"/>.
/// </summary>
/// <remarks>
/// WebKitGTK cannot serve a custom https host, so the app's files are
/// served at <c>oriel://app.oriel/</c> (<see cref="AppScheme"/>).
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxPlatform : IPlatform
{
    public const string Scheme = AppScheme.Scheme;

    private static LinuxPlatform? s_running;

    private readonly ConcurrentQueue<Action> _queue = new();
    // The scheme belongs to the default context, one per process, which outlives an app.
    private static bool s_schemeRegistered;

    private volatile bool _looping;
    private string? _assetsFolder;
    private EmbeddedFrontend? _embedded;

    // WebKit takes the first thread that uses it for its main thread, for
    // the life of the process, and aborts when another one does later. So
    // the loops of all the apps a process runs, one after the other, run on
    // this one thread, and Run waits for its app's.
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> s_loops = [];
    private static readonly Lazy<Thread> s_gtkThread = new(() =>
    {
        var thread = new Thread(() =>
        {
            foreach (var loop in s_loops.GetConsumingEnumerable())
                loop();
        })
        {
            IsBackground = true,
            Name = "Oriel GTK",
        };
        thread.Start();
        return thread;
    });

    public void Run(Action started)
    {
        if (Thread.CurrentThread == s_gtkThread.Value)
        {
            Loop(started);
            return;
        }
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        s_loops.Add(() =>
        {
            try
            {
                Loop(started);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }

    private void Loop(Action started)
    {
        if (Gtk.gtk_init_check(0, 0) == 0)
            throw new WebViewUnavailableException("GTK could not start: there is no display. Set DISPLAY or WAYLAND_DISPLAY, or run under Xvfb.");
        s_running = this;
        try
        {
            _looping = true;
            started();
            Drain();
            Gtk.gtk_main();
        }
        finally
        {
            _looping = false;
            s_running = null;
        }
    }

    public void Post(Action action)
    {
        _queue.Enqueue(action);
        // Before the loop runs, Run drains the queue once it starts.
        if (_looping)
            Gtk.g_idle_add(&Dispatch, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int Dispatch(nint data)
    {
        try
        {
            s_running?.Drain();
        }
        catch (Exception e)
        {
            // An exception must not unwind into GLib: a window reports it, as on Windows.
            Report(e);
        }
        return 0; // G_SOURCE_REMOVE
    }

    /// <summary>The windows that are open, which report what fails outside any of them.</summary>
    internal static readonly List<LinuxWebWindow> Windows = [];

    private static void Report(Exception e)
    {
        Tracing.Write($"a posted action failed: {e}");
        if (Windows.Count > 0)
            Windows[0].Fail(e);
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var action))
            action();
    }

    public void Quit() => Gtk.gtk_main_quit();

    public void OpenExternally(Uri uri)
    {
        nint error = 0;
        if (Gtk.gtk_show_uri_on_window(0, uri.AbsoluteUri, 0, &error) == 0)
        {
            Gtk.g_error_free(error);
            throw new InvalidOperationException($"Opening {uri} with the system's default app failed.");
        }
    }

    public IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        RegisterScheme(options);
        var window = new LinuxWebWindow(options, events);
        window.Start();
        return window;
    }

    /// <summary>
    /// Serves the app's files at oriel://app.oriel/. Every window of an app
    /// shows the same frontend, and the scheme belongs to the webview's
    /// default context, which they share: it is registered once per
    /// process, and serves the files of the app that runs.
    /// </summary>
    private void RegisterScheme(PlatformWindowOptions options)
    {
        _assetsFolder = options.AssetsFolder;
        _embedded = options.EmbeddedAssets;
        if (s_schemeRegistered)
            return;
        s_schemeRegistered = true;
        var context = Gtk.webkit_web_context_get_default();
        Gtk.webkit_web_context_register_uri_scheme(context, Scheme, &ServeRequest, 0, 0);
        var security = Gtk.webkit_web_context_get_security_manager(context);
        // Secure, so that pages are a secure context as at https://app.oriel; CORS, for modules and fetch.
        Gtk.webkit_security_manager_register_uri_scheme_as_secure(security, Scheme);
        Gtk.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, Scheme);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void ServeRequest(nint request, nint data)
    {
        try
        {
            s_running?.Serve(request);
        }
        catch (Exception e)
        {
            Report(e);
        }
    }

    private void Serve(nint request)
    {
        var path = AppScheme.FilePath(Gtk.Text(Gtk.webkit_uri_scheme_request_get_path(request)) ?? "/");
        var file = AppScheme.Read(path, _assetsFolder, _embedded);
        Tracing.Write($"serves {path}{(file is null ? ": not found" : "")}");
        // A missing file is a 404, as from a server, rather than a failed load.
        var body = file ?? "Not Found"u8.ToArray();
        nint bytes;
        fixed (byte* p = body)
            bytes = Gtk.g_bytes_new(p, (nuint)body.Length); // copies
        var stream = Gtk.g_memory_input_stream_new_from_bytes(bytes);
        var response = Gtk.webkit_uri_scheme_response_new(stream, body.Length);
        if (file is null)
            Gtk.webkit_uri_scheme_response_set_status(response, 404, "Not Found");
        var type = file is null ? "text/plain; charset=utf-8" : EmbeddedFrontend.ContentType(path);
        Gtk.webkit_uri_scheme_response_set_content_type(response, type);
        // Headers too, which fetch() and WebAssembly.compileStreaming read; the response takes them.
        var headers = Gtk.soup_message_headers_new(Gtk.SOUP_MESSAGE_HEADERS_RESPONSE);
        Gtk.soup_message_headers_append(headers, "Content-Type", type);
        Gtk.soup_message_headers_append(headers, "Cache-Control", "no-cache");
        Gtk.webkit_uri_scheme_response_set_http_headers(response, headers);
        Gtk.webkit_uri_scheme_request_finish_with_response(request, response);
        Gtk.g_object_unref(response);
        Gtk.g_object_unref(stream);
        Gtk.g_bytes_unref(bytes);
    }
}
