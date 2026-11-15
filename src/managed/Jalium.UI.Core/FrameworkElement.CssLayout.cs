using Jalium.UI.Styling;

namespace Jalium.UI;

internal readonly record struct CssNativeBoxContext(Size ContainingBlock, bool FillHeight = false);

public partial class FrameworkElement
{
    /// <summary>
    /// CSS layout state (%, aspect-ratio, box-sizing, position/inset). Null when CSS layout
    /// does not participate — the hot layout path pays a single null check. Written only by
    /// the CSS engine's apply diff (which also calls InvalidateMeasure).
    /// </summary>
    internal CssLayoutState? CssLayout;
    internal CssDisplayMode CssDisplayMode;
    internal bool CssInlineOuter;
    internal CssParentBoxContext? CssParentBox, LastCssMeasureBox, LastCssArrangeBox;
    // These parents supply a percentage basis while leaving margins to the child.
    internal CssNativeBoxContext? CssNativeBox, LastCssNativeArrangeBox;
    internal CssFlexItemState? CssFlexSizing;
    // Available while ArrangeOverride is placing children, before RenderSize is updated.
    internal Size CssArrangeSizeForChildren;
    internal Size CssRelativeLastContainingBlock;
    internal byte CssRelativeLastLocalInsetMask;
    internal bool CssRelativeLastPreferRight;
    internal bool CssRelativeHasArrangeInputs;

    internal override void OnEffectiveValueSourceChanged(DependencyProperty property)
    {
        base.OnEffectiveValueSourceChanged(property);
        CssSizingPresentation.SourceChanged(this, property);
        CssFlexItemSizing.SourceChanged(this, property);
        CssIntrinsicSourceChanged(property);
        // A CSS percentage and a native local size or box edge can share a DP sentinel.
        // The source transition still changes computed style, even when GetValue does not.
        if (!CssEngine.IsActive || CssNode.Existing(this) is null) return;
        if (property == VerticalAlignmentProperty && CssDisplayMode == CssDisplayMode.Table)
        {
            // Default native stretch and an authored stretch have different
            // table-height semantics even when the DP value itself is unchanged.
            InvalidateArrange();
            return;
        }
        if (property == Controls.FlexPanel.BasisProperty || property == WidthProperty || property == HeightProperty ||
            property == MinWidthProperty || property == MinHeightProperty ||
            property == MaxWidthProperty || property == MaxHeightProperty ||
            property == Controls.FlexPanel.RowSpacingProperty || property == Controls.FlexPanel.ColumnSpacingProperty ||
            property == Controls.Grid.RowSpacingProperty || property == Controls.Grid.ColumnSpacingProperty ||
            property == MarginProperty ||
            property == CssDependencyPropertyLookup.Find(GetType(), "Padding") ||
            property == CssDependencyPropertyLookup.Find(GetType(), "FontStretch") ||
            property == CssDependencyPropertyLookup.Find(GetType(), "FontStyle") ||
            property == CssDependencyPropertyLookup.Find(GetType(), "FontFamily"))
        {
            // A source-only change can alter cyclic sizing or box edges even
            // when the effective DP sentinel is still NaN or the same pixels.
            InvalidateMeasure();
            if (this is not Controls.TextBlock { IsCssTypographyProjection: true } and not CssGeneratedText { IsDocumentBoxContent: true } and not ICssStyledBox and not ICssAnonymousBox)
                CssEvaluationScheduler.InvalidateSubtree(this);
            return;
        }
        for (var edge = 0; edge < 4; edge++)
            if (property == CssSlotAccumulator.InsetCompatibilityProperty?.Invoke(edge))
            {
                CssEvaluationScheduler.InvalidateSubtree(this);
                return;
            }
    }

