using System.Diagnostics;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Which scroll host a <see cref="RazorItemsHost"/> should use.</summary>
public enum RazorVirtualizeScrollHost
{
    /// <summary>Reuse an enclosing <see cref="ScrollViewer"/> when it adopted this host as its
    /// scroll info; otherwise build an inner one. This is the default.</summary>
    Auto = 0,

    /// <summary>Always behave as the scrolling content of an enclosing <see cref="ScrollViewer"/>.</summary>
    Outer = 1,

    /// <summary>Always build an inner <see cref="ScrollViewer"/>.</summary>
    Self = 2,

    /// <summary>Never scroll. The host is laid out at its full extent by whatever contains it.</summary>
    None = 3,
}

/// <summary>Which virtualizing panel a <see cref="RazorItemsHost"/> lays its items out with.</summary>
public enum RazorVirtualizeLayout
{
    /// <summary>One item per line along <see cref="RazorItemsHost.Orientation"/>.</summary>
    Stack = 0,

    /// <summary>Items wrap into rows or columns.</summary>
    Wrap = 1,
}

/// <summary>
/// What a <see cref="RazorItemsHost"/> does when it is measured with an infinite constraint on
/// its scrolling axis, which makes virtualization impossible.
/// </summary>
public enum RazorVirtualizeUnbounded
{
    /// <summary>Fall back to non-virtualized layout so the items still render. Default.</summary>
    Degrade = 0,

    /// <summary>Clamp the scrolling axis to <see cref="RazorItemsHost.FallbackViewportLength"/>.</summary>
    FixedViewport = 1,

    /// <summary>Report the problem and render nothing.</summary>
    Strict = 2,
}

/// <summary>
/// Runtime host for the <c>@virtualize</c> Razor directive: an <see cref="ItemsControl"/> that
/// arrives pre-wired for virtualization and picks its own scroll host.
/// </summary>
/// <remarks>
/// <para>
/// Virtualization here needs four things at once and silently degrades if any is missing: a
/// <see cref="ScrollViewer"/> whose content is <em>directly</em> an <see cref="IScrollInfo"/>, an
/// items panel that is a <see cref="VirtualizingPanel"/>, <c>IsVirtualizing</c> on the owner, and
/// containers with a definite size along the scroll axis. A bare <see cref="ItemsControl"/>
/// satisfies none of the first three, so this host supplies its own control template and items
/// panel instead of depending on a theme.
/// </para>
/// <para>
/// The host implements <see cref="IScrollInfo"/> and forwards to the inner
/// <see cref="ItemsPresenter"/>. That is what makes "reuse the outer scroll viewer" work with no
/// extra plumbing when its <see cref="ScrollViewer.CanContentScroll"/> is enabled: an enclosing viewer assigns
/// <see cref="IScrollInfo.ScrollOwner"/> when its content is set, which happens while the tree is
/// being built and therefore before the first measure. By the time <see cref="MeasureOverride"/>
/// runs, a non-null owner already means an outer viewport is driving this host.
/// </para>
/// </remarks>
public sealed class RazorItemsHost : ItemsControl, IScrollInfo
{
    // Templates are shared statics. FrameworkTemplate.LoadContent invokes the factory on every
    // call, so one sealed instance still produces a fresh tree per host.
    private static readonly ControlTemplate s_passthroughTemplate = BuildTemplate(static () => new ItemsPresenter());

