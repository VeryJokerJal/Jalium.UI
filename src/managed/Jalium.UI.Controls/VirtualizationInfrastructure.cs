using System.ComponentModel;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a way to store and retrieve values associated with items in a virtualized list.
/// </summary>
public interface IContainItemStorage
{
    /// <summary>Stores a value for a given item and dependency property.</summary>
    void StoreItemValue(object item, DependencyProperty dp, object value);

    /// <summary>Reads a value for a given item and dependency property.</summary>
    object? ReadItemValue(object item, DependencyProperty dp);

    /// <summary>Clears a stored value for a given item and dependency property.</summary>
    void ClearItemValue(object item, DependencyProperty dp);

    /// <summary>Clears all stored values for the specified dependency property.</summary>
    void ClearValue(DependencyProperty dp);

    /// <summary>Clears all stored values.</summary>
    void Clear();
}

/// <summary>
/// Provides properties through which a hierarchical data item reports information about
/// its virtualization and scrolling state.
/// </summary>
public interface IHierarchicalVirtualizationAndScrollInfo
{
    /// <summary>Gets or sets the constraints for the virtualization.</summary>
    HierarchicalVirtualizationConstraints Constraints { get; set; }

    /// <summary>Gets the desired sizes of the header.</summary>
    HierarchicalVirtualizationHeaderDesiredSizes HeaderDesiredSizes { get; }

    /// <summary>Gets the desired sizes of the items.</summary>
    HierarchicalVirtualizationItemDesiredSizes ItemDesiredSizes { get; }

    /// <summary>Gets or sets a value indicating whether virtualization must be disabled.</summary>
    bool MustDisableVirtualization { get; set; }

    /// <summary>Gets or sets a value indicating whether the item is in a background layout pass.</summary>
    bool InBackgroundLayout { get; set; }
}

/// <summary>
/// Represents the constraints on the size of the viewport and the cache for a hierarchical virtualization scenario.
/// </summary>
public struct HierarchicalVirtualizationConstraints
{
    public HierarchicalVirtualizationConstraints(VirtualizationCacheLength cacheLength, VirtualizationCacheLengthUnit cacheLengthUnit, Rect viewport)
    {
        CacheLength = cacheLength;
        CacheLengthUnit = cacheLengthUnit;
        Viewport = viewport;
    }

    /// <summary>Gets the cache length.</summary>
    public VirtualizationCacheLength CacheLength { get; }

    /// <summary>Gets the cache length unit.</summary>
    public VirtualizationCacheLengthUnit CacheLengthUnit { get; }

    /// <summary>Gets the viewport rectangle.</summary>
    public Rect Viewport { get; }

    /// <summary>Gets the scroll offset.</summary>
    public Size ScrollOffset { get; init; }
}

/// <summary>
/// Represents the desired sizes of the header element in a hierarchical virtualization scenario.
/// </summary>
public struct HierarchicalVirtualizationHeaderDesiredSizes
{
    public HierarchicalVirtualizationHeaderDesiredSizes(Size logicalSize, Size pixelSize)
    {
        LogicalSize = logicalSize;
        PixelSize = pixelSize;
    }

    /// <summary>Gets the logical size of the header.</summary>
    public Size LogicalSize { get; }

    /// <summary>Gets the pixel size of the header.</summary>
    public Size PixelSize { get; }
}

/// <summary>
/// Represents the desired sizes of the items in a hierarchical virtualization scenario.
/// </summary>
public struct HierarchicalVirtualizationItemDesiredSizes
{
    public HierarchicalVirtualizationItemDesiredSizes(
        Size logicalSize, Size logicalSizeInViewport,
        Size logicalSizeBeforeViewport, Size logicalSizeAfterViewport,
        Size pixelSize, Size pixelSizeInViewport,
        Size pixelSizeBeforeViewport, Size pixelSizeAfterViewport)
    {
        LogicalSize = logicalSize;
        LogicalSizeInViewport = logicalSizeInViewport;
        LogicalSizeBeforeViewport = logicalSizeBeforeViewport;
        LogicalSizeAfterViewport = logicalSizeAfterViewport;
        PixelSize = pixelSize;
        PixelSizeInViewport = pixelSizeInViewport;
        PixelSizeBeforeViewport = pixelSizeBeforeViewport;
        PixelSizeAfterViewport = pixelSizeAfterViewport;
    }

    /// <summary>Gets the total logical size.</summary>
    public Size LogicalSize { get; }

    /// <summary>Gets the logical size in the viewport.</summary>
    public Size LogicalSizeInViewport { get; }

    /// <summary>Gets the logical size before the viewport.</summary>
    public Size LogicalSizeBeforeViewport { get; }

    /// <summary>Gets the logical size after the viewport.</summary>
    public Size LogicalSizeAfterViewport { get; }

    /// <summary>Gets the total pixel size.</summary>
    public Size PixelSize { get; }

    /// <summary>Gets the pixel size in the viewport.</summary>
    public Size PixelSizeInViewport { get; }

    /// <summary>Gets the pixel size before the viewport.</summary>
    public Size PixelSizeBeforeViewport { get; }

    /// <summary>Gets the pixel size after the viewport.</summary>
    public Size PixelSizeAfterViewport { get; }
}

/// <summary>
/// Represents the length of the cache before and after the viewport when virtualizing.
/// </summary>
[TypeConverter(typeof(VirtualizationCacheLengthConverter))]
public struct VirtualizationCacheLength : IEquatable<VirtualizationCacheLength>
{
    /// <summary>
    /// Initializes a new instance with a uniform cache length before and after the viewport.
    /// </summary>
    public VirtualizationCacheLength(double uniformCacheLength) : this(uniformCacheLength, uniformCacheLength) { }

    /// <summary>
    /// Initializes a new instance with separate cache lengths before and after the viewport.
    /// </summary>
    public VirtualizationCacheLength(double cacheBeforeViewport, double cacheAfterViewport)
    {
        CacheBeforeViewport = cacheBeforeViewport;
        CacheAfterViewport = cacheAfterViewport;
    }

    /// <summary>Gets the size of the cache before the viewport.</summary>
    public double CacheBeforeViewport { get; }

    /// <summary>Gets the size of the cache after the viewport.</summary>
    public double CacheAfterViewport { get; }

    public bool Equals(VirtualizationCacheLength other) =>
        CacheBeforeViewport == other.CacheBeforeViewport && CacheAfterViewport == other.CacheAfterViewport;

    public override bool Equals(object? obj) => obj is VirtualizationCacheLength other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(CacheBeforeViewport, CacheAfterViewport);
    public static bool operator ==(VirtualizationCacheLength left, VirtualizationCacheLength right) => left.Equals(right);
    public static bool operator !=(VirtualizationCacheLength left, VirtualizationCacheLength right) => !left.Equals(right);
}

/// <summary>
/// Converts strings to <see cref="VirtualizationCacheLength"/> instances.
/// </summary>
public sealed class VirtualizationCacheLengthConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            var parts = s.Split(',');
            if (parts.Length == 1)
                return new VirtualizationCacheLength(double.Parse(parts[0].Trim()));
            if (parts.Length == 2)
                return new VirtualizationCacheLength(double.Parse(parts[0].Trim()), double.Parse(parts[1].Trim()));
        }
        return base.ConvertFrom(context, culture, value);
    }
}
