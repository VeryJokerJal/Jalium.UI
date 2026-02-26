using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ink;

/// <summary>
/// Represents a collection of <see cref="Stroke"/> objects with change notification.
/// </summary>
public sealed class StrokeCollection : ObservableCollection<Stroke>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeCollection"/> class.
    /// </summary>
    public StrokeCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeCollection"/> class with the specified strokes.
    /// </summary>
    /// <param name="strokes">The strokes to add to the collection.</param>
    public StrokeCollection(IEnumerable<Stroke> strokes) : base(strokes)
    {
        foreach (var stroke in this)
        {
            stroke.Invalidated += OnStrokeInvalidated;
        }
    }

    /// <summary>
    /// Occurs when strokes are added, removed, or replaced in the collection.
    /// </summary>
    public event EventHandler<StrokeCollectionChangedEventArgs>? StrokesChanged;

    /// <summary>
    /// Gets the bounding rectangle of all strokes in the collection.
    /// </summary>
    /// <returns>A <see cref="Rect"/> that bounds all strokes.</returns>
    public Rect GetBounds()
    {
        if (Count == 0)
            return Rect.Empty;

        var bounds = Rect.Empty;
        foreach (var stroke in this)
        {
            var strokeBounds = stroke.GetBounds();
            if (bounds.IsEmpty)
                bounds = strokeBounds;
            else
                bounds = bounds.Union(strokeBounds);
        }

        return bounds;
    }

    /// <summary>
    /// Creates a copy of this collection.
    /// </summary>
    /// <returns>A new <see cref="StrokeCollection"/> containing cloned strokes.</returns>
    public StrokeCollection Clone()
    {
        var cloned = new StrokeCollection();
        foreach (var stroke in this)
        {
            cloned.Add(stroke.Clone());
        }
        return cloned;
    }

    /// <summary>
    /// Adds a range of strokes to the collection.
    /// </summary>
    /// <param name="strokes">The strokes to add.</param>
    public void AddRange(IEnumerable<Stroke> strokes)
    {
        foreach (var stroke in strokes)
        {
            Add(stroke);
        }
    }

    /// <summary>
    /// Removes a range of strokes from the collection.
    /// </summary>
    /// <param name="strokes">The strokes to remove.</param>
    public void RemoveRange(IEnumerable<Stroke> strokes)
    {
        foreach (var stroke in strokes.ToList())
        {
            Remove(stroke);
        }
    }

    /// <summary>
    /// Returns all strokes that intersect with the specified point.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="diameter">The diameter of the hit test area.</param>
    /// <returns>A collection of strokes that hit the point.</returns>
    public StrokeCollection HitTest(Point point, double diameter = 4.0)
    {
        var result = new StrokeCollection();
        foreach (var stroke in this)
        {
            if (stroke.HitTest(point, diameter))
            {
                result.Add(stroke);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns all strokes that intersect with the specified rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to test.</param>
    /// <returns>A collection of strokes that intersect the rectangle.</returns>
    public StrokeCollection HitTest(Rect rect)
    {
        var result = new StrokeCollection();
        foreach (var stroke in this)
        {
            if (stroke.HitTest(rect))
            {
                result.Add(stroke);
            }
        }
        return result;
    }

    /// <summary>
    /// Draws all strokes in the collection using the specified drawing context.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    public void Draw(DrawingContext dc)
    {
        foreach (var stroke in this)
        {
            stroke.Draw(dc);
        }
    }

    /// <inheritdoc/>
    protected override void InsertItem(int index, Stroke item)
    {
        item.Invalidated += OnStrokeInvalidated;
        base.InsertItem(index, item);
        OnStrokesChanged(new StrokeCollectionChangedEventArgs(new[] { item }, Array.Empty<Stroke>()));
    }

    /// <inheritdoc/>
    protected override void RemoveItem(int index)
    {
        var item = this[index];
        item.Invalidated -= OnStrokeInvalidated;
        base.RemoveItem(index);
        OnStrokesChanged(new StrokeCollectionChangedEventArgs(Array.Empty<Stroke>(), new[] { item }));
    }

    /// <inheritdoc/>
    protected override void SetItem(int index, Stroke item)
    {
        var oldItem = this[index];
        oldItem.Invalidated -= OnStrokeInvalidated;
        item.Invalidated += OnStrokeInvalidated;
        base.SetItem(index, item);
        OnStrokesChanged(new StrokeCollectionChangedEventArgs(new[] { item }, new[] { oldItem }));
    }

    /// <inheritdoc/>
    protected override void ClearItems()
    {
        var removed = this.ToArray();
        foreach (var item in removed)
        {
            item.Invalidated -= OnStrokeInvalidated;
        }
        base.ClearItems();
        OnStrokesChanged(new StrokeCollectionChangedEventArgs(Array.Empty<Stroke>(), removed));
    }

    private void OnStrokeInvalidated(object? sender, EventArgs e)
    {
        // Notify that collection needs redraw
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Raises the <see cref="StrokesChanged"/> event.
    /// </summary>
    protected void OnStrokesChanged(StrokeCollectionChangedEventArgs e)
    {
        StrokesChanged?.Invoke(this, e);
    }
}

/// <summary>
/// Provides data for the <see cref="StrokeCollection.StrokesChanged"/> event.
/// </summary>
public sealed class StrokeCollectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeCollectionChangedEventArgs"/> class.
    /// </summary>
    /// <param name="added">The strokes that were added.</param>
    /// <param name="removed">The strokes that were removed.</param>
    public StrokeCollectionChangedEventArgs(IEnumerable<Stroke> added, IEnumerable<Stroke> removed)
    {
        Added = added.ToArray();
        Removed = removed.ToArray();
    }

    /// <summary>
    /// Gets the strokes that were added to the collection.
    /// </summary>
    public IReadOnlyList<Stroke> Added { get; }

    /// <summary>
    /// Gets the strokes that were removed from the collection.
    /// </summary>
    public IReadOnlyList<Stroke> Removed { get; }
}
