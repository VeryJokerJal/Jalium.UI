using System.Diagnostics;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button that displays as a hyperlink and can navigate to a URI.
/// </summary>
public class HyperlinkButton : ButtonBase
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the NavigateUri dependency property.
    /// </summary>
    public static readonly DependencyProperty NavigateUriProperty =
        DependencyProperty.Register(nameof(NavigateUri), typeof(Uri), typeof(HyperlinkButton),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsUnderlined dependency property.
    /// </summary>
    public static readonly DependencyProperty IsUnderlinedProperty =
        DependencyProperty.Register(nameof(IsUnderlined), typeof(bool), typeof(HyperlinkButton),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the URI to navigate to when the hyperlink is clicked.
    /// </summary>
    public Uri? NavigateUri
    {
        get => (Uri?)GetValue(NavigateUriProperty);
        set => SetValue(NavigateUriProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the hyperlink text is underlined.
    /// </summary>
    public bool IsUnderlined
    {
        get => (bool)(GetValue(IsUnderlinedProperty) ?? true);
        set => SetValue(IsUnderlinedProperty, value);
    }

    #endregion

    #region Private Fields

    private bool _isHovered;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperlinkButton"/> class.
    /// </summary>
    public HyperlinkButton()
    {
        // Default hyperlink appearance
        Foreground = new SolidColorBrush(Color.FromRgb(0, 102, 204));
        Background = null;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        // Note: Cursor support will be added when the base class supports it
    }

    #endregion

    #region Click Handling

    /// <inheritdoc />
    protected override void OnClick()
    {
        // Try to navigate to the URI if set
        if (NavigateUri != null)
        {
            try
            {
                var uri = NavigateUri.ToString();
                // Use shell execute to open URLs in the default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // Navigation failed - silently ignore or could raise an event
            }
        }

        base.OnClick();
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(RoutedEventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(RoutedEventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;

        if (Content is string text)
        {
            var fontFamily = FontFamily ?? "Segoe UI";
            var fontSize = FontSize > 0 ? FontSize : 14;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(
                formattedText.Width + padding.TotalWidth,
                formattedText.Height + padding.TotalHeight);
        }

        if (Content is UIElement element)
        {
            element.Measure(availableSize);
            return new Size(
                element.DesiredSize.Width + padding.TotalWidth,
                element.DesiredSize.Height + padding.TotalHeight);
        }

        return new Size(padding.TotalWidth, padding.TotalHeight);
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

        // Draw background if set (usually null for hyperlinks)
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Determine foreground color based on state
        var fgBrush = Foreground;
        if (_isHovered || IsPressed)
        {
            // Darken on hover/press
            fgBrush = new SolidColorBrush(Color.FromRgb(0, 51, 153));
        }

        // Draw content
        if (Content is string text && fgBrush != null)
        {
            var formattedText = new FormattedText(text, FontFamily, FontSize)
            {
                Foreground = fgBrush
            };

            TextMeasurement.MeasureText(formattedText);
            var textX = padding.Left;
            var textY = padding.Top;

            dc.DrawText(formattedText, new Point(textX, textY));

            // Draw underline if enabled
            if (IsUnderlined)
            {
                var underlineY = textY + formattedText.Height - 1;
                var underlinePen = new Pen(fgBrush, 1);
                dc.DrawLine(underlinePen, new Point(textX, underlineY), new Point(textX + formattedText.Width, underlineY));
            }
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HyperlinkButton button)
        {
            button.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Provides cursor types for controls.
/// </summary>
public static class Cursors
{
    /// <summary>
    /// Gets the default arrow cursor.
    /// </summary>
    public static object Arrow { get; } = "Arrow";

    /// <summary>
    /// Gets the hand (pointer) cursor.
    /// </summary>
    public static object Hand { get; } = "Hand";

    /// <summary>
    /// Gets the I-beam text cursor.
    /// </summary>
    public static object IBeam { get; } = "IBeam";

    /// <summary>
    /// Gets the wait/busy cursor.
    /// </summary>
    public static object Wait { get; } = "Wait";

    /// <summary>
    /// Gets the crosshair cursor.
    /// </summary>
    public static object Cross { get; } = "Cross";

    /// <summary>
    /// Gets the resize horizontal cursor.
    /// </summary>
    public static object SizeWE { get; } = "SizeWE";

    /// <summary>
    /// Gets the resize vertical cursor.
    /// </summary>
    public static object SizeNS { get; } = "SizeNS";
}
