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
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void FlyoutPresenter_ThemeValuesRemainOwnedByTheStyle()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var frame = new FlyoutPresenter
            {
                Style = (Style)app.Resources[typeof(FlyoutPresenter)]
            };
            frame.ApplyTemplate();
            var chrome = Assert.IsType<Border>(frame.FindName("PART_Root"));
            Assert.Same(app.Resources["MenuFlyoutPresenterBackground"], chrome.Background);
            Assert.Same(app.Resources["MenuFlyoutPresenterBorderBrush"], chrome.BorderBrush);
            Assert.False(frame.HasLocalValue(Control.BackgroundProperty));
            Assert.False(frame.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(frame.HasLocalValue(Control.PaddingProperty));
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
            var presenter = Assert.IsType<FlyoutPresenter>(popup.Child);
            presenter.ApplyTemplate();
            var chrome = Assert.IsType<Border>(presenter.FindName("PART_Root"));
            Assert.Same(popupBackground, chrome.Background);
            Assert.Same(popupBorder, chrome.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
