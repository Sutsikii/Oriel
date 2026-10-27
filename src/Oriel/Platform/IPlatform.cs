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

    /// <summary>
    /// Whether <see cref="Run"/> runs on the thread that calls
    /// <see cref="OrielApp.Run"/>, rather than on a thread of Oriel's: on
    /// macOS, where AppKit takes the process's main thread.
    /// </summary>
    bool RunsOnCallingThread => false;

    /// <summary>Runs <paramref name="action"/> on the UI thread, soon. Any thread may call it, before <see cref="Run"/> too.</summary>
    void Post(Action action);

    /// <summary>Ends the event loop. UI thread only.</summary>
    void Quit();

    /// <summary>Opens <paramref name="uri"/>, an http(s) or mailto URL, with the system's default app. UI thread only.</summary>
    void OpenExternally(Uri uri);

    /// <summary>Opens a window showing a page. UI thread only.</summary>
    IPlatformWindow OpenWindow(PlatformWindowOptions options, IPlatformWindowEvents events);

    // ---- The clipboard, UI thread only (see Clipboard) ----

    string? GetClipboardText() => throw ClipboardNotYet();

    string? GetClipboardHtml() => throw ClipboardNotYet();

    /// <summary>The image on the clipboard, as a PNG file's bytes.</summary>
    byte[]? GetClipboardImage() => throw ClipboardNotYet();

    IReadOnlyList<string>? GetClipboardFiles() => throw ClipboardNotYet();

    /// <summary>Empties the clipboard, then puts each format of <paramref name="content"/> there.</summary>
    void SetClipboard(ClipboardContent content) => throw ClipboardNotYet();

    private static NotSupportedException ClipboardNotYet() =>
        new("The clipboard is in the Windows backend only, for now.");
}

/// <summary>A window of a backend. Every member is called on the UI thread.</summary>
internal interface IPlatformWindow
{
    /// <summary>Delivers a message to the page; dropped when there is no page yet.</summary>
    void PostMessage(string message);

    /// <summary>
    /// Delivers a message to the page with <paramref name="bytes"/>, in a
    /// buffer shared with it, rather than copied through JSON. Returns false
    /// when the webview has no such buffers; the message is not sent then.
    /// </summary>
    bool PostBuffer(string message, ReadOnlySpan<byte> bytes) => false;

    void SetTitle(string title);

    void OpenDevTools();

    /// <summary>Closes the window, as its close button would.</summary>
    void Close();

    /// <summary>Reloads the page, after its renderer failed.</summary>
    void Reload();

    /// <summary>Creates the webview anew and loads the page, after the browser process failed.</summary>
    void Recreate();

    /// <summary>Shows <paramref name="html"/> in place of the page, which keeps failing.</summary>
    void ShowErrorPage(string html);

    /// <summary>
    /// Shows a file or folder dialog, modal to the window, and returns once it
    /// is closed: the full paths chosen, or null when it was canceled.
    /// </summary>
    IReadOnlyList<string>? ShowFileDialog(FileDialogRequest request);

    /// <summary>Shows a message box, modal to the window, and returns the button pressed.</summary>
    MessageResult ShowMessage(MessageRequest request);

    /// <summary>Opens a print dialog for the page, and returns at once.</summary>
    void ShowPrintDialog(PrintDialog dialog);

    /// <summary>
    /// Starts printing the page to a PDF at <paramref name="path"/>, a full
    /// path, laid out by <paramref name="options"/> (validated), or as the
    /// webview does by default when null. Calls <paramref name="done"/> on the
    /// UI thread with whether the file was written; at once with false when
    /// there is no page.
    /// </summary>
    void PrintToPdf(string path, PdfOptions? options, Action<bool> done);

    /// <summary>Shows <paramref name="menu"/> as the window's bar, or removes the bar when null. Clicks go to <see cref="IPlatformWindowEvents.MenuItemClicked"/>.</summary>
    void SetMenu(Menu? menu);

    /// <summary>
    /// Shows <paramref name="menu"/> at <paramref name="at"/>, a point of the
    /// page in CSS pixels, or at the mouse pointer when null; returns once it
    /// is closed: the item clicked, or null.
    /// </summary>
    MenuItem? ShowContextMenu(Menu menu, PagePoint? at);

    /// <summary>
    /// Opens the menu of the bar whose mnemonic is <paramref name="mnemonic"/>,
    /// as Alt and that letter do; or, for <c>'\0'</c>, selects the bar, as F10 does.
    /// </summary>
    void ActivateMenuBar(char mnemonic);

    /// <summary>Moves or resizes the window, in device-independent pixels; null keeps what it is. A minimized or maximized window is restored first.</summary>
    void SetBounds(int? x, int? y, int? width, int? height);

    /// <summary>Puts the window in the middle of its monitor's work area.</summary>
    void Center();

    void SetState(WindowState state);

    void SetFullScreen(bool fullScreen);

    void SetAlwaysOnTop(bool onTop);

    /// <summary>Brings the window to the front and gives it the keyboard, restoring it when minimized.</summary>
    void Focus();

