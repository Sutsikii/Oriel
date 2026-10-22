namespace Oriel;

/// <summary>Which print dialog <see cref="OrielWindow.ShowPrintDialog"/> shows.</summary>
public enum PrintDialog
{
    /// <summary>The webview's own, with a preview: what Ctrl+P shows.</summary>
    Browser,

    /// <summary>The system's, with every option of the printers.</summary>
    System,
}

/// <summary>
/// How <see cref="OrielWindow.PrintToPdfAsync"/> lays out the page. Sizes and
/// margins are in inches.
/// </summary>
public sealed class PdfOptions
{
    /// <summary>Whether pages are wider than tall. Defaults to false.</summary>
    public bool Landscape { get; init; }

    /// <summary>How much the content is scaled, from 0.1 to 2. Defaults to 1.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>The width of a page, in inches. Defaults to 8.5, US Letter; A4 is 8.27.</summary>
    public double PageWidth { get; init; } = 8.5;

    /// <summary>The height of a page, in inches. Defaults to 11, US Letter; A4 is 11.69.</summary>
    public double PageHeight { get; init; } = 11;

    /// <summary>The top margin, in inches. Defaults to 0.4, about a centimeter.</summary>
    public double MarginTop { get; init; } = 0.4;

    /// <summary>The bottom margin, in inches. Defaults to 0.4.</summary>
    public double MarginBottom { get; init; } = 0.4;

    /// <summary>The left margin, in inches. Defaults to 0.4.</summary>
    public double MarginLeft { get; init; } = 0.4;

    /// <summary>The right margin, in inches. Defaults to 0.4.</summary>
    public double MarginRight { get; init; } = 0.4;

    /// <summary>Whether background colors and images are printed. Defaults to false, as browsers do.</summary>
    public bool Backgrounds { get; init; }

    /// <summary>Whether pages have a header (date, title) and a footer (URL, page number). Defaults to false.</summary>
    public bool HeaderAndFooter { get; init; }

    /// <summary>The title in the header, when there is one. Defaults to the page's title.</summary>
    public string? HeaderTitle { get; init; }

    /// <summary>Checks the options can be printed.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A size, margin or scale cannot be printed.</exception>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Scale, 0.1, nameof(Scale));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Scale, 2.0, nameof(Scale));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PageWidth, nameof(PageWidth));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PageHeight, nameof(PageHeight));
        ArgumentOutOfRangeException.ThrowIfNegative(MarginTop, nameof(MarginTop));
        ArgumentOutOfRangeException.ThrowIfNegative(MarginBottom, nameof(MarginBottom));
        ArgumentOutOfRangeException.ThrowIfNegative(MarginLeft, nameof(MarginLeft));
        ArgumentOutOfRangeException.ThrowIfNegative(MarginRight, nameof(MarginRight));
        if (MarginLeft + MarginRight >= PageWidth || MarginTop + MarginBottom >= PageHeight)
            throw new ArgumentOutOfRangeException(nameof(PageWidth), "The margins leave no room on the page.");
    }
}
