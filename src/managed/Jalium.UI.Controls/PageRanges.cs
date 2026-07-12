using System.Globalization;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies which pages are selected for printing.
/// </summary>
public enum PageRangeSelection
{
    /// <summary>
    /// All pages are printed.
    /// </summary>
    AllPages,

    /// <summary>
    /// A user-defined page range is printed.
    /// </summary>
    UserPages,

    /// <summary>
    /// The current page, as defined by the application, is printed.
    /// </summary>
    CurrentPage,

    /// <summary>
    /// The currently selected pages, as defined by the application, are printed.
    /// </summary>
    SelectedPages
}

/// <summary>
/// Represents an inclusive range of page numbers.
/// </summary>
public struct PageRange
{
    private int _pageFrom;
    private int _pageTo;

    /// <summary>
    /// Initializes a range containing one page.
    /// </summary>
    public PageRange(int page)
    {
        _pageFrom = page;
        _pageTo = page;
    }

    /// <summary>
    /// Initializes a range with the specified first and last pages.
    /// </summary>
    public PageRange(int pageFrom, int pageTo)
    {
        _pageFrom = pageFrom;
        _pageTo = pageTo;
    }

    /// <summary>
    /// Gets or sets the first page in the range.
    /// </summary>
    public int PageFrom
    {
        get => _pageFrom;
        set => _pageFrom = value;
    }

    /// <summary>
    /// Gets or sets the last page in the range.
    /// </summary>
    public int PageTo
    {
        get => _pageTo;
        set => _pageTo = value;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return _pageTo != _pageFrom
            ? string.Format(CultureInfo.InvariantCulture, "{0}-{1}", _pageFrom, _pageTo)
            : _pageFrom.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is PageRange pageRange && Equals(pageRange);

    /// <summary>
    /// Determines whether the supplied range has the same endpoints.
    /// </summary>
    public bool Equals(PageRange pageRange) =>
        pageRange.PageFrom == PageFrom && pageRange.PageTo == PageTo;

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    public static bool operator ==(PageRange pr1, PageRange pr2) => pr1.Equals(pr2);
    public static bool operator !=(PageRange pr1, PageRange pr2) => !pr1.Equals(pr2);
}
