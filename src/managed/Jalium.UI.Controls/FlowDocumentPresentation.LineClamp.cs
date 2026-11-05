using Jalium.UI.Documents;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed record DocumentClampBox(DependencyObject Source, DocumentBox Box, DocumentHeight Height, double Top, double Bottom);
    private sealed record DocumentClampParagraph(ParagraphView View, TextBlock.DocumentLine[] Lines, double Top);
    private readonly record struct DocumentClampUnit(DependencyObject Source, Paragraph? Paragraph, int Index, int End, double Bottom);
    private readonly record struct DocumentClampRange(DependencyObject Source, int First, int End);
    private sealed class DocumentClampPlan(DependencyObject source)
    {
        internal readonly DependencyObject Source = source;
        internal readonly Dictionary<Paragraph, CssClampParagraphLimit> Limits = [];
        internal readonly HashSet<DependencyObject> Excluded = new(ReferenceEqualityComparer.Instance);
    }
    private readonly Dictionary<DependencyObject, DocumentClampBox> _documentClampBoxes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Paragraph, DocumentClampParagraph> _documentClampParagraphs = [];
    private readonly Dictionary<Paragraph, ParagraphView> _documentClampViews = [];
    private readonly List<DocumentClampPlan> _documentClampPlans = [];
    private readonly HashSet<DependencyObject> _documentClampExcluded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TextBlock, CssClampParagraphLimit> _documentClampLimits = [];
    private readonly Dictionary<TextBlock, CssClampParagraphLimit> _documentSharedClampLimits = [];
    private readonly HashSet<FrameworkElement> _documentClampParticipants = new(ReferenceEqualityComparer.Instance);
    private bool _collectDocumentClamp;
    private bool _hadDocumentClamp;

    private void ClearDocumentClamping()
    {
        foreach (var view in _paragraphs) view.Text.DocumentClampLimit = null;
        _documentClampPlans.Clear(); _documentClampExcluded.Clear(); _documentClampLimits.Clear(); _documentSharedClampLimits.Clear(); _documentClampParticipants.Clear();
        _documentClampViews.Clear(); ClearDocumentClampGeometry(); _hadDocumentClamp = _collectDocumentClamp = false;
    }

    private IEnumerable<DependencyObject> DocumentClampChildren(DependencyObject source) => source switch
    {
        FlowDocument doc => ContentsFlowNodes(doc.Blocks),
        Section section => ContentsFlowNodes(section.Blocks),
        DocList list => ContentsFlowNodes(list.ListItems),
        ListItem item => ContentsFlowNodes(item.Blocks),
        Table table => SourceTableGroups(table).SelectMany(BoxRows).SelectMany(BoxCells),
        TableCell cell => ContentsFlowNodes(CellNodes(cell)),
        _ => [],
    };

    private IEnumerable<DependencyObject> DocumentClampTree(DependencyObject source)
    {
        yield return source;
        foreach (var child in DocumentClampChildren(source))
            foreach (var node in DocumentClampTree(child)) yield return node;
    }

    private static bool IsDocumentClampContainer(DependencyObject source)
    {
        if (CssDisplayProperties.SuppressesBoxTree(source) || source is not (FlowDocument or Paragraph or Section or DocList or ListItem or TableCell) ||
            HasOwnDocumentColumns(source) || source is FlowDocument { ColumnWidth: >= 0 and < double.PositiveInfinity }) return false;
        var display = (CssDisplaySpecification)source.GetValue(CssDisplayProperties.SpecificationProperty)!;
        if (display.None) return false;
        var legacy = CssLineClampProperties.LegacyVertical(source);
        if (display.Mode is not (CssDisplayMode.Native or CssDisplayMode.Block or CssDisplayMode.FlowRoot or CssDisplayMode.TableCell or CssDisplayMode.TableCaption) &&
            !(source is FlowDocument && display.Mode == CssDisplayMode.Contents) && !legacy) return false;
        return source.GetValue(CssLineClampProperties.ContinueProperty) is CssContinue.Collapse ||
            legacy && source.GetValue(CssLineClampProperties.ContinueProperty) is CssContinue.WebkitLegacy;
    }

    private static bool IndependentDocumentClampBox(DependencyObject source) => !DocumentContents(source) && (source is Table or TableCell ||
        IsDocumentClampContainer(source) || HasOwnDocumentColumns(source) ||
        source.GetValue(CssDisplayProperties.SpecificationProperty) is CssDisplaySpecification
            { Mode: CssDisplayMode.FlowRoot or CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table } ||
        CssOverflowProperties.Get(source) is { } overflow &&
            (overflow.X is not (CssOverflowMode.Visible or CssOverflowMode.Clip) || overflow.Y is not (CssOverflowMode.Visible or CssOverflowMode.Clip)) ||
        (bool)source.GetValue(CssMultiColumnProperties.SpanProperty)!);

    private void ClearDocumentClampGeometry()
    {
        _documentClampBoxes.Clear(); _documentClampParagraphs.Clear();
    }

    private void CaptureDocumentClampBox(DependencyObject source, DocumentBox box, DocumentHeight height, double top, double bottom)
    { if (_collectDocumentClamp) _documentClampBoxes[source] = new(source, box, height, top, bottom); }

    private void CaptureDocumentClampParagraph(ParagraphView view, IReadOnlyList<TextBlock.DocumentLine> lines, double top, double height)
    {
        if (!_collectDocumentClamp) return;
        var actual = lines.Count > 0 ? lines.ToArray() : height > 0 ? [new TextBlock.DocumentLine(0, 0, 0, height)] : [];
        _documentClampParagraphs[view.Source] = new(view, actual, top);
    }

    private bool DocumentClampExcluded(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = DocumentBoxParent(current))
            if (_documentClampExcluded.Contains(current)) return true;
        return false;
    }
    private bool DocumentClampTextSuppressed(ParagraphView view) => _documentClampLimits.TryGetValue(view.Text, out var limit) && limit.Count == 0;

    private bool SkipDocumentClampBlock(Block source, ref int index, double y, double width) => SkipDocumentClampItem(source, ref index, y, width);
    private bool SkipDocumentClampItem(DependencyObject source, ref int index, double y, double width)
    {
        if (!DocumentClampExcluded(source)) return false;
        var end = DocumentBoxSourceRange(source).End;
        while (index < _paragraphs.Count && _paragraphs[index].Offset < end)
        {
            var view = _paragraphs[index++];
            view.ClampSuppressed = true; view.Bounds = new(0, y, 0, 0);
            view.Text.InvalidateCssTextLayout(); view.Text.InvalidateMeasure();
            view.Text.Measure(new(Math.Max(0, width), double.PositiveInfinity));
        }
        return true;
    }

    private void LayoutDocumentWithClamping(double width)
    {
        var scopes = DocumentClampTree(_document!).Where(IsDocumentClampContainer).ToArray();
        foreach (var view in _paragraphs) view.Text.DocumentClampLimit = null;
        _documentClampPlans.Clear(); _documentClampExcluded.Clear(); _documentClampLimits.Clear(); _documentSharedClampLimits.Clear(); _documentClampParticipants.Clear();
        _documentClampViews.Clear();
        if (scopes.Length == 0)
        {
            if (_hadDocumentClamp) foreach (var view in _paragraphs) { view.Text.InvalidateCssTextLayout(); view.Text.InvalidateMeasure(); }
            _hadDocumentClamp = false; _collectDocumentClamp = false; LayoutDocumentContent(width); return;
        }
        _hadDocumentClamp = _collectDocumentClamp = true;
        foreach (var view in _paragraphs) _documentClampViews.Add(view.Source, view);
        foreach (var scope in scopes) Participate(scope, true);
        void Participate(DependencyObject source, bool root)
        {
            if (!root && IndependentDocumentClampBox(source)) return;
            if (source is Paragraph paragraph)
            {
                _documentClampParticipants.Add(_documentClampViews[paragraph].Text);
            }
            else foreach (var child in DocumentClampChildren(source)) Participate(child, false);
        }
        static int Depth(DependencyObject source)
        {
            var depth = 0;
            for (var current = (source as FrameworkContentElement)?.Parent; current is not null; current = (current as FrameworkContentElement)?.Parent) depth++;
            return depth;
        }
        void Layout(bool balanced, bool frozen = false)
        {
            MergeDocumentClampPlans();
            using var pass = CssLineClampLayout.BeginDocumentPass(this, _documentClampLimits, _documentClampParticipants, balanced, frozen, _documentSharedClampLimits);
            foreach (var view in _paragraphs) { view.Text.InvalidateCssTextLayout(); view.Text.InvalidateMeasure(); }
            LayoutDocumentContent(width);
        }
        try
        {
            // Resolve inner formatting contexts first. Their retained natural
            // height, including their ellipsis, is an atomic box for outer budgets.
            foreach (var level in scopes.GroupBy(Depth).OrderByDescending(group => group.Key))
            {
                Layout(false);
                foreach (var scope in level)
                    if (PlanDocumentClamp(scope) is { } plan) _documentClampPlans.Add(plan);
            }
            Layout(false);
            var reflow = _documentClampParticipants.Any(text => text.GetValue(CssFlowProperties.TextWrapStyleProperty) is
                CssTextWrapStyle.Balance or CssTextWrapStyle.Pretty or CssTextWrapStyle.AvoidShortLastLine);
            if (reflow)
            {
                Layout(true);
                var changed = false;
                foreach (var scope in scopes.OrderByDescending(Depth))
                {
                    if (scope.GetValue(CssLineClampProperties.MaxLinesProperty) is not CssMaxLines { Auto: true } ||
                        PlanDocumentClamp(scope, frozen: true, heightOnly: true) is not { } earlier) continue;
                    var prior = _documentClampPlans.FirstOrDefault(plan => ReferenceEquals(plan.Source, scope));
                    if (prior is not null) { earlier.Excluded.UnionWith(prior.Excluded); _documentClampPlans.Remove(prior); }
                    _documentClampPlans.Add(earlier); changed = true;
                }
                if (changed) Layout(false, frozen: true);
            }
            // Geometry queries and page arrangements can shape a proxy outside
            // this pass. Keep its retained endpoint until the next document reflow.
            foreach (var view in _paragraphs) view.Text.DocumentClampLimit = _documentClampLimits.GetValueOrDefault(view.Text);
        }
        finally { _collectDocumentClamp = false; }
    }

    private void MergeDocumentClampPlans()
    {
        _documentClampLimits.Clear(); _documentSharedClampLimits.Clear(); _documentClampExcluded.Clear();
        foreach (var plan in _documentClampPlans)
        {
            _documentClampExcluded.UnionWith(plan.Excluded);
            foreach (var pair in plan.Limits)
            {
                var text = _documentClampViews[pair.Key].Text;
                var limits = DocumentContents(pair.Key) ? _documentSharedClampLimits : _documentClampLimits;
                if (!limits.TryGetValue(text, out var prior) || pair.Value.Count < prior.Count) limits[text] = pair.Value;
            }
        }
        foreach (var view in _paragraphs)
            if (DocumentClampExcluded(view.Source)) _documentClampLimits[view.Text] = new(0);
    }

    private DocumentClampPlan? PlanDocumentClamp(DependencyObject scope, bool frozen = false, bool heightOnly = false)
    {
        if (!_documentClampBoxes.TryGetValue(scope, out var owner)) return null;
        var units = new List<DocumentClampUnit>(); var ranges = new List<DocumentClampRange>();
        void Visit(DependencyObject source, bool root)
        {
            if (DocumentClampExcluded(source)) return;
            if (DocumentContents(source) && source is not Paragraph)
            {
                foreach (var child in DocumentClampChildren(source)) Visit(child, false);
                return;
            }
            if (!_documentClampBoxes.TryGetValue(source, out var box)) return;
            var first = units.Count;
            if (!root && IndependentDocumentClampBox(source)) units.Add(new(source, null, 0, 0, box.Bottom));
            else if (source is Paragraph paragraph && _documentClampParagraphs.TryGetValue(paragraph, out var text))
            {
                for (var i = 0; i < text.Lines.Length; i++)
                { var line = text.Lines[i]; units.Add(new(source, paragraph, i, line.End, text.Top + line.Top + line.Height)); }
            }
            else foreach (var child in DocumentClampChildren(source)) Visit(child, false);
            if (units.Count == first && !root && !DocumentContents(source)) units.Add(new(source, null, 0, 0, box.Bottom));
            if (!root && !DocumentContents(source)) ranges.Add(new(source, first, units.Count));
        }
        Visit(scope, true);
        var maximum = (CssMaxLines)scope.GetValue(CssLineClampProperties.MaxLinesProperty)!;
        var height = owner.Height.Used(double.PositiveInfinity);
        var cut = -1; var hasCut = false; var lines = 0;
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (maximum.Auto && PrefixHeight(unit, owner) > height + .000001) { cut = i - 1; hasCut = true; break; }
            if (unit.Paragraph is not null) lines++;
            if (!heightOnly && maximum.Count > 0 && lines == maximum.Count && i + 1 < units.Count) { cut = i; hasCut = true; break; }
        }
        if (!hasCut) return null;
        var plan = new DocumentClampPlan(scope);
        foreach (var unit in units.Skip(cut + 1))
            if (unit.Paragraph is { } paragraph) plan.Limits.TryAdd(paragraph, new(0));
        if (cut >= 0 && units[cut] is { Paragraph: { } last } selected)
            plan.Limits[last] = new(selected.Index + 1) { End = frozen ? selected.End : null };
        foreach (var range in ranges) if (range.First > cut) plan.Excluded.Add(range.Source);
        return plan;
    }

    private double PrefixHeight(DocumentClampUnit unit, DocumentClampBox owner)
    {
        var bottom = unit.Bottom;
        DependencyObject? current = unit.Source;
        if (unit.Paragraph is null)
        {
            if (_documentClampBoxes.TryGetValue(unit.Source, out var atomic)) bottom += atomic.Box.Margin.Bottom;
            current = DocumentBoxParent(current);
        }
        while (current is not null && !ReferenceEquals(current, owner.Source))
        {
            if (!DocumentContents(current) && _documentClampBoxes.TryGetValue(current, out var frame))
            {
                var natural = bottom - frame.Top + frame.Box.Padding.Bottom + frame.Box.Border.Bottom;
                bottom = frame.Top + frame.Height.Used(Math.Max(0, natural)) + frame.Box.Margin.Bottom;
            }
            current = DocumentBoxParent(current);
        }
        return Math.Max(0, bottom - owner.Top + owner.Box.Padding.Bottom + owner.Box.Border.Bottom);
    }
}
