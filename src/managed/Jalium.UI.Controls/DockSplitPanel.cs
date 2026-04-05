using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A panel that arranges children in a horizontal or vertical split layout
/// with resizable splitter bars between them.
/// </summary>
[ContentProperty("Children")]
public class DockSplitPanel : Panel
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(ThemeColors.WindowBackground);

    public DockSplitPanel()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
    }

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(DockSplitPanel),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SplitterSizeProperty =
        DependencyProperty.Register(nameof(SplitterSize), typeof(double), typeof(DockSplitPanel),
            new PropertyMetadata(6.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Attached property: the initial size of a child pane.
    /// Uses <see cref="GridLength"/> 閳?e.g. "250" for 250px, "*" for 1-star, "2*" for 2-star.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.RegisterAttached("Size", typeof(GridLength), typeof(DockSplitPanel),
            new PropertyMetadata(GridLength.Star, OnLayoutPropertyChanged));

    /// <summary>
    /// Attached property: the minimum size (in pixels) of a child pane.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinSizeProperty =
        DependencyProperty.RegisterAttached("MinSize", typeof(double), typeof(DockSplitPanel),
            new PropertyMetadata(100.0, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double SplitterSize
    {
        get => (double)GetValue(SplitterSizeProperty)!;
        set => SetValue(SplitterSizeProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static GridLength GetSize(UIElement element)
        => (GridLength)(element.GetValue(SizeProperty) ?? GridLength.Star);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetSize(UIElement element, GridLength value)
        => element.SetValue(SizeProperty, value);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static double GetMinSize(UIElement element)
        => (double)(element.GetValue(MinSizeProperty) ?? 100.0);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetMinSize(UIElement element, double value)
        => element.SetValue(MinSizeProperty, value);

    #endregion

    #region Internal Splitter Management

    private readonly List<DockSplitBar> _splitters = new();

    /// <summary>
    /// Tracks resolved actual sizes for each child (in the main axis direction).
    /// Updated during Measure and used during Arrange.
    /// </summary>
    private double[] _resolvedSizes = Array.Empty<double>();

    #endregion

    #region Visual Tree

    /// <summary>
    /// Includes both pane children and internal splitter bars in visual traversal.
    /// </summary>
    public override int VisualChildrenCount => base.VisualChildrenCount + _splitters.Count;

    /// <summary>
    /// Returns splitter bars first, then pane children.
    /// This keeps pane borders visually on top when adjacent to splitters.
    /// </summary>
    public override Visual? GetVisualChild(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var splitterCount = _splitters.Count;
        if (index < splitterCount)
            return _splitters[index];

        var paneIndex = index - splitterCount;
        var paneCount = base.VisualChildrenCount;
        if (paneIndex >= 0 && paneIndex < paneCount)
            return base.GetVisualChild(paneIndex);

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // For attached properties, the sender is the child element
        if (d is DockSplitPanel panel)
        {
            panel.InvalidateMeasure();
        }
        else if (d is UIElement element)
        {
            // Attached property changed on a child 閳?invalidate the parent DockSplitPanel
            if (element.VisualParent is DockSplitPanel parentPanel)
                parentPanel.InvalidateMeasure();
        }
    }

    /// <summary>
    /// Ensures the internal splitter bars match the current Children count (N-1 splitters for N children).
    /// </summary>
    private void SyncSplitters()
    {
        var childCount = Children.Count;
        var requiredSplitters = Math.Max(0, childCount - 1);

        // Remove excess splitters
        while (_splitters.Count > requiredSplitters)
        {
            var bar = _splitters[^1];
            _splitters.RemoveAt(_splitters.Count - 1);
            RemoveVisualChild(bar);
        }

        // Add missing splitters
        while (_splitters.Count < requiredSplitters)
        {
            var index = _splitters.Count;
            var bar = new DockSplitBar
            {
                Owner = this,
                PaneIndex1 = index,
                PaneIndex2 = index + 1
            };
            bar.UpdateCursorForOrientation();
            _splitters.Add(bar);
            AddVisualChild(bar);
        }

        // Update indices and cursors
        for (int i = 0; i < _splitters.Count; i++)
        {
            _splitters[i].PaneIndex1 = i;
            _splitters[i].PaneIndex2 = i + 1;
            _splitters[i].UpdateCursorForOrientation();
        }
    }

    /// <summary>
    /// Gets the effective minimum pane size in the panel's main axis.
    /// If DockSplitPanel.MinSize is set locally on the child, that value wins.
    /// Otherwise, respects both default MinSize and FrameworkElement MinWidth/MinHeight.
    /// </summary>
    private double GetEffectiveMinSize(UIElement child)
    {
        var localMinValue = child.ReadLocalValue(MinSizeProperty);
        if (localMinValue != DependencyProperty.UnsetValue && localMinValue is double localMin)
            return Math.Max(0, localMin);

        var attachedMin = Math.Max(0, GetMinSize(child));
        if (child is not FrameworkElement fe)
            return attachedMin;

        var frameworkMin = Orientation == Orientation.Horizontal ? fe.MinWidth : fe.MinHeight;
        if (double.IsNaN(frameworkMin) || double.IsInfinity(frameworkMin))
            frameworkMin = 0;

        return Math.Max(attachedMin, Math.Max(0, frameworkMin));
    }

    /// <summary>
    /// Resolves the actual pixel sizes for all children based on their Size attached property.
    /// </summary>
    private double[] ResolveSizes(double availableMainAxis)
    {
        var childCount = Children.Count;
        if (childCount == 0) return Array.Empty<double>();

        var sizes = new double[childCount];
        var totalSplitterSpace = Math.Max(0, childCount - 1) * SplitterSize;
        var availableForChildren = Math.Max(0, availableMainAxis - totalSplitterSpace);

        // First pass: allocate pixel-sized children
        double pixelTotal = 0;
        double starTotal = 0;

        for (int i = 0; i < childCount; i++)
        {
            var child = Children[i];
            var gridLength = GetSize(child);

            if (gridLength.IsAbsolute)
            {
                sizes[i] = gridLength.Value;
                pixelTotal += gridLength.Value;
            }
            else if (gridLength.IsStar)
            {
                starTotal += gridLength.Value;
                sizes[i] = -1; // Placeholder for star
            }
            else // Auto 閳?treat as star(1)
            {
                starTotal += 1;
                sizes[i] = -1;
            }
        }

        // Second pass: distribute remaining space to star-sized children
        var remainingSpace = Math.Max(0, availableForChildren - pixelTotal);
        if (starTotal > 0)
        {
            for (int i = 0; i < childCount; i++)
            {
                if (sizes[i] < 0)
                {
                    var child = Children[i];
                    var gridLength = GetSize(child);
                    var starValue = gridLength.IsStar ? gridLength.Value : 1.0;
                    sizes[i] = remainingSpace * (starValue / starTotal);
                }
            }
        }
        else
        {
            // No star children 閳?fill remaining with equal distribution
            int unresolved = 0;
            for (int i = 0; i < childCount; i++)
            {
                if (sizes[i] < 0) unresolved++;
            }
            if (unresolved > 0)
            {
                var each = remainingSpace / unresolved;
                for (int i = 0; i < childCount; i++)
                {
                    if (sizes[i] < 0) sizes[i] = each;
                }
            }
        }

        // Enforce minimum sizes
        for (int i = 0; i < childCount; i++)
        {
            var child = Children[i];
            var minSize = GetEffectiveMinSize(child);
            sizes[i] = Math.Max(sizes[i], minSize);
        }

        // Proportional scaling fallback: if all children are pixel-sized and the total
        // doesn't match available space, scale proportionally so the layout fills properly.
        if (starTotal == 0 && childCount > 0)
        {
            double totalResolved = 0;
            for (int i = 0; i < childCount; i++)
                totalResolved += sizes[i];

            if (totalResolved > 0 && Math.Abs(totalResolved - availableForChildren) > 1.0)
            {
                var scale = availableForChildren / totalResolved;
                for (int i = 0; i < childCount; i++)
                {
                    var child = Children[i];
                    var minSize = GetEffectiveMinSize(child);
                    sizes[i] = Math.Max(minSize, sizes[i] * scale);
                }
            }
        }

        return sizes;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncSplitters();

        var childCount = Children.Count;
        if (childCount == 0) return new Size();

        var isHorizontal = Orientation == Orientation.Horizontal;
        var mainAxis = isHorizontal ? availableSize.Width : availableSize.Height;
        var crossAxis = isHorizontal ? availableSize.Height : availableSize.Width;

        _resolvedSizes = ResolveSizes(mainAxis);

        // Measure each child
        for (int i = 0; i < childCount; i++)
        {
            var child = Children[i];
            if (child.Visibility == Visibility.Collapsed) continue;

            var childSize = isHorizontal
                ? new Size(_resolvedSizes[i], crossAxis)
                : new Size(crossAxis, _resolvedSizes[i]);

            if (child is FrameworkElement fe)
                fe.Measure(childSize);
        }

        // Measure splitters
        foreach (var bar in _splitters)
        {
            var barSize = isHorizontal
                ? new Size(SplitterSize, crossAxis)
                : new Size(crossAxis, SplitterSize);
            bar.Measure(barSize);
        }

        return availableSize;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        dc.DrawRectangle(ResolveBackgroundBrush(), null, new Rect(RenderSize));
        base.OnRender(drawingContextObj);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var childCount = Children.Count;
        if (childCount == 0) return finalSize;

        var isHorizontal = Orientation == Orientation.Horizontal;
        var mainAxis = isHorizontal ? finalSize.Width : finalSize.Height;
        var crossAxis = isHorizontal ? finalSize.Height : finalSize.Width;

        // Re-resolve sizes with final dimensions
        _resolvedSizes = ResolveSizes(mainAxis);

        double offset = 0;

        for (int i = 0; i < childCount; i++)
        {
            var child = Children[i];
            var size = _resolvedSizes[i];

            // Arrange child
            var childRect = isHorizontal
                ? new Rect(offset, 0, size, crossAxis)
                : new Rect(0, offset, crossAxis, size);

            if (child is FrameworkElement fe)
                fe.Arrange(childRect);

            offset += size;

            // Arrange splitter bar (if not the last child)
            if (i < _splitters.Count)
            {
                var barRect = isHorizontal
                    ? new Rect(offset, 0, SplitterSize, crossAxis)
                    : new Rect(0, offset, crossAxis, SplitterSize);

                _splitters[i].Arrange(barRect);
                offset += SplitterSize;
            }
        }

        return finalSize;
    }

    #region Pane Management

    /// <summary>
    /// Removes a child pane from this split panel. Splitters are re-synced on next measure.
    /// If only one child remains, the remaining child's Size is reset to star sizing.
    /// </summary>
    internal void RemovePane(UIElement child)
    {
        var index = Children.IndexOf(child);
        if (index < 0) return;

        Children.RemoveAt(index);

        // If only one child left, reset it to star sizing so it fills the space
        if (Children.Count == 1)
        {
            SetSize(Children[0], GridLength.Star);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Inserts a new pane adjacent to an existing child.
    /// If the orientation matches, inserts directly; otherwise wraps the target in a new nested DockSplitPanel.
    /// </summary>
    /// <param name="target">The existing child to dock relative to.</param>
    /// <param name="newPane">The new pane to insert.</param>
    /// <param name="position">The dock direction: Left/Right (Horizontal) or Top/Bottom (Vertical).</param>
    internal void InsertPane(UIElement target, UIElement newPane, DockPosition position)
    {
        var targetIndex = Children.IndexOf(target);
        if (targetIndex < 0) return;

        var requiredOrientation = position is DockPosition.Left or DockPosition.Right
            ? Orientation.Horizontal
            : Orientation.Vertical;

        var insertBefore = position is DockPosition.Left or DockPosition.Top;

        if (Orientation == requiredOrientation)
        {
            // Same orientation 閳?insert adjacent to target
            var insertIndex = insertBefore ? targetIndex : targetIndex + 1;
            SetSize(newPane, GridLength.Star);
            Children.Insert(insertIndex, newPane);
        }
        else
        {
            // Different orientation 閳?wrap target in a new nested DockSplitPanel
            Children.RemoveAt(targetIndex);

            var wrapper = new DockSplitPanel { Orientation = requiredOrientation };
            if (insertBefore)
            {
                wrapper.Children.Add(newPane);
                wrapper.Children.Add(target);
            }
            else
            {
                wrapper.Children.Add(target);
                wrapper.Children.Add(newPane);
            }

            // The wrapper takes the target's original size allocation
            var originalSize = GetSize(target);
            SetSize(wrapper, originalSize);
            SetSize(target, GridLength.Star);
            SetSize(newPane, GridLength.Star);

            Children.Insert(targetIndex, wrapper);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    #endregion

    #region Resize Support (called by DockSplitBar)

    /// <summary>
    /// Gets the current actual sizes of two panes.
    /// </summary>
    internal void GetPaneSizes(int index1, int index2, out double size1, out double size2)
    {
        size1 = index1 >= 0 && index1 < _resolvedSizes.Length ? _resolvedSizes[index1] : 0;
        size2 = index2 >= 0 && index2 < _resolvedSizes.Length ? _resolvedSizes[index2] : 0;
    }

    /// <summary>
    /// Applies a resize delta to two adjacent panes.
    /// Converts both panes to star sizing with proportional ratios so the layout
    /// adapts when the window is resized.
    /// </summary>
    internal void ResizePanes(int index1, int index2, double originalSize1, double originalSize2, double delta)
    {
        if (index1 < 0 || index1 >= Children.Count || index2 < 0 || index2 >= Children.Count)
            return;

        var child1 = Children[index1];
        var child2 = Children[index2];

        var minSize1 = GetEffectiveMinSize(child1);
        var minSize2 = GetEffectiveMinSize(child2);

        // Clamp delta so both panes respect minimum size and total size stays constant.
        var minDelta = minSize1 - originalSize1;
        var maxDelta = originalSize2 - minSize2;
        if (minDelta > maxDelta)
            return;

        var clampedDelta = Math.Clamp(delta, minDelta, maxDelta);
        var newPixel1 = originalSize1 + clampedDelta;
        var newPixel2 = originalSize2 - clampedDelta;

        // Convert desired pixel sizes to correct star values.
        // Read current star values so we scale proportionally relative to other panes.
        var oldStar1 = GetSize(child1);
        var oldStar2 = GetSize(child2);
        var starValue1 = oldStar1.IsStar ? oldStar1.Value : 1.0;
        var starValue2 = oldStar2.IsStar ? oldStar2.Value : 1.0;

        // The two panes currently occupy (originalSize1 + originalSize2) pixels
        // with star weights (starValue1 + starValue2).  Redistribute the same
        // total star weight proportionally to the new pixel sizes.
        var totalPixels = originalSize1 + originalSize2;
        if (totalPixels > 0)
        {
            var totalStar = starValue1 + starValue2;
            var newStar1 = totalStar * (newPixel1 / totalPixels);
            var newStar2 = totalStar * (newPixel2 / totalPixels);
            SetSize(child1, new GridLength(Math.Max(0.001, newStar1), GridUnitType.Star));
            SetSize(child2, new GridLength(Math.Max(0.001, newStar2), GridUnitType.Star));
        }

        InvalidateMeasure();
    }

    private Brush ResolveBackgroundBrush()
    {
        if (TryFindResource("OneBackgroundPrimary") is Brush primary)
            return primary;
        if (TryFindResource("WindowBackground") is Brush secondary)
            return secondary;
        return s_fallbackBackgroundBrush;
    }

    #endregion
}
