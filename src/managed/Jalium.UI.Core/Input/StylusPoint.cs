using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Jalium.UI.Input;

/// <summary>Represents one packet sampled from a stylus device.</summary>
public struct StylusPoint : IEquatable<StylusPoint>
{
    public const float DefaultPressure = 0.5f;
    public static readonly double MaxXY = 81164736.28346430d;
    public static readonly double MinXY = -81164736.32125960d;

    private double _x;
    private double _y;
    private float _pressureFactor;
    private StylusPointDescription? _description;
    private int[]? _additionalValues;

    public StylusPoint(double x, double y)
        : this(x, y, DefaultPressure, null, null, validateAdditionalData: false)
    {
    }

    public StylusPoint(double x, double y, float pressureFactor)
        : this(x, y, pressureFactor, null, null, validateAdditionalData: false)
    {
    }

    public StylusPoint(
        double x,
        double y,
        float pressureFactor,
        StylusPointDescription stylusPointDescription,
        int[]? additionalValues)
        : this(x, y, pressureFactor, stylusPointDescription, additionalValues, validateAdditionalData: true)
    {
    }

    private StylusPoint(
        double x,
        double y,
        float pressureFactor,
        StylusPointDescription? stylusPointDescription,
        int[]? additionalValues,
        bool validateAdditionalData)
    {
        if (double.IsNaN(x))
            throw new ArgumentOutOfRangeException(nameof(x));
        if (double.IsNaN(y))
            throw new ArgumentOutOfRangeException(nameof(y));
        if (pressureFactor < 0f || pressureFactor > 1f)
            throw new ArgumentOutOfRangeException(nameof(pressureFactor));

        _x = ClampCoordinate(x);
        _y = ClampCoordinate(y);
        _pressureFactor = pressureFactor;
        _description = stylusPointDescription;
        _additionalValues = additionalValues;

        if (!validateAdditionalData)
            return;

        ArgumentNullException.ThrowIfNull(stylusPointDescription);
        int extraPropertyCount = stylusPointDescription.PropertyCount - StylusPointDescription.RequiredPropertyCount;
        if (extraPropertyCount > 0)
            ArgumentNullException.ThrowIfNull(additionalValues);
        if (additionalValues is not null && additionalValues.Length != extraPropertyCount)
            throw new ArgumentException("The additional values do not match the stylus point description.", nameof(additionalValues));

        if (additionalValues is not null)
        {
            _additionalValues = stylusPointDescription.CreateAdditionalDataBuffer();
            ReadOnlyCollection<StylusPointPropertyInfo> properties = stylusPointDescription.GetStylusPointProperties();
            for (int propertyIndex = StylusPointDescription.RequiredPropertyCount, valueIndex = 0;
                 propertyIndex < properties.Count;
                 propertyIndex++, valueIndex++)
            {
                SetPropertyValueCore(properties[propertyIndex], additionalValues[valueIndex], copyBeforeWrite: false);
            }
        }
    }

    public double X
    {
        readonly get => _x;
        set
        {
            if (double.IsNaN(value))
                throw new ArgumentOutOfRangeException(nameof(X));
            _x = ClampCoordinate(value);
        }
    }

    public double Y
    {
        readonly get => _y;
        set
        {
            if (double.IsNaN(value))
                throw new ArgumentOutOfRangeException(nameof(Y));
            _y = ClampCoordinate(value);
        }
    }

    public float PressureFactor
    {
        readonly get => Math.Clamp(_pressureFactor, 0f, 1f);
        set
        {
            if (value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(PressureFactor));
            _pressureFactor = value;
        }
    }

