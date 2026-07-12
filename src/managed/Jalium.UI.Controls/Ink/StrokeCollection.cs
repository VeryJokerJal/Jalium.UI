using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Ink;

/// <summary>Represents an observable collection of unique <see cref="Stroke"/> objects.</summary>
[TypeConverter(typeof(Jalium.UI.StrokeCollectionConverter))]
public class StrokeCollection : ObservableCollection<Stroke>
{
    /// <summary>The clipboard format name used for persisted ink.</summary>
    public static readonly string InkSerializedFormat = "Ink Serialized Format";

    private readonly Dictionary<Guid, object> _propertyData = new();
    private int _deferNotifications;

    /// <summary>Initializes an empty collection.</summary>
    public StrokeCollection()
    {
    }

    /// <summary>Initializes a collection with unique stroke references.</summary>
    public StrokeCollection(IEnumerable<Stroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        _deferNotifications++;
        try
        {
            foreach (Stroke stroke in strokes)
                Add(stroke);
        }
        catch
        {
            foreach (Stroke stroke in this)
                stroke.Invalidated -= OnStrokeInvalidated;
            Items.Clear();
            throw;
        }
        finally
        {
            _deferNotifications--;
        }
    }

    /// <summary>Initializes a collection from its persisted binary representation.</summary>
    public StrokeCollection(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        InkStrokeSerializer.Load(this, stream);
    }

    /// <summary>Occurs when strokes are added, removed, or replaced.</summary>
    public event StrokeCollectionChangedEventHandler? StrokesChanged;

    /// <summary>Occurs when custom collection property data changes.</summary>
    public event PropertyDataChangedEventHandler? PropertyDataChanged;

    /// <summary>Gets the union of the real rendered stroke bounds.</summary>
    public Rect GetBounds()
    {
        if (Count == 0)
            return Rect.Empty;

        Rect bounds = Rect.Empty;
        foreach (Stroke stroke in this)
        {
            Rect strokeBounds = stroke.GetBounds();
            bounds = bounds.IsEmpty ? strokeBounds : Rect.Union(bounds, strokeBounds);
        }
        return bounds;
    }

    /// <summary>Creates a deep copy of the strokes and collection property data.</summary>
    public virtual StrokeCollection Clone()
    {
        var clone = new StrokeCollection(this.Select(static stroke => stroke.Clone()));
        foreach ((Guid id, object value) in _propertyData)
            clone._propertyData.Add(id, InkPropertyData.CloneValue(value));
        return clone;
    }

