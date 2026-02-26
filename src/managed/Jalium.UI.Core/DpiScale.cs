namespace Jalium.UI;

/// <summary>
/// Stores DPI information from which a <see cref="Visual"/> or <see cref="UIElement"/> is rendered.
/// </summary>
public readonly struct DpiScale : IEquatable<DpiScale>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DpiScale"/> structure.
    /// </summary>
    /// <param name="dpiScaleX">The DPI scale on the X axis.</param>
    /// <param name="dpiScaleY">The DPI scale on the Y axis.</param>
    public DpiScale(double dpiScaleX, double dpiScaleY)
    {
        DpiScaleX = dpiScaleX;
        DpiScaleY = dpiScaleY;
    }

    /// <summary>
    /// Gets the DPI scale on the X axis.
    /// </summary>
    public double DpiScaleX { get; }

    /// <summary>
    /// Gets the DPI scale on the Y axis.
    /// </summary>
    public double DpiScaleY { get; }

    /// <summary>
    /// Gets the DPI along X axis (DpiScaleX * 96).
    /// </summary>
    public double PixelsPerDip => DpiScaleX;

    /// <summary>
    /// Gets the horizontal DPI value.
    /// </summary>
    public double PixelsPerInchX => DpiScaleX * 96.0;

    /// <summary>
    /// Gets the vertical DPI value.
    /// </summary>
    public double PixelsPerInchY => DpiScaleY * 96.0;

    /// <inheritdoc />
    public bool Equals(DpiScale other) =>
        DpiScaleX == other.DpiScaleX && DpiScaleY == other.DpiScaleY;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DpiScale other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(DpiScaleX, DpiScaleY);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(DpiScale left, DpiScale right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(DpiScale left, DpiScale right) => !left.Equals(right);
}

/// <summary>
/// Provides data for DPI changed events.
/// </summary>
public sealed class DpiChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DpiChangedEventArgs"/> class.
    /// </summary>
    public DpiChangedEventArgs(DpiScale oldDpi, DpiScale newDpi)
    {
        OldDpi = oldDpi;
        NewDpi = newDpi;
    }

    /// <summary>
    /// Gets the old DPI scale information.
    /// </summary>
    public DpiScale OldDpi { get; }

    /// <summary>
    /// Gets the new DPI scale information.
    /// </summary>
    public DpiScale NewDpi { get; }
}

/// <summary>
/// Represents the method that handles DPI changed events.
/// </summary>
public delegate void DpiChangedEventHandler(object sender, DpiChangedEventArgs e);