    public StylusPointDescription Description
    {
        get => _description ??= new StylusPointDescription();
        internal set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!StylusPointDescription.AreCompatible(Description, value))
                throw new ArgumentException("The stylus point descriptions are not compatible.", nameof(value));
            _description = value;
        }
    }

    public readonly bool HasProperty(StylusPointProperty stylusPointProperty)
    {
        ArgumentNullException.ThrowIfNull(stylusPointProperty);
        StylusPoint copy = this;
        return copy.Description.HasProperty(stylusPointProperty);
    }

    public readonly int GetPropertyValue(StylusPointProperty stylusPointProperty)
    {
        ArgumentNullException.ThrowIfNull(stylusPointProperty);
        if (stylusPointProperty.Id == StylusPointProperties.X.Id)
            return (int)_x;
        if (stylusPointProperty.Id == StylusPointProperties.Y.Id)
            return (int)_y;

        StylusPoint copy = this;
        StylusPointDescription description = copy.Description;
        if (stylusPointProperty.Id == StylusPointProperties.NormalPressure.Id)
        {
            StylusPointPropertyInfo pressure = description.GetPropertyInfo(StylusPointProperties.NormalPressure);
            return (int)(_pressureFactor * pressure.Maximum);
        }

        int propertyIndex = description.GetPropertyIndex(stylusPointProperty.Id);
        if (propertyIndex < 0)
            throw new ArgumentException("The stylus point does not contain that property.", nameof(stylusPointProperty));

        int[] additionalValues = _additionalValues ?? Array.Empty<int>();
        if (stylusPointProperty.IsButton)
        {
            int bitPosition = description.GetButtonBitPosition(stylusPointProperty);
            int packedButtons = additionalValues.Length == 0 ? 0 : additionalValues[^1];
            return (packedButtons & (1 << bitPosition)) == 0 ? 0 : 1;
        }

        return additionalValues[propertyIndex - StylusPointDescription.RequiredPropertyCount];
    }

    public void SetPropertyValue(StylusPointProperty stylusPointProperty, int value)
        => SetPropertyValueCore(stylusPointProperty, value, copyBeforeWrite: true);

    private void SetPropertyValueCore(StylusPointProperty stylusPointProperty, int value, bool copyBeforeWrite)
    {
        ArgumentNullException.ThrowIfNull(stylusPointProperty);
        if (stylusPointProperty.Id == StylusPointProperties.X.Id)
        {
            _x = ClampCoordinate(value);
            return;
        }
        if (stylusPointProperty.Id == StylusPointProperties.Y.Id)
        {
            _y = ClampCoordinate(value);
            return;
        }

        StylusPointDescription description = Description;
        if (stylusPointProperty.Id == StylusPointProperties.NormalPressure.Id)
        {
            StylusPointPropertyInfo pressure = description.GetPropertyInfo(StylusPointProperties.NormalPressure);
            _pressureFactor = pressure.Maximum == 0
                ? 0f
                : (float)(pressure.Minimum + value) / pressure.Maximum;
            return;
        }

        int propertyIndex = description.GetPropertyIndex(stylusPointProperty.Id);
        if (propertyIndex < 0)
            throw new ArgumentException("The stylus point does not contain that property.", nameof(stylusPointProperty));

        if (copyBeforeWrite && _additionalValues is not null)
            _additionalValues = (int[])_additionalValues.Clone();
        _additionalValues ??= description.CreateAdditionalDataBuffer();

        if (stylusPointProperty.IsButton)
        {
            if (value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value));
            int bit = 1 << description.GetButtonBitPosition(stylusPointProperty);
            if (value == 0)
                _additionalValues[^1] &= ~bit;
            else
                _additionalValues[^1] |= bit;
        }
        else
        {
            _additionalValues[propertyIndex - StylusPointDescription.RequiredPropertyCount] = value;
        }
    }

    internal readonly int[] GetAdditionalData()
        => _additionalValues is null ? Array.Empty<int>() : (int[])_additionalValues.Clone();

    internal readonly int[] GetUnpackedAdditionalValues(StylusPointDescription description)
    {
        ReadOnlyCollection<StylusPointPropertyInfo> properties = description.GetStylusPointProperties();
        int[] values = new int[properties.Count - StylusPointDescription.RequiredPropertyCount];
        for (int index = StylusPointDescription.RequiredPropertyCount; index < properties.Count; index++)
            values[index - StylusPointDescription.RequiredPropertyCount] = GetPropertyValue(properties[index]);
        return values;
    }

    public readonly Point ToPoint() => new(_x, _y);
    public static explicit operator Point(StylusPoint stylusPoint) => stylusPoint.ToPoint();
    public static StylusPoint FromPoint(Point point) => new(point.X, point.Y);

    public static bool Equals(StylusPoint stylusPoint1, StylusPoint stylusPoint2)
    {
        if (stylusPoint1._x != stylusPoint2._x ||
            stylusPoint1._y != stylusPoint2._y ||
            stylusPoint1._pressureFactor != stylusPoint2._pressureFactor)
        {
            return false;
        }

        if (stylusPoint1._additionalValues is null && stylusPoint2._additionalValues is null)
            return true;
        if (stylusPoint1._additionalValues is null || stylusPoint2._additionalValues is null)
            return false;
        if (!StylusPointDescription.AreCompatible(stylusPoint1.Description, stylusPoint2.Description))
            return false;
        return stylusPoint1._additionalValues.AsSpan().SequenceEqual(stylusPoint2._additionalValues);
    }

    public readonly bool Equals(StylusPoint other) => Equals(this, other);
    public override readonly bool Equals(object? obj) => obj is StylusPoint other && Equals(other);
    public override readonly int GetHashCode() => _x.GetHashCode() ^ _y.GetHashCode() ^ _pressureFactor.GetHashCode();
    public static bool operator ==(StylusPoint left, StylusPoint right) => Equals(left, right);
    public static bool operator !=(StylusPoint left, StylusPoint right) => !Equals(left, right);
    public override readonly string ToString() => $"{_x},{_y},{PressureFactor}";

    private static double ClampCoordinate(double value) => Math.Clamp(value, MinXY, MaxXY);
}

