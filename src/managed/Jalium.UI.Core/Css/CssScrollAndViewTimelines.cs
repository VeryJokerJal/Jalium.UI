using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Axis along which a scroll or view progress timeline progresses.
/// </summary>
public enum CssTimelineAxis : byte
{
    Block,
    Inline,
    X,
    Y,
}

/// <summary>
/// Scroll container reference for anonymous <c>scroll()</c> timelines.
/// </summary>
public enum CssScrollScroller : byte
{
    Nearest,
    Root,
    Self,
}

/// <summary>
/// Kind of timeline attached via <c>animation-timeline</c>.
/// </summary>
public enum CssTimelineKind : byte
{
    Auto,
    None,
    Named,
    Scroll,
    View,
}

/// <summary>
/// Named timeline range for <c>animation-range-start</c> and <c>animation-range-end</c>.
/// </summary>
public enum CssTimelineRangeName : byte
{
    None,
    Normal,
    Cover,
    Contain,
    Entry,
    Exit,
    EntryCrossing,
    ExitCrossing,
}

/// <summary>
/// Represents one start or end boundary of an <c>animation-range</c>.
/// </summary>
internal readonly record struct CssTimelineRangeBoundary(
    CssTimelineRangeName RangeName,
    CssLength Offset,
    bool HasExplicitOffset)
{
    public static CssTimelineRangeBoundary Normal =>
        new(CssTimelineRangeName.Normal, new CssLength(0, CssUnit.Percent), false);
}

/// <summary>
/// Represents the start and end insets for a <c>view-timeline-inset</c> or <c>view()</c> function.
/// </summary>
internal readonly record struct CssViewTimelineInset(CssLength Start, CssLength End)
{
    public static CssViewTimelineInset Auto =>
        new(new CssLength(0, CssUnit.Auto), new CssLength(0, CssUnit.Auto));
}

/// <summary>
/// Specification for a single entry in <c>animation-timeline</c>.
/// </summary>
internal sealed record CssAnimationTimelineSpec(
    CssTimelineKind Kind,
    string? Name = null,
    CssScrollScroller Scroller = CssScrollScroller.Nearest,
    CssTimelineAxis Axis = CssTimelineAxis.Block,
    CssViewTimelineInset Inset = default)
{
    public static readonly CssAnimationTimelineSpec Auto = new(CssTimelineKind.Auto);
    public static readonly CssAnimationTimelineSpec None = new(CssTimelineKind.None);
}

/// <summary>
/// CSS Scroll-Driven Animations Level 1 (<c>scroll-timeline</c>, <c>view-timeline</c>,
/// <c>animation-timeline</c>, <c>animation-range</c>, <c>timeline-scope</c>).
/// </summary>
public static class CssScrollAndViewTimelines
{
    private static bool s_registered;
    private static bool s_scrollEventHooked;
    private static readonly object s_sync = new();
    private static readonly List<WeakReference<DependencyObject>> s_timelineTargets = [];

