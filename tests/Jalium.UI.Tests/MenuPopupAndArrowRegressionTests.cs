using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class MenuPopupAndArrowRegressionTests
{
    [Fact]
    public void MenuPopupScrollHost_MouseWheelHandledByRepeatButton_ShouldStillScroll()
    {
        var host = CreateOverflowingMenuPopupHost(out var hostType);
        var scrollViewer = GetPrivateField<ScrollViewer>(hostType, host, "_scrollViewer");
        var downButton = GetPrivateField<RepeatButton>(hostType, host, "_scrollDownButton");

        downButton.AddHandler(
            UIElement.MouseWheelEvent,
            new MouseWheelEventHandler((_, args) => args.Handled = true));

        var before = scrollViewer.VerticalOffset;
        downButton.RaiseEvent(CreateMouseWheel(new Point(6, 6), delta: -120, timestamp: 1));

        Assert.True(scrollViewer.VerticalOffset > before);
    }

    [Fact]
    public void MenuFlyoutSubItem_ZeroSize_ShouldNotDrawArrowAtOrigin()
    {
        var item = new MenuFlyoutSubItem();
        item.Measure(new Size(0, 0));
        item.Arrange(new Rect(0, 0, 0, 0));

        var dc = new RecordingDrawingContext();
        item.Render(dc);

        Assert.Equal(0, dc.DrawGeometryCalls);
    }

    [Fact]
    public void MenuItem_ZeroSizeWithSubmenu_ShouldNotDrawArrowAtOrigin()
    {
        var item = new MenuItem { Header = "Root" };
        item.Items.Add(new MenuItem { Header = "Child" });
        item.Measure(new Size(0, 0));
        item.Arrange(new Rect(0, 0, 0, 0));

        var dc = new RecordingDrawingContext();
        item.Render(dc);

        Assert.Equal(0, dc.DrawGeometryCalls);
    }

    [Fact]
    public void MenuFlyoutPresenterPopup_ShouldNotForceRootConstraint()
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem { Text = "Open" });

        var anchor = new Border();
        flyout.ShowAt(anchor);

        var popup = GetPrivateField<Popup>(typeof(FlyoutBase), flyout, "_popup");
        Assert.False(popup.ShouldConstrainToRootBounds);
    }

    [Fact]
    public void MenuItemSubmenuPopup_ShouldNotForceRootConstraint()
    {
        var root = new MenuItem { Header = "Root" };
        root.Items.Add(new MenuItem { Header = "Child" });

        root.IsSubmenuOpen = true;

        var popup = GetPrivateField<Popup>(typeof(MenuItem), root, "_submenuPopup");
        Assert.False(popup.ShouldConstrainToRootBounds);
    }

    [Fact]
    public void MenuFlyoutSubItemPopup_ShouldNotForceRootConstraint()
    {
        var subItem = new MenuFlyoutSubItem();
        subItem.Items.Add(new MenuFlyoutItem { Text = "Child" });

        subItem.ShowSubMenu();

        var popup = GetPrivateField<Popup>(typeof(MenuFlyoutSubItem), subItem, "_subPopup");
        Assert.False(popup.ShouldConstrainToRootBounds);
    }

    [Fact]
    public void ContextMenuPopup_ShouldNotForceRootConstraint()
    {
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = "Item" });

        contextMenu.Open(new Point(12, 18));

        var popup = GetPrivateField<Popup>(typeof(ContextMenu), contextMenu, "_popup");
        Assert.False(popup.ShouldConstrainToRootBounds);
    }

    private static Control CreateOverflowingMenuPopupHost(out Type hostType)
    {
        var controlsAssembly = typeof(MenuFlyout).Assembly;
        hostType = controlsAssembly.GetType("Jalium.UI.Controls.MenuPopupScrollHost")
            ?? throw new InvalidOperationException("MenuPopupScrollHost type was not found.");

        var hostInstance = Activator.CreateInstance(hostType)
            ?? throw new InvalidOperationException("Failed to create MenuPopupScrollHost.");
        var host = Assert.IsAssignableFrom<Control>(hostInstance);

        var itemsPanelProperty = hostType.GetProperty("ItemsPanel", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(itemsPanelProperty);
        var itemsPanel = Assert.IsType<StackPanel>(itemsPanelProperty!.GetValue(host));

        for (int i = 0; i < 16; i++)
        {
            itemsPanel.Children.Add(new MenuFlyoutItem { Text = $"Item {i}" });
        }

        host.Measure(new Size(220, 120));
        host.Arrange(new Rect(0, 0, 220, 120));
        return host;
    }

    private static MouseWheelEventArgs CreateMouseWheel(Point position, int delta, int timestamp)
    {
        return new MouseWheelEventArgs(
            UIElement.MouseWheelEvent,
            position,
            delta,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: timestamp);
    }

    private static T GetPrivateField<T>(Type ownerType, object owner, string fieldName)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(owner);
        Assert.NotNull(value);
        return (T)value!;
    }

    private sealed class RecordingDrawingContext : DrawingContext
    {
        public int DrawGeometryCalls { get; private set; }

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            DrawGeometryCalls++;
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }
}
