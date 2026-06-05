namespace Jalium.UI.Media;

/// <summary>
/// Base class for all brushes that describe how an area is painted.
/// </summary>
public abstract class Brush
{
    private double _opacity = 1.0;

    /// <summary>
    /// Gets or sets the opacity of the brush (0.0 - 1.0).
    /// </summary>
    public double Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// Gets or sets the transform applied to the brush.
    /// </summary>
    public Transform? Transform { get; set; }
}

/// <summary>
/// Paints an area with a solid color.
/// </summary>
public sealed class SolidColorBrush : Brush
{
    /// <summary>
    /// Gets or sets the color of the brush.
    /// </summary>
    public Color Color { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    public SolidColorBrush()
    {
        Color = Color.Transparent;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    /// <param name="color">The brush color.</param>
    public SolidColorBrush(Color color)
    {
        Color = color;
    }

    /// <inheritdoc />
    public override string ToString() => $"SolidColorBrush({Color})";
}

/// <summary>
/// Base class for gradient brushes.
/// </summary>
[ContentProperty("GradientStops")]
public abstract class GradientBrush : Brush
{
    /// <summary>
    /// Gets the collection of gradient stops.
    /// </summary>
    /// <remarks>
    /// A <see cref="GradientStopCollection"/> whose <see cref="GradientStopCollection.Changed"/>
    /// event is wired to <see cref="InvalidateContentHash"/> in the constructor, so adding,
    /// removing or recolouring a stop now automatically invalidates the cached content hash —
    /// the render backend rebuilds the native brush without the caller having to signal it.
    /// </remarks>
    public GradientStopCollection GradientStops { get; }

    /// <summary>
    /// Initializes the gradient-stop collection and subscribes to its change notifications so
    /// that stop mutations invalidate the cached content hash.
    /// </summary>
    protected GradientBrush()
    {
        GradientStops = new GradientStopCollection();
        GradientStops.Changed += OnGradientStopsChanged;
    }

    private void OnGradientStopsChanged(object? sender, EventArgs e) => InvalidateContentHash();

    /// <summary>
    /// Gets or sets how the gradient is drawn outside the [0, 1] range.
    /// </summary>
    public GradientSpreadMethod SpreadMethod { get; set; } = GradientSpreadMethod.Pad;

    /// <summary>
    /// Gets or sets the mapping mode for the gradient.
    /// </summary>
    public BrushMappingMode MappingMode { get; set; } = BrushMappingMode.RelativeToBoundingBox;

    // 缓存的 content hash —— 第一次 ComputeContentHash 计算后存到 _cachedContentHash，
    // 后续直接读，把每帧 O(stops 数) 的 FNV 折叠压成 O(1)。失效来源有两类：
    // ① 标量 setter (StartPoint/EndPoint/Center/Radius/SpreadMethod/MappingMode/Opacity)
    //    在写入路径里显式调用 InvalidateContentHash()；
    // ② GradientStops 现为 GradientStopCollection，其 Changed 事件(结构变更 + 每个 stop 的
    //    Color/Offset 变更)在构造函数里接到 InvalidateContentHash() —— 因此 stops 的 Add/
    //    Clear/重新着色不再需要用户手动通知（修掉旧 List 暴露下的失效盲区）。
    private long _cachedContentHash;
    private bool _hasCachedContentHash;

    /// <summary>
    /// Forces the next <see cref="ComputeContentHash"/> call to re-evaluate from
    /// scratch. Subclasses must call this from any setter that mutates a field
    /// folded into the hash, otherwise the cached value would diverge from the
    /// brush's observable state.
    /// </summary>
    protected void InvalidateContentHash()
    {
        _hasCachedContentHash = false;
    }

    /// <summary>
    /// Computes a 64-bit content hash of the brush — every observable field that
    /// changes the rendered result, including each <see cref="GradientStop"/>'s
    /// color and offset. Lets the rendering backend detect when a managed brush
    /// instance has been mutated since the last native upload (e.g. a stop was
    /// added, a start point moved) and rebuild the native resource without
    /// having to dispose+create on every frame.
    ///
    /// Result is memoised in <c>_cachedContentHash</c>; subclasses' hash bodies
    /// run at most once per logical mutation generation.
    ///
    /// Subclasses fold their endpoint / center / radius fields into the base
    /// hash via <see cref="ComputeBaseContentHash"/>.
    /// </summary>
    internal long ComputeContentHash()
    {
        if (_hasCachedContentHash) return _cachedContentHash;
        long h = ComputeContentHashCore();
        _cachedContentHash = h;
        _hasCachedContentHash = true;
        return h;
    }

    /// <summary>
    /// Subclass hook that does the actual O(n) hash fold. Called once per
    /// mutation generation by <see cref="ComputeContentHash"/>.
    /// </summary>
    internal abstract long ComputeContentHashCore();

    /// <summary>
    /// Folds the fields shared by all gradient brushes — spread method, mapping
    /// mode, opacity, and every gradient stop — into a 64-bit hash. Subclasses
    /// XOR their own variant fields on top.
    /// </summary>
    protected long ComputeBaseContentHash()
    {
        // FNV-1a 64-bit accumulator. Cheap, allocation-free, and stable across
        // runs (Random/string-keyed HashCode is not, which would let the hash
        // drift between processes — an unwanted property for a render cache key
        // that may be persisted alongside the recorded drawing list).
        const long FnvOffsetBasis = unchecked((long)0xcbf29ce484222325UL);
        const long FnvPrime = unchecked((long)0x100000001b3UL);

        long hash = FnvOffsetBasis;
        hash = unchecked((hash ^ (long)SpreadMethod) * FnvPrime);
        hash = unchecked((hash ^ (long)MappingMode) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(Opacity)) * FnvPrime);

        var stops = GradientStops;
        int count = stops.Count;
        hash = unchecked((hash ^ count) * FnvPrime);
        for (int i = 0; i < count; i++)
        {
            var s = stops[i];
            // Pack ARGB as 32 bits and offset as 64 bits, fold each separately —
            // alternates dominate the dispersion for typical 2-3 stop gradients.
            uint argb = ((uint)s.Color.A << 24) | ((uint)s.Color.R << 16) |
                        ((uint)s.Color.G << 8) | s.Color.B;
            hash = unchecked((hash ^ argb) * FnvPrime);
            hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(s.Offset)) * FnvPrime);
        }

