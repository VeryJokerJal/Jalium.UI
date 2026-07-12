namespace Jalium.UI.Media;

using Jalium.UI.Interop;

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

    /// <summary>Identifies the ClearTypeHint attached property.</summary>
    public static readonly DependencyProperty ClearTypeHintProperty =
        DependencyProperty.RegisterAttached(
            "ClearTypeHint",
            typeof(ClearTypeHint),
            typeof(RenderOptions),
            new PropertyMetadata(ClearTypeHint.Auto));

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

    /// <summary>
    /// Gets or sets the system-wide default edge mode used when an element's
    /// resolved <see cref="EdgeModeProperty"/> is <see cref="EdgeMode.Unspecified"/>.
    /// Defaults to <see cref="EdgeMode.Antialiased"/>.
    /// </summary>
    public static EdgeMode DefaultEdgeMode { get; set; } = EdgeMode.Antialiased;

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

    /// <summary>Sets the ClearType hint for the specified element.</summary>
    public static void SetClearTypeHint(DependencyObject target, ClearTypeHint clearTypeHint) =>
        target.SetValue(ClearTypeHintProperty, clearTypeHint);

    /// <summary>Gets the ClearType hint for the specified element.</summary>
    public static ClearTypeHint GetClearTypeHint(DependencyObject target) =>
        target.GetValue(ClearTypeHintProperty) is ClearTypeHint value ? value : ClearTypeHint.Auto;

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
    Unspecified = 0,
    /// <summary>Use low quality scaling.</summary>
    LowQuality = 1,
    /// <summary>Use high quality scaling.</summary>
    HighQuality = 2,
    /// <summary>Use nearest neighbor scaling.</summary>
    NearestNeighbor = 3,
    /// <summary>Use linear scaling.</summary>
    Linear = 1,
    /// <summary>Use Fant scaling.</summary>
    Fant = 2
}

/// <summary>Specifies caching behavior for TileBrush objects.</summary>
public enum CachingHint
{
    /// <summary>No caching hint.</summary>
    Unspecified,
    /// <summary>Cache the tiled brush off-screen.</summary>
    Cache
}
