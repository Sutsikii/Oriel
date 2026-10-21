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
    /// Defaults to <c>wwwroot</c> beside the executable.
    /// </summary>
    public string? AssetsDirectory { get; init; }

    /// <summary>Whether <see cref="OrielApp.Run"/> returns once the last window closed. Defaults to true.</summary>
    public bool QuitWhenLastWindowCloses { get; init; } = true;
}

/// <summary>How an <see cref="OrielWindow"/> opens.</summary>
public sealed class WindowOptions
{
    /// <summary>
    /// The title of the window. When null, the window shows the title of its
    /// page, <c>document.title</c>, and follows it.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>The width of the window, in device-independent pixels.</summary>
    public int Width { get; init; } = 1024;

    /// <summary>The height of the window, in device-independent pixels.</summary>
    public int Height { get; init; } = 768;

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
}
