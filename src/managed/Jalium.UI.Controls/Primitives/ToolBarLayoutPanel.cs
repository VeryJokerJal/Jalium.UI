using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>Allocates a toolbar's main items and optional overflow affordance.</summary>
public sealed class ToolBarLayoutPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(ToolBarLayoutPanel),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ButtonExtentProperty = DependencyProperty.Register(
        nameof(ButtonExtent), typeof(double), typeof(ToolBarLayoutPanel),
        new FrameworkPropertyMetadata(28d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(ToolBarLayoutRole), typeof(ToolBarLayoutPanel),
        new FrameworkPropertyMetadata(ToolBarLayoutRole.None, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty)!; set => SetValue(OrientationProperty, value); }
    public double ButtonExtent { get => (double)GetValue(ButtonExtentProperty)!; set => SetValue(ButtonExtentProperty, value); }
    public static ToolBarLayoutRole GetRole(DependencyObject target) => (ToolBarLayoutRole)target.GetValue(RoleProperty)!;
    public static void SetRole(DependencyObject target, ToolBarLayoutRole role) => target.SetValue(RoleProperty, role);
    private ToolBar? Owner
    {
        get
        {
            // Unnamed layout nodes need not receive a direct TemplatedParent.
            // The live visual chain also rejects a retired template's stale measure.
            for (Visual? parent = ParentVisual; parent is not null; parent = parent.VisualParent)
                if (parent is ToolBar owner) return owner;
            return null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        bool vertical = Orientation == Orientation.Vertical;
        UIElement? main = null, button = null;
        foreach (UIElement child in Children)
        {
            if (GetRole(child) == ToolBarLayoutRole.Items) main = child;
            if (GetRole(child) == ToolBarLayoutRole.OverflowButton) button = child;
        }
        // Attaching/initially measuring ItemsPresenter establishes the generator's host.
        if (Owner?.ItemsHostInternal is null || main is ItemsPresenter { ItemsPanel: null })
            main?.Measure(availableSize);
        Owner?.UpdateOverflowStateFromCurrentHost(availableSize);
        double extent = Owner?.HasOverflowItems == true && button is not null
            ? (double.IsFinite(ButtonExtent) ? Math.Max(0, ButtonExtent) : 28) : 0;
        double axis = vertical ? availableSize.Height : availableSize.Width;
        double cross = vertical ? availableSize.Width : availableSize.Height;
        double remaining = double.IsFinite(axis) ? Math.Max(0, axis - extent) : axis;
        var mainAvailable = vertical ? new Size(cross, remaining) : new Size(remaining, cross);
        if (extent > 0) Owner?.UpdateOverflowStateFromCurrentHost(mainAvailable);
        main?.Measure(mainAvailable);
        if (button is not null) button.Measure(vertical ? new Size(cross, extent) : new Size(extent, cross));
        foreach (UIElement child in Children)
            if (!ReferenceEquals(child, main) && !ReferenceEquals(child, button)) child.Measure(availableSize);
        var mainSize = main?.DesiredSize ?? default;
        var buttonSize = button?.DesiredSize ?? default;
        return vertical
            ? new Size(Math.Max(mainSize.Width, buttonSize.Width), mainSize.Height + extent)
            : new Size(mainSize.Width + extent, Math.Max(mainSize.Height, buttonSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool vertical = Orientation == Orientation.Vertical;
        double axis = vertical ? finalSize.Height : finalSize.Width;
        double cross = vertical ? finalSize.Width : finalSize.Height;
        double extent = Owner?.HasOverflowItems == true
            ? Math.Min(axis, double.IsFinite(ButtonExtent) ? Math.Max(0, ButtonExtent) : 28) : 0;
        foreach (UIElement child in Children)
        {
            var rect = GetRole(child) switch
            {
                ToolBarLayoutRole.Items => AxisRect(0, Math.Max(0, axis - extent)),
                ToolBarLayoutRole.OverflowButton => AxisRect(Math.Max(0, axis - extent), extent),
                _ => new Rect(finalSize)
            };
            child.Arrange(rect);
        }
        return finalSize;
        Rect AxisRect(double start, double length) => vertical
            ? new Rect(0, start, cross, length) : new Rect(start, 0, length, cross);
    }
}

/// <summary>Identifies a toolbar template child's layout role.</summary>
public enum ToolBarLayoutRole { None, Items, OverflowButton }