    public static readonly DependencyProperty AnimationTimelinesProperty =
        DependencyProperty.RegisterAttached(
            "AnimationTimelines",
            typeof(CssAnimationTimelineSpec[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineConfigurationChanged));

    public static readonly DependencyProperty AnimationRangeStartsProperty =
        DependencyProperty.RegisterAttached(
            "AnimationRangeStarts",
            typeof(CssTimelineRangeBoundary[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineConfigurationChanged));

    public static readonly DependencyProperty AnimationRangeEndsProperty =
        DependencyProperty.RegisterAttached(
            "AnimationRangeEnds",
            typeof(CssTimelineRangeBoundary[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineConfigurationChanged));

    public static readonly DependencyProperty ScrollTimelineNamesProperty =
        DependencyProperty.RegisterAttached(
            "ScrollTimelineNames",
            typeof(string[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineProviderChanged));

    public static readonly DependencyProperty ScrollTimelineAxesProperty =
        DependencyProperty.RegisterAttached(
            "ScrollTimelineAxes",
            typeof(CssTimelineAxis[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineProviderChanged));

    public static readonly DependencyProperty ViewTimelineNamesProperty =
        DependencyProperty.RegisterAttached(
            "ViewTimelineNames",
            typeof(string[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineProviderChanged));

    public static readonly DependencyProperty ViewTimelineAxesProperty =
        DependencyProperty.RegisterAttached(
            "ViewTimelineAxes",
            typeof(CssTimelineAxis[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineProviderChanged));

    public static readonly DependencyProperty ViewTimelineInsetsProperty =
        DependencyProperty.RegisterAttached(
            "ViewTimelineInsets",
            typeof(CssViewTimelineInset[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineProviderChanged));

    public static readonly DependencyProperty TimelineScopeProperty =
        DependencyProperty.RegisterAttached(
            "TimelineScope",
            typeof(string[]),
            typeof(CssScrollAndViewTimelines),
            new PropertyMetadata(null, OnTimelineProviderChanged));

    static CssScrollAndViewTimelines()
    {
        EnsureScrollViewerHook();
        CssPropertyRegistry.RegisterExtension(Register);
    }

    internal static void EnsureScrollViewerHook()
    {
        if (s_scrollEventHooked) return;
        lock (s_sync)
        {
            if (s_scrollEventHooked) return;
            s_scrollEventHooked = true;
            EventManager.RegisterClassHandler(
                typeof(ScrollViewer),
                ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(OnScrollViewerScrollChanged),
                handledEventsToo: true);
        }
    }

    /// <summary>
    /// Registers CSS Scroll-Driven Animations Level 1 and <c>animation-composition</c> properties.
    /// </summary>
    public static void Register()
    {
        EnsureScrollViewerHook();

        RegisterLonghand("animation-timeline", AnimationTimelinesProperty, "auto", ParseAnimationTimeline);
        RegisterLonghand("scroll-timeline-name", ScrollTimelineNamesProperty, "none",
            static (ref CssTokenReader r, CssCompileContext _) => ParseTimelineNames(ref r, ScrollTimelineNamesProperty));
        RegisterLonghand("scroll-timeline-axis", ScrollTimelineAxesProperty, "block",
            static (ref CssTokenReader r, CssCompileContext _) => ParseTimelineAxes(ref r, ScrollTimelineAxesProperty));
        RegisterShorthand("scroll-timeline", "none block", ExpandScrollTimeline);

        RegisterLonghand("view-timeline-name", ViewTimelineNamesProperty, "none",
            static (ref CssTokenReader r, CssCompileContext _) => ParseTimelineNames(ref r, ViewTimelineNamesProperty));
        RegisterLonghand("view-timeline-axis", ViewTimelineAxesProperty, "block",
            static (ref CssTokenReader r, CssCompileContext _) => ParseTimelineAxes(ref r, ViewTimelineAxesProperty));
        RegisterLonghand("view-timeline-inset", ViewTimelineInsetsProperty, "auto", ParseViewTimelineInsets);
        RegisterShorthand("view-timeline", "none block auto", ExpandViewTimeline);

        RegisterLonghand("timeline-scope", TimelineScopeProperty, "none", ParseTimelineScope);

        RegisterLonghand("animation-range-start", AnimationRangeStartsProperty, "normal",
            static (ref CssTokenReader r, CssCompileContext ctx) => ParseAnimationRangeSide(ref r, isEnd: false));
        RegisterLonghand("animation-range-end", AnimationRangeEndsProperty, "normal",
            static (ref CssTokenReader r, CssCompileContext ctx) => ParseAnimationRangeSide(ref r, isEnd: true));
        RegisterShorthand("animation-range", "normal", ExpandAnimationRange);

        CssAnimations.RegisterComposition();
        s_registered = true;
    }

    internal static CssAnimationTimelineSpec[]? GetAnimationTimelines(DependencyObject element)
        => element.GetValue(AnimationTimelinesProperty) as CssAnimationTimelineSpec[];

    internal static CssTimelineRangeBoundary[]? GetAnimationRangeStarts(DependencyObject element)
        => element.GetValue(AnimationRangeStartsProperty) as CssTimelineRangeBoundary[];

    internal static CssTimelineRangeBoundary[]? GetAnimationRangeEnds(DependencyObject element)
        => element.GetValue(AnimationRangeEndsProperty) as CssTimelineRangeBoundary[];

    internal static bool HasNonAutoTimeline(DependencyObject target, int animationIndex = 0)
    {
        var specs = GetAnimationTimelines(target);
        if (specs is null || specs.Length == 0) return false;
        var spec = specs[animationIndex % specs.Length];
        return spec.Kind != CssTimelineKind.Auto;
    }

    /// <summary>
    /// Resolves the normalized <c>[0, 1]</c> progress (or unclamped raw progress before fill mode clamping)
    /// for the first scroll/view timeline attached to <paramref name="target"/>.
    /// </summary>
    public static bool TryResolveProgress(DependencyObject target, out double progress)
        => TryResolveProgress(target, 0, out progress);

    /// <summary>
    /// Resolves the progress for the specified animation index on <paramref name="target"/>.
    /// </summary>
    public static bool TryResolveProgress(DependencyObject target, int animationIndex, out double progress)
    {
        ArgumentNullException.ThrowIfNull(target);
        progress = 0.0;

        var specs = GetAnimationTimelines(target);
        if (specs is null || specs.Length == 0)
            return false;

        var spec = specs[Math.Max(0, animationIndex) % specs.Length];
        if (spec.Kind == CssTimelineKind.Auto)
            return false;

        if (spec.Kind == CssTimelineKind.None)
        {
            progress = double.NaN;
            return true;
        }

        var starts = GetAnimationRangeStarts(target);
        var ends = GetAnimationRangeEnds(target);
        var rangeStart = starts is { Length: > 0 }
            ? starts[Math.Max(0, animationIndex) % starts.Length]
            : CssTimelineRangeBoundary.Normal;
        var rangeEnd = ends is { Length: > 0 }
            ? ends[Math.Max(0, animationIndex) % ends.Length]
            : CssTimelineRangeBoundary.Normal;

        return spec.Kind switch
        {
            CssTimelineKind.Scroll => TryEvaluateScrollTimeline(
                ResolveScrollContainer(target, spec.Scroller),
                spec.Axis,
                rangeStart,
                rangeEnd,
                target,
                out progress),
            CssTimelineKind.View => TryEvaluateViewTimeline(
                target as UIElement,
                spec.Axis,
                spec.Inset,
                rangeStart,
                rangeEnd,
                target,
                out progress),
            CssTimelineKind.Named when !string.IsNullOrEmpty(spec.Name) => TryEvaluateNamedTimeline(
                target,
                spec.Name!,
                rangeStart,
                rangeEnd,
                out progress),
            _ => false,
        };
    }

    internal static void TrackTarget(DependencyObject target)
    {
        lock (s_sync)
        {
            for (var i = s_timelineTargets.Count - 1; i >= 0; i--)
            {
                if (!s_timelineTargets[i].TryGetTarget(out var existing))
                {
                    s_timelineTargets.RemoveAt(i);
                }
                else if (ReferenceEquals(existing, target))
                {
                    return;
                }
            }

            s_timelineTargets.Add(new WeakReference<DependencyObject>(target));
        }
    }

    internal static void RefreshActiveTimelines()
    {
        List<DependencyObject>? targets = null;
        lock (s_sync)
        {
            for (var i = s_timelineTargets.Count - 1; i >= 0; i--)
            {
                if (!s_timelineTargets[i].TryGetTarget(out var target))
                {
                    s_timelineTargets.RemoveAt(i);
                    continue;
                }

                targets ??= [];
                targets.Add(target);
            }
        }

        if (targets is null) return;
        foreach (var target in targets)
        {
            if (CssNode.Existing(target)?.CssRuntimeState?.AnimationRunner is { } runner)
            {
                runner.RefreshTimelineProgress();
            }
        }
    }

    private static void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RefreshActiveTimelines();
    }

    private static void OnTimelineConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        TrackTarget(d);
        if (CssNode.Existing(d)?.CssRuntimeState?.AnimationRunner is { } runner)
        {
            runner.RefreshTimelineProgress();
        }
        else if (d.GetValue(CssAnimations.DataProperty) is CssAnimationData data && data.Definitions.Length > 0)
        {
            CssAnimations.Update(CssNode.Get(d), data);
        }
    }

    private static void OnTimelineProviderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        RefreshActiveTimelines();
    }

    private static bool TryEvaluateNamedTimeline(
        DependencyObject target,
        string name,
        CssTimelineRangeBoundary rangeStart,
        CssTimelineRangeBoundary rangeEnd,
        out double progress)
    {
        progress = 0.0;
        if (!TryFindNamedTimelineSource(target, name, out var provider, out var isViewTimeline, out var axis, out var inset))
            return false;

        if (isViewTimeline)
        {
            return TryEvaluateViewTimeline(
                provider as UIElement,
                axis,
                inset,
                rangeStart,
                rangeEnd,
                target,
                out progress);
        }

        var scroller = ResolveScrollContainer(provider, CssScrollScroller.Self)
            ?? ResolveScrollContainer(provider, CssScrollScroller.Nearest);
        return TryEvaluateScrollTimeline(scroller, axis, rangeStart, rangeEnd, target, out progress);
    }

    private static bool TryFindNamedTimelineSource(
        DependencyObject start,
        string timelineName,
        out DependencyObject provider,
        out bool isViewTimeline,
        out CssTimelineAxis axis,
        out CssViewTimelineInset inset)
    {
        provider = null!;
        isViewTimeline = false;
        axis = CssTimelineAxis.Block;
        inset = CssViewTimelineInset.Auto;

        var current = start;
        var guard = 0;
        while (current is not null && guard++ < 512)
        {
            if (TryMatchTimelineProviderOnElement(current, timelineName, out isViewTimeline, out axis, out inset))
            {
                provider = current;
                return true;
            }

            if (DeclaresTimelineScope(current, timelineName) &&
                TryFindDescendantTimelineProvider(current, timelineName, out provider, out isViewTimeline, out axis, out inset))
            {
                return true;
            }

            current = GetParentObject(current);
        }

        return false;
    }

    private static bool DeclaresTimelineScope(DependencyObject element, string timelineName)
    {
        if (element.GetValue(TimelineScopeProperty) is not string[] scopes || scopes.Length == 0)
            return false;

        for (var i = 0; i < scopes.Length; i++)
        {
            if (string.Equals(scopes[i], "all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scopes[i], timelineName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindDescendantTimelineProvider(
        DependencyObject root,
        string timelineName,
        out DependencyObject provider,
        out bool isViewTimeline,
        out CssTimelineAxis axis,
        out CssViewTimelineInset inset)
    {
        provider = null!;
        isViewTimeline = false;
        axis = CssTimelineAxis.Block;
        inset = CssViewTimelineInset.Auto;

        if (root is not Visual rootVisual)
            return false;

        var queue = new Queue<Visual>();
        queue.Enqueue(rootVisual);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!ReferenceEquals(current, rootVisual) &&
                TryMatchTimelineProviderOnElement(current, timelineName, out isViewTimeline, out axis, out inset))
            {
                provider = current;
                return true;
            }

            var count = current.InternalVisualChildrenCount;
            for (var i = 0; i < count; i++)
            {
                if (current.InternalGetVisualChild(i) is { } child)
                    queue.Enqueue(child);
            }
        }

        return false;
    }

    private static bool TryMatchTimelineProviderOnElement(
        DependencyObject element,
        string timelineName,
        out bool isViewTimeline,
        out CssTimelineAxis axis,
        out CssViewTimelineInset inset)
    {
        isViewTimeline = false;
        axis = CssTimelineAxis.Block;
        inset = CssViewTimelineInset.Auto;

        if (element.GetValue(ViewTimelineNamesProperty) is string[] viewNames)
        {
            for (var i = 0; i < viewNames.Length; i++)
            {
                if (string.Equals(viewNames[i], timelineName, StringComparison.Ordinal))
                {
                    isViewTimeline = true;
                    var axes = element.GetValue(ViewTimelineAxesProperty) as CssTimelineAxis[];
                    var insets = element.GetValue(ViewTimelineInsetsProperty) as CssViewTimelineInset[];
                    axis = axes is { Length: > 0 } ? axes[i % axes.Length] : CssTimelineAxis.Block;
                    inset = insets is { Length: > 0 } ? insets[i % insets.Length] : CssViewTimelineInset.Auto;
                    return true;
                }
            }
        }

        if (element.GetValue(ScrollTimelineNamesProperty) is string[] scrollNames)
        {
            for (var i = 0; i < scrollNames.Length; i++)
            {
                if (string.Equals(scrollNames[i], timelineName, StringComparison.Ordinal))
                {
                    isViewTimeline = false;
                    var axes = element.GetValue(ScrollTimelineAxesProperty) as CssTimelineAxis[];
                    axis = axes is { Length: > 0 } ? axes[i % axes.Length] : CssTimelineAxis.Block;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryEvaluateScrollTimeline(
        ScrollViewer? scroller,
        CssTimelineAxis axis,
        CssTimelineRangeBoundary rangeStart,
        CssTimelineRangeBoundary rangeEnd,
        DependencyObject target,
        out double progress)
    {
        progress = 0.0;
        if (scroller is null)
            return false;

        var horizontal = IsHorizontalAxis(axis, scroller);
        var scrollOffset = horizontal ? scroller.HorizontalOffset : scroller.VerticalOffset;
        var maxScroll = horizontal ? scroller.ScrollableWidth : scroller.ScrollableHeight;
        if (maxScroll <= 0)
        {
            var extent = horizontal ? scroller.ExtentWidth : scroller.ExtentHeight;
            var viewport = horizontal ? scroller.ViewportWidth : scroller.ViewportHeight;
            if (viewport <= 0)
                viewport = horizontal ? scroller.ActualWidth : scroller.ActualHeight;
            maxScroll = Math.Max(0, extent - viewport);
        }

        if (maxScroll <= 0)
        {
            progress = 0.0;
            return true;
        }

        var lengths = BuildLengthContext(target, maxScroll, maxScroll);
        var startPos = ResolveScrollRangeBoundary(rangeStart, maxScroll, isEnd: false, in lengths);
        var endPos = ResolveScrollRangeBoundary(rangeEnd, maxScroll, isEnd: true, in lengths);
        var span = endPos - startPos;
        if (Math.Abs(span) < 1e-6)
        {
            progress = scrollOffset >= endPos ? 1.0 : 0.0;
            return true;
        }

        progress = Math.Clamp((scrollOffset - startPos) / span, 0.0, 1.0);
        return true;
    }

    private static bool TryEvaluateViewTimeline(
        UIElement? subject,
        CssTimelineAxis axis,
        CssViewTimelineInset inset,
        CssTimelineRangeBoundary rangeStart,
        CssTimelineRangeBoundary rangeEnd,
        DependencyObject target,
        out double progress)
    {
        progress = 0.0;
        if (subject is null)
            return false;

        var scroller = ResolveScrollContainer(subject, CssScrollScroller.Nearest);
        if (scroller is null)
            return false;

        var horizontal = IsHorizontalAxis(axis, scroller);
        var scrollOffset = horizontal ? scroller.HorizontalOffset : scroller.VerticalOffset;
        var viewportSize = horizontal ? scroller.ViewportWidth : scroller.ViewportHeight;
        if (viewportSize <= 0)
            viewportSize = horizontal ? scroller.ActualWidth : scroller.ActualHeight;
        if (viewportSize <= 0)
            viewportSize = horizontal ? scroller.RenderSize.Width : scroller.RenderSize.Height;
        if (viewportSize <= 0)
            return false;

        var subjectSize = horizontal ? subject.RenderSize.Width : subject.RenderSize.Height;
        if (subjectSize <= 0 && subject is FrameworkElement fe)
        {
            subjectSize = horizontal ? fe.ActualWidth : fe.ActualHeight;
            if (subjectSize <= 0 && double.IsFinite(horizontal ? fe.Width : fe.Height))
                subjectSize = horizontal ? fe.Width : fe.Height;
        }

        // Compute the subject's un-scrolled position P0 inside the scrollable content.
        var p0 = ComputeSubjectContentOffset(subject, scroller, horizontal, scrollOffset);

        var insetLengths = BuildLengthContext(subject, viewportSize, viewportSize);
        var startInset = inset.Start.Unit == CssUnit.Auto ? 0.0 : ResolveLengthOrPercent(inset.Start, in insetLengths, viewportSize);
        var endInset = inset.End.Unit == CssUnit.Auto ? 0.0 : ResolveLengthOrPercent(inset.End, in insetLengths, viewportSize);

        // Key scroll offsets where subject edges align with the inset-adjusted scrollport edges:
        // Enter start (cover 0%): subject start edge reaches scrollport end edge (viewportSize - endInset)
        var enterStart = p0 - (viewportSize - endInset);
        // Enter end: subject end edge reaches scrollport end edge
        var enterEnd = p0 + subjectSize - (viewportSize - endInset);
        // Exit start: subject start edge reaches scrollport start edge (startInset)
        var exitStart = p0 - startInset;
        // Exit end (cover 100%): subject end edge reaches scrollport start edge
        var exitEnd = p0 + subjectSize - startInset;

        var rangeLengths = BuildLengthContext(target, viewportSize, viewportSize);
        var startOffset = ResolveViewRangeBoundary(
            rangeStart, isEnd: false, enterStart, enterEnd, exitStart, exitEnd, in rangeLengths);
        var endOffset = ResolveViewRangeBoundary(
            rangeEnd, isEnd: true, enterStart, enterEnd, exitStart, exitEnd, in rangeLengths);

        var span = endOffset - startOffset;
        if (Math.Abs(span) < 1e-6)
        {
            progress = scrollOffset >= endOffset ? 1.0 : 0.0;
            return true;
        }

        progress = Math.Clamp((scrollOffset - startOffset) / span, 0.0, 1.0);
        return true;
    }

    private static double ComputeSubjectContentOffset(
        UIElement subject,
        ScrollViewer scroller,
        bool horizontal,
        double scrollOffset)
    {
        if (scroller.Content is Visual contentRoot &&
            (ReferenceEquals(subject, contentRoot) || contentRoot.IsAncestorOf(subject)))
        {
            var relToContent = ReferenceEquals(subject, contentRoot)
                ? new Point(0, 0)
                : subject.TransformToAncestor(contentRoot).Transform(new Point(0, 0));
            return horizontal ? relToContent.X : relToContent.Y;
        }

        var relToScroller = ReferenceEquals(subject, scroller)
            ? new Point(0, 0)
            : subject.TransformToAncestor(scroller).Transform(new Point(0, 0));
        var visualOffset = horizontal ? relToScroller.X : relToScroller.Y;
        return visualOffset + scrollOffset;
    }

    private static double ResolveScrollRangeBoundary(
        CssTimelineRangeBoundary boundary,
        double maxScroll,
        bool isEnd,
        in CssLengthContext lengths)
    {
        if (!boundary.HasExplicitOffset &&
            boundary.RangeName is CssTimelineRangeName.Normal or CssTimelineRangeName.None)
        {
            return isEnd ? maxScroll : 0.0;
        }

        if (boundary.HasExplicitOffset)
        {
            return ResolveLengthOrPercent(boundary.Offset, in lengths, maxScroll);
        }

        return isEnd ? maxScroll : 0.0;
    }

    private static double ResolveViewRangeBoundary(
        CssTimelineRangeBoundary boundary,
        bool isEnd,
        double enterStart,
        double enterEnd,
        double exitStart,
        double exitEnd,
        in CssLengthContext lengths)
    {
        var containStart = Math.Min(enterEnd, exitStart);
        var containEnd = Math.Max(enterEnd, exitStart);

        var (windowStart, windowEnd) = boundary.RangeName switch
        {
            CssTimelineRangeName.Contain => (containStart, containEnd),
            CssTimelineRangeName.Entry => (enterStart, containStart),
            CssTimelineRangeName.Exit => (containEnd, exitEnd),
            CssTimelineRangeName.EntryCrossing => (enterStart, enterEnd),
            CssTimelineRangeName.ExitCrossing => (exitStart, exitEnd),
            _ => (enterStart, exitEnd), // Cover, Normal, None
        };

        var windowSpan = windowEnd - windowStart;
        if (!boundary.HasExplicitOffset)
        {
            return isEnd ? windowEnd : windowStart;
        }

        if (boundary.Offset.Unit == CssUnit.Percent)
        {
            return windowStart + (boundary.Offset.Value / 100.0) * windowSpan;
        }

        return windowStart + ResolveLengthOrPercent(boundary.Offset, in lengths, windowSpan);
    }

    private static double ResolveLengthOrPercent(CssLength length, in CssLengthContext context, double basisPx)
    {
        if (length.Unit == CssUnit.Percent)
            return (length.Value / 100.0) * basisPx;
        if (length.Unit == CssUnit.Expression && length.Expression is { } expr)
            return expr.TryEvaluate(context, basisPx, out var eval) && double.IsFinite(eval) ? eval : 0.0;
        return length.TryResolve(in context, CssPercentBasis.Fraction, out var px) && double.IsFinite(px) ? px : 0.0;
    }

    private static ScrollViewer? ResolveScrollContainer(DependencyObject start, CssScrollScroller scrollerMode)
    {
        if (scrollerMode == CssScrollScroller.Self)
        {
            return ExtractScrollViewer(start);
        }

        ScrollViewer? nearest = null;
        ScrollViewer? root = null;
        var current = scrollerMode == CssScrollScroller.Nearest ? GetParentObject(start) ?? start : start;
        var guard = 0;
        while (current is not null && guard++ < 512)
        {
            if (ExtractScrollViewer(current) is { } sv)
            {
                nearest ??= sv;
                root = sv;
            }

            current = GetParentObject(current);
        }

        if (nearest is null && ExtractScrollViewer(start) is { } selfViewer)
        {
            nearest = selfViewer;
            root ??= selfViewer;
        }

        return scrollerMode == CssScrollScroller.Root ? root : nearest;
    }

    private static ScrollViewer? ExtractScrollViewer(DependencyObject node)
    {
        if (node is ScrollViewer sv)
            return sv;

        if (node is FlowDocumentScrollViewer docViewer)
        {
            return FindVisualDescendantScrollViewer(docViewer);
        }

        return null;
    }

    private static ScrollViewer? FindVisualDescendantScrollViewer(Visual root)
    {
        var count = root.InternalVisualChildrenCount;
        for (var i = 0; i < count; i++)
        {
            if (root.InternalGetVisualChild(i) is not { } child) continue;
            if (child is ScrollViewer sv) return sv;
            if (FindVisualDescendantScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        if (current is Visual v && v.VisualParent is { } vp)
            return vp;
        if (current is FrameworkElement fe && fe.Parent is { } p)
            return p;
        if (current is FrameworkContentElement fce && fce.Parent is { } cp)
            return cp;
        if (CssNode.Existing(current)?.FrameworkParent is { } fp)
            return fp.Target;
        return null;
    }

    private static bool IsHorizontalAxis(CssTimelineAxis axis, ScrollViewer scroller)
        => axis is CssTimelineAxis.X or CssTimelineAxis.Inline;

    private static CssLengthContext BuildLengthContext(DependencyObject target, double refW, double refH)
    {
        var fontSize = target switch
        {
            Control c => c.FontSize,
            TextBlock tb => tb.FontSize,
            TextElement te => te.FontSize,
            _ => 16.0,
        };
        if (!double.IsFinite(fontSize) || fontSize <= 0) fontSize = 16.0;
        return new CssLengthContext(fontSize, fontSize, fontSize, Math.Max(1, refW), Math.Max(1, refH));
    }

    private static void RegisterLonghand(
        string name,
        DependencyProperty property,
        string initialValue,
        CssParseValueDelegate parse)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = property,
            Parse = parse,
        });
    }

    private static void RegisterShorthand(string name, string initialValue, CssExpandDelegate expand)
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Shorthand,
            Expand = expand,
        });
    }

    private static CssCompiledValue? ParseAnimationTimeline(ref CssTokenReader reader, CssCompileContext context)
    {
        if (!TryParseAnimationTimelineList(ref reader, out var items) || !reader.AtEnd)
            return null;

        return new CssImmediateValue(AnimationTimelinesProperty, items);
    }

    internal static bool TryParseAnimationTimelineList(ref CssTokenReader reader, out CssAnimationTimelineSpec[] items)
    {
        var list = new List<CssAnimationTimelineSpec>();
        while (true)
        {
            if (!TryParseSingleAnimationTimeline(ref reader, out var item))
            {
                items = [];
                return false;
            }
            list.Add(item);
            if (reader.AtEnd) break;
            if (!reader.TryReadComma())
            {
                items = [];
                return false;
            }
        }

        items = list.ToArray();
        return items.Length > 0;
    }

    internal static bool TryParseSingleAnimationTimeline(ref CssTokenReader reader, out CssAnimationTimelineSpec spec)
    {
        spec = CssAnimationTimelineSpec.Auto;
        var probe = reader;
        if (probe.TryReadFunction(out var func, out var argsReader))
        {
            if (func.Equals("scroll", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseScrollFunctionArgs(ref argsReader, out var scroller, out var axis))
                    return false;
                reader = probe;
                spec = new CssAnimationTimelineSpec(CssTimelineKind.Scroll, null, scroller, axis);
                return true;
            }

            if (func.Equals("view", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseViewFunctionArgs(ref argsReader, out var axis, out var inset))
                    return false;
                reader = probe;
                spec = new CssAnimationTimelineSpec(CssTimelineKind.View, null, CssScrollScroller.Nearest, axis, inset);
                return true;
            }

            return false;
        }

        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                spec = CssAnimationTimelineSpec.Auto;
                return true;
            }

            if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                spec = CssAnimationTimelineSpec.None;
                return true;
            }

            if (IsValidTimelineNameIdent(ident))
            {
                reader = probe;
                spec = new CssAnimationTimelineSpec(CssTimelineKind.Named, ident.ToString());
                return true;
            }
        }

        return false;
    }

    private static bool TryParseScrollFunctionArgs(
        ref CssTokenReader reader,
        out CssScrollScroller scroller,
        out CssTimelineAxis axis)
    {
        scroller = CssScrollScroller.Nearest;
        axis = CssTimelineAxis.Block;
        var hasScroller = false;
        var hasAxis = false;

        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var ident))
                return false;

            if (!hasScroller && TryParseScrollerKeyword(ident, out var parsedScroller))
            {
                scroller = parsedScroller;
                hasScroller = true;
                continue;
            }

            if (!hasAxis && TryParseAxisKeyword(ident, out var parsedAxis))
            {
                axis = parsedAxis;
                hasAxis = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryParseViewFunctionArgs(
        ref CssTokenReader reader,
        out CssTimelineAxis axis,
        out CssViewTimelineInset inset)
    {
        axis = CssTimelineAxis.Block;
        inset = CssViewTimelineInset.Auto;
        var hasAxis = false;
        var insetParts = new List<CssLength>(2);

        while (!reader.AtEnd)
        {
            var probe = reader;
            if (!hasAxis && probe.TryReadIdent(out var ident) && TryParseAxisKeyword(ident, out var parsedAxis))
            {
                reader = probe;
                axis = parsedAxis;
                hasAxis = true;
                continue;
            }

            if (insetParts.Count < 2 && TryReadInsetLength(ref reader, out var len))
            {
                insetParts.Add(len);
                continue;
            }

            return false;
        }

        if (insetParts.Count == 1)
            inset = new CssViewTimelineInset(insetParts[0], insetParts[0]);
        else if (insetParts.Count == 2)
            inset = new CssViewTimelineInset(insetParts[0], insetParts[1]);
        return true;
    }

    private static CssCompiledValue? ParseTimelineNames(ref CssTokenReader reader, DependencyProperty targetProperty)
    {
        var list = new List<string>();
        while (true)
        {
            if (!reader.TryReadIdent(out var ident))
                return null;

            if (ident.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(string.Empty);
            }
            else if (IsValidTimelineNameIdent(ident))
            {
                list.Add(ident.ToString());
            }
            else
            {
                return null;
            }

            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return null;
        }

        return new CssImmediateValue(targetProperty, list.ToArray());
    }

    private static CssCompiledValue? ParseTimelineAxes(ref CssTokenReader reader, DependencyProperty targetProperty)
    {
        var list = new List<CssTimelineAxis>();
        while (true)
        {
            if (!reader.TryReadIdent(out var ident) || !TryParseAxisKeyword(ident, out var axis))
                return null;
            list.Add(axis);
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return null;
        }

        return new CssImmediateValue(targetProperty, list.ToArray());
    }

    private static CssCompiledValue? ParseViewTimelineInsets(ref CssTokenReader reader, CssCompileContext context)
    {
        var list = new List<CssViewTimelineInset>();
        while (true)
        {
            if (!TryReadInsetLength(ref reader, out var first))
                return null;
            var second = first;
            if (!reader.AtEnd)
            {
                var probe = reader;
                if (!probe.TryReadComma() && TryReadInsetLength(ref probe, out var parsedSecond))
                {
                    reader = probe;
                    second = parsedSecond;
                }
            }

            list.Add(new CssViewTimelineInset(first, second));
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return null;
        }

        return new CssImmediateValue(ViewTimelineInsetsProperty, list.ToArray());
    }

    private static CssCompiledValue? ParseTimelineScope(ref CssTokenReader reader, CssCompileContext context)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var single) && probe.AtEnd)
        {
            if (single.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                return new CssImmediateValue(TimelineScopeProperty, Array.Empty<string>());
            }

            if (single.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                return new CssImmediateValue(TimelineScopeProperty, new[] { "all" });
            }
        }

        var list = new List<string>();
        while (true)
        {
            if (!reader.TryReadIdent(out var ident) || !IsValidTimelineNameIdent(ident))
                return null;
            list.Add(ident.ToString());
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return null;
        }

        return new CssImmediateValue(TimelineScopeProperty, list.ToArray());
    }

