using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ThemeRegistrationTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void ThemeStyles_ForRegisteredInfrastructureTypes_ShouldBeLoadable()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var styledTypes = new[]
            {
                typeof(Split),
                typeof(DocumentPageView),
                typeof(ResizeGrip),
                typeof(ScrollViewer),
                typeof(SelectiveScrollingGrid),
                typeof(TabPanel),
                typeof(TickBar),
                typeof(ToolBarOverflowPanel),
                typeof(ToolBarPanel),
                typeof(UniformGrid),
                typeof(VirtualizingStackPanel)
            };

            foreach (var type in styledTypes)
            {
                Assert.Same(type, XamlTypeRegistry.GetType(type.Name));
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
