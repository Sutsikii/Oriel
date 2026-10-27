namespace Oriel;

/// <summary>The person asked to close a window: see <see cref="OrielWindow.Closing"/>.</summary>
public sealed class WindowClosingEventArgs : EventArgs
{
    /// <summary>Set it to keep the window open.</summary>
    public bool Cancel { get; set; }
}
