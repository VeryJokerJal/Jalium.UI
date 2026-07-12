using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class PopupToolTipWpfParityTests
{
    [Fact]
    public void PopupSurface_DeclaresWpfDependencyPropertiesAndVirtualHooks()
    {
        string[] fields =
        [
            nameof(Popup.AllowsTransparencyProperty),
            nameof(Popup.CustomPopupPlacementCallbackProperty),
            nameof(Popup.HasDropShadowProperty),
            nameof(Popup.PlacementRectangleProperty),
            nameof(Popup.PopupAnimationProperty),
        ];

        foreach (var name in fields)
        {
            var field = typeof(Popup).GetField(
                name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.NotNull(field);
            Assert.True(field!.IsInitOnly);
            Assert.IsType<DependencyProperty>(field.GetValue(null));
        }

        var hasDropShadow = typeof(Popup).GetProperty(
            nameof(Popup.HasDropShadow),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        Assert.True(hasDropShadow.CanRead);
        Assert.False(hasDropShadow.CanWrite);

        AssertVirtualHook("OnOpened");
        AssertVirtualHook("OnClosed");

        var createRootPopup = typeof(Popup).GetMethod(
            nameof(Popup.CreateRootPopup),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null,
            [typeof(Popup), typeof(UIElement)],
            null);
        Assert.NotNull(createRootPopup);
    }

    [Fact]
    public void Transparency_CoercesAnimationAndUpdatesDropShadowState()
    {
        var popup = new Popup
        {
            PopupAnimation = PopupAnimation.Slide,
        };

        Assert.False(popup.AllowsTransparency);
        Assert.Equal(PopupAnimation.None, popup.PopupAnimation);
        Assert.False(popup.HasDropShadow);

        popup.AllowsTransparency = true;
        popup.PopupAnimation = PopupAnimation.Scroll;

        Assert.Equal(PopupAnimation.Scroll, popup.PopupAnimation);
        Assert.Equal(SystemParameters.DropShadow, popup.HasDropShadow);

        popup.AllowsTransparency = false;
        Assert.Equal(PopupAnimation.None, popup.PopupAnimation);
        Assert.False(popup.HasDropShadow);
    }

    [Fact]
    public void CustomPlacement_ReceivesTargetSizeAndOffsetAndUsesFirstCandidate()
    {
        var target = new Border();
        target.Arrange(new Rect(20, 30, 100, 50));

        Size observedPopupSize = default;
        Size observedTargetSize = default;
        Point observedOffset = default;

        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Custom,
            PlacementRectangle = new Rect(5, 6, 40, 20),
            HorizontalOffset = 3,
            VerticalOffset = 4,
            CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                observedPopupSize = popupSize;
                observedTargetSize = targetSize;
                observedOffset = offset;
                return [new CustomPopupPlacement(new Point(7, 8), PopupPrimaryAxis.Horizontal)];
            },
        };

        var method = typeof(Popup).GetMethod(
            "CalculateWindowLocalPosition",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (Point)method.Invoke(popup, [new Size(10, 11)])!;

        Assert.Equal(new Size(10, 11), observedPopupSize);
        Assert.Equal(new Size(40, 20), observedTargetSize);
        Assert.Equal(new Point(3, 4), observedOffset);
        Assert.Equal(new Point(32, 44), result);
    }

    [Fact]
    public void CreateRootPopup_BindsAllPlacementStateWithIsOpenLast()
    {
        var target = new Border();
        var toolTip = new ToolTip
        {
            PlacementTarget = target,
            Placement = PlacementMode.Right,
            PlacementRectangle = new Rect(1, 2, 30, 40),
            HorizontalOffset = 5,
            VerticalOffset = 6,
            StaysOpen = false,
        };
        CustomPopupPlacementCallback callback = (_, _, _) =>
            [new CustomPopupPlacement(Point.Zero, PopupPrimaryAxis.None)];
        toolTip.CustomPopupPlacementCallback = callback;

        var popup = new Popup();
        Popup.CreateRootPopup(popup, toolTip);

        Assert.Same(toolTip, popup.Child);
        Assert.Same(target, popup.PlacementTarget);
        Assert.Equal(PlacementMode.Right, popup.Placement);
        Assert.Equal(toolTip.PlacementRectangle, popup.PlacementRectangle);
        Assert.Equal(5, popup.HorizontalOffset);
        Assert.Equal(6, popup.VerticalOffset);
        Assert.False(popup.StaysOpen);
        Assert.Same(callback, popup.CustomPopupPlacementCallback);

        toolTip.HorizontalOffset = 17;
        toolTip.Placement = PlacementMode.Top;
        Assert.Equal(17, popup.HorizontalOffset);
        Assert.Equal(PlacementMode.Top, popup.Placement);
    }

    [Fact]
    public void PopupAndToolTipVirtualHooks_RaiseTheirPublicEvents()
    {
        var popup = new HookPopup();
        var popupOpened = 0;
        var popupClosed = 0;
        popup.Opened += (_, _) => popupOpened++;
        popup.Closed += (_, _) => popupClosed++;

        popup.RaiseOpened();
        popup.RaiseClosed();

        Assert.Equal(1, popup.OpenedCalls);
        Assert.Equal(1, popup.ClosedCalls);
        Assert.Equal(1, popupOpened);
        Assert.Equal(1, popupClosed);

        var toolTip = new HookToolTip();
        var toolTipOpened = 0;
        var toolTipClosed = 0;
        toolTip.Opened += (_, _) => toolTipOpened++;
        toolTip.Closed += (_, _) => toolTipClosed++;

        toolTip.RaiseOpened();
        toolTip.RaiseClosed();

        Assert.Equal(1, toolTip.OpenedCalls);
        Assert.Equal(1, toolTip.ClosedCalls);
        Assert.Equal(1, toolTipOpened);
        Assert.Equal(1, toolTipClosed);
    }

    private static void AssertVirtualHook(string name)
    {
        var method = typeof(Popup).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            [typeof(EventArgs)],
            null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }

    private sealed class HookPopup : Popup
    {
        public int OpenedCalls { get; private set; }
        public int ClosedCalls { get; private set; }

        public void RaiseOpened() => OnOpened(EventArgs.Empty);
        public void RaiseClosed() => OnClosed(EventArgs.Empty);

        protected override void OnOpened(EventArgs e)
        {
            OpenedCalls++;
            base.OnOpened(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            ClosedCalls++;
            base.OnClosed(e);
        }
    }

    private sealed class HookToolTip : ToolTip
    {
        public int OpenedCalls { get; private set; }
        public int ClosedCalls { get; private set; }

        public void RaiseOpened() => OnOpened(new RoutedEventArgs(OpenedEvent, this));
        public void RaiseClosed() => OnClosed(new RoutedEventArgs(ClosedEvent, this));

        protected override void OnOpened(RoutedEventArgs e)
        {
            OpenedCalls++;
            base.OnOpened(e);
        }

        protected override void OnClosed(RoutedEventArgs e)
        {
            ClosedCalls++;
            base.OnClosed(e);
        }
    }
}
