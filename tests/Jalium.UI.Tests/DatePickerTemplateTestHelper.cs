using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;
internal static class DatePickerTemplateTestHelper
{
    internal static readonly Lazy<ResourceDictionary> Themes = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    internal static void Register(ResourceDictionary themes)
    {
        var scoped = new ResourceDictionary();
        foreach (var type in new[]
        {
            typeof(DatePicker),
            typeof(DatePickerTextBox),
            typeof(TextBox),
            typeof(Button),
            typeof(Calendar),
            typeof(CalendarItem),
            typeof(CalendarDayButton),
            typeof(CalendarButton)
        }

        )
            if (themes.TryGetValue(type, out var style))
                scoped[type] = style;
        ThemeManager.RegisterControlThemes(scoped);
    }

    internal static void Apply(Control control)
    {
        Register(Themes.Value);
        control.Style = (Style)Themes.Value[control.GetType()];
        control.ApplyTemplate();
        control.Measure(new Size(280, 80));
        control.Arrange(new Rect(0, 0, 280, 80));
        control.UpdateLayout();
    }
}
