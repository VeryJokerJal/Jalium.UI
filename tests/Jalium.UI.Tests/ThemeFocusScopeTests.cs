using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ThemeFocusScopeTests
{
    [Fact]
    public void ScrollBarParts_ShouldNotDrawAccentFocusBorders()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            var scrollBar = new ScrollBar();
            var styleKeys = new[]
            {
                "ScrollBarStyle",
                "ScrollBarThumbStyle",
                "ScrollBarLineButtonStyle"
            };

            foreach (string styleKey in styleKeys)
            {
                var style = Assert.IsType<Style>(scrollBar.TryFindResource(styleKey));

                Assert.DoesNotContain(
                    style.Triggers.OfType<Trigger>(),
                    trigger => trigger.Property == UIElement.IsKeyboardFocusedProperty
                        || trigger.Property == UIElement.IsKeyboardFocusWithinProperty);
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CompositePresentationContainers_ShouldNotPromoteDescendantFocus()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            Type[] containerTypes =
            [
                typeof(Calendar),
                typeof(CalendarItem),
                typeof(ListBox),
                typeof(ListView),
                typeof(TabControl)
            ];

            foreach (Type containerType in containerTypes)
            {
                var style = Assert.IsType<Style>(app.Resources[containerType]);
                var propertyTriggers = GetPropertyTriggers(style);

                Assert.Contains(
                    propertyTriggers,
                    trigger => trigger.Property == UIElement.IsKeyboardFocusedProperty);
                Assert.DoesNotContain(
                    propertyTriggers,
                    trigger => trigger.Property == UIElement.IsKeyboardFocusWithinProperty);
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Trigger[] GetPropertyTriggers(Style style)
    {
        var triggers = style.Triggers.OfType<Trigger>().ToList();
        var template = style.Setters
            .OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == Control.TemplateProperty)
            ?.Value as ControlTemplate;
        if (template != null)
        {
            triggers.AddRange(template.Triggers.OfType<Trigger>());
        }

        return triggers.ToArray();
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField(
            "_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod(
            "Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }
}
