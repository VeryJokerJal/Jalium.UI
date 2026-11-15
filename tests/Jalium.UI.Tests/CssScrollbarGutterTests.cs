using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssScrollbarGutterTests
{
    [Fact]
    public void AutoReservesOnlyShownClassicBarsAndReleasesBothGutters()
    {
        var content = new Border { Width = 94, Height = 90 };
        var viewer = Viewer(content);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        CssBorderRadiusTests.Layout(viewer, 100, 80);

        // Vertical overflow admits the right gutter, which then makes this
        // otherwise fitting 94-DIP-wide content overflow horizontally.
        Assert.Equal(new Size(88, 68), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
        Assert.Equal(new Vector(88, 0),
            VisualTreeHelper.GetOffset(Assert.IsType<ScrollBar>(viewer.GetVisualChild(1))));
        Assert.Equal(new Vector(0, 68),
            VisualTreeHelper.GetOffset(Assert.IsType<ScrollBar>(viewer.GetVisualChild(2))));

        content.Width = 80;
        content.Height = 40;
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        Assert.Equal(new Size(100, 80), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
    }

    [Fact]
    public void AutoHorizontalOverflowCanAdmitVerticalGutter()
    {
        var viewer = Viewer(new Border { Width = 110, Height = 74 });
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        CssBorderRadiusTests.Layout(viewer, 100, 80);

        Assert.Equal(new Size(88, 68), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
    }

    [Fact]
    public void AutoWithExplicitVisibleAndRtlUsesInlineEndEvenWithoutOverflow()
    {
        var viewer = Viewer(new Border { Width = 40, Height = 30 });
        viewer.FlowDirection = FlowDirection.RightToLeft;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;

        CssBorderRadiusTests.Layout(viewer, 100, 80);

        Assert.Equal(new Size(88, 80), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(new Vector(0, 0),
            VisualTreeHelper.GetOffset(Assert.IsType<ScrollBar>(viewer.GetVisualChild(1))));

        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        Assert.Equal(new Size(100, 80), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
    }

    [Fact]
    public void AutoRemeasuresScrollInfoWithBothAdmittedGutters()
    {
        var content = new ScrollInfoProbe
        {
            ExtentWidthForViewport = _ => 94,
            FixedExtentHeight = 90,
        };
        var viewer = Viewer(content);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        CssBorderRadiusTests.Layout(viewer, 100, 80);

        Assert.Equal(new Size(88, 68), content.LastConstraint);
        Assert.Equal(new Size(88, 68), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
    }

    [Fact]
    public void AutoClassicGutterClipsContentPaintAndPointerHits()
    {
        var content = new Border { Width = 150, Height = 120, Background = Brushes.Lime };
        var viewer = Viewer(content);
        viewer.Background = Brushes.Blue;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;

        CssBorderRadiusTests.Layout(viewer, 100, 80);

        var pixels = CssBorderRadiusTests.Pixels(viewer, 100, 80);
        Assert.Equal((byte)255, pixels[(20 * 100 + 20) * 4 + 1]);
        Assert.NotEqual((byte)255, pixels[(20 * 100 + 93) * 4 + 1]);
        Assert.Same(content, viewer.HitTest(new Point(20, 20))?.VisualHit);
        Assert.NotSame(content, viewer.HitTest(new Point(93, 20))?.VisualHit);
    }

    [Fact]
    public void StableReservesClassicVerticalGutterEvenWithoutOverflow()
    {
        var viewer = Viewer(new Border { Width = 80, Height = 40 });
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Css.SetStyle(viewer, "scrollbar-gutter:stable");

        CssBorderRadiusTests.Layout(viewer, 100, 80);

        Assert.Equal(new Size(88, 80), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);

        Css.SetStyle(viewer, "scrollbar-gutter:auto");
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        Assert.Equal(new Size(100, 80), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
    }

    [Fact]
    public void EmptyScrollContainerStillReservesStableGutters()
    {
        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsOverlayScrollBarEnabled = false,
            IsScrollBarAutoHideEnabled = false,
        };
        Css.SetStyle(viewer, "scrollbar-gutter:stable both-edges");

        viewer.Measure(new Size(200, 140));
        viewer.Arrange(new Rect(0, 0, 200, 140));

        Assert.Equal(new Size(24, 12), viewer.DesiredSize);
        Assert.Equal(new Size(176, 128), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(0)).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
    }

    [Fact]
    public void BothEdgesAndHorizontalBarUseTheRemainingContentBox()
    {
        var viewer = Viewer(new Border { Width = 300, Height = 240 });
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        Css.SetStyle(viewer, "scrollbar-gutter: stable both-edges");

        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Equal(new Size(176, 128), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(new Vector(12, 0), VisualTreeHelper.GetOffset((Visual)viewer.Content!));
        var vertical = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        var horizontal = Assert.IsType<ScrollBar>(viewer.GetVisualChild(2));
        Assert.Equal(new Vector(188, 0), VisualTreeHelper.GetOffset(vertical));
        Assert.Equal(new Size(12, 128), vertical.RenderSize);
        Assert.Equal(new Vector(12, 128), VisualTreeHelper.GetOffset(horizontal));
        Assert.Equal(new Size(176, 12), horizontal.RenderSize);

        Css.SetStyle(viewer, "scrollbar-gutter: stable; scrollbar-width: thin");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(new Size(190, 130), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(new Size(10, 130), vertical.RenderSize);
        Assert.Equal(new Size(190, 10), horizontal.RenderSize);
    }

    [Fact]
    public void RtlPlacesTheClassicBarAtInlineEndAndMirrorsTheOtherEdge()
    {
        var viewer = Viewer(new Border { Width = 300, Height = 240 });
        viewer.FlowDirection = FlowDirection.RightToLeft;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        Css.SetStyle(viewer, "scrollbar-gutter:stable both-edges; scrollbar-width:thin");

        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Equal(180, viewer.ViewportWidth);
        Assert.Equal(new Vector(0, 0),
            VisualTreeHelper.GetOffset(Assert.IsType<ScrollBar>(viewer.GetVisualChild(1))));
        Assert.Equal(new Vector(10, 0), VisualTreeHelper.GetOffset((Visual)viewer.Content!));
    }

    [Fact]
    public void BorderAndPaddingKeepClassicBarsAdjacentToTheInnerBorder()
    {
        var content = new Border { Width = 300, Height = 240, Background = Brushes.Lime };
        var viewer = Viewer(content);
        viewer.Background = Brushes.Blue;
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        Css.SetStyle(viewer,
            "border:8px solid red; padding:4px 6px; scrollbar-gutter:stable both-edges");

        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Equal(new Size(148, 104), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(new Vector(26, 12), VisualTreeHelper.GetOffset(content));
        var vertical = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
        var horizontal = Assert.IsType<ScrollBar>(viewer.GetVisualChild(2));
        Assert.Equal(new Vector(180, 8), VisualTreeHelper.GetOffset(vertical));
        Assert.Equal(new Size(12, 112), vertical.RenderSize);
        Assert.Equal(new Vector(20, 120), VisualTreeHelper.GetOffset(horizontal));
        Assert.Equal(new Size(160, 12), horizontal.RenderSize);

        var pixels = CssBorderRadiusTests.Pixels(viewer, 200, 140);
        Assert.Equal((byte)255, pixels[(40 * 200 + 10) * 4]);
        Assert.Equal((byte)255, pixels[(40 * 200 + 30) * 4 + 1]);
        Assert.NotSame(content, viewer.HitTest(new Point(185, 20))?.VisualHit);
    }

    [Fact]
    public void OverlayBarsAndNoneWidthDoNotReserveSpace()
    {
        var viewer = Viewer(new Border { Width = 300, Height = 240 });
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.IsOverlayScrollBarEnabled = true;
        Css.SetStyle(viewer, "scrollbar-gutter:stable both-edges");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(new Size(200, 140), new Size(viewer.ViewportWidth, viewer.ViewportHeight));

        viewer.IsOverlayScrollBarEnabled = false;
        Css.SetStyle(viewer, "scrollbar-gutter:stable both-edges; scrollbar-width:none");
        CssBorderRadiusTests.Layout(viewer, 200, 140);
        Assert.Equal(new Size(200, 140), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.True(viewer.CanScrollVertically);
    }

    [Fact]
    public void AutoHorizontalBarReflowsAndReleasesItsBlockEndGutter()
    {
        var content = new Border { Width = 90, Height = 100 };
        var viewer = Viewer(content);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Css.SetStyle(viewer, "scrollbar-gutter:stable");

        CssBorderRadiusTests.Layout(viewer, 100, 80);

        Assert.Equal(new Size(88, 68), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);

        content.Width = 80;
        content.Height = 40;
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        Assert.Equal(new Size(88, 80), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(1)).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
    }

    [Fact]
    public void TemplateHostedViewerUsesOwnerUnlessItHasItsOwnGutterDeclaration()
    {
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() =>
        {
            var viewer = Viewer(new Border { Width = 80, Height = 40 });
            viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            return viewer;
        });
        var owner = new ListBox { Template = template };
        Css.SetStyle(owner, "scrollbar-gutter:stable");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        var hosted = Assert.IsType<ScrollViewer>(owner.GetVisualChild(0));
        Assert.Equal(188, hosted.ViewportWidth);

        Css.SetStyle(hosted, "scrollbar-gutter:auto");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(200, hosted.ViewportWidth);

        Css.SetStyle(hosted, "scrollbar-gutter:unset");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(200, hosted.ViewportWidth);

        Css.SetStyle(hosted, "scrollbar-gutter:inherit");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(188, hosted.ViewportWidth);

        Css.SetStyle(hosted, string.Empty);
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(188, hosted.ViewportWidth);

        Css.SetStyle(owner, "scrollbar-gutter:stable both-edges");
        CssBorderRadiusTests.Layout(owner, 200, 140);
        Assert.Equal(176, hosted.ViewportWidth);
    }

    [Fact]
    public void OrdinaryChildViewerDoesNotInheritGutter()
    {
        var parent = new Grid();
        var viewer = Viewer(new Border { Width = 80, Height = 40 });
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        parent.Children.Add(viewer);
        Css.SetStyle(parent, "scrollbar-gutter:stable");
        CssBorderRadiusTests.Layout(parent, 200, 140);
        Assert.Equal(200, viewer.ViewportWidth);
    }

    [Fact]
    public void ScrollInfoContentReceivesTheReservedFiniteViewport()
    {
        var content = new ScrollInfoProbe();
        var viewer = Viewer(content);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        Css.SetStyle(viewer, "scrollbar-gutter:stable both-edges");

        CssBorderRadiusTests.Layout(viewer, 200, 140);

        Assert.Same(viewer, content.ScrollOwner);
        Assert.Equal(new Size(176, 128), content.LastConstraint);
        Assert.Equal(new Size(176, 128), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
    }

    [Fact]
    public void HeightDependentScrollInfoKeepsAnAdmittedHorizontalGutter()
    {
        var content = new ScrollInfoProbe
        {
            ExtentWidthForViewport = viewport => viewport.Height >= 100 ? 100 : 80,
            FixedExtentHeight = 40,
        };
        var viewer = Viewer(content);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Css.SetStyle(viewer, "scrollbar-gutter:stable");

        CssBorderRadiusTests.Layout(viewer, 100, 100);
        Assert.Equal(new Size(88, 88), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
        Assert.Equal(new Size(88, 88), content.LastConstraint);

        viewer.InvalidateMeasure();
        CssBorderRadiusTests.Layout(viewer, 100, 100);
        Assert.Equal(new Size(88, 88), new Size(viewer.ViewportWidth, viewer.ViewportHeight));
        Assert.Equal(Visibility.Visible, Assert.IsType<ScrollBar>(viewer.GetVisualChild(2)).Visibility);
    }

    [Fact]
    public void ReservedGutterPaintsTheViewerBackgroundAndRejectsContentHits()
    {
        var content = new Border { Width = 150, Height = 120, Background = Brushes.Lime };
        var viewer = Viewer(content);
        viewer.Background = Brushes.Blue;
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        Css.SetStyle(viewer, "scrollbar-gutter:stable");
        CssBorderRadiusTests.Layout(viewer, 100, 80);

        Assert.True(content.RenderSize.Width > viewer.ViewportWidth);
        var pixels = CssBorderRadiusTests.Pixels(viewer, 100, 80);
        Assert.Equal((byte)255, pixels[(20 * 100 + 20) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(20 * 100 + 93) * 4]);
        Assert.Equal((byte)0, pixels[(20 * 100 + 93) * 4 + 1]);
        Assert.Same(content, viewer.HitTest(new Point(20, 20))?.VisualHit);
        Assert.Same(viewer, viewer.HitTest(new Point(93, 20))?.VisualHit);
    }

    [Fact]
    public void GutterExtendsThePaddingAreaForBackgroundClipping()
    {
        var viewer = Viewer(new Border { Width = 150, Height = 120 });
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        var root = new Border { Background = Brushes.Blue, Child = viewer };
        Css.SetStyle(viewer,
            "background-color:lime; background-clip:content-box; scrollbar-gutter:stable");
        CssBorderRadiusTests.Layout(root, 100, 80);

        var pixels = CssBorderRadiusTests.Pixels(root, 100, 80);
        Assert.Equal((byte)255, pixels[(20 * 100 + 20) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(20 * 100 + 93) * 4]);

        Css.SetStyle(viewer,
            "background-color:lime; background-clip:padding-box; scrollbar-gutter:stable");
        CssBorderRadiusTests.Layout(root, 100, 80);
        pixels = CssBorderRadiusTests.Pixels(root, 100, 80);
        Assert.Equal((byte)255, pixels[(20 * 100 + 93) * 4 + 1]);
    }

    [Fact]
    public void ParserAcceptsBothOrdersAndRejectsDuplicateKeywords()
    {
        Assert.True(Css.Supports("scrollbar-gutter", "stable both-edges"));
        Assert.True(Css.Supports("scrollbar-gutter", "STABLE"));
        Assert.True(Css.Supports("scrollbar-gutter", "both-edges stable"));
        Assert.False(Css.Supports("scrollbar-gutter", "stable stable"));
        Assert.False(Css.Supports("scrollbar-gutter", "auto both-edges"));

        var viewer = Viewer(new Border { Width = 80, Height = 40 });
        Css.SetStyle(viewer, "scrollbar-gutter:stable; scrollbar-gutter:stable stable");
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        Assert.Equal(88, viewer.ViewportWidth);

        Css.SetStyle(viewer, "scrollbar-gutter:both-edges stable");
        CssBorderRadiusTests.Layout(viewer, 100, 80);
        Assert.Equal(76, viewer.ViewportWidth);
    }

    private sealed class ScrollInfoProbe : FrameworkElement, IScrollInfo
    {
        public Size LastConstraint { get; private set; }
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public Func<Size, double>? ExtentWidthForViewport { get; set; }
        public double FixedExtentHeight { get; set; } = 240;
        public double ExtentWidth => ExtentWidthForViewport?.Invoke(LastConstraint) ?? 300;
        public double ExtentHeight => FixedExtentHeight;
        public double ViewportWidth => LastConstraint.Width;
        public double ViewportHeight => LastConstraint.Height;
        public double HorizontalOffset { get; private set; }
        public double VerticalOffset { get; private set; }
        public ScrollViewer? ScrollOwner { get; set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            LastConstraint = availableSize;
            return availableSize;
        }
        public void LineUp() { }
        public void LineDown() { }
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() { }
        public void PageDown() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelUp() { }
        public void MouseWheelDown() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void SetHorizontalOffset(double offset) => HorizontalOffset = offset;
        public void SetVerticalOffset(double offset) => VerticalOffset = offset;
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }

    private static ScrollViewer Viewer(FrameworkElement content) => new()
    {
        Content = content,
        IsScrollBarAutoHideEnabled = false,
        IsOverlayScrollBarEnabled = false,
    };
}
