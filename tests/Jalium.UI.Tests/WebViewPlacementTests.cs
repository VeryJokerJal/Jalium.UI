using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class WebViewPlacementTests
{
    [Fact]
    public void CalculateControllerBounds_ShouldStartAtOrigin_WhenFullyVisible()
    {
        var rawBounds = new PixelRect(120, 80, 300, 200);
        var visibleBounds = new PixelRect(120, 80, 300, 200);

        var controllerBounds = WebView.CalculateControllerBounds(rawBounds, visibleBounds);

        Assert.Equal(new PixelRect(0, 0, 300, 200), controllerBounds);
    }

    [Fact]
    public void CalculateControllerBounds_ShouldPreserveHiddenOffset_WhenClipped()
    {
        var rawBounds = new PixelRect(120, 80, 300, 200);
        var visibleBounds = new PixelRect(170, 110, 250, 170);

        var controllerBounds = WebView.CalculateControllerBounds(rawBounds, visibleBounds);

        Assert.Equal(new PixelRect(-50, -30, 300, 200), controllerBounds);
    }
}
