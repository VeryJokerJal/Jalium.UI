using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a text box for entering numeric values with spin buttons for increment/decrement.
/// </summary>
public class NumberBox : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumberBox),
            new PropertyMetadata(0.0, OnValueChanged, CoerceValue));

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberBox),
            new PropertyMetadata(double.MinValue, OnRangeChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberBox),
            new PropertyMetadata(double.MaxValue, OnRangeChanged));

    /// <summary>
    /// Identifies the SmallChange dependency property.
    /// </summary>
    public static readonly DependencyProperty SmallChangeProperty =
        DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(NumberBox),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Identifies the LargeChange dependency property.
    /// </summary>
    public static readonly DependencyProperty LargeChangeProperty =
        DependencyProperty.Register(nameof(LargeChange), typeof(double), typeof(NumberBox),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the SpinButtonPlacementMode dependency property.
    /// </summary>
    public static readonly DependencyProperty SpinButtonPlacementModeProperty =
        DependencyProperty.Register(nameof(SpinButtonPlacementMode), typeof(NumberBoxSpinButtonPlacementMode), typeof(NumberBox),
            new PropertyMetadata(NumberBoxSpinButtonPlacementMode.Inline, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the PlaceholderText dependency property.
    /// </summary>
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(NumberBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(NumberBox),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the NumberFormatter dependency property.
    /// </summary>
    public static readonly DependencyProperty NumberFormatterProperty =
        DependencyProperty.Register(nameof(NumberFormatter), typeof(string), typeof(NumberBox),
            new PropertyMetadata("G", OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the AcceptsExpression dependency property.
    /// </summary>
    public static readonly DependencyProperty AcceptsExpressionProperty =
        DependencyProperty.Register(nameof(AcceptsExpression), typeof(bool), typeof(NumberBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsWrapEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsWrapEnabledProperty =
        DependencyProperty.Register(nameof(IsWrapEnabled), typeof(bool), typeof(NumberBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(NumberBox),
            new PropertyMetadata("0", OnTextChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ValueChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(NumberBox));

    /// <summary>
    /// Occurs when the Value property changes.
    /// </summary>
    public event RoutedPropertyChangedEventHandler<double> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the numeric value.
    /// </summary>
    public double Value
    {
        get => (double)(GetValue(ValueProperty) ?? 0.0);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum value.
    /// </summary>
    public double Minimum
    {
        get => (double)(GetValue(MinimumProperty) ?? double.MinValue);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value.
    /// </summary>
    public double Maximum
    {
        get => (double)(GetValue(MaximumProperty) ?? double.MaxValue);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the small change value (for arrow keys).
    /// </summary>
    public double SmallChange
    {
        get => (double)(GetValue(SmallChangeProperty) ?? 1.0);
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>
    /// Gets or sets the large change value (for page up/down).
    /// </summary>
    public double LargeChange
    {
        get => (double)(GetValue(LargeChangeProperty) ?? 10.0);
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>
    /// Gets or sets the spin button placement mode.
    /// </summary>
    public NumberBoxSpinButtonPlacementMode SpinButtonPlacementMode
    {
        get => (NumberBoxSpinButtonPlacementMode)(GetValue(SpinButtonPlacementModeProperty) ?? NumberBoxSpinButtonPlacementMode.Inline);
        set => SetValue(SpinButtonPlacementModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text.
    /// </summary>
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the number format string.
    /// </summary>
    public string NumberFormatter
    {
        get => (string)(GetValue(NumberFormatterProperty) ?? "G");
        set => SetValue(NumberFormatterProperty, value);
    }

    /// <summary>
    /// Gets or sets whether mathematical expressions are accepted.
    /// </summary>
    public bool AcceptsExpression
    {
        get => (bool)(GetValue(AcceptsExpressionProperty) ?? false);
        set => SetValue(AcceptsExpressionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the value wraps around at min/max.
    /// </summary>
    public bool IsWrapEnabled
    {
        get => (bool)(GetValue(IsWrapEnabledProperty) ?? false);
        set => SetValue(IsWrapEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the text representation of the value.
    /// </summary>
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? "0");
        set => SetValue(TextProperty, value);
    }

    #endregion

    #region Private Fields

    private const double SpinButtonWidth = 32;
    private const double SpinButtonHeight = 16;
    private const double DefaultHeight = 32;
    private bool _isEditing;
    private string _editText = "";
    private Rect _upButtonRect;
    private Rect _downButtonRect;
    private Rect _textRect;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="NumberBox"/> class.
    /// </summary>
    public NumberBox()
    {
        Focusable = true;
        Height = DefaultHeight;
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 4, 8, 4);
        CornerRadius = new CornerRadius(4);

        // Register input handlers
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new RoutedEventHandler(OnTextInputHandler));
        AddHandler(LostFocusEvent, new RoutedEventHandler(OnLostFocusHandler));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = mouseArgs.GetPosition(this);

            if (SpinButtonPlacementMode != NumberBoxSpinButtonPlacementMode.Hidden)
            {
                if (_upButtonRect.Contains(position))
                {
                    StepUp();
                    e.Handled = true;
                    return;
                }

                if (_downButtonRect.Contains(position))
                {
                    StepDown();
                    e.Handled = true;
                    return;
                }
            }

            // Start editing
            if (_textRect.Contains(position))
            {
                StartEditing();
                e.Handled = true;
            }
        }
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is KeyEventArgs keyArgs)
        {
            switch (keyArgs.Key)
            {
                case Key.Up:
                    StepUp();
                    e.Handled = true;
                    break;
                case Key.Down:
                    StepDown();
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    Value += LargeChange;
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    Value -= LargeChange;
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (_isEditing)
                    {
                        CommitEdit();
                    }
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_isEditing)
                    {
                        CancelEdit();
                    }
                    e.Handled = true;
                    break;
                case Key.Back:
                    if (_isEditing && _editText.Length > 0)
                    {
                        _editText = _editText.Substring(0, _editText.Length - 1);
                        InvalidateVisual();
                    }
                    e.Handled = true;
                    break;
            }
        }
    }

    private void OnTextInputHandler(object sender, RoutedEventArgs e)
    {
        if (!_isEditing) return;

        if (e is TextCompositionEventArgs textArgs)
        {
            var input = textArgs.Text;
            if (!string.IsNullOrEmpty(input))
            {
                // Allow digits, minus sign, decimal point
                foreach (var c in input)
                {
                    if (char.IsDigit(c) || c == '-' || c == '.' || c == ',')
                    {
                        _editText += c;
                    }
                }
                InvalidateVisual();
            }
            e.Handled = true;
        }
    }

    private void OnLostFocusHandler(object sender, RoutedEventArgs e)
    {
        if (_isEditing)
        {
            CommitEdit();
        }
    }

    #endregion

    #region Edit Operations

    private void StartEditing()
    {
        _isEditing = true;
        _editText = Value.ToString(NumberFormatter);
        InvalidateVisual();
    }

    private void CommitEdit()
    {
        _isEditing = false;
        if (double.TryParse(_editText, out var newValue))
        {
            Value = newValue;
        }
        InvalidateVisual();
    }

    private void CancelEdit()
    {
        _isEditing = false;
        _editText = "";
        InvalidateVisual();
    }

    #endregion

    #region Step Operations

    private void StepUp()
    {
        var newValue = Value + SmallChange;
        if (IsWrapEnabled && newValue > Maximum)
        {
            newValue = Minimum;
        }
        Value = newValue;
    }

    private void StepDown()
    {
        var newValue = Value - SmallChange;
        if (IsWrapEnabled && newValue < Minimum)
        {
            newValue = Maximum;
        }
        Value = newValue;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var border = BorderThickness;
        var headerHeight = 0.0;

        // Measure header
        if (Header is string headerText)
        {
            var headerFormatted = new FormattedText(headerText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14);
            TextMeasurement.MeasureText(headerFormatted);
            headerHeight = headerFormatted.Height + 4;
        }

        var width = double.IsPositiveInfinity(availableSize.Width) ? 120 : availableSize.Width;
        var height = DefaultHeight + headerHeight;

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var cornerRadius = CornerRadius;
        var hasCornerRadius = cornerRadius.TopLeft > 0;
        var headerHeight = 0.0;

        // Draw header
        if (Header is string headerText && Foreground != null)
        {
            var headerFormatted = new FormattedText(headerText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
            {
                Foreground = Foreground
            };
            TextMeasurement.MeasureText(headerFormatted);
            dc.DrawText(headerFormatted, new Point(0, 0));
            headerHeight = headerFormatted.Height + 4;
        }

        // Adjust rect for header
        var inputRect = new Rect(0, headerHeight, rect.Width, rect.Height - headerHeight);

        // Draw background
        if (Background != null)
        {
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(Background, null, inputRect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(Background, null, inputRect);
            }
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(null, pen, inputRect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, pen, inputRect);
            }
        }

        // Calculate regions
        var spinButtonWidth = SpinButtonPlacementMode == NumberBoxSpinButtonPlacementMode.Hidden ? 0 : SpinButtonWidth;
        _textRect = new Rect(padding.Left, inputRect.Top + padding.Top, inputRect.Width - spinButtonWidth - padding.TotalWidth, inputRect.Height - padding.TotalHeight);

        if (SpinButtonPlacementMode == NumberBoxSpinButtonPlacementMode.Inline)
        {
            _upButtonRect = new Rect(inputRect.Right - SpinButtonWidth, inputRect.Top, SpinButtonWidth, inputRect.Height / 2);
            _downButtonRect = new Rect(inputRect.Right - SpinButtonWidth, inputRect.Top + inputRect.Height / 2, SpinButtonWidth, inputRect.Height / 2);

            // Draw spin buttons
            DrawSpinButton(dc, _upButtonRect, true);
            DrawSpinButton(dc, _downButtonRect, false);
        }

        // Draw value/text
        var displayText = _isEditing ? _editText : Value.ToString(NumberFormatter);
        if (string.IsNullOrEmpty(displayText) && !string.IsNullOrEmpty(PlaceholderText))
        {
            displayText = PlaceholderText;
            var placeholderFormatted = new FormattedText(displayText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128))
            };
            TextMeasurement.MeasureText(placeholderFormatted);
            var textY = inputRect.Top + (inputRect.Height - placeholderFormatted.Height) / 2;
            dc.DrawText(placeholderFormatted, new Point(_textRect.Left, textY));
        }
        else if (!string.IsNullOrEmpty(displayText))
        {
            var valueFormatted = new FormattedText(displayText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
            {
                Foreground = Foreground ?? new SolidColorBrush(Color.White)
            };
            TextMeasurement.MeasureText(valueFormatted);
            var textY = inputRect.Top + (inputRect.Height - valueFormatted.Height) / 2;
            dc.DrawText(valueFormatted, new Point(_textRect.Left, textY));

            // Draw caret if editing
            if (_isEditing && IsFocused)
            {
                var caretX = _textRect.Left + valueFormatted.Width + 1;
                var caretPen = new Pen(Foreground ?? new SolidColorBrush(Color.White), 1);
                dc.DrawLine(caretPen, new Point(caretX, textY), new Point(caretX, textY + valueFormatted.Height));
            }
        }
    }

    private void DrawSpinButton(DrawingContext dc, Rect rect, bool isUp)
    {
        // Draw button background
        var buttonBg = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        dc.DrawRectangle(buttonBg, null, rect);

        // Draw arrow
        var arrowBrush = new SolidColorBrush(Color.White);
        var arrowPen = new Pen(arrowBrush, 1.5);
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var arrowSize = 4;

        if (isUp)
        {
            dc.DrawLine(arrowPen, new Point(centerX - arrowSize, centerY + arrowSize / 2), new Point(centerX, centerY - arrowSize / 2));
            dc.DrawLine(arrowPen, new Point(centerX, centerY - arrowSize / 2), new Point(centerX + arrowSize, centerY + arrowSize / 2));
        }
        else
        {
            dc.DrawLine(arrowPen, new Point(centerX - arrowSize, centerY - arrowSize / 2), new Point(centerX, centerY + arrowSize / 2));
            dc.DrawLine(arrowPen, new Point(centerX, centerY + arrowSize / 2), new Point(centerX + arrowSize, centerY - arrowSize / 2));
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberBox numberBox)
        {
            numberBox.Text = ((double)e.NewValue).ToString(numberBox.NumberFormatter);
            numberBox.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
                (double)e.OldValue, (double)e.NewValue, ValueChangedEvent));
            numberBox.InvalidateVisual();
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Text changed externally, try to parse
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberBox numberBox)
        {
            // Re-coerce value
            var coerced = (double)(CoerceValue(numberBox, numberBox.Value) ?? numberBox.Value);
            if (coerced != numberBox.Value)
            {
                numberBox.Value = coerced;
            }
        }
    }

    private static object? CoerceValue(DependencyObject d, object? value)
    {
        if (d is NumberBox numberBox && value is double doubleValue)
        {
            return Math.Clamp(doubleValue, numberBox.Minimum, numberBox.Maximum);
        }
        return value;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberBox numberBox)
        {
            numberBox.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberBox numberBox)
        {
            numberBox.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Specifies the placement mode for NumberBox spin buttons.
/// </summary>
public enum NumberBoxSpinButtonPlacementMode
{
    /// <summary>
    /// Spin buttons are hidden.
    /// </summary>
    Hidden,

    /// <summary>
    /// Spin buttons are placed inline (right side).
    /// </summary>
    Inline,

    /// <summary>
    /// Spin buttons are placed compactly.
    /// </summary>
    Compact
}
