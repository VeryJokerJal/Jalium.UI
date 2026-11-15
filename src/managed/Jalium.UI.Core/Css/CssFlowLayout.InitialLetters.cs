using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal sealed partial class CssFlowLayout
{
    internal static CssFloatRegion[] ExportedRegions(FrameworkElement element) =>
        element is TextBlock { CanFormatCssInline: true } text
            ? text.CssTextFlow.ExportedInitialLetters
            : CssDisplayLayout.FlowFor(element).ExportedFloats;

    internal static bool SharesParent(FrameworkElement element) => element.CssParentBox is not null && !element.ClipToBounds &&
        !CssLineClampProperties.Active(element) && element.CssLayout?.IsOutOfFlow != true &&
        CssFloatProperties.ActiveSide(element) == CssFloatSide.None && !element.CssInlineOuter &&
        !CssContainerProperties.HasSizeContainment(element) && !CssMultiColumnProperties.Active(element) &&
        !(bool)element.GetValue(CssMultiColumnProperties.SpanProperty)! && element.CssDisplayMode == CssDisplayMode.Block &&
        CssContentsLayout.BoxParent(element) is FrameworkElement parent && CssDisplayLayout.IsFlow(parent);
}
