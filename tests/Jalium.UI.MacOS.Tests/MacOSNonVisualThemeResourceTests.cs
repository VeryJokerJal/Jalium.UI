using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class MacOSNonVisualThemeResourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeChangesRefreshNonVisualResourcesAtTheirOwnPrecedenceLayer(bool stateLayer)
    {
        Reset();
        ThemeLoader.Initialize();
        var app = new Application();
        var host = new Border();
        var brush = new SolidColorBrush();
        if (stateLayer) brush.ClearValue(SolidColorBrush.ColorProperty);
        try
        {
            app.MainWindow = new Window { Content = host };
            ThemeManager.ApplyTheme(ThemeVariant.Dark);
            if (stateLayer)
                DynamicResourceBindingOperations.SetDynamicResource(host, brush, SolidColorBrush.ColorProperty,
                    "TextFillColorPrimary", DependencyObject.LayerValueSource.VisualState);
            else
                DynamicResourceBindingOperations.SetDynamicResourceForNonVisual(host, brush, SolidColorBrush.ColorProperty, "TextFillColorPrimary");
            var dark = brush.Color;
            ThemeManager.ApplyTheme(ThemeVariant.Light);
            Assert.NotEqual(dark, brush.Color);
            if (stateLayer)
            {
                var local = Color.FromRgb(11, 22, 33);
                brush.Color = local;
                ThemeManager.ApplyTheme(ThemeVariant.Dark);
                Assert.Equal(local, brush.Color);
                brush.ClearValue(SolidColorBrush.ColorProperty);
                Assert.Equal(dark, brush.Color);
            }
        }
        finally
        {
            if (stateLayer) DynamicResourceBindingOperations.ClearDynamicResource(brush, SolidColorBrush.ColorProperty, DependencyObject.LayerValueSource.VisualState);
            else DynamicResourceBindingOperations.ClearDynamicResourceForNonVisual(brush, SolidColorBrush.ColorProperty);
            Reset();
        }
    }
    private static void Reset()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    }
}
