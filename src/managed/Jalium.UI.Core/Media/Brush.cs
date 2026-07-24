using System.Globalization;
using System.Runtime.CompilerServices;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media;

/// <summary>
/// Base class for all brushes that describe how an area is painted.
/// </summary>
public abstract partial class Brush : Animatable, IFormattable
{
    private static readonly Transform s_identityTransform = CreateIdentityTransform();
    private WeakReference<UIElement>? _singleRenderOwner;
    private int _singleRenderOwnerReferenceCount;
    private ConditionalWeakTable<UIElement, RenderOwnerRegistration>? _renderOwners;

    private sealed class RenderOwnerRegistration
    {
        public int ReferenceCount { get; set; }
    }

    public static readonly DependencyProperty OpacityProperty =
        DependencyProperty.Register(nameof(Opacity), typeof(double), typeof(Brush), new PropertyMetadata(1d));
    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(nameof(Transform), typeof(Transform), typeof(Brush), new PropertyMetadata(s_identityTransform));
    public static readonly DependencyProperty RelativeTransformProperty =
        DependencyProperty.Register(nameof(RelativeTransform), typeof(Transform), typeof(Brush), new PropertyMetadata(s_identityTransform));

    /// <summary>
    /// Gets or sets the opacity of the brush (0.0 - 1.0).
    /// </summary>
    public double Opacity
    {
        get => (double)(GetValue(OpacityProperty) ?? 1d);
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>
    /// Gets or sets the transform applied to the brush.
    /// </summary>
    public Transform? Transform
    {
        get => (Transform?)GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    /// <summary>Gets or sets the transform applied relative to the brush output bounds.</summary>
    public Transform? RelativeTransform
    {
        get => (Transform?)GetValue(RelativeTransformProperty);
        set => SetValue(RelativeTransformProperty, value);
    }

    public new Brush Clone() => (Brush)base.Clone();
    public new Brush CloneCurrentValue() => (Brush)base.CloneCurrentValue();

    public override string ToString() => ToString(CultureInfo.CurrentCulture);
    public string ToString(IFormatProvider? provider) => GetType().Name;
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);

    /// <summary>
    /// Adds a weak visual owner for in-place brush change invalidation. A single element can
    /// consume the same brush through multiple dependency properties, hence the reference count.
    /// </summary>
    internal void AddRenderOwner(UIElement owner)
    {
        if (IsFrozen)
        {
            return;
        }

        if (_renderOwners is { } owners)
        {
            var registration = owners.GetValue(owner, static _ => new RenderOwnerRegistration());
            registration.ReferenceCount++;
            return;
        }

        if (_singleRenderOwner is null)
        {
            _singleRenderOwner = new WeakReference<UIElement>(owner);
            _singleRenderOwnerReferenceCount = 1;
            return;
        }

        if (!_singleRenderOwner.TryGetTarget(out var existingOwner))
        {
            _singleRenderOwner.SetTarget(owner);
            _singleRenderOwnerReferenceCount = 1;
            return;
        }

        if (ReferenceEquals(existingOwner, owner))
        {
            _singleRenderOwnerReferenceCount++;
            return;
        }

        // The overwhelmingly common case is a brush owned by one visual; keep that path to one
        // WeakReference. Promote to an ephemeron table only when a brush is genuinely shared.
        owners = new ConditionalWeakTable<UIElement, RenderOwnerRegistration>();
        owners.Add(existingOwner, new RenderOwnerRegistration
        {
            ReferenceCount = _singleRenderOwnerReferenceCount
        });
        owners.Add(owner, new RenderOwnerRegistration { ReferenceCount = 1 });
        _renderOwners = owners;
        _singleRenderOwner = null;
        _singleRenderOwnerReferenceCount = 0;
    }

    /// <summary>Removes one dependency-property use of this brush from a visual owner.</summary>
    internal void RemoveRenderOwner(UIElement owner)
    {
        if (_renderOwners is { } owners)
        {
            if (owners.TryGetValue(owner, out var registration)
                && --registration.ReferenceCount == 0)
            {
                owners.Remove(owner);
            }
            return;
        }

        if (_singleRenderOwner is not null
            && _singleRenderOwner.TryGetTarget(out var singleOwner)
            && ReferenceEquals(singleOwner, owner)
            && --_singleRenderOwnerReferenceCount == 0)
        {
            _singleRenderOwner = null;
        }
    }

    /// <inheritdoc />
    protected override void OnChanged()
    {
        InvalidateRenderOwners();
        base.OnChanged();
    }

    private void InvalidateRenderOwners()
    {
        if (_renderOwners is { } owners)
        {
            foreach (var owner in owners)
            {
                owner.Key.InvalidateVisual();
            }
            return;
        }

        if (_singleRenderOwner?.TryGetTarget(out var singleOwner) == true)
        {
            singleOwner.InvalidateVisual();
        }
        else
        {
            _singleRenderOwner = null;
            _singleRenderOwnerReferenceCount = 0;
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, TransformProperty) || ReferenceEquals(e.Property, RelativeTransformProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, e.Property);
        }

        if (ReferenceEquals(e.Property, OpacityProperty)
            || ReferenceEquals(e.Property, TransformProperty)
            || ReferenceEquals(e.Property, RelativeTransformProperty))
        {
            WritePostscript();
        }
    }

