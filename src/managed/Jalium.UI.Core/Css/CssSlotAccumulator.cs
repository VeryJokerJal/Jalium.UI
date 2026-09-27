namespace Jalium.UI.Styling;

/// <summary>Aggregation slots: several longhand declarations combining into one DP value.</summary>
internal enum CssSlot : byte
{
    None,
    Margin,
    Padding,
    BorderWidth,
    CornerRadius,
    Background,
    Effect,
    Transform,
    Font,
    Transition,
    TextDecoration,
}

/// <summary>
/// Collects slot contributions while one element's winning declarations are applied, then
/// flushes each touched slot into a single DP setter. Missing components take the CSS
/// initial value (0 for margin/padding edges).
/// </summary>
internal sealed class CssSlotAccumulator
{
    public Jalium.UI.Media.Brush? ForegroundBrush;
    public CssTransitionParts? Transitions;
    public CssAnimationParts? Animations;
    public CssGridPlacementParts? GridPlacement;
    public CssGapParts? Gaps;
    public CssVisibilityParts? Visibility;
    /// <summary>
    /// Combines box-shadow and filter effects into one Effect (EffectGroup lives in the
    /// Jalium.UI.Media assembly, which Core cannot reference). Injected by the Controls-level
    /// table registration.
    /// </summary>
    internal static Func<object?, object?, object?>? EffectCombiner;
    internal static Func<int, DependencyProperty?>? InsetCompatibilityProperty;

    /// <summary>
    /// Set by the engine before each declaration's TryApply: contributions made while true
    /// mark their slot as state-owned, so the merged value lands in the CssState layer.
    /// </summary>
    public bool CurrentContributionIsState;
    public bool CurrentContributionIsImportant;
    public bool LogicalRightToLeft;
    public Rect TransformReferenceBox;

    private ThicknessSlot _margin;
    private ThicknessSlot _padding;
    private ThicknessSlot _borderWidth;
    private BorderColorSlot _borderColors;
    private BorderStyleSlot _borderStyles;
    private CornerSlot _corner;
    private BackgroundSlot _background;
    private EffectSlot _effect;
    private LayoutSlot _layout;
    private CssOverflowMode _overflowX, _overflowY;
    private bool _overflowTouched, _overflowFromState, _overflowFromImportant;

    public void SetOverflow(bool horizontal, CssOverflowMode mode)
    {
        _overflowTouched = true;
        _overflowFromState |= CurrentContributionIsState;
        _overflowFromImportant |= CurrentContributionIsImportant;
        if (horizontal) _overflowX = mode;
        else _overflowY = mode;
    }

    public void SetBorderStyle(int edge, CssBorderLineStyle style)
    {
        _borderStyles.Touched = true;
        _borderStyles.FromState |= CurrentContributionIsState;
        _borderStyles.FromImportant |= CurrentContributionIsImportant;
        switch (edge)
        {
            case 0: _borderStyles.Left = style; break;
            case 1: _borderStyles.Top = style; break;
            case 2: _borderStyles.Right = style; break;
            case 3: _borderStyles.Bottom = style; break;
        }
    }

    public void SetBorderColor(int edge, Jalium.UI.Media.Brush brush)
    {
        _borderColors.Touched = true;
        _borderColors.FromState |= CurrentContributionIsState;
        _borderColors.FromImportant |= CurrentContributionIsImportant;
        switch (edge)
        {
            case 0: _borderColors.Left = brush; break;
            case 1: _borderColors.Top = brush; break;
            case 2: _borderColors.Right = brush; break;
            case 3: _borderColors.Bottom = brush; break;
        }
    }

    private struct LayoutSlot
    {
        public bool Touched;
        public CssLayoutLength Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight;
        public double AspectRatio;
        public double AspectRatioNumerator, AspectRatioDenominator;
        public bool AspectRatioAuto;
        public bool HasAspectRatio;
        public CssBoxSizing BoxSizing;
        public bool HasBoxSizing;
        public CssPositionMode Position;
        public CssPositionKeyword ComputedPosition;
        public bool HasPosition;
        public CssLayoutLength InsetLeft, InsetTop, InsetRight, InsetBottom;
        public DependencyProperty? InsetLeftCompatDp, InsetTopCompatDp, InsetRightCompatDp, InsetBottomCompatDp;
    }

