using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupPlacementLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstrainedPopupFollowsOwnerResizeWithoutLosingItsEditor(bool custom)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(custom);
        var root = h.Root;
        Assert.Equal(126, Canvas.GetLeft(root), 6);
        h.Resize(560, 380);
        Assert.Same(root, h.Card.VisualParent);
        Assert.True(h.Popup.IsOpen);
        Assert.InRange(Canvas.GetLeft(root) + root.Width, 0, h.Window.ActualWidth + .01);
        Assert.InRange(Canvas.GetTop(root) + root.Height, 0, h.Window.ActualHeight + .01);
        Assert.Same(h.Editor, Keyboard.FocusedElement);
        Assert.Equal("保留输入与选区🙂", h.Editor.Text);
        Assert.Equal(2, h.Editor.SelectionStart);
        Assert.Equal(3, h.Editor.SelectionLength);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingAnAnchorInTheSameCanvasRepositionsItsExistingPopup(bool custom)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(custom);
        Canvas.SetLeft(h.Anchor, 80);
        Canvas.SetTop(h.Anchor, 160);
        h.Pump();
        Assert.Equal(190, Canvas.GetLeft(h.Root), 6);
        Assert.Equal(h.Anchor.TranslatePoint(new Point(), h.Window).Y, Canvas.GetTop(h.Root), 6);
        Assert.Same(h.Editor, Keyboard.FocusedElement);
    }

    [Fact]
    public void RenderOnlyAnchorTransformChangesAlsoRepositionThePopup()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        h.Anchor.RenderTransform = new TranslateTransform(20, 15);
        h.Pump();
        Assert.Equal(146, Canvas.GetLeft(h.Root), 6);
        Assert.Equal(75, Canvas.GetTop(h.Root), 6);
    }

    [Fact]
    public void UnchangedRenderLayoutsDoNotRepeatedlyRepositionTheSettledPopup()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        int before = h.Popup.PlacementQueries;
        for (int i = 0; i < 10; i++) h.Pump();
        Assert.Equal(before, h.Popup.PlacementQueries);
        Assert.True(h.Window.IsMeasureValid);
        Assert.True(h.Window.IsArrangeValid);
    }

    [Fact]
    public void AQueuedOldLayoutCannotMoveAReopenedPopupWithDifferentContentSize()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        var old = h.Root;
        h.Window.Width = 560;
        h.Window.InvalidateMeasure();
        Host.RenderLayout(h.Window);
        h.Popup.IsOpen = false;
        h.Card.Width = 200;
        h.Card.Height = 120;
        Canvas.SetLeft(h.Anchor, 280);
        Canvas.SetTop(h.Anchor, 220);
        h.Popup.IsOpen = true;
        h.Pump();
        Assert.NotSame(old, h.Root);
        Assert.Equal(200, h.Root.Width, 6);
        Assert.InRange(Canvas.GetLeft(h.Root) + h.Root.Width, 0, 560.01);
        Assert.Equal(220, Canvas.GetTop(h.Root), 6);
        Assert.True(h.Popup.IsOpen);
    }

    [Fact]
    public void ContentGrowthAndOwnerResizeUseTheNewPopupSize()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        h.Card.Width = 480;
        h.Pump();
        h.Resize(560, 380);
        Assert.Equal(480, h.Root.Width, 6);
        Assert.Equal(80, Canvas.GetLeft(h.Root), 6);
    }

    [Fact]
    public void PlacementRectangleUsesTheAnchorsScaledCoordinates()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        h.Anchor.RenderTransform = new ScaleTransform(1.5, 2);
        h.Popup.PlacementRectangle = new Rect(4, 5, 40, 12);
        h.Pump();
        Assert.Equal(82, Canvas.GetLeft(h.Root), 6);
        Assert.Equal(70, Canvas.GetTop(h.Root), 6);
    }

    [Fact]
    public void APlacementCallbackCannotApplyItsOldSizeToAReopenedRoot()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        bool reopen = false;
        h.Popup.Placement = PlacementMode.Custom;
        h.Popup.CustomPopupPlacementCallback = (_, _, _) =>
        {
            if (reopen)
            {
                reopen = false;
                h.Popup.IsOpen = false;
                h.Card.Width = 200;
                h.Popup.IsOpen = true;
            }
            return [new CustomPopupPlacement(new Point(284, 17), PopupPrimaryAxis.None)];
        };
        var old = h.Root;
        reopen = true;
        h.Popup.UpdatePosition();
        Assert.NotSame(old, h.Root);
        Assert.Equal(200, h.Root.Width, 6);
        Assert.Equal(300, Canvas.GetLeft(h.Root), 6);
    }

    private sealed class CountedPopup : Popup
    {
        internal int PlacementQueries;
        internal override bool CanCreateMacOSPopupHost
        {
            get { PlacementQueries++; return false; }
        }
    }

    private sealed class Host : IDisposable
    {
        private static readonly MethodInfo Render = typeof(Window).GetMethod(
            "UpdateLayoutForRender", BindingFlags.Instance | BindingFlags.NonPublic)!;
        internal readonly DisplayedTestWindow Window;
        internal readonly Canvas Canvas = new();
        internal readonly Control Anchor = new() { Width = 110, Height = 32, Focusable = true };
        internal readonly TextBox Editor = new() { Text = "保留输入与选区🙂", Height = 36 };
        internal readonly Border Card;
        internal readonly CountedPopup Popup;
        internal PopupRoot Root => Assert.IsType<PopupRoot>(Card.VisualParent);

        internal Host(bool custom = false)
        {
            Keyboard.Initialize();
            Keyboard.ClearFocus();
            Window = new DisplayedTestWindow
            {
                Width = 680, Height = 620, Content = Canvas,
                TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native
            };
            Canvas.Children.Add(Anchor);
            Controls.Canvas.SetLeft(Anchor, 16);
            Controls.Canvas.SetTop(Anchor, 60);
            RenderLayout(Window);
            Card = new Border { Width = 440, Height = 340, Child = Editor };
            Popup = new CountedPopup
            {
                PlacementTarget = Anchor, Child = Card, Placement = PlacementMode.Right,
                ShouldConstrainToRootBounds = true, StaysOpen = true, PopupAnimation = PopupAnimation.None
            };
            Popup.IsOpen = true;
            Pump();
            Assert.True(Editor.Focus());
            Editor.Select(2, 3);
        }

        internal static void RenderLayout(Window window) => Render.Invoke(window, null);
        internal void Pump()
        {
            for (int i = 0; i < 3; i++)
            {
                RenderLayout(Window);
                Dispatcher.CurrentDispatcher.ProcessQueue();
            }
        }
        internal void Resize(double width, double height)
        {
            Window.Width = width; Window.Height = height;
            Window.InvalidateMeasure(); Pump();
        }
        public void Dispose()
        {
            Popup.IsOpen = false;
            Keyboard.ClearFocus();
            Window.Close();
            Dispatcher.CurrentDispatcher.ProcessQueue();
        }
    }
}
