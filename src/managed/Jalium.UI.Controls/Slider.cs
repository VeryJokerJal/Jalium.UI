using Jalium.UI.Input;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that lets the user select from a range of values by moving a thumb.
/// </summary>
public class Slider : Jalium.UI.Controls.Primitives.RangeBase
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.SliderAutomationPeer(this);
    }

    // Cached brushes and pens for OnRender
    private static readonly SolidColorBrush s_trackBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_accentBrush = new(ThemeColors.SliderThumb);
    private static readonly SolidColorBrush s_accentPressedBrush = new(Color.FromRgb(0, 100, 190));
    private static readonly SolidColorBrush s_tickBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly Pen s_tickPen = new(s_tickBrush, 1);
    private static readonly SolidColorBrush s_whiteBrush = new(ThemeColors.CheckMark);
    private static readonly Pen s_thumbBorderPen = new(s_whiteBrush, 2);
    private static readonly RoutedCommand s_decreaseLarge = new(nameof(DecreaseLarge), typeof(Slider));
    private static readonly RoutedCommand s_decreaseSmall = new(nameof(DecreaseSmall), typeof(Slider));
    private static readonly RoutedCommand s_increaseLarge = new(nameof(IncreaseLarge), typeof(Slider));
    private static readonly RoutedCommand s_increaseSmall = new(nameof(IncreaseSmall), typeof(Slider));
    private static readonly RoutedCommand s_maximizeValue = new(nameof(MaximizeValue), typeof(Slider));
    private static readonly RoutedCommand s_minimizeValue = new(nameof(MinimizeValue), typeof(Slider));

    #region Dependency Properties

    public static readonly DependencyProperty AutoToolTipPlacementProperty =
        DependencyProperty.Register(nameof(AutoToolTipPlacement), typeof(AutoToolTipPlacement), typeof(Slider),
            new PropertyMetadata(AutoToolTipPlacement.None), IsValidAutoToolTipPlacement);

    public static readonly DependencyProperty AutoToolTipPrecisionProperty =
        DependencyProperty.Register(nameof(AutoToolTipPrecision), typeof(int), typeof(Slider),
            new PropertyMetadata(0), value => value is int precision && precision >= 0);

    public static readonly DependencyProperty DelayProperty =
        RepeatButton.DelayProperty.AddOwner(typeof(Slider),
            new PropertyMetadata(500, null, CoerceDelay));

    public static readonly DependencyProperty IntervalProperty =
        RepeatButton.IntervalProperty.AddOwner(typeof(Slider),
            new PropertyMetadata(33, null, CoerceInterval));

    public static readonly DependencyProperty IsDirectionReversedProperty =
        DependencyProperty.Register(nameof(IsDirectionReversed), typeof(bool), typeof(Slider),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    public static readonly DependencyProperty IsMoveToPointEnabledProperty =
        DependencyProperty.Register(nameof(IsMoveToPointEnabled), typeof(bool), typeof(Slider),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsSelectionRangeEnabledProperty =
        DependencyProperty.Register(nameof(IsSelectionRangeEnabled), typeof(bool), typeof(Slider),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register(nameof(SelectionStart), typeof(double), typeof(Slider),
            new PropertyMetadata(0.0, OnSelectionStartChanged, CoerceSelectionStart), IsFiniteDouble);

    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register(nameof(SelectionEnd), typeof(double), typeof(Slider),
            new PropertyMetadata(0.0, OnSelectionEndChanged, CoerceSelectionEnd), IsFiniteDouble);

    public static readonly DependencyProperty TickPlacementProperty =
        DependencyProperty.Register(nameof(TickPlacement), typeof(TickPlacement), typeof(Slider),
            new PropertyMetadata(TickPlacement.None, OnVisualPropertyChanged), IsValidTickPlacement);

    public static readonly DependencyProperty TicksProperty =
        DependencyProperty.Register(nameof(Ticks), typeof(Jalium.UI.Media.DoubleCollection), typeof(Slider),
            new PropertyMetadata(null, OnTicksChanged));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(Slider),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged),
            value => value is Orientation orientation && Enum.IsDefined(orientation));

    /// <summary>
    /// Identifies the TickFrequency dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TickFrequencyProperty =
        DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(Slider),
            new PropertyMetadata(1.0, OnVisualPropertyChanged), IsFiniteDouble);

    /// <summary>
    /// Identifies the IsSnapToTickEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSnapToTickEnabledProperty =
        DependencyProperty.Register(nameof(IsSnapToTickEnabled), typeof(bool), typeof(Slider),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the TrackBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(Slider),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ThumbBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ThumbBrushProperty =
        DependencyProperty.Register(nameof(ThumbBrush), typeof(Brush), typeof(Slider),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    public static RoutedCommand DecreaseLarge => s_decreaseLarge;
    public static RoutedCommand DecreaseSmall => s_decreaseSmall;
    public static RoutedCommand IncreaseLarge => s_increaseLarge;
    public static RoutedCommand IncreaseSmall => s_increaseSmall;
    public static RoutedCommand MaximizeValue => s_maximizeValue;
    public static RoutedCommand MinimizeValue => s_minimizeValue;

    public AutoToolTipPlacement AutoToolTipPlacement
    {
        get => (AutoToolTipPlacement)GetValue(AutoToolTipPlacementProperty)!;
        set => SetValue(AutoToolTipPlacementProperty, value);
    }

    public int AutoToolTipPrecision
    {
        get => (int)GetValue(AutoToolTipPrecisionProperty)!;
        set => SetValue(AutoToolTipPrecisionProperty, value);
    }

    public int Delay
    {
        get => (int)GetValue(DelayProperty)!;
        set
        {
            if (value < 0) throw new ArgumentException("Delay cannot be negative.", nameof(value));
            SetValue(DelayProperty, value);
        }
    }

    public int Interval
    {
        get => (int)GetValue(IntervalProperty)!;
        set
        {
            if (value <= 0) throw new ArgumentException("Interval must be positive.", nameof(value));
            SetValue(IntervalProperty, value);
        }
    }

    public bool IsDirectionReversed
    {
        get => (bool)GetValue(IsDirectionReversedProperty)!;
        set => SetValue(IsDirectionReversedProperty, value);
    }

    public bool IsMoveToPointEnabled
    {
        get => (bool)GetValue(IsMoveToPointEnabledProperty)!;
        set => SetValue(IsMoveToPointEnabledProperty, value);
    }

    public bool IsSelectionRangeEnabled
    {
        get => (bool)GetValue(IsSelectionRangeEnabledProperty)!;
        set => SetValue(IsSelectionRangeEnabledProperty, value);
    }

    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty)!;
        set => SetValue(SelectionStartProperty, value);
    }

    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty)!;
        set => SetValue(SelectionEndProperty, value);
    }

    public TickPlacement TickPlacement
    {
        get => (TickPlacement)GetValue(TickPlacementProperty)!;
        set => SetValue(TickPlacementProperty, value);
    }

    public Jalium.UI.Media.DoubleCollection Ticks
    {
        get
        {
            if (GetValue(TicksProperty) is Jalium.UI.Media.DoubleCollection ticks) return ticks;
            ticks = new Jalium.UI.Media.DoubleCollection();
            SetCurrentValue(TicksProperty, ticks);
            return ticks;
        }
        set => SetValue(TicksProperty, value);
    }

    /// <summary>
    /// Gets or sets the orientation of the Slider.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the interval between tick marks.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double TickFrequency
    {
        get => (double)GetValue(TickFrequencyProperty)!;
        set => SetValue(TickFrequencyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Slider snaps to tick marks.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSnapToTickEnabled
    {
        get => (bool)GetValue(IsSnapToTickEnabledProperty)!;
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the track.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the thumb.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ThumbBrush
    {
        get => (Brush?)GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    #endregion

    #region Private Fields

    private bool _isDragging;
    private Point _dragStartPoint;
    private const double ThumbSize = 16.0;
    private const double TrackThickness = 4.0;

    #endregion

    #region Template Parts

    private Border? _trackBorder;
    private Border? _selectionRangeBorder;
    private Border? _thumbBorder;

    #endregion

    #region Constructor

    static Slider()
    {
        MaximumProperty.OverrideMetadata(typeof(Slider), new PropertyMetadata(10.0));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Slider"/> class.
    /// </summary>
    public Slider()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        // Register input event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(RoutedCommand.ExecutedEvent, new ExecutedRoutedEventHandler(OnCommandExecuted));
        AddHandler(RoutedCommand.CanExecuteEvent, new CanExecuteRoutedEventHandler(OnCommandCanExecute));

        // Touch parallels mouse but with per-contact capture so a touch slide
        // is not interrupted by a second simultaneous contact elsewhere.
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler));
        AddHandler(TouchMoveEvent, new RoutedEventHandler(OnTouchMoveHandler));
        AddHandler(TouchUpEvent, new RoutedEventHandler(OnTouchUpHandler));
        AddHandler(LostTouchCaptureEvent, new RoutedEventHandler(OnLostTouchCaptureHandler));
    }

    private int _activeTouchId = -1;

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;

        _activeTouchId = touchArgs.TouchDevice.Id;
        CaptureTouch(touchArgs.TouchDevice);
        Focus();

        var position = touchArgs.GetTouchPoint(this).Position;
        SetValueFromPosition(position);
        OnThumbDragStarted(new DragStartedEventArgs(position.X, position.Y));
        InvalidateVisual();

        e.Handled = true;
    }

    private void OnTouchMoveHandler(object sender, RoutedEventArgs e)
    {
        if (!_isDragging || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        var position = touchArgs.GetTouchPoint(this).Position;
        OnThumbDragDelta(new DragDeltaEventArgs(position.X - _dragStartPoint.X, position.Y - _dragStartPoint.Y));
        _dragStartPoint = position;
        e.Handled = true;
    }

    private void OnTouchUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        ReleaseTouchCapture(touchArgs.TouchDevice);
        _activeTouchId = -1;
        if (_isDragging)
        {
            OnThumbDragCompleted(new DragCompletedEventArgs(0, 0, false));
        }
        e.Handled = true;
    }

    private void OnLostTouchCaptureHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        _activeTouchId = -1;
        if (_isDragging)
        {
            OnThumbDragCompleted(new DragCompletedEventArgs(0, 0, true));
        }
    }

    #endregion

    /// <inheritdoc />
    protected override bool ShouldSuppressAutomaticTransition(DependencyProperty dp)
    {
        return ReferenceEquals(dp, ValueProperty) && _isDragging;
    }

    #region Template

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _trackBorder = GetTemplateChild("PART_Track") as Border;
        _selectionRangeBorder = GetTemplateChild("PART_SelectionRange") as Border;
        _thumbBorder = GetTemplateChild("PART_Thumb") as Border;

        UpdateSliderLayout();
    }

    private void UpdateSliderLayout(double? currentValue = null)
    {
        if (_thumbBorder == null) return;

        var val = currentValue ?? Value;
        var percentage = GetVisualPercentage(val);

        if (Orientation == Orientation.Horizontal)
        {
            // OnApplyTemplate runs before the first arrange, when RenderSize is
            // commonly zero. Never write a negative computed Width into a
            // template child; FrameworkElement correctly rejects such sizes.
            var trackWidth = Math.Max(0, RenderSize.Width - ThumbSize);
            var thumbX = percentage * trackWidth;

            _thumbBorder.Margin = new Thickness(thumbX, 0, 0, 0);

            if (_selectionRangeBorder != null)
            {
                var first = IsSelectionRangeEnabled ? GetVisualPercentage(SelectionStart) : GetVisualPercentage(Minimum);
                var second = IsSelectionRangeEnabled ? GetVisualPercentage(SelectionEnd) : percentage;
                var low = Math.Min(first, second);
                var high = Math.Max(first, second);
                _selectionRangeBorder.Margin = new Thickness(ThumbSize / 2 + low * trackWidth, 0, 0, 0);
                _selectionRangeBorder.Width = (high - low) * trackWidth;
            }
        }
        else
        {
            var trackHeight = Math.Max(0, RenderSize.Height - ThumbSize);
            var thumbY = (1 - percentage) * trackHeight;

            _thumbBorder.Margin = new Thickness(0, thumbY, 0, 0);

            if (_selectionRangeBorder != null)
            {
                var first = IsSelectionRangeEnabled ? GetVisualPercentage(SelectionStart) : GetVisualPercentage(Minimum);
                var second = IsSelectionRangeEnabled ? GetVisualPercentage(SelectionEnd) : percentage;
                var low = Math.Min(first, second);
                var high = Math.Max(first, second);
                _selectionRangeBorder.Margin = new Thickness(0, ThumbSize / 2 + (1 - high) * trackHeight, 0, 0);
                _selectionRangeBorder.Height = (high - low) * trackHeight;
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
        UpdateSliderLayout();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // MUST measure template children so they get correct PreviousAvailableSize.
        base.MeasureOverride(availableSize);

        if (Orientation == Orientation.Horizontal)
        {
            var height = double.IsNaN(Height) || Height <= 0 ? 24 : Height;
            return new Size(Math.Min(availableSize.Width, double.IsPositiveInfinity(availableSize.Width) ? 200 : availableSize.Width), height);
        }
        else
        {
            var width = double.IsNaN(Width) || Width <= 0 ? 24 : Width;
            return new Size(width, Math.Min(availableSize.Height, double.IsPositiveInfinity(availableSize.Height) ? 200 : availableSize.Height));
        }
    }

    #endregion

    #region Input Handling

    /// <inheritdoc />
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.Handled || !IsEnabled || GetThumbRect().Contains(e.GetPosition(this)))
        {
            return;
        }

        Focus();
        var target = GetValueFromPosition(e.GetPosition(this));
        if (IsMoveToPointEnabled)
        {
            Value = target;
        }
        else if (target > Value)
        {
            OnIncreaseLarge();
        }
        else if (target < Value)
        {
            OnDecreaseLarge();
        }

        e.Handled = true;
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = e.GetPosition(this);
            var thumbRect = GetThumbRect();

            // Capture mouse for dragging
            CaptureMouse();

            if (thumbRect.Contains(position))
            {
                // Start dragging the thumb
                OnThumbDragStarted(new DragStartedEventArgs(position.X, position.Y));
            }
            else
            {
                if (IsMoveToPointEnabled)
                {
                    SetValueFromPosition(position);
                    OnThumbDragStarted(new DragStartedEventArgs(position.X, position.Y));
                }
                else if (GetValueFromPosition(position) > Value)
                {
                    OnIncreaseLarge();
                    ReleaseMouseCapture();
                }
                else
                {
                    OnDecreaseLarge();
                    ReleaseMouseCapture();
                }
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_isDragging)
            {
                OnThumbDragCompleted(new DragCompletedEventArgs(
                    e.GetPosition(this).X - _dragStartPoint.X,
                    e.GetPosition(this).Y - _dragStartPoint.Y,
                    false));
                ReleaseMouseCapture();
            }
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var position = e.GetPosition(this);
            OnThumbDragDelta(new DragDeltaEventArgs(position.X - _dragStartPoint.X, position.Y - _dragStartPoint.Y));
            _dragStartPoint = position;
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_isDragging)
        {
            OnThumbDragCompleted(new DragCompletedEventArgs(0, 0, true));
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
                if (IsDirectionReversed) OnIncreaseSmall(); else OnDecreaseSmall();
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Up:
                if (IsDirectionReversed) OnDecreaseSmall(); else OnIncreaseSmall();
                e.Handled = true;
                break;
            case Key.PageDown:
                OnDecreaseLarge();
                e.Handled = true;
                break;
            case Key.PageUp:
                OnIncreaseLarge();
                e.Handled = true;
                break;
            case Key.Home:
                OnMinimizeValue();
                e.Handled = true;
                break;
            case Key.End:
                OnMaximizeValue();
                e.Handled = true;
                break;
        }
    }

    private void OnCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (IsSliderCommand(e.Command))
        {
            e.CanExecute = IsEnabled;
            e.Handled = true;
        }
    }

    private void OnCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.Command, IncreaseLarge)) OnIncreaseLarge();
        else if (ReferenceEquals(e.Command, IncreaseSmall)) OnIncreaseSmall();
        else if (ReferenceEquals(e.Command, DecreaseLarge)) OnDecreaseLarge();
        else if (ReferenceEquals(e.Command, DecreaseSmall)) OnDecreaseSmall();
        else if (ReferenceEquals(e.Command, MaximizeValue)) OnMaximizeValue();
        else if (ReferenceEquals(e.Command, MinimizeValue)) OnMinimizeValue();
        else return;
        e.Handled = true;
    }

    private static bool IsSliderCommand(System.Windows.Input.ICommand command) =>
        ReferenceEquals(command, IncreaseLarge) || ReferenceEquals(command, IncreaseSmall) ||
        ReferenceEquals(command, DecreaseLarge) || ReferenceEquals(command, DecreaseSmall) ||
        ReferenceEquals(command, MaximizeValue) || ReferenceEquals(command, MinimizeValue);

    protected virtual void OnIncreaseLarge() => MoveValue(LargeChange);
    protected virtual void OnIncreaseSmall() => MoveValue(SmallChange);
    protected virtual void OnDecreaseLarge() => MoveValue(-LargeChange);
    protected virtual void OnDecreaseSmall() => MoveValue(-SmallChange);
    protected virtual void OnMaximizeValue() => Value = Maximum;
    protected virtual void OnMinimizeValue() => Value = Minimum;

    private void MoveValue(double change)
    {
        var target = Math.Clamp(Value + change, Minimum, Maximum);
        Value = IsSnapToTickEnabled
            ? SnapValue(target, change >= 0 ? 1 : -1)
            : target;
    }

    protected virtual void OnThumbDragStarted(DragStartedEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = new Point(e.HorizontalOffset, e.VerticalOffset);
        UpdateAutoToolTip();
        InvalidateVisual();
    }

    protected virtual void OnThumbDragDelta(DragDeltaEventArgs e)
    {
        var trackLength = Orientation == Orientation.Horizontal
            ? RenderSize.Width - ThumbSize
            : RenderSize.Height - ThumbSize;
        if (trackLength <= 0 || Maximum <= Minimum) return;
        var pixelDelta = Orientation == Orientation.Horizontal ? e.HorizontalChange : -e.VerticalChange;
        if (IsDirectionReversed) pixelDelta = -pixelDelta;
        var target = Value + pixelDelta / trackLength * (Maximum - Minimum);
        Value = IsSnapToTickEnabled
            ? SnapValue(target, pixelDelta >= 0 ? 1 : -1)
            : Math.Clamp(target, Minimum, Maximum);
        UpdateAutoToolTip();
    }

    protected virtual void OnThumbDragCompleted(DragCompletedEventArgs e)
    {
        _isDragging = false;
        ToolTipService.HideToolTip(this);
        InvalidateVisual();
    }

    private void UpdateAutoToolTip()
    {
        if (AutoToolTipPlacement == AutoToolTipPlacement.None) return;
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetPlacement(this, AutoToolTipPlacement == AutoToolTipPlacement.TopLeft
            ? (Orientation == Orientation.Horizontal ? PlacementMode.Top : PlacementMode.Left)
            : (Orientation == Orientation.Horizontal ? PlacementMode.Bottom : PlacementMode.Right));
        var text = Value.ToString($"F{AutoToolTipPrecision}", System.Globalization.CultureInfo.CurrentCulture);
        var thumb = GetThumbRect();
        ToolTipService.ShowToolTip(this, text, new Point(thumb.X + thumb.Width / 2, thumb.Y + thumb.Height / 2));
    }

    private void SetValueFromPosition(Point position)
    {
        Value = GetValueFromPosition(position);
    }

    private double GetValueFromPosition(Point position)
    {
        var range = Maximum - Minimum;
        if (range <= 0) return Minimum;

        double percentage;
        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = RenderSize.Width - ThumbSize;
            if (trackWidth <= 0) return Value;
            percentage = (position.X - ThumbSize / 2) / trackWidth;
        }
        else
        {
            var trackHeight = RenderSize.Height - ThumbSize;
            if (trackHeight <= 0) return Value;
            percentage = 1 - (position.Y - ThumbSize / 2) / trackHeight;
        }

        percentage = Math.Clamp(percentage, 0, 1);
        if (IsDirectionReversed) percentage = 1 - percentage;
        var newValue = Minimum + percentage * range;
        return IsSnapToTickEnabled ? SnapValue(newValue, 0) : newValue;
    }

    private double SnapValue(double value, int direction)
    {
        value = Math.Clamp(value, Minimum, Maximum);
        var candidates = GetTickValues().OrderBy(candidate => candidate).ToArray();
        if (candidates.Length == 0) return value;
        var snapped = candidates
            .OrderBy(candidate => Math.Abs(candidate - value))
            .ThenBy(candidate => direction > 0 ? -candidate : candidate)
            .First();

        // A directional keyboard/command move must make progress even when the
        // requested delta is smaller than half a tick interval.
        if (direction > 0 && snapped <= Value + 1e-10)
            return candidates.FirstOrDefault(candidate => candidate > Value + 1e-10, Maximum);
        if (direction < 0 && snapped >= Value - 1e-10)
            return candidates.LastOrDefault(candidate => candidate < Value - 1e-10, Minimum);
        return snapped;
    }

    private IEnumerable<double> GetTickValues()
    {
        yield return Minimum;
        if (GetValue(TicksProperty) is Jalium.UI.Media.DoubleCollection ticks && ticks.Count > 0)
        {
            foreach (var tick in ticks)
                if (double.IsFinite(tick) && tick > Minimum && tick < Maximum) yield return tick;
        }
        else if (TickFrequency > 0 && double.IsFinite(TickFrequency))
        {
            for (var tick = Minimum + TickFrequency; tick < Maximum; tick += TickFrequency)
                yield return tick;
        }
        yield return Maximum;
    }

    private Rect GetThumbRect()
    {
        var percentage = GetVisualPercentage(Value);

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

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        // Template-based rendering: layout is updated from OnSizeChanged and
        // property callbacks (NOT here). Modifying child Margin/Width during OnRender
        // triggers InvalidateMeasure, but UpdateLayout() already ran for this frame.
        if (_thumbBorder != null)
            return;

        var dc = drawingContext;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw track
        DrawTrack(dc, bounds);

        // Draw filled portion
        DrawFilledTrack(dc, bounds);

        // Draw tick marks if enabled
        if (TickPlacement != TickPlacement.None)
        {
            DrawTicks(dc, bounds);
        }

        // Draw thumb
        DrawThumb(dc);
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

    private void DrawFilledTrack(DrawingContext dc, Rect bounds)
    {
        var filledBrush = s_accentBrush;
        var startPercentage = IsSelectionRangeEnabled ? GetVisualPercentage(SelectionStart) : GetVisualPercentage(Minimum);
        var endPercentage = IsSelectionRangeEnabled ? GetVisualPercentage(SelectionEnd) : GetVisualPercentage(Value);
        var lowPercentage = Math.Min(startPercentage, endPercentage);
        var highPercentage = Math.Max(startPercentage, endPercentage);

        Rect filledRect;
        if (Orientation == Orientation.Horizontal)
        {
            var trackY = (bounds.Height - TrackThickness) / 2;
            var trackWidth = bounds.Width - ThumbSize;
            filledRect = new Rect(
                ThumbSize / 2 + trackWidth * lowPercentage,
                trackY,
                trackWidth * (highPercentage - lowPercentage),
                TrackThickness);
        }
        else
        {
            var trackX = (bounds.Width - TrackThickness) / 2;
            var trackHeight = bounds.Height - ThumbSize;
            filledRect = new Rect(
                trackX,
                ThumbSize / 2 + trackHeight * (1 - highPercentage),
                TrackThickness,
                trackHeight * (highPercentage - lowPercentage));
        }

        dc.DrawRoundedRectangle(filledBrush, null, filledRect, 2, 2);
    }

    private void DrawTicks(DrawingContext dc, Rect bounds)
    {
        if (TickPlacement == TickPlacement.None || Maximum <= Minimum) return;
        foreach (var value in GetTickValues())
        {
            var percentage = GetVisualPercentage(value);

            if (Orientation == Orientation.Horizontal)
            {
                var x = ThumbSize / 2 + (bounds.Width - ThumbSize) * percentage;
                if (TickPlacement is TickPlacement.TopLeft or TickPlacement.Both)
                    dc.DrawLine(s_tickPen, new Point(x, 2), new Point(x, 6));
                if (TickPlacement is TickPlacement.BottomRight or TickPlacement.Both)
                    dc.DrawLine(s_tickPen, new Point(x, bounds.Height - 6), new Point(x, bounds.Height - 2));
            }
            else
            {
                var y = ThumbSize / 2 + (bounds.Height - ThumbSize) * (1 - percentage);
                if (TickPlacement is TickPlacement.TopLeft or TickPlacement.Both)
                    dc.DrawLine(s_tickPen, new Point(2, y), new Point(6, y));
                if (TickPlacement is TickPlacement.BottomRight or TickPlacement.Both)
                    dc.DrawLine(s_tickPen, new Point(bounds.Width - 6, y), new Point(bounds.Width - 2, y));
            }
        }
    }

    private void DrawThumb(DrawingContext dc)
    {
        var thumbRect = GetThumbRect();
        var thumbBrush = ThumbBrush ?? (_isDragging
            ? s_accentPressedBrush
            : s_accentBrush);

        var centerX = thumbRect.X + thumbRect.Width / 2;
        var centerY = thumbRect.Y + thumbRect.Height / 2;
        var radius = ThumbSize / 2 - 1;

        // Draw thumb circle
        dc.DrawEllipse(thumbBrush, null, new Point(centerX, centerY), radius, radius);

        // Draw thumb border
        dc.DrawEllipse(null, s_thumbBorderPen, new Point(centerX, centerY), radius - 1, radius - 1);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            slider.UpdateSliderLayout();
            slider.InvalidateMeasure();
            slider.InvalidateVisual();
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            slider.InvalidateVisual();
        }
    }

    private static void OnSelectionStartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider) return;
        if (slider.SelectionEnd < slider.SelectionStart)
            slider.SelectionEnd = slider.SelectionStart;
        slider.UpdateSliderLayout();
        slider.InvalidateVisual();
    }

    private static void OnSelectionEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            slider.UpdateSliderLayout();
            slider.InvalidateVisual();
        }
    }

    private static object? CoerceSelectionStart(DependencyObject d, object? value) =>
        d is Slider slider && value is double number ? Math.Clamp(number, slider.Minimum, slider.Maximum) : value;

    private static object? CoerceSelectionEnd(DependencyObject d, object? value) =>
        d is Slider slider && value is double number
            ? Math.Clamp(number, Math.Max(slider.Minimum, slider.SelectionStart), slider.Maximum)
            : value;

    private void CoerceSelectionRange()
    {
        SelectionStart = Math.Clamp(SelectionStart, Minimum, Maximum);
        SelectionEnd = Math.Clamp(SelectionEnd, SelectionStart, Maximum);
    }

    private static void OnTicksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider) return;
        if (e.OldValue is Jalium.UI.Media.DoubleCollection oldTicks) oldTicks.Changed -= slider.OnTicksCollectionChanged;
        if (e.NewValue is Jalium.UI.Media.DoubleCollection newTicks) newTicks.Changed += slider.OnTicksCollectionChanged;
        slider.InvalidateVisual();
    }

    private void OnTicksCollectionChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static bool IsFiniteDouble(object? value) => value is double number && double.IsFinite(number);
    private static bool IsValidAutoToolTipPlacement(object? value) =>
        value is AutoToolTipPlacement placement && Enum.IsDefined(placement);
    private static bool IsValidTickPlacement(object? value) =>
        value is TickPlacement placement && Enum.IsDefined(placement);
    private static object? CoerceDelay(DependencyObject d, object? value) =>
        value is int delay && delay >= 0 ? delay : throw new ArgumentException("Delay cannot be negative.", nameof(value));
    private static object? CoerceInterval(DependencyObject d, object? value) =>
        value is int interval && interval > 0 ? interval : throw new ArgumentException("Interval must be positive.", nameof(value));

    /// <summary>
    /// Called when the Value property changes.
    /// </summary>
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        UpdateSliderLayout(newValue);
        InvalidateVisual();
        base.OnValueChanged(oldValue, newValue);
    }

    private double GetVisualPercentage(double value)
    {
        var range = Maximum - Minimum;
        var percentage = range > 0 ? Math.Clamp((value - Minimum) / range, 0, 1) : 0;
        return IsDirectionReversed ? 1 - percentage : percentage;
    }

    /// <inheritdoc />
    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        CoerceSelectionRange();
        UpdateSliderLayout();
        base.OnMinimumChanged(oldMinimum, newMinimum);
    }

    /// <inheritdoc />
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        CoerceSelectionRange();
        UpdateSliderLayout();
        base.OnMaximumChanged(oldMaximum, newMaximum);
    }

    #endregion
}
