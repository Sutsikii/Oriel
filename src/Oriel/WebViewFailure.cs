namespace Oriel;

/// <summary>What failed in the webview of a window.</summary>
public enum WebViewFailure
{
    /// <summary>The browser process exited: every webview of the app lost its page.</summary>
    BrowserExited,

    /// <summary>The process rendering the page exited, out of memory for instance.</summary>
    RendererExited,

    /// <summary>The process rendering the page stopped responding.</summary>
    RendererUnresponsive,

    /// <summary>The process rendering a frame of the page exited; the page itself goes on.</summary>
    FrameRendererExited,

    /// <summary>The GPU process exited; the webview starts another one itself.</summary>
    GpuExited,

    /// <summary>Another of the webview's helper processes exited.</summary>
    Other,
}

/// <summary>What Oriel does after a <see cref="WebViewFailure"/>.</summary>
public enum WebViewRecovery
{
    /// <summary>Nothing: the page goes on, or the webview recovers by itself.</summary>
    None,

    /// <summary>Reloads the page in the same webview.</summary>
    Reload,

    /// <summary>Creates the webview anew and loads the page in it.</summary>
    Recreate,

    /// <summary>Shows a page saying the window stopped working, with a link to load it again.</summary>
    ErrorPage,
}

/// <summary>The webview of a window failed; see <see cref="OrielWindow.WebViewFailed"/>.</summary>
public sealed class WebViewFailedEventArgs : EventArgs
{
    internal WebViewFailedEventArgs(WebViewFailure failure, WebViewRecovery recovery)
    {
        Failure = failure;
        Recovery = recovery;
    }

    /// <summary>What failed.</summary>
    public WebViewFailure Failure { get; }

    /// <summary>
    /// What Oriel does next. Set by Oriel to recover the page, or to show an
    /// error page when the page keeps failing; handlers may change it.
    /// </summary>
    public WebViewRecovery Recovery { get; set; }
}
