namespace Oriel;

/// <summary>What happens to a navigation of a window's page, or to a page opening a window.</summary>
public enum NavigationAction
{
    /// <summary>
    /// The window navigates; for a new window, Oriel opens an <see cref="OrielWindow"/>
    /// showing the page.
    /// </summary>
    Allow,

    /// <summary>Nothing happens.</summary>
    Deny,

    /// <summary>The page opens in the system's browser (or mail app), and the window stays where it is.</summary>
    OpenExternally,
}

/// <summary>A page navigates, or opens a window; see <see cref="OrielWindow.NavigationRequested"/>.</summary>
public sealed class NavigationRequestedEventArgs : EventArgs
{
    internal NavigationRequestedEventArgs(Uri uri, bool isNewWindow, bool isUserInitiated, NavigationAction action)
    {
        Uri = uri;
        IsNewWindow = isNewWindow;
        IsUserInitiated = isUserInitiated;
        Action = action;
    }

    /// <summary>Where the page goes.</summary>
    public Uri Uri { get; }

    /// <summary>Whether the page opens a window (<c>window.open</c>, <c>target="_blank"</c>) rather than navigating.</summary>
    public bool IsNewWindow { get; }

    /// <summary>Whether a person started it, with a click for instance, rather than a script.</summary>
    public bool IsUserInitiated { get; }

    /// <summary>What happens. Set by Oriel's policy; handlers may change it.</summary>
    public NavigationAction Action { get; set; }
}

/// <summary>What pages may use, besides what every page can. None by default.</summary>
[Flags]
public enum WebPermissions
{
    /// <summary>Nothing beyond what every page can.</summary>
    None = 0,

    /// <summary>The microphone.</summary>
    Microphone = 1 << 0,

    /// <summary>The camera.</summary>
    Camera = 1 << 1,

    /// <summary>The position of the device.</summary>
    Geolocation = 1 << 2,

    /// <summary>Web notifications.</summary>
    Notifications = 1 << 3,

    /// <summary>Sensors other than the camera, the microphone and geolocation.</summary>
    OtherSensors = 1 << 4,

    /// <summary>Reading the clipboard from script; writing it needs no permission.</summary>
    ClipboardRead = 1 << 5,

    /// <summary>Downloading several files without a click each.</summary>
    MultipleDownloads = 1 << 6,

    /// <summary>The File System Access API.</summary>
    FileSystem = 1 << 7,

    /// <summary>Playing media with sound before any interaction.</summary>
    Autoplay = 1 << 8,

    /// <summary>The list of fonts installed on the device.</summary>
    LocalFonts = 1 << 9,

    /// <summary>System exclusive messages of MIDI devices.</summary>
    MidiSystemExclusive = 1 << 10,

    /// <summary>Placing windows on several screens.</summary>
    WindowManagement = 1 << 11,

    /// <summary>Storage the browser does not evict.</summary>
    PersistentStorage = 1 << 12,
}

/// <summary>Oriel's default answer to where pages may go.</summary>
internal static class NavigationPolicy
{
    /// <summary>
    /// The app's pages stay in their window, or open in a new one; other web
    /// pages and mail links open outside; anything else (<c>file:</c>, custom
    /// schemes of other apps…) is refused, since a page could use them to
    /// start programs.
    /// </summary>
    public static NavigationAction Decide(Frontend frontend, Uri uri, bool isNewWindow)
    {
        switch (uri.Scheme)
        {
            case "http" or "https":
                return frontend.IsTrusted(uri.AbsoluteUri) ? NavigationAction.Allow : NavigationAction.OpenExternally;
            case "mailto":
                return NavigationAction.OpenExternally;
            case "about" or "data" or "blob":
                // Documents of the page itself: fine in place, not as windows of their own.
                return isNewWindow ? NavigationAction.Deny : NavigationAction.Allow;
            default:
                return NavigationAction.Deny;
        }
    }
}
