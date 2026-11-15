using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

// Managed layout and hit testing; physical mouse acceptance is recorded separately.
[Collection("macOS Window globals")]
public sealed class MacOSVirtualizedHitTestTests
{
    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void ScrolledContainerKeepsNestedActionsInItsHitPath(Orientation orientation)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        _ = Application.Current ?? new Application();
        var list = new ActionList();
        var itemsPanel = new ItemsPanelTemplate();
        itemsPanel.SetVisualTree(() => new VirtualizingStackPanel { Orientation = orientation });
        list.ItemsPanel = itemsPanel;
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() => new ScrollViewer
        {
            CanContentScroll = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new ItemsPresenter()
        });
        list.Template = template;
        for (int i = 0; i < 30; i++) list.Items.Add(i);
        var window = new DisplayedTestWindow { Content = list };
        try
        {
            var viewport = new Size(250, 210);
            window.Measure(viewport); window.Arrange(new Rect(new Point(), viewport));
            var panel = Assert.IsType<VirtualizingStackPanel>(list.Host);
            // Leave the first complete row at +20 in the viewport. Passing the
            // viewport point directly to the row then hits its background,
            // avoiding a null hit that could mask the bug through fallback.
            if (orientation == Orientation.Vertical) panel.SetVerticalOffset(20 * 80 - 20);
            else panel.SetHorizontalOffset(20 * 110 - 20);
            for (int pass = 0; pass < 3; pass++)
            {
                panel.InvalidateMeasure(); panel.Measure(viewport); panel.Arrange(new Rect(new Point(), viewport));
            }
            Assert.True(list.ItemContainerGenerator.ContainerFromIndex(20) != null,
                $"offset={panel.HorizontalOffset},{panel.VerticalOffset}; extent={panel.ExtentWidth},{panel.ExtentHeight}; realized={string.Join(',', Enumerable.Range(0, 30).Where(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null))}");
            var row = Assert.IsType<ActionRow>(list.ItemContainerGenerator.ContainerFromIndex(20));
            var point = row.Action.TranslatePoint(new Point(10, 10), panel);
            var path = new List<Visual>();
            for (var hit = panel.HitTest(point)?.VisualHit as Visual; hit != null; hit = hit.VisualParent) path.Add(hit);
            Assert.Contains(row.Action, path);
        }
        finally { window.Close(); }
    }

    private sealed class ActionList : ListBox
    {
        internal Panel? Host => ItemsHost;
        protected override FrameworkElement GetContainerForItem(object item) => new ActionRow();
    }

    private sealed class ActionRow : ListBoxItem
    {
        internal Button Action { get; } = new()
        {
            Content = "!", Width = 20, Height = 20, MinWidth = 0, MinHeight = 0,
            Margin = new Thickness(12, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };

        internal ActionRow()
        {
            Width = 110; Height = 80; MinHeight = 0;
            var template = new ControlTemplate(typeof(ListBoxItem));
            template.SetVisualTree(() => { var root = new Grid(); root.Children.Add(Action); return root; });
            Template = template;
        }
    }
}