    private static CssCompiledValue? ParseAnimationRangeSide(ref CssTokenReader reader, bool isEnd)
    {
        var list = new List<CssTimelineRangeBoundary>();
        while (true)
        {
            if (!TryReadRangeBoundary(ref reader, isEnd, out var boundary))
                return null;
            list.Add(boundary);
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return null;
        }

        return new CssImmediateValue(
            isEnd ? AnimationRangeEndsProperty : AnimationRangeStartsProperty,
            list.ToArray());
    }

    private static bool ExpandScrollTimeline(
        ref CssTokenReader reader,
        CssCompileContext context,
        List<CssCompiledDeclaration> output)
    {
        var names = new List<string>();
        var axes = new List<CssTimelineAxis>();

        while (true)
        {
            if (!reader.TryReadIdent(out var nameIdent))
                return false;

            string name;
            if (nameIdent.Equals("none", StringComparison.OrdinalIgnoreCase))
                name = string.Empty;
            else if (IsValidTimelineNameIdent(nameIdent))
                name = nameIdent.ToString();
            else
                return false;

            var axis = CssTimelineAxis.Block;
            var probe = reader;
            if (!probe.AtEnd && probe.TryReadIdent(out var axisIdent) && TryParseAxisKeyword(axisIdent, out var parsedAxis))
            {
                reader = probe;
                axis = parsedAxis;
            }

            names.Add(name);
            axes.Add(axis);

            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return false;
        }

        output.Add(new CssCompiledDeclaration(
            "scroll-timeline-name",
            new CssImmediateValue(ScrollTimelineNamesProperty, names.ToArray()),
            false));
        output.Add(new CssCompiledDeclaration(
            "scroll-timeline-axis",
            new CssImmediateValue(ScrollTimelineAxesProperty, axes.ToArray()),
            false));
        return true;
    }

