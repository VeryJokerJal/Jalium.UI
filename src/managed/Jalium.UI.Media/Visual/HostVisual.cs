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
}

/// <summary>
/// Represents a target for rendering visuals from a HostVisual.
/// </summary>
public sealed class VisualTarget : IDisposable
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
    /// Gets or sets the root visual of this target.
    /// </summary>
    public Visual? RootVisual { get; set; }

    /// <summary>
    /// Gets the transform from root to host.
    /// </summary>
    public Matrix TransformToAncestor => Matrix.Identity;

    public void Dispose()
    {
        RootVisual = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a visual that caches its content as a bitmap for performance.
/// </summary>
public sealed class BitmapCacheBrush : Brush
{
    /// <summary>
    /// Gets or sets the Visual whose content is cached.
    /// </summary>
    public Visual? Target { get; set; }

    /// <summary>
    /// Gets or sets the bitmap cache settings.
    /// </summary>
    public BitmapCache? BitmapCache { get; set; }

    /// <summary>
    /// Gets or sets the opacity of the cached bitmap.
    /// </summary>
    public new double Opacity { get; set; } = 1.0;
}

/// <summary>
/// Defines a means to cache a <see cref="UIElement"/> and its subtree
/// into hardware or software surfaces for faster compositing.
/// </summary>
public abstract class CacheMode
{
}

/// <summary>
/// Provides caching behavior for rendered content.
/// </summary>
public sealed class BitmapCache : CacheMode
{
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
    public double RenderAtScale { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets a value indicating whether the cache snaps to device pixels.
    /// </summary>
    public bool SnapsToDevicePixels { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether mipmap levels are enabled.
    /// </summary>
    public bool EnableClearType { get; set; }
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
    public static bool IsPixelShaderVersionSupported(int majorVersionRequested, int minorVersionRequested)
    {
        return majorVersionRequested <= 3;
    }

    /// <summary>
    /// Gets a value indicating whether the system supports the specified shader model.
    /// </summary>
    public static bool IsShaderEffectSoftwareRenderingSupported => true;

    /// <summary>
    /// Gets the maximum texture size for the current hardware.
    /// </summary>
    public static int MaxHardwareTextureSize => 16384;

    /// <summary>
    /// Occurs when the rendering capability tier changes.
    /// </summary>
    public static event EventHandler? TierChanged;

    internal static void RaiseTierChanged() => TierChanged?.Invoke(null, EventArgs.Empty);
}