    internal bool CssRelativeArrangeInputsChanged(Size finalSlotSize)
    {
        if (CssLayout?.Position != CssPositionMode.Relative) return false;
        return !CssRelativeHasArrangeInputs ||
            CssRelativeLastContainingBlock != CssRelativeLayout.ContainingBlock(this, finalSlotSize) ||
            CssRelativeLastLocalInsetMask != CssRelativeLayout.LocalInsetMask(this) ||
            CssRelativeLastPreferRight != CssRelativeLayout.PreferRight(this);
    }
    internal double CssPreferredWidth(double basis) => ResolveEffectiveWidth(CssLayout, basis);
    internal double CssPreferredHeight(double basis) => ResolveEffectiveHeight(CssLayout, basis);
    internal bool CssHasDefiniteWidth(double basis)
    {
        if (CssRubyBoxMetrics.IgnoresSizes(this) && !HasLocalOrAnimatedValue(WidthProperty)) return false;
        if (CssProbingSize(true)) return false;
        var gridStretch = CssContentsLayout.BoxParent(this) is FrameworkElement { CssDisplayMode: CssDisplayMode.Grid } &&
            CssParentAlignment?.Horizontal is CssBoxAlignment.Normal or CssBoxAlignment.Stretch &&
            (!HasLocalOrAnimatedValue(HorizontalAlignmentProperty) || HorizontalAlignment == HorizontalAlignment.Stretch);
        if (!HasLocalOrAnimatedValue(WidthProperty) && CssPresentedSize(CssLayoutSlotField.Width).Calculation is { } calculation)
        {
            var behavior = calculation.SizingBasis;
            if (behavior.IsAuto && gridStretch) return true;
            return behavior.Kind == CssLayoutLength.KindStretch ? double.IsFinite(basis)
                : !behavior.IsAuto && !behavior.IsSizing && double.IsFinite(behavior.Resolve(basis, double.NaN));
        }
        if (CssHasIntrinsicPreferredSize(true)) return false;
        if (CssLayout is { IsOutOfFlow: true } outOfFlow &&
            CssAbsoluteLayout.HasBothHorizontalInsets(outOfFlow, this) &&
            (double.IsFinite(basis) || outOfFlow.Position == CssPositionMode.Fixed)) return true;
        return double.IsFinite(CssPreferredWidth(basis)) || gridStretch;
    }
    internal bool CssHasDefiniteHeight(double basis)
    {
        if (CssRubyBoxMetrics.IgnoresSizes(this) && !HasLocalOrAnimatedValue(HeightProperty)) return false;
        if (CssProbingSize(false)) return false;
        if (CssFlexSizing?.Current is { DefiniteHeight: { } definite }) return definite;
        if (CssTableLayout.TryCellDefiniteHeight(this, out var cellDefinite)) return cellDefinite;
        if (!HasLocalOrAnimatedValue(HeightProperty) && CssPresentedSize(CssLayoutSlotField.Height).Calculation is { } calculation)
        {
            var behavior = calculation.SizingBasis;
            if (behavior.IsAuto && CssContentsLayout.BoxParent(this) is FrameworkElement { CssDisplayMode: CssDisplayMode.Grid } &&
                CssParentAlignment?.Vertical is CssBoxAlignment.Normal or CssBoxAlignment.Stretch &&
                (!HasLocalOrAnimatedValue(VerticalAlignmentProperty) || VerticalAlignment == VerticalAlignment.Stretch)) return true;
            return behavior.Kind == CssLayoutLength.KindStretch ? double.IsFinite(basis)
                : !behavior.IsAuto && !behavior.IsSizing && double.IsFinite(behavior.Resolve(basis, double.NaN));
        }
        if (CssHasIntrinsicPreferredSize(false)) return false;
        if (CssLayout is { IsOutOfFlow: true } outOfFlow &&
            CssAbsoluteLayout.HasBothVerticalInsets(outOfFlow, this) &&
            (double.IsFinite(basis) || outOfFlow.Position == CssPositionMode.Fixed))
            return true;
        return double.IsFinite(CssPreferredHeight(basis)) ||
            CssContentsLayout.BoxParent(this) is FrameworkElement { CssDisplayMode: CssDisplayMode.Grid } &&
            CssParentAlignment?.Vertical is CssBoxAlignment.Normal or CssBoxAlignment.Stretch &&
            (!HasLocalOrAnimatedValue(VerticalAlignmentProperty) || VerticalAlignment == VerticalAlignment.Stretch);
    }
    internal Size CssChildContainingSize(Size innerAvailable)
    {
        if (CssTableLayout.TryCellContainingHeight(this, out var cellHeight))
            return new(innerAvailable.Width, cellHeight);
        var parentHeight = CssParentBox?.ContainingBlock.Height ??
            CssNativeBox?.ContainingBlock.Height ?? PreviousAvailableSize.Height;
        return new(innerAvailable.Width, CssHasDefiniteHeight(parentHeight) &&
            !CssHasIntrinsicPreferredSize(false) ? innerAvailable.Height : double.PositiveInfinity);
    }
    internal (double Min, double Max) CssWidthBounds(double basis) => ResolveEffectiveWidthBounds(CssLayout, basis);
    internal (double Min, double Max) CssHeightBounds(double basis) => ResolveEffectiveHeightBounds(CssLayout, basis);
    internal (CssBoxAlignment Horizontal, CssBoxAlignment Vertical)? CssParentAlignment;
    internal (CssBoxAlignment Horizontal, CssBoxAlignment Vertical)? LastCssParentAlignment;

