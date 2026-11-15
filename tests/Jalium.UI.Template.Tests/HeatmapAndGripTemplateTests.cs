using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class HeatmapAndGripTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void HeatmapTemplateOwnsTheLegendAndReusesItsGradient()
    {
        var map = Create();
        Layout(map);
        Assert.Same(map, Part<GeographicHeatmapView>(map, "PART_HeatmapView").Owner);
        Assert.Same(map.LegendGradientBrush, Part<Border>(map, "PART_GradientBar").Background);
        Assert.Equal("强度", Part<TextBlock>(map, "PART_LegendTitle").Text);
        Assert.NotNull(map.HeatmapContentClip);
        var brush = map.LegendGradientBrush;
        Layout(map);
        Assert.Same(brush, map.LegendGradientBrush);
        map.Gradient = new HeatmapGradient(new GradientStopCollection
        {
            new GradientStop(Colors.Black, 0), new GradientStop(Colors.White, 1)
        });
        Assert.NotSame(brush, map.LegendGradientBrush);
        Assert.Equal(Colors.Black, map.LegendGradientBrush!.GradientStops[0].Color);
    }

    [Fact]
    public void LegendPlacementAndVisibilityFollowOwnerProperties()
    {
        var map = Create();
        Layout(map);
        var before = map.LegendBounds;
        map.LegendPosition = HeatmapLegendPosition.TopLeft;
        Layout(map);
        Assert.True(map.LegendBounds.X < before.X);
        Assert.Equal(10, map.LegendBounds.Y);
        map.ShowLegend = false;
        map.IsEnabled = false;
        Assert.Equal(Visibility.Collapsed, Part<Border>(map, "PART_Legend").Visibility);
        Assert.Equal("LegendHidden", VisualStateManager.GetCurrentStateName(map, "LegendStates"));
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(map, "CommonStates"));
    }

    [Fact]
    public void HeatmapRetemplatingReleasesItsSurfaceAndPreservesPoints()
    {
        var map = Create();
        Layout(map);
        var retired = Part<GeographicHeatmapView>(map, "PART_HeatmapView");
        var points = map.Points;
        var blank = new ControlTemplate(typeof(GeographicHeatmap));
        blank.SetVisualTree(() => new Border());
        map.Template = blank;
        Layout(map);
        Assert.Null(retired.Owner);
        Assert.Same(points, map.Points);
        map.ClearValue(Control.TemplateProperty);
        Layout(map);
        Assert.Same(map, Part<GeographicHeatmapView>(map, "PART_HeatmapView").Owner);
        Layout(new GeographicHeatmapView());
    }

    [Fact]
    public void ResizeGripUsesAReplaceablePathWithTheOwnersForeground()
    {
        var grip = new ResizeGrip { Foreground = Brushes.Red };
        grip.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(grip);
        Assert.Same(Brushes.Red, Part<Jalium.UI.Shapes.Path>(grip, "PART_Grip").Stroke);
        grip.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(grip, "CommonStates"));
        var blank = new ControlTemplate(typeof(ResizeGrip));
        blank.SetVisualTree(() => new Border());
        grip.Template = blank;
        Layout(grip);
        Assert.Null(grip.FindName("PART_Grip"));
    }

    private static GeographicHeatmap Create()
    {
        var map = new GeographicHeatmap { LegendTitle = "强度", HighLabel = "高", MidLabel = "中", LowLabel = "低" };
        map.Resources.MergedDictionaries.Add(Defaults.Value);
        return map;
    }
    private static T Part<T>(Control owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(FrameworkElement element)
    {
        for (var i = 0; i < 4; i++)
        {
            element.InvalidateMeasure(); element.Measure(new Size(360, 240));
            element.Arrange(new Rect(0, 0, 360, 240)); element.UpdateLayout();
        }
    }
}
