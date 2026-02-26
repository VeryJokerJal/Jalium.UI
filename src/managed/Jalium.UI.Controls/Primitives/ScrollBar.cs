using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that provides a scroll bar for scrolling content.
/// </summary>
public sealed class ScrollBar : RangeBase
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultTrackBrush = new(Color.FromRgb(40, 40, 40));
    private static readonly SolidColorBrush s_defaultThumbBrush = new(Color.FromRgb(170, 170, 170));
    private static readonly SolidColorBrush s_defaultArrowBrush = new(Color.FromRgb(210, 210, 210));
    private static readonly SolidColorBrush s_transparentBrush = new(Color.FromArgb(0, 0, 0, 0));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ScrollBar),
            new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ViewportSize dependency property.
    /// </summary>
    public static readonly DependencyProperty ViewportSizeProperty =
        DependencyProperty.Register(nameof(ViewportSize), typeof(double), typeof(ScrollBar),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Scroll routed event.
    /// </summary>
    public static readonly RoutedEvent ScrollEvent =
        EventManager.RegisterRoutedEvent(nameof(Scroll), RoutingStrategy.Bubble,
            typeof(ScrollEventHandler), typeof(ScrollBar));

    /// <summary>
    /// Occurs when the Scroll event is raised.
    /// </summary>
    public event ScrollEventHandler Scroll
    {
        add => AddHandler(ScrollEvent, value);
        remove => RemoveHandler(ScrollEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the orientation of the ScrollBar.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the viewport, which determines the thumb size.
    /// </summary>
    public double ViewportSize
    {
        get => (double)GetValue(ViewportSizeProperty)!;
        set => SetValue(ViewportSizeProperty, value);
    }

    #endregion

    #region Private Fields

    private Track? _track;
    private RepeatButton? _lineUpButton;
    private RepeatButton? _lineDownButton;
    private const double DefaultThickness = 16;
    private const double MinThumbLength = 20;
    private bool _isDragging;
    private bool _hasCustomLineButtonStyle;
    private const string ScrollBarStyleKey = "ScrollBarStyle";
    private const string LineButtonStyleKey = "ScrollBarLineButtonStyle";
    private const string PageButtonStyleKey = "ScrollBarPageButtonStyle";
    private const string ThumbStyleKey = "ScrollBarThumbStyle";

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollBar"/> class.
    /// </summary>
    public ScrollBar()
    {
        // Set default values for range base
        Maximum = 100;
        SmallChange = 1;
        LargeChange = 10;
        Background = s_defaultTrackBrush;
        BorderBrush = s_transparentBrush;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(2);
        CornerRadius = new CornerRadius(5);

        // Create visual children
        CreateVisualChildren();

        // Register event handlers
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseWheelEvent, new RoutedEventHandler(OnMouseWheelHandler));
    }

    private void CreateVisualChildren()
    {
        // Create line up/left button
        _lineUpButton = new RepeatButton
        {
            Focusable = false,
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Foreground = s_defaultArrowBrush,
            UseScrollBarArrowAnimation = true,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _lineUpButton.Cursor = Jalium.UI.Cursors.Arrow;
        _lineUpButton.Click += OnLineUpClick;
        AddVisualChild(_lineUpButton);

        // Create track
        _track = new Track();
        _track.Thumb = new Thumb
        {
            Background = s_defaultThumbBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        _track.Thumb.Cursor = Jalium.UI.Cursors.Arrow;
        _track.Thumb.DragStarted += OnThumbDragStarted;
        _track.Thumb.DragDelta += OnThumbDragDelta;
        _track.Thumb.DragCompleted += OnThumbDragCompleted;

        _track.DecreaseRepeatButton = new RepeatButton
        {
            Focusable = false,
            Opacity = 0,
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _track.DecreaseRepeatButton.Cursor = Jalium.UI.Cursors.Arrow;
        _track.DecreaseRepeatButton.Click += OnPageUpClick;

        _track.IncreaseRepeatButton = new RepeatButton
        {
            Focusable = false,
            Opacity = 0,
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _track.IncreaseRepeatButton.Cursor = Jalium.UI.Cursors.Arrow;
        _track.IncreaseRepeatButton.Click += OnPageDownClick;

        AddVisualChild(_track);

        // Create line down/right button
        _lineDownButton = new RepeatButton
        {
            Focusable = false,
            Background = s_transparentBrush,
            BorderBrush = s_transparentBrush,
            BorderThickness = new Thickness(0),
            Foreground = s_defaultArrowBrush,
            UseScrollBarArrowAnimation = true,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinWidth = 0,
            MinHeight = 0
        };
        _lineDownButton.Cursor = Jalium.UI.Cursors.Arrow;
        _lineDownButton.Click += OnLineDownClick;
        AddVisualChild(_lineDownButton);

        ApplySelfStyle();
        ApplyPartStyles();
        UpdateLineButtonDirectionTags();
        UpdateTrackBindings();
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        ApplySelfStyle();
        ApplyPartStyles();
    }

    private void UpdateTrackBindings()
    {
        if (_track != null)
        {
            _track.Minimum = Minimum;
            _track.Maximum = Maximum;
            _track.Value = Value;
            _track.ViewportSize = ViewportSize;
            _track.Orientation = Orientation;
        }

        UpdateLineButtonDirectionTags();
    }

    private void ApplyPartStyles()
    {
        var lineButtonStyle = TryFindResource(LineButtonStyleKey) as Style;
        _hasCustomLineButtonStyle = lineButtonStyle != null;

        if (_lineUpButton != null)
        {
            _lineUpButton.Style = lineButtonStyle ?? _lineUpButton.Style;
        }

        if (_lineDownButton != null)
        {
            _lineDownButton.Style = lineButtonStyle ?? _lineDownButton.Style;
        }

        if (_track?.DecreaseRepeatButton != null)
        {
            _track.DecreaseRepeatButton.Style = TryFindResource(PageButtonStyleKey) as Style ?? _track.DecreaseRepeatButton.Style;
        }

        if (_track?.IncreaseRepeatButton != null)
        {
            _track.IncreaseRepeatButton.Style = TryFindResource(PageButtonStyleKey) as Style ?? _track.IncreaseRepeatButton.Style;
        }

        if (_track?.Thumb != null)
        {
            _track.Thumb.Style = TryFindResource(ThumbStyleKey) as Style ?? _track.Thumb.Style;
        }
    }

    private void ApplySelfStyle()
    {
        if (Style != null)
        {
            return;
        }

        if (TryFindResource(ScrollBarStyleKey) is Style explicitStyle)
        {
            Style = explicitStyle;
            return;
        }

        if (TryFindResource(typeof(ScrollBar)) is Style implicitStyle)
        {
            Style = implicitStyle;
        }
    }

    private void UpdateLineButtonDirectionTags()
    {
        if (_lineUpButton == null || _lineDownButton == null)
            return;

        if (Orientation == Orientation.Vertical)
        {
            _lineUpButton.Tag = "Up";
            _lineDownButton.Tag = "Down";
        }
        else
        {
            _lineUpButton.Tag = "Left";
            _lineDownButton.Tag = "Right";
        }
    }

    #endregion

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount => 3; // LineUp, Track, LineDown

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        return index switch
        {
            0 => _lineUpButton,
            1 => _track,
            2 => _lineDownButton,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Orientation == Orientation.Vertical)
        {
            var width = double.IsNaN(Width) || Width <= 0 ? DefaultThickness : Width;
            var buttonHeight = width; // Square buttons

            _lineUpButton?.Measure(new Size(width, buttonHeight));
            _lineDownButton?.Measure(new Size(width, buttonHeight));

            var trackHeight = Math.Max(0, availableSize.Height - buttonHeight * 2);
            _track?.Measure(new Size(width, trackHeight));

            var height = double.IsPositiveInfinity(availableSize.Height)
                ? buttonHeight * 2 + MinThumbLength * 2
                : availableSize.Height;

            return new Size(width, height);
        }
        else
        {
            var height = double.IsNaN(Height) || Height <= 0 ? DefaultThickness : Height;
            var buttonWidth = height; // Square buttons

            _lineUpButton?.Measure(new Size(buttonWidth, height));
            _lineDownButton?.Measure(new Size(buttonWidth, height));

            var trackWidth = Math.Max(0, availableSize.Width - buttonWidth * 2);
            _track?.Measure(new Size(trackWidth, height));

            var width = double.IsPositiveInfinity(availableSize.Width)
                ? buttonWidth * 2 + MinThumbLength * 2
                : availableSize.Width;

            return new Size(width, height);
        }
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        ApplySelfStyle();
        ApplyPartStyles();
        UpdateTrackBindings();

        if (Orientation == Orientation.Vertical)
        {
            var buttonSize = finalSize.Width;
            var trackHeight = Math.Max(0, finalSize.Height - buttonSize * 2);

            _lineUpButton?.Arrange(new Rect(0, 0, finalSize.Width, buttonSize));
            _track?.Arrange(new Rect(0, buttonSize, finalSize.Width, trackHeight));
            _lineDownButton?.Arrange(new Rect(0, finalSize.Height - buttonSize, finalSize.Width, buttonSize));
        }
        else
        {
            var buttonSize = finalSize.Height;
            var trackWidth = Math.Max(0, finalSize.Width - buttonSize * 2);

            _lineUpButton?.Arrange(new Rect(0, 0, buttonSize, finalSize.Height));
            _track?.Arrange(new Rect(buttonSize, 0, trackWidth, finalSize.Height));
            _lineDownButton?.Arrange(new Rect(finalSize.Width - buttonSize, 0, buttonSize, finalSize.Height));
        }

        return finalSize;
    }

    #endregion

    #region Event Handlers

    private void OnLineUpClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Max(Minimum, Value - SmallChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.SmallDecrement);
        }
    }

    private void OnLineDownClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Min(Maximum, Value + SmallChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.SmallIncrement);
        }
    }

    private void OnPageUpClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Max(Minimum, Value - LargeChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.LargeDecrement);
        }
    }

    private void OnPageDownClick(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Min(Maximum, Value + LargeChange);
        if (Math.Abs(newValue - Value) > double.Epsilon)
        {
            Value = newValue;
            RaiseScrollEvent(ScrollEventType.LargeIncrement);
        }
    }

    private void OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        _isDragging = true;
    }

    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_track != null)
        {
            var valueDelta = _track.ValueFromDistance(e.HorizontalChange, e.VerticalChange);
            var newValue = Math.Clamp(Value + valueDelta, Minimum, Maximum);

            if (Math.Abs(newValue - Value) > double.Epsilon)
            {
                Value = newValue;
                RaiseScrollEvent(ScrollEventType.ThumbTrack);
            }
        }
    }

    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDragging = false;
        RaiseScrollEvent(ScrollEventType.EndScroll);
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();
        }
    }

    private void OnMouseWheelHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseWheelEventArgs wheelArgs)
        {
            var delta = wheelArgs.Delta > 0 ? -SmallChange * 3 : SmallChange * 3;
            var newValue = Math.Clamp(Value + delta, Minimum, Maximum);

            if (Math.Abs(newValue - Value) > double.Epsilon)
            {
                Value = newValue;
                RaiseScrollEvent(delta < 0 ? ScrollEventType.SmallDecrement : ScrollEventType.SmallIncrement);
            }

            e.Handled = true;
        }
    }

    private void RaiseScrollEvent(ScrollEventType scrollType)
    {
        RaiseEvent(new ScrollEventArgs(ScrollEvent, scrollType, Value)
        {
            Source = this
        });
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollBar scrollBar)
        {
            scrollBar.UpdateTrackBindings();
            scrollBar.InvalidateMeasure();
        }
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateTrackBindings();
    }

    /// <inheritdoc />
    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        UpdateTrackBindings();
    }

    /// <inheritdoc />
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        UpdateTrackBindings();
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var innerRect = new Rect(
            Padding.Left,
            Padding.Top,
            Math.Max(0, RenderSize.Width - Padding.Left - Padding.Right),
            Math.Max(0, RenderSize.Height - Padding.Top - Padding.Bottom));
        if (innerRect.Width <= 0 || innerRect.Height <= 0)
        {
            return;
        }

        var bgBrush = Background ?? s_defaultTrackBrush;
        dc.DrawRoundedRectangle(bgBrush, null, innerRect, CornerRadius);

        if (BorderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var borderPen = new Pen(BorderBrush, BorderThickness.Left);
            dc.DrawRoundedRectangle(null, borderPen, innerRect, CornerRadius);
        }

        // Fallback: if line-button styles are missing, draw simple arrows directly.
        if (!_hasCustomLineButtonStyle)
        {
            DrawFallbackArrows(dc);
        }
    }

    private void DrawFallbackArrows(DrawingContext dc)
    {
        const double baseArrowSize = 8.0;
        var upBrush = (_lineUpButton?.Foreground as Brush) ?? s_defaultArrowBrush;
        var downBrush = (_lineDownButton?.Foreground as Brush) ?? s_defaultArrowBrush;
        var upScale = Math.Clamp(_lineUpButton?.CurrentScrollBarArrowScale ?? 1.0, 0.7, 1.25);
        var downScale = Math.Clamp(_lineDownButton?.CurrentScrollBarArrowScale ?? 1.0, 0.7, 1.25);
        var upArrowSize = baseArrowSize * upScale;
        var downArrowSize = baseArrowSize * downScale;

        if (Orientation == Orientation.Vertical)
        {
            var buttonSize = RenderSize.Width;
            if (buttonSize <= 0 || RenderSize.Height < buttonSize * 2)
                return;

            var topCenter = new Point(RenderSize.Width / 2, buttonSize / 2);
            var bottomCenter = new Point(RenderSize.Width / 2, RenderSize.Height - buttonSize / 2);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                upBrush,
                new Rect(topCenter.X - upArrowSize / 2, topCenter.Y - upArrowSize / 2, upArrowSize, upArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Up);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                downBrush,
                new Rect(bottomCenter.X - downArrowSize / 2, bottomCenter.Y - downArrowSize / 2, downArrowSize, downArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Down);
        }
        else
        {
            var buttonSize = RenderSize.Height;
            if (buttonSize <= 0 || RenderSize.Width < buttonSize * 2)
                return;

            var leftCenter = new Point(buttonSize / 2, RenderSize.Height / 2);
            var rightCenter = new Point(RenderSize.Width - buttonSize / 2, RenderSize.Height / 2);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                upBrush,
                new Rect(leftCenter.X - upArrowSize / 2, leftCenter.Y - upArrowSize / 2, upArrowSize, upArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Left);

            Jalium.UI.Controls.ArrowIcons.DrawArrow(
                dc,
                downBrush,
                new Rect(rightCenter.X - downArrowSize / 2, rightCenter.Y - downArrowSize / 2, downArrowSize, downArrowSize),
                Jalium.UI.Controls.ArrowIcons.Direction.Right);
        }
    }

    #endregion
}