    internal bool CssTableHeightUsesAllocation(double basis) =>
        ResolveCssParentVerticalAlignment(CssLayout, basis) == VerticalAlignment.Stretch &&
        (CssParentAlignment?.Vertical is CssBoxAlignment.Normal or CssBoxAlignment.Stretch ||
         CssNativeBox is { FillHeight: true } || HasValueAboveInherited(VerticalAlignmentProperty));

    private HorizontalAlignment ResolveCssParentHorizontalAlignment(CssLayoutState? layout, double basis)
    {
        if (CssParentAlignment is not { } alignment || HasLocalOrAnimatedValue(HorizontalAlignmentProperty)) return HorizontalAlignment;
        if (alignment.Horizontal == CssBoxAlignment.Normal &&
            GetValueSourceInternal(HorizontalAlignmentProperty).BaseValueSource != BaseValueSource.Default) return HorizontalAlignment;
        return alignment.Horizontal switch
        {
            CssBoxAlignment.Center => HorizontalAlignment.Center,
            CssBoxAlignment.End => HorizontalAlignment.Right,
            CssBoxAlignment.Normal or CssBoxAlignment.Stretch when double.IsNaN(ResolveEffectiveWidth(layout, basis)) => HorizontalAlignment.Stretch,
            _ => HorizontalAlignment.Left,
        };
    }

    private VerticalAlignment ResolveCssParentVerticalAlignment(CssLayoutState? layout, double basis)
    {
        if (CssParentAlignment is not { } alignment || HasLocalOrAnimatedValue(VerticalAlignmentProperty)) return VerticalAlignment;
        if (alignment.Vertical == CssBoxAlignment.Normal &&
            GetValueSourceInternal(VerticalAlignmentProperty).BaseValueSource != BaseValueSource.Default) return VerticalAlignment;
        return alignment.Vertical switch
        {
            CssBoxAlignment.Center => VerticalAlignment.Center,
            CssBoxAlignment.End or CssBoxAlignment.LastBaseline => VerticalAlignment.Bottom,
            CssBoxAlignment.Normal or CssBoxAlignment.Stretch when double.IsNaN(ResolveEffectiveHeight(layout, basis)) => VerticalAlignment.Stretch,
            _ => VerticalAlignment.Top,
        };
    }

    /// <summary>
    /// The element's chance to consume a CSS declaration by name — the interception
    /// protocol container types override to map CSS concepts onto their own properties
    /// (e.g. StackPanel maps flex-direction to Orientation). Called by the engine for
    /// registered intercepted properties and as the last resort before an
    /// unknown-property diagnostic. Return true when consumed. Implementations must
    /// write values through <paramref name="setter"/> (never SetValue), may read their
    /// own properties, and must not trigger layout synchronously.
    /// </summary>
    protected internal virtual bool TryApplyCssPropertyCore(
        string propertyName, string rawValue, in CssDeclarationSetter setter)
        => false;

    /// <summary>Margin resolved against the containing block (CSS %: basis is the block's width).</summary>
    private Thickness ResolveCssMargin(CssLayoutState layout, double basisWidth)
    {
        basisWidth = CssIntrinsicPercentage.EdgeBasis(this, basisWidth);
        return new(
            layout.MarginLeft.Resolve(basisWidth, 0),
            layout.MarginTop.Resolve(basisWidth, 0),
            layout.MarginRight.Resolve(basisWidth, 0),
            layout.MarginBottom.Resolve(basisWidth, 0));
    }

