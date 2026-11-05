using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DockIndicatorTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplatePlacesStableTargetsAtTheExistingDockHitRegions()
    {
        var guide = Create(); Layout(guide);
        Assert.Same(guide.Targets, Assert.IsType<ItemsControl>(guide.FindName("PART_Targets")).ItemsSource);
        Assert.Equal(5, guide.Targets.Count(x => x.Visibility == Visibility.Visible));
        var center = guide.Targets.Single(x => x.Position == DockPosition.Center);
        Assert.Equal(new Rect(164, 104, 32, 32), center.Bounds);
        Assert.False(guide.IsHitTestVisible);
        var identities = guide.Targets.ToArray();
        guide.HoveredPosition = DockPosition.Left;
        Assert.Equal(new Rect(0, 0, 180, 240), guide.PreviewBounds);
        Assert.Equal(Visibility.Visible, guide.PreviewVisibility);
        Assert.Single(guide.Targets, x => x.IsHovered);
        Assert.Equal("TargetHovered", VisualStateManager.GetCurrentStateName(guide, "HoverStates"));
        Assert.True(identities.Zip(guide.Targets).All(pair => ReferenceEquals(pair.First, pair.Second)));
    }

    [Fact]
    public void EdgeModeAndHiddenTargetsKeepPreviewVisibilityConsistent()
    {
        var guide = Create(); guide.ShowCenterCross = false; guide.ShowEdgeButtons = true; Layout(guide);
        Assert.Equal(4, guide.Targets.Count(x => x.Visibility == Visibility.Visible));
        guide.HoveredPosition = DockPosition.Center;
        Assert.Equal(Visibility.Collapsed, guide.PreviewVisibility);
        guide.HoveredPosition = DockPosition.EdgeBottom;
        Assert.Equal(new Rect(0, 120, 360, 120), guide.PreviewBounds);
        Assert.Equal(Visibility.Visible, guide.PreviewVisibility);
        guide.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(guide, "CommonStates"));
    }

    [Fact]
    public void HoveredTargetUsesTheTemplateHighlightAndClearsItWhenLeaving()
    {
        var guide = Create(); guide.HoverBackground = Brushes.Green; Layout(guide); guide.HoveredPosition = DockPosition.Left; Layout(guide);
        var targets = Descendants(guide).OfType<Border>().Where(x => x.Name == "TargetBorder").ToArray();
        Assert.NotEmpty(targets);
        var highlighted = Assert.IsAssignableFrom<Brush>(guide.HoverBackground);
        Assert.Single(targets, x => ReferenceEquals(x.Background, highlighted));
        guide.HoveredPosition = DockPosition.None; Layout(guide);
        Assert.DoesNotContain(targets, x => ReferenceEquals(x.Background, highlighted));
    }

    [Fact]
    public void ReplacingTemplatesDetachesOldModelsWithoutChangingDragState()
    {
        var guide = Create(); Layout(guide); guide.HoveredPosition = DockPosition.Center;
        var old = Assert.IsType<ItemsControl>(guide.FindName("PART_Targets"));
        var blank = new ControlTemplate(typeof(DockIndicatorVisual)); blank.SetVisualTree(() => new Border());
        guide.Template = blank; Layout(guide);
        Assert.Null(old.ItemsSource);
        Assert.Equal(DockPosition.Center, guide.HoveredPosition);
        guide.ClearValue(Control.TemplateProperty); Layout(guide);
        Assert.NotSame(old, guide.FindName("PART_Targets"));
        Assert.Equal(Visibility.Visible, Assert.IsType<Border>(guide.FindName("PART_Preview")).Visibility);
    }

    private static DockIndicatorVisual Create()
    {
        var guide = new DockIndicatorVisual { ShowCenterCross = true };
        guide.Resources.MergedDictionaries.Add(Defaults.Value); return guide;
    }
    private static IEnumerable<Visual> Descendants(Visual root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Assert.IsAssignableFrom<Visual>(VisualTreeHelper.GetChild(root, i));
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Layout(FrameworkElement element)
    {
        for (var i = 0; i < 4; i++)
        {
            element.InvalidateMeasure(); element.Measure(new Size(360, 240));
            element.Arrange(new Rect(0, 0, 360, 240)); element.UpdateLayout();
        }
    }
}
