namespace Jalium.UI.Controls;

/// <summary>Main-axis direction of a <see cref="FlexPanel"/>.</summary>
public enum FlexDirection
{
    Row,
    RowReverse,
    Column,
    ColumnReverse,
}

/// <summary>Line-wrapping behavior of a <see cref="FlexPanel"/>.</summary>
public enum FlexWrap
{
    NoWrap,
    Wrap,
    WrapReverse,
}

/// <summary>Main-axis distribution (justify-content).</summary>
public enum FlexJustify
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

/// <summary>Cross-axis alignment, shared by AlignItems and AlignSelf (Auto defers to the container).</summary>
public enum FlexAlign
{
    Auto,
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    Baseline,
    LastBaseline,
}

/// <summary>Multi-line cross-axis distribution (align-content).</summary>
public enum FlexContentAlign
{
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

/// <summary>
/// A CSS Flexbox container: children flow along the main axis, flexible lengths resolve
/// through grow/shrink factors with min/max freezing, lines wrap, and justify/align
/// distribute free space. Attached Grow/Shrink/Basis/AlignSelf/Order configure children.
/// </summary>
public class FlexPanel : Panel
{
    private readonly Panel? _cssLayoutHost;
    public FlexPanel() { }
    internal FlexPanel(Panel cssLayoutHost) => _cssLayoutHost = cssLayoutHost;
    private UIElementCollection LayoutChildren => _cssLayoutHost?.InternalChildren ?? InternalChildren;
    private object? LayoutValue(DependencyProperty property) => (_cssLayoutHost ?? this).GetValue(property);
    internal Size MeasureCssLayout(Size available) => MeasureOverride(available);
    internal Size ArrangeCssLayout(Size finalSize) => ArrangeOverride(finalSize);
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(nameof(Direction), typeof(FlexDirection), typeof(FlexPanel),
            new PropertyMetadata(FlexDirection.Row, OnMeasurePropertyChanged));

    public static readonly DependencyProperty WrapProperty =
        DependencyProperty.Register(nameof(Wrap), typeof(FlexWrap), typeof(FlexPanel),
            new PropertyMetadata(FlexWrap.NoWrap, OnMeasurePropertyChanged));

    public static readonly DependencyProperty JustifyContentProperty =
        DependencyProperty.Register(nameof(JustifyContent), typeof(FlexJustify), typeof(FlexPanel),
            new PropertyMetadata(FlexJustify.FlexStart, OnArrangePropertyChanged));

    public static readonly DependencyProperty AlignItemsProperty =
        DependencyProperty.Register(nameof(AlignItems), typeof(FlexAlign), typeof(FlexPanel),
            new PropertyMetadata(FlexAlign.Stretch, OnMeasurePropertyChanged));

    public static readonly DependencyProperty AlignContentProperty =
        DependencyProperty.Register(nameof(AlignContent), typeof(FlexContentAlign), typeof(FlexPanel),
            new PropertyMetadata(FlexContentAlign.Stretch, OnMeasurePropertyChanged));

    /// <summary>Vertical gap between lines/items (the CSS row-gap).</summary>
    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(FlexPanel),
            new PropertyMetadata(0.0, OnMeasurePropertyChanged));

