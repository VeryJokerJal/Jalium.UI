using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that allows the user to select a date.
/// </summary>
public class DatePicker : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedDate dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DatePicker),
            new PropertyMetadata(null, OnSelectedDateChanged));

    /// <summary>
    /// Identifies the DisplayDate dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayDateProperty =
        DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(DatePicker),
            new PropertyMetadata(DateTime.Today));

    /// <summary>
    /// Identifies the DisplayDateStart dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayDateStartProperty =
        DependencyProperty.Register(nameof(DisplayDateStart), typeof(DateTime?), typeof(DatePicker),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the DisplayDateEnd dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register(nameof(DisplayDateEnd), typeof(DateTime?), typeof(DatePicker),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(DatePicker),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(DatePicker),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the PlaceholderText dependency property.
    /// </summary>
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(DatePicker),
            new PropertyMetadata("Select a date", OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DateFormat dependency property.
    /// </summary>
    public static readonly DependencyProperty DateFormatProperty =
        DependencyProperty.Register(nameof(DateFormat), typeof(string), typeof(DatePicker),
            new PropertyMetadata("d", OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the SelectedDateChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedDateChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedDateChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(DatePicker));

    /// <summary>
    /// Identifies the CalendarOpened routed event.
    /// </summary>
    public static readonly RoutedEvent CalendarOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(CalendarOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DatePicker));

    /// <summary>
    /// Identifies the CalendarClosed routed event.
    /// </summary>
    public static readonly RoutedEvent CalendarClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(CalendarClosed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DatePicker));

    /// <summary>
    /// Occurs when the selected date changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs> SelectedDateChanged
    {
        add => AddHandler(SelectedDateChangedEvent, value);
        remove => RemoveHandler(SelectedDateChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the calendar is opened.
    /// </summary>
    public event RoutedEventHandler CalendarOpened
    {
        add => AddHandler(CalendarOpenedEvent, value);
        remove => RemoveHandler(CalendarOpenedEvent, value);
    }

    /// <summary>
    /// Occurs when the calendar is closed.
    /// </summary>
    public event RoutedEventHandler CalendarClosed
    {
        add => AddHandler(CalendarClosedEvent, value);
        remove => RemoveHandler(CalendarClosedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the currently selected date.
    /// </summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>
    /// Gets or sets the date to display when the calendar is opened.
    /// </summary>
    public DateTime DisplayDate
    {
        get => (DateTime)(GetValue(DisplayDateProperty) ?? DateTime.Today);
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>
    /// Gets or sets the first date that can be selected.
    /// </summary>
    public DateTime? DisplayDateStart
    {
        get => (DateTime?)GetValue(DisplayDateStartProperty);
        set => SetValue(DisplayDateStartProperty, value);
    }

    /// <summary>
    /// Gets or sets the last date that can be selected.
    /// </summary>
    public DateTime? DisplayDateEnd
    {
        get => (DateTime?)GetValue(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the calendar dropdown is open.
    /// </summary>
    public bool IsDropDownOpen
    {
        get => (bool)(GetValue(IsDropDownOpenProperty) ?? false);
        set => SetValue(IsDropDownOpenProperty, value);
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
    /// Gets or sets the placeholder text displayed when no date is selected.
    /// </summary>
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the format string for displaying the date.
    /// </summary>
    public string DateFormat
    {
        get => (string)(GetValue(DateFormatProperty) ?? "d");
        set => SetValue(DateFormatProperty, value);
    }

    #endregion

    #region Private Fields

    private const double DefaultHeight = 32;
    private const double DropdownButtonWidth = 32;
    private Rect _dropdownButtonRect;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DatePicker"/> class.
    /// </summary>
    public DatePicker()
    {
        Focusable = true;
        Height = DefaultHeight;
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 4, 8, 4);
        CornerRadius = new CornerRadius(4);

        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
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

            // Toggle dropdown when clicking anywhere
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is KeyEventArgs keyArgs)
        {
            switch (keyArgs.Key)
            {
                case Key.Enter:
                case Key.Space:
                    IsDropDownOpen = !IsDropDownOpen;
                    e.Handled = true;
                    break;
                case Key.Escape when IsDropDownOpen:
                    IsDropDownOpen = false;
                    e.Handled = true;
                    break;
                case Key.Down when keyArgs.KeyboardModifiers.HasFlag(ModifierKeys.Alt):
                    IsDropDownOpen = true;
                    e.Handled = true;
                    break;
            }
        }
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

        var width = double.IsPositiveInfinity(availableSize.Width) ? 200 : availableSize.Width;
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
        var borderBrush = IsFocused ? new SolidColorBrush(Color.FromRgb(0, 120, 212)) : BorderBrush;
        if (borderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(borderBrush, BorderThickness.Left);
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(null, pen, inputRect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, pen, inputRect);
            }
        }

        // Draw date text or placeholder
        var textRect = new Rect(padding.Left, inputRect.Top + padding.Top,
            inputRect.Width - DropdownButtonWidth - padding.TotalWidth,
            inputRect.Height - padding.TotalHeight);

        string displayText;
        Brush textBrush;

        if (SelectedDate.HasValue)
        {
            displayText = SelectedDate.Value.ToString(DateFormat);
            textBrush = Foreground ?? new SolidColorBrush(Color.White);
        }
        else
        {
            displayText = PlaceholderText ?? "Select a date";
            textBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        }

        var textFormatted = new FormattedText(displayText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
        {
            Foreground = textBrush
        };
        TextMeasurement.MeasureText(textFormatted);

        var textY = inputRect.Top + (inputRect.Height - textFormatted.Height) / 2;
        dc.DrawText(textFormatted, new Point(padding.Left, textY));

        // Draw dropdown button
        _dropdownButtonRect = new Rect(inputRect.Right - DropdownButtonWidth, inputRect.Top, DropdownButtonWidth, inputRect.Height);
        DrawDropdownButton(dc, _dropdownButtonRect);
    }

    private void DrawDropdownButton(DrawingContext dc, Rect rect)
    {
        // Draw calendar icon (simplified)
        var iconBrush = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        var iconPen = new Pen(iconBrush, 1.5);

        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var iconSize = 14;

        // Draw calendar outline
        var calRect = new Rect(centerX - iconSize / 2, centerY - iconSize / 2, iconSize, iconSize);
        dc.DrawRectangle(null, iconPen, calRect);

        // Draw calendar header bar
        dc.DrawLine(iconPen, new Point(calRect.Left, calRect.Top + 4), new Point(calRect.Right, calRect.Top + 4));

        // Draw calendar hangers
        dc.DrawLine(iconPen, new Point(calRect.Left + 3, calRect.Top - 2), new Point(calRect.Left + 3, calRect.Top + 2));
        dc.DrawLine(iconPen, new Point(calRect.Right - 3, calRect.Top - 2), new Point(calRect.Right - 3, calRect.Top + 2));
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.InvalidateVisual();

            var args = new SelectionChangedEventArgs(SelectedDateChangedEvent,
                e.OldValue != null ? new[] { e.OldValue } : Array.Empty<object>(),
                e.NewValue != null ? new[] { e.NewValue } : Array.Empty<object>());
            datePicker.RaiseEvent(args);
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            if ((bool)e.NewValue)
            {
                datePicker.RaiseEvent(new RoutedEventArgs(CalendarOpenedEvent, datePicker));
            }
            else
            {
                datePicker.RaiseEvent(new RoutedEventArgs(CalendarClosedEvent, datePicker));
            }
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.InvalidateVisual();
        }
    }

    #endregion
}
