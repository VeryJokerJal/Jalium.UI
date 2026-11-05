using System.Globalization;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Represents the flags for the CSS <c>contain</c> property (CSS Containment Module Level 2 / 3).
/// </summary>
[Flags]
public enum CssContainFlags
{
    None = 0,
    Size = 1 << 0,
    InlineSize = 1 << 1,
    Layout = 1 << 2,
    Style = 1 << 3,
    Paint = 1 << 4,
    Content = Layout | Style | Paint,
    Strict = Size | Layout | Style | Paint,
}

/// <summary>
/// Represents a single-axis value for <c>contain-intrinsic-width</c>, <c>contain-intrinsic-height</c>,
/// <c>contain-intrinsic-inline-size</c>, or <c>contain-intrinsic-block-size</c>.
/// </summary>
public readonly record struct CssContainIntrinsicAxisSize(
    bool IsNone,
    bool HasAuto,
    double Length)
{
    public static readonly CssContainIntrinsicAxisSize None = new(true, false, double.NaN);

    public bool HasExplicitLength => !IsNone && double.IsFinite(Length) && Length >= 0;

    public double? Resolve(double? rememberedLength)
    {
        if (HasAuto && rememberedLength is { } rem && double.IsFinite(rem) && rem >= 0)
            return rem;
        if (HasExplicitLength)
            return Length;
        return null;
    }
}

/// <summary>
/// Specifies the behavior of the CSS <c>content-visibility</c> property.
/// </summary>
public enum CssContentVisibility
{
    Visible = 0,
    Auto = 1,
    Hidden = 2,
}

/// <summary>
/// Implements CSS Containment Module Level 2 / 3 properties:
/// <c>contain</c>, <c>contain-intrinsic-width</c>, <c>contain-intrinsic-height</c>,
/// <c>contain-intrinsic-inline-size</c>, <c>contain-intrinsic-block-size</c>,
/// <c>contain-intrinsic-size</c>, and <c>content-visibility</c>.
/// </summary>
public static class CssContainmentProperties
{
    private static bool s_hasContentVisibility;

