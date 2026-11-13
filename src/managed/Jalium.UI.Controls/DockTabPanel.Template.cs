using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_Layout", Type = typeof(DockTabLayoutPanel))]
[TemplatePart(Name = "PART_ItemsPresenter", Type = typeof(ItemsPresenter))]
[TemplatePart(Name = "PART_SelectedContentHost", Type = typeof(ContentControl))]
[TemplatePart(Name = "PART_TabStripScrollBar", Type = typeof(ScrollBar))]
[TemplatePart(Name = "PART_Background", Type = typeof(DockTabChrome))]
[TemplatePart(Name = "PART_ChromeOverlay", Type = typeof(DockTabChrome))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Top", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Bottom", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Left", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Right", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Overflowing", GroupName = "OverflowStates")]
[TemplateVisualState(Name = "NotOverflowing", GroupName = "OverflowStates")]
[TemplateVisualState(Name = "Empty", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
public partial class DockTabPanel
{
    private DockTabLayoutPanel? _dockLayoutPanel;
    private ItemsPresenter? _headerPresenter;
    private DockTabChrome? _backgroundChrome, _overlayChrome;

    private Size DockAreaSize => _dockLayoutPanel?.RenderSize ?? RenderSize;

    private Rect ToOwnerRect(Rect rect)
    {
        var offset = _dockLayoutPanel?.TransformToAncestor(this) ?? default;
        return new Rect(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
    }

    public override void OnApplyTemplate()
    {
        if (_tabStripScrollBar is { } previousBar) previousBar.Scroll -= OnTabStripScroll;
        base.OnApplyTemplate();
        _dockLayoutPanel = GetTemplateChild("PART_Layout") as DockTabLayoutPanel;
        _headerPresenter = GetTemplateChild("PART_ItemsPresenter") as ItemsPresenter;
        _selectedContentHost = GetTemplateChild("PART_SelectedContentHost") as ContentControl;
        _tabStripScrollBar = GetTemplateChild("PART_TabStripScrollBar") as ScrollBar;
        _backgroundChrome = GetTemplateChild("PART_Background") as DockTabChrome;
        _overlayChrome = GetTemplateChild("PART_ChromeOverlay") as DockTabChrome;
        if (_tabStripScrollBar is { } bar) bar.Scroll += OnTabStripScroll;
        if (_selectedContentHost is TransitioningContentControl host)
            host.SetCurrentValue(TransitioningContentControl.TransitionModeProperty, SelectedContentTransitionMode);
        SetHostedContent(SelectedContent);
        EnsureItemsHostOrientation();
        UpdateDockVisualStates();
        InvalidateDockVisual();
    }

    internal override void OnTemplateContentClearing()
    {
        _backgroundChrome?.ReleaseBrushes();
        _overlayChrome?.ReleaseBrushes();
        if (_tabStripScrollBar is { } bar) bar.Scroll -= OnTabStripScroll;
        SetHostedContent(null);
        _dockLayoutPanel = null;
        _headerPresenter = null;
        _selectedContentHost = null;
        _tabStripScrollBar = null;
        _backgroundChrome = _overlayChrome = null;
        _tabStripRect = _tabHeaderViewportRect = _tabScrollBarRect = _contentRect = default;
        base.OnTemplateContentClearing();
    }

    private void SetHostedContent(object? content)
        => _selectedContentHost?.SetCurrentValue(ContentControl.ContentProperty, content);

    internal static void DetachContentFromDockPanel(UIElement element, DockTabPanel? destination = null)
    {
        // A content element now sits below a template host, so the panel can be
        // several ancestors away. Release through the owning panel's contract.
        for (var ancestor = ((Visual)element).VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
            if (ancestor is DockTabPanel panel)
            {
                if (!ReferenceEquals(panel, destination)) panel.ReleaseContentElement(element);
                return;
            }
    }

    private void InvalidateDockMeasure()
    {
        _dockLayoutPanel?.InvalidateMeasure();
        InvalidateMeasure();
    }

    private void InvalidateDockArrange()
    {
        _dockLayoutPanel?.InvalidateArrange();
        InvalidateArrange();
    }

    private double GetTabScrollBarThickness()
    {
        var requested = IsVerticalTabStrip ? _tabStripScrollBar?.Width : _tabStripScrollBar?.Height;
        return requested is double value && double.IsFinite(value) ? Math.Max(0, value) : TabStripScrollBarThickness;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateDefaultStyle();
        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == SelectedContentProperty) SetHostedContent(args.NewValue);
        if (args.Property == IsEnabledProperty || args.Property == IsPanelFocusedProperty ||
            args.Property == SelectedContentProperty || args.Property == TabStripPlacementProperty)
            UpdateDockVisualStates();
        InvalidateDockVisual();
    }

    private void UpdateDockVisualStates()
    {
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsPanelFocused ? "Focused" : "Unfocused", false);
        VisualStateManager.GoToState(this, TabStripPlacement.ToString(), false);
        VisualStateManager.GoToState(this, _isTabStripScrollable ? "Overflowing" : "NotOverflowing", false);
        VisualStateManager.GoToState(this, SelectedContent is null ? "Empty" : "Selected", false);
    }

    private void InvalidateDockVisual()
    {
        InvalidateVisual();
        _backgroundChrome?.RefreshBrushes(GetDockBrushes());
        _overlayChrome?.RefreshBrushes(GetDockBrushes());
        _backgroundChrome?.InvalidateVisual();
        _overlayChrome?.InvalidateVisual();
    }

    private IEnumerable<Brush> GetDockBrushes()
    {
        yield return ResolvePanelBackgroundBrush();
        yield return ResolveTabStripBackgroundBrush();
        yield return ResolveTabStripBorderBrush();
        yield return ResolveAccentBrush();
        if (GetSelectedDockItem() is { } item) yield return item.ResolveSelectedBackgroundBrush();
    }

    internal bool OwnsDockLayout(DockTabLayoutPanel panel) => ReferenceEquals(panel, _dockLayoutPanel);
    internal bool OwnsDockChrome(DockTabChrome chrome)
        => ReferenceEquals(chrome, _backgroundChrome) || ReferenceEquals(chrome, _overlayChrome);

    internal Geometry? GetDockHeaderClip(Visual child)
        => ReferenceEquals(child, _headerPresenter) ? new RectangleGeometry(_tabHeaderViewportRect) : null;
}
