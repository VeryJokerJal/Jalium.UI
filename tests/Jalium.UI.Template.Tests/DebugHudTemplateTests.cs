using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DebugHudTemplateTests
{
    [Theory]
    [InlineData(1500)]
    [InlineData(390)]
    public void DiagnosticBarsAndTextFitTheHostViewport(int width)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        var type = typeof(Control).Assembly.GetType("Jalium.UI.Controls.DebugHudOverlay", true)!;
        var hud = (Control)Activator.CreateInstance(type, true)!;
        using var stream = new MemoryStream();
        hud.Resources.MergedDictionaries.Add(ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!);
        hud.Visibility = Visibility.Visible;
        Update(hud, width);
        hud.Measure(new Size(width, 300));
        hud.Arrange(new Rect(0, 0, width, 300));
        Update(hud, width);
        hud.Measure(new Size(width, 300));
        hud.Arrange(new Rect(0, 0, width, 300));
        hud.UpdateLayout();
        double Read(string name) => (double)type.GetProperty(name)!.GetValue(hud)!;
        Assert.Equal(Read("BarWidth"), Read("LayoutBarWidth") + Read("RenderBarWidth") + Read("PresentBarWidth"), 8);
        Assert.InRange(Read("BarWidth"), 0, width - hud.Padding.TotalWidth - hud.BorderThickness.TotalWidth - hud.Margin.TotalWidth);
        Assert.InRange(hud.ActualWidth + hud.Margin.TotalWidth, 1, width);
        Assert.Equal(Read("BarWidth"), Assert.IsType<StackPanel>(hud.FindName("PART_FrameTimeBar")).ActualWidth, 3);
        Assert.Equal(TextWrapping.Wrap, Assert.IsType<TextBlock>(hud.FindName("PART_TimingText")).TextWrapping);
    }

    private static void Update(Control hud, int width)
        => hud.GetType().GetMethod("Update")!.Invoke(hud, new object[]
        { 60d, 1d, 2d, 3d, 5d, 10d, "Full", "Metal", "Impeller", 1, 2, 0, 0, 3, "none", width, 844, 1f, 100L, 0, 0, 0, 0, 0, 0, 0d });
}
