using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Axis selection for <c>scroll-snap-type</c>.</summary>
public enum CssScrollSnapAxis : byte
{
    None = 0,
    X,
    Y,
    Block,
    Inline,
    Both,
}

/// <summary>Snap strictness for <c>scroll-snap-type</c>.</summary>
public enum CssScrollSnapStrictness : byte
{
    None = 0,
    Proximity,
    Mandatory,
}

/// <summary>Parsed <c>scroll-snap-type</c> value combining axis and strictness.</summary>
public readonly record struct CssScrollSnapType(CssScrollSnapAxis Axis, CssScrollSnapStrictness Strictness)
{
    public static CssScrollSnapType None => new(CssScrollSnapAxis.None, CssScrollSnapStrictness.None);

    public bool IsEnabled => Axis != CssScrollSnapAxis.None && Strictness != CssScrollSnapStrictness.None;
}

/// <summary>Alignment keyword for one axis of <c>scroll-snap-align</c>.</summary>
public enum CssScrollSnapAlignment : byte
{
    None = 0,
    Start,
    End,
    Center,
}

/// <summary>Two-axis <c>scroll-snap-align</c> value (block and inline axes).</summary>
public readonly record struct CssScrollSnapAlign(CssScrollSnapAlignment Block, CssScrollSnapAlignment Inline)
{
    public static CssScrollSnapAlign None => new(CssScrollSnapAlignment.None, CssScrollSnapAlignment.None);
}

/// <summary>Snap stopping behavior for <c>scroll-snap-stop</c>.</summary>
public enum CssScrollSnapStop : byte
{
    Normal = 0,
    Always,
}

/// <summary>Scroll behavior for <c>scroll-behavior</c>.</summary>
public enum CssScrollBehavior : byte
{
    Auto = 0,
    Smooth,
}

/// <summary>Boundary chaining and bounce behavior for <c>overscroll-behavior</c>.</summary>
public enum CssOverscrollBehavior : byte
{
    Auto = 0,
    Contain,
    None,
}

/// <summary>Scroll anchoring mode for <c>overflow-anchor</c>.</summary>
public enum CssOverflowAnchor : byte
{
    Auto = 0,
    None,
}

/// <summary>
/// CSS Scroll Snap Module Level 1, CSS Overscroll Behavior Module Level 1,
/// CSS Scroll Anchoring Module Level 1, and <c>scroll-behavior</c> properties and runtime helpers.
/// </summary>
public static class CssScrollProperties
{
    private static bool s_classHandlersRegistered;