    /// <summary>Adds all strokes as one logical collection change.</summary>
    public void Add(StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Count == 0)
            return;
        ValidateCanAdd(strokes, nameof(strokes));
        RunBatch(strokes, Array.Empty<Stroke>(), () =>
        {
            foreach (Stroke stroke in strokes)
                Add(stroke);
        });
    }

    /// <summary>Adds a range as one logical collection change.</summary>
    public void AddRange(IEnumerable<Stroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        Add(strokes as StrokeCollection ?? CreateDetached(strokes));
    }

    /// <summary>Removes all supplied strokes as one logical collection change.</summary>
    public void Remove(StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Count == 0)
            return;
        foreach (Stroke stroke in strokes)
        {
            if (IndexOfReference(stroke) < 0)
                throw new ArgumentException("A stroke to remove is not in the collection.", nameof(strokes));
        }

        RunBatch(Array.Empty<Stroke>(), strokes, () =>
        {
            foreach (Stroke stroke in strokes.OrderByDescending(IndexOfReference))
                RemoveAt(IndexOfReference(stroke));
        });
    }

    /// <summary>Removes a range as one logical collection change.</summary>
    public void RemoveRange(IEnumerable<Stroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        Remove(strokes as StrokeCollection ?? CreateDetached(strokes));
    }

    /// <summary>Replaces one stroke with zero or more strokes.</summary>
    public void Replace(Stroke strokeToReplace, StrokeCollection strokesToReplaceWith)
    {
        ArgumentNullException.ThrowIfNull(strokeToReplace);
        Replace(CreateDetached([strokeToReplace]), strokesToReplaceWith);
    }

    /// <summary>Replaces a set of strokes with another set in one logical change.</summary>
    public void Replace(StrokeCollection strokesToReplace, StrokeCollection strokesToReplaceWith)
    {
        ArgumentNullException.ThrowIfNull(strokesToReplace);
        ArgumentNullException.ThrowIfNull(strokesToReplaceWith);
        if (strokesToReplace.Count == 0)
            throw new ArgumentException("At least one stroke must be replaced.", nameof(strokesToReplace));

        int insertionIndex = Count;
        foreach (Stroke stroke in strokesToReplace)
        {
            int index = IndexOfReference(stroke);
            if (index < 0)
                throw new ArgumentException("A stroke to replace is not in the collection.", nameof(strokesToReplace));
            insertionIndex = Math.Min(insertionIndex, index);
        }
        ValidateCanAdd(strokesToReplaceWith, nameof(strokesToReplaceWith));

        RunBatch(strokesToReplaceWith, strokesToReplace, () =>
        {
            foreach (Stroke stroke in strokesToReplace.OrderByDescending(IndexOfReference))
                RemoveAt(IndexOfReference(stroke));
            int index = insertionIndex;
            foreach (Stroke stroke in strokesToReplaceWith)
                Insert(index++, stroke);
        });
    }

    /// <summary>Returns strokes hit by a one-pixel tap.</summary>
    public StrokeCollection HitTest(Point point) =>
        HitTest([point], new RectangleStylusShape(1.0, 1.0));

    /// <summary>Returns strokes hit by a circular tap area.</summary>
    public StrokeCollection HitTest(Point point, double diameter)
    {
        if (!double.IsFinite(diameter) || diameter < DrawingAttributes.MinWidth || diameter > DrawingAttributes.MaxWidth)
            throw new ArgumentOutOfRangeException(nameof(diameter));
        var result = new StrokeCollection();
        foreach (Stroke stroke in this)
        {
            if (stroke.HitTest(point, diameter))
                result.Add(stroke);
        }
        return result;
    }

    /// <summary>Returns strokes whose rendered bounds intersect a rectangle.</summary>
    public StrokeCollection HitTest(Rect rect)
    {
        var result = new StrokeCollection();
        foreach (Stroke stroke in this)
        {
            if (stroke.HitTest(rect))
                result.Add(stroke);
        }
        return result;
    }

    /// <summary>Returns strokes with the requested percentage inside a rectangle.</summary>
    public StrokeCollection HitTest(Rect bounds, int percentageWithinBounds)
    {
        if (percentageWithinBounds is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentageWithinBounds));
        var result = new StrokeCollection();
        foreach (Stroke stroke in this)
        {
            if (stroke.HitTest(bounds, percentageWithinBounds))
                result.Add(stroke);
        }
        return result;
    }

    /// <summary>Returns strokes with the requested percentage inside a lasso.</summary>
    public StrokeCollection HitTest(IEnumerable<Point> lassoPoints, int percentageWithinLasso)
    {
        ArgumentNullException.ThrowIfNull(lassoPoints);
        if (percentageWithinLasso is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentageWithinLasso));
        Point[] points = lassoPoints.ToArray();
        var result = new StrokeCollection();
        foreach (Stroke stroke in this)
        {
            if (stroke.HitTest(points, percentageWithinLasso))
                result.Add(stroke);
        }
        return result;
    }

    /// <summary>Returns strokes hit by an eraser path.</summary>
    public StrokeCollection HitTest(IEnumerable<Point> path, StylusShape stylusShape)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(stylusShape);
        Point[] points = path.ToArray();
        var result = new StrokeCollection();
        foreach (Stroke stroke in this)
        {
            if (stroke.HitTest(points, stylusShape))
                result.Add(stroke);
        }
        return result;
    }

    /// <summary>Clips every stroke to a lasso region.</summary>
    public void Clip(IEnumerable<Point> lassoPoints)
    {
        ArgumentNullException.ThrowIfNull(lassoPoints);
        Point[] points = lassoPoints.ToArray();
        if (points.Length == 0)
            throw new ArgumentException("The lasso must contain at least one point.", nameof(lassoPoints));
        if (points.Length < 3)
        {
            Clear();
            return;
        }

        ReplaceAll(SelectMany(stroke =>
            stroke.HitTest(points, 100)
                ? CreateDetached([stroke])
                : stroke.GetClipResult(points)));
    }

    /// <summary>Clips every stroke to a rectangle.</summary>
    public void Clip(Rect bounds)
    {
        if (bounds.IsEmpty)
            return;
        ReplaceAll(SelectMany(stroke =>
            stroke.HitTest(bounds, 100)
                ? CreateDetached([stroke])
                : stroke.GetClipResult(bounds)));
    }

    /// <summary>Erases stroke portions contained by a lasso.</summary>
    public void Erase(IEnumerable<Point> lassoPoints)
    {
        ArgumentNullException.ThrowIfNull(lassoPoints);
        Point[] points = lassoPoints.ToArray();
        if (points.Length == 0)
            throw new ArgumentException("The lasso must contain at least one point.", nameof(lassoPoints));
        if (points.Length < 3)
            return;

        ReplaceAll(SelectMany(stroke =>
            stroke.HitTest(points, 1)
                ? stroke.GetEraseResult(points)
                : CreateDetached([stroke])));
    }

    /// <summary>Erases stroke portions contained by a rectangle.</summary>
    public void Erase(Rect bounds)
    {
        if (bounds.IsEmpty)
            return;
        ReplaceAll(SelectMany(stroke =>
            stroke.HitTest(bounds, 1)
                ? stroke.GetEraseResult(bounds)
                : CreateDetached([stroke])));
    }

    /// <summary>Erases stroke portions touched by an eraser path.</summary>
    public void Erase(IEnumerable<Point> eraserPath, StylusShape eraserShape)
    {
        ArgumentNullException.ThrowIfNull(eraserPath);
        ArgumentNullException.ThrowIfNull(eraserShape);
        Point[] points = eraserPath.ToArray();
        if (points.Length == 0)
            return;

        ReplaceAll(SelectMany(stroke =>
            stroke.HitTest(points, eraserShape)
                ? stroke.GetEraseResult(points, eraserShape)
                : CreateDetached([stroke])));
    }

    /// <summary>Creates an incremental lasso hit tester.</summary>
    public IncrementalLassoHitTester GetIncrementalLassoHitTester(int percentageWithinLasso)
    {
        if (percentageWithinLasso is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentageWithinLasso));
        return new IncrementalLassoHitTester(this, percentageWithinLasso);
    }

    /// <summary>Creates an incremental eraser hit tester.</summary>
    public IncrementalStrokeHitTester GetIncrementalStrokeHitTester(StylusShape eraserShape)
    {
        ArgumentNullException.ThrowIfNull(eraserShape);
        return new IncrementalStrokeHitTester(this, eraserShape);
    }

    /// <summary>Transforms every stroke in the collection.</summary>
    public void Transform(Matrix transformMatrix, bool applyToStylusTip)
    {
        if (!transformMatrix.IsIdentity && (!transformMatrix.HasInverse || !IsFinite(transformMatrix)))
            throw new ArgumentException("The transform matrix must be finite and invertible.", nameof(transformMatrix));
        foreach (Stroke stroke in this)
            stroke.Transform(transformMatrix, applyToStylusTip);
    }

    /// <summary>Draws every stroke in collection order.</summary>
    public void Draw(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        foreach (Stroke stroke in this)
            stroke.Draw(drawingContext);
    }

    /// <summary>Writes the complete collection to a stream.</summary>
    public void Save(Stream stream) => Save(stream, compress: true);

    /// <summary>Writes the complete collection, optionally compressing its payload.</summary>
    public virtual void Save(Stream stream, bool compress)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("The stream must be writable.", nameof(stream));
        InkStrokeSerializer.Save(this, stream, compress);
    }

    /// <summary>Adds or replaces collection property data.</summary>
    public void AddPropertyData(Guid propertyDataId, object propertyData)
    {
        InkPropertyData.Validate(propertyDataId, propertyData);
        if (_propertyData.TryGetValue(propertyDataId, out object? previous) &&
            InkPropertyData.ValuesEqual(previous, propertyData))
        {
            return;
        }

        object stored = InkPropertyData.CloneValue(propertyData);
        _propertyData[propertyDataId] = stored;
        OnPropertyDataChanged(new PropertyDataChangedEventArgs(propertyDataId, stored, previous));
    }

    /// <summary>Removes collection property data.</summary>
    public void RemovePropertyData(Guid propertyDataId)
    {
        if (!_propertyData.Remove(propertyDataId, out object? previous))
            throw new ArgumentException("The property identifier was not found.", nameof(propertyDataId));
        OnPropertyDataChanged(new PropertyDataChangedEventArgs(propertyDataId, null, previous));
    }

    /// <summary>Gets collection property data.</summary>
    public object GetPropertyData(Guid propertyDataId)
    {
        if (_propertyData.TryGetValue(propertyDataId, out object? value))
            return value;
        throw new ArgumentException("The property identifier was not found.", nameof(propertyDataId));
    }

    public Guid[] GetPropertyDataIds() => _propertyData.Keys.ToArray();

    public bool ContainsPropertyData(Guid propertyDataId) => _propertyData.ContainsKey(propertyDataId);

    /// <summary>Raises <see cref="StrokesChanged"/>.</summary>
    protected virtual void OnStrokesChanged(StrokeCollectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        StrokesChanged?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="PropertyDataChanged"/>.</summary>
    protected virtual void OnPropertyDataChanged(PropertyDataChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        PropertyDataChanged?.Invoke(this, e);
    }

    protected override void InsertItem(int index, Stroke item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (IndexOfReference(item) >= 0)
            throw new ArgumentException("A StrokeCollection cannot contain duplicate stroke references.", nameof(item));
        item.Invalidated += OnStrokeInvalidated;
        base.InsertItem(index, item);
        if (_deferNotifications == 0)
            OnStrokesChanged(new StrokeCollectionChangedEventArgs([item], Array.Empty<Stroke>()));
    }

    protected override void RemoveItem(int index)
    {
        Stroke removed = this[index];
        removed.Invalidated -= OnStrokeInvalidated;
        base.RemoveItem(index);
        if (_deferNotifications == 0)
            OnStrokesChanged(new StrokeCollectionChangedEventArgs(Array.Empty<Stroke>(), [removed]));
    }

    protected override void SetItem(int index, Stroke item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (IndexOfReference(item) >= 0)
            throw new ArgumentException("A StrokeCollection cannot contain duplicate stroke references.", nameof(item));
        Stroke removed = this[index];
        removed.Invalidated -= OnStrokeInvalidated;
        item.Invalidated += OnStrokeInvalidated;
        base.SetItem(index, item);
        if (_deferNotifications == 0)
            OnStrokesChanged(new StrokeCollectionChangedEventArgs([item], [removed]));
    }

    protected override void ClearItems()
    {
        if (Count == 0)
            return;
        Stroke[] removed = this.ToArray();
        foreach (Stroke stroke in removed)
            stroke.Invalidated -= OnStrokeInvalidated;
        base.ClearItems();
        if (_deferNotifications == 0)
            OnStrokesChanged(new StrokeCollectionChangedEventArgs(Array.Empty<Stroke>(), removed));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_deferNotifications == 0)
            base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_deferNotifications == 0)
            base.OnPropertyChanged(e);
    }

    internal IReadOnlyDictionary<Guid, object> PropertyData => _propertyData;

    internal void LoadPropertyData(Guid id, object value) => _propertyData[id] = value;

    internal void AddLoadedStroke(Stroke stroke)
    {
        stroke.Invalidated += OnStrokeInvalidated;
        Items.Add(stroke);
    }

    internal static StrokeCollection CreateDetached(IEnumerable<Stroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        var collection = new StrokeCollection();
        var seen = new HashSet<Stroke>(ReferenceEqualityComparer.Instance);
        foreach (Stroke stroke in strokes)
        {
            ArgumentNullException.ThrowIfNull(stroke);
            if (!seen.Add(stroke))
                throw new ArgumentException("A StrokeCollection cannot contain duplicate stroke references.", nameof(strokes));
            collection.Items.Add(stroke);
        }
        return collection;
    }

    internal void DetachFromStrokes()
    {
        foreach (Stroke stroke in this)
            stroke.Invalidated -= OnStrokeInvalidated;
    }

    private void RunBatch(
        IEnumerable<Stroke> added,
        IEnumerable<Stroke> removed,
        Action mutation)
    {
        Stroke[] addedArray = added.ToArray();
        Stroke[] removedArray = removed.ToArray();
        _deferNotifications++;
        try
        {
            mutation();
        }
        finally
        {
            _deferNotifications--;
        }

        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnStrokesChanged(new StrokeCollectionChangedEventArgs(addedArray, removedArray));
    }

    private void ReplaceAll(IEnumerable<Stroke> replacement)
    {
        Stroke[] oldItems = this.ToArray();
        Stroke[] newItems = replacement.ToArray();
        if (oldItems.Length == newItems.Length &&
            oldItems.Zip(newItems).All(static pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        if (newItems.Distinct(ReferenceEqualityComparer.Instance).Count() != newItems.Length)
            throw new InvalidOperationException("An ink operation produced duplicate stroke references.");

        RunBatch(newItems, oldItems, () =>
        {
            Clear();
            foreach (Stroke stroke in newItems)
                Add(stroke);
        });
    }

    private IEnumerable<Stroke> SelectMany(Func<Stroke, StrokeCollection> selector)
    {
        foreach (Stroke stroke in this.ToArray())
        {
            StrokeCollection selected = selector(stroke);
            try
            {
                foreach (Stroke result in selected)
                    yield return result;
            }
            finally
            {
                selected.DetachFromStrokes();
            }
        }
    }

    private void ValidateCanAdd(IEnumerable<Stroke> strokes, string parameterName)
    {
        var seen = new HashSet<Stroke>(ReferenceEqualityComparer.Instance);
        foreach (Stroke stroke in strokes)
        {
            ArgumentNullException.ThrowIfNull(stroke);
            if (!seen.Add(stroke) || IndexOfReference(stroke) >= 0)
                throw new ArgumentException("A StrokeCollection cannot contain duplicate stroke references.", parameterName);
        }
    }

    private int IndexOfReference(Stroke? stroke)
    {
        if (stroke is null)
            return -1;
        for (int i = 0; i < Count; i++)
        {
            if (ReferenceEquals(this[i], stroke))
                return i;
        }
        return -1;
    }

    private void OnStrokeInvalidated(object? sender, EventArgs e) =>
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    private static bool IsFinite(Matrix matrix) =>
        double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12) &&
        double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) &&
        double.IsFinite(matrix.OffsetX) && double.IsFinite(matrix.OffsetY);

    internal sealed class ReadOnlyStrokeCollection : StrokeCollection
    {
        internal ReadOnlyStrokeCollection(IEnumerable<Stroke> strokes)
        {
            foreach (Stroke stroke in strokes)
                AddLoadedStroke(stroke);
            DetachFromStrokes();
        }

        protected override void InsertItem(int index, Stroke item) => ThrowReadOnly();

        protected override void RemoveItem(int index) => ThrowReadOnly();

        protected override void SetItem(int index, Stroke item) => ThrowReadOnly();

        protected override void ClearItems() => ThrowReadOnly();

        private static void ThrowReadOnly() =>
            throw new NotSupportedException("This stroke collection is read-only.");
    }
}

/// <summary>Provides strokes added and removed by one collection change.</summary>
public class StrokeCollectionChangedEventArgs : EventArgs
{
    public StrokeCollectionChangedEventArgs(StrokeCollection? added, StrokeCollection? removed)
    {
        if (added is null && removed is null)
            throw new ArgumentException("The added and removed collections cannot both be null.");
        Added = new StrokeCollection.ReadOnlyStrokeCollection(
            added is null ? Array.Empty<Stroke>() : added);
        Removed = new StrokeCollection.ReadOnlyStrokeCollection(
            removed is null ? Array.Empty<Stroke>() : removed);
    }

    internal StrokeCollectionChangedEventArgs(IEnumerable<Stroke> added, IEnumerable<Stroke> removed)
        : this(StrokeCollection.CreateDetached(added), StrokeCollection.CreateDetached(removed))
    {
    }

    public StrokeCollection Added { get; }

    public StrokeCollection Removed { get; }
}

public delegate void StrokeCollectionChangedEventHandler(
    object sender,
    StrokeCollectionChangedEventArgs e);
