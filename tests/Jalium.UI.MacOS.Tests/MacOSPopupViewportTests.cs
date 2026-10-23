using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupViewportTests
{
    public static IEnumerable<object[]> Surfaces()
    {
        foreach (int depth in new[] { 0, 1, 2 })
        foreach (bool custom in new[] { false, true })
        foreach (bool vertical in new[] { false, true })
            yield return [depth, custom, vertical];
    }

    public static IEnumerable<object[]> MissingViewports()
    {
        foreach (var row in Surfaces())
        foreach (bool blindViewer in new[] { false, true })
            yield return [row[0], row[1], row[2], blindViewer];
    }

    public static IEnumerable<object[]> PopupViewports() => MissingViewports().Where(row => (int)row[0] > 0);

    [Theory, MemberData(nameof(MissingViewports))]
    public void InitialMeasureUsesThePresentingWindowsViewport(
        int depth, bool custom, bool vertical, bool blindViewer)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var scene = new Scene(depth, custom, vertical, blindViewer ? "blind" : "none");
        scene.Settle();
        Assert.Equal(scene.SurfaceAxis, scene.ViewportAxis, 4);
        scene.AssertRealizationBound();
        Assert.Same(scene.Owner, Window.GetWindow(scene.Panel));
        Assert.Same(scene.Surface, Window.GetMacOSPhysicalWindow(scene.Panel));
    }

    [Theory, MemberData(nameof(Surfaces))]
    public void AReadyAncestorViewportStillHasPriority(int depth, bool custom, bool vertical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var scene = new Scene(depth, custom, vertical, "ready");
        scene.Settle();
        var viewer = Assert.IsType<ScrollViewer>(scene.Viewer);
        double viewport = vertical ? viewer.ViewportWidth : viewer.ViewportHeight;
        Assert.True(viewport > 0);
        Assert.NotEqual(scene.SurfaceAxis, viewport);
        Assert.Equal(viewport, scene.ViewportAxis, 4);
    }

    [Theory, MemberData(nameof(PopupViewports))]
    public void OwnerResizeDoesNotChangeAnExternalPopupsViewport(
        int depth, bool custom, bool vertical, bool blindViewer)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var scene = new Scene(depth, custom, vertical, blindViewer ? "blind" : "none");
        scene.Settle();
        double expected = scene.SurfaceAxis;
        scene.Owner.Width += 240; scene.Owner.Height += 170;
        Layout(scene.Owner);
        scene.Settle();
        Assert.Equal(expected, scene.SurfaceAxis, 4);
        Assert.Equal(expected, scene.ViewportAxis, 4);
        scene.AssertRealizationBound();
    }

    [Theory, MemberData(nameof(PopupViewports))]
    public void PopupResizeRefreshesTheFallbackInsteadOfKeepingThePreviousViewport(
        int depth, bool custom, bool vertical, bool blindViewer)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var scene = new Scene(depth, custom, vertical, blindViewer ? "blind" : "none");
        scene.Settle();
        double previous = scene.SurfaceAxis;
        scene.Body.Width = scene.Surface.Width = 320;
        scene.Body.Height = scene.Surface.Height = 240;
        Layout(scene.Surface);
        scene.ResetBlindViewer();
        scene.Settle();
        Assert.NotEqual(previous, scene.SurfaceAxis);
        Assert.Equal(scene.SurfaceAxis, scene.ViewportAxis, 4);
        scene.AssertRealizationBound();
    }

    [Theory, MemberData(nameof(MissingViewports))]
    public void ActualWindowSizeChangeInvalidatesAnUnchangedMeasureConstraint(
        int depth, bool custom, bool vertical, bool blindViewer)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var scene = new Scene(depth, custom, vertical, blindViewer ? "blind" : "none");
        scene.Settle();
        Assert.True(scene.Panel.IsMeasureValid);
        scene.ResizeAxis();
        Assert.False(scene.Panel.IsMeasureValid);
        scene.Panel.Measure(scene.Constraint);
        Assert.Equal(scene.SurfaceAxis, scene.ViewportAxis, 4);
    }

    [Theory, MemberData(nameof(Surfaces))]
    public void MovingToAnotherWindowRetiresTheOldSizeDependency(int depth, bool custom, bool vertical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var scene = new Scene(depth, custom, vertical, "none");
        scene.Settle();
        var body = new Canvas { Width = 384, Height = 252 };
        var next = new Window { Width = 384, Height = 252, Content = body,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
        Display(next); Layout(next);
        try
        {
            scene.Body.Children.Remove(scene.Items); body.Children.Add(scene.Items);
            MeasureOn(scene.Panel, next, vertical);
            Assert.True(scene.Panel.IsMeasureValid);
            Assert.Same(next, Window.GetMacOSPhysicalWindow(scene.Panel));
            scene.ResizeAxis();
            Assert.True(scene.Panel.IsMeasureValid);
            if (vertical) next.Width += 40; else next.Height += 30;
            Layout(next);
            Assert.False(scene.Panel.IsMeasureValid);
            scene.Panel.Measure(vertical ? new Size(double.PositiveInfinity, next.ActualHeight)
                : new Size(next.ActualWidth, double.PositiveInfinity));
            Assert.Equal(vertical ? next.ActualWidth : next.ActualHeight, scene.ViewportAxis, 4);
        }
        finally { next.Close(); }
    }

    [Theory, MemberData(nameof(Surfaces))]
    public void ATemporaryDetachCanObserveTheSameWindowAgain(int depth, bool custom, bool vertical)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var scene = new Scene(depth, custom, vertical, "none");
        scene.Settle();
        scene.Body.Children.Remove(scene.Items);
        scene.ResizeAxis();
        scene.Body.Children.Add(scene.Items);
        scene.Settle();
        Assert.True(scene.Panel.IsMeasureValid);
        scene.ResizeAxis();
        Assert.False(scene.Panel.IsMeasureValid);
        scene.Panel.Measure(scene.Constraint);
        Assert.Equal(scene.SurfaceAxis, scene.ViewportAxis, 4);
    }

    private static void MeasureOn(VirtualizingWrapPanel panel, Window window, bool vertical)
    {
        double cross = vertical ? window.ActualHeight : window.ActualWidth;
        var constraint = vertical ? new Size(double.PositiveInfinity, cross) : new Size(cross, double.PositiveInfinity);
        int previous = -1;
        for (int i = 0; i < 70; i++)
        {
            panel.Measure(constraint);
            panel.Arrange(vertical ? new Rect(0, 0, panel.ExtentWidth, cross)
                : new Rect(0, 0, cross, panel.ExtentHeight));
            if (panel.Children.Count == previous && panel.IsMeasureValid) break;
            previous = panel.Children.Count;
        }
    }

    private static void Display(Window window) => typeof(Window).GetMethod(
        "SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [Visibility.Visible]);
    private static void Layout(Window window)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }
    private sealed class ExternalPopup : Popup
    {
        internal ModelHost? Host;
        internal ExternalPopup() { PreferExternalWindow = true; PopupAnimation = PopupAnimation.None; StaysOpen = true; }
        internal override bool CanCreateMacOSPopupHost => true;
        internal override MacOSPopupWindowHost CreateMacOSPopupHost(Window owner, PopupRoot root) => Host = new(owner, this, root);
        internal override Rect GetMacOSPopupWorkArea() => new(0, 0, 1800, 1200);
    }
    private sealed class ModelHost(Window owner, Popup popup, PopupRoot root) : MacOSPopupWindowHost(owner, popup, root)
    {
        public override void Show() { Display(this); Layout(this); }
    }
    private sealed class ProbeItems : ItemsControl
    {
        internal ProbeItems() => ItemsPanel = new ItemsPanelTemplate { PanelType = typeof(VirtualizingWrapPanel) };
        internal VirtualizingWrapPanel Panel => Assert.IsType<VirtualizingWrapPanel>(ItemsHost);
        protected override FrameworkElement GetContainerForItem(object item) => new Border { Width = 40, Height = 30 };
    }
    private sealed class Scene : IDisposable
    {
        internal readonly Window Owner;
        internal readonly Window Surface;
        internal readonly Canvas Body;
        internal readonly VirtualizingWrapPanel Panel;
        internal readonly ProbeItems Items;
        internal readonly ScrollViewer? Viewer;
        private readonly bool _vertical, _blind;
        private readonly List<ExternalPopup> _popups = [];
        internal double SurfaceAxis => _vertical ? Surface.ActualWidth : Surface.ActualHeight;
        internal double ViewportAxis => _vertical ? Panel.ViewportWidth : Panel.ViewportHeight;
        internal Scene(int depth, bool custom, bool vertical, string viewer)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); Mouse.Capture(null);
            _vertical = vertical; _blind = viewer == "blind";
            var body = new Canvas { Width = 640, Height = 480 };
            var anchor = new Border { Width = 16, Height = 16 }; body.Children.Add(anchor);
            Owner = new Window { Width = 640, Height = 480, Content = body,
                TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            Display(Owner); Layout(Owner); Window surface = Owner;
            for (int i = 0; i < depth; i++)
            {
                body = new Canvas { Width = 240, Height = 180 };
                var next = new Border { Width = 16, Height = 16 }; body.Children.Add(next);
                var popup = new ExternalPopup { PlacementTarget = anchor, Child = body };
                _popups.Add(popup); popup.IsOpen = true;
                surface = Assert.IsType<ModelHost>(popup.Host); anchor = next;
            }
            Body = body; Surface = surface;
            var items = new ProbeItems { ItemsSource = Enumerable.Range(0, 1000).ToArray() };
            Items = items;
            VirtualizingPanel.SetCacheLength(items, new VirtualizationCacheLength(0));
            VirtualizingPanel.SetCacheLengthUnit(items, VirtualizationCacheLengthUnit.Pixel);
            Panel = items.Panel; Panel.UseAncestorScrollViewer = true;
            Panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            Panel.ItemWidth = 40; Panel.ItemHeight = 30;
            if (viewer == "none") Body.Children.Add(items);
            else
            {
                Viewer = new ScrollViewer { Content = items, CanContentScroll = false,
                    Width = _blind ? 0 : 120, Height = _blind ? 0 : 90,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
                Body.Children.Add(Viewer);
                var size = _blind ? new Size(0, 0) : new Size(120, 90);
                Viewer.Measure(size); Viewer.Arrange(new Rect(0, 0, size.Width, size.Height));
            }
            Assert.Same(Owner, Window.GetWindow(Panel));
            Assert.Same(Surface, Window.GetMacOSPhysicalWindow(Panel));
        }
        internal Size Constraint => _vertical ? new Size(double.PositiveInfinity, Surface.ActualHeight)
            : new Size(Surface.ActualWidth, double.PositiveInfinity);
        internal void ResizeAxis()
        {
            if (_vertical) Body.Width = Surface.Width += 40;
            else Body.Height = Surface.Height += 30;
            Layout(Surface);
        }
        internal void ResetBlindViewer()
        {
            if (_blind && Viewer != null)
            {
                Viewer.InvalidateMeasure(); Viewer.Measure(new Size(0, 0));
                Viewer.Arrange(new Rect(0, 0, 0, 0));
            }
        }
        internal void Settle()
        {
            double cross = Viewer != null && !_blind
                ? (_vertical ? Viewer.ViewportHeight : Viewer.ViewportWidth)
                : (_vertical ? Surface.ActualHeight : Surface.ActualWidth);
            var constraint = _vertical ? new Size(double.PositiveInfinity, cross) : new Size(cross, double.PositiveInfinity);
            int previous = -1;
            for (int i = 0; i < 70; i++)
            {
                Panel.InvalidateMeasure(); Panel.Measure(constraint);
                // The ancestor mode arranges the full scroll extent. Commit
                // the new cross-axis size before considering a resize settled.
                Panel.Arrange(_vertical
                    ? new Rect(0, 0, Panel.ExtentWidth, cross)
                    : new Rect(0, 0, cross, Panel.ExtentHeight));
                if (Panel.Children.Count == previous && Panel.IsMeasureValid) break;
                previous = Panel.Children.Count;
            }
        }
        internal void AssertRealizationBound()
        {
            double cross = _vertical ? Surface.ActualHeight : Surface.ActualWidth;
            double cellAxis = _vertical ? 40 : 30, cellCross = _vertical ? 30 : 40;
            int perRow = (int)Math.Floor(cross / cellCross);
            int visible = (int)Math.Ceiling(SurfaceAxis / cellAxis) * perRow;
            Assert.InRange(Panel.Children.Count, visible, visible + 2 * perRow);
        }
        public void Dispose()
        {
            for (int i = _popups.Count - 1; i >= 0; i--) _popups[i].IsOpen = false;
            Owner.Close(); Keyboard.ClearFocus(); Mouse.Capture(null);
        }
    }
}