    private static bool ExpandViewTimeline(
        ref CssTokenReader reader,
        CssCompileContext context,
        List<CssCompiledDeclaration> output)
    {
        var names = new List<string>();
        var axes = new List<CssTimelineAxis>();
        var insets = new List<CssViewTimelineInset>();

        while (true)
        {
            if (!reader.TryReadIdent(out var nameIdent))
                return false;

            string name;
            if (nameIdent.Equals("none", StringComparison.OrdinalIgnoreCase))
                name = string.Empty;
            else if (IsValidTimelineNameIdent(nameIdent))
                name = nameIdent.ToString();
            else
                return false;

            var axis = CssTimelineAxis.Block;
            var hasAxis = false;
            var insetParts = new List<CssLength>(2);

            while (!reader.AtEnd)
            {
                var commaProbe = reader;
                if (commaProbe.TryReadComma())
                    break;

                var axisProbe = reader;
                if (!hasAxis && axisProbe.TryReadIdent(out var axisIdent) && TryParseAxisKeyword(axisIdent, out var parsedAxis))
                {
                    reader = axisProbe;
                    axis = parsedAxis;
                    hasAxis = true;
                    continue;
                }

                if (insetParts.Count < 2 && TryReadInsetLength(ref reader, out var len))
                {
                    insetParts.Add(len);
                    continue;
                }

                return false;
            }

            var inset = insetParts.Count switch
            {
                1 => new CssViewTimelineInset(insetParts[0], insetParts[0]),
                2 => new CssViewTimelineInset(insetParts[0], insetParts[1]),
                _ => CssViewTimelineInset.Auto,
            };

            names.Add(name);
            axes.Add(axis);
            insets.Add(inset);

            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return false;
        }

        output.Add(new CssCompiledDeclaration(
            "view-timeline-name",
            new CssImmediateValue(ViewTimelineNamesProperty, names.ToArray()),
            false));
        output.Add(new CssCompiledDeclaration(
            "view-timeline-axis",
            new CssImmediateValue(ViewTimelineAxesProperty, axes.ToArray()),
            false));
        output.Add(new CssCompiledDeclaration(
            "view-timeline-inset",
            new CssImmediateValue(ViewTimelineInsetsProperty, insets.ToArray()),
            false));
        return true;
    }