    /// <summary>Horizontal gap between lines/items (the CSS column-gap).</summary>
    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(FlexPanel),
            new PropertyMetadata(0.0, OnMeasurePropertyChanged));

    public FlexDirection Direction
    {
        get => (FlexDirection)(LayoutValue(DirectionProperty) ?? FlexDirection.Row);
        set => SetValue(DirectionProperty, value);
    }

    public FlexWrap Wrap
    {
        get => (FlexWrap)(LayoutValue(WrapProperty) ?? FlexWrap.NoWrap);
        set => SetValue(WrapProperty, value);
    }

    public FlexJustify JustifyContent
    {
        get => (FlexJustify)(LayoutValue(JustifyContentProperty) ?? FlexJustify.FlexStart);
        set => SetValue(JustifyContentProperty, value);
    }

    public FlexAlign AlignItems
    {
        get => (FlexAlign)(LayoutValue(AlignItemsProperty) ?? FlexAlign.Stretch);
        set => SetValue(AlignItemsProperty, value);
    }

    public FlexContentAlign AlignContent
    {
        get => (FlexContentAlign)(LayoutValue(AlignContentProperty) ?? FlexContentAlign.Stretch);
        set => SetValue(AlignContentProperty, value);
    }

    public double RowSpacing
    {
        get => (double)(LayoutValue(RowSpacingProperty) ?? 0.0);
        set => SetValue(RowSpacingProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)(LayoutValue(ColumnSpacingProperty) ?? 0.0);
        set => SetValue(ColumnSpacingProperty, value);
    }

    // ── Attached item properties ───────────────────────────────────────────────────────

    public static readonly DependencyProperty GrowProperty =
        DependencyProperty.RegisterAttached("Grow", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(0.0, OnItemPropertyChanged), IsFlexFactorValid);

    public static readonly DependencyProperty ShrinkProperty =
        DependencyProperty.RegisterAttached("Shrink", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(1.0, OnItemPropertyChanged), IsFlexFactorValid);

    /// <summary>Main-axis base size in pixels; NaN = auto (the child's natural size).</summary>
    public static readonly DependencyProperty BasisProperty =
        DependencyProperty.RegisterAttached("Basis", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnItemPropertyChanged), FrameworkElement.IsWidthHeightValid);

    internal static readonly DependencyProperty CssBasisProperty =
        DependencyProperty.RegisterAttached("CssBasis", typeof(Jalium.UI.Styling.CssLength?), typeof(FlexPanel),
            new PropertyMetadata(null, OnItemPropertyChanged));

    public static readonly DependencyProperty AlignSelfProperty =
        DependencyProperty.RegisterAttached("AlignSelf", typeof(FlexAlign), typeof(FlexPanel),
            new PropertyMetadata(FlexAlign.Auto, OnItemPropertyChanged));

    public static readonly DependencyProperty OrderProperty =
        DependencyProperty.RegisterAttached("Order", typeof(int), typeof(FlexPanel),
            new PropertyMetadata(0, OnItemPropertyChanged));

    public static double GetGrow(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(GrowProperty) ?? 0.0);
    }

    public static void SetGrow(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(GrowProperty, value);
    }

    public static double GetShrink(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(ShrinkProperty) ?? 1.0);
    }

    public static void SetShrink(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ShrinkProperty, value);
    }

    public static double GetBasis(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(BasisProperty) ?? double.NaN);
    }

    public static void SetBasis(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(BasisProperty, value);
    }

    public static FlexAlign GetAlignSelf(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (FlexAlign)(element.GetValue(AlignSelfProperty) ?? FlexAlign.Auto);
    }

    public static void SetAlignSelf(UIElement element, FlexAlign value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AlignSelfProperty, value);
    }

    public static int GetOrder(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(OrderProperty) ?? 0);
    }

    public static void SetOrder(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(OrderProperty, value);
    }

    private static bool IsFlexFactorValid(object? value)
        => value is double d && !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0;

    private static void OnMeasurePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement panel)
        {
            panel.InvalidateMeasure();
        }
    }

    private static void OnArrangePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement panel)
        {
            panel.InvalidateArrange();
        }
    }

    private static void OnItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement child)
        {
            return;
        }

        var parent = child.VisualParent as Panel;
        if (parent is null && child is FrameworkElement fe)
        {
            parent = fe.Parent as Panel;
        }

        parent?.InvalidateMeasure();
    }

    // ── Layout ─────────────────────────────────────────────────────────────────────────

    private readonly struct FlexAxis
    {
        public readonly bool Horizontal;
        public readonly bool MainReversed;
        public readonly bool CrossReversed;

        public FlexAxis(FlexDirection direction, FlexWrap wrap, FlowDirection flowDirection)
        {
            Horizontal = direction is FlexDirection.Row or FlexDirection.RowReverse;
            var rtl = flowDirection == FlowDirection.RightToLeft;
            MainReversed = Horizontal ? (direction == FlexDirection.RowReverse) != rtl
                : direction == FlexDirection.ColumnReverse;
            CrossReversed = (Horizontal ? false : rtl) != (wrap == FlexWrap.WrapReverse);
        }

        public double Main(in Size size) => Horizontal ? size.Width : size.Height;
        public double Cross(in Size size) => Horizontal ? size.Height : size.Width;
        public Size Size(double main, double cross) => Horizontal ? new Size(main, cross) : new Size(cross, main);
        public Rect Rect(double mainPos, double crossPos, double mainLen, double crossLen)
            => Horizontal ? new Rect(mainPos, crossPos, mainLen, crossLen) : new Rect(crossPos, mainPos, crossLen, mainLen);
        public double MainGap(double rowSpacing, double columnSpacing) => Horizontal ? columnSpacing : rowSpacing;
        public double CrossGap(double rowSpacing, double columnSpacing) => Horizontal ? rowSpacing : columnSpacing;
        public double MinMain(FrameworkElement fe) => Horizontal ? fe.MinWidth : fe.MinHeight;
        public double MaxMain(FrameworkElement fe) => Horizontal ? fe.MaxWidth : fe.MaxHeight;
    }

    private struct FlexItem
    {
        public UIElement Element;
        public double Grow, Shrink;
        public double MinMain, MaxMain;
        public double BaseMain;    // clamped hypothetical main size (margin box)
        public double TargetMain;  // resolved main size (margin box)
        public double OuterCross;  // desired cross size after the final measure
        public double OriginalCross; // first-round cross size for arrange rewrapping
        public double Baseline;    // physical top margin edge to the chosen first/last baseline
        public double OriginalBaseline;
        public double StrutCross;  // original line cross size for visibility:collapse
        public FlexAlign Align;
        public bool Frozen;
        public bool AutoBasis;
        public bool Collapsed;
        public bool AutoMainStart, AutoMainEnd;
        public bool AutoCrossStart, AutoCrossEnd;
    }

    private struct FlexLine
    {
        public int Start, Count, ActiveCount;
        public double UsedMain;    // Σ target + gaps
        public double BaseMain;    // Σ base + gaps
        public double CrossSize;
        public double MeasuredCrossSize;
        public double Baseline;    // first group baseline distance from the line's cross-start edge
        public double LastBaseline; // last group baseline distance from the line's cross-end edge
        public bool HasBaselineGroup;
        public bool HasLastBaselineGroup;
        public double CrossOffset; // filled during arrange
    }

    private FlexItem[]? _items;
    private int _itemCount;
    private bool _hasCollapsedItems;
    private long[]? _orderKeys;
    private FlexLine[]? _lines;
    private int _lineCount;
    private Size _lastLayoutAvailable;

    private static double SanitizeSpacing(double value)
        => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;

    private static (bool MainStart, bool MainEnd, bool CrossStart, bool CrossEnd)
        AutoMargins(FrameworkElement? element, FlexAxis axis)
    {
        if (element is null || element.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty) ||
            element.CssLayout is not { HasMargin: true } margin)
            return default;

        var mainStart = axis.Horizontal
            ? axis.MainReversed ? margin.MarginRight.IsAuto : margin.MarginLeft.IsAuto
            : axis.MainReversed ? margin.MarginBottom.IsAuto : margin.MarginTop.IsAuto;
        var mainEnd = axis.Horizontal
            ? axis.MainReversed ? margin.MarginLeft.IsAuto : margin.MarginRight.IsAuto
            : axis.MainReversed ? margin.MarginTop.IsAuto : margin.MarginBottom.IsAuto;
        var crossStart = axis.Horizontal
            ? axis.CrossReversed ? margin.MarginBottom.IsAuto : margin.MarginTop.IsAuto
            : axis.CrossReversed ? margin.MarginRight.IsAuto : margin.MarginLeft.IsAuto;
        var crossEnd = axis.Horizontal
            ? axis.CrossReversed ? margin.MarginTop.IsAuto : margin.MarginBottom.IsAuto
            : axis.CrossReversed ? margin.MarginLeft.IsAuto : margin.MarginRight.IsAuto;
        return (mainStart, mainEnd, crossStart, crossEnd);
    }

    private static double MeasureBaseline(UIElement element, double outerCross, bool last = false)
    {
        if (element is not FrameworkElement fe) return outerCross;
        var margin = fe.CssLayout is { HasMargin: true } css &&
            !fe.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty)
            ? css.MeasureMarginCache : fe.Margin;
        var explicitBaseline = TextBlock.GetBaselineOffset(element);
        if (double.IsFinite(explicitBaseline)) return margin.Top + explicitBaseline;

        if (Jalium.UI.Styling.CssDisplayLayout.TryGetBaseline(element,
            Math.Max(0, outerCross - margin.Top - margin.Bottom), last, out var childBaseline))
            return margin.Top + childBaseline;

        if (element is TextBlock text)
        {
            var borderHeight = Math.Max(0, outerCross - margin.Top - margin.Bottom);
            var textBaseline = last ? text.GetLastBaselineOffset(borderHeight) : text.GetFirstBaselineOffset(borderHeight);
            if (double.IsFinite(textBaseline)) return margin.Top + textBaseline;
        }

        // An item without a baseline synthesizes one at its border-box end edge.
        return outerCross - margin.Bottom;
    }

    private static void SetLineCrossMetrics(ref FlexLine line, FlexItem[] items,
        bool row, bool collapsedRound, bool originalRound, bool crossReversed)
    {
        double otherCross = 0;
        double largestStart = double.NegativeInfinity;
        double largestEnd = double.NegativeInfinity;
        double lastStart = double.NegativeInfinity;
        double lastEnd = double.NegativeInfinity;
        for (var i = line.Start; i < line.Start + line.Count; i++)
        {
            ref readonly var item = ref items[i];
            if (collapsedRound && item.Collapsed)
            {
                otherCross = Math.Max(otherCross, item.StrutCross);
                continue;
            }

            var cross = originalRound ? item.OriginalCross : item.OuterCross;
            if (row && item.Align is (FlexAlign.Baseline or FlexAlign.LastBaseline) &&
                !item.AutoCrossStart && !item.AutoCrossEnd)
            {
                var topBaseline = originalRound ? item.OriginalBaseline : item.Baseline;
                var start = crossReversed ? cross - topBaseline : topBaseline;
                if (item.Align == FlexAlign.Baseline)
                {
                    largestStart = Math.Max(largestStart, start);
                    largestEnd = Math.Max(largestEnd, cross - start);
                }
                else
                {
                    lastStart = Math.Max(lastStart, start);
                    lastEnd = Math.Max(lastEnd, cross - start);
                }
            }
            else
            {
                otherCross = Math.Max(otherCross, cross);
            }
        }

        line.Baseline = double.IsNegativeInfinity(largestStart) ? 0 : largestStart;
        line.LastBaseline = double.IsNegativeInfinity(lastEnd) ? 0 : lastEnd;
        line.HasBaselineGroup = !double.IsNegativeInfinity(largestStart);
        line.HasLastBaselineGroup = !double.IsNegativeInfinity(lastEnd);
        line.CrossSize = Math.Max(otherCross,
            Math.Max(line.HasBaselineGroup ? largestStart + largestEnd : 0,
                line.HasLastBaselineGroup ? lastStart + lastEnd : 0));
        line.MeasuredCrossSize = line.CrossSize;
    }

    // CSS Flexbox §8.5: expose the startmost/endmost line's alignment baseline to
    // an outer flex container or an inline formatting context. The caller supplies
    // the used border height, so explicit heights and line distribution are included.
    internal double BaselineOffset(double borderHeight, bool last)
    {
        if (_items is null || _lines is null || _lineCount == 0) return double.NaN;
        var owner = _cssLayoutHost ?? this;
        var insets = Jalium.UI.Styling.CssBoxMetrics.ContentInsets(owner,
            owner.CssLayout?.ContainingWidthCache ?? owner.DesiredSize.Width);
        var axis = new FlexAxis(Direction, Wrap, owner.FlowDirection);
        var lineIndex = last ? _lineCount - 1 : 0;
        ref readonly var line = ref _lines[lineIndex];
        var selected = -1;
        for (var i = line.Start; i < line.Start + line.Count; i++)
        {
            if (_items[i].Collapsed) continue;
            selected = i;
            if (!last) break;
        }
        if (selected < 0) return double.NaN;

        if (!axis.Horizontal)
        {
            var mainFinal = Math.Max(0, borderHeight - insets.Top - insets.Bottom);
            var freeMain = mainFinal - line.UsedMain;
            var autoCount = 0;
            if (freeMain > 0)
                for (var i = line.Start; i < line.Start + line.Count; i++)
                {
                    if (_items[i].Collapsed) continue;
                    if (_items[i].AutoMainStart) autoCount++;
                    if (_items[i].AutoMainEnd) autoCount++;
                }
            var autoShare = autoCount > 0 ? freeMain / autoCount : 0;
            var justify = JustifyContent;
            if (freeMain < 0)
                justify = justify switch
                {
                    FlexJustify.SpaceBetween => FlexJustify.FlexStart,
                    FlexJustify.SpaceAround or FlexJustify.SpaceEvenly => FlexJustify.Center,
                    _ => justify,
                };
            var (leading, extra) = ComputeDistribution(justify,
                autoCount > 0 ? 0 : freeMain, line.ActiveCount);
            var rowGap = Jalium.UI.Styling.CssGapProperties.Resolve(owner, true, mainFinal);
            var gap = SanitizeSpacing(rowGap);
            var cursor = leading;
            var arranged = 0;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                ref readonly var item = ref _items[i];
                if (item.Collapsed) continue;
                if (item.AutoMainStart) cursor += autoShare;
                if (i == selected)
                {
                    var top = axis.MainReversed ? mainFinal - cursor - item.TargetMain : cursor;
                    return insets.Top + top + MeasureBaseline(item.Element, item.TargetMain, last);
                }
                arranged++;
                cursor += item.TargetMain + (item.AutoMainEnd ? autoShare : 0) +
                    (arranged < line.ActiveCount ? gap + extra : 0);
            }
            return double.NaN;
        }

        var crossFinal = Math.Max(0, borderHeight - insets.Top - insets.Bottom);
        var crossGap = SanitizeSpacing(Jalium.UI.Styling.CssGapProperties.Resolve(owner, true, crossFinal));
        double lineCross, lineOffset;
        if (_lineCount == 1 && Wrap == FlexWrap.NoWrap)
        {
            lineCross = crossFinal;
            lineOffset = 0;
        }
        else
        {
            var totalCross = crossGap * Math.Max(0, _lineCount - 1);
            for (var i = 0; i < _lineCount; i++) totalCross += _lines[i].MeasuredCrossSize;
            var freeCross = crossFinal - totalCross;
            var stretch = AlignContent == FlexContentAlign.Stretch && freeCross > 0
                ? freeCross / _lineCount : 0;
            if (stretch > 0) freeCross = 0;
            var contentMode = AlignContent switch
            {
                FlexContentAlign.FlexStart or FlexContentAlign.Stretch => FlexJustify.FlexStart,
                FlexContentAlign.FlexEnd => FlexJustify.FlexEnd,
                FlexContentAlign.Center => FlexJustify.Center,
                FlexContentAlign.SpaceBetween => FlexJustify.SpaceBetween,
                FlexContentAlign.SpaceAround => FlexJustify.SpaceAround,
                _ => FlexJustify.SpaceEvenly,
            };
            if (freeCross < 0)
                contentMode = contentMode switch
                {
                    FlexJustify.SpaceBetween => FlexJustify.FlexStart,
                    FlexJustify.SpaceAround or FlexJustify.SpaceEvenly => FlexJustify.Center,
                    _ => contentMode,
                };
            var (leading, extra) = ComputeDistribution(contentMode, freeCross, _lineCount);
            lineCross = line.MeasuredCrossSize + stretch;
            lineOffset = leading;
            for (var i = 0; i < lineIndex; i++)
                lineOffset += _lines[i].MeasuredCrossSize + stretch + crossGap + extra;
            if (axis.CrossReversed) lineOffset = crossFinal - lineOffset - lineCross;
        }
        if (!last && line.HasBaselineGroup)
            return insets.Top + lineOffset + (axis.CrossReversed ? lineCross - line.Baseline : line.Baseline);
        if (last && line.HasLastBaselineGroup)
            return insets.Top + lineOffset + (axis.CrossReversed ? line.LastBaseline : lineCross - line.LastBaseline);
        if (!last && line.HasLastBaselineGroup)
            return insets.Top + lineOffset + (axis.CrossReversed ? line.LastBaseline : lineCross - line.LastBaseline);
        if (last && line.HasBaselineGroup)
            return insets.Top + lineOffset + (axis.CrossReversed ? lineCross - line.Baseline : line.Baseline);

        ref readonly var firstItem = ref _items[selected];
        var itemCross = firstItem.OuterCross;
        double crossPos;
        if (firstItem.AutoCrossStart || firstItem.AutoCrossEnd)
        {
            var free = Math.Max(0, lineCross - itemCross);
            var autoStart = firstItem.AutoCrossStart
                ? firstItem.AutoCrossEnd ? free / 2 : free : 0;
            crossPos = axis.CrossReversed ? lineOffset + lineCross - itemCross - autoStart
                : lineOffset + autoStart;
        }
        else crossPos = firstItem.Align switch
        {
            FlexAlign.Stretch => lineOffset,
            FlexAlign.Center => lineOffset + (lineCross - itemCross) / 2,
            FlexAlign.FlexEnd or FlexAlign.LastBaseline => axis.CrossReversed ? lineOffset : lineOffset + lineCross - itemCross,
            _ => axis.CrossReversed ? lineOffset + lineCross - itemCross : lineOffset,
        };
        var usedCross = firstItem.Align == FlexAlign.Stretch &&
            !firstItem.AutoCrossStart && !firstItem.AutoCrossEnd ? lineCross : itemCross;
        return insets.Top + crossPos + MeasureBaseline(firstItem.Element, usedCross, last);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = _cssLayoutHost ?? this;
        var insets = Jalium.UI.Styling.CssBoxMetrics.ContentInsets(owner,
            owner.CssLayout?.ContainingWidthCache ?? availableSize.Width);
        var contentAvailable = Jalium.UI.Styling.CssBoxMetrics.InnerSize(availableSize, insets);
        var axis = new FlexAxis(Direction, Wrap, owner.FlowDirection);
        var mainAvailable = axis.Main(contentAvailable);
        var crossAvailable = axis.Cross(contentAvailable);
        var rowGap = Jalium.UI.Styling.CssGapProperties.Resolve(owner, true, contentAvailable.Height);
        var columnGap = Jalium.UI.Styling.CssGapProperties.Resolve(owner, false, contentAvailable.Width);
        var mainGap = SanitizeSpacing(axis.MainGap(rowGap, columnGap));
        var crossGap = SanitizeSpacing(axis.CrossGap(rowGap, columnGap));
        var alignItems = AlignItems;
        if (alignItems == FlexAlign.Auto)
        {
            alignItems = FlexAlign.Stretch;
        }

        CollectItems(axis, mainAvailable, crossAvailable, alignItems);
        BreakLinesPreservingItems(mainAvailable, mainGap, collapsedRound: false);

        var items = _items!;
        var lines = _lines!;
        for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
        {
            ref var line = ref lines[lineIndex];
            ResolveFlexibleLengths(ref line, mainAvailable, mainGap, collapsedRound: false);

            // Final measure at the resolved main size; the cross constraint is the panel's.
            double usedMain = 0;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                ref var item = ref items[i];
                // Auto-basis items that did not flex keep their natural measure; everything
                // else re-measures at the resolved size (same-constraint calls short-circuit
                // inside UIElement.Measure).
                if (!item.AutoBasis || Math.Abs(item.TargetMain - item.BaseMain) > 0.001)
                {
                    item.Element.Measure(axis.Size(item.TargetMain, crossAvailable));
                }

                item.OuterCross = axis.Cross(item.Element.DesiredSize);
                item.OriginalCross = item.OuterCross;
                if (axis.Horizontal && item.Align is FlexAlign.Baseline or FlexAlign.LastBaseline)
                    item.OriginalBaseline = item.Baseline = MeasureBaseline(item.Element, item.OuterCross,
                        item.Align == FlexAlign.LastBaseline);
                usedMain += item.TargetMain;
            }

            line.UsedMain = usedMain + mainGap * Math.Max(0, line.Count - 1);
            SetLineCrossMetrics(ref line, items, axis.Horizontal, collapsedRound: false,
                originalRound: false, crossReversed: axis.CrossReversed);
        }

        if (_hasCollapsedItems)
        {
            // The strut retains the cross size of its original line, not merely
            // the collapsed item's own cross size. Rewrap after recording it.
            for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
            {
                ref readonly var line = ref lines[lineIndex];
                for (var i = line.Start; i < line.Start + line.Count; i++)
                    if (items[i].Collapsed) items[i].StrutCross = line.CrossSize;
            }

            BreakLinesPreservingItems(mainAvailable, mainGap, collapsedRound: true);
            for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
            {
                ref var line = ref lines[lineIndex];
                ResolveFlexibleLengths(ref line, mainAvailable, mainGap, collapsedRound: true);
                double usedMain = 0;
                for (var i = line.Start; i < line.Start + line.Count; i++)
                {
                    ref var item = ref items[i];
                    if (item.Collapsed) continue;
                    if (!item.AutoBasis || Math.Abs(item.TargetMain - item.BaseMain) > 0.001)
                        item.Element.Measure(axis.Size(item.TargetMain, crossAvailable));
                    item.OuterCross = axis.Cross(item.Element.DesiredSize);
                    if (axis.Horizontal && item.Align is FlexAlign.Baseline or FlexAlign.LastBaseline)
                        item.Baseline = MeasureBaseline(item.Element, item.OuterCross,
                            item.Align == FlexAlign.LastBaseline);
                    usedMain += item.TargetMain;
                }
                line.UsedMain = usedMain + mainGap * Math.Max(0, line.ActiveCount - 1);
                SetLineCrossMetrics(ref line, items, axis.Horizontal, collapsedRound: true,
                    originalRound: false, crossReversed: axis.CrossReversed);
            }
        }

        // Desired size follows the content-based principle: growth never inflates it,
        // shrink reports the shrunken size, frozen mins report the true overflow.
        double desiredMain = 0;
        double desiredCross = 0;
        for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
        {
            ref readonly var line = ref lines[lineIndex];
            desiredMain = Math.Max(desiredMain, Math.Min(line.BaseMain, line.UsedMain));
            desiredCross += line.CrossSize;
        }

        if (_lineCount > 1)
        {
            desiredCross += crossGap * (_lineCount - 1);
        }

        owner.MeasureCssLayoutAbsoluteChildren(availableSize);

        _lastLayoutAvailable = contentAvailable;
        var desired = axis.Size(desiredMain, desiredCross);
        return new Size(desired.Width + insets.Left + insets.Right,
            desired.Height + insets.Top + insets.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = _cssLayoutHost ?? this;
        var insets = Jalium.UI.Styling.CssBoxMetrics.ContentInsets(owner,
            owner.CssLayout?.ContainingWidthCache ?? finalSize.Width);
        var contentFinal = Jalium.UI.Styling.CssBoxMetrics.InnerSize(finalSize, insets);
        var axis = new FlexAxis(Direction, Wrap, owner.FlowDirection);
        var mainFinal = axis.Main(contentFinal);
        var crossFinal = axis.Cross(contentFinal);
        var rowGap = Jalium.UI.Styling.CssGapProperties.Resolve(owner, true, contentFinal.Height);
        var columnGap = Jalium.UI.Styling.CssGapProperties.Resolve(owner, false, contentFinal.Width);
        var mainGap = SanitizeSpacing(axis.MainGap(rowGap, columnGap));
        var crossGap = SanitizeSpacing(axis.CrossGap(rowGap, columnGap));

        if (_items is null || _lines is null || _itemCount == 0)
        {
            owner.ArrangeCssLayoutAbsoluteChildren(finalSize);
            _lastLayoutAvailable = contentFinal;
            return finalSize;
        }

        // Alignment and track allocation can change the available box after Measure.
        // Re-resolve flex lengths and remeasure at the final constraints: wrapped
        // content and percentage-sized descendants can change their cross size.
        var mainChanged = Math.Abs(mainFinal - axis.Main(_lastLayoutAvailable)) > 0.001;
        var crossChanged = Math.Abs(crossFinal - axis.Cross(_lastLayoutAvailable)) > 0.001;
        var constraintChanged = mainChanged || crossChanged;
        if (_hasCollapsedItems || constraintChanged)
        {
            var crossConstraint = crossFinal;
            if (constraintChanged)
            {
                // Percentage and math bases use the container's final inner main
                // size. A basis that becomes indefinite falls back to content.
                for (var i = 0; i < _itemCount; i++)
                {
                    ref var item = ref _items[i];
                    var basis = ResolveBasis(item.Element, item.Element as FrameworkElement, mainFinal);
                    item.AutoBasis = double.IsNaN(basis);
                    if (item.AutoBasis)
                    {
                        item.Element.Measure(axis.Size(double.PositiveInfinity, crossConstraint));
                        basis = axis.Main(item.Element.DesiredSize);
                    }
                    item.BaseMain = Math.Clamp(basis, item.MinMain, item.MaxMain);
                }
            }
            if (_hasCollapsedItems)
            {
                BreakLinesPreservingItems(mainFinal, mainGap, collapsedRound: false);
                double originalCross = crossGap * Math.Max(0, _lineCount - 1);
                for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
                {
                    ref var line = ref _lines![lineIndex];
                    ResolveFlexibleLengths(ref line, mainFinal, mainGap, collapsedRound: false);
                    for (var i = line.Start; i < line.Start + line.Count; i++)
                    {
                        if (constraintChanged)
                        {
                            _items[i].Element.Measure(axis.Size(_items[i].TargetMain, crossConstraint));
                            _items[i].OriginalCross = axis.Cross(_items[i].Element.DesiredSize);
                            if (axis.Horizontal && _items[i].Align is FlexAlign.Baseline or FlexAlign.LastBaseline)
                                _items[i].OriginalBaseline = MeasureBaseline(
                                    _items[i].Element, _items[i].OriginalCross,
                                    _items[i].Align == FlexAlign.LastBaseline);
                        }
                    }
                    SetLineCrossMetrics(ref line, _items, axis.Horizontal, collapsedRound: false,
                        originalRound: true, crossReversed: axis.CrossReversed);
                    originalCross += line.CrossSize;
                }

                // The original round performs cross-axis stretch before recording
                // struts. Apply the final container's used cross size here, then
                // let the collapsed round distribute its own remaining space.
                var originalStretch = double.IsFinite(crossFinal) && Wrap != FlexWrap.NoWrap &&
                    AlignContent == FlexContentAlign.Stretch && crossFinal > originalCross
                    ? (crossFinal - originalCross) / _lineCount : 0;
                for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
                {
                    ref readonly var line = ref _lines![lineIndex];
                    var strut = Wrap == FlexWrap.NoWrap && double.IsFinite(crossFinal)
                        ? crossFinal : line.CrossSize + originalStretch;
                    for (var i = line.Start; i < line.Start + line.Count; i++)
                        if (_items[i].Collapsed) _items[i].StrutCross = strut;
                }
            }
            BreakLinesPreservingItems(mainFinal, mainGap, collapsedRound: _hasCollapsedItems);
            for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
            {
                ref var line = ref _lines![lineIndex];
                ResolveFlexibleLengths(ref line, mainFinal, mainGap, collapsedRound: _hasCollapsedItems);
                double usedMain = 0;
                for (var i = line.Start; i < line.Start + line.Count; i++)
                {
                    if (_hasCollapsedItems && _items[i].Collapsed) continue;
                    if (constraintChanged)
                    {
                        _items[i].Element.Measure(axis.Size(_items[i].TargetMain, crossConstraint));
                        _items[i].OuterCross = axis.Cross(_items[i].Element.DesiredSize);
                        if (axis.Horizontal && _items[i].Align is FlexAlign.Baseline or FlexAlign.LastBaseline)
                            _items[i].Baseline = MeasureBaseline(_items[i].Element, _items[i].OuterCross,
                                _items[i].Align == FlexAlign.LastBaseline);
                    }
                    usedMain += _items[i].TargetMain;
                }

                line.UsedMain = usedMain + mainGap * Math.Max(0, line.ActiveCount - 1);
                SetLineCrossMetrics(ref line, _items, axis.Horizontal,
                    collapsedRound: _hasCollapsedItems, originalRound: false,
                    crossReversed: axis.CrossReversed);
            }
        }

        var items = _items;
        var lines = _lines!;

        // align-content: distribute lines along the cross axis.
        double crossCursor = 0;
        double crossExtra = 0;
        if (_lineCount == 1 && Wrap == FlexWrap.NoWrap)
        {
            lines[0].CrossSize = crossFinal; // single nowrap line fills the container cross axis
            lines[0].CrossOffset = 0;
        }
        else
        {
            double totalCross = crossGap * Math.Max(0, _lineCount - 1);
            for (var i = 0; i < _lineCount; i++)
            {
                totalCross += lines[i].CrossSize;
            }

            var freeCross = crossFinal - totalCross;
            if (AlignContent == FlexContentAlign.Stretch && freeCross > 0 && _lineCount > 0)
            {
                var add = freeCross / _lineCount;
                for (var i = 0; i < _lineCount; i++)
                {
                    lines[i].CrossSize += add;
                }

                freeCross = 0;
            }

            var contentMode = AlignContent switch
            {
                FlexContentAlign.FlexStart or FlexContentAlign.Stretch => FlexJustify.FlexStart,
                FlexContentAlign.FlexEnd => FlexJustify.FlexEnd,
                FlexContentAlign.Center => FlexJustify.Center,
                FlexContentAlign.SpaceBetween => FlexJustify.SpaceBetween,
                FlexContentAlign.SpaceAround => FlexJustify.SpaceAround,
                _ => FlexJustify.SpaceEvenly,
            };
            if (freeCross < 0)
            {
                contentMode = contentMode switch
                {
                    FlexJustify.SpaceBetween => FlexJustify.FlexStart,
                    FlexJustify.SpaceAround or FlexJustify.SpaceEvenly => FlexJustify.Center,
                    _ => contentMode,
                };
            }

            (crossCursor, crossExtra) = ComputeDistribution(contentMode, freeCross, _lineCount);

            var offset = crossCursor;
            for (var i = 0; i < _lineCount; i++)
            {
                lines[i].CrossOffset = offset;
                offset += lines[i].CrossSize + crossGap + crossExtra;
            }

            if (axis.CrossReversed)
            {
                // Mirror the line stack across the cross axis.
                for (var i = 0; i < _lineCount; i++)
                {
                    lines[i].CrossOffset = crossFinal - lines[i].CrossOffset - lines[i].CrossSize;
                }
            }
        }

        // Per line: justify along the main axis, align within the line.
        for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
        {
            ref readonly var line = ref lines[lineIndex];
            var freeMain = mainFinal - line.UsedMain;
            var autoMainMargins = 0;
            if (freeMain > 0)
                for (var i = line.Start; i < line.Start + line.Count; i++)
                {
                    if (_hasCollapsedItems && items[i].Collapsed) continue;
                    if (items[i].AutoMainStart) autoMainMargins++;
                    if (items[i].AutoMainEnd) autoMainMargins++;
                }
            var autoMainShare = autoMainMargins > 0 ? freeMain / autoMainMargins : 0;
            var justify = JustifyContent;
            if (freeMain < 0)
            {
                justify = justify switch
                {
                    FlexJustify.SpaceBetween => FlexJustify.FlexStart,
                    FlexJustify.SpaceAround or FlexJustify.SpaceEvenly => FlexJustify.Center,
                    _ => justify,
                };
            }

            var (leading, extra) = ComputeDistribution(justify,
                autoMainMargins > 0 ? 0 : freeMain, line.ActiveCount);
            var cursor = leading;
            var arranged = 0;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                ref readonly var item = ref items[i];
                if (_hasCollapsedItems && item.Collapsed)
                {
                    item.Element.Arrange(new Rect(insets.Left, insets.Top, 0, 0));
                    continue;
                }
                if (item.AutoMainStart) cursor += autoMainShare;
                var outerMain = item.TargetMain;
                var mainPos = axis.MainReversed ? mainFinal - cursor - outerMain : cursor;

                double crossPos;
                double crossLen;
                var crossReversed = axis.CrossReversed;
                if (item.AutoCrossStart || item.AutoCrossEnd)
                {
                    var freeItemCross = Math.Max(0, line.CrossSize - item.OuterCross);
                    var autoCrossStart = item.AutoCrossStart
                        ? item.AutoCrossEnd ? freeItemCross / 2 : freeItemCross : 0;
                    crossPos = crossReversed
                        ? line.CrossOffset + line.CrossSize - item.OuterCross - autoCrossStart
                        : line.CrossOffset + autoCrossStart;
                    crossLen = item.OuterCross;
                }
                else switch (item.Align)
                {
                    case FlexAlign.Stretch:
                        crossPos = line.CrossOffset;
                        crossLen = line.CrossSize;
                        break;
                    case FlexAlign.Center:
                        crossPos = line.CrossOffset + (line.CrossSize - item.OuterCross) / 2;
                        crossLen = item.OuterCross;
                        break;
                    case FlexAlign.FlexEnd:
                        crossPos = crossReversed ? line.CrossOffset
                            : line.CrossOffset + line.CrossSize - item.OuterCross;
                        crossLen = item.OuterCross;
                        break;
                    case FlexAlign.LastBaseline when !axis.Horizontal:
                        crossPos = crossReversed ? line.CrossOffset
                            : line.CrossOffset + line.CrossSize - item.OuterCross;
                        crossLen = item.OuterCross;
                        break;
                    case FlexAlign.Baseline when axis.Horizontal:
                        crossPos = crossReversed
                            ? line.CrossOffset + line.CrossSize - line.Baseline - item.Baseline
                            : line.CrossOffset + line.Baseline - item.Baseline;
                        crossLen = item.OuterCross;
                        break;
                    case FlexAlign.LastBaseline when axis.Horizontal:
                        crossPos = crossReversed
                            ? line.CrossOffset + line.LastBaseline - item.Baseline
                            : line.CrossOffset + line.CrossSize - line.LastBaseline - item.Baseline;
                        crossLen = item.OuterCross;
                        break;
                    default: // FlexStart (Auto resolved earlier)
                        crossPos = crossReversed
                            ? line.CrossOffset + line.CrossSize - item.OuterCross
                            : line.CrossOffset;
                        crossLen = item.OuterCross;
                        break;
                }

                var m0 = FrameworkElement.SnapLayoutValue(mainPos);
                var m1 = FrameworkElement.SnapLayoutValue(mainPos + outerMain);
                var c0 = FrameworkElement.SnapLayoutValue(crossPos);
                var c1 = FrameworkElement.SnapLayoutValue(crossPos + crossLen);
                var slot = axis.Rect(m0, c0, Math.Max(0, m1 - m0), Math.Max(0, c1 - c0));
                var alignment = item.Align == FlexAlign.Stretch &&
                    !item.AutoCrossStart && !item.AutoCrossEnd
                        ? Jalium.UI.Styling.CssBoxAlignment.Stretch
                        : Jalium.UI.Styling.CssBoxAlignment.Start;
                Jalium.UI.Styling.CssLayoutBox.ArrangeWithAlignment(item.Element,
                    new Rect(slot.X + insets.Left, slot.Y + insets.Top, slot.Width, slot.Height),
                    axis.Horizontal ? Jalium.UI.Styling.CssBoxAlignment.Stretch : alignment,
                    axis.Horizontal ? alignment : Jalium.UI.Styling.CssBoxAlignment.Stretch);

                arranged++;
                cursor += outerMain + (item.AutoMainEnd ? autoMainShare : 0) +
                    (arranged < line.ActiveCount ? mainGap + extra : 0);
            }
        }

        owner.ArrangeCssLayoutAbsoluteChildren(finalSize);
        _lastLayoutAvailable = contentFinal;
        return finalSize;
    }

    /// <summary>
    /// Distribution offsets for justify-content/align-content. Negative free space keeps
    /// the unsafe FlexStart/Center/FlexEnd behavior (overflow allowed); Space* modes are
    /// pre-degraded by the callers per the CSS fallback rules.
    /// </summary>
    private static (double Leading, double Extra) ComputeDistribution(FlexJustify mode, double free, int count)
    {
        if (count <= 0 || free <= 0)
        {
            return (mode == FlexJustify.FlexEnd ? free : mode == FlexJustify.Center ? free / 2 : 0, 0);
        }

        return mode switch
        {
            FlexJustify.FlexEnd => (free, 0),
            FlexJustify.Center => (free / 2, 0),
            FlexJustify.SpaceBetween => count > 1 ? (0.0, free / (count - 1)) : (0.0, 0.0),
            FlexJustify.SpaceAround => (free / (2 * count), free / count),
            FlexJustify.SpaceEvenly => (free / (count + 1), free / (count + 1)),
            _ => (0, 0),
        };
    }

    private void CollectItems(FlexAxis axis, double mainAvailable, double crossAvailable, FlexAlign alignItems)
    {
        var childCount = LayoutChildren.Count;
        if (_items is null || _items.Length < childCount)
        {
            _items = new FlexItem[Math.Max(4, childCount)];
            _orderKeys = new long[_items.Length];
        }

        _itemCount = 0;
        _hasCollapsedItems = false;
        var index = 0;
        foreach (var child in LayoutChildren.EnumerateStruct())
        {
            index++;
            if (child.Visibility == Visibility.Collapsed || IsCssAbsolute(child))
            {
                continue;
            }

            var fe = child as FrameworkElement;
            var basis = ResolveBasis(child, fe, mainAvailable);
            var autoBasis = double.IsNaN(basis);
            var minMain = fe is null ? 0 : axis.MinMain(fe);
            var maxMain = fe is null ? double.PositiveInfinity : axis.MaxMain(fe);

            double baseMain;
            if (autoBasis)
            {
                // Natural size: measure with an unconstrained main axis (WrapPanel model).
                child.Measure(axis.Size(double.PositiveInfinity, crossAvailable));
                baseMain = axis.Main(child.DesiredSize);
            }
            else
            {
                baseMain = basis;
            }

            var align = GetAlignSelf(child);
            if (align == FlexAlign.Auto)
            {
                align = alignItems;
            }

            var (autoMainStart, autoMainEnd, autoCrossStart, autoCrossEnd) =
                AutoMargins(fe, axis);

            _items[_itemCount] = new FlexItem
            {
                Element = child,
                Grow = GetGrow(child),
                Shrink = GetShrink(child),
                MinMain = minMain,
                MaxMain = maxMain,
                BaseMain = Math.Clamp(baseMain, minMain, maxMain),
                Align = align,
                AutoBasis = autoBasis,
                Collapsed = Jalium.UI.Styling.CssDisplayProperties.IsCollapsedFlexItem(child),
                AutoMainStart = autoMainStart,
                AutoMainEnd = autoMainEnd,
                AutoCrossStart = autoCrossStart,
                AutoCrossEnd = autoCrossEnd,
            };
            _hasCollapsedItems |= _items[_itemCount].Collapsed;
            _orderKeys![_itemCount] = ((long)GetOrder(child) << 32) | (uint)(index - 1);
            _itemCount++;
        }

        // Stable order sort: same Order keeps document order via the low 32 bits.
        Array.Sort(_orderKeys!, _items, 0, _itemCount);
    }

    private static double ResolveBasis(UIElement child, FrameworkElement? fe, double mainAvailable)
    {
        var basis = GetBasis(child);
        if (child.HasLocalValue(BasisProperty) ||
            child.GetValue(CssBasisProperty) is not Jalium.UI.Styling.CssLength cssBasis ||
            fe is null)
            return basis;

        var context = Jalium.UI.Styling.CssEngine.BuildLengthContext(fe);
        if (cssBasis.UsesPercent && !double.IsFinite(mainAvailable)) return double.NaN;
        if (cssBasis.Expression is { } expression)
            return expression.TryEvaluate(context, mainAvailable, out var calculated)
                ? Math.Max(0, calculated) : double.NaN;
        if (cssBasis.Unit == Jalium.UI.Styling.CssUnit.Percent)
            return Math.Max(0, cssBasis.Value / 100 * mainAvailable);
        return cssBasis.TryResolve(context, Jalium.UI.Styling.CssPercentBasis.NotSupported, out var pixels)
            ? Math.Max(0, pixels) : double.NaN;
    }

    private void BreakLinesPreservingItems(double mainAvailable, double mainGap, bool collapsedRound)
    {
        if (_lines is null || _lines.Length < Math.Max(1, _itemCount))
        {
            _lines = new FlexLine[Math.Max(4, _itemCount)];
        }

        _lineCount = 0;
        if (_itemCount == 0)
        {
            return;
        }

        var wrap = Wrap != FlexWrap.NoWrap && !double.IsInfinity(mainAvailable);
        var items = _items!;
        var lineStart = 0;
        double running = 0;
        var countOnLine = 0;
        var activeOnLine = 0;
        for (var i = 0; i < _itemCount; i++)
        {
            var collapsed = collapsedRound && items[i].Collapsed;
            var outer = collapsed ? 0 : items[i].BaseMain;
            var prospective = collapsed ? running : running + (activeOnLine > 0 ? mainGap : 0) + outer;
            if (wrap && countOnLine > 0 && !collapsed && prospective > mainAvailable + 0.001)
            {
                _lines[_lineCount++] = new FlexLine { Start = lineStart, Count = countOnLine,
                    ActiveCount = activeOnLine, BaseMain = running };
                lineStart = i;
                running = outer;
                countOnLine = 1;
                activeOnLine = 1;
            }
            else
            {
                running = prospective;
                countOnLine++;
                if (!collapsed) activeOnLine++;
            }
        }

        _lines[_lineCount++] = new FlexLine { Start = lineStart, Count = countOnLine,
            ActiveCount = activeOnLine, BaseMain = running };
    }

    /// <summary>CSS Flexbox §9.7: resolve flexible lengths with min/max violation freezing.</summary>
    private void ResolveFlexibleLengths(ref FlexLine line, double mainAvailable, double mainGap, bool collapsedRound)
    {
        var items = _items!;
        var start = line.Start;
        var end = line.Start + line.Count;

        if (collapsedRound)
            for (var i = start; i < end; i++)
                if (items[i].Collapsed) items[i].TargetMain = 0;

        if (double.IsInfinity(mainAvailable))
        {
            for (var i = start; i < end; i++)
            {
                if (collapsedRound && items[i].Collapsed) continue;
                items[i].TargetMain = items[i].BaseMain;
            }

            return;
        }

        var lineAvailable = mainAvailable - mainGap * Math.Max(0, line.ActiveCount - 1);
        double sumBase = 0;
        for (var i = start; i < end; i++)
        {
            if (collapsedRound && items[i].Collapsed) continue;
            sumBase += items[i].BaseMain;
        }

        var growing = sumBase < lineAvailable;

        double initialFree = lineAvailable;
        for (var i = start; i < end; i++)
        {
            ref var item = ref items[i];
            if (collapsedRound && item.Collapsed) continue;
            item.Frozen = growing ? item.Grow <= 0 : item.Shrink <= 0;
            item.TargetMain = item.BaseMain;
            if (item.Frozen)
            {
                initialFree -= item.TargetMain;
            }
            else
            {
                initialFree -= item.BaseMain;
            }
        }

        for (var iteration = 0; iteration < line.Count + 1; iteration++)
        {
            double frozenTotal = 0;
            double unfrozenBase = 0;
            double sumFactors = 0;
            double sumScaled = 0;
            var anyUnfrozen = false;
            for (var i = start; i < end; i++)
            {
                ref readonly var item = ref items[i];
                if (collapsedRound && item.Collapsed) continue;
                if (item.Frozen)
                {
                    frozenTotal += item.TargetMain;
                }
                else
                {
                    anyUnfrozen = true;
                    unfrozenBase += item.BaseMain;
                    sumFactors += growing ? item.Grow : item.Shrink;
                    sumScaled += item.Shrink * item.BaseMain;
                }
            }

            if (!anyUnfrozen)
            {
                break;
            }

            var remainingFree = lineAvailable - frozenTotal - unfrozenBase;
            if (sumFactors < 1 && Math.Abs(initialFree * sumFactors) < Math.Abs(remainingFree))
            {
                remainingFree = initialFree * sumFactors;
            }

            if (growing)
            {
                if (remainingFree <= 0 || sumFactors <= 0)
                {
                    break; // all remaining stay at base
                }

                for (var i = start; i < end; i++)
                {
                    ref var item = ref items[i];
                    if (collapsedRound && item.Collapsed) continue;
                    if (!item.Frozen)
                    {
                        item.TargetMain = item.BaseMain + remainingFree * item.Grow / sumFactors;
                    }
                }
            }
            else
            {
                if (remainingFree >= 0 || sumScaled <= 0)
                {
                    break;
                }

                for (var i = start; i < end; i++)
                {
                    ref var item = ref items[i];
                    if (collapsedRound && item.Collapsed) continue;
                    if (!item.Frozen)
                    {
                        item.TargetMain = item.BaseMain + remainingFree * (item.Shrink * item.BaseMain) / sumScaled;
                    }
                }
            }

            double totalViolation = 0;
            for (var i = start; i < end; i++)
            {
                ref var item = ref items[i];
                if (collapsedRound && item.Collapsed) continue;
                if (item.Frozen)
                {
                    continue;
                }

                var clamped = Math.Clamp(item.TargetMain, item.MinMain, item.MaxMain);
                totalViolation += clamped - item.TargetMain;
                item.TargetMain = clamped;
            }

            if (Math.Abs(totalViolation) < 0.001)
            {
                break; // done — everything fits within its bounds
            }

            for (var i = start; i < end; i++)
            {
                ref var item = ref items[i];
                if (collapsedRound && item.Collapsed) continue;
                if (item.Frozen)
                {
                    continue;
                }

                if (totalViolation > 0 && item.TargetMain <= item.MinMain + 0.0005)
                {
                    item.Frozen = true;
                }
                else if (totalViolation < 0 && item.TargetMain >= item.MaxMain - 0.0005)
                {
                    item.Frozen = true;
                }
            }
        }

        foreach (ref var item in items.AsSpan(start, line.Count))
        {
            if (collapsedRound && item.Collapsed) continue;
            if (!double.IsFinite(item.TargetMain) || item.TargetMain < 0)
            {
                item.TargetMain = Math.Clamp(item.BaseMain, item.MinMain, item.MaxMain);
            }
        }
    }
}
