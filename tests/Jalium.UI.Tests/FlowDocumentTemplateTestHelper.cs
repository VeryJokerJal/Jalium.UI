using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;
internal static class FlowDocumentTemplateTestHelper
{
    internal static void Register()
    {
        var themes = new ResourceDictionary
        {
            [typeof(DocumentViewer)] = MenuFlyoutTemplateTestHelper.Themes.Value[typeof(DocumentViewer)],
            [typeof(FlowDocumentScrollViewer)] = MenuFlyoutTemplateTestHelper.Themes.Value[typeof(FlowDocumentScrollViewer)],
            [typeof(FlowDocumentPageViewer)] = MenuFlyoutTemplateTestHelper.Themes.Value[typeof(FlowDocumentPageViewer)],
            [typeof(FlowDocumentReader)] = MenuFlyoutTemplateTestHelper.Themes.Value[typeof(FlowDocumentReader)]
        };
        ThemeManager.RegisterControlThemes(themes);
    }
}