    /// <summary>
    /// The window's placement, in a form of the backend's own that
    /// <see cref="PlatformWindowOptions.Placement"/> takes back: the
    /// position and size it has when neither minimized nor maximized, and
    /// whether it is maximized. Still available while <see cref="IPlatformWindowEvents.Closed"/> runs.
    /// </summary>
    string Placement();

    /// <summary>Runs an editing command of the webview, such as <c>Copy</c>, in the page's focused element.</summary>
    void RunEditCommand(string command);
}

/// <summary>A point of the page, in CSS pixels from its top left corner.</summary>
internal readonly record struct PagePoint(double X, double Y);

internal enum FileDialogKind
{
    /// <summary>A file that exists.</summary>
    Open,

    /// <summary>Files that exist, one or more.</summary>
    OpenMany,

    /// <summary>Where to save a file, asking before replacing one.</summary>
    Save,

    /// <summary>A folder that exists.</summary>
    Folder,
}

/// <summary>A kind of file of a dialog: what it shows, and its patterns joined by <c>;</c>, such as <c>*.png;*.jpg</c>.</summary>
internal sealed record FileTypeSpec(string Name, string Spec);

/// <summary>
/// A file dialog to show, with everything resolved: <c>Directory</c> is a
/// full path that exists, or null.
/// </summary>
internal sealed record FileDialogRequest(FileDialogKind Kind, string? Title, IReadOnlyList<FileTypeSpec> Filters, string? Directory)
{
    /// <summary>The name a save dialog proposes.</summary>
    public string? FileName { get; init; }

    /// <summary>The extension a save dialog adds to a name typed without one, without its dot.</summary>
    public string? DefaultExtension { get; init; }
}

/// <summary>A message box to show.</summary>
internal sealed record MessageRequest(string Title, string Text, MessageKind Kind, MessageButtons Buttons);

/// <summary>What a backend window tells Oriel. Called on the UI thread.</summary>
internal interface IPlatformWindowEvents
{
    /// <summary>
    /// The page sent <paramref name="message"/>; <paramref name="source"/> is
    /// the URL of the document that sent it, and <paramref name="files"/> the
    /// full paths of the files it sent with the message, if any.
    /// </summary>
    void MessageReceived(string source, string message, IReadOnlyList<string>? files = null);

    /// <summary>The page's title changed.</summary>
    void TitleChanged(string title);

    /// <summary>The window is gone.</summary>
    void Closed();

    /// <summary>Something failed after the window opened, such as creating its webview.</summary>
    void Failed(Exception error);

    /// <summary>A process of the webview failed. Oriel answers with one of the window's recovery methods.</summary>
    void WebViewFailed(WebViewFailure failure);

    /// <summary>
    /// The page's top document is about to navigate to <paramref name="uri"/>.
    /// The backend cancels the navigation unless this returns <see cref="NavigationAction.Allow"/>.
    /// </summary>
    NavigationAction NavigationStarting(string uri, bool isUserInitiated);

    /// <summary>
    /// The page asks for a window. The backend opens none itself: Oriel opens
    /// an <see cref="OrielWindow"/>, the system's browser, or nothing.
    /// </summary>
    void NewWindowRequested(string uri, bool isUserInitiated);

    /// <summary>A document at <paramref name="uri"/> asks for <paramref name="permission"/>; true grants it.</summary>
    bool PermissionRequested(string uri, WebPermissions permission);

    /// <summary>The window moved or was resized; <paramref name="bounds"/> are its new ones.</summary>
    void BoundsChanged(WindowBounds bounds);

    /// <summary>The window was minimized, maximized or restored.</summary>
    void StateChanged(WindowState state);

    /// <summary>The window became the active one, or stopped being it.</summary>
    void FocusChanged(bool focused);

    /// <summary>An item of the window's bar was clicked.</summary>
    void MenuItemClicked(MenuItem item);

    /// <summary>
    /// Whether <paramref name="chord"/> may run an item of the window's bar,
    /// once the page left it alone: the backend then keeps the webview from
    /// using it, as it would Ctrl+P or F5.
    /// </summary>
    bool IsAccelerator(KeyChord chord);
}

/// <summary>
/// A window to open, with everything resolved: sizes in device-independent
/// pixels. When <c>ErrorPage</c> is set, the window shows that HTML instead of
/// <c>Url</c>, which cannot be shown.
/// </summary>
/// <remarks>
/// <c>Placement</c>, from <see cref="IPlatformWindow.Placement"/> of an
/// earlier window, wins over the position, size and state when the backend
/// can still use it.
/// </remarks>
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
    EmbeddedFrontend? EmbeddedAssets,
    string? ErrorPage,
    string UserDataFolder,
    Menu? Menu = null)
{
    public int? X { get; init; }

    public int? Y { get; init; }

    public bool Center { get; init; }

    public WindowState State { get; init; }

    public bool AlwaysOnTop { get; init; }

    public bool Frameless { get; init; }

    public string? Placement { get; init; }

    /// <summary>The app's shared streams, which the window serves (see <see cref="StreamServer.TryGetId"/>).</summary>
    public StreamServer? Streams { get; init; }
}