    private static bool ExpandAnimationRange(
        ref CssTokenReader reader,
        CssCompileContext context,
        List<CssCompiledDeclaration> output)
    {
        var starts = new List<CssTimelineRangeBoundary>();
        var ends = new List<CssTimelineRangeBoundary>();

        while (true)
        {
            if (!TryReadRangeBoundary(ref reader, isEnd: false, out var start))
                return false;

            CssTimelineRangeBoundary end;
            var commaProbe = reader;
            if (reader.AtEnd || commaProbe.TryReadComma())
            {
                // Single range token: if a named range like "contain" or "entry 20%" was specified,
                // animation-range-end defaults to the same range name at 100%; otherwise "normal".
                end = start.RangeName is not (CssTimelineRangeName.Normal or CssTimelineRangeName.None)
                    ? new CssTimelineRangeBoundary(start.RangeName, new CssLength(100, CssUnit.Percent), false)
                    : CssTimelineRangeBoundary.Normal;
            }
            else
            {
                if (!TryReadRangeBoundary(ref reader, isEnd: true, out end))
                    return false;
            }

            starts.Add(start);
            ends.Add(end);

            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return false;
        }

        output.Add(new CssCompiledDeclaration(
            "animation-range-start",
            new CssImmediateValue(AnimationRangeStartsProperty, starts.ToArray()),
            false));
        output.Add(new CssCompiledDeclaration(
            "animation-range-end",
            new CssImmediateValue(AnimationRangeEndsProperty, ends.ToArray()),
            false));
        return true;
    }

