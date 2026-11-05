using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;
internal static class MenuFlyoutTemplateTestHelper
{
    [ThreadStatic]
    private static Lazy<ResourceDictionary>? s_themes;
    internal static Lazy<ResourceDictionary> Themes => s_themes ??= new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    internal static void Register()
    {
        var scoped = new ResourceDictionary();
        foreach (var type in new[]
        {
            typeof(MenuIconPresenter),
            typeof(MenuFlyoutItem),
            typeof(ToggleMenuFlyoutItem),
            typeof(MenuFlyoutSubItem),
            typeof(MenuFlyoutSeparator)
        }

        )
            scoped[type] = Themes.Value[type];
        ThemeManager.RegisterControlThemes(scoped);
    }
    internal static void Apply(Control control)
    {
        Register();
        if (Themes.Value.TryGetValue(control.GetType(), out var style))
            control.Style = (Style)style;
        else if (control is MenuFlyoutItem)
            control.Style = (Style)Themes.Value[typeof(MenuFlyoutItem)];
        control.ApplyTemplate();
    }
}
