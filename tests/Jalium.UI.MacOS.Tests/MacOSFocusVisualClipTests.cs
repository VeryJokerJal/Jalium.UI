using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFocusVisualClipTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void FocusRingUsesContentViewportInsteadOfClassicScrollBarGutters(bool vertical, bool horizontal, bool rtl)
    {
        using var fixture = new ClipFixture(vertical, horizontal, rtl);
        var expected = fixture.Viewport;
        Assert.True(expected.Width < fixture.Viewer.RenderSize.Width || expected.Height < fixture.Viewer.RenderSize.Height);
        AssertClip(fixture.Ring, fixture.Viewer, expected);
        fixture.Viewer.ScrollToHorizontalOffset(65);
        fixture.Viewer.ScrollToVerticalOffset(45);
        fixture.Layout();
        AssertClip(fixture.Ring, fixture.Viewer, expected);
    }

    [Fact]
    public void OverlayScrollBarsKeepFullViewport()
    {
        using var fixture = new ClipFixture(true, true);
        fixture.Viewer.IsOverlayScrollBarEnabled = true;
        fixture.Layout();
        AssertClip(fixture.Ring, fixture.Viewer, new Rect(fixture.Viewer.RenderSize));
    }

    [Fact]
    public void StableBothEdgesClipsBothReservedVerticalGutters()
    {
        using var fixture = new ClipFixture(true, false);
        fixture.Viewer.SetValue(ScrollViewer.CssScrollBarGutterProperty, CssScrollBarGutterMode.StableBothEdges);
        fixture.Layout();
        var expected = fixture.Viewport;
        Assert.True(expected.Left > 0 && expected.Right < fixture.Viewer.RenderSize.Width);
        AssertClip(fixture.Ring, fixture.Viewer, expected);
    }

    [Fact]
    public void ScrollBarOwnFocusRingIsNotClippedToContentViewport()
    {
        using var fixture = new ClipFixture(true, false);
        var bar = (ScrollBar)typeof(ScrollViewer).GetField("_verticalScrollBar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Viewer)!;
        var ring = new FocusVisualAdorner(bar, new Style(typeof(Control)));
        fixture.Window.AdornerLayer!.Add(ring);
        fixture.Layout();
        AssertClip(ring, fixture.Viewer, new Rect(fixture.Viewer.RenderSize));
        var visible = Rect.Intersect(new Rect(ring.RenderSize), ring.GetLayoutClip()!.Bounds);
        Assert.Equal(new Rect(ring.RenderSize), visible);
    }

    [Fact]
    public void AncestorClippingDoesNotRemoveIntentionalOutwardRingBorder()
    {
        using var fixture = new ClipFixture(true, true);
        fixture.Target.ClipToBounds = true;
        AssertClip(fixture.Ring, fixture.Viewer, fixture.Viewport);
        Assert.True(fixture.Ring.GetLayoutClip()!.Bounds.Left < -2);
        Assert.True(fixture.Ring.GetLayoutClip()!.Bounds.Top < -2);
    }

    private static void AssertClip(FocusVisualAdorner ring, ScrollViewer viewer, Rect viewport)
    {
        var actual = Assert.IsType<RectangleGeometry>(ring.GetLayoutClip()).Bounds;
        var expected = new Rect(viewer.TranslatePoint(viewport.TopLeft, ring), viewport.Size);
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    private sealed class ClipFixture : IDisposable
    {
        internal Button Target { get; } = new() { Width = 100, Height = 40, Content = "中文焦点🙂" };
        internal ScrollViewer Viewer { get; }
        internal DisplayedTestWindow Window { get; }
        internal FocusVisualAdorner Ring { get; }
        internal Rect Viewport => (Rect)typeof(ScrollViewer).GetMethod("GetContentViewportRect", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(Size)], null)!.Invoke(Viewer, [Viewer.RenderSize])!;
        internal ClipFixture(bool vertical, bool horizontal, bool rtl = false)
        {
            var content = new Canvas { Width = 500, Height = 400 };
            Canvas.SetLeft(Target, 90);
            Canvas.SetTop(Target, 60);
            content.Children.Add(Target);
            Viewer = new ScrollViewer
            {
                Content = content, Width = 200, Height = 120, IsOverlayScrollBarEnabled = false,
                VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled,
                FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                IsScrollInertiaEnabled = false,
            };
            Window = new DisplayedTestWindow { Content = Viewer, Width = 320, Height = 240, TitleBarStyle = WindowTitleBarStyle.Native };
            Layout();
            Ring = new FocusVisualAdorner(Target, new Style(typeof(Control)));
            Window.AdornerLayer!.Add(Ring);
            Layout();
        }
        internal void Layout()
        {
            Window.Measure(new Size(320, 240));
            Window.Arrange(new Rect(0, 0, 320, 240));
            Window.UpdateLayout();
        }
        public void Dispose() => Window.Close();
    }
}
