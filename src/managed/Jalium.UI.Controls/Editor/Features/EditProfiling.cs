namespace Jalium.UI.Controls.Editor;

internal readonly record struct RenderStats(
    int VisibleLineCount,
    int RenderedLineCount,
    int HighlightedLineCount,
    int CachedLineCount);

internal interface IEditProfiler
{
    void OnRenderCompleted(RenderStats stats);
}
