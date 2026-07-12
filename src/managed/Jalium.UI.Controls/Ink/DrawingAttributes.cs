using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Ink;

/// <summary>
/// Specifies the appearance of a <see cref="Stroke"/> when rendered.
/// </summary>
public class DrawingAttributes : INotifyPropertyChanged
{
    internal const double DefaultWidth = 2.0031496062992127;
    internal const double DefaultHeight = 2.0031496062992127;

    /// <summary>The minimum legal stylus-tip height.</summary>
    public static readonly double MinHeight = 0.00003779527559055120;

    /// <summary>The minimum legal stylus-tip width.</summary>
    public static readonly double MinWidth = 0.00003779527559055120;

    /// <summary>The maximum legal stylus-tip height.</summary>
    public static readonly double MaxHeight = 162329.4614173230;

    /// <summary>The maximum legal stylus-tip width.</summary>
    public static readonly double MaxWidth = 162329.4614173230;

    private static readonly Guid s_brushTypePropertyId =
        new("de48cb4e-5334-4a55-a6c0-4a670af75643");
    private static readonly Guid s_brushShaderPropertyId =
        new("b48bd4ad-6879-4180-a7d6-d332587ceb55");

    private readonly Dictionary<Guid, object> _propertyData = new();
    private Color _color = Colors.Black;
    private double _width = DefaultWidth;
    private double _height = DefaultHeight;
    private StylusTip _stylusTip = StylusTip.Ellipse;
    private Matrix _stylusTipTransform = Matrix.Identity;
    private bool _isHighlighter;
    private bool _fitToCurve;
    private bool _ignorePressure;
    private BrushType _brushType = BrushType.Round;
    private BrushShader? _brushShader;

    /// <summary>Occurs when a CLR property value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Occurs when a built-in drawing attribute changes.</summary>
    public event PropertyDataChangedEventHandler? AttributeChanged;

    /// <summary>Occurs when custom property data changes.</summary>
    public event PropertyDataChangedEventHandler? PropertyDataChanged;

    /// <summary>Gets or sets the color of the stroke.</summary>
    public Color Color
    {
        get => _color;
        set => SetAttribute(
            DrawingAttributeIds.Color,
            ref _color,
            value,
            Colors.Black);
    }

    /// <summary>Gets or sets the width of the stroke.</summary>
    public double Width
    {
        get => _width;
        set
        {
            ValidateDimension(value, MinWidth, MaxWidth, nameof(Width));
            SetAttribute(
                DrawingAttributeIds.StylusWidth,
                ref _width,
                value,
                DefaultWidth);
        }
    }

    /// <summary>Gets or sets the height of the stroke.</summary>
    public double Height
    {
        get => _height;
        set
        {
            ValidateDimension(value, MinHeight, MaxHeight, nameof(Height));
            SetAttribute(
                DrawingAttributeIds.StylusHeight,
                ref _height,
                value,
                DefaultHeight);
        }
    }