    public static readonly DependencyProperty ScrollSnapTypeProperty = DependencyProperty.RegisterAttached(
        "ScrollSnapType",
        typeof(CssScrollSnapType),
        typeof(CssScrollProperties),
        new PropertyMetadata(CssScrollSnapType.None, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollSnapAlignProperty = DependencyProperty.RegisterAttached(
        "ScrollSnapAlign",
        typeof(CssScrollSnapAlign),
        typeof(CssScrollProperties),
        new PropertyMetadata(CssScrollSnapAlign.None, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollSnapStopProperty = DependencyProperty.RegisterAttached(
        "ScrollSnapStop",
        typeof(CssScrollSnapStop),
        typeof(CssScrollProperties),
        new PropertyMetadata(CssScrollSnapStop.Normal, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollMarginTopProperty = DependencyProperty.RegisterAttached(
        "ScrollMarginTop",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollMarginRightProperty = DependencyProperty.RegisterAttached(
        "ScrollMarginRight",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollMarginBottomProperty = DependencyProperty.RegisterAttached(
        "ScrollMarginBottom",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollMarginLeftProperty = DependencyProperty.RegisterAttached(
        "ScrollMarginLeft",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollPaddingTopProperty = DependencyProperty.RegisterAttached(
        "ScrollPaddingTop",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollPaddingRightProperty = DependencyProperty.RegisterAttached(
        "ScrollPaddingRight",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollPaddingBottomProperty = DependencyProperty.RegisterAttached(
        "ScrollPaddingBottom",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollPaddingLeftProperty = DependencyProperty.RegisterAttached(
        "ScrollPaddingLeft",
        typeof(double),
        typeof(CssScrollProperties),
        new PropertyMetadata(0.0, OnScrollPropertyChanged));

    public static readonly DependencyProperty ScrollBehaviorProperty = DependencyProperty.RegisterAttached(
        "ScrollBehavior",
        typeof(CssScrollBehavior),
        typeof(CssScrollProperties),
        new PropertyMetadata(CssScrollBehavior.Auto, OnScrollBehaviorChanged));

    public static readonly DependencyProperty OverscrollBehaviorXProperty = DependencyProperty.RegisterAttached(
        "OverscrollBehaviorX",
        typeof(CssOverscrollBehavior),
        typeof(CssScrollProperties),
        new PropertyMetadata(CssOverscrollBehavior.Auto, OnScrollPropertyChanged));

    public static readonly DependencyProperty OverscrollBehaviorYProperty = DependencyProperty.RegisterAttached(
        "OverscrollBehaviorY",
        typeof(CssOverscrollBehavior),
        typeof(CssScrollProperties),
        new PropertyMetadata(CssOverscrollBehavior.Auto, OnScrollPropertyChanged));

    public static readonly DependencyProperty OverflowAnchorProperty = DependencyProperty.RegisterAttached(
        "OverflowAnchor",
        typeof(CssOverflowAnchor),
        typeof(CssScrollProperties),
        new PropertyMetadata(CssOverflowAnchor.Auto, OnScrollPropertyChanged));

    private sealed class ScrollBoxState
    {
        public Point Offset;
        public WeakReference<FrameworkElement>? AnchorElement;
        public Point AnchorContentPosition;
        public Point AnchorSavedScrollOffset;
        public bool HasCapturedAnchor;
        public bool SuppressAnchorUntilArrange;
        public double? PaddingLeftPercent;
        public double? PaddingTopPercent;
        public double? PaddingRightPercent;
        public double? PaddingBottomPercent;
        public readonly ConditionalWeakTable<FrameworkElement, StrongBox<Rect>> ChildTranslatedBounds = new();
        public readonly ConditionalWeakTable<FrameworkElement, StrongBox<Rect>> ChildUnscrolledBounds = new();
        public bool IsRestoringAnchor;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, ScrollBoxState> s_states = new();

    public static CssScrollSnapType GetScrollSnapType(DependencyObject target) =>
        (CssScrollSnapType)target.GetValue(ScrollSnapTypeProperty)!;

    public static void SetScrollSnapType(DependencyObject target, CssScrollSnapType value) =>
        target.SetValue(ScrollSnapTypeProperty, value);

    public static CssScrollSnapAlign GetScrollSnapAlign(DependencyObject target) =>
        (CssScrollSnapAlign)target.GetValue(ScrollSnapAlignProperty)!;

    public static void SetScrollSnapAlign(DependencyObject target, CssScrollSnapAlign value) =>
        target.SetValue(ScrollSnapAlignProperty, value);

    public static CssScrollSnapStop GetScrollSnapStop(DependencyObject target) =>
        (CssScrollSnapStop)target.GetValue(ScrollSnapStopProperty)!;

    public static void SetScrollSnapStop(DependencyObject target, CssScrollSnapStop value) =>
        target.SetValue(ScrollSnapStopProperty, value);

    public static Thickness GetScrollMargin(DependencyObject target) =>
        new(
            (double)target.GetValue(ScrollMarginLeftProperty)!,
            (double)target.GetValue(ScrollMarginTopProperty)!,
            (double)target.GetValue(ScrollMarginRightProperty)!,
            (double)target.GetValue(ScrollMarginBottomProperty)!);

    public static void SetScrollMargin(DependencyObject target, Thickness value)
    {
        target.SetValue(ScrollMarginLeftProperty, value.Left);
        target.SetValue(ScrollMarginTopProperty, value.Top);
        target.SetValue(ScrollMarginRightProperty, value.Right);
        target.SetValue(ScrollMarginBottomProperty, value.Bottom);
    }

    public static Thickness GetScrollPadding(DependencyObject target) =>
        GetScrollPadding(target, null);

    public static Thickness GetScrollPadding(DependencyObject target, Size? viewportSize)
    {
        var left = (double)target.GetValue(ScrollPaddingLeftProperty)!;
        var top = (double)target.GetValue(ScrollPaddingTopProperty)!;
        var right = (double)target.GetValue(ScrollPaddingRightProperty)!;
        var bottom = (double)target.GetValue(ScrollPaddingBottomProperty)!;

        if (target is FrameworkElement fe && s_states.TryGetValue(fe, out var state))
        {
            var vw = viewportSize?.Width ?? (fe is ScrollViewer sv ? sv.ViewportWidth : fe.ActualWidth);
            var vh = viewportSize?.Height ?? (fe is ScrollViewer svH ? svH.ViewportHeight : fe.ActualHeight);
            if (state.PaddingLeftPercent is { } lp && double.IsFinite(vw) && vw > 0) left = vw * lp;
            if (state.PaddingRightPercent is { } rp && double.IsFinite(vw) && vw > 0) right = vw * rp;
            if (state.PaddingTopPercent is { } tp && double.IsFinite(vh) && vh > 0) top = vh * tp;
            if (state.PaddingBottomPercent is { } bp && double.IsFinite(vh) && vh > 0) bottom = vh * bp;
        }

        return new Thickness(left, top, right, bottom);
    }

    public static void SetScrollPadding(DependencyObject target, Thickness value)
    {
        target.SetValue(ScrollPaddingLeftProperty, Math.Max(0, value.Left));
        target.SetValue(ScrollPaddingTopProperty, Math.Max(0, value.Top));
        target.SetValue(ScrollPaddingRightProperty, Math.Max(0, value.Right));
        target.SetValue(ScrollPaddingBottomProperty, Math.Max(0, value.Bottom));
    }

    public static CssScrollBehavior GetScrollBehavior(DependencyObject target) =>
        (CssScrollBehavior)target.GetValue(ScrollBehaviorProperty)!;

    public static void SetScrollBehavior(DependencyObject target, CssScrollBehavior value) =>
        target.SetValue(ScrollBehaviorProperty, value);

    public static CssOverscrollBehavior GetOverscrollBehaviorX(DependencyObject target) =>
        (CssOverscrollBehavior)target.GetValue(OverscrollBehaviorXProperty)!;

    public static void SetOverscrollBehaviorX(DependencyObject target, CssOverscrollBehavior value) =>
        target.SetValue(OverscrollBehaviorXProperty, value);

    public static CssOverscrollBehavior GetOverscrollBehaviorY(DependencyObject target) =>
        (CssOverscrollBehavior)target.GetValue(OverscrollBehaviorYProperty)!;

    public static void SetOverscrollBehaviorY(DependencyObject target, CssOverscrollBehavior value) =>
        target.SetValue(OverscrollBehaviorYProperty, value);

    public static CssOverflowAnchor GetOverflowAnchor(DependencyObject target) =>
        (CssOverflowAnchor)target.GetValue(OverflowAnchorProperty)!;

    public static void SetOverflowAnchor(DependencyObject target, CssOverflowAnchor value) =>
        target.SetValue(OverflowAnchorProperty, value);

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Registers R1 scroll CSS properties into CssPropertyRegistry before style sheets are parsed.")]
    internal static void Hook()
        => CssPropertyRegistry.RegisterExtension(Register);

    public static void Register()
    {
        EnsureClassHandlersRegistered();

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "scroll-snap-type",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ScrollSnapTypeProperty,
            Parse = ParseScrollSnapType,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "scroll-snap-align",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ScrollSnapAlignProperty,
            Parse = ParseScrollSnapAlign,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "scroll-snap-stop",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ScrollSnapStopProperty,
            Parse = ParseScrollSnapStop,
        });

        RegisterScrollMarginProperties();
        RegisterScrollPaddingProperties();

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "scroll-behavior",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ScrollBehaviorProperty,
            Parse = ParseScrollBehavior,
        });

        RegisterOverscrollBehaviorProperties();

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overflow-anchor",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = OverflowAnchorProperty,
            Parse = ParseOverflowAnchor,
        });
    }

    private static void EnsureClassHandlersRegistered()
    {
        if (s_classHandlersRegistered)
            return;
        s_classHandlersRegistered = true;

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            UIElement.MouseWheelEvent,
            new MouseWheelEventHandler(OnFrameworkElementMouseWheel),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(OnFrameworkElementBringIntoView),
            handledEventsToo: false);

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler(OnFrameworkElementSizeChanged),
            handledEventsToo: true);
    }

    private static void OnScrollPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
        {
            sv.EnsureCssScrollHandlersAttached();
        }
        else if (d is FrameworkElement fe && fe.VisualParent is ScrollViewer parentSv)
        {
            parentSv.EnsureCssScrollHandlersAttached();
        }
    }

    private static void OnScrollBehaviorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv && e.NewValue is CssScrollBehavior behavior)
        {
            sv.EnsureCssScrollHandlersAttached();
            if (!sv.HasLocalValue(ScrollViewer.IsScrollInertiaEnabledProperty))
            {
                sv.SetLayerValue(
                    ScrollViewer.IsScrollInertiaEnabledProperty,
                    behavior == CssScrollBehavior.Smooth,
                    DependencyObject.LayerValueSource.ParentTemplate,
                    allowAutoTransition: false);
            }
        }
    }

    private static CssCompiledValue? ParseScrollSnapType(ref CssTokenReader reader, CssCompileContext _)
    {
        if (!reader.TryReadIdent(out var firstSpan))
            return null;
        var first = firstSpan.ToString().ToLowerInvariant();
        if (first == "none")
            return reader.AtEnd ? new CssImmediateValue(ScrollSnapTypeProperty, CssScrollSnapType.None) : null;

        if (!TryMapSnapAxis(first, out var axis))
            return null;

        var strictness = CssScrollSnapStrictness.Proximity;
        if (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var secondSpan) || !reader.AtEnd)
                return null;
            var second = secondSpan.ToString().ToLowerInvariant();
            strictness = second switch
            {
                "mandatory" => CssScrollSnapStrictness.Mandatory,
                "proximity" => CssScrollSnapStrictness.Proximity,
                _ => CssScrollSnapStrictness.None,
            };
            if (strictness == CssScrollSnapStrictness.None)
                return null;
        }

        return new CssImmediateValue(ScrollSnapTypeProperty, new CssScrollSnapType(axis, strictness));
    }

    private static bool TryMapSnapAxis(string keyword, out CssScrollSnapAxis axis)
    {
        axis = keyword switch
        {
            "x" => CssScrollSnapAxis.X,
            "y" => CssScrollSnapAxis.Y,
            "block" => CssScrollSnapAxis.Block,
            "inline" => CssScrollSnapAxis.Inline,
            "both" => CssScrollSnapAxis.Both,
            _ => CssScrollSnapAxis.None,
        };
        return axis != CssScrollSnapAxis.None;
    }

    private static CssCompiledValue? ParseScrollSnapAlign(ref CssTokenReader reader, CssCompileContext _)
    {
        if (!reader.TryReadIdent(out var firstSpan) || !TryMapSnapAlignment(firstSpan.ToString(), out var block))
            return null;

        var inline = block;
        if (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var secondSpan) || !reader.AtEnd ||
                !TryMapSnapAlignment(secondSpan.ToString(), out inline))
                return null;
        }

