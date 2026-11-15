using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>One segment of a proportional diagnostic readout.</summary>
public sealed record DevToolsBarSegment(double Value, Brush Color);

/// <summary>An items control with proportional layout and theme-supplied segment visuals.</summary>
public class DevToolsStackedBar : ItemsControl
{
    static DevToolsStackedBar() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsStackedBar), new PropertyMetadata(typeof(DevToolsStackedBar)));
    // Adjacent readouts carry the values. Segment models and decorative
    // rectangles must not become extra navigation stops or ToString labels.
    protected override global::Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;
    protected override FrameworkElement GetContainerForItem(object item) => new DevToolsBarSegmentPresenter();
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is DevToolsBarSegmentPresenter presenter && item is DevToolsBarSegment segment)
        {
            presenter.SetCurrentValue(BackgroundProperty, segment.Color);
            DevToolsProportionalPanel.SetWeight(presenter, segment.Value);
        }
    }
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        base.ClearContainerForItem(element, item);
        if (item is DevToolsBarSegment && !ReferenceEquals(element, item))
        {
            element.ClearValue(BackgroundProperty);
            element.ClearValue(DevToolsProportionalPanel.WeightProperty);
        }
    }
}

/// <summary>The optional visual container for one diagnostic segment.</summary>
public class DevToolsBarSegmentPresenter : Control
{
    static DevToolsBarSegmentPresenter() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsBarSegmentPresenter), new PropertyMetadata(typeof(DevToolsBarSegmentPresenter)));
}

/// <summary>Arranges children proportionally without creating or decorating visual elements.</summary>
public class DevToolsProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(DevToolsProportionalPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure |
            FrameworkPropertyMetadataOptions.AffectsParentArrange, null,
            static (_, value) => value is double weight && double.IsFinite(weight) ? Math.Max(0, weight) : 0.0));
    public static void SetWeight(DependencyObject target, double value) => target.SetValue(WeightProperty, value);
    public static double GetWeight(DependencyObject target) => (double)target.GetValue(WeightProperty)!;

    private static double LayoutWeight(UIElement child) => child.Visibility == Visibility.Collapsed ? 0 : GetWeight(child);
    private (double Largest, double Total) WeightScale()
    {
        double largest = 0, total = 0;
        foreach (UIElement child in Children) largest = Math.Max(largest, LayoutWeight(child));
        if (largest > 0)
            foreach (UIElement child in Children) total += LayoutWeight(child) / largest;
        return (largest, total);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (largest, total) = WeightScale();
        double width = 0, height = 0;
        foreach (UIElement child in Children)
        {
            double fraction = total == 0 ? 0 : LayoutWeight(child) / largest / total;
            child.Measure(new Size(fraction == 0 ? 0 : double.IsFinite(availableSize.Width) ? availableSize.Width * fraction : double.PositiveInfinity, availableSize.Height));
            if (fraction > 0) { width += child.DesiredSize.Width; height = Math.Max(height, child.DesiredSize.Height); }
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (largest, total) = WeightScale();
        double x = 0;
        foreach (UIElement child in Children)
        {
            double width = total == 0 ? 0 : finalSize.Width * (LayoutWeight(child) / largest / total);
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }
}