    /// <summary>
    /// The effective explicit width: the Width DP wins when set; otherwise a CSS percentage
    /// resolves against the basis (degrading to auto against an infinite basis); NaN = auto.
    /// box-sizing:content-box adds the padding+border chrome to explicit values so the
    /// framework's border-box measurement model receives the equivalent outer size.
    /// </summary>
    private double ResolveEffectiveWidth(CssLayoutState? layout, double basisWidth)
    {
        if (CssRubyBoxMetrics.IgnoresSizes(this) && !HasLocalOrAnimatedValue(WidthProperty)) return double.NaN;
        if (CssProbingSize(true)) return double.NaN;
        var computedWidth = CssPresentedSize(CssLayoutSlotField.Width);
        if (CssFlexItemSizing.MainSize(this, true) is { } flexWidth) return flexWidth;
        if (!HasLocalOrAnimatedValue(WidthProperty) && CssDisplayLayout.IsSubgridded(this, false)) return double.NaN;
        if (!HasLocalOrAnimatedValue(WidthProperty) && computedWidth.IsSet && CssIntrinsicPercentage.CyclicSize(this, computedWidth, true))
        {
            var size = CssIntrinsicPercentage.Resolve(this, computedWidth, true, double.NaN);
            return double.IsFinite(size) ? Math.Max(GetContentBoxChromeX(), size + (layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeX() : 0)) : size;
        }
        if (!HasLocalOrAnimatedValue(WidthProperty) && computedWidth.IsSizing)
            return CssResolveSizing(computedWidth, true, basisWidth, double.NaN);
        if (!HasLocalOrAnimatedValue(WidthProperty) && (CssSizingAnimationMask & 1) != 0)
        {
            var animated = computedWidth.IsAuto ? double.NaN : computedWidth.Resolve(basisWidth, double.NaN);
            return double.IsFinite(animated) ? Math.Max(GetContentBoxChromeX(), animated +
                (layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeX() : 0)) : animated;
        }
        var width = Width;
        if (double.IsNaN(width) && !HasLocalOrAnimatedValue(WidthProperty) && computedWidth.IsSet)
        {
            width = Math.Max(0, computedWidth.Resolve(basisWidth, double.NaN));
        }

        if (!double.IsNaN(width) && !HasLocalOrAnimatedValue(WidthProperty) && layout is { BoxSizing: CssBoxSizing.ContentBox })
        {
            width += GetContentBoxChromeX();
        }

        return width;
    }

    private double ResolveEffectiveHeight(CssLayoutState? layout, double basisHeight)
    {
        if (CssRubyBoxMetrics.IgnoresSizes(this) && !HasLocalOrAnimatedValue(HeightProperty)) return double.NaN;
        if (CssProbingSize(false)) return double.NaN;
        var computedHeight = CssPresentedSize(CssLayoutSlotField.Height);
        if (CssFlexItemSizing.MainSize(this, false) is { } flexHeight) return flexHeight;
        // A table cell's auto height contributes its content minimum. A calc-size
        // with an auto basis keeps that behavior rather than specifying a row minimum.
        if (!HasLocalOrAnimatedValue(HeightProperty) && computedHeight.Calculation is not null &&
            computedHeight.SizingBasis.IsAuto && CssTableLayout.Role(this) == CssDisplayMode.TableCell) return double.NaN;
        if (!HasLocalOrAnimatedValue(HeightProperty) && CssDisplayLayout.IsSubgridded(this, true)) return double.NaN;
        if (!HasLocalOrAnimatedValue(HeightProperty) && computedHeight.IsSet && CssIntrinsicPercentage.CyclicSize(this, computedHeight, false))
        {
            var size = CssIntrinsicPercentage.Resolve(this, computedHeight, false, double.NaN);
            return double.IsFinite(size) ? Math.Max(GetContentBoxChromeY(), size + (layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeY() : 0)) : size;
        }
        if (!HasLocalOrAnimatedValue(HeightProperty) && computedHeight.IsSizing)
            return CssResolveSizing(computedHeight, false, basisHeight, double.NaN);
        if (!HasLocalOrAnimatedValue(HeightProperty) && (CssSizingAnimationMask & 2) != 0)
        {
            var animated = computedHeight.IsAuto ? double.NaN : computedHeight.Resolve(basisHeight, double.NaN);
            return double.IsFinite(animated) ? Math.Max(GetContentBoxChromeY(), animated +
                (layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeY() : 0)) : animated;
        }
        var height = Height;
        if (double.IsNaN(height) && !HasLocalOrAnimatedValue(HeightProperty) && computedHeight.IsSet)
        {
            height = Math.Max(0, computedHeight.Resolve(basisHeight, double.NaN));
        }

        if (!double.IsNaN(height) && !HasLocalOrAnimatedValue(HeightProperty) && layout is { BoxSizing: CssBoxSizing.ContentBox })
        {
            height += GetContentBoxChromeY();
        }

        return height;
    }

