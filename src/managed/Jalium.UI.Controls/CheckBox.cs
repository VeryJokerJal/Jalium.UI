using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that a user can select and clear.
/// </summary>
public class CheckBox : ToggleButton
{
    #region Constructor

    private const double CheckBoxSize = 18.0;
    private const double CheckBoxMargin = 8.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckBox"/> class.
    /// </summary>
    public CheckBox()
    {
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var contentSize = MeasureContent(new Size(
            Math.Max(0, availableSize.Width - CheckBoxSize - CheckBoxMargin),
            availableSize.Height));

        return new Size(
            CheckBoxSize + CheckBoxMargin + contentSize.Width,
            Math.Max(CheckBoxSize, contentSize.Height));
    }

    private Size MeasureContent(Size availableSize)
    {
        if (Content is string text)
        {
            var fontFamily = FontFamily ?? "Segoe UI";
            var fontSize = FontSize > 0 ? FontSize : 14;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(formattedText.Width, formattedText.Height);
        }

        if (Content is UIElement element)
        {
            element.Measure(availableSize);
            return element.DesiredSize;
        }

        return new Size(0, 0);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Content is FrameworkElement fe)
        {
            var contentRect = new Rect(
                CheckBoxSize + CheckBoxMargin,
                0,
                Math.Max(0, finalSize.Width - CheckBoxSize - CheckBoxMargin),
                finalSize.Height);

            fe.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        // Calculate checkbox box position (vertically centered)
        var boxY = (RenderSize.Height - CheckBoxSize) / 2;
        var boxRect = new Rect(0, boxY, CheckBoxSize, CheckBoxSize);

        // Use property values - styles and triggers handle state changes
        var bgBrush = Background;
        var borderBrush = BorderBrush;
        var fgBrush = Foreground;
        var cornerRadius = CornerRadius.TopLeft > 0 ? CornerRadius.TopLeft : 4;

        // Draw checkbox background
        if (bgBrush != null)
        {
            dc.DrawRoundedRectangle(bgBrush, null, boxRect, cornerRadius, cornerRadius);
        }

        // Draw checkbox border
        if (borderBrush != null)
        {
            var borderPen = new Pen(borderBrush, 1.5);
            dc.DrawRoundedRectangle(null, borderPen, boxRect, cornerRadius, cornerRadius);
        }

        // Draw check mark or indeterminate indicator
        if (IsChecked == true)
        {
            DrawCheckMark(dc, boxRect);
        }
        else if (IsChecked == null)
        {
            DrawIndeterminate(dc, boxRect);
        }

        // Draw content text
        if (Content is string text && !string.IsNullOrEmpty(text) && fgBrush != null)
        {
            var fontFamily = FontFamily ?? "Segoe UI";
            var fontSize = FontSize > 0 ? FontSize : 14;
            var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);

            var formattedText = new FormattedText(text, fontFamily, fontSize)
            {
                Foreground = fgBrush
            };

            var textX = CheckBoxSize + CheckBoxMargin;
            var textY = (RenderSize.Height - fontMetrics.LineHeight) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }
    }

    private void DrawCheckMark(DrawingContext dc, Rect boxRect)
    {
        // Use white for the check mark (contrasts with blue accent background)
        var checkBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        var checkPen = new Pen(checkBrush, 2.0);

        // Draw checkmark (two lines forming a check)
        var cx = boxRect.X + boxRect.Width / 2;
        var cy = boxRect.Y + boxRect.Height / 2;

        // First line: from bottom-left to middle-bottom
        var p1 = new Point(cx - 4.5, cy);
        var p2 = new Point(cx - 1.5, cy + 3.5);
        dc.DrawLine(checkPen, p1, p2);

        // Second line: from middle-bottom to top-right
        var p3 = new Point(cx + 4.5, cy - 3.5);
        dc.DrawLine(checkPen, p2, p3);
    }

    private void DrawIndeterminate(DrawingContext dc, Rect boxRect)
    {
        // Use white for the indeterminate indicator (contrasts with blue accent background)
        var indeterminateBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));

        // Draw a horizontal line/rectangle in the middle
        var lineRect = new Rect(
            boxRect.X + 4,
            boxRect.Y + boxRect.Height / 2 - 1.5,
            boxRect.Width - 8,
            3);

        dc.DrawRoundedRectangle(indeterminateBrush, null, lineRect, 1, 1);
    }

    #endregion
}