/// <summary>A mutable collection of stylus packets that share one packet description.</summary>
public partial class StylusPointCollection : Collection<StylusPoint>
{
    private StylusPointDescription _description;

    public StylusPointCollection()
        : this(new StylusPointDescription())
    {
    }

    public StylusPointCollection(int initialCapacity)
        : this(new StylusPointDescription(), initialCapacity)
    {
    }

    public StylusPointCollection(StylusPointDescription stylusPointDescription)
    {
        _description = stylusPointDescription ?? throw new ArgumentNullException(nameof(stylusPointDescription));
    }

    public StylusPointCollection(StylusPointDescription stylusPointDescription, int initialCapacity)
        : this(stylusPointDescription)
    {
        if (initialCapacity < 0)
            throw new ArgumentException("Capacity cannot be negative.", nameof(initialCapacity));
        ((List<StylusPoint>)Items).Capacity = initialCapacity;
    }

    public StylusPointCollection(IEnumerable<StylusPoint> stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        List<StylusPoint> points = new(stylusPoints);
        if (points.Count == 0)
            throw new ArgumentException("The collection must contain at least one point.", nameof(stylusPoints));
        _description = points[0].Description;
        ((List<StylusPoint>)Items).Capacity = points.Count;
        foreach (StylusPoint point in points)
            Add(point);
    }

    public StylusPointCollection(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        List<StylusPoint> converted = points.Select(static point => new StylusPoint(point.X, point.Y)).ToList();
        if (converted.Count == 0)
            throw new ArgumentException("The collection must contain at least one point.", nameof(points));
        _description = new StylusPointDescription();
        ((List<StylusPoint>)Items).AddRange(converted);
    }

    public event EventHandler? Changed;
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    internal event CancelEventHandler? CountGoingToZero;

    public StylusPointDescription Description => _description;

    public void Add(Point point) => Add(new StylusPoint(point.X, point.Y));

    public void Add(StylusPointCollection stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        if (!StylusPointDescription.AreCompatible(stylusPoints.Description, Description))
            throw new ArgumentException("The stylus point descriptions are not compatible.", nameof(stylusPoints));

        int count = stylusPoints.Count;
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            StylusPoint point = stylusPoints[index];
            point.Description = Description;
            ((List<StylusPoint>)Items).Add(point);
        }
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void AddRange(IEnumerable<StylusPoint> stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        List<StylusPoint> points = stylusPoints.ToList();
        if (points.Count == 0)
            return;
        if (points.Any(point => !StylusPointDescription.AreCompatible(point.Description, Description)))
            throw new ArgumentException("The stylus point descriptions are not compatible.", nameof(stylusPoints));
        foreach (StylusPoint sourcePoint in points)
        {
            StylusPoint point = sourcePoint;
            point.Description = Description;
            ((List<StylusPoint>)Items).Add(point);
        }
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected sealed override void InsertItem(int index, StylusPoint item)
    {
        if (!StylusPointDescription.AreCompatible(item.Description, Description))
            throw new ArgumentException("The stylus point descriptions are not compatible.", nameof(item));
        item.Description = Description;
        base.InsertItem(index, item);
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    protected sealed override void SetItem(int index, StylusPoint item)
    {
        if (!StylusPointDescription.AreCompatible(item.Description, Description))
            throw new ArgumentException("The stylus point descriptions are not compatible.", nameof(item));
        item.Description = Description;
        StylusPoint oldItem = this[index];
        base.SetItem(index, item);
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, index));
    }