    private (double Min, double Max) ResolveEffectiveWidthBounds(CssLayoutState? layout, double basisWidth)
    {
        if (CssRubyBoxMetrics.IgnoresSizes(this)) return (
            HasLocalOrAnimatedValue(MinWidthProperty) ? MinWidth : 0,
            HasLocalOrAnimatedValue(MaxWidthProperty) ? MaxWidth : double.PositiveInfinity);
        if (CssProbingSize(true) || CssFlexItemSizing.IgnoreBounds(this, true)) return (0, double.PositiveInfinity);
        if (CssDisplayLayout.IsSubgridded(this, false))
            return (HasLocalOrAnimatedValue(MinWidthProperty) ? MinWidth : 0,
                Math.Max(HasLocalOrAnimatedValue(MinWidthProperty) ? MinWidth : 0,
                    HasLocalOrAnimatedValue(MaxWidthProperty) ? MaxWidth : double.PositiveInfinity));
        var cssMinimum = CssPresentedSize(CssLayoutSlotField.MinWidth);
        var cssMaximum = CssPresentedSize(CssLayoutSlotField.MaxWidth);
        var min = MinWidth;
        var max = MaxWidth;
        if (!HasLocalOrAnimatedValue(MinWidthProperty) && (CssSizingAnimationMask & 4) != 0 && !cssMinimum.IsSizing)
            min = Math.Max(0, cssMinimum.Resolve(CssIntrinsicPercentage.MinimumBasis(basisWidth), 0));
        if (!HasLocalOrAnimatedValue(MaxWidthProperty) && (CssSizingAnimationMask & 16) != 0 && !cssMaximum.IsSizing)
            max = cssMaximum.Resolve(basisWidth, double.PositiveInfinity);
        if (layout is not null || CssSizingAnimationMask != 0)
        {
            var chrome = layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeX() : 0;
            if (!HasLocalOrAnimatedValue(MinWidthProperty) && cssMinimum.IsSizing)
                min = CssResolveSizing(cssMinimum, true, basisWidth, 0);
            if (!HasLocalOrAnimatedValue(MaxWidthProperty) && cssMaximum.IsSizing)
                max = CssResolveSizing(cssMaximum, true, basisWidth, double.PositiveInfinity);
            if (!HasLocalOrAnimatedValue(MinWidthProperty) && min == 0 && cssMinimum.IsSet)
            {
                min = Math.Max(0, cssMinimum.Resolve(CssIntrinsicPercentage.MinimumBasis(basisWidth), 0));
            }

            if (!HasLocalOrAnimatedValue(MaxWidthProperty) && double.IsPositiveInfinity(max) && cssMaximum.IsSet)
            {
                var resolved = cssMaximum.Resolve(basisWidth, double.PositiveInfinity);
                max = double.IsPositiveInfinity(resolved) ? resolved : Math.Max(0, resolved);
            }
            if (!HasLocalOrAnimatedValue(MinWidthProperty) && !cssMinimum.IsSizing) min += chrome;
            if (!HasLocalOrAnimatedValue(MaxWidthProperty) && !cssMaximum.IsSizing) max += chrome;
        }

        if (layout is not null || CssSizingAnimationMask != 0)
        {
            var chrome = layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeX() : 0;
            if (!HasLocalOrAnimatedValue(MinWidthProperty) && CssIntrinsicPercentage.CyclicSize(this, cssMinimum, true))
                min = CssIntrinsicPercentage.Resolve(this, cssMinimum, true, 0, true) + chrome;
            if (!HasLocalOrAnimatedValue(MaxWidthProperty) && CssIntrinsicPercentage.CyclicSize(this, cssMaximum, true))
                max = CssIntrinsicPercentage.Resolve(this, cssMaximum, true, double.PositiveInfinity) + chrome;
        }
        return (min, Math.Max(min, max));
    }

