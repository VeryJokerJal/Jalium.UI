using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents the constraints on the size of the viewport and the cache for a hierarchical virtualization scenario.
/// </summary>
public struct HierarchicalVirtualizationConstraints : IEquatable<HierarchicalVirtualizationConstraints>
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

    /// <summary>
    /// Monotonic epoch used by the hierarchical measure pass to discard stale anchor/scroll
    /// corrections. Internal and intentionally EXCLUDED from equality/hash so a generation bump
    /// alone does not force a re-measure of an otherwise-unchanged subtree.
    /// </summary>
    internal long ScrollGeneration { get; set; }

    // Equality compares the public layout-affecting fields only (CacheLength, CacheLengthUnit,
    // Viewport). NOTE: WPF's own operator== has a known typo comparing Viewport to itself; this
    // implementation compares the two operands correctly.
    public bool Equals(HierarchicalVirtualizationConstraints other) =>
        CacheLength == other.CacheLength && CacheLengthUnit == other.CacheLengthUnit && Viewport == other.Viewport;

    public override bool Equals(object? obj) => obj is HierarchicalVirtualizationConstraints other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CacheLength, CacheLengthUnit, Viewport);

    public static bool operator ==(HierarchicalVirtualizationConstraints left, HierarchicalVirtualizationConstraints right) => left.Equals(right);

    public static bool operator !=(HierarchicalVirtualizationConstraints left, HierarchicalVirtualizationConstraints right) => !left.Equals(right);
}

/// <summary>
/// Represents the desired sizes of the header element in a hierarchical virtualization scenario.
/// </summary>
public struct HierarchicalVirtualizationHeaderDesiredSizes : IEquatable<HierarchicalVirtualizationHeaderDesiredSizes>
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

    public bool Equals(HierarchicalVirtualizationHeaderDesiredSizes other) =>
        LogicalSize == other.LogicalSize && PixelSize == other.PixelSize;

    public override bool Equals(object? obj) => obj is HierarchicalVirtualizationHeaderDesiredSizes other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LogicalSize, PixelSize);

    public static bool operator ==(HierarchicalVirtualizationHeaderDesiredSizes left, HierarchicalVirtualizationHeaderDesiredSizes right) => left.Equals(right);

    public static bool operator !=(HierarchicalVirtualizationHeaderDesiredSizes left, HierarchicalVirtualizationHeaderDesiredSizes right) => !left.Equals(right);
}

/// <summary>
/// Represents the desired sizes of the items in a hierarchical virtualization scenario.
/// </summary>
public struct HierarchicalVirtualizationItemDesiredSizes : IEquatable<HierarchicalVirtualizationItemDesiredSizes>
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

    public bool Equals(HierarchicalVirtualizationItemDesiredSizes other) =>
        LogicalSize == other.LogicalSize &&
        LogicalSizeInViewport == other.LogicalSizeInViewport &&
        LogicalSizeBeforeViewport == other.LogicalSizeBeforeViewport &&
        LogicalSizeAfterViewport == other.LogicalSizeAfterViewport &&
        PixelSize == other.PixelSize &&
        PixelSizeInViewport == other.PixelSizeInViewport &&
        PixelSizeBeforeViewport == other.PixelSizeBeforeViewport &&
        PixelSizeAfterViewport == other.PixelSizeAfterViewport;

    public override bool Equals(object? obj) => obj is HierarchicalVirtualizationItemDesiredSizes other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        LogicalSize, LogicalSizeInViewport, LogicalSizeBeforeViewport, LogicalSizeAfterViewport,
        PixelSize, PixelSizeInViewport, PixelSizeBeforeViewport, PixelSizeAfterViewport);

    public static bool operator ==(HierarchicalVirtualizationItemDesiredSizes left, HierarchicalVirtualizationItemDesiredSizes right) => left.Equals(right);

    public static bool operator !=(HierarchicalVirtualizationItemDesiredSizes left, HierarchicalVirtualizationItemDesiredSizes right) => !left.Equals(right);
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
        if (double.IsNaN(cacheBeforeViewport))
        {
            throw new ArgumentException("Cache length cannot be NaN.", nameof(cacheBeforeViewport));
        }

        if (double.IsNaN(cacheAfterViewport))
        {
            throw new ArgumentException("Cache length cannot be NaN.", nameof(cacheAfterViewport));
        }

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

    /// <summary>
    /// Returns the invariant "before,after" string form (matching the
    /// <see cref="VirtualizationCacheLengthConverter"/> output for the invariant culture).
    /// </summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{CacheBeforeViewport},{CacheAfterViewport}");
}

/// <summary>
/// Converts strings to <see cref="VirtualizationCacheLength"/> instances.
/// </summary>
public sealed class VirtualizationCacheLengthConverter : TypeConverter
{
    /// <summary>Accepts strings and any numeric type (numeric -&gt; uniform cache length).</summary>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        switch (Type.GetTypeCode(sourceType))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
            case TypeCode.String:
                return true;
            default:
                return base.CanConvertFrom(context, sourceType);
        }
    }

    /// <summary>Supports conversion to string and to <see cref="InstanceDescriptor"/>.</summary>
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is string text)
        {
            culture ??= CultureInfo.CurrentCulture;
            var separator = culture.TextInfo.ListSeparator;
            var tokens = text.Split(separator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 1)
            {
                return new VirtualizationCacheLength(double.Parse(tokens[0], NumberStyles.Float, culture));
            }

            if (tokens.Length == 2)
            {
                return new VirtualizationCacheLength(
                    double.Parse(tokens[0], NumberStyles.Float, culture),
                    double.Parse(tokens[1], NumberStyles.Float, culture));
            }

            throw new FormatException(
                $"Cannot parse '{text}' as a {nameof(VirtualizationCacheLength)}; expected one or two '{separator}'-separated numbers.");
        }

        // Numeric source -> uniform cache length on both edges.
        var uniform = Convert.ToDouble(value, culture ?? CultureInfo.CurrentCulture);
        return new VirtualizationCacheLength(uniform);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is VirtualizationCacheLength cacheLength)
        {
            if (destinationType == typeof(string))
            {
                culture ??= CultureInfo.CurrentCulture;
                var separator = culture.TextInfo.ListSeparator;
                return string.Create(culture, $"{cacheLength.CacheBeforeViewport}{separator}{cacheLength.CacheAfterViewport}");
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                var constructor = typeof(VirtualizationCacheLength).GetConstructor(new[] { typeof(double), typeof(double) });
                if (constructor is not null)
                {
                    return new InstanceDescriptor(
                        constructor,
                        new object[] { cacheLength.CacheBeforeViewport, cacheLength.CacheAfterViewport });
                }
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
