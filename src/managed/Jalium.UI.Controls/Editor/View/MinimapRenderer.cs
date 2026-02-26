using Jalium.UI.Media;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Renders a minimap (code overview) at the right side of the editor.
/// Each line is rendered as a thin colored strip based on syntax highlighting.
/// </summary>
internal sealed class MinimapRenderer
{
    private const double MinimapWidth = 80;
    private const double MinimapLineHeight = 2;
    private const double MinimapCharWidth = 1.2;
    private const double MinViewportHeight = 4;

    /// <summary>
    /// Gets the width of the minimap.
    /// </summary>
    public double Width => MinimapWidth;

    /// <summary>
    /// Renders the minimap into the drawing context.
    /// </summary>
    public void Render(DrawingContext dc, TextDocument document, EditorView view,
        ISyntaxHighlighter? highlighter,
        double editorWidth, double editorHeight,
        Brush background, Brush foreground, Brush viewportBrush)
    {
        var minimapRect = GetMinimapRect(editorWidth, editorHeight);
        Render(dc, document, view, highlighter, minimapRect, background, foreground, viewportBrush);
    }

    public void Render(
        DrawingContext dc,
        TextDocument document,
        EditorView view,
        ISyntaxHighlighter? highlighter,
        Rect minimapRect,
        Brush background,
        Brush foreground,
        Brush viewportBrush)
    {
        if (minimapRect.IsEmpty || minimapRect.Width <= 0 || minimapRect.Height <= 0 || document.LineCount <= 0)
            return;

        // Background
        dc.DrawRectangle(background, null, minimapRect);

        // Viewport indicator
        var viewportRect = GetViewportRect(document, view, minimapRect);
        if (!viewportRect.IsEmpty)
            dc.DrawRectangle(viewportBrush, null, viewportRect);

        // Render each line as a thin strip
        double totalLines = document.LineCount;
        double lineScale = minimapRect.Height / totalLines;
        if (lineScale < 0.5) lineScale = 0.5; // minimum line size

        int step = totalLines > minimapRect.Height / MinimapLineHeight
            ? (int)Math.Ceiling(totalLines / (minimapRect.Height / MinimapLineHeight))
            : 1;

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum += step)
        {
            double y = minimapRect.Y + (lineNum - 1) * lineScale;
            if (y > minimapRect.Bottom) break;

            var lineText = document.GetLineText(lineNum);
            if (lineText.Length == 0) continue;

            // Simple rendering: draw a single colored line per source line
            int indent = 0;
            while (indent < lineText.Length && char.IsWhiteSpace(lineText[indent]))
                indent++;

            double x = minimapRect.X + indent * MinimapCharWidth;
            double w = Math.Min((lineText.Length - indent) * MinimapCharWidth, minimapRect.Width - indent * MinimapCharWidth);
            w = Math.Max(w, 2);

            dc.DrawRectangle(foreground, null,
                new Rect(x, y, w, Math.Max(MinimapLineHeight, lineScale)));
        }
    }

    public Rect GetMinimapRect(double editorWidth, double editorHeight, double rightInset = 0)
    {
        double clampedWidth = Math.Max(0, Math.Min(MinimapWidth, editorWidth));
        double x = Math.Max(0, editorWidth - rightInset - clampedWidth);
        return new Rect(x, 0, clampedWidth, Math.Max(0, editorHeight));
    }

    public Rect GetViewportRect(TextDocument document, EditorView view, Rect minimapRect)
    {
        if (minimapRect.IsEmpty || minimapRect.Height <= 0 || document.LineCount <= 0)
            return Rect.Empty;

        double maxOffset = GetMaxVerticalOffset(view);
        double extent = GetVerticalScrollExtent(view, maxOffset);
        double viewportHeight = Math.Max(0, view.ViewportHeight);

        if (extent <= 0 || viewportHeight <= 0)
            return Rect.Empty;

        double viewportHeightScaled = (viewportHeight / extent) * minimapRect.Height;
        double clampedHeight = Math.Clamp(viewportHeightScaled, MinViewportHeight, minimapRect.Height);
        double travel = Math.Max(0, minimapRect.Height - clampedHeight);
        double clampedTop = 0;
        if (travel > 0 && maxOffset > 0)
        {
            double normalizedOffset = Math.Clamp(view.VerticalOffset, 0, maxOffset) / maxOffset;
            clampedTop = normalizedOffset * travel;
        }

        return new Rect(
            minimapRect.X,
            minimapRect.Y + clampedTop,
            minimapRect.Width,
            clampedHeight);
    }

    public int GetLineFromPoint(double y, Rect minimapRect, EditorView view, TextDocument document)
    {
        if (minimapRect.IsEmpty || minimapRect.Height <= 0 || document.LineCount <= 0)
            return 1;

        double relative = Math.Clamp((y - minimapRect.Y) / minimapRect.Height, 0, 1);
        double targetAbsoluteY = relative * Math.Max(0, view.TotalContentHeight - 1);
        int line = view.GetLineNumberFromY(targetAbsoluteY - view.VerticalOffset);
        return Math.Clamp(line, 1, document.LineCount);
    }

    public double GetVerticalOffsetFromViewportTop(double viewportTopY, Rect minimapRect, EditorView view)
    {
        if (minimapRect.IsEmpty || minimapRect.Height <= 0)
            return 0;

        double maxOffset = GetMaxVerticalOffset(view);
        if (maxOffset <= 0)
            return 0;

        double extent = GetVerticalScrollExtent(view, maxOffset);
        double viewportHeight = Math.Max(0, view.ViewportHeight);
        double viewportHeightScaled = (viewportHeight / Math.Max(1, extent)) * minimapRect.Height;
        double clampedHeight = Math.Clamp(viewportHeightScaled, MinViewportHeight, minimapRect.Height);
        double travel = Math.Max(0, minimapRect.Height - clampedHeight);
        if (travel <= 0)
            return 0;

        double clampedTop = Math.Clamp(viewportTopY - minimapRect.Y, 0, travel);
        double relative = clampedTop / travel;
        return relative * maxOffset;
    }

    /// <summary>
    /// Gets the line number from a click position on the minimap.
    /// </summary>
    public int GetLineFromPoint(double y, double editorHeight, int totalLines)
    {
        if (editorHeight <= 0 || totalLines <= 0) return 1;
        int line = (int)(y / editorHeight * totalLines) + 1;
        return Math.Clamp(line, 1, totalLines);
    }

    private static double GetMaxVerticalOffset(EditorView view)
    {
        double contentHeight = Math.Max(0, view.TotalContentHeight);
        double viewportHeight = Math.Max(0, view.ViewportHeight);
        double lineHeight = Math.Max(1, view.LineHeight);

        // Keep minimap mapping consistent with EditControl scroll domain,
        // which allows the last line to move to the top of the viewport.
        double defaultMax = Math.Max(0, contentHeight - viewportHeight);
        double lastLineTopMax = Math.Max(0, contentHeight - lineHeight);
        return Math.Max(defaultMax, lastLineTopMax);
    }

    private static double GetVerticalScrollExtent(EditorView view, double maxOffset)
    {
        double contentHeight = Math.Max(0, view.TotalContentHeight);
        double viewportHeight = Math.Max(0, view.ViewportHeight);
        return Math.Max(contentHeight, maxOffset + viewportHeight);
    }
}
