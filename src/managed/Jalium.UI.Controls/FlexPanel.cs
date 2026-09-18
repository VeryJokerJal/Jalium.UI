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

        public FlexAxis(FlexDirection direction)
        {
            Horizontal = direction is FlexDirection.Row or FlexDirection.RowReverse;
            MainReversed = direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;
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
        public FlexAlign Align;
        public bool Frozen;
        public bool AutoBasis;
    }

    private struct FlexLine
    {
        public int Start, Count;
        public double UsedMain;    // Σ target + gaps
        public double BaseMain;    // Σ base + gaps
        public double CrossSize;
        public double CrossOffset; // filled during arrange
    }

    private FlexItem[]? _items;
    private int _itemCount;
    private long[]? _orderKeys;
    private FlexLine[]? _lines;
    private int _lineCount;
    private Size _measuredAvailable;

    private static double SanitizeSpacing(double value)
        => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;

    protected override Size MeasureOverride(Size availableSize)
    {
        var axis = new FlexAxis(Direction);
        var mainAvailable = axis.Main(availableSize);
        var crossAvailable = axis.Cross(availableSize);
        var rowGap = Jalium.UI.Styling.CssGapProperties.Resolve(_cssLayoutHost ?? this, true, availableSize.Height);
        var columnGap = Jalium.UI.Styling.CssGapProperties.Resolve(_cssLayoutHost ?? this, false, availableSize.Width);
        var mainGap = SanitizeSpacing(axis.MainGap(rowGap, columnGap));
        var crossGap = SanitizeSpacing(axis.CrossGap(rowGap, columnGap));
        var alignItems = AlignItems;
        if (alignItems == FlexAlign.Auto)
        {
            alignItems = FlexAlign.Stretch;
        }

        CollectItems(axis, mainAvailable, crossAvailable, alignItems);
        BreakLinesPreservingItems(mainAvailable, mainGap);

        var items = _items!;
        var lines = _lines!;
        for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
        {
            ref var line = ref lines[lineIndex];
            ResolveFlexibleLengths(ref line, mainAvailable, mainGap);

            // Final measure at the resolved main size; the cross constraint is the panel's.
            double usedMain = 0;
            double crossSize = 0;
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
                usedMain += item.TargetMain;
                crossSize = Math.Max(crossSize, item.OuterCross);
            }

            line.UsedMain = usedMain + mainGap * Math.Max(0, line.Count - 1);
            line.CrossSize = crossSize;
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

        (_cssLayoutHost ?? this).MeasureCssLayoutAbsoluteChildren(availableSize);

        _measuredAvailable = availableSize;
        return axis.Size(desiredMain, desiredCross);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var axis = new FlexAxis(Direction);
        var mainFinal = axis.Main(finalSize);
        var crossFinal = axis.Cross(finalSize);
        var rowGap = Jalium.UI.Styling.CssGapProperties.Resolve(_cssLayoutHost ?? this, true, finalSize.Height);
        var columnGap = Jalium.UI.Styling.CssGapProperties.Resolve(_cssLayoutHost ?? this, false, finalSize.Width);
        var mainGap = SanitizeSpacing(axis.MainGap(rowGap, columnGap));
        var crossGap = SanitizeSpacing(axis.CrossGap(rowGap, columnGap));

        if (_items is null || _lines is null || _itemCount == 0)
        {
            (_cssLayoutHost ?? this).ArrangeCssLayoutAbsoluteChildren(finalSize);
            return finalSize;
        }

        // The arrange extent can differ from the measured constraint (alignment, star
        // tracks). Re-run the pure arithmetic (line breaking + flex resolution) against
        // the final size; cached base sizes and cross measurements stay valid.
        if (Math.Abs(mainFinal - axis.Main(_measuredAvailable)) > 0.5)
        {
            BreakLinesPreservingItems(mainFinal, mainGap);
            for (var lineIndex = 0; lineIndex < _lineCount; lineIndex++)
            {
                ref var line = ref _lines![lineIndex];
                ResolveFlexibleLengths(ref line, mainFinal, mainGap);
                double usedMain = 0;
                double crossSize = 0;
                for (var i = line.Start; i < line.Start + line.Count; i++)
                {
                    usedMain += _items[i].TargetMain;
                    crossSize = Math.Max(crossSize, _items[i].OuterCross);
                }

                line.UsedMain = usedMain + mainGap * Math.Max(0, line.Count - 1);
                line.CrossSize = crossSize;
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

            if (Wrap == FlexWrap.WrapReverse)
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

            var (leading, extra) = ComputeDistribution(justify, freeMain, line.Count);
            var cursor = leading;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                ref readonly var item = ref items[i];
                var outerMain = item.TargetMain;
                var mainPos = axis.MainReversed ? mainFinal - cursor - outerMain : cursor;

                double crossPos;
                double crossLen;
                switch (item.Align)
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
                        crossPos = line.CrossOffset + line.CrossSize - item.OuterCross;
                        crossLen = item.OuterCross;
                        break;
                    default: // FlexStart (Auto resolved earlier)
                        crossPos = line.CrossOffset;
                        crossLen = item.OuterCross;
                        break;
                }

                var m0 = FrameworkElement.SnapLayoutValue(mainPos);
                var m1 = FrameworkElement.SnapLayoutValue(mainPos + outerMain);
                var c0 = FrameworkElement.SnapLayoutValue(crossPos);
                var c1 = FrameworkElement.SnapLayoutValue(crossPos + crossLen);
                item.Element.Arrange(axis.Rect(m0, c0, Math.Max(0, m1 - m0), Math.Max(0, c1 - c0)));

                cursor += outerMain + mainGap + extra;
            }
        }

        (_cssLayoutHost ?? this).ArrangeCssLayoutAbsoluteChildren(finalSize);
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
        var index = 0;
        foreach (var child in LayoutChildren.EnumerateStruct())
        {
            index++;
            if (child.Visibility == Visibility.Collapsed || IsCssAbsolute(child))
            {
                continue;
            }

            var fe = child as FrameworkElement;
            var basis = GetBasis(child);
            if (!child.HasLocalValue(BasisProperty) && child.GetValue(CssBasisProperty) is Jalium.UI.Styling.CssLength cssBasis && fe is not null)
            {
                var context = Jalium.UI.Styling.CssEngine.BuildLengthContext(fe);
                if (cssBasis.UsesPercent && !double.IsFinite(mainAvailable)) basis = double.NaN;
                else if (cssBasis.Expression is { } expression)
                    basis = expression.TryEvaluate(context, mainAvailable, out var calculated) ? Math.Max(0, calculated) : double.NaN;
                else if (cssBasis.Unit == Jalium.UI.Styling.CssUnit.Percent) basis = Math.Max(0, cssBasis.Value / 100 * mainAvailable);
                else basis = cssBasis.TryResolve(context, Jalium.UI.Styling.CssPercentBasis.NotSupported, out var pixels) ? Math.Max(0, pixels) : double.NaN;
            }
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
            };
            _orderKeys![_itemCount] = ((long)GetOrder(child) << 32) | (uint)(index - 1);
            _itemCount++;
        }

        // Stable order sort: same Order keeps document order via the low 32 bits.
        Array.Sort(_orderKeys!, _items, 0, _itemCount);
    }

    private void BreakLinesPreservingItems(double mainAvailable, double mainGap)
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
        for (var i = 0; i < _itemCount; i++)
        {
            var outer = items[i].BaseMain;
            var prospective = countOnLine > 0 ? running + mainGap + outer : outer;
            if (wrap && countOnLine > 0 && prospective > mainAvailable + 0.001)
            {
                _lines[_lineCount++] = new FlexLine { Start = lineStart, Count = countOnLine, BaseMain = running + mainGap * (countOnLine - 1) };
                lineStart = i;
                running = outer;
                countOnLine = 1;
            }
            else
            {
                running = prospective;
                countOnLine++;
            }
        }

        _lines[_lineCount++] = new FlexLine { Start = lineStart, Count = countOnLine, BaseMain = running };
    }

    /// <summary>CSS Flexbox §9.7: resolve flexible lengths with min/max violation freezing.</summary>
    private void ResolveFlexibleLengths(ref FlexLine line, double mainAvailable, double mainGap)
    {
        var items = _items!;
        var start = line.Start;
        var end = line.Start + line.Count;

        if (double.IsInfinity(mainAvailable))
        {
            for (var i = start; i < end; i++)
            {
                items[i].TargetMain = items[i].BaseMain;
            }

            return;
        }

        var lineAvailable = mainAvailable - mainGap * Math.Max(0, line.Count - 1);
        double sumBase = 0;
        for (var i = start; i < end; i++)
        {
            sumBase += items[i].BaseMain;
        }

        var growing = sumBase < lineAvailable;

        double initialFree = lineAvailable;
        for (var i = start; i < end; i++)
        {
            ref var item = ref items[i];
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
            if (!double.IsFinite(item.TargetMain) || item.TargetMain < 0)
            {
                item.TargetMain = Math.Clamp(item.BaseMain, item.MinMain, item.MaxMain);
            }
        }
    }
}
