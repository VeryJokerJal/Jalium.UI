using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class ContextMenuServiceTests
{
    [Fact]
    public void RightClick_ShouldOpenAttachedContextMenu()
    {
        var owner = new Border();
        var menu = new ContextMenu();
        ContextMenuService.SetContextMenu(owner, menu);

        owner.RaiseEvent(CreateRightMouseUp(new Point(42, 18)));

        Assert.True(menu.IsOpen);
        Assert.Same(owner, menu.PlacementTarget);
    }

    [Fact]
    public void ShiftF10_ShouldOpenAttachedContextMenuAndFallbackPlacement()
    {
        var owner = new Border();
        var menu = new ContextMenu();
        ContextMenuService.SetContextMenu(owner, menu);
        ContextMenuService.SetPlacement(owner, PlacementMode.MousePoint);

        owner.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.F10, ModifierKeys.Shift, isDown: true, isRepeat: false, timestamp: 0));

        Assert.True(menu.IsOpen);
        Assert.Equal(PlacementMode.Bottom, menu.Placement);
        Assert.Same(owner, menu.PlacementTarget);
    }

    [Fact]
    public void ServiceIsEnabledFalse_ShouldNotOpen()
    {
        var owner = new Border();
        var menu = new ContextMenu();
        ContextMenuService.SetContextMenu(owner, menu);
        ContextMenuService.SetIsEnabled(owner, false);

        owner.RaiseEvent(CreateRightMouseUp(new Point(12, 8)));

        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void DisabledOwner_WithShowOnDisabled_ShouldOpen()
    {
        var owner = new Border { IsEnabled = false };
        var menu = new ContextMenu();
        ContextMenuService.SetContextMenu(owner, menu);
        ContextMenuService.SetShowOnDisabled(owner, true);

        owner.RaiseEvent(CreateRightMouseUp(new Point(8, 6)));

        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void ExplicitOpen_WithOwnerMenuPoint_ShouldApplyAttachedSettings()
    {
        var owner = new Border();
        var menu = new ContextMenu();
        ContextMenuService.SetPlacement(owner, PlacementMode.Right);
        ContextMenuService.SetHorizontalOffset(owner, 6);
        ContextMenuService.SetVerticalOffset(owner, 9);

        ContextMenuService.Open(owner, menu, new Point(20, 15));

        Assert.True(menu.IsOpen);
        Assert.Same(owner, menu.PlacementTarget);
        Assert.Equal(PlacementMode.Right, menu.Placement);
        Assert.Equal(6, menu.HorizontalOffset);
        Assert.Equal(9, menu.VerticalOffset);
    }

    [Fact]
    public void OpeningEventHandled_ShouldCancelOpen()
    {
        var owner = new Border();
        var menu = new ContextMenu();
        ContextMenuService.SetContextMenu(owner, menu);

        bool openingRaised = false;
        ContextMenuService.AddContextMenuOpeningHandler(owner, (s, e) =>
        {
            openingRaised = true;
            Assert.True(e.IsOpening);
            e.Handled = true;
        });

        owner.RaiseEvent(CreateRightMouseUp(new Point(30, 11)));

        Assert.True(openingRaised);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void ClosingEvent_ShouldBeRaisedAfterOpen()
    {
        var owner = new Border();
        var menu = new ContextMenu();

        bool closingRaised = false;
        ContextMenuService.AddContextMenuClosingHandler(owner, (s, e) =>
        {
            closingRaised = true;
            Assert.False(e.IsOpening);
        });

        ContextMenuService.Open(owner, menu, new Point(3, 4));
        menu.Close();

        Assert.True(closingRaised);
    }

    private static MouseButtonEventArgs CreateRightMouseUp(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseUpEvent,
            position,
            MouseButton.Right,
            MouseButtonState.Released,
            clickCount: 1,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);
    }
}
