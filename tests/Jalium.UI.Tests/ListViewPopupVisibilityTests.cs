using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ListViewPopupVisibilityTests
{
    [Fact]
    public void ListViewItem_ShouldBeInheritable_AndTemplatePartHookShouldWork()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var host = new StackPanel { Width = 400, Height = 200 };
            var item = new DerivedListViewItem { Content = "Item" };
            host.Children.Add(item);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.True(item.CellsPanelAttached);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void PopupWithListView_ShouldGenerateAndLayoutItems()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var listView = new InspectableListView
            {
                Width = 280,
                Height = 140,
                ItemsSource = new[] { "Alpha", "Beta", "Gamma" }
            };

            var popup = new Popup
            {
                Child = listView,
                ShouldConstrainToRootBounds = true,
                StaysOpen = true
            };

            var root = new Grid { Width = 800, Height = 600 };
            root.Children.Add(popup);

            var window = new Window
            {
                Width = 800,
                Height = 600,
                Content = root
            };
            app.MainWindow = window;

            popup.PlacementTarget = root;
            popup.IsOpen = true;

            window.Measure(new Size(800, 600));
            window.Arrange(new Rect(0, 0, 800, 600));

            var itemsHost = listView.ExposedItemsHost;
            Assert.NotNull(itemsHost);
            Assert.Equal(3, itemsHost!.Children.Count);
            Assert.All(itemsHost.Children.OfType<ListViewItem>(), item => Assert.True(item.RenderSize.Height > 0));

            popup.IsOpen = false;
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

    private sealed class InspectableListView : ListView
    {
        public Panel? ExposedItemsHost => ItemsHost;
    }

    private sealed class DerivedListViewItem : ListViewItem
    {
        public bool CellsPanelAttached { get; private set; }

        protected override void OnCellsPanelAttached(StackPanel? cellsPanel)
        {
            CellsPanelAttached = cellsPanel != null;
            base.OnCellsPanelAttached(cellsPanel);
        }
    }
}
