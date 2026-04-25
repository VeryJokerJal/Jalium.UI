using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that allows the user to select a time.
/// </summary>
public class TimePicker : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.TimePickerAutomationPeer(this);
    }

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedTimeProperty =
        DependencyProperty.Register(nameof(SelectedTime), typeof(TimeSpan?), typeof(TimePicker),
            new PropertyMetadata(null, OnSelectedTimeChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ClockIdentifierProperty =
        DependencyProperty.Register(nameof(ClockIdentifier), typeof(string), typeof(TimePicker),
            new PropertyMetadata("12HourClock", OnClockIdentifierChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinuteIncrementProperty =
        DependencyProperty.Register(nameof(MinuteIncrement), typeof(int), typeof(TimePicker),
            new PropertyMetadata(1));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(TimePicker),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(TimePicker),
            new PropertyMetadata("Select a time", OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(TimePicker),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    #endregion

    #region Routed Events

    public static readonly RoutedEvent SelectedTimeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedTimeChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<TimePickerSelectedValueChangedEventArgs>), typeof(TimePicker));

    public event EventHandler<TimePickerSelectedValueChangedEventArgs> SelectedTimeChanged
    {
        add => AddHandler(SelectedTimeChangedEvent, value);
        remove => RemoveHandler(SelectedTimeChangedEvent, value);
    }

    #endregion

    #region CLR Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public TimeSpan? SelectedTime
    {
        get => (TimeSpan?)GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string ClockIdentifier
    {
        get => (string)(GetValue(ClockIdentifierProperty) ?? "12HourClock");
        set => SetValue(ClockIdentifierProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int MinuteIncrement
    {
        get => (int)GetValue(MinuteIncrementProperty)!;
        set => SetValue(MinuteIncrementProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    #endregion

    #region Private Fields

    private const double DefaultHeight = 32;
    private const double DropdownButtonWidth = 32;
    private Rect _dropdownButtonRect;

    // Popup
    private Popup? _popup;
    private Border? _flyoutBorder;
    private StackPanel? _hourColumn;
    private StackPanel? _minuteColumn;
    private StackPanel? _periodColumn;
    private bool _isCloseAnimating;
    private bool _isOpen;

    // Pending selection
    private int _pendingHour;
    private int _pendingMinute;
    private int _pendingPeriodIndex; // 0=AM, 1=PM

    private const double ColumnWidth = 70;
    private const double ItemHeight = 40;
    private const double FlyoutMaxHeight = 260;

    // Highlight colors
    private static readonly SolidColorBrush SelectedItemBg = new(ThemeColors.SelectedItemBackground);
    private static readonly SolidColorBrush TransparentBg = new(Color.Transparent);

    // Static brushes & pens for rendering
    private static readonly SolidColorBrush s_focusBorderBrush = new(ThemeColors.ControlBorderFocused);
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_placeholderBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_iconBrush = new(Color.FromRgb(160, 160, 160));

    #endregion

    #region Constructor

    public TimePicker()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Popup Management

    private bool Is24Hour => ClockIdentifier == "24HourClock";

    private void EnsurePopup()
    {
        if (_popup != null) return;
        BuildPopup();
    }

    private void BuildPopup()
    {
        var is24Hour = Is24Hour;

        // Hour column
        _hourColumn = new StackPanel { Orientation = Orientation.Vertical };
        PopulateHourColumn(is24Hour);
        var hourScroll = new ScrollViewer
        {
            Content = _hourColumn,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Width = ColumnWidth,
            MaxHeight = FlyoutMaxHeight - ItemHeight
        };

        // Minute column
        _minuteColumn = new StackPanel { Orientation = Orientation.Vertical };
        PopulateMinuteColumn();
        var minuteScroll = new ScrollViewer
        {
            Content = _minuteColumn,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Width = ColumnWidth,
            MaxHeight = FlyoutMaxHeight - ItemHeight
        };

        // Columns container
        var columnsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        columnsPanel.Children.Add(hourScroll);
        columnsPanel.Children.Add(CreateSeparator());
        columnsPanel.Children.Add(minuteScroll);

        // AM/PM column (only for 12-hour)
        if (!is24Hour)
        {
            _periodColumn = new StackPanel { Orientation = Orientation.Vertical };
            PopulatePeriodColumn();
            var periodScroll = new ScrollViewer
            {
                Content = _periodColumn,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Width = 60,
                MaxHeight = FlyoutMaxHeight - ItemHeight
            };
            columnsPanel.Children.Add(CreateSeparator());
            columnsPanel.Children.Add(periodScroll);
        }

        // Accept button
        var acceptButton = new Button
        {
            Content = "\u2713",
            Height = ItemHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = ResolvePopupBrush("AccentBrush", new SolidColorBrush(ThemeColors.Accent)),
            Foreground = ResolvePopupBrush("TextPrimary", new SolidColorBrush(Color.White)),
            FontSize = 18,
            CornerRadius = new CornerRadius(0, 0, 6, 6),
            BorderThickness = new Thickness(0)
        };
        acceptButton.Click += OnAcceptClicked;

        // Main layout
        var mainPanel = new StackPanel { Orientation = Orientation.Vertical };
        mainPanel.Children.Add(columnsPanel);
        mainPanel.Children.Add(acceptButton);

        _flyoutBorder = new Border
        {
            Background = ResolvePopupBrush("SurfaceBackground", new SolidColorBrush(Color.FromRgb(45, 45, 45))),
            BorderBrush = ResolvePopupBrush("ControlBorder", new SolidColorBrush(Color.FromRgb(67, 67, 70))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 4, 0, 0),
            Child = mainPanel
        };

        _popup = new Popup
        {
            Child = _flyoutBorder,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };
        _popup.Closed += OnPopupClosed;
    }

    private Border CreateSeparator()
    {
        return new Border
        {
            Width = 1,
            Background = ResolvePopupBrush("ControlBorder", new SolidColorBrush(Color.FromRgb(67, 67, 70)))
        };
    }

    private void PopulateHourColumn(bool is24Hour)
    {
        var maxHour = is24Hour ? 23 : 12;
        var minHour = is24Hour ? 0 : 1;

        for (int h = minHour; h <= maxHour; h++)
        {
            var hour = h;
            var item = CreateTimeItem(is24Hour ? h.ToString("D2") : h.ToString(), () =>
            {
                _pendingHour = hour;
                UpdateColumnHighlights();
            });
            _hourColumn!.Children.Add(item);
        }
    }

    private void PopulateMinuteColumn()
    {
        var increment = Math.Max(1, MinuteIncrement);
        for (int m = 0; m < 60; m += increment)
        {
            var minute = m;
            var item = CreateTimeItem(m.ToString("D2"), () =>
            {
                _pendingMinute = minute;
                UpdateColumnHighlights();
            });
            _minuteColumn!.Children.Add(item);
        }
    }

    private void PopulatePeriodColumn()
    {
        _periodColumn!.Children.Add(CreateTimeItem("AM", () =>
        {
            _pendingPeriodIndex = 0;
            UpdateColumnHighlights();
        }));
        _periodColumn.Children.Add(CreateTimeItem("PM", () =>
        {
            _pendingPeriodIndex = 1;
            UpdateColumnHighlights();
        }));
    }

    private Border CreateTimeItem(string text, Action onClick)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 16,
            Foreground = ResolvePopupBrush("TextPrimary", new SolidColorBrush(Color.White)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Height = ItemHeight,
            Child = textBlock,
            Background = TransparentBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4)
        };

        border.AddHandler(MouseDownEvent, new MouseButtonEventHandler((s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                onClick();
                e.Handled = true;
            }
        }));

        return border;
    }

    private void UpdateColumnHighlights()
    {
        var is24Hour = Is24Hour;

        // Update hour column
        if (_hourColumn != null)
        {
            var minHour = is24Hour ? 0 : 1;
            for (int i = 0; i < _hourColumn.Children.Count; i++)
            {
                if (_hourColumn.Children[i] is Border border)
                {
                    var hourValue = minHour + i;
                    border.Background = hourValue == _pendingHour ? ResolveSelectedItemBackground() : TransparentBg;
                }
            }
        }

        // Update minute column
        if (_minuteColumn != null)
        {
            var increment = Math.Max(1, MinuteIncrement);
            for (int i = 0; i < _minuteColumn.Children.Count; i++)
            {
                if (_minuteColumn.Children[i] is Border border)
                {
                    var minuteValue = i * increment;
                    border.Background = minuteValue == _pendingMinute ? ResolveSelectedItemBackground() : TransparentBg;
                }
            }
        }

        // Update period column
        if (_periodColumn != null)
        {
            for (int i = 0; i < _periodColumn.Children.Count; i++)
            {
                if (_periodColumn.Children[i] is Border border)
                {
                    border.Background = i == _pendingPeriodIndex ? ResolveSelectedItemBackground() : TransparentBg;
                }
            }
        }
    }

    private void OpenDropDown()
    {
        if (_isOpen) return;

        _isCloseAnimating = false;

        // Rebuild popup if clock format changed
        if (_popup != null)
        {
            var hasperiod = _periodColumn != null;
            var needsPeriod = !Is24Hour;
            if (hasperiod != needsPeriod)
            {
                _popup = null;
            }
        }

        EnsurePopup();

        // Initialize pending values from current selection
        var current = SelectedTime ?? TimeSpan.Zero;
        if (Is24Hour)
        {
            _pendingHour = current.Hours;
        }
        else
        {
            _pendingHour = current.Hours % 12;
            if (_pendingHour == 0) _pendingHour = 12;
            _pendingPeriodIndex = current.Hours >= 12 ? 1 : 0;
        }

        // Snap minute to nearest increment
        var increment = Math.Max(1, MinuteIncrement);
        _pendingMinute = (current.Minutes / increment) * increment;

        UpdateColumnHighlights();

        _popup!.IsOpen = true;
        _isOpen = true;

        AnimateOpen();
    }

    private void CloseDropDown()
    {
        if (!_isOpen) return;
        _isOpen = false;

        AnimateClose();
    }

    private void OnAcceptClicked(object sender, RoutedEventArgs e)
    {
        int hour24;

        if (Is24Hour)
        {
            hour24 = _pendingHour;
        }
        else
        {
            hour24 = _pendingHour % 12;
            if (_pendingPeriodIndex == 1) hour24 += 12; // PM
        }

        SelectedTime = new TimeSpan(hour24, _pendingMinute, 0);
        IsDropDownOpen = false;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (_isCloseAnimating) return;

        _isOpen = false;
        if (_flyoutBorder != null)
        {
            _flyoutBorder.Opacity = 1;
            _flyoutBorder.RenderOffset = default;
        }
        SetValue(IsDropDownOpenProperty, false);
    }

    #endregion

    #region Animation

    private void AnimateOpen()
    {
        if (_flyoutBorder != null)
        {
            _flyoutBorder.Opacity = 1;
            _flyoutBorder.RenderOffset = default;
        }
    }

    private void AnimateClose()
    {
        _isCloseAnimating = true;
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        if (_flyoutBorder != null)
        {
            _flyoutBorder.Opacity = 1;
            _flyoutBorder.RenderOffset = default;
        }

        _isCloseAnimating = false;
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        switch (e.Key)
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
            case Key.Up:
                IncrementTime(TimeSpan.FromMinutes(MinuteIncrement));
                e.Handled = true;
                break;
            case Key.Down:
                IncrementTime(TimeSpan.FromMinutes(-MinuteIncrement));
                e.Handled = true;
                break;
        }
    }

    private void IncrementTime(TimeSpan delta)
    {
        var current = SelectedTime ?? TimeSpan.Zero;
        var newTime = current.Add(delta);

        if (newTime.TotalHours >= 24)
            newTime = newTime.Subtract(TimeSpan.FromHours(24));
        else if (newTime.TotalHours < 0)
            newTime = newTime.Add(TimeSpan.FromHours(24));

        SelectedTime = newTime;
    }

    #endregion

    #region Formatting

    private string FormatTime(TimeSpan time)
    {
        if (Is24Hour)
        {
            return $"{time.Hours:D2}:{time.Minutes:D2}";
        }
        else
        {
            var hour = time.Hours % 12;
            if (hour == 0) hour = 12;
            var period = time.Hours >= 12 ? "PM" : "AM";
            return $"{hour}:{time.Minutes:D2} {period}";
        }
    }

    #endregion

    #region Layout

    protected override Size MeasureOverride(Size availableSize)
    {
        var headerHeight = 0.0;

        if (Header is string headerText)
        {
            var headerFormatted = new FormattedText(headerText, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14);
            TextMeasurement.MeasureText(headerFormatted);
            headerHeight = headerFormatted.Height + 4;
        }

        var width = double.IsPositiveInfinity(availableSize.Width) ? 150 : availableSize.Width;
        var height = DefaultHeight + headerHeight;

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var cornerRadius = CornerRadius;
        var headerHeight = 0.0;

        // Draw header
        if (Header is string headerText && Foreground != null)
        {
            var headerFormatted = new FormattedText(headerText, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
            {
                Foreground = Foreground
            };
            TextMeasurement.MeasureText(headerFormatted);
            dc.DrawText(headerFormatted, new Point(0, 0));
            headerHeight = headerFormatted.Height + 4;
        }

        var inputRect = new Rect(0, headerHeight, rect.Width, rect.Height - headerHeight);
        var strokeThickness = BorderThickness.Left;
        var borderRect = ControlRenderGeometry.GetStrokeAlignedRect(inputRect, strokeThickness);
        var borderRadius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(cornerRadius, strokeThickness);

        // Draw background
        if (Background != null)
        {
            dc.DrawRoundedRectangle(Background, null, borderRect, borderRadius);
        }

        // Draw border
        var borderBrush = IsKeyboardFocused ? ResolveFocusedBorderBrush() : BorderBrush;
        if (borderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(borderBrush, strokeThickness);
            dc.DrawRoundedRectangle(null, pen, borderRect, borderRadius);
        }

        // Focus indicator is painted by FocusVisualManager into the adorner layer.

        // Draw time text or placeholder
        string displayText;
        Brush textBrush;

        if (SelectedTime.HasValue)
        {
            displayText = FormatTime(SelectedTime.Value);
            textBrush = Foreground ?? s_whiteBrush;
        }
        else
        {
            displayText = PlaceholderText ?? "Select a time";
            textBrush = ResolvePlaceholderBrush();
        }

        var textFormatted = new FormattedText(displayText, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
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

    private Brush ResolveFocusedBorderBrush()
    {
        return TryFindResource("ControlBorderFocused") as Brush ?? s_focusBorderBrush;
    }

    private Brush ResolvePlaceholderBrush()
    {
        return TryFindResource("TextPlaceholder") as Brush ?? s_placeholderBrush;
    }

    private Brush ResolveSecondaryTextBrush()
    {
        return TryFindResource("TextSecondary") as Brush ?? s_iconBrush;
    }

    private Brush ResolveSelectedItemBackground()
    {
        return TryFindResource("AccentBrush") as Brush ?? SelectedItemBg;
    }

    private Brush ResolvePopupBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private void DrawDropdownButton(DrawingContext dc, Rect rect)
    {
        var iconPen = new Pen(ResolveSecondaryTextBrush(), 1.5);

        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var radius = 6;

        // Draw clock circle
        dc.DrawEllipse(null, iconPen, new Point(centerX, centerY), radius, radius);

        // Draw clock hands
        dc.DrawLine(iconPen, new Point(centerX, centerY), new Point(centerX, centerY - 4));
        dc.DrawLine(iconPen, new Point(centerX, centerY), new Point(centerX + 3, centerY + 2));
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectedTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePicker timePicker)
        {
            timePicker.InvalidateVisual();

            var args = new TimePickerSelectedValueChangedEventArgs(SelectedTimeChangedEvent,
                e.OldValue as TimeSpan?, e.NewValue as TimeSpan?);
            timePicker.RaiseEvent(args);
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePicker timePicker)
        {
            if ((bool)e.NewValue)
                timePicker.OpenDropDown();
            else
                timePicker.CloseDropDown();
        }
    }

    private static void OnClockIdentifierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePicker timePicker)
        {
            // Force popup rebuild on next open
            timePicker._popup = null;
            timePicker._hourColumn = null;
            timePicker._minuteColumn = null;
            timePicker._periodColumn = null;
            timePicker.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePicker timePicker)
        {
            timePicker.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePicker timePicker)
        {
            timePicker.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Provides data for the SelectedTimeChanged event.
/// </summary>
public sealed class TimePickerSelectedValueChangedEventArgs : RoutedEventArgs
{
    public TimeSpan? OldTime { get; }
    public TimeSpan? NewTime { get; }

    public TimePickerSelectedValueChangedEventArgs(RoutedEvent routedEvent, TimeSpan? oldTime, TimeSpan? newTime)
    {
        RoutedEvent = routedEvent;
        OldTime = oldTime;
        NewTime = newTime;
    }
}