    private (double Min, double Max) ResolveEffectiveHeightBounds(CssLayoutState? layout, double basisHeight)
    {
        if (CssRubyBoxMetrics.IgnoresSizes(this)) return (
            HasLocalOrAnimatedValue(MinHeightProperty) ? MinHeight : 0,
            HasLocalOrAnimatedValue(MaxHeightProperty) ? MaxHeight : double.PositiveInfinity);
        if (CssProbingSize(false) || CssFlexItemSizing.IgnoreBounds(this, false)) return (0, double.PositiveInfinity);
        if (CssDisplayLayout.IsSubgridded(this, true))
            return (HasLocalOrAnimatedValue(MinHeightProperty) ? MinHeight : 0,
                Math.Max(HasLocalOrAnimatedValue(MinHeightProperty) ? MinHeight : 0,
                    HasLocalOrAnimatedValue(MaxHeightProperty) ? MaxHeight : double.PositiveInfinity));
        var cssMinimum = CssPresentedSize(CssLayoutSlotField.MinHeight);
        var cssMaximum = CssPresentedSize(CssLayoutSlotField.MaxHeight);
        var min = MinHeight;
        var max = MaxHeight;
        if (!HasLocalOrAnimatedValue(MinHeightProperty) && (CssSizingAnimationMask & 8) != 0 && !cssMinimum.IsSizing)
            min = Math.Max(0, cssMinimum.Resolve(CssIntrinsicPercentage.MinimumBasis(basisHeight), 0));
        if (!HasLocalOrAnimatedValue(MaxHeightProperty) && (CssSizingAnimationMask & 32) != 0 && !cssMaximum.IsSizing)
            max = cssMaximum.Resolve(basisHeight, double.PositiveInfinity);
        if (layout is not null || CssSizingAnimationMask != 0)
        {
            var chrome = layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeY() : 0;
            if (!HasLocalOrAnimatedValue(MinHeightProperty) && cssMinimum.IsSizing)
                min = CssResolveSizing(cssMinimum, false, basisHeight, 0);
            if (!HasLocalOrAnimatedValue(MaxHeightProperty) && cssMaximum.IsSizing)
                max = CssResolveSizing(cssMaximum, false, basisHeight, double.PositiveInfinity);
            if (!HasLocalOrAnimatedValue(MinHeightProperty) && min == 0 && cssMinimum.IsSet)
            {
                min = Math.Max(0, cssMinimum.Resolve(CssIntrinsicPercentage.MinimumBasis(basisHeight), 0));
            }

            if (!HasLocalOrAnimatedValue(MaxHeightProperty) && double.IsPositiveInfinity(max) && cssMaximum.IsSet)
            {
                var resolved = cssMaximum.Resolve(basisHeight, double.PositiveInfinity);
                max = double.IsPositiveInfinity(resolved) ? resolved : Math.Max(0, resolved);
            }
            if (!HasLocalOrAnimatedValue(MinHeightProperty) && !cssMinimum.IsSizing) min += chrome;
            if (!HasLocalOrAnimatedValue(MaxHeightProperty) && !cssMaximum.IsSizing) max += chrome;
        }

        if (layout is not null || CssSizingAnimationMask != 0)
        {
            var chrome = layout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeY() : 0;
            if (!HasLocalOrAnimatedValue(MinHeightProperty) && CssIntrinsicPercentage.CyclicSize(this, cssMinimum, false))
                min = CssIntrinsicPercentage.Resolve(this, cssMinimum, false, 0, true) + chrome;
            if (!HasLocalOrAnimatedValue(MaxHeightProperty) && CssIntrinsicPercentage.CyclicSize(this, cssMaximum, false))
                max = CssIntrinsicPercentage.Resolve(this, cssMaximum, false, double.PositiveInfinity) + chrome;
        }
        return (min, Math.Max(min, max));
    }

    /// <summary>aspect-ratio = width/height. Derives the auto axis from the determinate one.</summary>
    private (double Width, double Height) ApplyCssAspectRatio(
        double ratio, double width, bool widthDeterminate, double height, bool heightDeterminate)
    {
        var chromeX = CssLayout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeX() : 0;
        var chromeY = CssLayout?.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeY() : 0;
        width = Math.Max(0, width - chromeX); height = Math.Max(0, height - chromeY);
        var result = ApplyRatio(ratio, width, widthDeterminate, height, heightDeterminate);
        return (result.Width + chromeX, result.Height + chromeY);
    }

