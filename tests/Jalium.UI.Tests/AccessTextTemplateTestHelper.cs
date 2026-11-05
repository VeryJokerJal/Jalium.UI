using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
namespace Jalium.UI.Tests;
internal static class AccessTextTemplateTestHelper
{
    internal static void Register()
    {
        // CSS/text contracts exercise their own explicit style inputs. Keep an
        // Application or deferred native themes from another collection out.
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        typeof(Application).GetField("_current", flags)!.SetValue(null, null);
        typeof(ThemeManager).GetField("_controlThemeDictionaries", flags)!.SetValue(null, Array.Empty<ResourceDictionary>());
        ThemeManager.Reset();
        ThemeManager.RegisterControlThemes(new ResourceDictionary
        {
            [typeof(AccessText)] = MenuFlyoutTemplateTestHelper.Themes.Value[typeof(AccessText)]
        });
    }
}
