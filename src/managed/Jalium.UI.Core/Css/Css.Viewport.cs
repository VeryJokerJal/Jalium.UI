namespace Jalium.UI.Styling;

/// <summary>Native host viewport sizes in DIPs, updated together as host chrome changes.</summary>
public sealed record CssViewportMetrics
{
    public Size Small { get; }
    public Size Large { get; }
    public Size Dynamic { get; }
    public bool IsVertical { get; }

    public CssViewportMetrics(Size small, Size large, Size dynamic, bool isVertical = false)
    {
        static bool Valid(Size value) => double.IsFinite(value.Width) && double.IsFinite(value.Height) && value.Width >= 0 && value.Height >= 0;
        if (!Valid(small) || !Valid(large) || !Valid(dynamic) ||
            small.Width > dynamic.Width || dynamic.Width > large.Width || small.Height > dynamic.Height || dynamic.Height > large.Height)
            throw new ArgumentException("Viewport sizes must be finite, nonnegative and ordered small <= dynamic <= large.");
        Small = small; Large = large; Dynamic = dynamic; IsVertical = isVertical;
    }

    internal static CssViewportMetrics Uniform(Size size) => new(size, size, size);
}

public static partial class Css
{
    /// <summary>Optional inherited viewport supplied by a native host or embedded XAML surface.</summary>
    public static readonly DependencyProperty ViewportMetricsProperty = DependencyProperty.RegisterAttached(
        "ViewportMetrics", typeof(CssViewportMetrics), typeof(Css), new PropertyMetadata(null,
            static (target, _) =>
            {
                if (CssEngine.IsActive && target is FrameworkElement or FrameworkContentElement)
                    CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(target));
            }, null, inherits: true));

    public static CssViewportMetrics? GetViewportMetrics(DependencyObject element) => (CssViewportMetrics?)element.GetValue(ViewportMetricsProperty);
    public static void SetViewportMetrics(DependencyObject element, CssViewportMetrics? metrics)
    {
        if (metrics is null) element.ClearValue(ViewportMetricsProperty);
        else element.SetValue(ViewportMetricsProperty, metrics);
    }
}
