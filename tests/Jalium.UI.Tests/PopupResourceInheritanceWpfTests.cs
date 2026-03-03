using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class PopupResourceInheritanceWpfTests
{
    [Fact]
    public void DetachedPopup_ShouldResolveResourcesFromPlacementTarget()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var markerBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x77, 0xAA));
            var host = new Grid();
            host.Resources["PopupProbe"] = markerBrush;

            var popup = new Popup
            {
                PlacementTarget = host
            };

            var resolved = ResourceLookup.FindResource(popup, "PopupProbe");
            Assert.Same(markerBrush, resolved);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }
}