    /// <summary>Gets or sets the shape of the stylus tip.</summary>
    public StylusTip StylusTip
    {
        get => _stylusTip;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentException("The stylus tip value is invalid.", nameof(value));

            SetAttribute(
                DrawingAttributeIds.StylusTip,
                ref _stylusTip,
                value,
                StylusTip.Ellipse);
        }
    }

    /// <summary>Gets or sets the linear transform applied to the stylus tip.</summary>
    public Matrix StylusTipTransform
    {
        get => _stylusTipTransform;
        set
        {
            if (value.OffsetX != 0 || value.OffsetY != 0 ||
                !IsFinite(value) || !value.HasInverse)
            {
                throw new ArgumentException(
                    "The stylus tip transform must be finite, invertible, and have no translation.",
                    nameof(value));
            }

            SetAttribute(
                DrawingAttributeIds.StylusTipTransform,
                ref _stylusTipTransform,
                value,
                Matrix.Identity);
        }
    }

    /// <summary>Gets or sets whether the stroke uses highlighter rendering.</summary>
    public bool IsHighlighter
    {
        get => _isHighlighter;
        set => SetAttribute(
            DrawingAttributeIds.IsHighlighter,
            ref _isHighlighter,
            value,
            false);
    }

    /// <summary>Gets or sets whether Bezier smoothing is used.</summary>
    public bool FitToCurve
    {
        get => _fitToCurve;
        set => SetDrawingFlag(DrawingFlags.FitToCurve, value);
    }

    /// <summary>Gets or sets whether packet pressure is ignored.</summary>
    public bool IgnorePressure
    {
        get => _ignorePressure;
        set => SetDrawingFlag(DrawingFlags.IgnorePressure, value);
    }

    /// <summary>Gets or sets Jalium's extended brush style.</summary>
    public BrushType BrushType
    {
        get => _brushType;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentException("The brush type is invalid.", nameof(value));
            if (_brushType == value)
                return;

            BrushType previous = _brushType;
            _brushType = value;
            OnAttributeChanged(new PropertyDataChangedEventArgs(
                s_brushTypePropertyId,
                value,
                previous));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(BrushType)));
        }
    }

    /// <summary>Gets or sets an optional Jalium pixel-shader brush override.</summary>
    public BrushShader? BrushShader
    {
        get => _brushShader;
        set
        {
            if (ReferenceEquals(_brushShader, value))
                return;

            BrushShader? previous = _brushShader;
            _brushShader = value;
            OnAttributeChanged(new PropertyDataChangedEventArgs(
                s_brushShaderPropertyId,
                value,
                previous));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(BrushShader)));
        }
    }

    /// <summary>Adds or replaces a built-in attribute or custom property value.</summary>
    public void AddPropertyData(Guid propertyDataId, object propertyData)
    {
        if (propertyDataId == Guid.Empty)
            throw new ArgumentException("The property identifier cannot be empty.", nameof(propertyDataId));
        ArgumentNullException.ThrowIfNull(propertyData);

        if (propertyDataId == DrawingAttributeIds.Color)
        {
            Color = RequireValue<Color>(propertyData, propertyDataId);
            return;
        }
        if (propertyDataId == DrawingAttributeIds.StylusWidth)
        {
            Width = RequireValue<double>(propertyData, propertyDataId);
            return;
        }
        if (propertyDataId == DrawingAttributeIds.StylusHeight)
        {
            Height = RequireValue<double>(propertyData, propertyDataId);
            return;
        }
        if (propertyDataId == DrawingAttributeIds.StylusTip)
        {
            StylusTip = RequireValue<StylusTip>(propertyData, propertyDataId);
            return;
        }
        if (propertyDataId == DrawingAttributeIds.StylusTipTransform)
        {
            StylusTipTransform = RequireValue<Matrix>(propertyData, propertyDataId);
            return;
        }
        if (propertyDataId == DrawingAttributeIds.IsHighlighter)
        {
            IsHighlighter = RequireValue<bool>(propertyData, propertyDataId);
            return;
        }
        if (propertyDataId == DrawingAttributeIds.DrawingFlags)
        {
            DrawingFlags flags = RequireValue<DrawingFlags>(propertyData, propertyDataId);
            SetDrawingFlags(flags);
            return;
        }

        InkPropertyData.Validate(propertyDataId, propertyData);
        SetCustomPropertyData(propertyDataId, propertyData);
    }

    /// <summary>Removes property data, restoring built-in attributes to their defaults.</summary>
    public void RemovePropertyData(Guid propertyDataId)
    {
        if (!_propertyData.ContainsKey(propertyDataId))
            throw new ArgumentException("The property identifier was not found.", nameof(propertyDataId));

        if (propertyDataId == DrawingAttributeIds.Color) { Color = Colors.Black; return; }
        if (propertyDataId == DrawingAttributeIds.StylusWidth) { Width = DefaultWidth; return; }
        if (propertyDataId == DrawingAttributeIds.StylusHeight) { Height = DefaultHeight; return; }
        if (propertyDataId == DrawingAttributeIds.StylusTip) { StylusTip = StylusTip.Ellipse; return; }
        if (propertyDataId == DrawingAttributeIds.StylusTipTransform) { StylusTipTransform = Matrix.Identity; return; }
        if (propertyDataId == DrawingAttributeIds.IsHighlighter) { IsHighlighter = false; return; }
        if (propertyDataId == DrawingAttributeIds.DrawingFlags) { SetDrawingFlags(DrawingFlags.AntiAliased); return; }

        object previous = _propertyData[propertyDataId];
        _propertyData.Remove(propertyDataId);
        OnPropertyDataChanged(new PropertyDataChangedEventArgs(propertyDataId, null, previous));
    }

    /// <summary>Gets a built-in attribute or custom property value.</summary>
    public object GetPropertyData(Guid propertyDataId)
    {
        if (_propertyData.TryGetValue(propertyDataId, out object? value))
            return value;

        if (propertyDataId == DrawingAttributeIds.Color) return Colors.Black;
        if (propertyDataId == DrawingAttributeIds.StylusWidth) return DefaultWidth;
        if (propertyDataId == DrawingAttributeIds.StylusHeight) return DefaultHeight;
        if (propertyDataId == DrawingAttributeIds.StylusTip) return StylusTip.Ellipse;
        if (propertyDataId == DrawingAttributeIds.StylusTipTransform) return Matrix.Identity;
        if (propertyDataId == DrawingAttributeIds.IsHighlighter) return false;
        if (propertyDataId == DrawingAttributeIds.DrawingFlags) return DrawingFlags.AntiAliased;

        throw new ArgumentException("The property identifier was not found.", nameof(propertyDataId));
    }

    /// <summary>Gets the identifiers whose values differ from defaults.</summary>
    public Guid[] GetPropertyDataIds() => _propertyData.Keys.ToArray();

    /// <summary>Returns whether the identifier has an explicitly stored value.</summary>
    public bool ContainsPropertyData(Guid propertyDataId) => _propertyData.ContainsKey(propertyDataId);

    /// <summary>Creates a deep value copy without copying event subscribers.</summary>
    public virtual DrawingAttributes Clone()
    {
        var clone = new DrawingAttributes
        {
            _color = _color,
            _width = _width,
            _height = _height,
            _stylusTip = _stylusTip,
            _stylusTipTransform = _stylusTipTransform,
            _isHighlighter = _isHighlighter,
            _fitToCurve = _fitToCurve,
            _ignorePressure = _ignorePressure,
            _brushType = _brushType,
            _brushShader = _brushShader,
        };

        foreach ((Guid id, object value) in _propertyData)
            clone._propertyData.Add(id, InkPropertyData.CloneValue(value));

        return clone;
    }

    /// <inheritdoc />
    public override bool Equals(object? o)
    {
        if (o is not DrawingAttributes other || o.GetType() != GetType())
            return false;

        return _color == other._color &&
            _width.Equals(other._width) &&
            _height.Equals(other._height) &&
            _stylusTip == other._stylusTip &&
            _stylusTipTransform == other._stylusTipTransform &&
            _isHighlighter == other._isHighlighter &&
            _fitToCurve == other._fitToCurve &&
            _ignorePressure == other._ignorePressure &&
            _brushType == other._brushType &&
            ReferenceEquals(_brushShader, other._brushShader) &&
            InkPropertyData.DictionariesEqual(_propertyData, other._propertyData);
    }

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    public static bool operator ==(DrawingAttributes? first, DrawingAttributes? second) =>
        ReferenceEquals(first, second) || (first is not null && first.Equals(second));

    public static bool operator !=(DrawingAttributes? first, DrawingAttributes? second) => !(first == second);

    /// <summary>Raises the built-in attribute changed event.</summary>
    protected virtual void OnAttributeChanged(PropertyDataChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        NotifyAttributePropertyChanged(e);
        AttributeChanged?.Invoke(this, e);
    }

    /// <summary>Raises the custom property-data changed event.</summary>
    protected virtual void OnPropertyDataChanged(PropertyDataChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        PropertyDataChanged?.Invoke(this, e);
    }

    /// <summary>Raises the CLR property changed event.</summary>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        PropertyChanged?.Invoke(this, e);
    }

    internal IReadOnlyDictionary<Guid, object> PropertyData => _propertyData;

    internal void LoadCustomPropertyData(Guid id, object value) => _propertyData[id] = value;

    private void SetDrawingFlag(DrawingFlags flag, bool value)
    {
        bool previous = flag == DrawingFlags.FitToCurve ? _fitToCurve : _ignorePressure;
        if (previous == value)
            return;

        DrawingFlags oldFlags = GetDrawingFlags();
        if (flag == DrawingFlags.FitToCurve)
            _fitToCurve = value;
        else
            _ignorePressure = value;
        DrawingFlags newFlags = GetDrawingFlags();

        StoreExplicit(DrawingAttributeIds.DrawingFlags, newFlags, DrawingFlags.AntiAliased);
        OnAttributeChanged(new PropertyDataChangedEventArgs(
            DrawingAttributeIds.DrawingFlags,
            newFlags,
            oldFlags));
    }

    private void SetDrawingFlags(DrawingFlags flags)
    {
        const DrawingFlags supported = DrawingFlags.AntiAliased |
            DrawingFlags.FitToCurve |
            DrawingFlags.IgnorePressure;
        if ((flags & ~supported) != 0)
            throw new ArgumentException("The drawing flags value is invalid.", nameof(flags));

        bool fit = (flags & DrawingFlags.FitToCurve) != 0;
        bool ignore = (flags & DrawingFlags.IgnorePressure) != 0;
        if (_fitToCurve == fit && _ignorePressure == ignore)
            return;

        DrawingFlags oldFlags = GetDrawingFlags();
        _fitToCurve = fit;
        _ignorePressure = ignore;

        DrawingFlags newFlags = GetDrawingFlags();
        StoreExplicit(DrawingAttributeIds.DrawingFlags, newFlags, DrawingFlags.AntiAliased);
        OnAttributeChanged(new PropertyDataChangedEventArgs(
            DrawingAttributeIds.DrawingFlags,
            newFlags,
            oldFlags));
    }

    private DrawingFlags GetDrawingFlags()
    {
        DrawingFlags flags = DrawingFlags.AntiAliased;
        if (_fitToCurve) flags |= DrawingFlags.FitToCurve;
        if (_ignorePressure) flags |= DrawingFlags.IgnorePressure;
        return flags;
    }

    private void SetCustomPropertyData(Guid id, object value)
    {
        if (_propertyData.TryGetValue(id, out object? previous) &&
            InkPropertyData.ValuesEqual(previous, value))
        {
            return;
        }

        object stored = InkPropertyData.CloneValue(value);
        _propertyData[id] = stored;
        OnPropertyDataChanged(new PropertyDataChangedEventArgs(id, stored, previous));
    }

    private void SetAttribute<T>(Guid id, ref T field, T value, T defaultValue)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        T previous = field;
        field = value;
        StoreExplicit(id, value!, defaultValue!);
        OnAttributeChanged(new PropertyDataChangedEventArgs(id, value, previous));
    }

    private void NotifyAttributePropertyChanged(PropertyDataChangedEventArgs e)
    {
        string? propertyName = e.PropertyGuid == DrawingAttributeIds.Color ? nameof(Color) :
            e.PropertyGuid == DrawingAttributeIds.StylusWidth ? nameof(Width) :
            e.PropertyGuid == DrawingAttributeIds.StylusHeight ? nameof(Height) :
            e.PropertyGuid == DrawingAttributeIds.StylusTip ? nameof(StylusTip) :
            e.PropertyGuid == DrawingAttributeIds.StylusTipTransform ? nameof(StylusTipTransform) :
            e.PropertyGuid == DrawingAttributeIds.IsHighlighter ? nameof(IsHighlighter) :
            null;
        if (propertyName is not null)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
            return;
        }

        if (e.PropertyGuid != DrawingAttributeIds.DrawingFlags ||
            e.NewValue is not DrawingFlags newFlags ||
            e.PreviousValue is not DrawingFlags previousFlags)
        {
            return;
        }

        DrawingFlags changed = newFlags ^ previousFlags;
        if ((changed & DrawingFlags.FitToCurve) != 0)
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(FitToCurve)));
        if ((changed & DrawingFlags.IgnorePressure) != 0)
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IgnorePressure)));
    }

    private void StoreExplicit(Guid id, object value, object defaultValue)
    {
        if (value.Equals(defaultValue))
            _propertyData.Remove(id);
        else
            _propertyData[id] = value;
    }

    private static T RequireValue<T>(object value, Guid id)
    {
        if (value is T typed)
            return typed;
        throw new ArgumentException(
            $"Property '{id}' requires a value of type {typeof(T).FullName}.",
            nameof(value));
    }

    private static bool IsFinite(Matrix matrix) =>
        double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12) &&
        double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) &&
        double.IsFinite(matrix.OffsetX) && double.IsFinite(matrix.OffsetY);

    private static void ValidateDimension(double value, double minimum, double maximum, string propertyName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(propertyName);
    }
}

/// <summary>Identifiers for the standard properties stored by <see cref="DrawingAttributes"/>.</summary>
public static class DrawingAttributeIds
{
    public static readonly Guid Color = new("5329cda5-fa5b-4ed2-bb32-834601724428");
    public static readonly Guid StylusTip = new("3526c731-ee79-4988-b93e-70d92f8907ed");
    public static readonly Guid StylusTipTransform = new("4b63bc16-7bc4-4fd2-95da-acff4775732d");
    public static readonly Guid StylusHeight = new("9d32b7ca-1213-4f54-b7e4-c9050ee17a38");
    public static readonly Guid StylusWidth = new("002df9af-dd8c-4949-ba46-d65e107d1a8a");
    public static readonly Guid DrawingFlags = new("5c0b730a-f394-4961-a933-37c434f4b7eb");
    public static readonly Guid IsHighlighter = new("ce305e1a-0e08-45e3-8cdc-e40bb4506f21");
}

[Flags]
internal enum DrawingFlags
{
    None = 0,
    AntiAliased = 1,
    FitToCurve = 2,
    IgnorePressure = 4,
}
