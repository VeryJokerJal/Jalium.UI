using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using PrimitiveDocumentPage = Jalium.UI.Controls.Primitives.DocumentPage;
using PrimitiveDocumentPaginator = Jalium.UI.Controls.Primitives.DocumentPaginator;

namespace Jalium.UI.Documents;

public partial class FlowDocument
{
    private FlowDocumentViewerPaginator? _viewerPaginator;
    private FlowDocumentPaginator? _documentPaginator;

    internal PrimitiveDocumentPaginator ViewerPaginator =>
        _viewerPaginator ??= new FlowDocumentViewerPaginator(this);

    DocumentPaginator IDocumentPaginatorSource.DocumentPaginator =>
        _documentPaginator ??= new FlowDocumentPaginator(this);

    internal event EventHandler? ViewerPaginationChanged;

    private void InitializeViewerPagination()
    {
        Blocks.Changed += OnBlocksChanged;
    }

    private void OnBlocksChanged(object? sender, EventArgs e)
    {
        _viewerPaginator?.InvalidatePagination();
        ViewerPaginationChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OnViewerPaginationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocument document)
        {
            document._viewerPaginator?.InvalidatePagination();
            document.ViewerPaginationChanged?.Invoke(document, EventArgs.Empty);
        }
    }

    private sealed class FlowDocumentViewerPaginator : PrimitiveDocumentPaginator, IFlowDocumentPageMetrics
    {
        private readonly FlowDocument _document;
        private Size _pageSize = new(816.0, 1056.0);

        public FlowDocumentViewerPaginator(FlowDocument document)
        {
            _document = document;
        }

        public override int PageCount => BuildPages().Count;

        public override bool IsPageCountValid => true;

        public override Size PageSize
        {
            get => ResolvePageSize();
            set
            {
                if (value.IsEmpty || !double.IsFinite(value.Width) || !double.IsFinite(value.Height) ||
                    value.Width <= 0 || value.Height <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                if (_pageSize != value)
                {
                    _pageSize = value;
                    _document.ViewerPaginationChanged?.Invoke(_document, EventArgs.Empty);
                }
            }
        }

        public override PrimitiveDocumentPage GetPage(int pageNumber)
        {
            var pages = BuildPages();
            if (pageNumber < 0 || pageNumber >= pages.Count)
            {
                return PrimitiveDocumentPage.Missing;
            }

            var page = pages[pageNumber];
            var pageSize = ResolvePageSize();
            var visual = new TextBlock
            {
                Text = page.Text,
                FontFamily = _document.FontFamily,
                FontSize = Math.Max(1.0, _document.FontSize),
                Foreground = _document.Foreground ?? new SolidColorBrush(Color.Black),
                Background = _document.Background,
                TextWrapping = TextWrapping.Wrap,
                Padding = _document.PagePadding,
                IsTextSelectionEnabled = true,
            };
            visual.Measure(pageSize);
            visual.Arrange(new Rect(pageSize));
            return new PrimitiveDocumentPage { Visual = visual, Size = pageSize };
        }

        public int GetPageNumberForOffset(int offset)
        {
            var pages = BuildPages();
            if (pages.Count == 0)
            {
                return 0;
            }

            var normalized = Math.Max(0, offset);
            for (var index = 0; index < pages.Count; index++)
            {
                if (normalized < pages[index].StartOffset + pages[index].Length || index == pages.Count - 1)
                {
                    return index + 1;
                }
            }

            return pages.Count;
        }

        public int GetPageStartOffset(int oneBasedPageNumber)
        {
            var pages = BuildPages();
            if (pages.Count == 0)
            {
                return 0;
            }

            var index = Math.Clamp(oneBasedPageNumber - 1, 0, pages.Count - 1);
            return pages[index].StartOffset;
        }

        public void InvalidatePagination()
        {
            // Pagination is computed from the live document on demand. The method intentionally
            // exists as the invalidation boundary used by viewers and future cached layout.
        }

        private List<PageSlice> BuildPages()
        {
            var text = _document.GetText();
            if (text.Length == 0)
            {
                return [new PageSlice(0, 0, string.Empty)];
            }

            var size = ResolvePageSize();
            var padding = _document.PagePadding;
            var contentWidth = Math.Max(1.0, size.Width - Math.Max(0.0, padding.Left) - Math.Max(0.0, padding.Right));
            var contentHeight = Math.Max(1.0, size.Height - Math.Max(0.0, padding.Top) - Math.Max(0.0, padding.Bottom));
            var fontSize = Math.Max(1.0, _document.FontSize);
            var averageCharacterWidth = Math.Max(1.0, fontSize * 0.55);
            var lineHeight = double.IsFinite(_document.LineHeight) && _document.LineHeight > 0
                ? _document.LineHeight
                : fontSize * 1.35;
            var charactersPerLine = Math.Max(1, (int)Math.Floor(contentWidth / averageCharacterWidth));
            var linesPerPage = Math.Max(1, (int)Math.Floor(contentHeight / Math.Max(1.0, lineHeight)));
            var charactersPerPage = Math.Max(1, charactersPerLine * linesPerPage);

            var pages = new List<PageSlice>();
            var offset = 0;
            while (offset < text.Length)
            {
                var remaining = text.Length - offset;
                var length = Math.Min(charactersPerPage, remaining);
                if (length < remaining)
                {
                    var breakIndex = FindPageBreak(text, offset, length);
                    if (breakIndex > offset)
                    {
                        length = breakIndex - offset;
                    }
                }

                length = Math.Max(1, length);
                pages.Add(new PageSlice(offset, length, text.Substring(offset, length)));
                offset += length;
            }

            return pages;
        }

        private Size ResolvePageSize()
        {
            var width = double.IsFinite(_document.PageWidth) && _document.PageWidth > 0
                ? _document.PageWidth
                : _pageSize.Width;
            var height = double.IsFinite(_document.PageHeight) && _document.PageHeight > 0
                ? _document.PageHeight
                : _pageSize.Height;
            width = Math.Max(_document.MinPageWidth, Math.Min(_document.MaxPageWidth, width));
            height = Math.Max(_document.MinPageHeight, Math.Min(_document.MaxPageHeight, height));
            return new Size(width, height);
        }

        private static int FindPageBreak(string text, int offset, int tentativeLength)
        {
            var end = Math.Min(text.Length, offset + tentativeLength);
            var minimum = offset + Math.Max(1, tentativeLength / 2);
            for (var index = end; index > minimum; index--)
            {
                var character = text[index - 1];
                if (character is '\n' or '\r' || char.IsWhiteSpace(character) || char.IsPunctuation(character))
                {
                    return index;
                }
            }

            return end;
        }

        private readonly record struct PageSlice(int StartOffset, int Length, string Text);
    }

    private sealed class FlowDocumentPaginator : DocumentPaginator
    {
        private readonly FlowDocument _document;

        internal FlowDocumentPaginator(FlowDocument document)
        {
            _document = document;
        }

        private PrimitiveDocumentPaginator Inner => _document.ViewerPaginator;

        public override bool IsPageCountValid => Inner.IsPageCountValid;
        public override int PageCount => Inner.PageCount;
        public override Size PageSize { get => Inner.PageSize; set => Inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _document;

        public override DocumentPage GetPage(int pageNumber)
        {
            var page = Inner.GetPage(pageNumber);
            return page.Visual is null || page.Size.IsEmpty
                ? DocumentPage.Missing
                : new DocumentPage(page.Visual, page.Size, new Rect(page.Size), new Rect(page.Size));
        }
    }
}

internal interface IFlowDocumentPageMetrics
{
    int GetPageNumberForOffset(int offset);
    int GetPageStartOffset(int oneBasedPageNumber);
}