    private static bool TryReadRangeBoundary(
        ref CssTokenReader reader,
        bool isEnd,
        out CssTimelineRangeBoundary boundary)
    {
        boundary = CssTimelineRangeBoundary.Normal;
        var probe = reader;

        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                reader = probe;
                boundary = CssTimelineRangeBoundary.Normal;
                return true;
            }

            if (TryParseRangeName(ident, out var rangeName))
            {
                reader = probe;
                var lengthProbe = reader;
                if (lengthProbe.TryReadLength(out var offset))
                {
                    reader = lengthProbe;
                    boundary = new CssTimelineRangeBoundary(rangeName, offset, true);
                    return true;
                }

                var defaultPct = new CssLength(isEnd ? 100 : 0, CssUnit.Percent);
                boundary = new CssTimelineRangeBoundary(rangeName, defaultPct, false);
                return true;
            }

            return false;
        }

        if (probe.TryReadLength(out var bareOffset))
        {
            reader = probe;
            boundary = new CssTimelineRangeBoundary(CssTimelineRangeName.Normal, bareOffset, true);
            return true;
        }

        return false;
    }

    private static bool TryReadInsetLength(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            length = new CssLength(0, CssUnit.Auto);
            return true;
        }

        return reader.TryReadLength(out length);
    }

    private static bool TryParseScrollerKeyword(ReadOnlySpan<char> ident, out CssScrollScroller scroller)
    {
        if (ident.Equals("nearest", StringComparison.OrdinalIgnoreCase))
        {
            scroller = CssScrollScroller.Nearest;
            return true;
        }
        if (ident.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            scroller = CssScrollScroller.Root;
            return true;
        }
        if (ident.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            scroller = CssScrollScroller.Self;
            return true;
        }
        scroller = default;
        return false;
    }

    private static bool TryParseAxisKeyword(ReadOnlySpan<char> ident, out CssTimelineAxis axis)
    {
        if (ident.Equals("block", StringComparison.OrdinalIgnoreCase))
        {
            axis = CssTimelineAxis.Block;
            return true;
        }
        if (ident.Equals("inline", StringComparison.OrdinalIgnoreCase))
        {
            axis = CssTimelineAxis.Inline;
            return true;
        }
        if (ident.Equals("x", StringComparison.OrdinalIgnoreCase))
        {
            axis = CssTimelineAxis.X;
            return true;
        }
        if (ident.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            axis = CssTimelineAxis.Y;
            return true;
        }
        axis = default;
        return false;
    }

    private static bool TryParseRangeName(ReadOnlySpan<char> ident, out CssTimelineRangeName rangeName)
    {
        if (ident.Equals("cover", StringComparison.OrdinalIgnoreCase))
        {
            rangeName = CssTimelineRangeName.Cover;
            return true;
        }
        if (ident.Equals("contain", StringComparison.OrdinalIgnoreCase))
        {
            rangeName = CssTimelineRangeName.Contain;
            return true;
        }
        if (ident.Equals("entry", StringComparison.OrdinalIgnoreCase))
        {
            rangeName = CssTimelineRangeName.Entry;
            return true;
        }
        if (ident.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            rangeName = CssTimelineRangeName.Exit;
            return true;
        }
        if (ident.Equals("entry-crossing", StringComparison.OrdinalIgnoreCase))
        {
            rangeName = CssTimelineRangeName.EntryCrossing;
            return true;
        }
        if (ident.Equals("exit-crossing", StringComparison.OrdinalIgnoreCase))
        {
            rangeName = CssTimelineRangeName.ExitCrossing;
            return true;
        }
        rangeName = default;
        return false;
    }

    private static bool IsValidTimelineNameIdent(ReadOnlySpan<char> ident)
    {
        if (ident.Length == 0)
            return false;
        return !ident.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
               !ident.Equals("none", StringComparison.OrdinalIgnoreCase) &&
               !ident.Equals("normal", StringComparison.OrdinalIgnoreCase) &&
               !ident.Equals("block", StringComparison.OrdinalIgnoreCase) &&
               !ident.Equals("inline", StringComparison.OrdinalIgnoreCase) &&
               !ident.Equals("x", StringComparison.OrdinalIgnoreCase) &&
               !ident.Equals("y", StringComparison.OrdinalIgnoreCase);
    }
}
