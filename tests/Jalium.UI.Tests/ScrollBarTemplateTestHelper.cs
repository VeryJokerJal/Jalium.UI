using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
namespace Jalium.UI.Tests;
internal static class ScrollBarTemplateTestHelper
{
    internal static void Register()
    {
        // Match a real application's complete theme/resource environment. Detached
        // tests previously received their parts from the ScrollBar constructor.
        Reset();
        ThemeLoader.Initialize();
        _ = new Application();
    }
    internal static void Reset()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        typeof(ThemeManager).GetField("_controlThemeDictionaries", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Array.Empty<ResourceDictionary>());
        ThemeManager.Reset();
    }
}