    protected sealed override void RemoveItem(int index)
    {
        if (Count == 1 && !CanGoToZero())
            throw new InvalidOperationException("A listener prevented the collection from becoming empty.");
        StylusPoint oldItem = this[index];
        base.RemoveItem(index);
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, index));
    }

    protected sealed override void ClearItems()
    {
        if (Count == 0)
            return;
        if (!CanGoToZero())
            throw new InvalidOperationException("A listener prevented the collection from becoming empty.");
        base.ClearItems();
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected virtual void OnChanged(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Changed?.Invoke(this, e);
    }

    public StylusPointCollection Clone()
    {
        StylusPointCollection clone = new(Description, Count);
        foreach (StylusPoint point in this)
            ((List<StylusPoint>)clone.Items).Add(point);
        return clone;
    }

    public StylusPointCollection Reformat(StylusPointDescription subsetToReformatTo)
    {
        ArgumentNullException.ThrowIfNull(subsetToReformatTo);
        if (!subsetToReformatTo.IsSubsetOf(Description))
            throw new ArgumentException("The requested description is not a subset of this collection.", nameof(subsetToReformatTo));

        StylusPointDescription targetDescription =
            StylusPointDescription.GetCommonDescription(Description, subsetToReformatTo);
        StylusPointCollection reformatted = new(targetDescription, Count);
        foreach (StylusPoint point in this)
        {
            int[] values = point.GetUnpackedAdditionalValues(targetDescription);
            StylusPoint converted = new(
                point.X,
                point.Y,
                point.PressureFactor,
                targetDescription,
                values.Length == 0 ? null : values);
            ((List<StylusPoint>)reformatted.Items).Add(converted);
        }
        return reformatted;
    }

    public int[] ToHiMetricArray()
    {
        const double AvalonToHiMetric = 2540d / 96d;
        int valuesPerPoint = Description.OutputValueCount;
        int[] result = new int[Count * valuesPerPoint];
        for (int pointIndex = 0; pointIndex < Count; pointIndex++)
        {
            int offset = pointIndex * valuesPerPoint;
            StylusPoint point = this[pointIndex];
            result[offset] = (int)Math.Round(point.X * AvalonToHiMetric);
            result[offset + 1] = (int)Math.Round(point.Y * AvalonToHiMetric);
            result[offset + 2] = point.GetPropertyValue(StylusPointProperties.NormalPressure);
            int[] additional = point.GetAdditionalData();
            Array.Copy(additional, 0, result, offset + StylusPointDescription.RequiredPropertyCount, additional.Length);
        }
        return result;
    }

    public static explicit operator Point[]?(StylusPointCollection? stylusPoints)
    {
        if (stylusPoints is null)
            return null;
        Point[] points = new Point[stylusPoints.Count];
        for (int index = 0; index < points.Length; index++)
            points[index] = stylusPoints[index].ToPoint();
        return points;
    }

    public Rect GetBounds()
    {
        if (Count == 0)
            return Rect.Empty;
        double minX = this.Min(static point => point.X);
        double minY = this.Min(static point => point.Y);
        double maxX = this.Max(static point => point.X);
        double maxY = this.Max(static point => point.Y);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public void Transform(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        for (int index = 0; index < Count; index++)
        {
            StylusPoint point = this[index];
            double x = point.X;
            double y = point.Y;
            point.X = x * m11 + y * m21 + offsetX;
            point.Y = x * m12 + y * m22 + offsetY;
            ((List<StylusPoint>)Items)[index] = point;
        }
        if (Count > 0)
            RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void NotifyReset() => RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    private void RaiseChanged(NotifyCollectionChangedEventArgs args)
    {
        OnChanged(EventArgs.Empty);
        CollectionChanged?.Invoke(this, args);
    }

    private bool CanGoToZero()
    {
        if (CountGoingToZero is null)
            return true;
        CancelEventArgs args = new();
        CountGoingToZero(this, args);
        return !args.Cancel;
    }
}
