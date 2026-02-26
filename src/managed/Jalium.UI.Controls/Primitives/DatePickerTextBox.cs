using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a text box used in DatePicker controls with watermark support.
/// </summary>
public sealed class DatePickerTextBox : TextBox
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Watermark dependency property.
    /// </summary>
    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.Register(nameof(Watermark), typeof(object), typeof(DatePickerTextBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the watermark content displayed when the text box is empty.
    /// </summary>
    public object? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the watermark should be shown.
    /// </summary>
    private bool ShouldShowWatermark =>
        string.IsNullOrEmpty(Text) && !IsKeyboardFocused && Watermark != null;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DatePickerTextBox"/> class.
    /// </summary>
    public DatePickerTextBox()
    {
        // Set default watermark
        Watermark = "Select a date";
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is DrawingContext dc && ShouldShowWatermark)
        {
            DrawWatermark(dc);
        }
    }

    private void DrawWatermark(DrawingContext dc)
    {
        var padding = Padding;
        var watermarkText = Watermark?.ToString() ?? string.Empty;

        var watermarkBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        var formattedText = new FormattedText(watermarkText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
        {
            Foreground = watermarkBrush
        };
        TextMeasurement.MeasureText(formattedText);

        var textX = padding.Left + BorderThickness.Left;
        var textY = (RenderSize.Height - formattedText.Height) / 2;
        dc.DrawText(formattedText, new Point(textX, textY));
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePickerTextBox textBox)
        {
            textBox.InvalidateVisual();
        }
    }

    #endregion

    #region Date Validation

    /// <summary>
    /// Validates that the text represents a valid date.
    /// </summary>
    /// <returns>True if the text is a valid date; otherwise, false.</returns>
    public bool ValidateDate()
    {
        return DateTime.TryParse(Text, out _);
    }

    /// <summary>
    /// Gets the date value if the text is a valid date.
    /// </summary>
    /// <returns>The parsed date, or null if invalid.</returns>
    public DateTime? GetDate()
    {
        if (DateTime.TryParse(Text, out var date))
        {
            return date;
        }
        return null;
    }

    /// <summary>
    /// Sets the text to represent the specified date.
    /// </summary>
    /// <param name="date">The date to set.</param>
    /// <param name="format">The format string to use.</param>
    public void SetDate(DateTime? date, string format = "d")
    {
        Text = date?.ToString(format) ?? string.Empty;
    }

    #endregion
}
