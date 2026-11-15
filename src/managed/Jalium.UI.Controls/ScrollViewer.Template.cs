using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_LayoutPanel", Type = typeof(ScrollViewerLayoutPanel))]
[TemplatePart(Name = "PART_ScrollContentPresenter", Type = typeof(ScrollContentPresenter))]
[TemplatePart(Name = "PART_VerticalScrollBar", Type = typeof(ScrollBar))]
[TemplatePart(Name = "PART_HorizontalScrollBar", Type = typeof(ScrollBar))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "NoScrollBars", GroupName = "ScrollBarStates")]
[TemplateVisualState(Name = "HorizontalScrollBar", GroupName = "ScrollBarStates")]
[TemplateVisualState(Name = "VerticalScrollBar", GroupName = "ScrollBarStates")]
[TemplateVisualState(Name = "BothScrollBars", GroupName = "ScrollBarStates")]
[TemplateVisualState(Name = "ClassicScrollBars", GroupName = "ScrollBarModeStates")]
[TemplateVisualState(Name = "OverlayScrollBars", GroupName = "ScrollBarModeStates")]
public partial class ScrollViewer
{
    private ScrollContentPresenter? _scrollContentPresenter;
    private ScrollViewerLayoutPanel? _scrollLayoutPanel;
    private bool _verticalBarWasHostOwned;
    private bool _horizontalBarWasHostOwned;

    private UIElement? ScrollLayoutContent => _scrollContentPresenter ?? base.ContentElement;
    private UIElement? ScrollSemanticContent => _scrollContentPresenter?.PresentedContent ?? base.ContentElement;

    private static readonly DependencyPropertyKey ChromePaddingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ChromePadding), typeof(Thickness), typeof(ScrollViewer), new PropertyMetadata(default(Thickness)));
    /// <summary>Identifies the read-only background content inset, including any classic scroll bar gutters.</summary>
    public static readonly DependencyProperty ChromePaddingProperty = ChromePaddingPropertyKey.DependencyProperty;
    /// <summary>Gets the background content inset used by the default template's frame.</summary>
    public Thickness ChromePadding => (Thickness)GetValue(ChromePaddingProperty)!;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _scrollLayoutPanel = GetTemplateChild("PART_LayoutPanel") as ScrollViewerLayoutPanel;
        _scrollContentPresenter = GetTemplateChild("PART_ScrollContentPresenter") as ScrollContentPresenter;
        if (_scrollContentPresenter is not null) _scrollContentPresenter.ViewportOwner = this;
        _verticalScrollBar = GetTemplateChild("PART_VerticalScrollBar") as ScrollBar;
        _horizontalScrollBar = GetTemplateChild("PART_HorizontalScrollBar") as ScrollBar;
        AttachScrollBar(_verticalScrollBar, out _verticalBarWasHostOwned);
        AttachScrollBar(_horizontalScrollBar, out _horizontalBarWasHostOwned);
        _cssThinScrollBarMinimumsApplied = false;
        SynchronizePresentedScrollInfo();
        UpdateCssScrollBarMinimums();
        RefreshCssScrollBarColors();
        UpdateScrollBarMetrics();
        UpdateScrollViewerPresentation();
    }

    internal override void OnTemplateContentClearing()
    {
        CompleteLifecycleCleanup();
        DetachScrollBar(_verticalScrollBar, _verticalBarWasHostOwned);
        DetachScrollBar(_horizontalScrollBar, _horizontalBarWasHostOwned);
        if (_scrollContentPresenter is not null) _scrollContentPresenter.ViewportOwner = null;
        ScrollInfo = null;
        _verticalScrollBar = null;
        _horizontalScrollBar = null;
        _scrollContentPresenter = null;
        _scrollLayoutPanel = null;
        _cssThinScrollBarMinimumsApplied = false;
        base.OnTemplateContentClearing();
    }

    private void AttachScrollBar(ScrollBar? bar, out bool wasHostOwned)
    {
        wasHostOwned = bar?.IsHostOwnedLayout ?? false;
        if (bar is null) return;
        // The allocation panel gives its bars fixed owner-derived slots. Their
        // hover/visibility animation need not invalidate the caller's layout.
        if (_scrollLayoutPanel is not null && ReferenceEquals(bar.VisualParent, _scrollLayoutPanel))
            bar.IsHostOwnedLayout = true;
        bar.Scroll += OnScrollBarScroll;
        bar.IsKeyboardFocusWithinChanged += OnScrollBarKeyboardFocusWithinChanged;
        bar.AddHandler(MouseEnterEvent, new Input.MouseEventHandler(OnScrollBarMouseEnter));
        bar.AddHandler(MouseLeaveEvent, new Input.MouseEventHandler(OnScrollBarMouseLeave));
    }

    private void DetachScrollBar(ScrollBar? bar, bool wasHostOwned)
    {
        if (bar is null) return;
        bar.CompleteInteractionForDetach();
        bar.Scroll -= OnScrollBarScroll;
        bar.IsKeyboardFocusWithinChanged -= OnScrollBarKeyboardFocusWithinChanged;
        bar.RemoveHandler(MouseEnterEvent, new Input.MouseEventHandler(OnScrollBarMouseEnter));
        bar.RemoveHandler(MouseLeaveEvent, new Input.MouseEventHandler(OnScrollBarMouseLeave));
        bar.SetOwnerCssColors(null);
        if (_cssThinScrollBarMinimumsApplied)
            bar.ClearLayerValue(bar.Orientation == Orientation.Vertical ? MinWidthProperty : MinHeightProperty,
                DependencyObject.LayerValueSource.ParentTemplate);
        bar.IsHostOwnedLayout = wasHostOwned;
    }

    private void InvalidateScrollMeasure()
    {
        _scrollLayoutPanel?.InvalidateMeasure();
        InvalidateMeasure();
    }

    private void InvalidateScrollArrange()
    {
        _scrollLayoutPanel?.InvalidateArrange();
        InvalidateArrange();
    }

    private void SynchronizePresentedScrollInfo()
    {
        if (ScrollSemanticContent is { } content)
            ScrollInfo = CanContentScroll ? content as IScrollInfo : null;
    }

    private void UpdateScrollViewerPresentation()
    {
        var size = IsArrangeValid ? RenderSize : PreviousAvailableSize;
        var gutter = GetClassicGutterInsets(size, _verticalScrollBarShown, _horizontalScrollBarShown);
        SetValue(ChromePaddingPropertyKey, new Thickness(Padding.Left + gutter.Left, Padding.Top,
            Padding.Right + gutter.Right, Padding.Bottom + gutter.Bottom));
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsOverlayScrollBarEnabled ? "OverlayScrollBars" : "ClassicScrollBars", false);
        VisualStateManager.GoToState(this, _verticalScrollBarShown
            ? _horizontalScrollBarShown ? "BothScrollBars" : "VerticalScrollBar"
            : _horizontalScrollBarShown ? "HorizontalScrollBar" : "NoScrollBars", false);
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateScrollViewerPresentation();
    }
}
