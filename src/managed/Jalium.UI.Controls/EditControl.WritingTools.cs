using Jalium.UI.Media;
namespace Jalium.UI.Controls;
public partial class EditControl
{
    internal void RenderWritingToolsText(DrawingContext context)
    {
        string family = FontFamily?.GetRenderingSource(this) ?? "Cascadia Code";
        UpdateViewFontLayout(family, FontSize);
        context.PushClip(new RectangleGeometry(new Rect(0, 0, RenderSize.Width, RenderSize.Height)));
        context.PushClip(new RectangleGeometry(new Rect(0, 0, GetContentRenderWidth(RenderSize.Width), GetContentRenderHeight(RenderSize.Height))));
        try
        {
            _view.Render(context, new Size(GetContentRenderWidth(RenderSize.Width), GetContentRenderHeight(RenderSize.Height)),
            _caret, _selection, ShowLineNumbers, false, ResolveForegroundBrush(), ResolveSelectionBrush(),
            ResolveCaretBrush(), ResolveLineNumberForegroundBrush(), ResolveCurrentLineBackgroundBrush(),
            ResolveGutterBackgroundBrush(), family, FontSize, FontWeight, FontStyle,
            renderLineNumbers: false, suppressCaret: true, writingToolsOwner: this, textOnly: true);
        }
        finally { context.Pop(); context.Pop(); }
    }
}

