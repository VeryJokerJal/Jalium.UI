using Jalium.UI.Documents;
using Jalium.UI.Xps;
using ViewerDocumentPaginator = Jalium.UI.Documents.DocumentPaginator;

namespace Jalium.UI.Controls;

internal sealed class FlowDocumentSearchSession
{
    private FlowDocument? _document;
    private string _lastSearchText = string.Empty;
    private int _nextSearchOffset;

    public TextSelection? Selection { get; private set; }

    public void Attach(FlowDocument? document)
    {
        _document = document;
        _lastSearchText = string.Empty;
        _nextSearchOffset = 0;
        Selection = document == null
            ? null
            : new TextSelection(document.ContentStart, document.ContentStart);
    }

    public bool Find(string searchText, bool matchCase = false)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        if (_document == null || searchText.Length == 0)
        {
            return false;
        }

        var documentText = _document.GetText();
        if (documentText.Length == 0)
        {
            return false;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(_lastSearchText, searchText, comparison))
        {
            _nextSearchOffset = 0;
        }

        var index = documentText.IndexOf(searchText, Math.Clamp(_nextSearchOffset, 0, documentText.Length), comparison);
        if (index < 0 && _nextSearchOffset > 0)
        {
            index = documentText.IndexOf(searchText, 0, comparison);
        }

        if (index < 0)
        {
            _lastSearchText = searchText;
            _nextSearchOffset = 0;
            return false;
        }

        Selection ??= new TextSelection(_document.ContentStart, _document.ContentStart);
        Selection.SelectOffsets(_document, index, searchText.Length);
        _lastSearchText = searchText;
        _nextSearchOffset = index + Math.Max(1, searchText.Length);
        return true;
    }

    public void SelectAll()
    {
        if (_document == null)
        {
            return;
        }

        Selection ??= new TextSelection(_document.ContentStart, _document.ContentStart);
        Selection.Select(_document.ContentStart, _document.ContentEnd);
    }
}

internal static class FlowDocumentViewerSupport
{
    private static readonly object s_printGate = new();
    private static XpsDocumentWriter? s_activeWriter;

    public static bool IsValidZoomValue(object? value) =>
        value is double number && double.IsFinite(number) && number > 0.0;

    public static double CoerceZoom(double value, double minZoom, double maxZoom) =>
        Math.Clamp(value, Math.Min(minZoom, maxZoom), Math.Max(minZoom, maxZoom));

    public static int GetPageCount(FlowDocument? document) => document?.ViewerPaginator.PageCount ?? 0;

    public static int GetPageNumberForOffset(FlowDocument? document, int offset)
    {
        if (document?.ViewerPaginator is IFlowDocumentPageMetrics metrics)
        {
            return metrics.GetPageNumberForOffset(offset);
        }

        return document == null ? 0 : 1;
    }

    public static int GetPageStartOffset(FlowDocument? document, int oneBasedPageNumber)
    {
        if (document?.ViewerPaginator is IFlowDocumentPageMetrics metrics)
        {
            return metrics.GetPageStartOffset(oneBasedPageNumber);
        }

        return 0;
    }

    public static bool Print(ViewerDocumentPaginator paginator, int currentPage)
    {
        ArgumentNullException.ThrowIfNull(paginator);
        if (paginator.PageCount <= 0)
        {
            return false;
        }

        var dialog = new PrintDialog
        {
            MinPage = 1,
            MaxPage = (uint)Math.Max(1, paginator.PageCount),
            PageRange = new PageRange(1, paginator.PageCount),
            CurrentPage = Math.Clamp(currentPage, 1, paginator.PageCount),
            CurrentPageEnabled = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (dialog.PrintQueue == null)
        {
            dialog.PrintDocument(paginator, "Flow document");
            return true;
        }

        var writer = new XpsDocumentWriter(dialog.PrintQueue);
        lock (s_printGate)
        {
            s_activeWriter = writer;
        }

        try
        {
            if (dialog.PrintTicket is { } printTicket)
            {
                writer.Write(paginator, printTicket);
            }
            else
            {
                writer.Write(paginator);
            }
            return true;
        }
        finally
        {
            lock (s_printGate)
            {
                if (ReferenceEquals(s_activeWriter, writer))
                {
                    s_activeWriter = null;
                }
            }
        }
    }

    public static void CancelPrint()
    {
        lock (s_printGate)
        {
            s_activeWriter?.CancelAsync();
        }
    }

}