    private static (double Width, double Height) ApplyRatio(
        double ratio, double width, bool widthDeterminate, double height, bool heightDeterminate)
    {
        if (widthDeterminate && heightDeterminate)
        {
            return (width, height);
        }

        if (widthDeterminate)
        {
            return (width, width / ratio);
        }

        if (heightDeterminate)
        {
            return (height * ratio, height);
        }

        // Both auto: derive from the content's main axis (width first, per CSS block flow).
        if (width > 0)
        {
            return (width, width / ratio);
        }

        if (height > 0)
        {
            return (height * ratio, height);
        }

        return (width, height);
    }

    /// <summary>
    /// Resolves %-padding into the element's Padding DP (CssBase layer) right before
    /// MeasureOverride, so the padding consumers (Control/Border subclasses) see plain
    /// pixel values. Equal values are skipped to avoid invalidation churn.
    /// </summary>
    private void MaterializeCssPadding(CssLayoutState layout, double basisWidth)
    {
        basisWidth = CssIntrinsicPercentage.EdgeBasis(this, basisWidth);
        var dp = CssDependencyPropertyLookup.Find(GetType(), "Padding");
        if (dp is null)
        {
            if (IsCssContentHost || CssDisplayLayout.HasBoxFormatter(this) || CssReplacedContent.IsActive(this) || this is CssGeneratedText { IsImageReplacement: true } or Controls.Page or Controls.Image or Controls.MediaElement) return;
            CssDiagnostics.Report(
                "padding", CssDiagnosticReason.TargetPropertyMissing, GetType(),
                "no dependency property 'Padding' on this element type; declaration skipped here");
            return;
        }

        var thickness = new Thickness(
            Math.Max(0, layout.PaddingLeft.Resolve(basisWidth, 0)),
            Math.Max(0, layout.PaddingTop.Resolve(basisWidth, 0)),
            Math.Max(0, layout.PaddingRight.Resolve(basisWidth, 0)),
            Math.Max(0, layout.PaddingBottom.Resolve(basisWidth, 0)));

        if (TryGetLayerValue(dp, LayerValueSource.CssBase, out var existing) &&
            existing is Thickness current && current == thickness)
        {
            return;
        }

        SetLayerValue(dp, thickness, LayerValueSource.CssBase, allowAutoTransition: false);
    }

    private double GetContentBoxChromeX()
    {
        if (IsCssContentHost || CssDisplayLayout.HasBoxFormatter(this) || CssReplacedContent.IsActive(this) || this is CssGeneratedText { IsImageReplacement: true } or Controls.Page or Controls.Image or Controls.MediaElement)
        {
            var insets = CssBoxMetrics.ContentInsets(this, CssLayout?.ContainingWidthCache ?? double.NaN);
            return insets.Left + insets.Right;
        }
        double chrome = 0;
        if (CssDependencyPropertyLookup.Find(GetType(), "Padding") is { } paddingDp &&
            GetValue(paddingDp) is Thickness padding)
        {
            chrome += padding.Left + padding.Right;
        }

        if (CssDependencyPropertyLookup.Find(GetType(), "BorderThickness") is { } borderDp &&
            GetValue(borderDp) is Thickness border)
        {
            chrome += border.Left + border.Right;
        }
        else
        {
            var cssBorder = CssBorderUsedThicknessProperties.Get(this);
            chrome += cssBorder.Left + cssBorder.Right;
        }

        return chrome;
    }

    private double GetContentBoxChromeY()
    {
        if (IsCssContentHost || CssDisplayLayout.HasBoxFormatter(this) || CssReplacedContent.IsActive(this) || this is CssGeneratedText { IsImageReplacement: true } or Controls.Page or Controls.Image or Controls.MediaElement)
        {
            var insets = CssBoxMetrics.ContentInsets(this, CssLayout?.ContainingWidthCache ?? double.NaN);
            return insets.Top + insets.Bottom;
        }
        double chrome = 0;
        if (CssDependencyPropertyLookup.Find(GetType(), "Padding") is { } paddingDp &&
            GetValue(paddingDp) is Thickness padding)
        {
            chrome += padding.Top + padding.Bottom;
        }

        if (CssDependencyPropertyLookup.Find(GetType(), "BorderThickness") is { } borderDp &&
            GetValue(borderDp) is Thickness border)
        {
            chrome += border.Top + border.Bottom;
        }
        else
        {
            var cssBorder = CssBorderUsedThicknessProperties.Get(this);
            chrome += cssBorder.Top + cssBorder.Bottom;
        }

        return chrome;
    }
}
