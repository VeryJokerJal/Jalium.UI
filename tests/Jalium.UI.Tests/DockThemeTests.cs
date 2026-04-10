using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DockThemeTests
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
    public void DockControls_ImplicitThemeStyles_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var dockTabPanel = new DockTabPanel();
            var dockItem = new DockItem { Header = "Explorer" };
            dockTabPanel.Items.Add(dockItem);
            var dockLayout = new DockLayout { Content = dockTabPanel };

            var host = new StackPanel { Width = 420, Height = 220 };
            host.Children.Add(dockLayout);

            host.Measure(new Size(420, 220));
            host.Arrange(new Rect(0, 0, 420, 220));

            Assert.True(app.Resources.TryGetValue(typeof(DockLayout), out var layoutStyleObj));
            Assert.True(app.Resources.TryGetValue(typeof(DockTabPanel), out var panelStyleObj));
            Assert.True(app.Resources.TryGetValue(typeof(DockItem), out var itemStyleObj));
            Assert.IsType<Style>(layoutStyleObj);
            Assert.IsType<Style>(panelStyleObj);
            Assert.IsType<Style>(itemStyleObj);

            Assert.False(dockLayout.HasLocalValue(Control.BackgroundProperty));
            Assert.False(dockTabPanel.HasLocalValue(Control.BackgroundProperty));
            Assert.False(dockTabPanel.HasLocalValue(DockTabPanel.TabStripBackgroundProperty));
            Assert.False(dockItem.HasLocalValue(Control.ForegroundProperty));
            Assert.False(dockItem.HasLocalValue(Control.PaddingProperty));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DockResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var windowBackground = Assert.IsAssignableFrom<Brush>(app.Resources["WindowBackground"]);
            var dockContentBackground = Assert.IsAssignableFrom<Brush>(app.Resources["DockContentBackground"]);
            var dockTabStripBackground = Assert.IsAssignableFrom<Brush>(app.Resources["DockTabStripBackground"]);
            var dockTabStripBorder = Assert.IsAssignableFrom<Brush>(app.Resources["DockTabStripBorder"]);
            var dockTabItemSelectedBackground = Assert.IsAssignableFrom<Brush>(app.Resources["DockTabItemSelectedBackground"]);
            var dockTabItemHoverBackground = Assert.IsAssignableFrom<Brush>(app.Resources["DockTabItemHoverBackground"]);
            var textPrimary = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);
            var textSecondary = Assert.IsAssignableFrom<Brush>(app.Resources["TextSecondary"]);
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var controlBackgroundHover = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackgroundHover"]);
            var controlBackgroundPressed = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackgroundPressed"]);
            var highlightBackground = Assert.IsAssignableFrom<Brush>(app.Resources["HighlightBackground"]);
            var dockSplitterBackground = Assert.IsAssignableFrom<Brush>(app.Resources["DockSplitterBackground"]);
            var dockSplitterHover = Assert.IsAssignableFrom<Brush>(app.Resources["DockSplitterHover"]);

            var dockTabPanel = new DockTabPanel();
            var dockItem = new DockItem { Header = "Explorer" };
            dockTabPanel.Items.Add(dockItem);
            var dockLayout = new DockLayout { Content = dockTabPanel };
            var host = new StackPanel { Width = 420, Height = 220 };
            host.Children.Add(dockLayout);

            host.Measure(new Size(420, 220));
            host.Arrange(new Rect(0, 0, 420, 220));

            var split = new Split();

            AssertBrushMatches(windowBackground, InvokePrivateBrushResolver(dockLayout, "ResolveBackgroundBrush"));
            AssertBrushMatches(dockContentBackground, InvokePrivateBrushResolver(dockTabPanel, "ResolvePanelBackgroundBrush"));
            AssertBrushMatches(dockTabStripBackground, InvokePrivateBrushResolver(dockTabPanel, "ResolveTabStripBackgroundBrush"));
            AssertBrushMatches(dockTabStripBorder, InvokePrivateBrushResolver(dockTabPanel, "ResolveTabStripBorderBrush"));
            AssertBrushMatches(accentBrush, InvokePrivateBrushResolver(dockTabPanel, "ResolveAccentBrush"));

            AssertBrushMatches(dockTabItemSelectedBackground, InvokePrivateBrushResolver(dockItem, "ResolveSelectedBackgroundBrush"));
            AssertBrushMatches(dockTabItemHoverBackground, InvokePrivateBrushResolver(dockItem, "ResolveHoverBackgroundBrush"));
            AssertBrushMatches(textPrimary, InvokePrivateBrushResolver(dockItem, "ResolveActiveTextBrush"));
            AssertBrushMatches(textSecondary, InvokePrivateBrushResolver(dockItem, "ResolveInactiveTextBrush"));
            AssertBrushMatches(controlBackgroundHover, InvokePrivateBrushResolver(dockItem, "ResolveCloseButtonHoverBrush"));
            AssertBrushMatches(controlBackgroundPressed, InvokePrivateBrushResolver(dockItem, "ResolveCloseButtonPressedBrush"));
            AssertBrushMatches(accentBrush, InvokePrivateBrushResolver(dockItem, "ResolveIndicatorBrush"));
            AssertBrushMatches(highlightBackground, InvokePrivateBrushResolver(dockItem, "ResolveDragDimBrush"));

            AssertBrushMatches(dockSplitterBackground, InvokePrivateBrushResolver(split, "ResolveBackgroundBrush"));
            AssertBrushMatches(dockSplitterHover, InvokePrivateBrushResolver(split, "ResolveHoverBrush"));
            AssertBrushMatches(accentBrush, InvokePrivateBrushResolver(split, "ResolveDraggingBrush"));
            AssertBrushMatches(dockTabStripBorder, InvokePrivateBrushResolver(split, "ResolveEdgeBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(object control, string methodName)
    {
        var method = control.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(control, null));
    }

    private static void AssertBrushMatches(Brush expected, Brush actual)
    {
        if (expected is SolidColorBrush expectedSolid && actual is SolidColorBrush actualSolid)
        {
            Assert.Equal(expectedSolid.Color, actualSolid.Color);
            return;
        }

        Assert.Same(expected, actual);
    }
}