    private static Transform CreateIdentityTransform()
    {
        var identity = new MatrixTransform(Matrix.Identity);
        identity.Freeze();
        return identity;
    }
}

/// <summary>
/// Paints an area with a solid color.
/// </summary>
public sealed class SolidColorBrush : Brush
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(Color), typeof(SolidColorBrush), new PropertyMetadata(Color.Transparent));

    /// <summary>
    /// Gets or sets the color of the brush.
    /// </summary>
    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.Transparent);
        set => SetValue(ColorProperty, value);
    }

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

    public new SolidColorBrush Clone() => (SolidColorBrush)base.Clone();
    public new SolidColorBrush CloneCurrentValue() => (SolidColorBrush)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new SolidColorBrush();

    public static object DeserializeFrom(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new SolidColorBrush(Color.FromArgb(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()));
    }

    /// <inheritdoc />
    public override string ToString() => $"SolidColorBrush({Color})";
    public new string ToString(IFormatProvider? provider) => Color.ToString(provider);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ColorProperty))
        {
            WritePostscript();
        }
    }
}

/// <summary>
/// Base class for gradient brushes.
/// </summary>
[Jalium.UI.Markup.ContentProperty("GradientStops")]
public abstract class GradientBrush : Brush
{
    /// <summary>Identifies the <see cref="ColorInterpolationMode"/> property.</summary>
    public static readonly DependencyProperty ColorInterpolationModeProperty =
        DependencyProperty.Register(
            nameof(ColorInterpolationMode),
            typeof(ColorInterpolationMode),
            typeof(GradientBrush),
            new PropertyMetadata(ColorInterpolationMode.SRgbLinearInterpolation));
    public static readonly DependencyProperty GradientStopsProperty =
        DependencyProperty.Register(nameof(GradientStops), typeof(GradientStopCollection), typeof(GradientBrush), new PropertyMetadata(null));
    public static readonly DependencyProperty MappingModeProperty =
        DependencyProperty.Register(nameof(MappingMode), typeof(BrushMappingMode), typeof(GradientBrush), new PropertyMetadata(BrushMappingMode.RelativeToBoundingBox));
    public static readonly DependencyProperty SpreadMethodProperty =
        DependencyProperty.Register(nameof(SpreadMethod), typeof(GradientSpreadMethod), typeof(GradientBrush), new PropertyMetadata(GradientSpreadMethod.Pad));

