using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that lets the user select a continuous sub-range (a start and an end value)
/// from within a numeric range by dragging two thumbs along a track.
/// </summary>
public class RangeSlider : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.RangeSliderAutomationPeer(this);
    }

    // Cached brushes/pens for the no-template render fallback.
    private static readonly SolidColorBrush s_trackBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_accentBrush = new(ThemeColors.SliderThumb);
    private static readonly SolidColorBrush s_accentPressedBrush = new(Color.FromRgb(0, 100, 190));
    private static readonly SolidColorBrush s_tickBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly Pen s_tickPen = new(s_tickBrush, 1);
    private static readonly SolidColorBrush s_whiteBrush = new(ThemeColors.CheckMark);
    private static readonly Pen s_thumbBorderPen = new(s_whiteBrush, 2);

    #region Dependency Properties

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(0.0, OnRangeBoundsChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(100.0, OnRangeBoundsChanged));

    /// <summary>
    /// Identifies the RangeStart dependency property (the lower selected value).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RangeStartProperty =
        DependencyProperty.Register(nameof(RangeStart), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(0.0, OnRangeStartChanged, CoerceRangeStart));

    /// <summary>
    /// Identifies the RangeEnd dependency property (the upper selected value).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RangeEndProperty =
        DependencyProperty.Register(nameof(RangeEnd), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(100.0, OnRangeEndChanged, CoerceRangeEnd));

    /// <summary>
    /// Identifies the MinimumRange dependency property — the smallest distance allowed
    /// between RangeStart and RangeEnd. Set to 0 to allow them to coincide.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinimumRangeProperty =
        DependencyProperty.Register(nameof(MinimumRange), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(0.0, OnRangeBoundsChanged));

    /// <summary>
    /// Identifies the SmallChange dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SmallChangeProperty =
        DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Identifies the LargeChange dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LargeChangeProperty =
        DependencyProperty.Register(nameof(LargeChange), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(RangeSlider),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the TickFrequency dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TickFrequencyProperty =
        DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsSnapToTickEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSnapToTickEnabledProperty =
        DependencyProperty.Register(nameof(IsSnapToTickEnabled), typeof(bool), typeof(RangeSlider),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the TrackBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RangeSlider),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ThumbBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ThumbBrushProperty =
        DependencyProperty.Register(nameof(ThumbBrush), typeof(Brush), typeof(RangeSlider),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the RangeStartChanged routed event.
    /// </summary>
    public static readonly RoutedEvent RangeStartChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(RangeStartChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(RangeSlider));

    /// <summary>
    /// Identifies the RangeEndChanged routed event.
    /// </summary>
    public static readonly RoutedEvent RangeEndChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(RangeEndChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(RangeSlider));

    /// <summary>
    /// Occurs when the RangeStart property changes.
    /// </summary>
    public event RoutedPropertyChangedEventHandler<double> RangeStartChanged
    {
        add => AddHandler(RangeStartChangedEvent, value);
        remove => RemoveHandler(RangeStartChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the RangeEnd property changes.
    /// </summary>
    public event RoutedPropertyChangedEventHandler<double> RangeEndChanged
    {
        add => AddHandler(RangeEndChangedEvent, value);
        remove => RemoveHandler(RangeEndChangedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>Gets or sets the lower bound of the addressable range.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the upper bound of the addressable range.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the lower selected value of the range.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double RangeStart
    {
        get => (double)GetValue(RangeStartProperty)!;
        set => SetValue(RangeStartProperty, value);
    }

    /// <summary>Gets or sets the upper selected value of the range.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double RangeEnd
    {
        get => (double)GetValue(RangeEndProperty)!;
        set => SetValue(RangeEndProperty, value);
    }

    /// <summary>Gets or sets the minimum allowed gap between RangeStart and RangeEnd.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinimumRange
    {
        get => (double)GetValue(MinimumRangeProperty)!;
        set => SetValue(MinimumRangeProperty, value);
    }

    /// <summary>Gets or sets the value to add or subtract for arrow-key adjustments.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty)!;
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>Gets or sets the value to add or subtract for PageUp/PageDown adjustments.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double LargeChange
    {
        get => (double)GetValue(LargeChangeProperty)!;
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>Gets or sets the orientation of the slider.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets the interval between tick marks.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double TickFrequency
    {
        get => (double)GetValue(TickFrequencyProperty)!;
        set => SetValue(TickFrequencyProperty, value);
    }

    /// <summary>Gets or sets whether thumb movement snaps to tick marks.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSnapToTickEnabled
    {
        get => (bool)GetValue(IsSnapToTickEnabledProperty)!;
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

    /// <summary>Gets or sets the brush used for the unfilled track.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Gets or sets the brush used for the thumbs.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ThumbBrush
    {
        get => (Brush?)GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    #endregion

    #region Private Fields

    private const double ThumbSize = 16.0;
    private const double TrackThickness = 4.0;

    private enum ActiveThumb { None, Start, End }
    private ActiveThumb _activeDrag = ActiveThumb.None;
    private ActiveThumb _focusedThumb = ActiveThumb.Start;

    private Border? _trackBorder;
    private Border? _selectionRangeBorder;
    private Border? _startThumbBorder;
    private Border? _endThumbBorder;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="RangeSlider"/> class.
    /// </summary>
    public RangeSlider()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    /// <inheritdoc />
    protected override bool ShouldSuppressAutomaticTransition(DependencyProperty dp)
    {
        return _activeDrag != ActiveThumb.None &&
               (ReferenceEquals(dp, RangeStartProperty) || ReferenceEquals(dp, RangeEndProperty));
    }

    #region Template

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _trackBorder = GetTemplateChild("PART_Track") as Border;
        _selectionRangeBorder = GetTemplateChild("PART_SelectionRange") as Border;
        _startThumbBorder = GetTemplateChild("PART_StartThumb") as Border;
        _endThumbBorder = GetTemplateChild("PART_EndThumb") as Border;

        UpdateLayoutGeometry();
    }

    private void UpdateLayoutGeometry()
    {
        if (_startThumbBorder == null && _endThumbBorder == null)
        {
            return;
        }

        var range = Maximum - Minimum;
        var startPercent = range > 0 ? Math.Clamp((RangeStart - Minimum) / range, 0, 1) : 0;
        var endPercent = range > 0 ? Math.Clamp((RangeEnd - Minimum) / range, 0, 1) : 0;

        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = Math.Max(0, RenderSize.Width - ThumbSize);
            var startX = startPercent * trackWidth;
            var endX = endPercent * trackWidth;

            if (_startThumbBorder != null)
                _startThumbBorder.Margin = new Thickness(startX, 0, 0, 0);
            if (_endThumbBorder != null)
                _endThumbBorder.Margin = new Thickness(endX, 0, 0, 0);

            if (_selectionRangeBorder != null)
            {
                _selectionRangeBorder.Margin = new Thickness(startX + ThumbSize / 2, 0, 0, 0);
                _selectionRangeBorder.Width = Math.Max(0, endX - startX);
                _selectionRangeBorder.HorizontalAlignment = HorizontalAlignment.Left;
            }
        }
        else
        {
            var trackHeight = Math.Max(0, RenderSize.Height - ThumbSize);
            // Higher value at the top (matches Slider conventions).
            var startY = (1 - startPercent) * trackHeight;
            var endY = (1 - endPercent) * trackHeight;

            if (_startThumbBorder != null)
                _startThumbBorder.Margin = new Thickness(0, startY, 0, 0);
            if (_endThumbBorder != null)
                _endThumbBorder.Margin = new Thickness(0, endY, 0, 0);

            if (_selectionRangeBorder != null)
            {
                // Selection spans from end (top) down to start (bottom).
                _selectionRangeBorder.Margin = new Thickness(0, endY + ThumbSize / 2, 0, 0);
                _selectionRangeBorder.Height = Math.Max(0, startY - endY);
                _selectionRangeBorder.VerticalAlignment = VerticalAlignment.Top;
            }
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnSizeChanged(sizeInfo);
        UpdateLayoutGeometry();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);

        if (Orientation == Orientation.Horizontal)
        {
            var height = double.IsNaN(Height) || Height <= 0 ? 24 : Height;
            return new Size(
                Math.Min(availableSize.Width, double.IsPositiveInfinity(availableSize.Width) ? 200 : availableSize.Width),
                height);
        }

        var width = double.IsNaN(Width) || Width <= 0 ? 24 : Width;
        return new Size(
            width,
            Math.Min(availableSize.Height, double.IsPositiveInfinity(availableSize.Height) ? 200 : availableSize.Height));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Focus();
        var position = e.GetPosition(this);
        CaptureMouse();

        var startRect = GetThumbRect(ActiveThumb.Start);
        var endRect = GetThumbRect(ActiveThumb.End);

        // Prefer the thumb whose hit-box contains the click. If both contain it
        // (overlapping thumbs), pick the one closest to the cursor center.
        bool inStart = startRect.Contains(position);
        bool inEnd = endRect.Contains(position);

        ActiveThumb target;
        if (inStart && inEnd)
        {
            target = DistanceTo(startRect, position) <= DistanceTo(endRect, position)
                ? ActiveThumb.Start
                : ActiveThumb.End;
        }
        else if (inStart)
        {
            target = ActiveThumb.Start;
        }
        else if (inEnd)
        {
            target = ActiveThumb.End;
        }
        else
        {
            // Track click — move the closest thumb to the click position.
            var clickedValue = ValueFromPosition(position);
            target = Math.Abs(clickedValue - RangeStart) <= Math.Abs(clickedValue - RangeEnd)
                ? ActiveThumb.Start
                : ActiveThumb.End;
            SetThumbValue(target, clickedValue);
        }

        _activeDrag = target;
        _focusedThumb = target;
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _activeDrag != ActiveThumb.None)
        {
            _activeDrag = ActiveThumb.None;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_activeDrag == ActiveThumb.None)
        {
            return;
        }

        var position = e.GetPosition(this);
        var newValue = ValueFromPosition(position);
        SetThumbValue(_activeDrag, newValue);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_activeDrag != ActiveThumb.None)
        {
            _activeDrag = ActiveThumb.None;
            InvalidateVisual();
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab:
                _focusedThumb = _focusedThumb == ActiveThumb.Start ? ActiveThumb.End : ActiveThumb.Start;
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Down:
                AdjustFocusedThumb(-SmallChange);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Up:
                AdjustFocusedThumb(SmallChange);
                e.Handled = true;
                break;
            case Key.PageDown:
                AdjustFocusedThumb(-LargeChange);
                e.Handled = true;
                break;
            case Key.PageUp:
                AdjustFocusedThumb(LargeChange);
                e.Handled = true;
                break;
            case Key.Home:
                if (_focusedThumb == ActiveThumb.Start)
                    RangeStart = Minimum;
                else
                    RangeEnd = RangeStart + MinimumRange;
                e.Handled = true;
                break;
            case Key.End:
                if (_focusedThumb == ActiveThumb.End)
                    RangeEnd = Maximum;
                else
                    RangeStart = RangeEnd - MinimumRange;
                e.Handled = true;
                break;
        }
    }

    private void AdjustFocusedThumb(double delta)
    {
        if (_focusedThumb == ActiveThumb.Start)
        {
            SetThumbValue(ActiveThumb.Start, RangeStart + delta);
        }
        else
        {
            SetThumbValue(ActiveThumb.End, RangeEnd + delta);
        }
    }

    private double ValueFromPosition(Point position)
    {
        var range = Maximum - Minimum;
        if (range <= 0)
        {
            return Minimum;
        }

        double percentage;
        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = RenderSize.Width - ThumbSize;
            if (trackWidth <= 0) return Minimum;
            percentage = (position.X - ThumbSize / 2) / trackWidth;
        }
        else
        {
            var trackHeight = RenderSize.Height - ThumbSize;
            if (trackHeight <= 0) return Minimum;
            percentage = 1 - (position.Y - ThumbSize / 2) / trackHeight;
        }

        percentage = Math.Clamp(percentage, 0, 1);
        var raw = Minimum + percentage * range;

        if (IsSnapToTickEnabled && TickFrequency > 0)
        {
            raw = Math.Round((raw - Minimum) / TickFrequency) * TickFrequency + Minimum;
        }

        return raw;
    }

    private void SetThumbValue(ActiveThumb thumb, double value)
    {
        if (thumb == ActiveThumb.Start)
        {
            RangeStart = value;
        }
        else
        {
            RangeEnd = value;
        }
    }

    private Rect GetThumbRect(ActiveThumb thumb)
    {
        var range = Maximum - Minimum;
        if (range <= 0)
        {
            return Rect.Empty;
        }

        var raw = thumb == ActiveThumb.Start ? RangeStart : RangeEnd;
        var percentage = Math.Clamp((raw - Minimum) / range, 0, 1);

        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = RenderSize.Width - ThumbSize;
            var thumbX = percentage * trackWidth;
            var thumbY = (RenderSize.Height - ThumbSize) / 2;
            return new Rect(thumbX, thumbY, ThumbSize, ThumbSize);
        }
        else
        {
            var trackHeight = RenderSize.Height - ThumbSize;
            var thumbY = (1 - percentage) * trackHeight;
            var thumbX = (RenderSize.Width - ThumbSize) / 2;
            return new Rect(thumbX, thumbY, ThumbSize, ThumbSize);
        }
    }

    private static double DistanceTo(Rect rect, Point point)
    {
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var dx = cx - point.X;
        var dy = cy - point.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        // Template-driven layout updates the borders directly. Skip OnRender when a template is in use.
        if (_startThumbBorder != null || _endThumbBorder != null)
        {
            return;
        }

        var dc = drawingContext;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        DrawTrack(dc, bounds);
        DrawSelectionRange(dc, bounds);

        if (TickFrequency > 0)
        {
            DrawTicks(dc, bounds);
        }

        DrawThumb(dc, ActiveThumb.Start);
        DrawThumb(dc, ActiveThumb.End);
    }

    private void DrawTrack(DrawingContext dc, Rect bounds)
    {
        var trackBrush = TrackBrush ?? s_trackBrush;

        Rect trackRect;
        if (Orientation == Orientation.Horizontal)
        {
            var trackY = (bounds.Height - TrackThickness) / 2;
            trackRect = new Rect(ThumbSize / 2, trackY, bounds.Width - ThumbSize, TrackThickness);
        }
        else
        {
            var trackX = (bounds.Width - TrackThickness) / 2;
            trackRect = new Rect(trackX, ThumbSize / 2, TrackThickness, bounds.Height - ThumbSize);
        }

        dc.DrawRoundedRectangle(trackBrush, null, trackRect, 2, 2);
    }

    private void DrawSelectionRange(DrawingContext dc, Rect bounds)
    {
        var range = Maximum - Minimum;
        if (range <= 0)
        {
            return;
        }

        var startPercent = (RangeStart - Minimum) / range;
        var endPercent = (RangeEnd - Minimum) / range;
        var fillBrush = s_accentBrush;

        Rect filledRect;
        if (Orientation == Orientation.Horizontal)
        {
            var trackY = (bounds.Height - TrackThickness) / 2;
            var trackWidth = bounds.Width - ThumbSize;
            var startX = ThumbSize / 2 + startPercent * trackWidth;
            var endX = ThumbSize / 2 + endPercent * trackWidth;
            filledRect = new Rect(startX, trackY, Math.Max(0, endX - startX), TrackThickness);
        }
        else
        {
            var trackX = (bounds.Width - TrackThickness) / 2;
            var trackHeight = bounds.Height - ThumbSize;
            var startY = ThumbSize / 2 + (1 - startPercent) * trackHeight;
            var endY = ThumbSize / 2 + (1 - endPercent) * trackHeight;
            filledRect = new Rect(trackX, endY, TrackThickness, Math.Max(0, startY - endY));
        }

        dc.DrawRoundedRectangle(fillBrush, null, filledRect, 2, 2);
    }

    private void DrawTicks(DrawingContext dc, Rect bounds)
    {
        var range = Maximum - Minimum;
        if (range <= 0 || TickFrequency <= 0)
        {
            return;
        }

        var tickCount = (int)Math.Round(range / TickFrequency);
        for (var i = 0; i <= tickCount; i++)
        {
            var value = Minimum + i * TickFrequency;
            if (value > Maximum + 1e-10) break;
            var percentage = (value - Minimum) / range;

            if (Orientation == Orientation.Horizontal)
            {
                var x = ThumbSize / 2 + (bounds.Width - ThumbSize) * percentage;
                dc.DrawLine(s_tickPen, new Point(x, bounds.Height - 6), new Point(x, bounds.Height - 2));
            }
            else
            {
                var y = ThumbSize / 2 + (bounds.Height - ThumbSize) * (1 - percentage);
                dc.DrawLine(s_tickPen, new Point(bounds.Width - 6, y), new Point(bounds.Width - 2, y));
            }
        }
    }

    private void DrawThumb(DrawingContext dc, ActiveThumb thumb)
    {
        var rect = GetThumbRect(thumb);
        var thumbBrush = ThumbBrush ?? (_activeDrag == thumb
            ? s_accentPressedBrush
            : s_accentBrush);

        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var radius = ThumbSize / 2 - 1;

        dc.DrawEllipse(thumbBrush, null, new Point(centerX, centerY), radius, radius);
        dc.DrawEllipse(null, s_thumbBorderPen, new Point(centerX, centerY), radius - 1, radius - 1);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnRangeBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeSlider slider)
        {
            // Re-coerce both bounds so they remain inside [Minimum, Maximum] with MinimumRange respected.
            slider.CoerceRangeValuesAfterBoundsChange();
            slider.UpdateLayoutGeometry();
            slider.InvalidateVisual();
        }
    }

    private void CoerceRangeValuesAfterBoundsChange()
    {
        var coercedStart = (double)(CoerceRangeStart(this, RangeStart) ?? RangeStart);
        if (Math.Abs(coercedStart - RangeStart) > double.Epsilon)
        {
            RangeStart = coercedStart;
        }

        var coercedEnd = (double)(CoerceRangeEnd(this, RangeEnd) ?? RangeEnd);
        if (Math.Abs(coercedEnd - RangeEnd) > double.Epsilon)
        {
            RangeEnd = coercedEnd;
        }
    }

    private static void OnRangeStartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeSlider slider)
        {
            var oldVal = (double)(e.OldValue ?? 0.0);
            var newVal = (double)(e.NewValue ?? 0.0);
            slider.UpdateLayoutGeometry();
            slider.InvalidateVisual();
            slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldVal, newVal, RangeStartChangedEvent));
        }
    }

    private static void OnRangeEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeSlider slider)
        {
            var oldVal = (double)(e.OldValue ?? 0.0);
            var newVal = (double)(e.NewValue ?? 0.0);
            slider.UpdateLayoutGeometry();
            slider.InvalidateVisual();
            slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldVal, newVal, RangeEndChangedEvent));
        }
    }

    // Reads the *uncoerced local* value of another DP, falling back to its default. We deliberately
    // bypass GetValue to avoid mutual recursion with the other thumb's Coerce callback — running
    // both coerce paths against each other's effective values produces a fixed point that lets the
    // moving thumb push the stationary one forward instead of being clamped.
    private static double ReadLocalDouble(DependencyObject d, DependencyProperty dp, double fallback)
    {
        var local = d.ReadLocalValue(dp);
        return local is double v ? v : fallback;
    }

    private static object? CoerceRangeStart(DependencyObject d, object? value)
    {
        if (d is not RangeSlider slider || value is not double v)
        {
            return value;
        }

        var min = slider.Minimum;
        var max = slider.Maximum;
        if (max < min) max = min;

        var rangeEnd = ReadLocalDouble(slider, RangeEndProperty,
            (double)(RangeEndProperty.DefaultMetadata.DefaultValue ?? 100.0));
        var minGap = Math.Max(0, slider.MinimumRange);
        var upperBound = Math.Clamp(rangeEnd - minGap, min, max);

        if (v < min) return min;
        if (v > upperBound) return upperBound;
        return v;
    }

    private static object? CoerceRangeEnd(DependencyObject d, object? value)
    {
        if (d is not RangeSlider slider || value is not double v)
        {
            return value;
        }

        var min = slider.Minimum;
        var max = slider.Maximum;
        if (max < min) max = min;

        var rangeStart = ReadLocalDouble(slider, RangeStartProperty,
            (double)(RangeStartProperty.DefaultMetadata.DefaultValue ?? 0.0));
        var minGap = Math.Max(0, slider.MinimumRange);
        var lowerBound = Math.Clamp(rangeStart + minGap, min, max);

        if (v < lowerBound) return lowerBound;
        if (v > max) return max;
        return v;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeSlider slider)
        {
            slider.InvalidateMeasure();
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeSlider slider)
        {
            slider.InvalidateVisual();
        }
    }

    #endregion
}
