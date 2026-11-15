using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        internal CssFloatRegion? InitialExclusion => _initialLetter is { Placed: true, Region: { } region }
            ? region with { IsInitial = true } : null;

        internal CssFloatRegion[] ExportedInitialLetters => CssFlowLayout.SharesParent(owner) && InitialExclusion is { } region
            ? [region.Offset(_insets.Left, _insets.Top)] : [];

        // Block descendants export their measured regions separately. Do not
        // prepare their private text projections while merely inspecting the
        // parent's inline run; that can retire a committed ::first-line scope.
        internal static bool HasNestedInitials(UIElement element) => element is FrameworkElement host &&
            (host.CssInlineOuter || element is ICssAnonymousTextBox { Owner: TextBlock }) &&
            (element.GetValue(CssInitialLetterProperties.ValueProperty) is CssInitialLetter { Active: true } ||
            host.CanFragmentCssInlineContent && !CssReplacedContent.IsActive(host) &&
            new CssPanelChildren(host).Enumerate().Any(HasNestedInitials));

        private double InitialFlowBottom => _run || CssFlowLayout.SharesParent(owner) ? _y : Math.Max(_y, InitialLetterBottom);

        private void ClearPreviousInitial()
        {
            if (_initialLetter is { Placed: false } && _floatSpace is { } space)
                _y = Math.Max(_y, space.InitialBottom());
        }
    }
}
