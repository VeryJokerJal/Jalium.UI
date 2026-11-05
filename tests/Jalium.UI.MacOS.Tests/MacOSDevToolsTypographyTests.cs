using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsTypographyTests : IDisposable
{
    public MacOSDevToolsTypographyTests() => Reset();
    public void Dispose() => Reset();

    [Fact]
    public void StandaloneMonoLeafStyleUsesPlatformDefaultBeforeApplicationExists()
    {
        Assert.Null(Application.Current);
        var text = DevToolsUi.Mono("0000 1111 MMMM");
        Assert.Equal("Menlo", text.FontFamily.Source);
        Assert.Null(Application.Current);
    }

    [Fact]
    public void MonoLeafStyleHonorsCallerResourcesAndExplicitFontValues()
    {
        var text = DevToolsUi.Mono("data");
        text.Resources["MonoFontFamily"] = new FontFamily("Consolas");
        Assert.Equal("Consolas", text.FontFamily.Source);
        text.FontFamily = new FontFamily("Courier New");
        text.Resources["MonoFontFamily"] = new FontFamily("Menlo");
        Assert.Equal("Courier New", text.FontFamily.Source);
    }

    [Fact]
    public void RuntimeTypographyUpdatesActiveDevToolsStyles()
    {
        var text = DevToolsUi.Mono("live data");
        Assert.Equal("Menlo", text.FontFamily.Source);
        _ = new Application();
        ThemeManager.ApplyTypography(ThemeManager.CurrentDisplayFontFamily, ThemeManager.CurrentBodyFontFamily, "Consolas");
        Assert.Equal("Consolas", text.FontFamily.Source);
        var local = new FontFamily("Courier New"); text.FontFamily = local;
        ThemeManager.ApplyTypography(ThemeManager.CurrentDisplayFontFamily, ThemeManager.CurrentBodyFontFamily, "Menlo");
        Assert.Same(local, text.FontFamily);
    }

    private static void Reset()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        typeof(ThemeManager).GetField("_controlThemeDictionaries", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Array.Empty<ResourceDictionary>());
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    }
}
