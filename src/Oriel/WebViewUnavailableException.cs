namespace Oriel;

/// <summary>
/// The system webview that pages show in is missing. On Windows, that is the
/// Microsoft Edge WebView2 Runtime; the message says where to get it.
/// </summary>
public sealed class WebViewUnavailableException : Exception
{
    /// <summary>Creates the exception with the reason.</summary>
    public WebViewUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the reason and the error behind it.</summary>
    public WebViewUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
