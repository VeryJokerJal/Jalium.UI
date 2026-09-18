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
    public CssGridPlacementParts? GridPlacement;
    public CssGapParts? Gaps;
    public CssVisibilityParts? Visibility;
    /// <summary>
    /// Combines box-shadow and filter effects into one Effect (EffectGroup lives in the
    /// Jalium.UI.Media assembly, which Core cannot reference). Injected by the Controls-level
    /// table registration.
    /// </summary>
    internal static Func<object?, object?, object?>? EffectCombiner;

    /// <summary>
    /// Set by the engine before each declaration's TryApply: contributions made while true
    /// mark their slot as state-owned, so the merged value lands in the CssState layer.
    /// </summary>
    public bool CurrentContributionIsState;

    private ThicknessSlot _margin;
    private ThicknessSlot _padding;
    private ThicknessSlot _borderWidth;
    private CornerSlot _corner;
    private BackgroundSlot _background;
    private EffectSlot _effect;
    private LayoutSlot _layout;
    private bool _borderHidden;
    private bool _borderStyleFromState;

    public void SetBorderStyle(bool hidden)
    {
        _borderHidden = hidden;
        _borderStyleFromState = CurrentContributionIsState;
    }

    private struct LayoutSlot
    {
        public bool Touched;
        public CssLayoutLength Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight;
        public double AspectRatio;
        public bool HasAspectRatio;
        public CssBoxSizing BoxSizing;
        public bool HasBoxSizing;
        public CssPositionMode Position;
        public bool HasPosition;
        public CssLayoutLength InsetLeft, InsetTop, InsetRight, InsetBottom;
        public DependencyProperty? InsetLeftCompatDp, InsetTopCompatDp, InsetRightCompatDp, InsetBottomCompatDp;
    }

    private struct ThicknessSlot
    {
        public bool Touched;
        public bool FromState;
        public CssLength Left, Top, Right, Bottom;
        public bool HasLeft, HasTop, HasRight, HasBottom;
    }

    private struct CornerSlot
    {
        public bool Touched;
        public bool FromState;
        public CssLength TopLeft, TopRight, BottomRight, BottomLeft;
        public bool HasTopLeft, HasTopRight, HasBottomRight, HasBottomLeft;
    }

    private struct BackgroundSlot
    {
        public bool Touched;
        public bool FromState;
        public object? ColorBrush;
        public bool HasColor;
        public object? ImageBrush;
        public bool HasImage;
        public object? Stretch;
    }

    private struct EffectSlot
    {
        public bool FromState;
        public object? Shadow;
        public bool HasShadow;
        public object? Filter;
        public bool HasFilter;
    }

    public void Reset()
    {
        _margin = default;
        _padding = default;
        _borderWidth = default;
        _corner = default;
        _background = default;
        _effect = default;
        _layout = default;
        Transitions = null;
        GridPlacement = null;
        Gaps = null;
        Visibility = null;
        _borderHidden = false;
        _borderStyleFromState = false;
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

    public void SetAspectRatio(double ratio)
    {
        _layout.Touched = true;
        _layout.AspectRatio = ratio;
        _layout.HasAspectRatio = true;
    }

    public void SetBoxSizing(CssBoxSizing boxSizing)
    {
        _layout.Touched = true;
        _layout.BoxSizing = boxSizing;
        _layout.HasBoxSizing = true;
    }

    public void SetPosition(CssPositionMode position)
    {
        _layout.Touched = true;
        _layout.Position = position;
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
    public void SetCorner(int corner, CssLength radius)
    {
        _corner.Touched = true;
        _corner.FromState |= CurrentContributionIsState;
        switch (corner)
        {
            case 0: _corner.TopLeft = radius; _corner.HasTopLeft = true; break;
            case 1: _corner.TopRight = radius; _corner.HasTopRight = true; break;
            case 2: _corner.BottomRight = radius; _corner.HasBottomRight = true; break;
            case 3: _corner.BottomLeft = radius; _corner.HasBottomLeft = true; break;
        }
    }

    public void SetBackgroundColor(object? brush)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.ColorBrush = brush;
        _background.HasColor = true;
    }

    public void SetBackgroundImage(object? brush)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.ImageBrush = brush;
        _background.HasImage = true;
    }

    public void SetBackgroundStretch(object stretch)
    {
        _background.Touched = true;
        _background.FromState |= CurrentContributionIsState;
        _background.Stretch = stretch;
    }

    public void SetBoxShadow(object? effect)
    {
        _effect.Shadow = effect;
        _effect.HasShadow = true;
        _effect.FromState |= CurrentContributionIsState;
    }

    public void SetFilter(object? effect)
    {
        _effect.Filter = effect;
        _effect.HasFilter = true;
        _effect.FromState |= CurrentContributionIsState;
    }

    /// <summary>Edges are CSS order-independent indices: 0 = left, 1 = top, 2 = right, 3 = bottom.</summary>
    public void SetThicknessEdge(CssSlot slot, int edge, CssLength length)
    {
        ref var target = ref GetThicknessSlot(slot);
        target.Touched = true;
        target.FromState |= CurrentContributionIsState;
        switch (edge)
        {
            case 0: target.Left = length; target.HasLeft = true; break;
            case 1: target.Top = length; target.HasTop = true; break;
            case 2: target.Right = length; target.HasRight = true; break;
            case 3: target.Bottom = length; target.HasBottom = true; break;
        }
    }

    public void Flush(in CssApplyContext context, ICssSetterSink sink)
    {
        var marginIsPercent = _margin.Touched && HasPercentEdge(in _margin);
        if (_margin.Touched && !marginIsPercent)
        {
            sink.CurrentValueIsState = _margin.FromState;
            var thickness = ResolveThickness(in _margin, in context, "margin", clampNonNegative: false);
            sink.Set(FrameworkElement.MarginProperty, thickness);
        }

        var paddingIsPercent = _padding.Touched && (HasPercentEdge(in _padding) ||
            context.Element.Target is Jalium.UI.Controls.Panel && CssDependencyPropertyLookup.Find(context.Element.GetType(), "Padding") is null);
        if (_padding.Touched && !paddingIsPercent)
        {
            sink.CurrentValueIsState = _padding.FromState;
            FlushNamedThickness(in _padding, in context, sink, "padding", "Padding", clampNonNegative: true);
        }

        if (_borderWidth.Touched)
        {
            sink.CurrentValueIsState = _borderWidth.FromState;
            FlushNamedThickness(in _borderWidth, in context, sink, "border-width", "BorderThickness", clampNonNegative: true);
        }

        if (_borderHidden)
        {
            sink.CurrentValueIsState = _borderStyleFromState;
            if (CssDependencyPropertyLookup.Find(context.Element.GetType(), "BorderBrush") is { } brush) sink.Set(brush, null);
            if (CssDependencyPropertyLookup.Find(context.Element.GetType(), "BorderThickness") is { } thickness) sink.Set(thickness, default(Thickness));
        }

        if (_corner.Touched)
        {
            sink.CurrentValueIsState = _corner.FromState;
            FlushCorner(in context, sink);
        }

        if (_background.Touched)
        {
            sink.CurrentValueIsState = _background.FromState;
            FlushBackground(in context, sink);
        }

        if (_effect.HasShadow || _effect.HasFilter)
        {
            sink.CurrentValueIsState = _effect.FromState;
            FlushEffect(in context, sink);
        }

        Transitions?.Flush(sink);
        GridPlacement?.Flush(sink);
        Gaps?.Flush(context, sink);
        Visibility?.Flush(sink);
        sink.CurrentValueIsState = false;

        FlushLayoutState(in context, sink, marginIsPercent, paddingIsPercent);
    }

    private static bool HasPercentEdge(in ThicknessSlot slot)
        => (slot.HasLeft && (slot.Left.UsesPercent || slot.Left.Unit == CssUnit.Auto)) ||
           (slot.HasTop && (slot.Top.UsesPercent || slot.Top.Unit == CssUnit.Auto)) ||
           (slot.HasRight && (slot.Right.UsesPercent || slot.Right.Unit == CssUnit.Auto)) ||
           (slot.HasBottom && (slot.Bottom.UsesPercent || slot.Bottom.Unit == CssUnit.Auto));

    /// <summary>
    /// Builds the immutable CssLayoutState snapshot (percent sizes, %-margin/padding,
    /// aspect-ratio, box-sizing, position/inset) and hands it to the sink. Static-position
    /// insets fall back to the Canvas compatibility DPs instead.
    /// </summary>
    private void FlushLayoutState(
        in CssApplyContext context, ICssSetterSink sink, bool marginIsPercent, bool paddingIsPercent)
    {
        var isAbsolute = _layout.HasPosition && _layout.Position == CssPositionMode.Absolute;
        if (!isAbsolute)
        {
            // position:static — absolute-pixel insets keep the historical Canvas mapping.
            FlushStaticInset(_layout.InsetLeft, _layout.InsetLeftCompatDp, "left", in context, sink);
            FlushStaticInset(_layout.InsetTop, _layout.InsetTopCompatDp, "top", in context, sink);
            FlushStaticInset(_layout.InsetRight, _layout.InsetRightCompatDp, "right", in context, sink);
            FlushStaticInset(_layout.InsetBottom, _layout.InsetBottomCompatDp, "bottom", in context, sink);
        }

        var needsState = marginIsPercent || paddingIsPercent || _layout.Touched &&
            (_layout.Width.IsSet || _layout.Height.IsSet ||
             _layout.MinWidth.IsSet || _layout.MinHeight.IsSet ||
             _layout.MaxWidth.IsSet || _layout.MaxHeight.IsSet ||
             _layout.HasAspectRatio || _layout.HasBoxSizing || isAbsolute);
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
            Position = isAbsolute ? CssPositionMode.Absolute : CssPositionMode.Static,
        };

        if (_layout.HasAspectRatio)
        {
            state.AspectRatio = _layout.AspectRatio;
        }

        if (isAbsolute)
        {
            state.InsetLeft = _layout.InsetLeft;
            state.InsetTop = _layout.InsetTop;
            state.InsetRight = _layout.InsetRight;
            state.InsetBottom = _layout.InsetBottom;
        }

        if (marginIsPercent)
        {
            state.HasMargin = true;
            state.MarginLeft = ToLayoutLength(_margin.HasLeft ? _margin.Left : default, in context, _margin.HasLeft);
            state.MarginTop = ToLayoutLength(_margin.HasTop ? _margin.Top : default, in context, _margin.HasTop);
            state.MarginRight = ToLayoutLength(_margin.HasRight ? _margin.Right : default, in context, _margin.HasRight);
            state.MarginBottom = ToLayoutLength(_margin.HasBottom ? _margin.Bottom : default, in context, _margin.HasBottom);
        }

        if (paddingIsPercent)
        {
            state.HasPadding = true;
            state.PaddingLeft = ToLayoutLength(_padding.HasLeft ? _padding.Left : default, in context, _padding.HasLeft);
            state.PaddingTop = ToLayoutLength(_padding.HasTop ? _padding.Top : default, in context, _padding.HasTop);
            state.PaddingRight = ToLayoutLength(_padding.HasRight ? _padding.Right : default, in context, _padding.HasRight);
            state.PaddingBottom = ToLayoutLength(_padding.HasBottom ? _padding.Bottom : default, in context, _padding.HasBottom);
        }

        state.Seal();
        sink.SetLayoutState(state);
    }

    private void FlushStaticInset(
        CssLayoutLength length, DependencyProperty? compatDp, string cssName,
        in CssApplyContext context, ICssSetterSink sink)
    {
        if (!length.IsSet)
        {
            return;
        }

        if (length.IsPercent)
        {
            CssDiagnostics.Report(
                cssName, CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                "percentage offsets need position: absolute; declaration dropped");
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
        if (dp is null)
        {
            CssDiagnostics.Report(
                "border-radius", CssDiagnosticReason.TargetPropertyMissing, context.Element.GetType(),
                "no dependency property 'CornerRadius' on this element type; declaration skipped here");
            return;
        }

        var tl = ResolveCornerComponent(_corner.HasTopLeft, _corner.TopLeft, in context);
        var tr = ResolveCornerComponent(_corner.HasTopRight, _corner.TopRight, in context);
        var br = ResolveCornerComponent(_corner.HasBottomRight, _corner.BottomRight, in context);
        var bl = ResolveCornerComponent(_corner.HasBottomLeft, _corner.BottomLeft, in context);
        sink.Set(dp, new CornerRadius(tl, tr, br, bl));
    }

    private static double ResolveCornerComponent(bool has, CssLength length, in CssApplyContext context)
    {
        if (!has)
        {
            return 0;
        }

        if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px))
        {
            CssDiagnostics.Report(
                "border-radius", CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                "percentage corner radii are not supported; treated as 0");
            return 0;
        }

        return Math.Max(0, px);
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

        // CSS paints the image over the color; with a single Brush slot the image wins.
        var brush = _background.HasImage && _background.ImageBrush is not null
            ? _background.ImageBrush
            : _background.HasColor ? _background.ColorBrush : null;

        if (_background.HasImage && _background.ImageBrush is not null &&
            _background.HasColor && _background.ColorBrush is not null)
        {
            CssDiagnostics.Report(
                "background", CssDiagnosticReason.LossyConversion, context.Element.GetType(),
                "background-color underneath a background-image is not composited; the image brush wins");
        }

        if (brush is Jalium.UI.Media.ImageBrush imageBrush && _background.Stretch is Jalium.UI.Media.Stretch stretch)
        {
            imageBrush.Stretch = stretch;
        }

        sink.Set(dp, brush);
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
        in ThicknessSlot slot, in CssApplyContext context, string cssName, bool clampNonNegative)
    {
        var left = ResolveEdge(slot.HasLeft, slot.Left, in context, cssName);
        var top = ResolveEdge(slot.HasTop, slot.Top, in context, cssName);
        var right = ResolveEdge(slot.HasRight, slot.Right, in context, cssName);
        var bottom = ResolveEdge(slot.HasBottom, slot.Bottom, in context, cssName);
        if (clampNonNegative)
        {
            left = Math.Max(0, left);
            top = Math.Max(0, top);
            right = Math.Max(0, right);
            bottom = Math.Max(0, bottom);
        }

        return new Thickness(left, top, right, bottom);
    }

    private static double ResolveEdge(bool has, CssLength length, in CssApplyContext context, string cssName)
    {
        if (!has)
        {
            return 0;
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
