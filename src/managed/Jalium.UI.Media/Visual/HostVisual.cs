namespace Jalium.UI.Media;

/// <summary>
/// Represents a Visual object that can be connected to from another thread.
/// Enables cross-thread visual composition scenarios.
/// </summary>
public sealed class HostVisual : ContainerVisual
{
    /// <summary>
    /// Initializes a new instance of the HostVisual class.
    /// </summary>
    public HostVisual()
    {
    }

    protected override GeometryHitTestResult? HitTestCore(GeometryHitTestParameters hitTestParameters) => null;
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
}

/// <summary>
/// Represents a target for rendering visuals from a HostVisual.
/// </summary>
public sealed class VisualTarget : CompositionTarget
{
    /// <summary>
    /// Initializes a new instance of the VisualTarget class.
    /// </summary>
    public VisualTarget(HostVisual hostVisual)
    {
        HostVisual = hostVisual;
    }

    /// <summary>
    /// Gets the host visual.
    /// </summary>
    public HostVisual HostVisual { get; }

    /// <summary>
    /// Gets the transform from root to host.
    /// </summary>
    public Matrix TransformToAncestor => Matrix.Identity;

    public override Matrix TransformFromDevice => Matrix.Identity;
    public override Matrix TransformToDevice => Matrix.Identity;

    public override void Dispose()
    {
        base.Dispose();
    }
}

/// <summary>
/// Represents a visual that caches its content as a bitmap for performance.
/// </summary>
public sealed class BitmapCacheBrush : Brush
{
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(Visual), typeof(BitmapCacheBrush), new PropertyMetadata(null));
    public static readonly DependencyProperty BitmapCacheProperty =
        DependencyProperty.Register(nameof(BitmapCache), typeof(BitmapCache), typeof(BitmapCacheBrush), new PropertyMetadata(null));
    public static readonly DependencyProperty AutoLayoutContentProperty =
        DependencyProperty.Register(nameof(AutoLayoutContent), typeof(bool), typeof(BitmapCacheBrush), new PropertyMetadata(true));

    /// <summary>
    /// Gets or sets the Visual whose content is cached.
    /// </summary>
    public Visual? Target
    {
        get => (Visual?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the bitmap cache settings.
    /// </summary>
    public BitmapCache? BitmapCache
    {
        get => (BitmapCache?)GetValue(BitmapCacheProperty);
        set => SetValue(BitmapCacheProperty, value);
    }

    public bool AutoLayoutContent
    {
        get => (bool)(GetValue(AutoLayoutContentProperty) ?? true);
        set => SetValue(AutoLayoutContentProperty, value);
    }

    public BitmapCacheBrush()
    {
    }

    public BitmapCacheBrush(Visual visual)
    {
        Target = visual ?? throw new ArgumentNullException(nameof(visual));
    }

    public new BitmapCacheBrush Clone() => (BitmapCacheBrush)base.Clone();
    public new BitmapCacheBrush CloneCurrentValue() => (BitmapCacheBrush)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new BitmapCacheBrush();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, BitmapCacheProperty) || ReferenceEquals(e.Property, TargetProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, e.Property);
        }

        if (ReferenceEquals(e.Property, BitmapCacheProperty)
            || ReferenceEquals(e.Property, TargetProperty)
            || ReferenceEquals(e.Property, AutoLayoutContentProperty))
        {
            WritePostscript();
        }
    }
}

/// <summary>
/// Provides caching behavior for rendered content.
/// </summary>
public sealed class BitmapCache : CacheMode
{
    public static readonly DependencyProperty EnableClearTypeProperty =
        DependencyProperty.Register(nameof(EnableClearType), typeof(bool), typeof(BitmapCache), new PropertyMetadata(false));
    public static readonly DependencyProperty RenderAtScaleProperty =
        DependencyProperty.Register(nameof(RenderAtScale), typeof(double), typeof(BitmapCache), new PropertyMetadata(1d));
    public static readonly DependencyProperty SnapsToDevicePixelsProperty =
        DependencyProperty.Register(nameof(SnapsToDevicePixels), typeof(bool), typeof(BitmapCache), new PropertyMetadata(false));

    /// <summary>
    /// Initializes a new instance of the BitmapCache class.
    /// </summary>
    public BitmapCache()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified render scale.
    /// </summary>
    public BitmapCache(double renderAtScale)
    {
        RenderAtScale = renderAtScale;
    }

    /// <summary>
    /// Gets or sets the scale at which the bitmap should be rendered.
    /// </summary>
    public double RenderAtScale
    {
        get => (double)(GetValue(RenderAtScaleProperty) ?? 1d);
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "RenderAtScale must be a finite positive number.");
            }

            if (RenderAtScale == value) return;
            SetValue(RenderAtScaleProperty, value);
            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the cache snaps to device pixels.
    /// </summary>
    public bool SnapsToDevicePixels
    {
        get => (bool)(GetValue(SnapsToDevicePixelsProperty) ?? false);
        set
        {
            if (SnapsToDevicePixels == value) return;
            SetValue(SnapsToDevicePixelsProperty, value);
            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether mipmap levels are enabled.
    /// </summary>
    public bool EnableClearType
    {
        get => (bool)(GetValue(EnableClearTypeProperty) ?? false);
        set
        {
            if (EnableClearType == value) return;
            SetValue(EnableClearTypeProperty, value);
            OnChanged();
        }
    }

    public new BitmapCache Clone() => (BitmapCache)base.Clone();
    public new BitmapCache CloneCurrentValue() => (BitmapCache)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new BitmapCache();
}

/// <summary>
/// Provides rendering capability tier information.
/// </summary>
public static class RenderCapability
{
    /// <summary>
    /// Gets the rendering capability tier for the current thread.
    /// </summary>
    public static int Tier => 2 << 16; // Tier 2 (hardware-accelerated)

    /// <summary>
    /// Gets a value indicating whether the system supports the specified pixel shader version.
    /// </summary>
    public static bool IsPixelShaderVersionSupported(short majorVersionRequested, short minorVersionRequested)
    {
        return majorVersionRequested is >= 0 and <= 3 && minorVersionRequested >= 0;
    }

    public static bool IsPixelShaderVersionSupportedInSoftware(short majorVersionRequested, short minorVersionRequested) =>
        majorVersionRequested is >= 0 and <= 3 && minorVersionRequested >= 0;

    public static int MaxPixelShaderInstructionSlots(short majorVersionRequested, short minorVersionRequested) =>
        majorVersionRequested switch
        {
            >= 3 => 512,
            2 => 96,
            _ => 0,
        };

    /// <summary>
    /// Gets a value indicating whether the system supports the specified shader model.
    /// </summary>
    public static bool IsShaderEffectSoftwareRenderingSupported => true;

    /// <summary>
    /// Gets the maximum texture size for the current hardware.
    /// </summary>
    public static Size MaxHardwareTextureSize => new(16384, 16384);

    /// <summary>
    /// Occurs when the rendering capability tier changes.
    /// </summary>
    public static event EventHandler? TierChanged;

    internal static void RaiseTierChanged() => TierChanged?.Invoke(null, EventArgs.Empty);
}
