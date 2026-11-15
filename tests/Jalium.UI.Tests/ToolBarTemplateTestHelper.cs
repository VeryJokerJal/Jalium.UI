using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
namespace Jalium.UI.Tests;
internal static class ToolBarTemplateTestHelper
{
    internal static void Register()
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        typeof(Application).GetField("_current", flags)!.SetValue(null, null);
        typeof(ThemeManager).GetField("_controlThemeDictionaries", flags)!.SetValue(null, Array.Empty<ResourceDictionary>());
        ThemeManager.Reset();
        ThemeManager.RegisterControlThemes(new ResourceDictionary
        {
            [typeof(ToolBar)] = MenuFlyoutTemplateTestHelper.Themes.Value[typeof(ToolBar)]
        });
    }
    internal static void UseBareMetrics(ToolBar bar)
        => bar.Style = new Style(typeof(ToolBar)) { Setters = { new Setter(Control.PaddingProperty, default(Thickness)) } };
    internal static Rect BoundsIn(FrameworkElement child, Visual owner)
        => new Rect(child.TransformToVisual(owner).Transform(new Point()), child.RenderSize);

}