    private static readonly ControlTemplate s_selfScrollVerticalTemplate = BuildTemplate(static () => new ScrollViewer
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = true,

        // Must be the ScrollViewer's direct content: OnContentChanged does
        // "ScrollInfo = ContentElement as IScrollInfo", so any wrapper here breaks the chain and
        // the panel ends up measured unbounded.
        Content = new ItemsPresenter(),
    });

    private static readonly ControlTemplate s_selfScrollHorizontalTemplate = BuildTemplate(static () => new ScrollViewer
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = true,
        Content = new ItemsPresenter(),
    });

    // ItemsPanelTemplate.CreatePanel uses Activator when PanelType is set, which cannot pass
    // Orientation. Going through SetVisualTree keeps the panels configurable and reflection-free.
    private static readonly ItemsPanelTemplate s_stackVertical =
        BuildPanel(static () => new VirtualizingStackPanel { Orientation = Orientation.Vertical });

    private static readonly ItemsPanelTemplate s_stackHorizontal =
        BuildPanel(static () => new VirtualizingStackPanel { Orientation = Orientation.Horizontal });

    private static readonly ItemsPanelTemplate s_wrapVertical =
        BuildPanel(static () => new VirtualizingWrapPanel { Orientation = Orientation.Vertical });

    private static readonly ItemsPanelTemplate s_wrapHorizontal =
        BuildPanel(static () => new VirtualizingWrapPanel { Orientation = Orientation.Horizontal });

    private ItemsPresenter? _presenter;
    private ScrollViewer? _innerScrollViewer;
    private ScrollViewer? _scrollOwner;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;
    private RazorVirtualizeScrollHost _appliedMode = (RazorVirtualizeScrollHost)(-1);
    private Orientation _appliedOrientation = (Orientation)(-1);
    private RazorIntRange? _range;
    private bool _unboundedReported;
    private bool _degraded;

    static RazorItemsHost()
    {
        // A missing viewport is temporary. Coercion keeps the application's value, style,
        // and binding intact, so a later bounded layout can resume virtualization.
        VirtualizingPanel.IsVirtualizingProperty.OverrideMetadata(typeof(RazorItemsHost),
            new PropertyMetadata(true, null, static (d, value) =>
                !((RazorItemsHost)d)._degraded && (bool)(value ?? true)));
    }

    #region Dependency properties

    /// <summary>Identifies the <see cref="Orientation"/> dependency property.</summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(RazorItemsHost),
            new PropertyMetadata(Orientation.Vertical, OnLayoutShapeChanged));

    /// <summary>Identifies the <see cref="Layout"/> dependency property.</summary>
    public static readonly DependencyProperty LayoutProperty =
        DependencyProperty.Register(nameof(Layout), typeof(RazorVirtualizeLayout), typeof(RazorItemsHost),
            new PropertyMetadata(RazorVirtualizeLayout.Stack, OnLayoutShapeChanged));

    /// <summary>Identifies the <see cref="ScrollHost"/> dependency property.</summary>
    public static readonly DependencyProperty ScrollHostProperty =
        DependencyProperty.Register(nameof(ScrollHost), typeof(RazorVirtualizeScrollHost), typeof(RazorItemsHost),
            new PropertyMetadata(RazorVirtualizeScrollHost.Auto, OnScrollHostChanged));

    /// <summary>Identifies the <see cref="UnboundedBehavior"/> dependency property.</summary>
    public static readonly DependencyProperty UnboundedBehaviorProperty =
        DependencyProperty.Register(nameof(UnboundedBehavior), typeof(RazorVirtualizeUnbounded), typeof(RazorItemsHost),
            new PropertyMetadata(RazorVirtualizeUnbounded.Degrade));

    /// <summary>Identifies the <see cref="FallbackViewportLength"/> dependency property.</summary>
    public static readonly DependencyProperty FallbackViewportLengthProperty =
        DependencyProperty.Register(nameof(FallbackViewportLength), typeof(double), typeof(RazorItemsHost),
            new PropertyMetadata(400d));

    /// <summary>Identifies the <see cref="MaxEagerItemCount"/> dependency property.</summary>
    public static readonly DependencyProperty MaxEagerItemCountProperty =
        DependencyProperty.Register(nameof(MaxEagerItemCount), typeof(int), typeof(RazorItemsHost),
            new PropertyMetadata(5000));

    /// <summary>Identifies the <see cref="RangeStart"/> dependency property.</summary>
    public static readonly DependencyProperty RangeStartProperty =
        DependencyProperty.Register(nameof(RangeStart), typeof(int), typeof(RazorItemsHost),
            new PropertyMetadata(0, OnRangeChanged));

    /// <summary>Identifies the <see cref="RangeEnd"/> dependency property.</summary>
    public static readonly DependencyProperty RangeEndProperty =
        DependencyProperty.Register(nameof(RangeEnd), typeof(int), typeof(RazorItemsHost),
            new PropertyMetadata(0, OnRangeChanged));

    /// <summary>Identifies the <see cref="RangeEndInclusive"/> dependency property.</summary>
    public static readonly DependencyProperty RangeEndInclusiveProperty =
        DependencyProperty.Register(nameof(RangeEndInclusive), typeof(bool), typeof(RazorItemsHost),
            new PropertyMetadata(false, OnRangeChanged));

    /// <summary>Identifies the <see cref="IsRangeSource"/> dependency property.</summary>
    public static readonly DependencyProperty IsRangeSourceProperty =
        DependencyProperty.Register(nameof(IsRangeSource), typeof(bool), typeof(RazorItemsHost),
            new PropertyMetadata(false, OnRangeChanged));

    /// <summary>Identifies the <see cref="RangeStep"/> dependency property.</summary>
    public static readonly DependencyProperty RangeStepProperty =
        DependencyProperty.Register(nameof(RangeStep), typeof(int), typeof(RazorItemsHost),
            new PropertyMetadata(1, OnRangeChanged));

    /// <summary>Gets or sets the axis items are laid out along. Defaults to vertical.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets whether items stack or wrap.</summary>
    public RazorVirtualizeLayout Layout
    {
        get => (RazorVirtualizeLayout)GetValue(LayoutProperty)!;
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Gets or sets how this host obtains a viewport.</summary>
    public RazorVirtualizeScrollHost ScrollHost
    {
        get => (RazorVirtualizeScrollHost)GetValue(ScrollHostProperty)!;
        set => SetValue(ScrollHostProperty, value);
    }

    /// <summary>Gets or sets what happens when the scrolling axis is measured unbounded.</summary>
    public RazorVirtualizeUnbounded UnboundedBehavior
    {
        get => (RazorVirtualizeUnbounded)GetValue(UnboundedBehaviorProperty)!;
        set => SetValue(UnboundedBehaviorProperty, value);
    }

    /// <summary>Gets or sets the viewport length assumed under <see cref="RazorVirtualizeUnbounded.FixedViewport"/>.</summary>
    public double FallbackViewportLength
    {
        get => (double)GetValue(FallbackViewportLengthProperty)!;
        set => SetValue(FallbackViewportLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the largest item count this host will realize all at once when it degrades to
    /// non-virtualized layout. Above this it reports an error and renders nothing rather than
    /// risking an out-of-memory stall.
    /// </summary>
    public int MaxEagerItemCount
    {
        get => (int)GetValue(MaxEagerItemCountProperty)!;
        set => SetValue(MaxEagerItemCountProperty, value);
    }

    /// <summary>Gets or sets the first value of the numeric-form sequence.</summary>
    public int RangeStart
    {
        get => (int)GetValue(RangeStartProperty)!;
        set => SetValue(RangeStartProperty, value);
    }

    /// <summary>Gets or sets the bound the numeric-form sequence stops at.</summary>
    public int RangeEnd
    {
        get => (int)GetValue(RangeEndProperty)!;
        set => SetValue(RangeEndProperty, value);
    }

    /// <summary>
    /// Gets or sets whether <see cref="RangeEnd"/> is itself part of the sequence, distinguishing
    /// a <c>&lt;=</c> loop condition from a <c>&lt;</c> one.
    /// </summary>
    public bool RangeEndInclusive
    {
        get => (bool)GetValue(RangeEndInclusiveProperty)!;
        set => SetValue(RangeEndInclusiveProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this host draws its items from the numeric form rather than from
    /// <see cref="ItemsControl.ItemsSource"/>.
    /// </summary>
    public bool IsRangeSource
    {
        get => (bool)GetValue(IsRangeSourceProperty)!;
        set => SetValue(IsRangeSourceProperty, value);
    }

    /// <summary>Gets the number of values the numeric form currently yields.</summary>
    public int RangeCount => _range?.Count ?? 0;

    /// <summary>Gets or sets the increment of the numeric-form sequence.</summary>
    public int RangeStep
    {
        get => (int)GetValue(RangeStepProperty)!;
        set => SetValue(RangeStepProperty, value);
    }

    #endregion

    /// <summary>Initializes a new instance of the <see cref="RazorItemsHost"/> class.</summary>
    public RazorItemsHost()
    {
        SyncItemsPanel();
    }

    /// <summary>
    /// This host always supplies its own template, so it never wants the fallback items host.
    /// </summary>
    /// <remarks>
    /// The template is only chosen on the first measure, because the scroll-host mode depends on
    /// whether an enclosing viewer claimed this host. Until then <c>HasTemplate</c> is false, and
    /// the fallback would spin up a shadow panel on the first <c>RefreshItems</c> — one that is
    /// never measured, arranged or rendered once the real template arrives, but that still resets
    /// a virtualizing panel and allocates a per-item height index. For a million-item range that
    /// is four megabytes bought and thrown away before layout has even started.
    /// </remarks>
    private protected override bool UsesFallbackItemsHost => false;

    private static void OnLayoutShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (RazorItemsHost)d;
        host.SyncItemsPanel();
        host.InvalidateMeasure();
    }

    private static void OnScrollHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RazorItemsHost)d).InvalidateMeasure();

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RazorItemsHost)d).SyncRange();

    private void SyncItemsPanel()
    {
        var wanted = (Layout, Orientation) switch
        {
            (RazorVirtualizeLayout.Wrap, Orientation.Vertical) => s_wrapVertical,
            (RazorVirtualizeLayout.Wrap, _) => s_wrapHorizontal,
            (_, Orientation.Vertical) => s_stackVertical,
            _ => s_stackHorizontal,
        };

        if (!ReferenceEquals(ItemsPanel, wanted))
        {
            ItemsPanel = wanted;
        }
    }

    /// <summary>
    /// Rebuilds the virtual sequence backing the numeric form. Publishing the three range values
    /// one at a time would let the panel observe inconsistent intermediate ranges.
    /// </summary>
    private void SyncRange()
    {
        if (!IsRangeSource)
        {
            return;
        }

        if (ItemsSource is not null && !ReferenceEquals(ItemsSource, _range))
        {
            throw new InvalidOperationException(
                "@virtualize cannot use the numeric form and ItemsSource at the same time.");
        }

        var start = RangeStart;
        var step = RangeStep == 0 ? 1 : RangeStep;
        var count = CountFor(start, RangeEnd, step, RangeEndInclusive);

        if (_range is null)
        {
            _range = new RazorIntRange(start, count, step);
        }
        else
        {
            _range.Update(start, count, step);
        }

        if (!ReferenceEquals(ItemsSource, _range))
        {
            ItemsSource = _range;
        }
    }

    /// <summary>
    /// Counts the iterations of <c>for (i = start; i &lt; end; i += step)</c>.
    /// </summary>
    /// <remarks>
    /// Done here rather than folded into the generated markup because the ceiling division has to
    /// be right for a descending loop too, and that is far clearer as code than as an expression
    /// assembled by string concatenation. Widened to <see cref="long"/> so a range spanning the
    /// whole <see cref="int"/> domain cannot overflow while being counted.
    /// </remarks>
    private static int CountFor(long start, long end, long step, bool inclusive)
    {
        var span = end - start + (inclusive ? Math.Sign(step) : 0);
        if (step > 0 ? span <= 0 : span >= 0)
        {
            return 0;
        }

        var count = (span + step - Math.Sign(step)) / step;
        return (int)Math.Min(count, int.MaxValue);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _innerScrollViewer = TemplateRootInternal as ScrollViewer;
        _presenter = TemplateRootInternal as ItemsPresenter
                     ?? _innerScrollViewer?.Content as ItemsPresenter;

        // When an outer viewer drives us, the presenter and through it the panel must see that
        // owner: the panel calls ScrollOwner.InvalidateScrollInfo() to publish extent changes, and
        // that has to reach the real outer viewer.
        if (_presenter is not null && _appliedMode != RazorVirtualizeScrollHost.Self)
        {
            _presenter.ScrollOwner = _scrollOwner;
            _presenter.CanHorizontallyScroll = _canHorizontallyScroll;
            _presenter.CanVerticallyScroll = _canVerticallyScroll;
        }
    }

    /// <inheritdoc />
    internal override void OnTemplateContentClearing()
    {
        // Drop the cached parts before the base retires the old items panel, so a queued measure
        // cannot make a discarded presenter realize containers that now belong elsewhere.
        _presenter = null;
        _innerScrollViewer = null;
        base.OnTemplateContentClearing();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureTemplateForMode();
        DiagnoseUnboundedConstraint(availableSize);
        return base.MeasureOverride(availableSize);
    }

    private RazorVirtualizeScrollHost ResolveMode() => ScrollHost switch
    {
        RazorVirtualizeScrollHost.Outer => RazorVirtualizeScrollHost.Outer,
        RazorVirtualizeScrollHost.Self => RazorVirtualizeScrollHost.Self,
        RazorVirtualizeScrollHost.None => RazorVirtualizeScrollHost.None,

        // An enclosing ScrollViewer sets ScrollOwner while the tree is being built, so this is
        // already settled by the time the first measure runs.
        _ => _scrollOwner is not null ? RazorVirtualizeScrollHost.Outer : RazorVirtualizeScrollHost.Self,
    };

    private void EnsureTemplateForMode()
    {
        var mode = ResolveMode();
        var orientation = Orientation;
        if (_appliedMode == mode && _appliedOrientation == orientation && Template is not null)
        {
            return;
        }

        _appliedMode = mode;
        _appliedOrientation = orientation;

        // Assigning Template runs ClearTemplateContent synchronously, which routes through
        // ItemsControl.OnTemplateContentClearing to ItemsPresenter.InvalidatePanel and then
        // ItemsControl.RetireItemsHostPanel. That is the framework's single retirement choke
        // point, so a mode flip cannot leave a zombie panel behind.
        Template = mode == RazorVirtualizeScrollHost.Self
            ? (orientation == Orientation.Vertical ? s_selfScrollVerticalTemplate : s_selfScrollHorizontalTemplate)
            : s_passthroughTemplate;
    }

    /// <summary>
    /// Detects the one condition that makes virtualization impossible and, worse, silently
    /// produces an empty list.
    /// </summary>
    /// <remarks>
    /// With an infinite constraint on the scroll axis, <c>VirtualizingStackPanel.CoerceViewport</c>
    /// falls back to the previous viewport, which is zero on the first pass, and
    /// <c>CoerceDesiredSize</c> then reports <c>Math.Min(extent, 0)</c>. The panel collapses to
    /// zero along the scroll axis and never recovers, so the list renders blank rather than merely
    /// rendering un-virtualized. That makes this a correctness guard, not a performance hint.
    /// </remarks>
    private void DiagnoseUnboundedConstraint(Size availableSize)
    {
        var axis = Orientation == Orientation.Vertical ? availableSize.Height : availableSize.Width;
        if (!double.IsInfinity(axis))
        {
            if (_degraded)
            {
                _degraded = false;
                CoerceValue(VirtualizingPanel.IsVirtualizingProperty);
                RefreshItems();
            }
            return;
        }

        var axisName = Orientation == Orientation.Vertical ? "vertical" : "horizontal";
        Report(
            $"@virtualize: host was measured with an infinite {axisName} constraint, so it has no " +
            "viewport and cannot virtualize. This usually means it sits inside a StackPanel, an " +
            "Auto-sized grid row, or another item template. Give it an explicit Height/MaxHeight, " +
            "or make it the direct content of a ScrollViewer with CanContentScroll enabled.");

        switch (UnboundedBehavior)
        {
            case RazorVirtualizeUnbounded.Degrade:
                var count = Items.Count;
                if (count <= MaxEagerItemCount)
                {
                    EnterDegradedMode();
                }
                else
                {
                    Report(
                        $"@virtualize: refusing to realize {count} items eagerly (MaxEagerItemCount is " +
                        $"{MaxEagerItemCount}). The list stays empty; set an explicit Height/MaxHeight.",
                        TraceEventType.Error);
                }

                break;

            case RazorVirtualizeUnbounded.FixedViewport:
                SetCurrentValue(
                    Orientation == Orientation.Vertical ? MaxHeightProperty : MaxWidthProperty,
                    FallbackViewportLength);
                break;

            case RazorVirtualizeUnbounded.Strict:
                break;
        }
    }

    /// <summary>
    /// Turns virtualization off so the items render the way a plain <c>@foreach</c> would.
    /// Un-virtualized but correct is never a regression; a blank list is.
    /// </summary>
    private void EnterDegradedMode()
    {
        if (_degraded)
        {
            return;
        }

        _degraded = true;
        CoerceValue(VirtualizingPanel.IsVirtualizingProperty);

        // The attached-property callback only invalidates measure. Containers are rebuilt by
        // RefreshItems alone, so switching pipelines needs an explicit push.
        RefreshItems();
    }

    private void Report(string message, TraceEventType level = TraceEventType.Warning)
    {
        if (_unboundedReported && level != TraceEventType.Error)
        {
            return;
        }

        _unboundedReported = true;
        Debug.WriteLine(message);
        System.Diagnostics.PresentationTraceSources.MarkupSource.TraceEvent(level, 0, message);
    }

    private static ControlTemplate BuildTemplate(Func<FrameworkElement> factory)
    {
        var template = new ControlTemplate(typeof(RazorItemsHost));
        template.SetVisualTree(factory);
        template.Seal();
        return template;
    }

    private static ItemsPanelTemplate BuildPanel(Func<FrameworkElement> factory)
    {
        var template = new ItemsPanelTemplate();
        template.SetVisualTree(factory);
        template.Seal();
        return template;
    }

    #region IScrollInfo

    // Only forward while an outer ScrollViewer is driving us. In Self mode the inner viewer owns
    // the presenter and nothing should reach it through this surface.
    private IScrollInfo? Target => _appliedMode == RazorVirtualizeScrollHost.Self ? null : _presenter;

    /// <inheritdoc />
    public bool CanHorizontallyScroll
    {
        get => Target?.CanHorizontallyScroll ?? _canHorizontallyScroll;
        set
        {
            _canHorizontallyScroll = value;
            if (Target is { } target)
            {
                target.CanHorizontallyScroll = value;
            }
        }
    }

    /// <inheritdoc />
    public bool CanVerticallyScroll
    {
        get => Target?.CanVerticallyScroll ?? _canVerticallyScroll;
        set
        {
            _canVerticallyScroll = value;
            if (Target is { } target)
            {
                target.CanVerticallyScroll = value;
            }
        }
    }

    /// <inheritdoc />
    public double ExtentWidth => Target?.ExtentWidth ?? 0d;

    /// <inheritdoc />
    public double ExtentHeight => Target?.ExtentHeight ?? 0d;

    /// <inheritdoc />
    public double ViewportWidth => Target?.ViewportWidth ?? RenderSize.Width;

    /// <inheritdoc />
    public double ViewportHeight => Target?.ViewportHeight ?? RenderSize.Height;

    /// <inheritdoc />
    public double HorizontalOffset => Target?.HorizontalOffset ?? 0d;

    /// <inheritdoc />
    public double VerticalOffset => Target?.VerticalOffset ?? 0d;

    /// <inheritdoc />
    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set
        {
            if (ReferenceEquals(_scrollOwner, value))
            {
                return;
            }

            _scrollOwner = value;
            if (Target is { } target)
            {
                target.ScrollOwner = value;
            }

            // Gaining or losing an owner can flip Auto between Outer and Self, but the flip has to
            // wait for the next measure: ScrollViewer.OnContentChanged assigns null and then the
            // real owner, and swapping templates in between would thrash.
            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    public void LineUp() => Target?.LineUp();

    /// <inheritdoc />
    public void LineDown() => Target?.LineDown();

    /// <inheritdoc />
    public void LineLeft() => Target?.LineLeft();

    /// <inheritdoc />
    public void LineRight() => Target?.LineRight();

    /// <inheritdoc />
    public void PageUp() => Target?.PageUp();

    /// <inheritdoc />
    public void PageDown() => Target?.PageDown();

    /// <inheritdoc />
    public void PageLeft() => Target?.PageLeft();

    /// <inheritdoc />
    public void PageRight() => Target?.PageRight();

    /// <inheritdoc />
    public void MouseWheelUp() => Target?.MouseWheelUp();

    /// <inheritdoc />
    public void MouseWheelDown() => Target?.MouseWheelDown();

    /// <inheritdoc />
    public void MouseWheelLeft() => Target?.MouseWheelLeft();

    /// <inheritdoc />
    public void MouseWheelRight() => Target?.MouseWheelRight();

    /// <inheritdoc />
    public void SetHorizontalOffset(double offset) => Target?.SetHorizontalOffset(offset);

    /// <inheritdoc />
    public void SetVerticalOffset(double offset) => Target?.SetVerticalOffset(offset);

    /// <inheritdoc />
    public Rect MakeVisible(Visual visual, Rect rectangle)
        => Target?.MakeVisible(visual, rectangle) ?? rectangle;

    #endregion
}