    private struct ThicknessSlot
    {
        public bool Touched;
        public bool FromState;
        public bool FromImportant;
        public CssLength Left, Top, Right, Bottom;
        public CssLayoutLength ComputedLeft, ComputedTop, ComputedRight, ComputedBottom;
        public bool HasLeft, HasTop, HasRight, HasBottom;
    }

    private struct CornerSlot
    {
        public bool Touched;
        public bool FromState;
        public bool FromImportant;
        public CssComputedCorner TopLeft, TopRight, BottomRight, BottomLeft;
    }

    private struct BorderColorSlot
    {
        public bool Touched;
        public bool FromState;
        public bool FromImportant;
        public Jalium.UI.Media.Brush? Left, Top, Right, Bottom;
    }

    private struct BorderStyleSlot
    {
        public bool Touched;
        public bool FromState;
        public bool FromImportant;
        public CssBorderLineStyle Left, Top, Right, Bottom;

        public readonly CssBorderStyles Value => new(Left, Top, Right, Bottom);
    }

    private struct BackgroundSlot
    {
        public bool Touched;
        public bool FromState;
        public bool FromImportant;
        public object? ColorBrush;
        public bool HasColor;
        public object? ImageBrush;
        public bool HasImage;
        public Jalium.UI.Media.CssBackgroundSize[]? Sizes;
        public Jalium.UI.Media.CssBackgroundRepeat[]? Repeats;
        public Jalium.UI.Media.CssBackgroundPosition[]? Positions;
        public Jalium.UI.Media.CssBackgroundBox[]? Origins;
        public Jalium.UI.Media.CssBackgroundBox[]? Clips;
    }

    private struct EffectSlot
    {
        public bool FromState;
        public bool FromImportant;
        public object? Shadow;
        public bool HasShadow;
        public object? Filter;
        public bool HasFilter;
    }

    public void Reset()
    {
        CurrentContributionIsState = false;
        CurrentContributionIsImportant = false;
        TransformReferenceBox = default;
        _margin = default;
        _padding = default;
        _borderWidth = default;
        _borderColors = default;
        _borderStyles = default;
        _corner = default;
        _background = default;
        _effect = default;
        _layout = default;
        Transitions = null;
        Animations = null;
        GridPlacement = null;
        Gaps = null;
        Visibility = null;
        _overflowX = _overflowY = CssOverflowMode.Visible;
        _overflowTouched = _overflowFromState = _overflowFromImportant = false;
    }

    public void SetLayoutPercent(CssLayoutSlotField field, double fraction)
        => SetLayoutLength(field, CssLayoutLength.Percent(fraction));

    public void SetLayoutLength(CssLayoutSlotField field, CssLayoutLength length)
    {
        _layout.Touched = true;
        switch (field)
        {
            case CssLayoutSlotField.Width: _layout.Width = length; break;
            case CssLayoutSlotField.Height: _layout.Height = length; break;
            case CssLayoutSlotField.MinWidth: _layout.MinWidth = length; break;
            case CssLayoutSlotField.MinHeight: _layout.MinHeight = length; break;
            case CssLayoutSlotField.MaxWidth: _layout.MaxWidth = length; break;
            case CssLayoutSlotField.MaxHeight: _layout.MaxHeight = length; break;
        }
    }

    public void SetAspectRatio(double numerator, double denominator, bool auto = false)
    {
        _layout.Touched = true;
        var ratio = numerator > 0 && denominator > 0 ? numerator / denominator : double.NaN;
        _layout.AspectRatio = double.IsFinite(ratio) ? ratio : double.NaN;
        _layout.AspectRatioNumerator = numerator;
        _layout.AspectRatioDenominator = denominator;
        _layout.AspectRatioAuto = auto;
        _layout.HasAspectRatio = true;
    }

    public void SetBoxSizing(CssBoxSizing boxSizing)
    {
        _layout.Touched = true;
        _layout.BoxSizing = boxSizing;
        _layout.HasBoxSizing = true;
    }

    public void SetPosition(CssPositionKeyword position)
    {
        _layout.Touched = true;
        _layout.ComputedPosition = position;
        _layout.Position = position switch
        {
            CssPositionKeyword.Relative => CssPositionMode.Relative,
            CssPositionKeyword.Absolute => CssPositionMode.Absolute,
            _ => CssPositionMode.Static,
        };
        _layout.HasPosition = true;
    }