        return new CssImmediateValue(ScrollSnapAlignProperty, new CssScrollSnapAlign(block, inline));
    }

    private static bool TryMapSnapAlignment(string keyword, out CssScrollSnapAlignment alignment)
    {
        switch (keyword.ToLowerInvariant())
        {
            case "none":
                alignment = CssScrollSnapAlignment.None;
                return true;
            case "start":
                alignment = CssScrollSnapAlignment.Start;
                return true;
            case "end":
                alignment = CssScrollSnapAlignment.End;
                return true;
            case "center":
                alignment = CssScrollSnapAlignment.Center;
                return true;
            default:
                alignment = CssScrollSnapAlignment.None;
                return false;
        }
    }

    private static CssCompiledValue? ParseScrollSnapStop(ref CssTokenReader reader, CssCompileContext _)
    {
        if (!reader.TryReadIdent(out var span) || !reader.AtEnd)
            return null;
        return span.ToString().ToLowerInvariant() switch
        {
            "normal" => new CssImmediateValue(ScrollSnapStopProperty, CssScrollSnapStop.Normal),
            "always" => new CssImmediateValue(ScrollSnapStopProperty, CssScrollSnapStop.Always),
            _ => null,
        };
    }

    private static CssCompiledValue? ParseScrollBehavior(ref CssTokenReader reader, CssCompileContext _)
    {
        if (!reader.TryReadIdent(out var span) || !reader.AtEnd)
            return null;
        return span.ToString().ToLowerInvariant() switch
        {
            "auto" => new CssImmediateValue(ScrollBehaviorProperty, CssScrollBehavior.Auto),
            "smooth" => new CssImmediateValue(ScrollBehaviorProperty, CssScrollBehavior.Smooth),
            _ => null,
        };
    }

    private static CssCompiledValue? ParseOverflowAnchor(ref CssTokenReader reader, CssCompileContext _)
    {
        if (!reader.TryReadIdent(out var span) || !reader.AtEnd)
            return null;
        return span.ToString().ToLowerInvariant() switch
        {
            "auto" => new CssImmediateValue(OverflowAnchorProperty, CssOverflowAnchor.Auto),
            "none" => new CssImmediateValue(OverflowAnchorProperty, CssOverflowAnchor.None),
            _ => null,
        };
    }

    private static void RegisterOverscrollBehaviorProperties()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overscroll-behavior-x",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = OverscrollBehaviorXProperty,
            Parse = (ref CssTokenReader r, CssCompileContext _) =>
                TryReadOverscrollKeyword(ref r, out var mode) && r.AtEnd
                    ? new CssImmediateValue(OverscrollBehaviorXProperty, mode)
                    : null,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overscroll-behavior-y",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = OverscrollBehaviorYProperty,
            Parse = (ref CssTokenReader r, CssCompileContext _) =>
                TryReadOverscrollKeyword(ref r, out var mode) && r.AtEnd
                    ? new CssImmediateValue(OverscrollBehaviorYProperty, mode)
                    : null,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overscroll-behavior-inline",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader r, CssCompileContext _) =>
                TryReadOverscrollKeyword(ref r, out var mode) && r.AtEnd
                    ? new LogicalOverscrollValue(isInline: true, mode)
                    : null,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overscroll-behavior-block",
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader r, CssCompileContext _) =>
                TryReadOverscrollKeyword(ref r, out var mode) && r.AtEnd
                    ? new LogicalOverscrollValue(isInline: false, mode)
                    : null,
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "overscroll-behavior",
            Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader r, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                if (!TryReadOverscrollKeyword(ref r, out var first))
                    return false;
                var second = first;
                if (!r.AtEnd && (!TryReadOverscrollKeyword(ref r, out second) || !r.AtEnd))
                    return false;
                output.Add(new CssCompiledDeclaration("overscroll-behavior-x", new CssImmediateValue(OverscrollBehaviorXProperty, first), false));
                output.Add(new CssCompiledDeclaration("overscroll-behavior-y", new CssImmediateValue(OverscrollBehaviorYProperty, second), false));
                return true;
            },
        });
    }

    private static bool TryReadOverscrollKeyword(ref CssTokenReader reader, out CssOverscrollBehavior behavior)
    {
        behavior = CssOverscrollBehavior.Auto;
        if (!reader.TryReadIdent(out var span))
            return false;
        switch (span.ToString().ToLowerInvariant())
        {
            case "auto":
                behavior = CssOverscrollBehavior.Auto;
                return true;
            case "contain":
                behavior = CssOverscrollBehavior.Contain;
                return true;
            case "none":
                behavior = CssOverscrollBehavior.None;
                return true;
            default:
                return false;
        }
    }

    private sealed class LogicalOverscrollValue(bool isInline, CssOverscrollBehavior mode) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var wm = CssWritingModeProperties.GetWritingMode(context.Element.Target);
            var vertical = CssWritingModeProperties.IsVertical(wm);
            var mapsToX = isInline ? !vertical : vertical;
            sink.Set(mapsToX ? OverscrollBehaviorXProperty : OverscrollBehaviorYProperty, mode);
            return true;
        }
    }

    private static void RegisterScrollMarginProperties()
    {
        RegisterScrollEdgeLonghand("scroll-margin-top", ScrollMarginTopProperty, allowPercent: false, allowAuto: false, allowNegative: true);
        RegisterScrollEdgeLonghand("scroll-margin-right", ScrollMarginRightProperty, allowPercent: false, allowAuto: false, allowNegative: true);
        RegisterScrollEdgeLonghand("scroll-margin-bottom", ScrollMarginBottomProperty, allowPercent: false, allowAuto: false, allowNegative: true);
        RegisterScrollEdgeLonghand("scroll-margin-left", ScrollMarginLeftProperty, allowPercent: false, allowAuto: false, allowNegative: true);

        RegisterLogicalScrollEdgeLonghand("scroll-margin-block-start", "block-start", isPadding: false);
        RegisterLogicalScrollEdgeLonghand("scroll-margin-block-end", "block-end", isPadding: false);
        RegisterLogicalScrollEdgeLonghand("scroll-margin-inline-start", "inline-start", isPadding: false);
        RegisterLogicalScrollEdgeLonghand("scroll-margin-inline-end", "inline-end", isPadding: false);

        RegisterLogicalPairShorthand("scroll-margin-block", "scroll-margin-block-start", "scroll-margin-block-end", "block-start", "block-end", isPadding: false);
        RegisterLogicalPairShorthand("scroll-margin-inline", "scroll-margin-inline-start", "scroll-margin-inline-end", "inline-start", "inline-end", isPadding: false);

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "scroll-margin",
            Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader r, CssCompileContext _, List<CssCompiledDeclaration> output) =>
                ExpandFourEdgeShorthand(ref r, "scroll-margin", allowPercent: false, allowAuto: false, allowNegative: true,
                    ScrollMarginTopProperty, ScrollMarginRightProperty, ScrollMarginBottomProperty, ScrollMarginLeftProperty, output),
        });
    }

    private static void RegisterScrollPaddingProperties()
    {
        RegisterScrollEdgeLonghand("scroll-padding-top", ScrollPaddingTopProperty, allowPercent: true, allowAuto: true, allowNegative: false);
        RegisterScrollEdgeLonghand("scroll-padding-right", ScrollPaddingRightProperty, allowPercent: true, allowAuto: true, allowNegative: false);
        RegisterScrollEdgeLonghand("scroll-padding-bottom", ScrollPaddingBottomProperty, allowPercent: true, allowAuto: true, allowNegative: false);
        RegisterScrollEdgeLonghand("scroll-padding-left", ScrollPaddingLeftProperty, allowPercent: true, allowAuto: true, allowNegative: false);

        RegisterLogicalScrollEdgeLonghand("scroll-padding-block-start", "block-start", isPadding: true);
        RegisterLogicalScrollEdgeLonghand("scroll-padding-block-end", "block-end", isPadding: true);
        RegisterLogicalScrollEdgeLonghand("scroll-padding-inline-start", "inline-start", isPadding: true);
        RegisterLogicalScrollEdgeLonghand("scroll-padding-inline-end", "inline-end", isPadding: true);

        RegisterLogicalPairShorthand("scroll-padding-block", "scroll-padding-block-start", "scroll-padding-block-end", "block-start", "block-end", isPadding: true);
        RegisterLogicalPairShorthand("scroll-padding-inline", "scroll-padding-inline-start", "scroll-padding-inline-end", "inline-start", "inline-end", isPadding: true);

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "scroll-padding",
            Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader r, CssCompileContext _, List<CssCompiledDeclaration> output) =>
                ExpandFourEdgeShorthand(ref r, "scroll-padding", allowPercent: true, allowAuto: true, allowNegative: false,
                    ScrollPaddingTopProperty, ScrollPaddingRightProperty, ScrollPaddingBottomProperty, ScrollPaddingLeftProperty, output),
        });
    }

    private static void RegisterScrollEdgeLonghand(
        string name,
        DependencyProperty property,
        bool allowPercent,
        bool allowAuto,
        bool allowNegative)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = property,
            Parse = (ref CssTokenReader r, CssCompileContext _) =>
                TryReadScrollEdgeLength(ref r, allowPercent, allowAuto, allowNegative, out var length) && r.AtEnd
                    ? new ScrollEdgeCompiledValue(property, null, isPadding: allowAuto, length)
                    : null,
        });
    }

    private static void RegisterLogicalScrollEdgeLonghand(string name, string logicalSide, bool isPadding)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader r, CssCompileContext _) =>
                TryReadScrollEdgeLength(ref r, allowPercent: isPadding, allowAuto: isPadding, allowNegative: !isPadding, out var length) && r.AtEnd
                    ? new ScrollEdgeCompiledValue(null, logicalSide, isPadding, length)
                    : null,
        });
    }

    private static void RegisterLogicalPairShorthand(
        string shorthandName,
        string startName,
        string endName,
        string startSide,
        string endSide,
        bool isPadding)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = shorthandName,
            Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader r, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                if (!TryReadScrollEdgeLength(ref r, allowPercent: isPadding, allowAuto: isPadding, allowNegative: !isPadding, out var first))
                    return false;
                var second = first;
                if (!r.AtEnd && (!TryReadScrollEdgeLength(ref r, allowPercent: isPadding, allowAuto: isPadding, allowNegative: !isPadding, out second) || !r.AtEnd))
                    return false;
                output.Add(new CssCompiledDeclaration(startName, new ScrollEdgeCompiledValue(null, startSide, isPadding, first), false));
                output.Add(new CssCompiledDeclaration(endName, new ScrollEdgeCompiledValue(null, endSide, isPadding, second), false));
                return true;
            },
        });
    }

    private static bool ExpandFourEdgeShorthand(
        ref CssTokenReader reader,
        string prefix,
        bool allowPercent,
        bool allowAuto,
        bool allowNegative,
        DependencyProperty topDp,
        DependencyProperty rightDp,
        DependencyProperty bottomDp,
        DependencyProperty leftDp,
        List<CssCompiledDeclaration> output)
    {
        var parts = new CssLength[4];
        var count = 0;
        while (!reader.AtEnd)
        {
            if (count == 4 || !TryReadScrollEdgeLength(ref reader, allowPercent, allowAuto, allowNegative, out parts[count]))
                return false;
            count++;
        }

        if (count == 0)
            return false;

        var (top, right, bottom, left) = count switch
        {
            1 => (parts[0], parts[0], parts[0], parts[0]),
            2 => (parts[0], parts[1], parts[0], parts[1]),
            3 => (parts[0], parts[1], parts[2], parts[1]),
            _ => (parts[0], parts[1], parts[2], parts[3]),
        };

        var isPadding = allowAuto;
        output.Add(new CssCompiledDeclaration($"{prefix}-top", new ScrollEdgeCompiledValue(topDp, null, isPadding, top), false));
        output.Add(new CssCompiledDeclaration($"{prefix}-right", new ScrollEdgeCompiledValue(rightDp, null, isPadding, right), false));
        output.Add(new CssCompiledDeclaration($"{prefix}-bottom", new ScrollEdgeCompiledValue(bottomDp, null, isPadding, bottom), false));
        output.Add(new CssCompiledDeclaration($"{prefix}-left", new ScrollEdgeCompiledValue(leftDp, null, isPadding, left), false));
        return true;
    }

    private static bool TryReadScrollEdgeLength(
        ref CssTokenReader reader,
        bool allowPercent,
        bool allowAuto,
        bool allowNegative,
        out CssLength length)
    {
        if (allowAuto)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                length = new CssLength(0, CssUnit.Auto);
                reader = probe;
                return true;
            }
        }

        if (!reader.TryReadLength(out length))
            return false;
        if (length.Unit == CssUnit.None && length.Value != 0)
            return false;
        if (!allowNegative && length.Expression is null && length.Value < 0)
            return false;
        if (length.Unit == CssUnit.Percent)
            return allowPercent;
        return length.IsLengthUnit || (length.Unit == CssUnit.None && length.Value == 0);
    }

    private sealed class ScrollEdgeCompiledValue(
        DependencyProperty? physicalProperty,
        string? logicalSide,
        bool isPadding,
        CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var target = context.Element.Target;
            var dp = physicalProperty;
            if (dp is null && logicalSide is not null)
            {
                var wm = CssWritingModeProperties.GetWritingMode(target);
                var physSide = CssLogicalBoxEdges.PhysicalSide(logicalSide, context.Slots.LogicalRightToLeft, wm);
                dp = (isPadding, physSide) switch
                {
                    (false, "top") => ScrollMarginTopProperty,
                    (false, "right") => ScrollMarginRightProperty,
                    (false, "bottom") => ScrollMarginBottomProperty,
                    (false, "left") => ScrollMarginLeftProperty,
                    (true, "top") => ScrollPaddingTopProperty,
                    (true, "right") => ScrollPaddingRightProperty,
                    (true, "bottom") => ScrollPaddingBottomProperty,
                    (true, "left") => ScrollPaddingLeftProperty,
                    _ => null,
                };
            }

            if (dp is null)
                return false;

            double pixels = 0;
            double? pct = null;
            if (length.Unit == CssUnit.Auto)
            {
                pixels = 0;
            }
            else if (length.Unit == CssUnit.Percent)
            {
                pct = length.Value / 100.0;
                if (target is FrameworkElement fe)
                {
                    var basis = dp == ScrollPaddingLeftProperty || dp == ScrollPaddingRightProperty
                        ? (fe is ScrollViewer svW && svW.ViewportWidth > 0 ? svW.ViewportWidth : fe.ActualWidth)
                        : (fe is ScrollViewer svH && svH.ViewportHeight > 0 ? svH.ViewportHeight : fe.ActualHeight);
                    if (double.IsFinite(basis) && basis > 0)
                        pixels = basis * pct.Value;
                }
            }
            else if (!length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out pixels))
            {
                return false;
            }

            if (isPadding)
            {
                pixels = Math.Max(0, pixels);
                if (target is FrameworkElement fe)
                {
                    var state = s_states.GetOrCreateValue(fe);
                    if (dp == ScrollPaddingLeftProperty) state.PaddingLeftPercent = pct;
                    else if (dp == ScrollPaddingTopProperty) state.PaddingTopPercent = pct;
                    else if (dp == ScrollPaddingRightProperty) state.PaddingRightPercent = pct;
                    else if (dp == ScrollPaddingBottomProperty) state.PaddingBottomPercent = pct;
                }
            }

            sink.Set(dp, pixels);
            return true;
        }
    }

    // ── Runtime Scroll Offset, Snap Solver & Scroll Anchoring ─────────────────

    public static Point GetScrollOffset(FrameworkElement container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (container is ScrollViewer sv)
        {
            return new Point(sv.HorizontalOffset, sv.VerticalOffset);
        }

        if (s_states.TryGetValue(container, out var state))
        {
            RestoreAnchorIfNeeded(container, state);
            return state.Offset;
        }

        return Point.Zero;
    }

    public static void SetScrollOffset(FrameworkElement container, Point offset)
    {
        SetScrollOffset(container, offset, applySnap: true);
    }

    public static void SetScrollOffset(FrameworkElement container, Point offset, bool applySnap)
    {
        ArgumentNullException.ThrowIfNull(container);
        var viewport = GetContainerViewportSize(container);
        var resolved = applySnap && GetEffectiveScrollSnapType(container).IsEnabled
            ? ResolveSnapOffset(container, offset, viewport)
            : ClampToScrollableRange(container, offset, viewport);

        if (container is ScrollViewer sv)
        {
            sv.SetCssScrollOffsetImmediate(resolved.X, resolved.Y);
            CaptureAnchor(sv);
            return;
        }

        var state = s_states.GetOrCreateValue(container);
        ApplyBoxScrollTranslation(container, state, resolved);
        CaptureAnchorCore(container, state);
    }

    public static Point ResolveSnapOffset(FrameworkElement container, Point rawOffset) =>
        ResolveSnapOffset(container, rawOffset, GetContainerViewportSize(container), default);

    public static Point ResolveSnapOffset(FrameworkElement container, Point rawOffset, Vector velocity) =>
        ResolveSnapOffset(container, rawOffset, GetContainerViewportSize(container), velocity);

    public static Point ResolveSnapOffset(
        FrameworkElement container,
        Point rawOffset,
        Size viewportSize,
        Vector velocity = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        var snapType = GetEffectiveScrollSnapType(container);
        var clampedRaw = ClampToScrollableRange(container, rawOffset, viewportSize);
        if (!snapType.IsEnabled)
            return clampedRaw;

        var wm = CssWritingModeProperties.GetWritingMode(container);
        var isVerticalWriting = CssWritingModeProperties.IsVertical(wm);
        var snapX = snapType.Axis is CssScrollSnapAxis.X or CssScrollSnapAxis.Both ||
                    (snapType.Axis == CssScrollSnapAxis.Inline && !isVerticalWriting) ||
                    (snapType.Axis == CssScrollSnapAxis.Block && isVerticalWriting);
        var snapY = snapType.Axis is CssScrollSnapAxis.Y or CssScrollSnapAxis.Both ||
                    (snapType.Axis == CssScrollSnapAxis.Block && !isVerticalWriting) ||
                    (snapType.Axis == CssScrollSnapAxis.Inline && isVerticalWriting);

        var padding = GetEffectiveScrollPadding(container, viewportSize);
        var snapportLeft = padding.Left;
        var snapportTop = padding.Top;
        var snapportWidth = Math.Max(0, viewportSize.Width - padding.Left - padding.Right);
        var snapportHeight = Math.Max(0, viewportSize.Height - padding.Top - padding.Bottom);

        var (maxScrollX, maxScrollY) = GetScrollableRange(container, viewportSize);
        var candidates = CollectSnapCandidates(container, isVerticalWriting, snapportLeft, snapportTop, snapportWidth, snapportHeight, maxScrollX, maxScrollY);
        var currentOffset = container is ScrollViewer sv
            ? new Point(sv.HorizontalOffset, sv.VerticalOffset)
            : (s_states.TryGetValue(container, out var st) ? st.Offset : Point.Zero);

        var resolvedX = snapX
            ? SolveSnapAxis(candidates, isX: true, clampedRaw.X, currentOffset.X, velocity.X, snapportWidth, snapType.Strictness, maxScrollX)
            : clampedRaw.X;
        var resolvedY = snapY
            ? SolveSnapAxis(candidates, isX: false, clampedRaw.Y, currentOffset.Y, velocity.Y, snapportHeight, snapType.Strictness, maxScrollY)
            : clampedRaw.Y;

        return new Point(resolvedX, resolvedY);
    }

    public static void ScrollIntoView(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        CssContainmentProperties.RequestRevealForFocusOrSearch(target);

        var container = FindNearestScrollContainer(target);
        if (container is null)
            return;

        var viewport = GetContainerViewportSize(container);
        var padding = GetEffectiveScrollPadding(container, viewport);
        var snapportLeft = padding.Left;
        var snapportTop = padding.Top;
        var snapportWidth = Math.Max(0, viewport.Width - padding.Left - padding.Right);
        var snapportHeight = Math.Max(0, viewport.Height - padding.Top - padding.Bottom);

        var contentRect = GetUnscrolledRectInContainer(container, target);
        var margin = GetScrollMargin(target);
        var areaLeft = contentRect.Left - margin.Left;
        var areaTop = contentRect.Top - margin.Top;
        var areaRight = contentRect.Right + margin.Right;
        var areaBottom = contentRect.Bottom + margin.Bottom;
        var areaWidth = Math.Max(0, areaRight - areaLeft);
        var areaHeight = Math.Max(0, areaBottom - areaTop);

        var current = GetScrollOffset(container);
        var nextX = current.X;
        var nextY = current.Y;

        var wm = CssWritingModeProperties.GetWritingMode(container);
        var isVerticalWriting = CssWritingModeProperties.IsVertical(wm);
        var align = GetScrollSnapAlign(target);
        var alignX = isVerticalWriting ? align.Block : align.Inline;
        var alignY = isVerticalWriting ? align.Inline : align.Block;

        if (alignX != CssScrollSnapAlignment.None)
        {
            nextX = ComputeAlignedSnapOffset(areaLeft, areaWidth, areaRight, snapportLeft, snapportWidth, alignX);
        }
        else if (areaLeft < current.X + snapportLeft)
        {
            nextX = areaLeft - snapportLeft;
        }
        else if (areaRight > current.X + snapportLeft + snapportWidth)
        {
            nextX = areaWidth <= snapportWidth
                ? areaRight - (snapportLeft + snapportWidth)
                : areaLeft - snapportLeft;
        }

        if (alignY != CssScrollSnapAlignment.None)
        {
            nextY = ComputeAlignedSnapOffset(areaTop, areaHeight, areaBottom, snapportTop, snapportHeight, alignY);
        }
        else if (areaTop < current.Y + snapportTop)
        {
            nextY = areaTop - snapportTop;
        }
        else if (areaBottom > current.Y + snapportTop + snapportHeight)
        {
            nextY = areaHeight <= snapportHeight
                ? areaBottom - (snapportTop + snapportHeight)
                : areaTop - snapportTop;
        }

        var clamped = ClampToScrollableRange(container, new Point(nextX, nextY), viewport);
        SetScrollOffset(container, clamped, applySnap: GetEffectiveScrollSnapType(container).IsEnabled);
    }

    internal static void SuppressAnchoringForPositionChange(FrameworkElement element)
    {
        // Scroll Anchoring 1: entering/leaving absolute positioning suppresses
        // anchoring throughout the scroller, including preceding siblings.
        for (var parent = element.VisualParent; parent is not null; parent = parent.VisualParent)
            if (parent is FrameworkElement container && s_states.TryGetValue(container, out var state))
            {
                state.SuppressAnchorUntilArrange = true;
                state.HasCapturedAnchor = false;
            }
    }

    public static void CaptureAnchor(FrameworkElement container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var state = s_states.GetOrCreateValue(container);
        if (container is ScrollViewer sv)
        {
            state.Offset = new Point(sv.HorizontalOffset, sv.VerticalOffset);
        }

        CaptureAnchorCore(container, state);
    }

    public static bool RestoreAnchor(FrameworkElement container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (!s_states.TryGetValue(container, out var state))
            return false;

        return RestoreAnchorIfNeeded(container, state);
    }

    private static void CaptureAnchorCore(FrameworkElement container, ScrollBoxState state)
    {
        if (state.SuppressAnchorUntilArrange) return;
        if (GetEffectiveOverflowAnchor(container) == CssOverflowAnchor.None)
        {
            state.HasCapturedAnchor = false;
            state.AnchorElement = null;
            return;
        }

        var offset = container is ScrollViewer sv
            ? new Point(sv.HorizontalOffset, sv.VerticalOffset)
            : state.Offset;
        if (offset.X <= 0.001 && offset.Y <= 0.001)
        {
            state.HasCapturedAnchor = false;
            state.AnchorElement = null;
            return;
        }

        var viewport = GetContainerViewportSize(container);
        var padding = GetEffectiveScrollPadding(container, viewport);
        var visibleTop = offset.Y + padding.Top;
        var visibleBottom = offset.Y + Math.Max(padding.Top, viewport.Height - padding.Bottom);
        var visibleLeft = offset.X + padding.Left;
        var visibleRight = offset.X + Math.Max(padding.Left, viewport.Width - padding.Right);

        var anchor = FindAnchorCandidate(container, visibleLeft, visibleTop, visibleRight, visibleBottom);
        if (anchor is null)
        {
            state.HasCapturedAnchor = false;
            state.AnchorElement = null;
            return;
        }

        var rect = GetUnscrolledRectInContainer(container, anchor);
        state.AnchorElement = new WeakReference<FrameworkElement>(anchor);
        state.AnchorContentPosition = new Point(rect.Left, rect.Top);
        state.AnchorSavedScrollOffset = offset;
        state.HasCapturedAnchor = true;
    }

    private static bool RestoreAnchorIfNeeded(FrameworkElement container, ScrollBoxState state)
    {
        if (state.SuppressAnchorUntilArrange)
        {
            state.SuppressAnchorUntilArrange = false;
            state.HasCapturedAnchor = false;
            state.AnchorElement = null;
            return false;
        }
        if (!state.HasCapturedAnchor || state.IsRestoringAnchor || state.AnchorElement is null)
            return false;

        if (GetEffectiveOverflowAnchor(container) == CssOverflowAnchor.None)
        {
            state.HasCapturedAnchor = false;
            return false;
        }

        if (!state.AnchorElement.TryGetTarget(out var anchor) || IsOverflowAnchorSuppressed(anchor, container))
        {
            state.HasCapturedAnchor = false;
            return false;
        }

        var newRect = GetUnscrolledRectInContainer(container, anchor);
        var dx = newRect.Left - state.AnchorContentPosition.X;
        var dy = newRect.Top - state.AnchorContentPosition.Y;
        if (Math.Abs(dx) <= 0.01 && Math.Abs(dy) <= 0.01)
            return false;

        state.IsRestoringAnchor = true;
        try
        {
            var currentOffset = container is ScrollViewer svCurrent
                ? new Point(svCurrent.HorizontalOffset, svCurrent.VerticalOffset)
                : state.Offset;
            var viewport = GetContainerViewportSize(container);
            var adjusted = ClampToScrollableRange(
                container,
                new Point(currentOffset.X + dx, currentOffset.Y + dy),
                viewport);

            state.AnchorContentPosition = new Point(newRect.Left, newRect.Top);
            state.AnchorSavedScrollOffset = adjusted;

            if (container is ScrollViewer sv)
            {
                sv.SetCssScrollOffsetImmediate(adjusted.X, adjusted.Y);
            }
            else
            {
                ApplyBoxScrollTranslation(container, state, adjusted);
            }

            return true;
        }
        finally
        {
            state.IsRestoringAnchor = false;
        }
    }

    private static FrameworkElement? FindAnchorCandidate(
        FrameworkElement container,
        double visibleLeft,
        double visibleTop,
        double visibleRight,
        double visibleBottom)
    {
        var root = container is ScrollViewer { Content: FrameworkElement content } ? content : container;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is not FrameworkElement child ||
                child is ScrollBar ||
                child.Visibility != Visibility.Visible)
                continue;

            if (IsOverflowAnchorSuppressed(child, container))
                continue;

            if (CssPositionedLayout.EscapesScrollport(child, container))
                continue;

            var rect = GetUnscrolledRectInContainer(container, child);
            if (rect.Width <= 0 && rect.Height <= 0)
                continue;

            var intersectsY = rect.Bottom > visibleTop + 0.01 && rect.Top < visibleBottom - 0.01;
            var intersectsX = rect.Right > visibleLeft + 0.01 && rect.Left < visibleRight - 0.01;
            if (intersectsY && intersectsX)
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsOverflowAnchorSuppressed(FrameworkElement element, FrameworkElement container)
    {
        for (Visual? cur = element; cur is not null && !ReferenceEquals(cur, container); cur = cur.VisualParent)
        {
            if (cur is DependencyObject dobj && GetOverflowAnchor(dobj) == CssOverflowAnchor.None)
                return true;
        }

        return GetEffectiveOverflowAnchor(container) == CssOverflowAnchor.None;
    }

    internal static CssOverflowAnchor GetEffectiveOverflowAnchor(FrameworkElement container)
    {
        var own = GetOverflowAnchor(container);
        if (own == CssOverflowAnchor.None)
            return CssOverflowAnchor.None;
        if (container is ScrollViewer { Content: DependencyObject content } &&
            GetOverflowAnchor(content) == CssOverflowAnchor.None)
            return CssOverflowAnchor.None;
        return CssOverflowAnchor.Auto;
    }

    internal static CssScrollSnapType GetEffectiveScrollSnapType(FrameworkElement container)
    {
        var own = GetScrollSnapType(container);
        if (own.IsEnabled)
            return own;
        if (container is ScrollViewer { Content: DependencyObject content })
            return GetScrollSnapType(content);
        return own;
    }

    internal static Thickness GetEffectiveScrollPadding(FrameworkElement container, Size? viewportSize = null)
    {
        var own = GetScrollPadding(container, viewportSize);
        if (own != default)
            return own;
        if (container is ScrollViewer { Content: DependencyObject content })
            return GetScrollPadding(content, viewportSize);
        return own;
    }

    internal static CssOverscrollBehavior GetEffectiveOverscrollBehaviorX(FrameworkElement container)
    {
        var own = GetOverscrollBehaviorX(container);
        if (own != CssOverscrollBehavior.Auto)
            return own;
        if (container is ScrollViewer { Content: DependencyObject content })
            return GetOverscrollBehaviorX(content);
        return own;
    }

    internal static CssOverscrollBehavior GetEffectiveOverscrollBehaviorY(FrameworkElement container)
    {
        var own = GetOverscrollBehaviorY(container);
        if (own != CssOverscrollBehavior.Auto)
            return own;
        if (container is ScrollViewer { Content: DependencyObject content })
            return GetOverscrollBehaviorY(content);
        return own;
    }

    private readonly record struct SnapCandidate(
        double? OffsetX,
        double? OffsetY,
        CssScrollSnapStop Stop);

    private static List<SnapCandidate> CollectSnapCandidates(
        FrameworkElement container,
        bool isVerticalWriting,
        double snapportLeft,
        double snapportTop,
        double snapportWidth,
        double snapportHeight,
        double maxScrollX,
        double maxScrollY)
    {
        var list = new List<SnapCandidate>();
        var root = container is ScrollViewer { Content: FrameworkElement content } ? content : container;
        CollectSnapCandidatesRecursive(
            container,
            root,
            isVerticalWriting,
            snapportLeft,
            snapportTop,
            snapportWidth,
            snapportHeight,
            maxScrollX,
            maxScrollY,
            list);
        return list;
    }

    private static void CollectSnapCandidatesRecursive(
        FrameworkElement container,
        Visual current,
        bool isVerticalWriting,
        double snapportLeft,
        double snapportTop,
        double snapportWidth,
        double snapportHeight,
        double maxScrollX,
        double maxScrollY,
        List<SnapCandidate> output)
    {
        var count = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(current, i) is not FrameworkElement child ||
                child is ScrollBar ||
                child.Visibility != Visibility.Visible)
                continue;

            var align = GetScrollSnapAlign(child);
            var alignX = isVerticalWriting ? align.Block : align.Inline;
            var alignY = isVerticalWriting ? align.Inline : align.Block;

            if (alignX != CssScrollSnapAlignment.None || alignY != CssScrollSnapAlignment.None)
            {
                var rect = GetUnscrolledRectInContainer(container, child);
                var margin = GetScrollMargin(child);
                var areaLeft = rect.Left - margin.Left;
                var areaTop = rect.Top - margin.Top;
                var areaRight = rect.Right + margin.Right;
                var areaBottom = rect.Bottom + margin.Bottom;
                var areaWidth = Math.Max(0, areaRight - areaLeft);
                var areaHeight = Math.Max(0, areaBottom - areaTop);

                double? offsetX = alignX != CssScrollSnapAlignment.None
                    ? ComputeAlignedSnapOffset(areaLeft, areaWidth, areaRight, snapportLeft, snapportWidth, alignX)
                    : null;
                double? offsetY = alignY != CssScrollSnapAlignment.None
                    ? ComputeAlignedSnapOffset(areaTop, areaHeight, areaBottom, snapportTop, snapportHeight, alignY)
                    : null;

                if (offsetX.HasValue && double.IsFinite(maxScrollX) && maxScrollX > 0)
                    offsetX = Math.Clamp(offsetX.Value, 0, maxScrollX);
                else if (offsetX.HasValue)
                    offsetX = Math.Max(0, offsetX.Value);

                if (offsetY.HasValue && double.IsFinite(maxScrollY) && maxScrollY > 0)
                    offsetY = Math.Clamp(offsetY.Value, 0, maxScrollY);
                else if (offsetY.HasValue)
                    offsetY = Math.Max(0, offsetY.Value);

                output.Add(new SnapCandidate(offsetX, offsetY, GetScrollSnapStop(child)));
            }

            if (child is not ScrollViewer && !IsCssScrollContainer(child))
            {
                CollectSnapCandidatesRecursive(
                    container,
                    child,
                    isVerticalWriting,
                    snapportLeft,
                    snapportTop,
                    snapportWidth,
                    snapportHeight,
                    maxScrollX,
                    maxScrollY,
                    output);
            }
        }
    }

    private static double ComputeAlignedSnapOffset(
        double areaStart,
        double areaLength,
        double areaEnd,
        double snapportStart,
        double snapportLength,
        CssScrollSnapAlignment alignment)
    {
        return alignment switch
        {
            CssScrollSnapAlignment.Start => areaStart - snapportStart,
            CssScrollSnapAlignment.Center => (areaStart + areaLength * 0.5) - (snapportStart + snapportLength * 0.5),
            CssScrollSnapAlignment.End => areaEnd - (snapportStart + snapportLength),
            _ => 0.0,
        };
    }

    private static double SolveSnapAxis(
        List<SnapCandidate> candidates,
        bool isX,
        double raw,
        double currentOffset,
        double velocity,
        double snapportLength,
        CssScrollSnapStrictness strictness,
        double maxScroll)
    {
        var axisCandidates = new List<(double Offset, CssScrollSnapStop Stop)>();
        foreach (var c in candidates)
        {
            var val = isX ? c.OffsetX : c.OffsetY;
            if (val.HasValue && double.IsFinite(val.Value))
            {
                axisCandidates.Add((val.Value, c.Stop));
            }
        }

        if (axisCandidates.Count == 0)
            return raw;

        axisCandidates.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));

        // Handle directional velocity & scroll-snap-stop: always
        if (Math.Abs(velocity) > 0.001)
        {
            var dir = Math.Sign(velocity);
            var start = Math.Sign(raw - currentOffset) == dir && Math.Abs(raw - currentOffset) > 0.5
                ? currentOffset
                : raw;

            // Check if any scroll-snap-stop: always point is passed between start and raw
            if (Math.Abs(raw - start) > 0.5)
            {
                if (dir > 0)
                {
                    foreach (var cand in axisCandidates)
                    {
                        if (cand.Stop == CssScrollSnapStop.Always &&
                            cand.Offset > start + 0.5 &&
                            cand.Offset <= raw + 0.5)
                        {
                            return cand.Offset;
                        }
                    }
                }
                else
                {
                    for (var i = axisCandidates.Count - 1; i >= 0; i--)
                    {
                        var cand = axisCandidates[i];
                        if (cand.Stop == CssScrollSnapStop.Always &&
                            cand.Offset < start - 0.5 &&
                            cand.Offset >= raw - 0.5)
                        {
                            return cand.Offset;
                        }
                    }
                }
            }
            else
            {
                // Velocity provided from raw as origin: if the next snap point in direction of velocity
                // has scroll-snap-stop: always (or mandatory snap), advance to the first snap point in dir.
                if (dir > 0)
                {
                    foreach (var cand in axisCandidates)
                    {
                        if (cand.Offset > raw + 0.5)
                        {
                            if (cand.Stop == CssScrollSnapStop.Always || strictness == CssScrollSnapStrictness.Mandatory)
                                return cand.Offset;
                            break;
                        }
                    }
                }
                else
                {
                    for (var i = axisCandidates.Count - 1; i >= 0; i--)
                    {
                        var cand = axisCandidates[i];
                        if (cand.Offset < raw - 0.5)
                        {
                            if (cand.Stop == CssScrollSnapStop.Always || strictness == CssScrollSnapStrictness.Mandatory)
                                return cand.Offset;
                            break;
                        }
                    }
                }
            }
        }

        // Nearest snap candidate to raw
        var bestOffset = raw;
        var bestDistance = double.PositiveInfinity;
        foreach (var cand in axisCandidates)
        {
            var dist = Math.Abs(cand.Offset - raw);
            if (dist < bestDistance - 0.001 ||
                (Math.Abs(dist - bestDistance) <= 0.001 && Math.Abs(velocity) > 0.001 && Math.Sign(cand.Offset - raw) == Math.Sign(velocity)))
            {
                bestDistance = dist;
                bestOffset = cand.Offset;
            }
        }

        if (strictness == CssScrollSnapStrictness.Mandatory)
            return bestOffset;

        // Proximity threshold: 30% of the snapport dimension (at least 24px)
        var threshold = Math.Max(24.0, snapportLength * 0.30);
        return bestDistance <= threshold ? bestOffset : raw;
    }

    internal static Size GetContainerViewportSize(FrameworkElement container)
    {
        if (container is ScrollViewer sv)
        {
            var vw = sv.ViewportWidth > 0 ? sv.ViewportWidth : (sv.ActualWidth > 0 ? sv.ActualWidth : sv.Width);
            var vh = sv.ViewportHeight > 0 ? sv.ViewportHeight : (sv.ActualHeight > 0 ? sv.ActualHeight : sv.Height);
            return new Size(double.IsFinite(vw) ? vw : 0, double.IsFinite(vh) ? vh : 0);
        }

        var insets = CssBoxMetrics.ContentInsets(container, container.ActualWidth > 0 ? container.ActualWidth : container.Width);
        var w = container.ActualWidth > 0 ? container.ActualWidth : (double.IsFinite(container.Width) ? container.Width : 0);
        var h = container.ActualHeight > 0 ? container.ActualHeight : (double.IsFinite(container.Height) ? container.Height : 0);
        return new Size(
            Math.Max(0, w - insets.Left - insets.Right),
            Math.Max(0, h - insets.Top - insets.Bottom));
    }

    private static (double MaxX, double MaxY) GetScrollableRange(FrameworkElement container, Size viewport)
    {
        if (container is ScrollViewer sv)
        {
            var maxX = sv.ScrollableWidth > 0
                ? sv.ScrollableWidth
                : Math.Max(0, sv.ExtentWidth - viewport.Width);
            var maxY = sv.ScrollableHeight > 0
                ? sv.ScrollableHeight
                : Math.Max(0, sv.ExtentHeight - viewport.Height);
            return (maxX, maxY);
        }

        var extent = ComputeChildrenExtent(container);
        return (Math.Max(0, extent.Width - viewport.Width), Math.Max(0, extent.Height - viewport.Height));
    }

    private static Point ClampToScrollableRange(FrameworkElement container, Point offset, Size viewport)
    {
        var (maxX, maxY) = GetScrollableRange(container, viewport);
        var x = double.IsFinite(offset.X) ? Math.Max(0, maxX > 0 ? Math.Min(offset.X, maxX) : offset.X) : 0;
        var y = double.IsFinite(offset.Y) ? Math.Max(0, maxY > 0 ? Math.Min(offset.Y, maxY) : offset.Y) : 0;
        return new Point(x, y);
    }

    private static Size ComputeChildrenExtent(FrameworkElement container)
    {
        var insets = CssBoxMetrics.ContentInsets(container, container.ActualWidth > 0 ? container.ActualWidth : container.Width);
        var maxRight = 0.0;
        var maxBottom = 0.0;
        s_states.TryGetValue(container, out var state);

        var count = VisualTreeHelper.GetChildrenCount(container);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(container, i) is not FrameworkElement child ||
                child.Visibility != Visibility.Visible)
                continue;

            var unscrolledBounds = ResolveDirectChildUnscrolledBounds(child, state);
            var unscrolledLeft = unscrolledBounds.X - insets.Left;
            var unscrolledTop = unscrolledBounds.Y - insets.Top;
            var margin = child.Margin;
            maxRight = Math.Max(maxRight, unscrolledLeft + unscrolledBounds.Width + Math.Max(0, margin.Right));
            maxBottom = Math.Max(maxBottom, unscrolledTop + unscrolledBounds.Height + Math.Max(0, margin.Bottom));
        }

        return new Size(maxRight, maxBottom);
    }

    private static Rect ResolveDirectChildUnscrolledBounds(FrameworkElement child, ScrollBoxState? state)
    {
        var bounds = child.VisualBounds;
        if (state is null)
            return bounds;

        if (state.ChildTranslatedBounds.TryGetValue(child, out var lastTranslatedBox) &&
            state.ChildUnscrolledBounds.TryGetValue(child, out var recordedUnscrolledBox) &&
            Math.Abs(bounds.X - lastTranslatedBox.Value.X) <= 1e-4 &&
            Math.Abs(bounds.Y - lastTranslatedBox.Value.Y) <= 1e-4 &&
            Math.Abs(bounds.Width - lastTranslatedBox.Value.Width) <= 1e-4 &&
            Math.Abs(bounds.Height - lastTranslatedBox.Value.Height) <= 1e-4)
        {
            return recordedUnscrolledBox.Value;
        }

        // Layout has re-arranged the child since the last translation; bounds is fresh unscrolled.
        state.ChildUnscrolledBounds.AddOrUpdate(child, new StrongBox<Rect>(bounds));
        state.ChildTranslatedBounds.Remove(child);
        return bounds;
    }

    internal static Rect GetUnscrolledRectInContainer(FrameworkElement container, FrameworkElement descendant)
    {
        if (container is ScrollViewer sv && sv.Content is FrameworkElement contentRoot)
        {
            double x = 0;
            double y = 0;
            Visual? cur = descendant;
            while (cur is not null && !ReferenceEquals(cur, contentRoot))
            {
                if (cur is FrameworkElement fe)
                {
                    x += fe.VisualBounds.X;
                    y += fe.VisualBounds.Y;
                }
                cur = cur.VisualParent;
            }

            return new Rect(x, y, descendant.ActualWidth > 0 ? descendant.ActualWidth : descendant.VisualBounds.Width,
                descendant.ActualHeight > 0 ? descendant.ActualHeight : descendant.VisualBounds.Height);
        }

        var insets = CssBoxMetrics.ContentInsets(container, container.ActualWidth > 0 ? container.ActualWidth : container.Width);
        s_states.TryGetValue(container, out var state);

        double relX = 0;
        double relY = 0;
        Visual? current = descendant;
        while (current is not null && !ReferenceEquals(current, container))
        {
            if (current is FrameworkElement fe)
            {
                if (ReferenceEquals(fe.VisualParent, container))
                {
                    var unscrolled = ResolveDirectChildUnscrolledBounds(fe, state);
                    relX += unscrolled.X - insets.Left;
                    relY += unscrolled.Y - insets.Top;
                }
                else
                {
                    relX += fe.VisualBounds.X;
                    relY += fe.VisualBounds.Y;
                }
            }
            current = current.VisualParent;
        }

        var w = descendant.ActualWidth > 0 ? descendant.ActualWidth : descendant.VisualBounds.Width;
        var h = descendant.ActualHeight > 0 ? descendant.ActualHeight : descendant.VisualBounds.Height;
        return new Rect(relX, relY, w, h);
    }

    private static void ApplyBoxScrollTranslation(FrameworkElement container, ScrollBoxState state, Point newOffset)
    {
        state.Offset = newOffset;

        var count = VisualTreeHelper.GetChildrenCount(container);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(container, i) is not FrameworkElement child ||
                child is ScrollBar)
                continue;

            if (CssPositionedLayout.EscapesScrollport(child, container))
                continue;

            var unscrolled = ResolveDirectChildUnscrolledBounds(child, state);
            var translated = new Rect(unscrolled.X - newOffset.X, unscrolled.Y - newOffset.Y, unscrolled.Width, unscrolled.Height);
            child.SetVisualBounds(translated);
            state.ChildTranslatedBounds.AddOrUpdate(child, new StrongBox<Rect>(translated));
        }

        container.InvalidateVisual();
    }

    internal static bool IsCssScrollContainer(FrameworkElement element)
    {
        if (element is ScrollViewer)
            return true;
        if (s_states.TryGetValue(element, out _))
            return true;
        if (CssOverflowProperties.Get(element) is { } overflow &&
            (overflow.X is CssOverflowMode.Auto or CssOverflowMode.Scroll or CssOverflowMode.Hidden ||
             overflow.Y is CssOverflowMode.Auto or CssOverflowMode.Scroll or CssOverflowMode.Hidden))
            return true;
        if (GetScrollSnapType(element).IsEnabled ||
            GetOverscrollBehaviorX(element) != CssOverscrollBehavior.Auto ||
            GetOverscrollBehaviorY(element) != CssOverscrollBehavior.Auto)
            return true;
        return false;
    }

    internal static FrameworkElement? FindNearestScrollContainer(FrameworkElement element)
    {
        for (Visual? cur = element.VisualParent; cur is not null; cur = cur.VisualParent)
        {
            if (cur is FrameworkElement fe && IsCssScrollContainer(fe))
                return fe;
        }
        return null;
    }

    private static void OnFrameworkElementSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement fe)
            return;

        // Check if an ancestor non-ScrollViewer CSS scroll container has an active scroll anchor
        for (Visual? cur = fe.VisualParent; cur is not null; cur = cur.VisualParent)
        {
            if (cur is ScrollViewer)
                break;
            if (cur is FrameworkElement container && s_states.TryGetValue(container, out var state) && state.HasCapturedAnchor)
            {
                RestoreAnchorIfNeeded(container, state);
                break;
            }
        }
    }

    private static void OnFrameworkElementBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement container || sender is ScrollViewer)
            return;
        if (!IsCssScrollContainer(container))
            return;
        if (e.TargetObject is FrameworkElement target && !ReferenceEquals(target, container))
        {
            ScrollIntoView(target);
            e.Handled = true;
        }
    }

    private static void OnFrameworkElementMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement container || sender is ScrollViewer)
            return;
        if (!IsCssScrollContainer(container))
            return;

        var overflow = CssOverflowProperties.Get(container);
        var canScrollX = overflow?.X is CssOverflowMode.Auto or CssOverflowMode.Scroll ||
                         GetOverscrollBehaviorX(container) != CssOverscrollBehavior.Auto;
        var canScrollY = overflow?.Y is CssOverflowMode.Auto or CssOverflowMode.Scroll ||
                         GetOverscrollBehaviorY(container) != CssOverscrollBehavior.Auto ||
                         overflow is null;

        var viewport = GetContainerViewportSize(container);
        var (maxX, maxY) = GetScrollableRange(container, viewport);
        var current = GetScrollOffset(container);

        var rawDx = e.HorizontalDelta != 0 ? -e.HorizontalDelta * 0.4 : 0.0;
        var rawDy = e.VerticalDelta != 0 ? -e.VerticalDelta * 0.4 : (e.Delta != 0 ? -e.Delta * 0.4 : 0.0);

        if (canScrollX && Math.Abs(rawDx) > 0.001 && !e.IsHorizontalDeltaHandled)
        {
            var nextX = Math.Clamp(current.X + rawDx, 0, maxX);
            if (Math.Abs(nextX - current.X) > 0.001)
            {
                SetScrollOffset(container, new Point(nextX, current.Y));
                current = GetScrollOffset(container);
                e.IsHorizontalDeltaHandled = true;
                e.Handled = true;
            }
            else if (GetOverscrollBehaviorX(container) is CssOverscrollBehavior.Contain or CssOverscrollBehavior.None)
            {
                e.IsHorizontalDeltaHandled = true;
                e.Handled = true;
            }
        }

        if (canScrollY && Math.Abs(rawDy) > 0.001 && !e.IsVerticalDeltaHandled && !e.Handled)
        {
            var nextY = Math.Clamp(current.Y + rawDy, 0, maxY);
            if (Math.Abs(nextY - current.Y) > 0.001)
            {
                SetScrollOffset(container, new Point(current.X, nextY));
                e.IsVerticalDeltaHandled = true;
                e.Handled = true;
            }
            else if (GetOverscrollBehaviorY(container) is CssOverscrollBehavior.Contain or CssOverscrollBehavior.None)
            {
                e.IsVerticalDeltaHandled = true;
                e.Handled = true;
            }
        }
    }
}