    /// <summary>
    /// Gets the collection of gradient stops.
    /// </summary>
    /// <remarks>
    /// A <see cref="GradientStopCollection"/> whose <see cref="GradientStopCollection.Changed"/>
    /// event is wired to <see cref="InvalidateContentHash"/> in the constructor, so adding,
    /// removing or recolouring a stop now automatically invalidates the cached content hash —
    /// the render backend rebuilds the native brush without the caller having to signal it.
    /// </remarks>
    public GradientStopCollection GradientStops
    {
        get => (GradientStopCollection?)GetValue(GradientStopsProperty) ?? new GradientStopCollection();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(GradientStops, value))
            {
                return;
            }
            SetValue(GradientStopsProperty, value);
        }
    }

    /// <summary>
    /// Initializes the gradient-stop collection and subscribes to its change notifications so
    /// that stop mutations invalidate the cached content hash.
    /// </summary>
    protected GradientBrush()
        : this(new GradientStopCollection())
    {
    }

    protected GradientBrush(GradientStopCollection gradientStopCollection)
    {
        ArgumentNullException.ThrowIfNull(gradientStopCollection);
        GradientStops = gradientStopCollection;
    }

    private void OnGradientStopsChanged(object? sender, EventArgs e) => InvalidateContentHash();

    /// <summary>
    /// Gets or sets how the gradient is drawn outside the [0, 1] range.
    /// </summary>
    public GradientSpreadMethod SpreadMethod
    {
        get => (GradientSpreadMethod)(GetValue(SpreadMethodProperty) ?? GradientSpreadMethod.Pad);
        set => SetValue(SpreadMethodProperty, value);
    }

    /// <summary>
    /// Gets or sets the mapping mode for the gradient.
    /// </summary>
    public BrushMappingMode MappingMode
    {
        get => (BrushMappingMode)(GetValue(MappingModeProperty) ?? BrushMappingMode.RelativeToBoundingBox);
        set => SetValue(MappingModeProperty, value);
    }

    /// <summary>Gets or sets the color space used to interpolate gradient stops.</summary>
    public ColorInterpolationMode ColorInterpolationMode
    {
        get => (ColorInterpolationMode)(GetValue(ColorInterpolationModeProperty) ?? ColorInterpolationMode.SRgbLinearInterpolation);
        set => SetValue(ColorInterpolationModeProperty, value);
    }

    public new GradientBrush Clone() => (GradientBrush)base.Clone();
    public new GradientBrush CloneCurrentValue() => (GradientBrush)base.CloneCurrentValue();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, GradientStopsProperty))
        {
            if (e.OldValue is GradientStopCollection oldStops)
            {
                oldStops.Changed -= OnGradientStopsChanged;
            }
            if (e.NewValue is GradientStopCollection newStops)
            {
                newStops.Changed += OnGradientStopsChanged;
            }
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, GradientStopsProperty);
        }

        if (ReferenceEquals(e.Property, GradientStopsProperty)
            || ReferenceEquals(e.Property, SpreadMethodProperty)
            || ReferenceEquals(e.Property, MappingModeProperty)
            || ReferenceEquals(e.Property, ColorInterpolationModeProperty))
        {
            InvalidateContentHash();
            WritePostscript();
        }
    }

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
        hash = unchecked((hash ^ (long)ColorInterpolationMode) * FnvPrime);
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
    public static readonly DependencyProperty StartPointProperty =
        DependencyProperty.Register(nameof(StartPoint), typeof(Point), typeof(LinearGradientBrush), new PropertyMetadata(new Point(0, 0)));
    public static readonly DependencyProperty EndPointProperty =
        DependencyProperty.Register(nameof(EndPoint), typeof(Point), typeof(LinearGradientBrush), new PropertyMetadata(new Point(1, 1)));

    /// <summary>
    /// Gets or sets the starting point of the gradient.
    /// </summary>
    public Point StartPoint
    {
        get => (Point)(GetValue(StartPointProperty) ?? new Point(0, 0));
        set => SetValue(StartPointProperty, value);
    }

    /// <summary>
    /// Gets or sets the ending point of the gradient.
    /// </summary>
    public Point EndPoint
    {
        get => (Point)(GetValue(EndPointProperty) ?? new Point(1, 1));
        set => SetValue(EndPointProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGradientBrush"/> class.
    /// </summary>
    public LinearGradientBrush()
    {
    }

    public LinearGradientBrush(Color startColor, Color endColor, Point startPoint, Point endPoint)
    {
        GradientStops.Add(new GradientStop(startColor, 0));
        GradientStops.Add(new GradientStop(endColor, 1));
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    public LinearGradientBrush(GradientStopCollection gradientStopCollection)
        : base(gradientStopCollection)
    {
    }

    public LinearGradientBrush(GradientStopCollection gradientStopCollection, double angle)
        : base(gradientStopCollection)
    {
        SetAngle(angle);
    }

    public LinearGradientBrush(GradientStopCollection gradientStopCollection, Point startPoint, Point endPoint)
        : base(gradientStopCollection)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
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

        SetAngle(angle);
    }

    private void SetAngle(double angle)
    {
        var radians = angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        StartPoint = new Point(0.5 - cos * 0.5, 0.5 - sin * 0.5);
        EndPoint = new Point(0.5 + cos * 0.5, 0.5 + sin * 0.5);
    }

    public new LinearGradientBrush Clone() => (LinearGradientBrush)base.Clone();
    public new LinearGradientBrush CloneCurrentValue() => (LinearGradientBrush)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new LinearGradientBrush();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, StartPointProperty) || ReferenceEquals(e.Property, EndPointProperty))
        {
            InvalidateContentHash();
            WritePostscript();
        }
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
    public static readonly DependencyProperty CenterProperty =
        DependencyProperty.Register(nameof(Center), typeof(Point), typeof(RadialGradientBrush), new PropertyMetadata(new Point(0.5, 0.5)));
    public static readonly DependencyProperty GradientOriginProperty =
        DependencyProperty.Register(nameof(GradientOrigin), typeof(Point), typeof(RadialGradientBrush), new PropertyMetadata(new Point(0.5, 0.5)));
    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register(nameof(RadiusX), typeof(double), typeof(RadialGradientBrush), new PropertyMetadata(0.5d));
    public static readonly DependencyProperty RadiusYProperty =
        DependencyProperty.Register(nameof(RadiusY), typeof(double), typeof(RadialGradientBrush), new PropertyMetadata(0.5d));

    /// <summary>
    /// Gets or sets the center of the gradient.
    /// </summary>
    public Point Center
    {
        get => (Point)(GetValue(CenterProperty) ?? new Point(0.5, 0.5));
        set => SetValue(CenterProperty, value);
    }

    /// <summary>
    /// Gets or sets the location of the gradient origin.
    /// </summary>
    public Point GradientOrigin
    {
        get => (Point)(GetValue(GradientOriginProperty) ?? new Point(0.5, 0.5));
        set => SetValue(GradientOriginProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal radius of the gradient. Must be non-negative.
    /// </summary>
    public double RadiusX
    {
        get => (double)(GetValue(RadiusXProperty) ?? 0.5d);
        set
        {
            double coerced = Math.Max(0, value);
            SetValue(RadiusXProperty, coerced);
        }
    }

    /// <summary>
    /// Gets or sets the vertical radius of the gradient. Must be non-negative.
    /// </summary>
    public double RadiusY
    {
        get => (double)(GetValue(RadiusYProperty) ?? 0.5d);
        set
        {
            double coerced = Math.Max(0, value);
            SetValue(RadiusYProperty, coerced);
        }
    }

    public RadialGradientBrush()
    {
    }

    public RadialGradientBrush(Color startColor, Color endColor)
    {
        GradientStops.Add(new GradientStop(startColor, 0));
        GradientStops.Add(new GradientStop(endColor, 1));
    }

    public RadialGradientBrush(GradientStopCollection gradientStopCollection)
        : base(gradientStopCollection)
    {
    }

    public new RadialGradientBrush Clone() => (RadialGradientBrush)base.Clone();
    public new RadialGradientBrush CloneCurrentValue() => (RadialGradientBrush)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new RadialGradientBrush();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, CenterProperty)
            || ReferenceEquals(e.Property, GradientOriginProperty)
            || ReferenceEquals(e.Property, RadiusXProperty)
            || ReferenceEquals(e.Property, RadiusYProperty))
        {
            InvalidateContentHash();
            WritePostscript();
        }
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
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(RadiusX)) * FnvPrime);
        hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(RadiusY)) * FnvPrime);
        return hash;
    }
}

