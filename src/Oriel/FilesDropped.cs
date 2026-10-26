namespace Oriel;

/// <summary>Files were dropped on a window's page; see <see cref="OrielWindow.FilesDropped"/>.</summary>
public sealed class FilesDroppedEventArgs : EventArgs
{
    internal FilesDroppedEventArgs(IReadOnlyList<string> paths, double x, double y)
    {
        Paths = paths;
        X = x;
        Y = y;
    }

    /// <summary>The full paths of the files dropped, in the order the system gave them.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Where they were dropped, in CSS pixels from the left of the page.</summary>
    public double X { get; }

    /// <summary>Where they were dropped, in CSS pixels from the top of the page.</summary>
    public double Y { get; }
}
