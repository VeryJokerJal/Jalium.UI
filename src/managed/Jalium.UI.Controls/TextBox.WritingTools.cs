using Jalium.UI.Media;
namespace Jalium.UI.Controls;
public partial class TextBox
{
    internal void RenderWritingToolsText(DrawingContext context)
    {
        EnsureLinesValid();
        double height = Math.Round(GetLineHeight());
        Rect rect = GetTextViewportRect();
        context.PushClip(new RectangleGeometry(rect));
        try
        {
            double shift = ComputeVerticalContentOffset(rect.Width, rect.Height, height);
            DrawText(context, new Rect(rect.X, rect.Y + shift, rect.Width, rect.Height), height);
        }
        finally { context.Pop(); }
    }
}