        return hash;
    }
}

/// <summary>
/// Paints an area with a linear gradient.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush
{
    /// <summary>
    /// Gets or sets the starting point of the gradient.
    /// </summary>
    public Point StartPoint { get; set; } = new(0, 0);

    /// <summary>
    /// Gets or sets the ending point of the gradient.
    /// </summary>
    public Point EndPoint { get; set; } = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGradientBrush"/> class.
    /// </summary>
    public LinearGradientBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGradientBrush"/> class.
    /// </summary>
    /// <param name="startColor">The starting color.</param>
    /// <param name="endColor">The ending color.</param>
    /// <param name="angle">The gradient angle in degrees.</param>
    public LinearGradientBrush(Color startColor, Color endColor, double angle)
    {
        GradientStops.Add(new GradientStop(startColor, 0));
        GradientStops.Add(new GradientStop(endColor, 1));

        // Calculate start and end points based on angle
        var radians = angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        StartPoint = new Point(0.5 - cos * 0.5, 0.5 - sin * 0.5);
        EndPoint = new Point(0.5 + cos * 0.5, 0.5 + sin * 0.5);
    }

    /// <inheritdoc />
    internal override long ComputeContentHashCore()
    {
        const long FnvPrime = unchecked((long)0x100000001b3UL);
        long hash = ComputeBaseContentHash();
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(StartPoint.X)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(StartPoint.Y)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(EndPoint.X)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(EndPoint.Y)) * FnvPrime);
        return hash;
    }
}

/// <summary>
/// Paints an area with a radial gradient.
/// </summary>
public sealed class RadialGradientBrush : GradientBrush
{
    /// <summary>
    /// Gets or sets the center of the gradient.
    /// </summary>
    public Point Center { get; set; } = new(0.5, 0.5);

