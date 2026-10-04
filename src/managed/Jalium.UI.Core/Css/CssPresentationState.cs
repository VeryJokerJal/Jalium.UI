using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

/// <summary>Maps native dialog and window presentation states to CSS selectors.</summary>
internal static class CssPresentationState
{
    internal static bool IsFullscreen(CssNode element) =>
        element.Target is Window { WindowState: WindowState.FullScreen };

    internal static bool IsModal(CssNode element) => element.Target switch
    {
        Window window => window.IsModalForCss || window.WindowState == WindowState.FullScreen,
        ContentDialog dialog => dialog.IsModalForCss,
        _ => false,
    };

    internal static void NotifyModalChange(DependencyObject target)
    {
        CssSelectorDependencies.NativeValueChanged(target, "IsModal");
        if (CssNode.Existing(target) is { } node)
            CssEvaluationScheduler.InvalidateSubtree(node);
    }
}
