using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;
internal static class LegacyMenuTemplateTestHelper
{
    internal static void Register()
    {
        MenuFlyoutTemplateTestHelper.Register();
        var scoped = new ResourceDictionary();
        foreach (var type in new[]
        {
            typeof(Menu),
            typeof(ContextMenu),
            typeof(MenuBar),
            typeof(MenuBarItem),
            typeof(MenuItem),
            typeof(MenuIconPresenter),
            typeof(MenuFlyoutPresenter),
            typeof(MenuPopupScrollHost),
            typeof(ScrollViewer),
            typeof(RepeatButton)
        }

        )
            scoped[type] = MenuFlyoutTemplateTestHelper.Themes.Value[type];
        ThemeManager.RegisterControlThemes(scoped);
    }

    internal static T Apply<T>(T control)
        where T : Control
    {
        Register();
        control.Style = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[control is ContextMenu ? typeof(ContextMenu) : control is Menu ? typeof(Menu) : control is MenuBar ? typeof(MenuBar) : control is MenuBarItem ? typeof(MenuBarItem) : typeof(MenuItem)];
        control.ApplyTemplate();
        return control;
    }
}