    /// <summary>
    /// Gets or sets the location of the gradient origin.
    /// </summary>
    public Point GradientOrigin { get; set; } = new(0.5, 0.5);

    private double _radiusX = 0.5;
    private double _radiusY = 0.5;

    /// <summary>
    /// Gets or sets the horizontal radius of the gradient. Must be non-negative.
    /// </summary>
    public double RadiusX
    {
        get => _radiusX;
        set => _radiusX = Math.Max(0, value);
    }

    /// <summary>
    /// Gets or sets the vertical radius of the gradient. Must be non-negative.
    /// </summary>
    public double RadiusY
    {
        get => _radiusY;
        set => _radiusY = Math.Max(0, value);
    }

    /// <inheritdoc />
    internal override long ComputeContentHashCore()
    {
        const long FnvPrime = unchecked((long)0x100000001b3UL);
        long hash = ComputeBaseContentHash();
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(Center.X)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(Center.Y)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(GradientOrigin.X)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(GradientOrigin.Y)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(_radiusX)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(_radiusY)) * FnvPrime);
        return hash;
    }
}

/// <summary>
/// Describes a single color and its position in a gradient.
/// </summary>
public sealed class GradientStop : DependencyObject
{
    /// <summary>
    /// Identifies the <see cref="Color"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(Color), typeof(GradientStop),
            new PropertyMetadata(Color.Transparent));

    /// <summary>
    /// Identifies the <see cref="Offset"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty OffsetProperty =
        DependencyProperty.Register(nameof(Offset), typeof(double), typeof(GradientStop),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Gets or sets the color at this stop.
    /// </summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty)!;
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of this stop (0.0 - 1.0).
    /// </summary>
    public double Offset
    {
        get => (double)GetValue(OffsetProperty)!;
        set => SetValue(OffsetProperty, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientStop"/> class.
    /// </summary>
    public GradientStop()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientStop"/> class.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="offset">The offset (0.0 - 1.0).</param>
    public GradientStop(Color color, double offset)
    {
        Color = color;
        Offset = offset;
    }
}

/// <summary>
/// Specifies how a gradient extends outside its defined range.
/// </summary>
public enum GradientSpreadMethod
{
    /// <summary>
    /// The gradient stops at the boundary.
    /// </summary>
    Pad,

    /// <summary>
    /// The gradient reflects at the boundary.
    /// </summary>
    Reflect,

    /// <summary>
    /// The gradient repeats at the boundary.
    /// </summary>
    Repeat
}

/// <summary>
/// Specifies how a brush maps its coordinates.
/// </summary>
public enum BrushMappingMode
{
    /// <summary>
    /// Coordinates are absolute in device-independent pixels.
    /// </summary>
    Absolute,

    /// <summary>
    /// Coordinates are relative to the bounding box (0.0 - 1.0).
    /// </summary>
    RelativeToBoundingBox
}

/// <summary>
/// Base class for tile brushes (ImageBrush, VisualBrush).
/// </summary>
public abstract class TileBrush : Brush
{
    /// <summary>
    /// Gets or sets the horizontal alignment of the content within the brush.
    /// </summary>
    public AlignmentX AlignmentX { get; set; } = AlignmentX.Center;

    /// <summary>
    /// Gets or sets the vertical alignment of the content within the brush.
    /// </summary>
    public AlignmentY AlignmentY { get; set; } = AlignmentY.Center;

    /// <summary>
    /// Gets or sets how the content is stretched to fill the output area.
    /// </summary>
    public Stretch Stretch { get; set; } = Stretch.Fill;

    /// <summary>
    /// Gets or sets how the content is tiled when it is smaller than the output area.
    /// </summary>
    public TileMode TileMode { get; set; } = TileMode.None;

    /// <summary>
    /// Gets or sets the position of the brush's viewport.
    /// </summary>
    public Rect Viewport { get; set; } = new Rect(0, 0, 1, 1);

    /// <summary>
    /// Gets or sets the coordinate system for the Viewport property.
    /// </summary>
    public BrushMappingMode ViewportUnits { get; set; } = BrushMappingMode.RelativeToBoundingBox;

    /// <summary>
    /// Gets or sets the position of the content within the tile.
    /// </summary>
    public Rect Viewbox { get; set; } = new Rect(0, 0, 1, 1);

    /// <summary>
    /// Gets or sets the coordinate system for the Viewbox property.
    /// </summary>
    public BrushMappingMode ViewboxUnits { get; set; } = BrushMappingMode.RelativeToBoundingBox;
}

/// <summary>
/// Paints an area with an image.
/// </summary>
public sealed class ImageBrush : TileBrush
{
    /// <summary>
    /// Gets or sets the image source.
    /// </summary>
    public ImageSource? ImageSource { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    public ImageBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class with the specified image source.
    /// </summary>
    /// <param name="imageSource">The image to use.</param>
    public ImageBrush(ImageSource imageSource)
    {
        ImageSource = imageSource;
    }

    /// <inheritdoc />
    public override string ToString() => $"ImageBrush({ImageSource})";
}

/// <summary>
/// Paints an area with a Drawing.
/// </summary>
public sealed class DrawingBrush : TileBrush
{
    /// <summary>
    /// Gets or sets the Drawing that defines the content of this brush.
    /// </summary>
    public Drawing? Drawing { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingBrush"/> class.
    /// </summary>
    public DrawingBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingBrush"/> class with the specified drawing.
    /// </summary>
    /// <param name="drawing">The drawing to use.</param>
    public DrawingBrush(Drawing drawing)
    {
        Drawing = drawing;
    }

    /// <inheritdoc />
    public override string ToString() => $"DrawingBrush({Drawing})";
}

/// <summary>
/// Paints an area with a visual element.
/// </summary>
public sealed class VisualBrush : TileBrush
{
    /// <summary>
    /// Gets or sets the visual that provides the content for the brush.
    /// </summary>
    public object? Visual { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether content is laid out automatically.
    /// </summary>
    public bool AutoLayoutContent { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualBrush"/> class.
    /// </summary>
    public VisualBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualBrush"/> class with the specified visual.
    /// </summary>
    /// <param name="visual">The visual to use.</param>
    public VisualBrush(object visual)
    {
        Visual = visual;
    }

    /// <inheritdoc />
    public override string ToString() => $"VisualBrush({Visual})";
}

/// <summary>
/// Specifies horizontal alignment.
/// </summary>
public enum AlignmentX
{
    /// <summary>
    /// Align to the left.
    /// </summary>
    Left,

    /// <summary>
    /// Align to the center.
    /// </summary>
    Center,

    /// <summary>
    /// Align to the right.
    /// </summary>
    Right
}

/// <summary>
/// Specifies vertical alignment.
/// </summary>
public enum AlignmentY
{
    /// <summary>
    /// Align to the top.
    /// </summary>
    Top,

    /// <summary>
    /// Align to the center.
    /// </summary>
    Center,

    /// <summary>
    /// Align to the bottom.
    /// </summary>
    Bottom
}

/// <summary>
/// Specifies how content is stretched to fill an area.
/// </summary>
public enum Stretch
{
    /// <summary>
    /// Content preserves its original size.
    /// </summary>
    None,

    /// <summary>
    /// Content is resized to fill the area. Aspect ratio is not preserved.
    /// </summary>
    Fill,

    /// <summary>
    /// Content is resized to fit in the area while preserving aspect ratio.
    /// </summary>
    Uniform,

    /// <summary>
    /// Content is resized to fill the area while preserving aspect ratio.
    /// Content may be clipped if the aspect ratios don't match.
    /// </summary>
    UniformToFill
}

/// <summary>
/// Specifies how a brush tiles its content.
/// </summary>
public enum TileMode
{
    /// <summary>
    /// The content is not tiled.
    /// </summary>
    None,

    /// <summary>
    /// The content is tiled.
    /// </summary>
    Tile,

    /// <summary>
    /// The content is flipped horizontally on alternate columns.
    /// </summary>
    FlipX,

    /// <summary>
    /// The content is flipped vertically on alternate rows.
    /// </summary>
    FlipY,

    /// <summary>
    /// The content is flipped both horizontally and vertically on alternating tiles.
    /// </summary>
    FlipXY
}