    /// <summary>Edges: 0 = left, 1 = top, 2 = right, 3 = bottom. compatDp = the Canvas
    /// attached property used for position:static compatibility (absolute px only).</summary>
    public void SetInset(int edge, CssLayoutLength length, DependencyProperty? compatDp)
    {
        _layout.Touched = true;
        switch (edge)
        {
            case 0: _layout.InsetLeft = length; _layout.InsetLeftCompatDp = compatDp; break;
            case 1: _layout.InsetTop = length; _layout.InsetTopCompatDp = compatDp; break;
            case 2: _layout.InsetRight = length; _layout.InsetRightCompatDp = compatDp; break;
            case 3: _layout.InsetBottom = length; _layout.InsetBottomCompatDp = compatDp; break;
        }
    }

    /// <summary>Corners in CSS/CornerRadius order: 0 = top-left, 1 = top-right, 2 = bottom-right, 3 = bottom-left.</summary>
    public void SetCorner(int corner, CssComputedCorner radius)
    {
        _corner.Touched = true;
        _corner.FromState |= CurrentContributionIsState;
        _corner.FromImportant |= CurrentContributionIsImportant;
        switch (corner)
        {
            case 0: _corner.TopLeft = radius; break;
            case 1: _corner.TopRight = radius; break;
            case 2: _corner.BottomRight = radius; break;
            case 3: _corner.BottomLeft = radius; break;
        }
    }

