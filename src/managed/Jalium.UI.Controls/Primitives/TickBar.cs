using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies the placement of a TickBar relative to the Track of a Slider.
/// </summary>
public enum TickBarPlacement
{
    /// <summary>
    /// The tick bar is positioned to the left of the Track.
    /// </summary>
    Left,

    /// <summary>
    /// The tick bar is positioned above the Track.
    /// </summary>
    Top,

    /// <summary>
    /// The tick bar is positioned to the right of the Track.
    /// </summary>
    Right,

    /// <summary>
    /// The tick bar is positioned below the Track.
    /// </summary>
    Bottom
}

/// <summary>
/// Represents a control that draws a set of tick marks for a Slider control.
/// </summary>
public sealed class TickBar : FrameworkElement
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultFillBrush = new(Color.FromRgb(100, 100, 100));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(TickBar),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(TickBar),
            new PropertyMetadata(100.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TickFrequency dependency property.
    /// </summary>
    public static readonly DependencyProperty TickFrequencyProperty =
        DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(TickBar),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Ticks dependency property.
    /// </summary>
    public static readonly DependencyProperty TicksProperty =
        DependencyProperty.Register(nameof(Ticks), typeof(DoubleCollection), typeof(TickBar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(TickBarPlacement), typeof(TickBar),
            new PropertyMetadata(TickBarPlacement.Top, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Fill dependency property.
    /// </summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(TickBar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsDirectionReversed dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDirectionReversedProperty =
        DependencyProperty.Register(nameof(IsDirectionReversed), typeof(bool), typeof(TickBar),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ReservedSpace dependency property.
    /// </summary>
    public static readonly DependencyProperty ReservedSpaceProperty =
        DependencyProperty.Register(nameof(ReservedSpace), typeof(double), typeof(TickBar),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsSelectionRangeEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSelectionRangeEnabledProperty =
        DependencyProperty.Register(nameof(IsSelectionRangeEnabled), typeof(bool), typeof(TickBar),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionStart dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register(nameof(SelectionStart), typeof(double), typeof(TickBar),
            new PropertyMetadata(-1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionEnd dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register(nameof(SelectionEnd), typeof(double), typeof(TickBar),
            new PropertyMetadata(-1.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the minimum value for the tick bar range.
    /// </summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value for the tick bar range.
    /// </summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the interval between tick marks.
    /// </summary>
    public double TickFrequency
    {
        get => (double)GetValue(TickFrequencyProperty)!;
        set => SetValue(TickFrequencyProperty, value);
    }

    /// <summary>
    /// Gets or sets a set of tick marks to display.
    /// </summary>
    public DoubleCollection? Ticks
    {
        get => (DoubleCollection?)GetValue(TicksProperty);
        set => SetValue(TicksProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of the tick marks relative to the Track.
    /// </summary>
    public TickBarPlacement Placement
    {
        get => (TickBarPlacement)(GetValue(PlacementProperty) ?? TickBarPlacement.Top);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to fill the tick marks.
    /// </summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the direction of increasing value is reversed.
    /// </summary>
    public bool IsDirectionReversed
    {
        get => (bool)GetValue(IsDirectionReversedProperty)!;
        set => SetValue(IsDirectionReversedProperty, value);
    }

    /// <summary>
    /// Gets or sets the space reserved for the thumb of the Slider.
    /// </summary>
    public double ReservedSpace
    {
        get => (double)GetValue(ReservedSpaceProperty)!;
        set => SetValue(ReservedSpaceProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the selection range is enabled.
    /// </summary>
    public bool IsSelectionRangeEnabled
    {
        get => (bool)GetValue(IsSelectionRangeEnabledProperty)!;
        set => SetValue(IsSelectionRangeEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the start of the selection range.
    /// </summary>
    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty)!;
        set => SetValue(SelectionStartProperty, value);
    }

    /// <summary>
    /// Gets or sets the end of the selection range.
    /// </summary>
    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty)!;
        set => SetValue(SelectionEndProperty, value);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Default size based on placement
        if (Placement == TickBarPlacement.Left || Placement == TickBarPlacement.Right)
        {
            return new Size(4, availableSize.Height);
        }
        else
        {
            return new Size(availableSize.Width, 4);
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var range = Maximum - Minimum;
        if (range <= 0)
            return;

        var tickBrush = Fill ?? s_defaultFillBrush;
        var tickPen = new Pen(tickBrush, 1);

        var isHorizontal = Placement == TickBarPlacement.Top || Placement == TickBarPlacement.Bottom;
        var reservedSpace = ReservedSpace;

        // Calculate drawing area
        double startOffset, endOffset, length;
        if (isHorizontal)
        {
            startOffset = reservedSpace / 2;
            endOffset = RenderSize.Width - reservedSpace / 2;
            length = endOffset - startOffset;
        }
        else
        {
            startOffset = reservedSpace / 2;
            endOffset = RenderSize.Height - reservedSpace / 2;
            length = endOffset - startOffset;
        }

        if (length <= 0)
            return;

        // Draw ticks
        var tickValues = GetTickValues();
        foreach (var value in tickValues)
        {
            if (value < Minimum || value > Maximum)
                continue;

            var ratio = (value - Minimum) / range;
            if (IsDirectionReversed)
            {
                ratio = 1 - ratio;
            }

            DrawTick(dc, tickPen, ratio, startOffset, length, isHorizontal);
        }
    }

    private IEnumerable<double> GetTickValues()
    {
        // If custom ticks are provided, use those
        if (Ticks != null && Ticks.Count > 0)
        {
            foreach (var tick in Ticks)
            {
                yield return tick;
            }
            yield break;
        }

        // Otherwise use tick frequency
        if (TickFrequency > 0)
        {
            for (var value = Minimum; value <= Maximum; value += TickFrequency)
            {
                yield return value;
            }

            // Ensure maximum is included
            if (Math.Abs((Maximum - Minimum) % TickFrequency) > double.Epsilon)
            {
                yield return Maximum;
            }
        }
        else
        {
            // Just draw start and end ticks
            yield return Minimum;
            yield return Maximum;
        }
    }

    private void DrawTick(DrawingContext dc, Pen pen, double ratio, double startOffset, double length, bool isHorizontal)
    {
        var position = startOffset + length * ratio;

        if (isHorizontal)
        {
            var tickLength = RenderSize.Height;
            Point start, end;

            if (Placement == TickBarPlacement.Top)
            {
                start = new Point(position, tickLength);
                end = new Point(position, tickLength / 2);
            }
            else // Bottom
            {
                start = new Point(position, 0);
                end = new Point(position, tickLength / 2);
            }

            dc.DrawLine(pen, start, end);
        }
        else
        {
            var tickLength = RenderSize.Width;
            Point start, end;

            if (Placement == TickBarPlacement.Left)
            {
                start = new Point(tickLength, position);
                end = new Point(tickLength / 2, position);
            }
            else // Right
            {
                start = new Point(0, position);
                end = new Point(tickLength / 2, position);
            }

            dc.DrawLine(pen, start, end);
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TickBar tickBar)
        {
            tickBar.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Represents a collection of double values.
/// </summary>
public sealed class DoubleCollection : List<double>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DoubleCollection"/> class.
    /// </summary>
    public DoubleCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DoubleCollection"/> class with the specified values.
    /// </summary>
    /// <param name="values">The values to add to the collection.</param>
    public DoubleCollection(IEnumerable<double> values) : base(values)
    {
    }

    /// <summary>
    /// Parses a string representation into a DoubleCollection.
    /// </summary>
    /// <param name="source">The string to parse (comma or space separated).</param>
    /// <returns>A new DoubleCollection.</returns>
    public static DoubleCollection Parse(string source)
    {
        var collection = new DoubleCollection();
        if (string.IsNullOrWhiteSpace(source))
            return collection;

        var separators = new[] { ',', ' ' };
        var parts = source.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (double.TryParse(part.Trim(), out var value))
            {
                collection.Add(value);
            }
        }

        return collection;
    }
}
