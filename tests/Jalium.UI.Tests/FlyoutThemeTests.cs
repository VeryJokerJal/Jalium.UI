using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class FlyoutThemeTests
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
    public void FlyoutBase_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var popupBackground = Assert.IsAssignableFrom<Brush>(app.Resources["MenuFlyoutPresenterBackground"]);
            var popupBorder = Assert.IsAssignableFrom<Brush>(app.Resources["MenuFlyoutPresenterBorderBrush"]);
            var flyout = new CommandBarFlyout();

            Assert.Same(popupBackground, InvokePrivateBrushResolver(flyout, "ResolvePopupBackgroundBrush"));
            Assert.Same(popupBorder, InvokePrivateBrushResolver(flyout, "ResolvePopupBorderBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CommandBarFlyout_PopupChrome_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var popupBackground = Assert.IsAssignableFrom<Brush>(app.Resources["MenuFlyoutPresenterBackground"]);
            var popupBorder = Assert.IsAssignableFrom<Brush>(app.Resources["MenuFlyoutPresenterBorderBrush"]);
            var flyout = new CommandBarFlyout();
            var anchor = new Border();

            flyout.ShowAt(anchor);

            var popupField = typeof(FlyoutBase).GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(popupField);
            var popup = Assert.IsType<Popup>(popupField!.GetValue(flyout));
            var chrome = Assert.IsType<Border>(popup.Child);

            Assert.Same(popupBackground, chrome.Background);
            Assert.Same(popupBorder, chrome.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(FlyoutBase flyout, string methodName)
    {
        var method = typeof(FlyoutBase).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(flyout, null));
    }
}
