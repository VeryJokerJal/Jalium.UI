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
        double minimapX = editorWidth - MinimapWidth;
        double totalLines = document.LineCount;

        // Background
        dc.DrawRectangle(background, null, new Rect(minimapX, 0, MinimapWidth, editorHeight));

        // Viewport indicator
        double viewportTop = (view.FirstVisibleLineNumber - 1) / totalLines * editorHeight;
        double viewportBottom = view.LastVisibleLineNumber / totalLines * editorHeight;
        dc.DrawRectangle(viewportBrush, null,
            new Rect(minimapX, viewportTop, MinimapWidth, Math.Max(4, viewportBottom - viewportTop)));

        // Render each line as a thin strip
        double lineScale = editorHeight / totalLines;
        if (lineScale < 0.5) lineScale = 0.5; // minimum line size

        int step = totalLines > editorHeight / MinimapLineHeight
            ? (int)Math.Ceiling(totalLines / (editorHeight / MinimapLineHeight))
            : 1;

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum += step)
        {
            double y = (lineNum - 1) * lineScale;
            if (y > editorHeight) break;

            var lineText = document.GetLineText(lineNum);
            if (lineText.Length == 0) continue;

            // Simple rendering: draw a single colored line per source line
            int indent = 0;
            while (indent < lineText.Length && char.IsWhiteSpace(lineText[indent]))
                indent++;

            double x = minimapX + indent * MinimapCharWidth;
            double w = Math.Min((lineText.Length - indent) * MinimapCharWidth, MinimapWidth - indent * MinimapCharWidth);
            w = Math.Max(w, 2);

            dc.DrawRectangle(foreground, null,
                new Rect(x, y, w, Math.Max(MinimapLineHeight, lineScale)));
        }
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
}
