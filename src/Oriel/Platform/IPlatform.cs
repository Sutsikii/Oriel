namespace Oriel.Platform;

/// <summary>
/// What an operating system backend provides: an event loop on one thread,
/// the UI thread, and windows that show a web page. <see cref="OrielApp"/>
/// does everything else, the same on every system. Each backend lives in a
/// folder of its own (Platform/Windows); see docs/architecture.md before
/// adding one.
/// </summary>
internal interface IPlatform
{
    /// <summary>
    /// Runs the event loop on the calling thread, which becomes the UI
    /// thread, until <see cref="Quit"/>. Calls <paramref name="started"/>
    /// once the loop runs.
    /// </summary>
    void Run(Action started);

    /// <summary>Runs <paramref name="action"/> on the UI thread, soon. Any thread may call it, before <see cref="Run"/> too.</summary>
    void Post(Action action);

    /// <summary>Ends the event loop. UI thread only.</summary>
    void Quit();

    /// <summary>Opens a window showing a page. UI thread only.</summary>
    IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events);
}

/// <summary>A window of a backend. Every member is called on the UI thread.</summary>
internal interface IPlatformWindow
{
    /// <summary>Delivers a message to the page; dropped when there is no page yet.</summary>
    void PostMessage(string message);

    void SetTitle(string title);

    void OpenDevTools();

    /// <summary>Closes the window, as its close button would.</summary>
    void Close();
}

/// <summary>What a backend window tells Oriel. Called on the UI thread.</summary>
internal interface IPlatformWindowEvents
{
    /// <summary>The page sent <paramref name="message"/>; <paramref name="source"/> is the URL of the document that sent it.</summary>
    void MessageReceived(string source, string message);

    /// <summary>The page's title changed.</summary>
    void TitleChanged(string title);

    /// <summary>The window is gone.</summary>
    void Closed();

    /// <summary>Something failed after the window opened, such as creating its webview.</summary>
    void Failed(Exception error);
}

/// <summary>A window to open, with everything resolved: sizes in device-independent pixels.</summary>
internal sealed record PlatformWindowOptions(
    string Title,
    int Width,
    int Height,
    int MinWidth,
    int MinHeight,
    Uri Url,
    string BridgeScript,
    bool DevTools,
    string? AssetsFolder,
    string UserDataFolder);