    public void SetBackgroundColor(object? brush)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.ColorBrush = brush;
        _background.HasColor = true;
    }

    public void SetBackgroundImage(object? brush)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.ImageBrush = brush;
        _background.HasImage = true;
    }

    public void SetBackgroundImages(Jalium.UI.Media.Brush?[] images)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.ImageBrush = images;
        _background.HasImage = true;
    }

    public void SetBackgroundSizes(Jalium.UI.Media.CssBackgroundSize[] sizes)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.Sizes = sizes;
    }

    public void SetBackgroundRepeats(Jalium.UI.Media.CssBackgroundRepeat[] repeats)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.Repeats = repeats;
    }

    public void SetBackgroundPositions(Jalium.UI.Media.CssBackgroundPosition[] positions)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.Positions = positions;
    }

    public void SetBackgroundOrigins(Jalium.UI.Media.CssBackgroundBox[] origins)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.Origins = origins;
    }

    public void SetBackgroundClips(Jalium.UI.Media.CssBackgroundBox[] clips)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.FromImportant |= CurrentContributionIsImportant;
        _background.Clips = clips;
    }

    public void SetBoxShadow(object? effect)
    {
        _effect.Shadow = effect;
        _effect.HasShadow = true;
        _effect.FromState |= CurrentContributionIsState;
        _effect.FromImportant |= CurrentContributionIsImportant;
    }

    public void SetFilter(object? effect)
    {
        _effect.Filter = effect;
        _effect.HasFilter = true;
        _effect.FromState |= CurrentContributionIsState;
        _effect.FromImportant |= CurrentContributionIsImportant;
    }

    /// <summary>Edges are CSS order-independent indices: 0 = left, 1 = top, 2 = right, 3 = bottom.</summary>
    public void SetThicknessEdge(CssSlot slot, int edge, CssLength length)
    {
        ref var target = ref GetThicknessSlot(slot);
        target.Touched = true;
        target.FromState |= CurrentContributionIsState;
        target.FromImportant |= CurrentContributionIsImportant;
        switch (edge)
        {
            case 0: target.Left = length; target.ComputedLeft = default; target.HasLeft = true; break;
            case 1: target.Top = length; target.ComputedTop = default; target.HasTop = true; break;
            case 2: target.Right = length; target.ComputedRight = default; target.HasRight = true; break;
            case 3: target.Bottom = length; target.ComputedBottom = default; target.HasBottom = true; break;
        }
    }

    public void SetComputedThicknessEdge(CssSlot slot, int edge, CssLayoutLength length)
    {
        ref var target = ref GetThicknessSlot(slot);
        target.Touched = true;
        target.FromState |= CurrentContributionIsState;
        target.FromImportant |= CurrentContributionIsImportant;
        var px = length.IsAuto ? new CssLength(0, CssUnit.Auto) :
            new CssLength(length.Resolve(double.NaN, 0), CssUnit.Px);
        switch (edge)
        {
            case 0: target.Left = px; target.ComputedLeft = length; target.HasLeft = true; break;
            case 1: target.Top = px; target.ComputedTop = length; target.HasTop = true; break;
            case 2: target.Right = px; target.ComputedRight = length; target.HasRight = true; break;
            case 3: target.Bottom = px; target.ComputedBottom = length; target.HasBottom = true; break;
        }
    }

    public void Flush(in CssApplyContext context, ICssSetterSink sink)
    {
        var marginIsPercent = _margin.Touched && HasPercentEdge(in _margin);
        if (_margin.Touched && !marginIsPercent)
        {
            sink.CurrentValueIsState = _margin.FromState;
            sink.CurrentValueIsImportant = _margin.FromImportant;
            var thickness = ResolveThickness(in _margin, in context, "margin", clampNonNegative: false);
            sink.Set(FrameworkElement.MarginProperty, thickness);
        }

        var paddingUsesLayout = _padding.Touched && (HasPercentEdge(in _padding) ||
            (context.Element.Target is Jalium.UI.Controls.Panel or Jalium.UI.Controls.Page or Jalium.UI.Controls.Image or Jalium.UI.Controls.MediaElement) &&
            CssDependencyPropertyLookup.Find(context.Element.GetType(), "Padding") is null);
        if (_padding.Touched && !paddingUsesLayout)
        {
            sink.CurrentValueIsState = _padding.FromState;
            sink.CurrentValueIsImportant = _padding.FromImportant;
            FlushNamedThickness(in _padding, in context, sink, "padding", "Padding", clampNonNegative: true);
        }

        var borderTouched = _borderWidth.Touched || _borderColors.Touched || _borderStyles.Touched;
        if (borderTouched)
        {
            sink.CurrentValueIsState = _borderWidth.FromState || _borderColors.FromState || _borderStyles.FromState;
            sink.CurrentValueIsImportant = _borderWidth.FromImportant || _borderColors.FromImportant || _borderStyles.FromImportant;
            var width = _borderWidth.Touched
                ? ResolveThickness(in _borderWidth, in context, "border-width", clampNonNegative: true, missingValue: 3)
                : new Thickness(3);
            var styles = _borderStyles.Value; // Missing style declarations have the CSS initial value, none.
            width = new Thickness(
                CssBorderStyles.IsHidden(styles.Left) ? 0 : width.Left,
                CssBorderStyles.IsHidden(styles.Top) ? 0 : width.Top,
                CssBorderStyles.IsHidden(styles.Right) ? 0 : width.Right,
                CssBorderStyles.IsHidden(styles.Bottom) ? 0 : width.Bottom);
            if (CssDependencyPropertyLookup.Find(context.Element.GetType(), "BorderThickness") is { } dp)
                sink.Set(dp, width);
            else if (context.Element.Target is Jalium.UI.Controls.Panel or Jalium.UI.Controls.TextBlock or Jalium.UI.Controls.Page)
                sink.Set(CssBorderUsedThicknessProperties.ValueProperty, width);
            else if (_borderWidth.Touched)
                CssDiagnostics.Report("border-width", CssDiagnosticReason.TargetPropertyMissing,
                    context.Element.GetType(), "no dependency property 'BorderThickness' on this element type; declaration skipped here");
            sink.Set(CssBorderStyleProperties.ValueProperty, styles);
        }

        if (borderTouched)
        {
            sink.CurrentValueIsState = _borderWidth.FromState || _borderColors.FromState || _borderStyles.FromState;
            sink.CurrentValueIsImportant = _borderWidth.FromImportant || _borderColors.FromImportant || _borderStyles.FromImportant;
            var current = context.CurrentColor;
            if (_borderColors.Left is null || _borderColors.Top is null ||
                _borderColors.Right is null || _borderColors.Bottom is null)
                CssColorBrushObserver.Observe(context.Element, current as Jalium.UI.Media.SolidColorBrush);
            var colors = new CssBorderPaint(
                _borderColors.Left ?? current, _borderColors.Top ?? current,
                _borderColors.Right ?? current, _borderColors.Bottom ?? current);
            var hidden = _borderStyles.Touched && _borderStyles.Value.AllHidden;
            sink.Set(CssBorderPaintProperties.ValueProperty, hidden ? null : colors);
            if (CssDependencyPropertyLookup.Find(context.Element.GetType(), "BorderBrush") is { } brush)
                sink.Set(brush, hidden ? null : colors.Top);
        }

        if (_corner.Touched)
        {
            sink.CurrentValueIsState = _corner.FromState;
            sink.CurrentValueIsImportant = _corner.FromImportant;
            FlushCorner(in context, sink);
        }

        if (_overflowTouched)
        {
            var overflow = CssOverflowValue.Compute(_overflowX, _overflowY);
            sink.CurrentValueIsState = _overflowFromState;
            sink.CurrentValueIsImportant = _overflowFromImportant;
            sink.Set(UIElement.ClipToBoundsProperty, overflow.Edges != ClipEdges.None);
            // All preserves the native four-edge meaning if a local ClipToBounds value wins.
            sink.Set(UIElement.ClipToBoundsEdgesProperty,
                overflow.Edges == ClipEdges.None ? ClipEdges.All : overflow.Edges);
            sink.Set(CssOverflowProperties.ValueProperty, overflow);
        }

        if (_background.Touched)
        {
            sink.CurrentValueIsState = _background.FromState;
            sink.CurrentValueIsImportant = _background.FromImportant;
            FlushBackground(in context, sink);
        }

        if (_effect.HasShadow || _effect.HasFilter)
        {
            sink.CurrentValueIsState = _effect.FromState;
            sink.CurrentValueIsImportant = _effect.FromImportant;
            FlushEffect(in context, sink);
        }

        sink.CurrentValueIsImportant = false;
        Transitions?.Flush(sink);
        Animations?.Flush(sink);
        GridPlacement?.Flush(sink);
        Gaps?.Flush(context, sink);
        Visibility?.Flush(sink);
        sink.CurrentValueIsState = false;
        sink.CurrentValueIsImportant = false;

        FlushLayoutState(in context, sink, marginIsPercent, paddingUsesLayout);
    }

    private static bool HasPercentEdge(in ThicknessSlot slot)
        => NeedsLayoutEdge(slot.HasLeft, slot.Left, slot.ComputedLeft) ||
           NeedsLayoutEdge(slot.HasTop, slot.Top, slot.ComputedTop) ||
           NeedsLayoutEdge(slot.HasRight, slot.Right, slot.ComputedRight) ||
           NeedsLayoutEdge(slot.HasBottom, slot.Bottom, slot.ComputedBottom);

    private static bool NeedsLayoutEdge(bool has, CssLength parsed, CssLayoutLength computed)
        => has && (computed.IsSet ? computed.IsPercent || computed.IsAuto
            : parsed.UsesPercent || parsed.Unit == CssUnit.Auto);

    /// <summary>
    /// Builds the immutable CssLayoutState snapshot (percent sizes, computed margin/padding,
    /// aspect-ratio, box-sizing, position/inset) and hands it to the sink. Static-position
    /// insets are retained here for computed-style queries while their supported pixel
    /// values still feed the Canvas compatibility DPs.
    /// </summary>
    private void FlushLayoutState(
        in CssApplyContext context, ICssSetterSink sink, bool marginIsPercent, bool paddingUsesLayout)
    {
        var position = _layout.HasPosition ? _layout.Position : CssPositionMode.Static;
        if (position == CssPositionMode.Static)
        {
            // position:static — absolute-pixel insets keep the historical Canvas mapping.
            // The computed lengths are also retained below for style queries and inheritance.
            FlushStaticInset(_layout.InsetLeft, _layout.InsetLeftCompatDp, "left", in context, sink);
            FlushStaticInset(_layout.InsetTop, _layout.InsetTopCompatDp, "top", in context, sink);
            FlushStaticInset(_layout.InsetRight, _layout.InsetRightCompatDp, "right", in context, sink);
            FlushStaticInset(_layout.InsetBottom, _layout.InsetBottomCompatDp, "bottom", in context, sink);
        }

        var needsState = marginIsPercent || paddingUsesLayout || _layout.Touched &&
            (_layout.Width.IsSet || _layout.Height.IsSet ||
             _layout.MinWidth.IsSet || _layout.MinHeight.IsSet ||
             _layout.MaxWidth.IsSet || _layout.MaxHeight.IsSet ||
             _layout.HasAspectRatio || _layout.HasBoxSizing || position != CssPositionMode.Static ||
             _layout.ComputedPosition is CssPositionKeyword.Fixed or CssPositionKeyword.Sticky ||
             _layout.InsetLeft.IsSet || _layout.InsetTop.IsSet ||
             _layout.InsetRight.IsSet || _layout.InsetBottom.IsSet);
        if (!needsState)
        {
            return;
        }

        var state = new CssLayoutState
        {
            Width = _layout.Width,
            Height = _layout.Height,
            MinWidth = _layout.MinWidth,
            MinHeight = _layout.MinHeight,
            MaxWidth = _layout.MaxWidth,
            MaxHeight = _layout.MaxHeight,
            BoxSizing = _layout.HasBoxSizing ? _layout.BoxSizing : CssBoxSizing.BorderBox,
            HasBoxSizing = _layout.HasBoxSizing,
            Position = position,
            ComputedPosition = _layout.HasPosition ? _layout.ComputedPosition : CssPositionKeyword.Static,
        };

        if (_layout.HasAspectRatio)
        {
            state.AspectRatio = _layout.AspectRatio;
            state.HasAspectRatio = true;
            state.AspectRatioAuto = _layout.AspectRatioAuto;
            state.AspectRatioNumerator = _layout.AspectRatioNumerator;
            state.AspectRatioDenominator = _layout.AspectRatioDenominator;
        }

        state.InsetLeft = _layout.InsetLeft;
        state.InsetTop = _layout.InsetTop;
        state.InsetRight = _layout.InsetRight;
        state.InsetBottom = _layout.InsetBottom;

        if (_margin.Touched)
        {
            state.HasMargin = marginIsPercent;
            state.MarginLeft = ToLayoutLength(in _margin, 0, in context);
            state.MarginTop = ToLayoutLength(in _margin, 1, in context);
            state.MarginRight = ToLayoutLength(in _margin, 2, in context);
            state.MarginBottom = ToLayoutLength(in _margin, 3, in context);
        }

        if (_padding.Touched)
        {
            state.HasPadding = paddingUsesLayout;
            state.PaddingLeft = ToLayoutLength(in _padding, 0, in context, clampNonNegative: true);
            state.PaddingTop = ToLayoutLength(in _padding, 1, in context, clampNonNegative: true);
            state.PaddingRight = ToLayoutLength(in _padding, 2, in context, clampNonNegative: true);
            state.PaddingBottom = ToLayoutLength(in _padding, 3, in context, clampNonNegative: true);
        }

        state.Seal();
        sink.SetLayoutState(state);
    }

    private void FlushStaticInset(
        CssLayoutLength length, DependencyProperty? compatDp, string cssName,
        in CssApplyContext context, ICssSetterSink sink)
    {
        if (!length.IsSet || length.IsAuto)
        {
            return;
        }

        if (length.IsPercent)
        {
            CssDiagnostics.Report(
                cssName, CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                "percentage offsets need position: relative or absolute; ignored by static layout");
            return;
        }

        if (compatDp is not null)
        {
            sink.Set(compatDp, length.Value);
        }
    }

    /// <summary>Converts an already-resolved slot length (Percent kept, other units → px via the context).</summary>
    private static CssLayoutLength ToLayoutLength(CssLength length, in CssApplyContext context, bool has = true)
    {
        if (!has)
        {
            return CssLayoutLength.Px(0);
        }

        if (length.Unit == CssUnit.Auto) return CssLayoutLength.Auto;
        if (length.Expression is { } expression)
            return CssLayoutLength.Math(expression, context.Lengths);
        if (length.Unit == CssUnit.Percent)
        {
            return CssLayoutLength.Percent(length.Value / 100.0);
        }

        return length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px)
            ? CssLayoutLength.Px(px)
            : CssLayoutLength.Px(0);
    }

    private static CssLayoutLength ToLayoutLength(in ThicknessSlot slot, int edge, in CssApplyContext context,
        bool clampNonNegative = false)
    {
        var (length, computed, has) = edge switch
        {
            0 => (slot.Left, slot.ComputedLeft, slot.HasLeft),
            1 => (slot.Top, slot.ComputedTop, slot.HasTop),
            2 => (slot.Right, slot.ComputedRight, slot.HasRight),
            _ => (slot.Bottom, slot.ComputedBottom, slot.HasBottom),
        };
        var value = computed.IsSet ? computed : ToLayoutLength(length, in context, has);
        if (!clampNonNegative) return value;
        if (value.Kind == CssLayoutLength.KindPx) return CssLayoutLength.Px(Math.Max(0, value.Value));
        if (value.Kind == CssLayoutLength.KindExpression &&
            value.Expression is { Kind: CssNumericKind.Percent } percentExpression &&
            percentExpression.TryEvaluate(value.Context, 1, out var fraction) && double.IsFinite(fraction))
            return CssLayoutLength.Percent(Math.Max(0, fraction));
        if (value.Kind == CssLayoutLength.KindExpression && !value.IsPercent)
        {
            var resolved = value.Resolve(double.NaN, double.NaN);
            if (double.IsFinite(resolved)) return CssLayoutLength.Px(Math.Max(0, resolved));
        }
        return value;
    }

    private static void FlushNamedThickness(
        in ThicknessSlot slot, in CssApplyContext context, ICssSetterSink sink,
        string cssName, string dpName, bool clampNonNegative)
    {
        var dp = CssDependencyPropertyLookup.Find(context.Element.GetType(), dpName);
        if (dp is null)
        {
            CssDiagnostics.Report(
                cssName, CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                $"no dependency property '{dpName}' on this element type; declaration skipped here");
            return;
        }

        sink.Set(dp, ResolveThickness(in slot, in context, cssName, clampNonNegative));
    }

    private void FlushCorner(in CssApplyContext context, ICssSetterSink sink)
    {
        var dp = CssDependencyPropertyLookup.Find(context.Element.GetType(), "CornerRadius");
        if (dp is null && context.Element.Target is not FrameworkElement)
        {
            CssDiagnostics.Report(
                "border-radius", CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                "border-radius requires a visual framework element; declaration skipped here");
            return;
        }

        var radii = new CssBorderRadiusValue(_corner.TopLeft, _corner.TopRight, _corner.BottomRight, _corner.BottomLeft);
        var element = context.Element;
        if (radii.UsesPercent && !CssEngine.EnsureState(element).ObservesOwnSize)
        {
            CssEngine.EnsureState(element).ObservesOwnSize = true;
            element.SizeChanged += (_, _) => CssEvaluationScheduler.InvalidateElement(element);
        }
        if (dp is not null)
            sink.Set(dp, radii.Horizontal(new Size(element.ActualWidth, element.ActualHeight)));
        sink.Set(CssBorderRadiusProperties.ValueProperty, radii);
    }

    private void FlushBackground(in CssApplyContext context, ICssSetterSink sink)
    {
        var dp = CssDependencyPropertyLookup.Find(context.Element.GetType(), "Background");
        if (dp is null)
        {
            CssDiagnostics.Report(
                "background", CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                "no dependency property 'Background' on this element type; declaration skipped here");
            return;
        }

        object? brush = _background.HasColor ? _background.ColorBrush : null;
        var images = _background.ImageBrush is Jalium.UI.Media.Brush?[] layers
            ? layers : _background.ImageBrush is Jalium.UI.Media.Brush single
                ? [single] : [];
        var paintLayers = new Jalium.UI.Media.CssBackgroundPaintLayer[images.Length];
        var sizeList = _background.Sizes is { Length: > 0 } sizes
            ? sizes : [new Jalium.UI.Media.CssBackgroundSize(Jalium.UI.Media.CssBackgroundSizeMode.Auto)];
        var repeatList = _background.Repeats is { Length: > 0 } repeats
            ? repeats : [Jalium.UI.Media.CssBackgroundRepeat.Both];
        var positionList = _background.Positions is { Length: > 0 } positions
            ? positions : [Jalium.UI.Media.CssBackgroundPosition.Initial];
        var clipList = _background.Clips is { Length: > 0 } clips
            ? clips : [Jalium.UI.Media.CssBackgroundBox.Border];
        var originList = _background.Origins is { Length: > 0 } origins
            ? origins : [Jalium.UI.Media.CssBackgroundBox.Padding];
        Jalium.UI.Media.CssBackgroundBox ClipAt(int index) => clipList[index % clipList.Length];
        Jalium.UI.Media.CssBackgroundBox OriginAt(int index) => originList[index % originList.Length];
        // The first CSS image is closest to the viewer. Build the brush from
        // the color upward so native draw calls paint in the same order.
        for (var i = images.Length - 1; i >= 0; i--)
        {
            if (images[i] is not { } image)
            {
                paintLayers[i] = new(null, OriginAt(i), ClipAt(i));
                continue; // `none` still occupies a list position.
            }
            if (image is Jalium.UI.Media.ImageBrush imageBrush)
            {
                if (imageBrush.IsFrozen) imageBrush = imageBrush.Clone();
                var size = sizeList[i % sizeList.Length];
                var repeat = repeatList[i % repeatList.Length];
                var position = positionList[i % positionList.Length];
                imageBrush.Stretch = size.Mode switch
                {
                    Jalium.UI.Media.CssBackgroundSizeMode.Cover => Jalium.UI.Media.Stretch.UniformToFill,
                    Jalium.UI.Media.CssBackgroundSizeMode.Contain => Jalium.UI.Media.Stretch.Uniform,
                    Jalium.UI.Media.CssBackgroundSizeMode.Fill => Jalium.UI.Media.Stretch.Fill,
                    _ => Jalium.UI.Media.Stretch.None,
                };
                imageBrush.CssBackgroundLayout = new Jalium.UI.Media.CssBackgroundImageLayout(size, repeat, position);
                image = imageBrush;
            }

            paintLayers[i] = new(image, OriginAt(i), ClipAt(i));

            brush = brush is Jalium.UI.Media.Brush bottom
                ? new Jalium.UI.Media.CssLayeredBackgroundBrush(bottom, image)
                : image;
        }

        sink.Set(dp, brush);
        sink.Set(Jalium.UI.Media.CssBackgroundPaintProperties.ValueProperty,
            new Jalium.UI.Media.CssBackgroundPaintState(
                brush as Jalium.UI.Media.Brush,
                _background.HasColor ? _background.ColorBrush as Jalium.UI.Media.Brush : null,
                ClipAt(Math.Max(0, images.Length - 1)), paintLayers,
                sizeList, positionList, repeatList, originList, clipList));
    }

    private void FlushEffect(in CssApplyContext context, ICssSetterSink sink)
    {
        var shadow = _effect.HasShadow ? _effect.Shadow : null;
        var filter = _effect.HasFilter ? _effect.Filter : null;

        object? combined;
        if (shadow is not null && filter is not null)
        {
            var combiner = EffectCombiner;
            if (combiner is not null)
            {
                combined = combiner(shadow, filter);
            }
            else
            {
                CssDiagnostics.Report(
                    "filter", CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                    "box-shadow and filter cannot be combined without the effect combiner; box-shadow wins");
                combined = shadow;
            }
        }
        else
        {
            combined = shadow ?? filter;
        }

        // An explicit `none` on either longhand still clears the property.
        sink.Set(UIElement.EffectProperty, combined);
    }

    private ref ThicknessSlot GetThicknessSlot(CssSlot slot)
    {
        switch (slot)
        {
            case CssSlot.Margin:
                return ref _margin;
            case CssSlot.Padding:
                return ref _padding;
            case CssSlot.BorderWidth:
                return ref _borderWidth;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "not a Thickness-shaped slot");
        }
    }

    private static Thickness ResolveThickness(
        in ThicknessSlot slot, in CssApplyContext context, string cssName,
        bool clampNonNegative, double missingValue = 0)
    {
        var left = ResolveEdge(slot.HasLeft, slot.Left, in context, cssName, missingValue);
        var top = ResolveEdge(slot.HasTop, slot.Top, in context, cssName, missingValue);
        var right = ResolveEdge(slot.HasRight, slot.Right, in context, cssName, missingValue);
        var bottom = ResolveEdge(slot.HasBottom, slot.Bottom, in context, cssName, missingValue);
        if (clampNonNegative)
        {
            left = Math.Max(0, left);
            top = Math.Max(0, top);
            right = Math.Max(0, right);
            bottom = Math.Max(0, bottom);
        }

        return new Thickness(left, top, right, bottom);
    }

    private static double ResolveEdge(bool has, CssLength length, in CssApplyContext context,
        string cssName, double missingValue)
    {
        if (!has)
        {
            return missingValue;
        }

        if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px))
        {
            CssDiagnostics.Report(
                cssName, CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                "percentage or viewport-relative edge cannot be resolved; treated as 0");
            return 0;
        }

        return px;
    }
}