/// <summary>
/// Describes a single color and its position in a gradient.
/// </summary>
public sealed class GradientStop : Animatable, IFormattable
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
        set => SetValue(OffsetProperty, value);
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

    public new GradientStop Clone() => (GradientStop)base.Clone();

    public new GradientStop CloneCurrentValue() => (GradientStop)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new GradientStop();

    public override string ToString() => ToString(CultureInfo.CurrentCulture);

    public string ToString(IFormatProvider? provider)
        => $"{Color.ToString(provider)},{Offset.ToString(provider)}";

    string IFormattable.ToString(string? format, IFormatProvider? provider)
        => ToString(provider);
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
    public static readonly DependencyProperty AlignmentXProperty =
        DependencyProperty.Register(nameof(AlignmentX), typeof(AlignmentX), typeof(TileBrush), new PropertyMetadata(AlignmentX.Center));
    public static readonly DependencyProperty AlignmentYProperty =
        DependencyProperty.Register(nameof(AlignmentY), typeof(AlignmentY), typeof(TileBrush), new PropertyMetadata(AlignmentY.Center));
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(TileBrush), new PropertyMetadata(Stretch.Fill));
    public static readonly DependencyProperty TileModeProperty =
        DependencyProperty.Register(nameof(TileMode), typeof(TileMode), typeof(TileBrush), new PropertyMetadata(TileMode.None));
    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(nameof(Viewport), typeof(Rect), typeof(TileBrush), new PropertyMetadata(new Rect(0, 0, 1, 1)));
    public static readonly DependencyProperty ViewportUnitsProperty =
        DependencyProperty.Register(nameof(ViewportUnits), typeof(BrushMappingMode), typeof(TileBrush), new PropertyMetadata(BrushMappingMode.RelativeToBoundingBox));
    public static readonly DependencyProperty ViewboxProperty =
        DependencyProperty.Register(nameof(Viewbox), typeof(Rect), typeof(TileBrush), new PropertyMetadata(new Rect(0, 0, 1, 1)));
    public static readonly DependencyProperty ViewboxUnitsProperty =
        DependencyProperty.Register(nameof(ViewboxUnits), typeof(BrushMappingMode), typeof(TileBrush), new PropertyMetadata(BrushMappingMode.RelativeToBoundingBox));

    /// <summary>
    /// Gets or sets the horizontal alignment of the content within the brush.
    /// </summary>
    public AlignmentX AlignmentX
    {
        get => (AlignmentX)(GetValue(AlignmentXProperty) ?? AlignmentX.Center);
        set => SetValue(AlignmentXProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical alignment of the content within the brush.
    /// </summary>
    public AlignmentY AlignmentY
    {
        get => (AlignmentY)(GetValue(AlignmentYProperty) ?? AlignmentY.Center);
        set => SetValue(AlignmentYProperty, value);
    }

    /// <summary>
    /// Gets or sets how the content is stretched to fill the output area.
    /// </summary>
    public Stretch Stretch
    {
        get => (Stretch)(GetValue(StretchProperty) ?? Stretch.Fill);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets how the content is tiled when it is smaller than the output area.
    /// </summary>
    public TileMode TileMode
    {
        get => (TileMode)(GetValue(TileModeProperty) ?? TileMode.None);
        set => SetValue(TileModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the brush's viewport.
    /// </summary>
    public Rect Viewport
    {
        get => (Rect)(GetValue(ViewportProperty) ?? new Rect(0, 0, 1, 1));
        set => SetValue(ViewportProperty, value);
    }

    /// <summary>
    /// Gets or sets the coordinate system for the Viewport property.
    /// </summary>
    public BrushMappingMode ViewportUnits
    {
        get => (BrushMappingMode)(GetValue(ViewportUnitsProperty) ?? BrushMappingMode.RelativeToBoundingBox);
        set => SetValue(ViewportUnitsProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the content within the tile.
    /// </summary>
    public Rect Viewbox
    {
        get => (Rect)(GetValue(ViewboxProperty) ?? new Rect(0, 0, 1, 1));
        set => SetValue(ViewboxProperty, value);
    }

    /// <summary>
    /// Gets or sets the coordinate system for the Viewbox property.
    /// </summary>
    public BrushMappingMode ViewboxUnits
    {
        get => (BrushMappingMode)(GetValue(ViewboxUnitsProperty) ?? BrushMappingMode.RelativeToBoundingBox);
        set => SetValue(ViewboxUnitsProperty, value);
    }

    public new TileBrush Clone() => (TileBrush)base.Clone();
    public new TileBrush CloneCurrentValue() => (TileBrush)base.CloneCurrentValue();

    protected abstract void GetContentBounds(out Rect contentBounds);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, AlignmentXProperty)
            || ReferenceEquals(e.Property, AlignmentYProperty)
            || ReferenceEquals(e.Property, StretchProperty)
            || ReferenceEquals(e.Property, TileModeProperty)
            || ReferenceEquals(e.Property, ViewportProperty)
            || ReferenceEquals(e.Property, ViewportUnitsProperty)
            || ReferenceEquals(e.Property, ViewboxProperty)
            || ReferenceEquals(e.Property, ViewboxUnitsProperty))
        {
            WritePostscript();
        }
    }
}

/// <summary>
/// Paints an area with an image.
/// </summary>
public sealed class ImageBrush : TileBrush
{
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(ImageBrush), new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the image source.
    /// </summary>
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

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

    public new ImageBrush Clone() => (ImageBrush)base.Clone();
    public new ImageBrush CloneCurrentValue() => (ImageBrush)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new ImageBrush();

    protected override void GetContentBounds(out Rect contentBounds)
    {
        ImageSource? source = ImageSource;
        contentBounds = source is null ? Rect.Empty : new Rect(0, 0, source.Width, source.Height);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ImageSourceProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, ImageSourceProperty);
            WritePostscript();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ImageBrush({ImageSource})";
    public new string ToString(IFormatProvider? provider) => ToString();
}

/// <summary>
/// Paints an area with a Drawing.
/// </summary>
public sealed class DrawingBrush : TileBrush
{
    public static readonly DependencyProperty DrawingProperty =
        DependencyProperty.Register(nameof(Drawing), typeof(Drawing), typeof(DrawingBrush), new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the Drawing that defines the content of this brush.
    /// </summary>
    public Drawing? Drawing
    {
        get => (Drawing?)GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

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

    public new DrawingBrush Clone() => (DrawingBrush)base.Clone();
    public new DrawingBrush CloneCurrentValue() => (DrawingBrush)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new DrawingBrush();
    protected override void GetContentBounds(out Rect contentBounds) => contentBounds = Drawing?.Bounds ?? Rect.Empty;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DrawingProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, DrawingProperty);
            WritePostscript();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"DrawingBrush({Drawing})";
    public new string ToString(IFormatProvider? provider) => ToString();
}

/// <summary>
/// Paints an area with a visual element.
/// </summary>
public sealed class VisualBrush : TileBrush
{
    public static readonly DependencyProperty VisualProperty =
        DependencyProperty.Register(nameof(Visual), typeof(Visual), typeof(VisualBrush), new PropertyMetadata(null));
    public static readonly DependencyProperty AutoLayoutContentProperty =
        DependencyProperty.Register(nameof(AutoLayoutContent), typeof(bool), typeof(VisualBrush), new PropertyMetadata(true));

    /// <summary>
    /// Gets or sets the visual that provides the content for the brush.
    /// </summary>
    public Visual? Visual
    {
        get => (Visual?)GetValue(VisualProperty);
        set => SetValue(VisualProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether content is laid out automatically.
    /// </summary>
    public bool AutoLayoutContent
    {
        get => (bool)(GetValue(AutoLayoutContentProperty) ?? true);
        set => SetValue(AutoLayoutContentProperty, value);
    }

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
    public VisualBrush(Visual visual)
    {
        Visual = visual;
    }

    public new VisualBrush Clone() => (VisualBrush)base.Clone();
    public new VisualBrush CloneCurrentValue() => (VisualBrush)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new VisualBrush();

    protected override void GetContentBounds(out Rect contentBounds)
    {
        contentBounds = Visual is UIElement element
            ? new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height)
            : Rect.Empty;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, VisualProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, VisualProperty);
        }

        if (ReferenceEquals(e.Property, VisualProperty) || ReferenceEquals(e.Property, AutoLayoutContentProperty))
        {
            WritePostscript();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"VisualBrush({Visual})";
    public new string ToString(IFormatProvider? provider) => ToString();
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
    None = 0,

    /// <summary>
    /// The content is tiled.
    /// </summary>
    Tile = 4,

    /// <summary>
    /// The content is flipped horizontally on alternate columns.
    /// </summary>
    FlipX = 1,

    /// <summary>
    /// The content is flipped vertically on alternate rows.
    /// </summary>
    FlipY = 2,

    /// <summary>
    /// The content is flipped both horizontally and vertically on alternating tiles.
    /// </summary>
    FlipXY = 3
}
