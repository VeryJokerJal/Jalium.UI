using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a resize grip for Window controls.
/// </summary>
public class ResizeGrip : Control
{
    #region Private Fields

    private const double DefaultSize = 12;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ResizeGrip"/> class.
    /// </summary>
    public ResizeGrip()
    {
        Width = DefaultSize;
        Height = DefaultSize;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Cursor = Jalium.UI.Cursors.SizeNWSE;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(DefaultSize, DefaultSize);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        // Draw diagonal grip lines (bottom-right corner style)
        var gripBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
        var gripPen = new Pen(gripBrush, 1);

        // Draw 3 diagonal lines from bottom-left to top-right
        var offset = 3.0;
        for (var i = 0; i < 3; i++)
        {
            var startX = rect.Width - (i + 1) * offset;
            var startY = rect.Height;
            var endX = rect.Width;
            var endY = rect.Height - (i + 1) * offset;

            dc.DrawLine(gripPen, new Point(startX, startY), new Point(endX, endY));
        }

        // Draw highlight lines (slightly offset)
        var highlightBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        var highlightPen = new Pen(highlightBrush, 1);

        for (var i = 0; i < 3; i++)
        {
            var startX = rect.Width - (i + 1) * offset - 1;
            var startY = rect.Height;
            var endX = rect.Width;
            var endY = rect.Height - (i + 1) * offset - 1;

            dc.DrawLine(highlightPen, new Point(startX, startY), new Point(endX, endY));
        }
    }

    #endregion
}
