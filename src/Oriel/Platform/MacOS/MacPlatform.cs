using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Oriel.Platform.MacOS.ObjC;

namespace Oriel.Platform.MacOS;

/// <summary>
/// The macOS backend: the AppKit event loop of <c>NSApplication</c>, and
/// windows whose page is a WKWebView. AppKit and WebKit are the system's,
/// reached through the Objective-C runtime (<see cref="ObjC"/>).
/// </summary>
/// <remarks>
/// AppKit runs on the process's main thread only, so this backend runs on
/// the thread that calls <see cref="Run"/>, which must be the main one
/// (<see cref="RunsOnCallingThread"/>). WKWebView cannot serve a custom
/// https host, so the app's files are served at <c>oriel://app.oriel/</c>
/// (<see cref="AppScheme"/>), as on Linux.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacPlatform : IPlatform
{
    public const byte YES = 1;
    public const byte NO = 0;

    private static MacPlatform? s_running;
    private static bool s_menuSet;

    private readonly ConcurrentQueue<Action> _queue = new();
    private volatile bool _looping;

    // The loops of apps run on other threads, which the main thread runs
    // when something serves them there: the end-to-end tests do, from
    // their own Main (ServeMainThread). Apps call Run on the main thread.
    private static readonly BlockingCollection<Action> s_loops = [];
    private static volatile bool s_served;

    public bool RunsOnCallingThread => true;

    /// <summary>The windows that are open, which report what fails outside any of them.</summary>
    internal static readonly List<MacWebWindow> Windows = [];

    public void Run(Action started)
    {
        if (pthread_main_np() == 1)
        {
            Loop(started);
            return;
        }
        if (!s_served)
            throw new InvalidOperationException(
                "On macOS, AppKit runs on the main thread only: call OrielApp.Run on it, from Main, before any await.");
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

    /// <summary>
    /// Runs, on the main thread that calls it, the loops of the apps that
    /// other threads run, until <paramref name="until"/> completes. For
    /// programs whose main thread is busy otherwise, such as a test runner.
    /// </summary>
    internal static void ServeMainThread(Task until)
    {
        if (pthread_main_np() != 1)
            throw new InvalidOperationException("ServeMainThread runs on the main thread.");
        s_served = true;
        try
        {
            while (!until.IsCompleted)
            {
                if (s_loops.TryTake(out var loop, 100))
                    loop();
            }
        }
        finally
        {
            s_served = false;
        }
    }

    private void Loop(Action started)
    {
        using var pool = AutoreleasePool();
        LoadFrameworks();
        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        Send(app, Sel("setActivationPolicy:"), (nint)0); // NSApplicationActivationPolicyRegular: in the Dock, with a menu bar
        if (!s_menuSet)
        {
            // Without an Edit menu, the page gets no Cmd+C, Cmd+V or Cmd+Z.
            Send(app, Sel("setMainMenu:"), MainMenu());
            s_menuSet = true;
        }
        s_running = this;
        try
        {
            _looping = true;
            started();
            Drain();
            Send(app, Sel("activateIgnoringOtherApps:"), YES);
            Send(app, Sel("run"));
        }
        finally
        {
            _looping = false;
            s_running = null;
        }
    }

    /// <summary>The app's menu, with Hide and Quit, and the Edit menu, whose keys WKWebView edits with.</summary>
    private static nint MainMenu()
    {
        var bar = Send(New("NSMenu"), Sel("autorelease"));
        AddSubmenu(bar, "", [
            ("Hide", "hide:", "h"),
            ("Quit", "stop:", "q"), // Run returns, as with OrielApp.Quit
        ]);
        AddSubmenu(bar, "Edit", [
            ("Undo", "undo:", "z"),
            ("Redo", "redo:", "Z"),
            ("Cut", "cut:", "x"),
            ("Copy", "copy:", "c"),
            ("Paste", "paste:", "v"),
            ("Select All", "selectAll:", "a"),
        ]);
        return bar;
    }

    private static void AddSubmenu(nint bar, string title, (string Title, string Action, string Key)[] items)
    {
        var menu = Send(Send(Send(Class("NSMenu"), Sel("alloc")), Sel("initWithTitle:"), Str(title)), Sel("autorelease"));
        foreach (var (itemTitle, action, key) in items)
        {
            var item = Send(Send(Class("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"), Str(itemTitle), Sel(action), Str(key));
            Send(menu, Sel("addItem:"), item);
            Release(item);
        }
        var holder = New("NSMenuItem");
        Send(holder, Sel("setSubmenu:"), menu);
        Send(bar, Sel("addItem:"), holder);
        Release(holder);
    }

    public void Post(Action action)
    {
        _queue.Enqueue(action);
        // Before the loop runs, Run drains the queue once it starts.
        if (_looping)
            dispatch_async_f(MainQueue, 0, &Dispatch);
    }

    [UnmanagedCallersOnly]
    private static void Dispatch(nint context)
    {
        using var pool = AutoreleasePool();
        try
        {
            s_running?.Drain();
        }
        catch (Exception e)
        {
            // An exception must not unwind into libdispatch: a window reports it, as on Windows.
            Report(e);
        }
    }

    /// <summary>Runs <paramref name="action"/> on the main thread, unless the app quit.</summary>
    internal static void PostToRunning(Action action) => s_running?.Post(action);

    internal static void Report(Exception e)
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

    public void Quit()
    {
        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        Send(app, Sel("stop:"), (nint)0);
        // stop: takes effect once the loop handles an event: give it one.
        var wake = Send(Class("NSEvent"), Sel("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
            15, new NSPoint(0, 0), 0, 0, 0, 0, 0, 0, 0); // NSEventTypeApplicationDefined
        Send(app, Sel("postEvent:atStart:"), wake, YES);
    }

    public void OpenExternally(Uri uri)
    {
        var workspace = Send(Class("NSWorkspace"), Sel("sharedWorkspace"));
        if (!SendBool(workspace, Sel("openURL:"), Url(uri.AbsoluteUri)))
            throw new InvalidOperationException($"Opening {uri} with the system's default app failed.");
    }

    public IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events)
    {
        var window = new MacWebWindow(options, events);
        window.Start();
        return window;
    }
}
