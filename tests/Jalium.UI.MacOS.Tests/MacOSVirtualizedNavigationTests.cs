using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSVirtualizedNavigationTests
{
    [Theory]
    [InlineData(Orientation.Vertical, false)]
    [InlineData(Orientation.Horizontal, false)]
    [InlineData(Orientation.Vertical, true)]
    [InlineData(Orientation.Horizontal, true)]
    public void ThemedScrollIntoViewRealizesItsTargetOnTheNextLayout(Orientation orientation, bool variableSizes)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        _ = Application.Current ?? new Application();
        var list = new NavigationList { Width = 320, Height = 120, VariableSizes = variableSizes };
        var panelTemplate = new ItemsPanelTemplate();
        panelTemplate.SetVisualTree(() => new VirtualizingStackPanel { Orientation = orientation });
        list.ItemsPanel = panelTemplate;
        for (int i = 0; i < 500; i++) list.Items.Add($"Item {i}");
        var viewport = new Size(320, 120);
        list.Measure(viewport); list.Arrange(new Rect(new Point(), viewport));
        var host = Assert.IsType<VirtualizingStackPanel>(list.Host);
        foreach (int target in new[] { 400, 12, 499, 125, 0 })
        {
            list.ScrollIntoView($"Item {target}");
            host.Measure(viewport); host.Arrange(new Rect(new Point(), viewport));
            Assert.True(list.ItemContainerGenerator.ContainerFromIndex(target) != null,
                $"target={target}; offset={host.HorizontalOffset},{host.VerticalOffset}; extent={host.ExtentWidth},{host.ExtentHeight}; realized={string.Join(',', Enumerable.Range(0, 500).Where(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null))}");
            var row = Assert.IsAssignableFrom<FrameworkElement>(list.ItemContainerGenerator.ContainerFromIndex(target));
            var start = row.TranslatePoint(new Point(), host);
            var axisStart = orientation == Orientation.Vertical ? start.Y : start.X;
            var axisEnd = axisStart + (orientation == Orientation.Vertical ? row.ActualHeight : row.ActualWidth);
            Assert.True(axisStart >= -0.5 && axisEnd <= (orientation == Orientation.Vertical ? viewport.Height : viewport.Width) + 0.5,
                $"target={target}; bounds={axisStart}..{axisEnd}");
            Assert.InRange(host.Children.Count, 1, 80);
        }
    }

    private sealed class NavigationList : ListBox
    {
        internal Panel? Host => ItemsHost;
        internal bool VariableSizes { get; init; }
        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        {
            base.PrepareContainerForItem(element, item);
            if (VariableSizes)
            {
                int index = int.Parse(((string)item).AsSpan(5));
                element.Height = 28 + index % 5 * 14;
                element.Width = 80 + index % 5 * 27;
            }
        }
    }
}
