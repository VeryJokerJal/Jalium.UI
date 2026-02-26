using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a command in a MenuFlyout control.
/// </summary>
public class MenuFlyoutItem : Control
{
    private static readonly SolidColorBrush s_fallbackHoverBrush = new(Color.FromRgb(62, 62, 64));
    private static readonly SolidColorBrush s_fallbackTextBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush s_fallbackDisabledTextBrush = new(Color.FromRgb(90, 90, 90));
    private static readonly SolidColorBrush s_fallbackAcceleratorBrush = new(Color.FromRgb(136, 136, 136));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MenuFlyoutItem),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(MenuFlyoutItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(MenuFlyoutItem),
            new PropertyMetadata(null, OnCommandChanged));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(MenuFlyoutItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the KeyboardAcceleratorTextOverride dependency property.
    /// </summary>
    public static readonly DependencyProperty KeyboardAcceleratorTextOverrideProperty =
        DependencyProperty.Register(nameof(KeyboardAcceleratorTextOverride), typeof(string), typeof(MenuFlyoutItem),
            new PropertyMetadata(string.Empty));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Click routed event.
    /// </summary>
    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MenuFlyoutItem));

    /// <summary>
    /// Occurs when the menu flyout item is clicked.
    /// </summary>
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the text content of the menu item.
    /// </summary>
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the graphic content of the menu item.
    /// </summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to invoke when the item is pressed.
    /// </summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the parameter to pass to the Command property.
    /// </summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the keyboard accelerator text override for display.
    /// </summary>
    public string KeyboardAcceleratorTextOverride
    {
        get => (string?)GetValue(KeyboardAcceleratorTextOverrideProperty) ?? string.Empty;
        set => SetValue(KeyboardAcceleratorTextOverrideProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the MenuFlyoutItem class.
    /// </summary>
    public MenuFlyoutItem()
    {
        Focusable = true;
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var fontSize = FontSize > 0 ? FontSize : 14;
        double textWidth = 0;
        if (!string.IsNullOrEmpty(Text))
        {
            var formattedText = new FormattedText(Text, FontFamily ?? "Segoe UI", fontSize);
            TextMeasurement.MeasureText(formattedText);
            textWidth = formattedText.Width + 16; // padding around text
        }

        // Always reserve icon column space for alignment (matches OnRender which reserves 28px even when Icon is null)
        var iconWidth = 28;

        double accelWidth = 0;
        if (!string.IsNullOrEmpty(KeyboardAcceleratorTextOverride))
        {
            var accelText = new FormattedText(KeyboardAcceleratorTextOverride, FontFamily ?? "Segoe UI", 12);
            TextMeasurement.MeasureText(accelText);
            accelWidth = accelText.Width + 24; // gap + padding
        }

        var contentWidth = iconWidth + textWidth + accelWidth + 16; // 16 = left/right padding
        return new Size(contentWidth, Math.Min(32, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        // Background (hover state handled by IsMouseOver)
        if (IsMouseOver)
        {
            var hoverBrush = ResolveBrush("OneSurfaceHover", "MenuFlyoutItemBackgroundHover", s_fallbackHoverBrush);
            dc.DrawRoundedRectangle(hoverBrush, null,
                new Rect(2, 0, RenderSize.Width - 4, RenderSize.Height),
                4, 4);
        }

        var textBrush = IsEnabled
            ? Foreground ?? ResolveBrush("OnePopupText", "TextPrimary", s_fallbackTextBrush)
            : ResolveBrush("OneTextDisabled", "TextDisabled", s_fallbackDisabledTextBrush);

        double x = 12;

        var fontSize = FontSize > 0 ? FontSize : 14;

        // Icon
        if (Icon is string iconText && !string.IsNullOrEmpty(iconText))
        {
            var iconFormatted = new FormattedText(
                iconText, "Segoe MDL2 Assets", 14) { Foreground = textBrush };
            TextMeasurement.MeasureText(iconFormatted);
            dc.DrawText(iconFormatted, new Point(x, (RenderSize.Height - iconFormatted.Height) / 2));
            x += 28;
        }
        else if (Icon == null)
        {
            x += 28; // Reserve space even without icon for alignment
        }

        // Text
        if (!string.IsNullOrEmpty(Text))
        {
            var textFormatted = new FormattedText(
                Text, FontFamily ?? "Segoe UI", fontSize) { Foreground = textBrush };
            TextMeasurement.MeasureText(textFormatted);
            dc.DrawText(textFormatted, new Point(x, (RenderSize.Height - textFormatted.Height) / 2));
        }

        // Keyboard accelerator text (right-aligned)
        if (!string.IsNullOrEmpty(KeyboardAcceleratorTextOverride))
        {
            var accelBrush = ResolveBrush("OneTextSecondary", "TextSecondary", s_fallbackAcceleratorBrush);
            var accelFormatted = new FormattedText(
                KeyboardAcceleratorTextOverride, FontFamily ?? "Segoe UI", 12) { Foreground = accelBrush };
            TextMeasurement.MeasureText(accelFormatted);
            var accelX = RenderSize.Width - accelFormatted.Width - 12;
            dc.DrawText(accelFormatted, new Point(accelX, (RenderSize.Height - accelFormatted.Height) / 2));
        }
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        RaiseEvent(new RoutedEventArgs(ClickEvent, this));

        Command?.Execute(CommandParameter);
        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, RoutedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
    {
        InvalidateVisual();
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MenuFlyoutItem item && e.NewValue is XamlUICommand uiCommand)
        {
            if (string.IsNullOrEmpty(item.Text))
                item.Text = uiCommand.Label;
            item.Icon ??= uiCommand.IconSource;
        }
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }
}
