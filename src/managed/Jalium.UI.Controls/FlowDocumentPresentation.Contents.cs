using Jalium.UI.Documents;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed class DocumentContentsRun(TextBlock owner)
    {
        internal readonly TextBlock Owner = owner;
        internal readonly TextBlock.CssTextFlowLayout Layout = new(owner);
        internal readonly List<ParagraphView> Views = [];
        internal readonly Dictionary<UIElement, Rect> Allocations = [];
    }
    private readonly List<DocumentContentsRun> _documentContentsRuns = [];

    // The source tree stays intact; table repair consumes the same box projection.
    private static bool DocumentContents(DependencyObject source) => source is Paragraph or Section or DocList or ListItem or Table or TableRowGroup or TableRow or TableCell or TableColumn &&
        source.GetValue(CssDisplayProperties.SpecificationProperty) is CssDisplaySpecification { Mode: CssDisplayMode.Contents, None: false };

    private DependencyObject ContentsFormattingParent(Paragraph paragraph) => ActualDocumentBoxParent(paragraph);

    private IEnumerable<DependencyObject> ContentsFlowNodes(IEnumerable<DependencyObject> blocks) => FixDocumentFlowTables(blocks);

    private TextBlock DocumentContentsOwner(Paragraph first, bool intrinsic = false)
    {
        var owner = new TextBlock { CssIntrinsicMeasure = intrinsic };
        // Use the DP as well as its mode field: inherited ellipsis changes must
        // preserve this synthetic block's formatting context.
        owner.SetValue(CssDisplayLayout.ModeProperty, CssDisplayMode.Block);
        owner.PresentDocumentContentsContext(ContentsFormattingParent(first), _document!, _defaultForeground);
        owner.SetRootDpi(DpiScale);
        return owner;
    }

    private void ClearDocumentContentsRuns() => ReleaseDocumentContentsRuns(0);

    private void ReleaseDocumentContentsRuns(int first)
    {
        for (var index = _documentContentsRuns.Count - 1; index >= first; index--)
        {
            var run = _documentContentsRuns[index];
            _documentClampParticipants.Remove(run.Owner);
            foreach (var view in run.Views)
            {
                view.Text.ReleaseCssInlineAllocation(run.Owner);
                view.Text.CssInlineOuter = false;
                view.ContentsFlow = false;
            }
        }
        _documentContentsRuns.RemoveRange(first, _documentContentsRuns.Count - first);
    }

    private void LayoutDocumentContentsRun(List<ParagraphView> views, double left, double width, ref double y)
    {
        if (views.Count == 0) return;
        var owner = DocumentContentsOwner(views[0].Source);
        var run = new DocumentContentsRun(owner);
        run.Views.AddRange(views); _documentContentsRuns.Add(run);
        foreach (var view in views) { view.Text.CssInlineOuter = true; view.ContentsFlow = true; }
        if (views.Any(view => _documentClampParticipants.Contains(view.Text))) _documentClampParticipants.Add(owner);
        var top = y; var firstLine = _lines.Count;
        var result = run.Layout.FormatRun(views.Select(view => (UIElement)view.Text).ToArray(),
            new(width, double.PositiveInfinity), 0, true, true, width, (element, bounds) => run.Allocations[element] = bounds);
        foreach (var view in views)
        {
            var rect = run.Allocations.GetValueOrDefault(view.Text);
            view.Bounds = new(left + rect.X, top + rect.Y, rect.Width, rect.Height);
            view.Text.Arrange(view.Bounds);
        }
        var sourceViews = views.ToDictionary(view => (UIElement)view.Text);
        foreach (var band in run.Layout.Lines.Where(line => line.Height > 0))
        {
            var sources = band.Sources.Select(source => (View: sourceViews[source.Element], source.Start, source.End)).ToArray();
            var start = sources.Length > 0 ? sources.Min(source => source.View.Offset + source.Start) : views[0].Offset;
            var end = sources.Length > 0 ? sources.Max(source => source.View.Offset + source.End) : views[0].Offset;
            _lines.Add(new(start, end, top + band.Top, band.Height, false, left, width));
        }
        y = top + result.Bottom;
        if (_collectDocumentClamp)
        {
            // One stable original paragraph key represents the entire anonymous
            // run. Endpoints are shared formatter units, not a proxy's UTF-16 index.
            var lines = run.Layout.ClampContents.Where(content => content.Line is not null).Select(content => content.Line!.Value)
                .Select(line => new TextBlock.DocumentLine(0, line.End, line.Top, line.Height)).ToArray();
            _documentClampParagraphs[views[0].Source] = new(views[0], lines, top);
            _documentClampBoxes[views[0].Source] = new(views[0].Source, default, new(double.NaN, 0, double.PositiveInfinity, 0, false), top, y);
        }
        _fragmentBoxes.Add(new(views[0].Source, firstLine, _lines.Count, top, top, y)
            { AnonymousOwner = ContentsFormattingParent(views[0].Source) });
        views.Clear();
    }

    private (double Min, double Max) DocumentContentsWidths(IReadOnlyList<Paragraph> paragraphs)
    {
        var owner = DocumentContentsOwner(paragraphs[0], intrinsic: true);
        var layout = new TextBlock.CssTextFlowLayout(owner);
        // Probes borrow the original inlines without disturbing a view's retained
        // fragments or adding temporary nodes to either the visual or source tree.
        var texts = paragraphs.Select(paragraph =>
        {
            var text = new ParagraphText(this, paragraph) { CssInlineOuter = true, CssIntrinsicMeasure = true };
            text.PresentDocumentParagraph(paragraph, _document!, _defaultForeground);
            return (UIElement)text;
        }).ToArray();
        try
        {
            var min = layout.FormatRun(texts, new(0, double.PositiveInfinity), 0, true, true, 0, static (_, _) => { }).Width;
            var max = layout.FormatRun(texts, new(double.PositiveInfinity, double.PositiveInfinity), 0, true, true, 0, static (_, _) => { }).Width;
            return (min, max);
        }
        finally
        {
            foreach (var text in texts.Cast<TextBlock>()) text.ReleaseCssInlineAllocation(owner);
        }
    }
}
