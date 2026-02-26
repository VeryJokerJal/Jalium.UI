namespace Jalium.UI.Media;

/// <summary>
/// Provides options for controlling rendering behavior of visual objects.
/// </summary>
public static class RenderOptions
{
    /// <summary>
    /// Identifies the EdgeMode attached property.
    /// </summary>
    public static readonly DependencyProperty EdgeModeProperty =
        DependencyProperty.RegisterAttached("EdgeMode", typeof(EdgeMode), typeof(RenderOptions),
            new PropertyMetadata(EdgeMode.Unspecified));

    /// <summary>
    /// Identifies the BitmapScalingMode attached property.
    /// </summary>
    public static readonly DependencyProperty BitmapScalingModeProperty =
        DependencyProperty.RegisterAttached("BitmapScalingMode", typeof(BitmapScalingMode), typeof(RenderOptions),
            new PropertyMetadata(BitmapScalingMode.Unspecified));

    /// <summary>
    /// Identifies the CachingHint attached property.
    /// </summary>
    public static readonly DependencyProperty CachingHintProperty =
        DependencyProperty.RegisterAttached("CachingHint", typeof(CachingHint), typeof(RenderOptions),
            new PropertyMetadata(CachingHint.Unspecified));

    /// <summary>
    /// Identifies the CacheInvalidationThresholdMinimum attached property.
    /// </summary>
    public static readonly DependencyProperty CacheInvalidationThresholdMinimumProperty =
        DependencyProperty.RegisterAttached("CacheInvalidationThresholdMinimum", typeof(double), typeof(RenderOptions),
            new PropertyMetadata(0.707));

    /// <summary>
    /// Identifies the CacheInvalidationThresholdMaximum attached property.
    /// </summary>
    public static readonly DependencyProperty CacheInvalidationThresholdMaximumProperty =
        DependencyProperty.RegisterAttached("CacheInvalidationThresholdMaximum", typeof(double), typeof(RenderOptions),
            new PropertyMetadata(1.414));

    /// <summary>Gets or sets the process-wide rendering mode.</summary>
    public static RenderMode ProcessRenderMode { get; set; } = RenderMode.Default;

    /// <summary>Sets the edge mode for the specified element.</summary>
    public static void SetEdgeMode(DependencyObject target, EdgeMode edgeMode) =>
        target.SetValue(EdgeModeProperty, edgeMode);

    /// <summary>Gets the edge mode for the specified element.</summary>
    public static EdgeMode GetEdgeMode(DependencyObject target) =>
        target.GetValue(EdgeModeProperty) is EdgeMode v ? v : EdgeMode.Unspecified;

    /// <summary>Sets the bitmap scaling mode for the specified element.</summary>
    public static void SetBitmapScalingMode(DependencyObject target, BitmapScalingMode value) =>
        target.SetValue(BitmapScalingModeProperty, value);

    /// <summary>Gets the bitmap scaling mode for the specified element.</summary>
    public static BitmapScalingMode GetBitmapScalingMode(DependencyObject target) =>
        target.GetValue(BitmapScalingModeProperty) is BitmapScalingMode v ? v : BitmapScalingMode.Unspecified;

    /// <summary>Sets the caching hint for the specified element.</summary>
    public static void SetCachingHint(DependencyObject target, CachingHint value) =>
        target.SetValue(CachingHintProperty, value);

    /// <summary>Gets the caching hint for the specified element.</summary>
    public static CachingHint GetCachingHint(DependencyObject target) =>
        target.GetValue(CachingHintProperty) is CachingHint v ? v : CachingHint.Unspecified;

    /// <summary>Sets the cache invalidation threshold minimum.</summary>
    public static void SetCacheInvalidationThresholdMinimum(DependencyObject target, double value) =>
        target.SetValue(CacheInvalidationThresholdMinimumProperty, value);

    /// <summary>Gets the cache invalidation threshold minimum.</summary>
    public static double GetCacheInvalidationThresholdMinimum(DependencyObject target) =>
        target.GetValue(CacheInvalidationThresholdMinimumProperty) is double v ? v : 0.707;

    /// <summary>Sets the cache invalidation threshold maximum.</summary>
    public static void SetCacheInvalidationThresholdMaximum(DependencyObject target, double value) =>
        target.SetValue(CacheInvalidationThresholdMaximumProperty, value);

    /// <summary>Gets the cache invalidation threshold maximum.</summary>
    public static double GetCacheInvalidationThresholdMaximum(DependencyObject target) =>
        target.GetValue(CacheInvalidationThresholdMaximumProperty) is double v ? v : 1.414;
}

/// <summary>Specifies the algorithm used to scale bitmap images.</summary>
public enum BitmapScalingMode
{
    /// <summary>Use default scaling.</summary>
    Unspecified,
    /// <summary>Use low quality scaling.</summary>
    LowQuality,
    /// <summary>Use high quality scaling.</summary>
    HighQuality,
    /// <summary>Use nearest neighbor scaling.</summary>
    NearestNeighbor,
    /// <summary>Use linear scaling.</summary>
    Linear,
    /// <summary>Use Fant scaling.</summary>
    Fant
}

/// <summary>Specifies the rendering mode for the process.</summary>
public enum RenderMode
{
    /// <summary>Default rendering.</summary>
    Default,
    /// <summary>Force software rendering.</summary>
    SoftwareOnly
}

/// <summary>Specifies caching behavior for TileBrush objects.</summary>
public enum CachingHint
{
    /// <summary>No caching hint.</summary>
    Unspecified,
    /// <summary>Cache the tiled brush off-screen.</summary>
    Cache
}
