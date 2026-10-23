namespace Oriel;

/// <summary>How an <see cref="OrielApp"/> runs.</summary>
public sealed class AppOptions
{
    /// <summary>
    /// The name of the app. It names the folders of its data: see
    /// <see cref="OrielApp.DataDirectory"/>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The folder of the built frontend, served to pages at <c>https://app.oriel/</c>.
    /// Defaults to the frontend embedded in <see cref="AssetsAssembly"/> when
    /// there is one, else to <c>wwwroot</c> beside the executable.
    /// </summary>
    public string? AssetsDirectory { get; init; }

    /// <summary>
    /// The assembly the built frontend is embedded in, with
    /// <c>OrielEmbedFrontend</c>. Defaults to the app's executable.
    /// </summary>
    public System.Reflection.Assembly? AssetsAssembly { get; init; }

    /// <summary>Whether <see cref="OrielApp.Run"/> returns once the last window closed. Defaults to true.</summary>
    public bool QuitWhenLastWindowCloses { get; init; } = true;

    /// <summary>
    /// The longest message a page may send, in characters. Longer ones are
    /// refused before they are parsed. Defaults to 16 Mi characters.
    /// </summary>
    public int MaxMessageLength { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    /// How deeply the JSON of a page's message may nest. Deeper messages are
    /// refused. Defaults to 64.
    /// </summary>
    public int MaxMessageDepth { get; init; } = 64;

    /// <summary>
    /// How many calls of a window's pages may run at once. Calls beyond it fail
    /// at once with a <c>CallError</c>. Defaults to 1000.
    /// </summary>
    public int MaxCallsInFlight { get; init; } = 1000;
}

/// <summary>How an <see cref="OrielWindow"/> opens.</summary>
public sealed class WindowOptions
{
    /// <summary>
    /// The title of the window. When null, the window shows the title of its
    /// page, <c>document.title</c>, and follows it.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// A name to find the window by, with <see cref="OrielApp.FindWindow"/>,
    /// such as <c>"settings"</c>. Two open windows cannot share a name, so an
    /// app can keep one window of a kind. Defaults to none.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The width of the window, in device-independent pixels.</summary>
    public int Width { get; init; } = 1024;

    /// <summary>The height of the window, in device-independent pixels.</summary>
    public int Height { get; init; } = 768;

    /// <summary>
    /// Where the window opens, from the left of the screen, in
    /// device-independent pixels. Defaults to where Windows puts new windows.
    /// </summary>
    public int? X { get; init; }

    /// <summary>Where the window opens, from the top of the screen, in device-independent pixels.</summary>
    public int? Y { get; init; }

    /// <summary>Whether the window opens in the middle of its monitor, rather than at <see cref="X"/> and <see cref="Y"/>.</summary>
    public bool Center { get; init; }

    /// <summary>Whether the window opens minimized or maximized. Defaults to neither.</summary>
    public WindowState State { get; init; }

    /// <summary>Whether the window stays over the others, as <see cref="OrielWindow.IsAlwaysOnTop"/>.</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>
    /// Whether the window has no title bar nor border: the page is all of it,
    /// and draws its own title bar. Parts of the page with the CSS
    /// <c>app-region: drag</c> move the window as a title bar would. It can
    /// be resized from its sides and bottom. Its <see cref="Menu"/> shows no
    /// bar, but its accelerators work.
    /// </summary>
    public bool Frameless { get; init; }

    /// <summary>
    /// A name to remember the window's placement by, such as <c>"main"</c>:
    /// where it was, its size, and whether it was maximized, saved as it
    /// closes and restored the next time a window of that key opens, on the
    /// same monitor. A monitor that is gone puts it where it would be without
    /// one. Defaults to none: nothing is remembered.
    /// </summary>
    public string? StateKey { get; init; }

    /// <summary>The smallest width the window can be resized to, in device-independent pixels.</summary>
    public int MinWidth { get; init; }

    /// <summary>The smallest height the window can be resized to, in device-independent pixels.</summary>
    public int MinHeight { get; init; }

    /// <summary>
    /// The page to show. A relative URL is one of the app's own pages: served
    /// from <see cref="AppOptions.AssetsDirectory"/>, or by the dev server while
    /// developing. Defaults to <c>/</c>.
    /// </summary>
    public string Url { get; init; } = "/";

    /// <summary>Whether the page's developer tools can be opened. Defaults to <see cref="OrielApp.IsDev"/>.</summary>
    public bool? DevTools { get; init; }

    /// <summary>
    /// What the app's pages may use, such as the camera or notifications.
    /// Requests for anything else, and every request of other origins, are
    /// denied. Defaults to <see cref="WebPermissions.None"/>.
    /// </summary>
    public WebPermissions Permissions { get; init; }

    /// <summary>The menu bar of the window; see <see cref="OrielWindow.Menu"/>. Defaults to none.</summary>
    public Menu? Menu { get; init; }
}
