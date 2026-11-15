using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private sealed class FirstLineState
    {
        internal double Width = double.PositiveInfinity;
        internal readonly Dictionary<double, int> Limits = [];
        internal int Limit => Limits.TryGetValue(Width, out var limit) ? limit : int.MaxValue;
    }
    private readonly Dictionary<CssFirstLine.Context, FirstLineState> _firstLineStates = [];
    private int _firstLineRevision = -1;
    private bool _hasFirstLine, _findingFirstLine;
    internal bool IsCssTypographyProjection => this is InlineSourceText || IsDocumentParagraph || DocumentFontSource is not null || _wordSpacingStyleSource is not null;

    internal bool HasCssFirstLine
    {
        get
        {
            if (this is InlineSourceText || _findingFirstLine) return false;
            if (_firstLineRevision == CssFirstLine.Revision) return _hasFirstLine;
            _findingFirstLine = true;
            try
            {
                _firstLineRevision = CssFirstLine.Revision;
                var context = CssFirstLine.For(this);
                _hasFirstLine = context is not null || CssFirstLine.HasNestedTargets(_inlines,this);
                foreach (var state in _firstLineStates.Values) state.Limits.Clear();
                if (context is not null) FirstLineStateFor(context);
                if (!_hasFirstLine) ReleaseFirstLineContexts();
                _inlineBoxesDirty = true;
                return _hasFirstLine;
            }
            finally { _findingFirstLine = false; }
        }
    }

    private FirstLineState FirstLineStateFor(CssFirstLine.Context context)
    {
        if (!_firstLineStates.TryGetValue(context, out var state)) _firstLineStates.Add(context, state = new());
        return state;
    }

    private void ReleaseFirstLineContexts(HashSet<CssFirstLine.Context>? used = null)
    {
        foreach (var context in _firstLineStates.Keys.Where(context => used?.Contains(context) != true).ToArray())
        { context.Release(this); _firstLineStates.Remove(context); }
    }

    internal void InvalidateFirstLineStyle()
    {
        foreach (var state in _firstLineStates.Values) state.Limits.Clear();
        InvalidateCaches(); InvalidateMeasure(); InvalidateArrange(); InvalidateVisual();
    }

    internal void InvalidateFirstLineAllocation()
    {
        InvalidateCaches();
        // Column layout can reuse its measured flow before entering the host's
        // content-preparation hook again. Publish the deferred split now, while
        // the previous formatting pass no longer owns the visual traversal.
        EnsureInlineSourceBoxes();
        InvalidateMeasure(); InvalidateArrange(); InvalidateVisual();
    }

    private CssFirstLine.Context? FirstLineContextFor(DependencyObject source)
        => HasCssFirstLine ? CssFirstLine.For(source, this) : null;

    private static DependencyObject FirstLineFormattingRoot(FrameworkElement owner)
    {
        if (owner is ICssStyledBox styled) return styled.StyleSource;
        if (owner is ICssAnonymousBox { Owner: TextBlock text }) return text.DocumentFontSource ?? text;
        if (owner is ICssAnonymousBox { Owner: ContentControl control }) return control;
        return owner is TextBlock block ? block.DocumentFontSource ?? block : owner;
    }

    private void SelectFirstLineConstraint(DependencyObject root, double width, bool prepare)
    {
        if (!HasCssFirstLine) return;
        foreach (var pair in _firstLineStates)
            if (ReferenceEquals(pair.Key.Root.Target, root) && !pair.Value.Width.Equals(width))
            {
                pair.Value.Width = width;
                _inlineBoxesDirty = true;
            }
        if (prepare && _inlineBoxesDirty) EnsureInlineSourceBoxes();
    }

    private void CommitFirstLine(CssFirstLine.Context context, double width, int limit)
    {
        var state = FirstLineStateFor(context);
        if (state.Limits.TryGetValue(width, out var previous) && previous == limit) return;
        if (state.Limits.Count >= 24 && !state.Limits.ContainsKey(width)) state.Limits.Remove(state.Limits.Keys.First());
        state.Limits[width] = limit;
        if (state.Width.Equals(width)) _inlineBoxesDirty = true;
        // The current paragraph still owns its immutable allocations. Rebuild
        // the private formatting tree on the next layout pass, after installation.
        CssFirstLine.ScheduleLayout(this);
    }

    internal sealed partial class CssTextFlowLayout
    {
        private CssFirstLine.Context? _firstLineContext;
        private bool _firstLineMetrics;
        private readonly HashSet<TextBlock> _firstLineOwners = [];
        private readonly HashSet<TextBlock> _firstLineNeedsRebuild = [];
        private readonly HashSet<CssInlineSource> _firstLinePaintSources = [];
        private readonly HashSet<CssInlineFramePiece> _firstLinePaintFrames = [];
        private Rect? _firstLinePaintBounds;

        private void BeginFirstLine()
        {
            _firstLineContext = null; _firstLineMetrics = false; _firstLineOwners.Clear(); _firstLineNeedsRebuild.Clear();
            _firstLinePaintSources.Clear(); _firstLinePaintFrames.Clear(); _firstLinePaintBounds = null;
            var root = FirstLineFormattingRoot(owner);
            var context = CssFirstLine.For(root, null);
            if (context is not null && ReferenceEquals(context.Root.Target, root)) _firstLineContext = context;
        }

        private void PrepareFirstLineSource(UIElement element)
        {
            var text = element is ICssAnonymousTextBox { Owner: TextBlock original } ? original : element as TextBlock;
            if (text is InlineSourceText leaf)
            {
                var context = leaf.FirstLineContext;
                if (context is not null && ReferenceEquals(context.Root.Target, FirstLineFormattingRoot(owner)))
                {
                    _firstLineContext ??= context; _firstLineOwners.Add(leaf.SourceOwner);
                    leaf.SourceOwner.SelectFirstLineConstraint(context.Root.Target, _containing.Width, false);
                    if (!leaf.FirstLineWidth.Equals(_containing.Width)) _firstLineNeedsRebuild.Add(leaf.SourceOwner);
                }
                return;
            }
            if (text is null || !text.HasCssFirstLine) return;
            text.SelectFirstLineConstraint(FirstLineFormattingRoot(owner), _containing.Width,
                !ReferenceEquals(text, owner));
        }

        private FrameworkElement FirstLineMetricsElement(FrameworkElement element)
        {
            if (!_firstLineMetrics || _firstLineContext is not { } context) return element;
            var source = element is ICssStyledBox styled ? styled.StyleSource : FirstLineFormattingRoot(element);
            if (CssFirstLetter.Origin(source) is not null || CssFirstLine.IsProjection(CssNode.Get(source))) return element;
            var found = false;
            for (var node = CssNode.Get(source); node is not null; node = node.FrameworkParent)
                if (ReferenceEquals(node, context.Root)) { found = true; break; }
            return found ? context.MetricsFor(source) : element;
        }

        private void RefreshFirstLineEnvironment()
        {
            if (_firstLineContext is not { } context) return;
            var regions = new List<CssFloatRegion>(_floatSpace?.At(0,_y).Regions ?? []);
            foreach (var item in _floatEvents)
            {
                if (!item.Element.IsMeasureValid)
                    new CssLayoutBox(item.Element,0).MeasureBorderBox(new(_containing.Width,double.PositiveInfinity),_containing);
                var margin = item.Element is FrameworkElement host ? CssBoxMetrics.Margin(host,_containing.Width) : default;
                var size = item.Element.DesiredSize;
                var borderSize = new Size(Math.Max(0,size.Width-margin.Left-margin.Right),Math.Max(0,size.Height-margin.Top-margin.Bottom));
                var shape = CssShapeOutsideProperties.Resolve(item.Element,borderSize,margin,_containing.Width);
                regions.Add(new(CssFloatProperties.ActiveSide(item.Element),item.Position,0,size.Width,size.Height,shape));
            }
            if (!context.NoteEnvironment(_containing.Width,new(regions.ToArray()))) return;
            foreach (var text in _firstLineOwners)
            {
                text.InvalidateFirstLineStyle();
                _firstLineNeedsRebuild.Add(text);
                CssFirstLine.ScheduleLayout(text);
            }
        }

        private void CommitFirstLine(bool firstFormattedLine)
        {
            InstallInlineSelectionGroups();
            InstallFirstLinePaint(firstFormattedLine);
            if (_firstLineContext is not { } context || _firstLineOwners.Count == 0) return;
            if (_firstLineNeedsRebuild.Count > 0)
            {
                foreach (var text in _firstLineNeedsRebuild)
                    CssFirstLine.ScheduleLayout(text);
                return;
            }
            var limits = _firstLineOwners.ToDictionary(text => text, _ => 0);
            if (firstFormattedLine && _lines.FirstOrDefault() is { } first)
                foreach (var (element, _, end) in first.Sources)
                    if (element is InlineSourceText source && ReferenceEquals(source.FirstLineContext, context))
                    {
                        var count = Math.Min(end, source.Text.Length);
                        if (count < source.Text.Length && source.Text[count] is '\r' or '\n') count++;
                        limits[source.SourceOwner] = Math.Max(limits[source.SourceOwner], source.SourceOffset + count);
                    }
            foreach (var pair in limits) pair.Key.CommitFirstLine(context, _containing.Width, pair.Value);
        }

        private void RecordFirstLinePaint(List<CssInlineFragment> fragments, double ascent, bool empty)
        {
            if (!_firstLineMetrics || _firstLineContext is not { } context || fragments.Count == 0) return;
            foreach (var fragment in fragments) _firstLinePaintSources.Add(fragment.Source);
            foreach (var piece in FramePieces(fragments)) _firstLinePaintFrames.Add(piece);
            var metrics = CssFlowProperties.LineMetrics(context.MetricsFor(context.Root.Target));
            var left = fragments.Min(fragment => fragment.FlowLeft);
            var right = fragments.Max(fragment => fragment.FlowRight);
            _firstLinePaintBounds = new(left, _y + ascent - metrics.Baseline, Math.Max(0,right-left), empty ? 0 : metrics.Height);
        }

        private void InstallFirstLinePaint(bool firstFormattedLine)
        {
            if (_intrinsic || !firstFormattedLine) return;
            if (_firstLineContext is not { } context || _firstLinePaintBounds is not { } bounds)
            { CssFirstLinePainter.Set(owner,null); return; }
            var styles = context.RootStyles();
            if (!styles.Any(style => style.Background is not null || (double)style.GetValue(UIElement.OpacityProperty)! < 1))
            { CssFirstLinePainter.Set(owner,null); return; }
            var layers = styles.Select(style =>
            {
                var painter = new InlineSourceBox(style);
                var text = _firstLineOwners.FirstOrDefault();
                if (text is not null) text.MirrorInlineSourceStyle(style,painter);
                else
                {
                    painter.Background = style.Background;
                    painter.Opacity = (double)style.GetValue(UIElement.OpacityProperty)!;
                }
                return new CssFirstLinePainter.Layer(painter,bounds);
            }).ToArray();
            CssFirstLinePainter.Set(owner,new(owner,layers,() => context.HasCurrentLine,
                _sources.Where(source => !source.EmptyFrame).GroupBy(source => (Visual)source.Element)
                    .ToDictionary(group => group.Key,group => group.Any(_firstLinePaintSources.Contains)),
                _frames.ToDictionary(frame => frame.Element,frame => frame.Fragments.Select(_firstLinePaintFrames.Contains).ToArray())));
        }

        private int FirstLineBoundary(List<CssInlineUnit> units, int start, int limit)
        {
            if (!_firstLineMetrics || _firstLineContext is not { } context) return limit;
            for (var index = start; index < limit; index++)
                if (units[index].Source.Text is InlineSourceText source && ReferenceEquals(source.FirstLineContext, context) &&
                    source.SourceOwner.FirstLineStateFor(context).Limits.TryGetValue(_containing.Width, out var end) &&
                    source.SourceOffset + units[index].Start >= end)
                    return index > start ? index : limit;
            return limit;
        }
    }
}
