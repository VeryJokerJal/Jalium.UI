using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ScrollBarThemeResourceTests
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
    public void ThemeResources_ShouldProvideScrollBarStyles()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var scrollBar = new ScrollBar
            {
                Orientation = Orientation.Vertical
            };

            var host = new Grid { Width = 40, Height = 220 };
            host.Children.Add(scrollBar);
            host.Measure(new Size(40, 220));
            host.Arrange(new Rect(0, 0, 40, 220));

            Assert.NotNull(scrollBar.TryFindResource("ScrollBarStyle") as Style);
            Assert.NotNull(scrollBar.TryFindResource("ScrollBarThumbStyle") as Style);
            Assert.NotNull(scrollBar.TryFindResource("ScrollBarLineButtonStyle") as Style);
            Assert.NotNull(scrollBar.TryFindResource("ScrollBarPageButtonStyle") as Style);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
