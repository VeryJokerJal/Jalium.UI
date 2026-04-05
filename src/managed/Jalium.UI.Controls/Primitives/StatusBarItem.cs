using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents an item in a StatusBar control.
/// </summary>
public class StatusBarItem : ContentControl
{
    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_defaultFgBrush = new(Color.White);
    private static readonly SolidColorBrush s_separatorBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly Pen s_separatorPen = new(s_separatorBrush, 1);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Separator dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SeparatorProperty =
        DependencyProperty.Register(nameof(Separator), typeof(bool), typeof(StatusBarItem),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether this item shows a separator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool Separator
    {
        get => (bool)GetValue(SeparatorProperty)!;
        set => SetValue(SeparatorProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBarItem"/> class.
    /// </summary>
    public StatusBarItem()
    {
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        Padding = new Thickness(4, 0, 4, 0);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var baseSize = base.MeasureOverride(availableSize);
        var padding = Padding;

        // Add separator width if enabled
        var separatorWidth = Separator ? 9 : 0; // 1px line + 4px margin on each side

        return new Size(
            baseSize.Width + padding.TotalWidth + separatorWidth,
            Math.Max(baseSize.Height, 20));
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

        // Draw background if set
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw content
        if (Content is string text)
        {
            var fgBrush = Foreground ?? s_defaultFgBrush;
            var formattedText = new FormattedText(text, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 12)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(formattedText);

            var textX = padding.Left;
            var textY = (rect.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }

        // Draw separator
        if (Separator)
        {
            var separatorX = rect.Width - 5;
            dc.DrawLine(s_separatorPen, new Point(separatorX, 4), new Point(separatorX, rect.Height - 4));
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBarItem item)
        {
            item.InvalidateVisual();
        }
    }

    #endregion
}
