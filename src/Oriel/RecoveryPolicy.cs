namespace Oriel;

/// <summary>
/// Decides how a window recovers from a failure of its webview: what to do
/// for each failure, and when to stop trying because the page keeps failing.
/// </summary>
internal sealed class RecoveryPolicy
{
    /// <summary>Failures in <see cref="Window"/> beyond which a page is deemed to fail for good.</summary>
    public const int MaxRecoveries = 3;

    /// <summary>How far back recoveries count.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly Queue<DateTimeOffset> _recoveries = new();

    public RecoveryPolicy(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>What to do after <paramref name="failure"/>.</summary>
    public WebViewRecovery Decide(WebViewFailure failure)
    {
        var recovery = failure switch
        {
            WebViewFailure.RendererExited or WebViewFailure.RendererUnresponsive => WebViewRecovery.Reload,
            WebViewFailure.BrowserExited => WebViewRecovery.Recreate,
            _ => WebViewRecovery.None,
        };
        if (recovery == WebViewRecovery.None)
            return recovery;

        // A page that fails again and again would reload forever: show an error instead.
        var now = _time.GetUtcNow();
        while (_recoveries.Count > 0 && now - _recoveries.Peek() > Window)
            _recoveries.Dequeue();
        if (_recoveries.Count >= MaxRecoveries)
            return WebViewRecovery.ErrorPage;
        _recoveries.Enqueue(now);
        return recovery;
    }
}
