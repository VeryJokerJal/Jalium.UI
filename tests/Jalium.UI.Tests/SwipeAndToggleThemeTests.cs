using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class SwipeAndToggleThemeTests
{
    public SwipeAndToggleThemeTests() => Jalium.UI.Markup.ThemeLoader.Initialize();
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
    public void SwipeControl_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var textOnAccent = Assert.IsAssignableFrom<Brush>(app.Resources["TextOnAccent"]);
            var item = new SwipeItem { Text = "Delete" }; var swipeControl = new SwipeControl { LeftItems = new SwipeItems { item } };
            var host = new StackPanel(); host.Children.Add(swipeControl); host.Measure(new Size(360,160)); host.Arrange(new Rect(0,0,360,160));
            Assert.True(swipeControl.Open(SwipeDirection.Left)); host.InvalidateMeasure(); host.Measure(new Size(360,160)); host.Arrange(new Rect(0,0,360,160));
            var actions = Assert.IsType<ItemsControl>(swipeControl.FindName("PART_Actions"));
            var button = FindButton(actions); Assert.NotNull(button);
            Assert.Same(accentBrush, button!.Background); Assert.Same(textOnAccent, button.Foreground);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ToggleMenuFlyoutItem_CheckGlyphResolver_ShouldUseThemeResource()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var textPrimary = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);
            var item = new ToggleMenuFlyoutItem { IsChecked = true };
            MenuFlyoutTemplateTestHelper.Apply(item); item.Measure(new Size(240,40)); item.Arrange(new Rect(0,0,240,40));
            var check = Assert.IsType<Jalium.UI.Shapes.Path>(item.FindName("PART_Check"));
            Assert.Equal(Visibility.Visible, check.Visibility); Assert.Same(textPrimary, check.Stroke);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static SwipeActionButton? FindButton(Visual root)
    {
        if(root is SwipeActionButton button) return button;
        for(var i=0;i<root.VisualChildrenCount;i++) if(root.GetVisualChild(i) is {} child && FindButton(child) is {} found) return found;
        return null;
    }
    private static Brush InvokePrivateBrushResolver(object owner, string methodName, params object[] args)
    {
        var method = owner.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(owner, args));
    }
}