    internal static bool SuppressedBySkippedAncestor(DependencyObject target, bool hiddenOnly = false)
    {
        if (!s_hasContentVisibility) return false;
        static bool Skipped(DependencyObject source, bool onlyHidden) =>
            GetContentVisibility(source) == CssContentVisibility.Hidden ||
            !onlyHidden && source is FrameworkElement element &&
            GetContentVisibility(element) == CssContentVisibility.Auto && IsContentSkipped(element);
        for (var ancestor = (target as Visual)?.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
            if (Skipped(ancestor, hiddenOnly)) return true;
        var sourceParent = target is FrameworkContentElement content ? content.Parent : null;
        for (var node = sourceParent is not null ? CssNode.Get(sourceParent) : CssNode.Existing(target)?.FrameworkParent;
            node is not null; node = node.FrameworkParent)
            if (Skipped(node.Target, hiddenOnly)) return true;
        return false;
    }

    internal static bool HidesContentsFromUserAgent(DependencyObject owner) => s_hasContentVisibility &&
        (GetContentVisibility(owner) == CssContentVisibility.Hidden || SuppressedBySkippedAncestor(owner, hiddenOnly: true));
    private sealed class ContainmentBoxState
    {
        public Size? RememberedRenderedSize;
        public bool? ExplicitOffscreenOverride;
        public bool RevealedByFocusOrSearch;
        public bool AppliedContainerTypeOverride;
        public bool AppliedWidthOverride;
        public bool AppliedHeightOverride;
        public bool AppliedClipOverride;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, ContainmentBoxState> s_states = new();
    private static bool s_classHandlersRegistered;

    public static readonly DependencyProperty ContainProperty = DependencyProperty.RegisterAttached(
        "Contain",
        typeof(CssContainFlags),
        typeof(CssContainmentProperties),
        new PropertyMetadata(CssContainFlags.None, OnContainmentPropertyChanged));

    public static readonly DependencyProperty ContainIntrinsicWidthProperty = DependencyProperty.RegisterAttached(
        "ContainIntrinsicWidth",
        typeof(CssContainIntrinsicAxisSize),
        typeof(CssContainmentProperties),
        new PropertyMetadata(CssContainIntrinsicAxisSize.None, OnContainmentPropertyChanged));

    public static readonly DependencyProperty ContainIntrinsicHeightProperty = DependencyProperty.RegisterAttached(
        "ContainIntrinsicHeight",
        typeof(CssContainIntrinsicAxisSize),
        typeof(CssContainmentProperties),
        new PropertyMetadata(CssContainIntrinsicAxisSize.None, OnContainmentPropertyChanged));

    public static readonly DependencyProperty ContainIntrinsicInlineSizeProperty = DependencyProperty.RegisterAttached(
        "ContainIntrinsicInlineSize",
        typeof(CssContainIntrinsicAxisSize),
        typeof(CssContainmentProperties),
        new PropertyMetadata(CssContainIntrinsicAxisSize.None, OnContainmentPropertyChanged));

    public static readonly DependencyProperty ContainIntrinsicBlockSizeProperty = DependencyProperty.RegisterAttached(
        "ContainIntrinsicBlockSize",
        typeof(CssContainIntrinsicAxisSize),
        typeof(CssContainmentProperties),
        new PropertyMetadata(CssContainIntrinsicAxisSize.None, OnContainmentPropertyChanged));

    public static readonly DependencyProperty ContentVisibilityProperty = DependencyProperty.RegisterAttached(
        "ContentVisibility",
        typeof(CssContentVisibility),
        typeof(CssContainmentProperties),
        new PropertyMetadata(CssContentVisibility.Visible, OnContainmentPropertyChanged));

    public static CssContainFlags GetContain(DependencyObject element) =>
        (CssContainFlags)(element.GetValue(ContainProperty) ?? CssContainFlags.None);

    public static void SetContain(DependencyObject element, CssContainFlags value) =>
        element.SetValue(ContainProperty, value);

    public static CssContainIntrinsicAxisSize GetContainIntrinsicWidth(DependencyObject element) =>
        (CssContainIntrinsicAxisSize)(element.GetValue(ContainIntrinsicWidthProperty) ?? CssContainIntrinsicAxisSize.None);

    public static void SetContainIntrinsicWidth(DependencyObject element, CssContainIntrinsicAxisSize value) =>
        element.SetValue(ContainIntrinsicWidthProperty, value);

    public static CssContainIntrinsicAxisSize GetContainIntrinsicHeight(DependencyObject element) =>
        (CssContainIntrinsicAxisSize)(element.GetValue(ContainIntrinsicHeightProperty) ?? CssContainIntrinsicAxisSize.None);

    public static void SetContainIntrinsicHeight(DependencyObject element, CssContainIntrinsicAxisSize value) =>
        element.SetValue(ContainIntrinsicHeightProperty, value);

    public static CssContainIntrinsicAxisSize GetContainIntrinsicInlineSize(DependencyObject element) =>
        (CssContainIntrinsicAxisSize)(element.GetValue(ContainIntrinsicInlineSizeProperty) ?? CssContainIntrinsicAxisSize.None);

    public static void SetContainIntrinsicInlineSize(DependencyObject element, CssContainIntrinsicAxisSize value) =>
        element.SetValue(ContainIntrinsicInlineSizeProperty, value);

    public static CssContainIntrinsicAxisSize GetContainIntrinsicBlockSize(DependencyObject element) =>
        (CssContainIntrinsicAxisSize)(element.GetValue(ContainIntrinsicBlockSizeProperty) ?? CssContainIntrinsicAxisSize.None);

    public static void SetContainIntrinsicBlockSize(DependencyObject element, CssContainIntrinsicAxisSize value) =>
        element.SetValue(ContainIntrinsicBlockSizeProperty, value);

    public static CssContentVisibility GetContentVisibility(DependencyObject element) =>
        (CssContentVisibility)(element.GetValue(ContentVisibilityProperty) ?? CssContentVisibility.Visible);

    public static void SetContentVisibility(DependencyObject element, CssContentVisibility value) =>
        element.SetValue(ContentVisibilityProperty, value);

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Registers R2 containment CSS properties into CssPropertyRegistry before style sheets are parsed.")]
    internal static void Hook()
        => CssPropertyRegistry.RegisterExtension(Register);

    public static void Register()
    {
        EnsureClassHandlersRegistered();

        CssPropertyRegistry.Register(new()
        {
            Name = "contain",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ContainProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParseContain(ref reader, out var flags) && reader.AtEnd
                    ? new CssImmediateValue(ContainProperty, flags)
                    : null,
        });

        RegisterIntrinsicAxisProperty("contain-intrinsic-width", ContainIntrinsicWidthProperty);
        RegisterIntrinsicAxisProperty("contain-intrinsic-height", ContainIntrinsicHeightProperty);
        RegisterIntrinsicAxisProperty("contain-intrinsic-inline-size", ContainIntrinsicInlineSizeProperty);
        RegisterIntrinsicAxisProperty("contain-intrinsic-block-size", ContainIntrinsicBlockSizeProperty);

        CssPropertyRegistry.Register(new()
        {
            Name = "contain-intrinsic-size",
            Kind = CssPropertyKind.Shorthand,
            Expand = static (ref CssTokenReader reader, CssCompileContext ctx, List<CssCompiledDeclaration> output) =>
            {
                if (!TryReadIntrinsicAxisValue(ref reader, ctx, out var widthVal))
                    return false;

                var heightVal = widthVal;
                if (!reader.AtEnd)
                {
                    if (!TryReadIntrinsicAxisValue(ref reader, ctx, out heightVal) || !reader.AtEnd)
                        return false;
                }

                output.Add(new("contain-intrinsic-width", new CssImmediateValue(ContainIntrinsicWidthProperty, widthVal), false));
                output.Add(new("contain-intrinsic-height", new CssImmediateValue(ContainIntrinsicHeightProperty, heightVal), false));
                return true;
            },
        });

        CssPropertyRegistry.Register(new()
        {
            Name = "content-visibility",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = ContentVisibilityProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                    return null;
                var kw = ident.ToString().ToLowerInvariant();
                return kw switch
                {
                    "visible" => new CssImmediateValue(ContentVisibilityProperty, CssContentVisibility.Visible),
                    "auto" => new CssImmediateValue(ContentVisibilityProperty, CssContentVisibility.Auto),
                    "hidden" => new CssImmediateValue(ContentVisibilityProperty, CssContentVisibility.Hidden),
                    _ => null,
                };
            },
        });
    }

    private static void RegisterIntrinsicAxisProperty(string name, DependencyProperty property)
    {
        CssPropertyRegistry.Register(new()
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = property,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx) =>
                TryReadIntrinsicAxisValue(ref reader, ctx, out var val) && reader.AtEnd
                    ? new CssImmediateValue(property, val)
                    : null,
        });
    }

    internal static bool TryParseContain(ref CssTokenReader reader, out CssContainFlags flags)
    {
        flags = CssContainFlags.None;
        if (!reader.TryReadIdent(out var firstIdent))
            return false;

        var first = firstIdent.ToString().ToLowerInvariant();
        if (first == "none")
            return reader.AtEnd;
        if (first == "strict")
        {
            flags = CssContainFlags.Strict;
            return reader.AtEnd;
        }
        if (first == "content")
        {
            flags = CssContainFlags.Content;
            return reader.AtEnd;
        }

        if (!TryMapContainKeyword(first, out var currentFlag))
            return false;
        flags |= currentFlag;

        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var nextIdent))
                return false;
            var kw = nextIdent.ToString().ToLowerInvariant();
            if (!TryMapContainKeyword(kw, out var nextFlag))
                return false;
            // Duplicate keywords or combining size + inline-size are invalid per CSS Containment 3.
            if ((flags & nextFlag) != 0)
                return false;
            if ((flags & (CssContainFlags.Size | CssContainFlags.InlineSize)) != 0 &&
                (nextFlag & (CssContainFlags.Size | CssContainFlags.InlineSize)) != 0)
                return false;
            flags |= nextFlag;
        }

        return true;
    }

    private static bool TryMapContainKeyword(string keyword, out CssContainFlags flag)
    {
        switch (keyword)
        {
            case "size":
                flag = CssContainFlags.Size;
                return true;
            case "inline-size":
                flag = CssContainFlags.InlineSize;
                return true;
            case "layout":
                flag = CssContainFlags.Layout;
                return true;
            case "style":
                flag = CssContainFlags.Style;
                return true;
            case "paint":
                flag = CssContainFlags.Paint;
                return true;
            default:
                flag = CssContainFlags.None;
                return false;
        }
    }

    internal static bool TryReadIntrinsicAxisValue(
        ref CssTokenReader reader,
        CssCompileContext ctx,
        out CssContainIntrinsicAxisSize value)
    {
        value = CssContainIntrinsicAxisSize.None;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            var kw = ident.ToString().ToLowerInvariant();
            if (kw == "none")
            {
                reader = probe;
                value = CssContainIntrinsicAxisSize.None;
                return true;
            }

            if (kw == "auto")
            {
                if (probe.TryReadIdent(out var secondIdent))
                {
                    if (secondIdent.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        reader = probe;
                        value = new CssContainIntrinsicAxisSize(true, true, double.NaN);
                        return true;
                    }
                    return false;
                }

                if (TryReadNonNegativeLengthPx(ref probe, ctx, out var autoLen))
                {
                    reader = probe;
                    value = new CssContainIntrinsicAxisSize(false, true, autoLen);
                    return true;
                }

                return false;
            }

            return false;
        }

        if (TryReadNonNegativeLengthPx(ref probe, ctx, out var len))
        {
            reader = probe;
            value = new CssContainIntrinsicAxisSize(false, false, len);
            return true;
        }

        return false;
    }

    private static bool TryReadNonNegativeLengthPx(ref CssTokenReader reader, CssCompileContext ctx, out double px)
    {
        px = 0;
        var probe = reader;
        if (!probe.TryReadNumber(out var number, out var unit) || !double.IsFinite(number) || number < 0)
            return false;

        if (unit == CssUnit.None)
        {
            if (number != 0)
                return false;
            reader = probe;
            px = 0;
            return true;
        }

        if (unit == CssUnit.Percent)
            return false;

        const double fontSize = 16.0;
        px = unit switch
        {
            CssUnit.Px => number,
            CssUnit.Pt => number * (96.0 / 72.0),
            CssUnit.Pc => number * 16.0,
            CssUnit.In => number * 96.0,
            CssUnit.Cm => number * (96.0 / 2.54),
            CssUnit.Mm => number * (96.0 / 25.4),
            CssUnit.Q => number * (96.0 / 101.6),
            CssUnit.Em or CssUnit.Rem => number * fontSize,
            CssUnit.Ex or CssUnit.Ch => number * fontSize * 0.5,
            _ => double.NaN,
        };

        if (!double.IsFinite(px))
            return false;

        reader = probe;
        return true;
    }

    public static bool HasSizeContainment(DependencyObject element)
    {
        var flags = GetContain(element);
        if ((flags & CssContainFlags.Size) != 0)
            return true;

        var cv = GetContentVisibility(element);
        if (cv == CssContentVisibility.Hidden)
            return true;
        if (cv == CssContentVisibility.Auto && element is FrameworkElement fe && IsContentSkipped(fe))
            return true;

        return false;
    }

    public static bool HasInlineSizeContainment(DependencyObject element)
    {
        if (HasSizeContainment(element))
            return true;
        return (GetContain(element) & CssContainFlags.InlineSize) != 0;
    }

    public static bool HasLayoutContainment(DependencyObject element)
    {
        if ((GetContain(element) & CssContainFlags.Layout) != 0)
            return true;
        return GetContentVisibility(element) is CssContentVisibility.Auto or CssContentVisibility.Hidden;
    }

    public static bool HasStyleContainment(DependencyObject element)
    {
        var result = (GetContain(element) & CssContainFlags.Style) != 0 ||
                     GetContentVisibility(element) is CssContentVisibility.Auto or CssContentVisibility.Hidden;
        if (result && element is FrameworkElement fe)
        {
            SyncStyleContainmentCounterScope(fe);
        }
        return result;
    }

    internal static void SyncStyleContainmentCounterScope(FrameworkElement container)
    {
        if (VisualTreeHelper.GetChildrenCount(container) == 0)
            return;
        if (VisualTreeHelper.GetChild(container, 0) is not DependencyObject firstChild)
            return;

        var counterNames = new HashSet<string>(StringComparer.Ordinal);
        CollectDescendantCounterNames(container, counterNames);
        if (counterNames.Count == 0)
            return;

        if (firstChild.GetEffectiveValueLayer(CssCounters.ResetProperty) is { } layer &&
            layer != DependencyValueStore.Layer.ParentTemplate)
            return;

        var ops = new List<CssCounterOperation>(counterNames.Count);
        foreach (var name in counterNames)
        {
            ops.Add(new CssCounterOperation(name, 0));
        }

        firstChild.SetLayerValue(
            CssCounters.ResetProperty,
            new CssCounterOperations(ops.ToArray()),
            DependencyObject.LayerValueSource.ParentTemplate,
            allowAutoTransition: false);
    }

    private static void CollectDescendantCounterNames(Visual root, HashSet<string> names)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is not Visual child)
                continue;

            if (child is DependencyObject dobj)
            {
                if (dobj.GetValue(CssCounters.IncrementProperty) is CssCounterOperations inc)
                {
                    foreach (var entry in inc.Entries)
                        names.Add(entry.Name);
                }
                if (dobj.GetValue(CssCounters.SetProperty) is CssCounterOperations set)
                {
                    foreach (var entry in set.Entries)
                        names.Add(entry.Name);
                }
            }

            CollectDescendantCounterNames(child, names);
        }
    }

    public static bool HasPaintContainment(DependencyObject element)
    {
        if ((GetContain(element) & CssContainFlags.Paint) != 0)
            return true;
        return GetContentVisibility(element) is CssContentVisibility.Auto or CssContentVisibility.Hidden;
    }

    public static void RememberRenderedSize(FrameworkElement element, Size renderedSize)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!double.IsFinite(renderedSize.Width) || !double.IsFinite(renderedSize.Height) ||
            renderedSize.Width < 0 || renderedSize.Height < 0)
        {
            return;
        }

        var state = s_states.GetOrCreateValue(element);
        state.RememberedRenderedSize = renderedSize;
        if (IsContentSkipped(element) || HasSizeContainment(element) || HasInlineSizeContainment(element))
        {
            SyncContainmentState(element);
        }
    }

    public static Size? GetRememberedRenderedSize(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_states.TryGetValue(element, out var state) ? state.RememberedRenderedSize : null;
    }

    public static void SetOffscreenForAutoVisibility(FrameworkElement element, bool? isOffscreen)
    {
        ArgumentNullException.ThrowIfNull(element);
        var state = s_states.GetOrCreateValue(element);
        if (isOffscreen == true)
            state.RevealedByFocusOrSearch = false;
        state.ExplicitOffscreenOverride = isOffscreen;
        SyncContainmentState(element);
    }

    public static bool TryGetIntrinsicPlaceholderSize(FrameworkElement element, out double? width, out double? height)
    {
        ArgumentNullException.ThrowIfNull(element);
        var isVertical = CssWritingModeProperties.IsVertical(CssWritingModeProperties.GetWritingMode(element));
        var inlineAxis = GetContainIntrinsicInlineSize(element);
        var blockAxis = GetContainIntrinsicBlockSize(element);
        var widthAxis = GetContainIntrinsicWidth(element);
        var heightAxis = GetContainIntrinsicHeight(element);

        var effectiveWidthAxis = !inlineAxis.IsNone || inlineAxis.HasAuto
            ? (isVertical ? blockAxis : inlineAxis)
            : widthAxis;
        if (isVertical && (!blockAxis.IsNone || blockAxis.HasAuto))
            effectiveWidthAxis = blockAxis;
        else if (!isVertical && (!inlineAxis.IsNone || inlineAxis.HasAuto))
            effectiveWidthAxis = inlineAxis;

        var effectiveHeightAxis = !blockAxis.IsNone || blockAxis.HasAuto
            ? (isVertical ? inlineAxis : blockAxis)
            : heightAxis;
        if (isVertical && (!inlineAxis.IsNone || inlineAxis.HasAuto))
            effectiveHeightAxis = inlineAxis;
        else if (!isVertical && (!blockAxis.IsNone || blockAxis.HasAuto))
            effectiveHeightAxis = blockAxis;

        var remembered = GetRememberedRenderedSize(element);
        width = effectiveWidthAxis.Resolve(remembered?.Width);
        height = effectiveHeightAxis.Resolve(remembered?.Height);
        return width.HasValue || height.HasValue;
    }

    public static bool IsContentSkipped(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var cv = GetContentVisibility(element);
        if (cv == CssContentVisibility.Visible)
            return false;
        if (cv == CssContentVisibility.Hidden)
            return true;

        // content-visibility: auto
        if (s_states.TryGetValue(element, out var state))
        {
            if (state.RevealedByFocusOrSearch)
                return false;
            if (state.ExplicitOffscreenOverride.HasValue)
                return state.ExplicitOffscreenOverride.Value;
        }

        if (HasFocusWithinSubtree(element))
            return false;

        return IsElementOffscreenInScrollHierarchy(element);
    }

    public static bool RequestRevealForFocusOrSearch(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureClassHandlersRegistered();

        var revealedAny = false;
        for (Visual? cur = target; cur is not null; cur = cur.VisualParent)
        {
            if (cur is FrameworkElement fe && GetContentVisibility(fe) == CssContentVisibility.Auto)
            {
                var state = s_states.GetOrCreateValue(fe);
                if (!state.RevealedByFocusOrSearch || state.ExplicitOffscreenOverride == true)
                {
                    state.RevealedByFocusOrSearch = true;
                    state.ExplicitOffscreenOverride = false;
                    SyncContainmentState(fe);
                    revealedAny = true;
                }
            }
        }

        return revealedAny;
    }

    private static bool HasFocusWithinSubtree(FrameworkElement element)
    {
        if (element.IsFocused || element.IsKeyboardFocused || element.IsKeyboardFocusWithin)
            return true;

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is FrameworkElement child &&
                HasFocusWithinSubtree(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsElementOffscreenInScrollHierarchy(FrameworkElement element)
    {
        var container = CssScrollProperties.FindNearestScrollContainer(element);
        if (container is null)
            return false;

        var viewport = CssScrollProperties.GetContainerViewportSize(container);
        if (viewport.Width <= 0 || viewport.Height <= 0 ||
            !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height))
        {
            return false;
        }

        var scrollOffset = CssScrollProperties.GetScrollOffset(container);
        var rect = EstimateUnscrolledRectInContainer(container, element);
        if (rect.Width <= 0 && rect.Height <= 0)
        {
            // Estimate height/width from placeholder or explicit size
            TryGetIntrinsicPlaceholderSize(element, out var phW, out var phH);
            var w = phW ?? (double.IsFinite(element.Width) ? element.Width : 1.0);
            var h = phH ?? (double.IsFinite(element.Height) ? element.Height : 1.0);
            rect = new Rect(rect.X, rect.Y, Math.Max(1.0, w), Math.Max(1.0, h));
        }

        var viewLeft = scrollOffset.X;
        var viewTop = scrollOffset.Y;
        var viewRight = scrollOffset.X + viewport.Width;
        var viewBottom = scrollOffset.Y + viewport.Height;

        var intersectsX = rect.Right > viewLeft && rect.Left < viewRight;
        var intersectsY = rect.Bottom > viewTop && rect.Top < viewBottom;
        return !(intersectsX && intersectsY);
    }

    private static Rect EstimateUnscrolledRectInContainer(FrameworkElement container, FrameworkElement descendant)
    {
        var rect = CssScrollProperties.GetUnscrolledRectInContainer(container, descendant);
        if (rect.Top > 0 || rect.Left > 0 || descendant.IsArrangeValid)
            return rect;

        // If layout hasn't arranged the element yet, estimate its position from preceding siblings in StackPanel/Canvas.
        double estX = 0;
        double estY = 0;
        Visual? cur = descendant;
        var stopRoot = container is ScrollViewer { Content: FrameworkElement contentRoot } ? contentRoot : container;
        while (cur is not null && !ReferenceEquals(cur, stopRoot) && !ReferenceEquals(cur, container))
        {
            if (cur is FrameworkElement fe)
            {
                var canvasLeft = Canvas.GetLeft(fe);
                var canvasTop = Canvas.GetTop(fe);
                if (double.IsFinite(canvasLeft))
                    estX += canvasLeft;
                if (double.IsFinite(canvasTop))
                    estY += canvasTop;
                estX += Math.Max(0, fe.Margin.Left);
                estY += Math.Max(0, fe.Margin.Top);

                if (fe.VisualParent is StackPanel sp)
                {
                    var siblingCount = VisualTreeHelper.GetChildrenCount(sp);
                    for (var i = 0; i < siblingCount; i++)
                    {
                        var sib = VisualTreeHelper.GetChild(sp, i) as FrameworkElement;
                        if (sib is null)
                            continue;
                        if (ReferenceEquals(sib, fe))
                            break;
                        if (sib.Visibility != Visibility.Visible)
                            continue;

                        if (sp.Orientation == Orientation.Vertical)
                        {
                            var sibH = sib.ActualHeight > 0
                                ? sib.ActualHeight
                                : (double.IsFinite(sib.Height) ? sib.Height : sib.DesiredSize.Height);
                            if (sibH <= 0 && TryGetIntrinsicPlaceholderSize(sib, out _, out var sibPhH) && sibPhH.HasValue)
                                sibH = sibPhH.Value;
                            estY += sibH + Math.Max(0, sib.Margin.Top) + Math.Max(0, sib.Margin.Bottom);
                        }
                        else
                        {
                            var sibW = sib.ActualWidth > 0
                                ? sib.ActualWidth
                                : (double.IsFinite(sib.Width) ? sib.Width : sib.DesiredSize.Width);
                            if (sibW <= 0 && TryGetIntrinsicPlaceholderSize(sib, out var sibPhW, out _) && sibPhW.HasValue)
                                sibW = sibPhW.Value;
                            estX += sibW + Math.Max(0, sib.Margin.Left) + Math.Max(0, sib.Margin.Right);
                        }
                    }
                }
            }
            cur = cur.VisualParent;
        }

        return new Rect(estX, estY, rect.Width, rect.Height);
    }

    private static void EnsureClassHandlersRegistered()
    {
        if (s_classHandlersRegistered)
            return;
        s_classHandlersRegistered = true;

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            UIElement.GotFocusEvent,
            new RoutedEventHandler(OnElementGotFocus),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnElementGotKeyboardFocus),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(OnElementRequestBringIntoView),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler(OnElementSizeChanged),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnScrollViewerScrollChanged),
            handledEventsToo: true);
    }

    private static void OnElementGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement target)
            RequestRevealForFocusOrSearch(target);
        else if (sender is FrameworkElement fe)
            RequestRevealForFocusOrSearch(fe);
    }

    private static void OnElementGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement target)
            RequestRevealForFocusOrSearch(target);
        else if (sender is FrameworkElement fe)
            RequestRevealForFocusOrSearch(fe);
    }

    private static void OnElementRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is FrameworkElement target)
            RequestRevealForFocusOrSearch(target);
        else if (sender is FrameworkElement fe)
            RequestRevealForFocusOrSearch(fe);
    }

    private static void OnElementSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement fe)
            return;

        _ = HasStyleContainment(fe);

        var cv = GetContentVisibility(fe);
        var contain = GetContain(fe);
        var hasIntrinsic = TryGetIntrinsicPlaceholderSize(fe, out _, out _);

        if ((cv != CssContentVisibility.Visible || hasIntrinsic) &&
            !IsContentSkipped(fe) &&
            (contain & (CssContainFlags.Size | CssContainFlags.InlineSize)) == 0 &&
            e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            var state = s_states.GetOrCreateValue(fe);
            state.RememberedRenderedSize = e.NewSize;
        }
    }

    private static void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;
        RefreshAutoContentVisibilityInSubtree(sv);
    }

    private static void RefreshAutoContentVisibilityInSubtree(Visual root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is not Visual child)
                continue;
            if (child is FrameworkElement fe && GetContentVisibility(fe) == CssContentVisibility.Auto)
            {
                if (s_states.TryGetValue(fe, out var state) &&
                    !HasFocusWithinSubtree(fe) &&
                    state.ExplicitOffscreenOverride is null)
                {
                    state.RevealedByFocusOrSearch = false;
                }
                SyncContainmentState(fe);
            }
            RefreshAutoContentVisibilityInSubtree(child);
        }
    }

    private static void OnContainmentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.Property == ContentVisibilityProperty) s_hasContentVisibility = true;
        EnsureClassHandlersRegistered();
        if (d is FrameworkElement fe)
        {
            SyncContainmentState(fe);
        }
        else if (d is TextElement text) text.NotifyTextContentChanged();
        else if (d is FlowDocument document) document.NotifyPaginationStyleChanged();
    }

    internal static void SyncContainmentState(FrameworkElement fe)
    {
        fe.CssRuntimeState ??= new CssElementState();
        var state = s_states.GetOrCreateValue(fe);

        var hasSize = HasSizeContainment(fe);
        var hasInlineSize = HasInlineSizeContainment(fe);
        var hasLayout = HasLayoutContainment(fe);
        var hasPaint = HasPaintContainment(fe);
        _ = HasStyleContainment(fe);
        var skipped = IsContentSkipped(fe);

        // 1. Synchronize CssContainerProperties.TypeProperty on ParentTemplate layer
        if (hasSize || hasInlineSize)
        {
            var desiredType = hasSize ? CssContainerType.Size : CssContainerType.InlineSize;
            if (!fe.HasLocalValue(CssContainerProperties.TypeProperty))
            {
                fe.SetLayerValue(
                    CssContainerProperties.TypeProperty,
                    desiredType,
                    DependencyObject.LayerValueSource.ParentTemplate,
                    allowAutoTransition: false);
                state.AppliedContainerTypeOverride = true;
            }
        }
        else if (state.AppliedContainerTypeOverride)
        {
            fe.ClearLayerValue(CssContainerProperties.TypeProperty, DependencyObject.LayerValueSource.ParentTemplate);
            state.AppliedContainerTypeOverride = false;
        }

        // 2. Synchronize contain-intrinsic-size placeholder onto Width / Height when size-contained or skipped
        TryGetIntrinsicPlaceholderSize(fe, out var intrinsicW, out var intrinsicH);
        var insets = CssContainerProperties.Insets(fe, fe.ActualWidth > 0 ? fe.ActualWidth : 0);
        var addInsets = fe.CssLayout is not { BoxSizing: CssBoxSizing.ContentBox };

        if (hasInlineSize && intrinsicW.HasValue && !fe.HasLocalValue(FrameworkElement.WidthProperty))
        {
            var boxWidth = intrinsicW.Value + (addInsets ? insets.Left + insets.Right : 0);
            fe.SetLayerValue(
                FrameworkElement.WidthProperty,
                boxWidth,
                DependencyObject.LayerValueSource.ParentTemplate,
                allowAutoTransition: false);
            state.AppliedWidthOverride = true;
        }
        else if (state.AppliedWidthOverride)
        {
            fe.ClearLayerValue(FrameworkElement.WidthProperty, DependencyObject.LayerValueSource.ParentTemplate);
            state.AppliedWidthOverride = false;
        }

        if (hasSize && intrinsicH.HasValue && !fe.HasLocalValue(FrameworkElement.HeightProperty))
        {
            var boxHeight = intrinsicH.Value + (addInsets ? insets.Top + insets.Bottom : 0);
            fe.SetLayerValue(
                FrameworkElement.HeightProperty,
                boxHeight,
                DependencyObject.LayerValueSource.ParentTemplate,
                allowAutoTransition: false);
            state.AppliedHeightOverride = true;
        }
        else if (state.AppliedHeightOverride)
        {
            fe.ClearLayerValue(FrameworkElement.HeightProperty, DependencyObject.LayerValueSource.ParentTemplate);
            state.AppliedHeightOverride = false;
        }

        // 3. Synchronize paint containment (ClipToBounds)
        if (hasPaint && !fe.HasLocalValue(UIElement.ClipToBoundsProperty))
        {
            fe.SetLayerValue(
                UIElement.ClipToBoundsProperty,
                true,
                DependencyObject.LayerValueSource.ParentTemplate,
                allowAutoTransition: false);
            state.AppliedClipOverride = true;
        }
        else if (state.AppliedClipOverride)
        {
            fe.ClearLayerValue(UIElement.ClipToBoundsProperty, DependencyObject.LayerValueSource.ParentTemplate);
            state.AppliedClipOverride = false;
        }

        // Containment establishes a containing block directly. It does not
        // author an identity transform or overwrite descendants' style layers.
        // Render, input and automation apply the skipped-subtree gate.
        CssPositionedLayout.ContainingBlockChanged(fe);

        fe.InvalidateMeasure();
        fe.InvalidateArrange();
        fe.InvalidateVisual();
        UIElement.InvalidateHitTestCache();
        fe.GetExistingAutomationPeer()?.InvalidatePeer();
    }

}
