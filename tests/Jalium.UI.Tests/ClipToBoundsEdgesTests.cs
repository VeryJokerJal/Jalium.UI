using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using CompilerOptions = Jalium.UI.Gpu.CompilerOptions;
using UICompiler = Jalium.UI.Gpu.UICompiler;

namespace Jalium.UI.Tests;

public class ClipToBoundsEdgesTests
{
    [Fact]
    public void ClipToBoundsEdges_DefaultsToAll_AndPreservesLegacyClip()
    {
        var element = new ProbeElement
        {
            ClipToBounds = true
        };

        element.Measure(new Size(100, 50));
        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Equal(ClipEdges.All, element.ClipToBoundsEdges);
        var clip = Assert.IsType<RectangleGeometry>(element.ExposedLayoutClip());
        Assert.Equal(new Rect(0, 0, 100, 50), clip.Rect);
        Assert.Equal(new Rect(0, 0, 100, 50), LayoutInformation.GetLayoutClip(element));
    }

    [Fact]
    public void ClipToBoundsEdges_ClipsOnlySelectedEdges()
    {
        var element = new ProbeElement
        {
            ClipToBounds = true,
            ClipToBoundsEdges = ClipEdges.Left | ClipEdges.Top
        };

        element.Measure(new Size(100, 50));
        element.Arrange(new Rect(0, 0, 100, 50));

        var clip = Assert.IsType<RectangleGeometry>(element.ExposedLayoutClip());

        Assert.Equal(new Rect(0, 0, 100, 50), clip.BoundsClipRect);
        Assert.Equal(ClipEdges.Left | ClipEdges.Top, clip.BoundsClipEdges);
        Assert.True(clip.Rect.Right > 100_000_000);
        Assert.True(clip.Rect.Bottom > 100_000_000);
        Assert.False(clip.FillContains(new Point(-1, 10)));
        Assert.False(clip.FillContains(new Point(10, -1)));
        Assert.True(clip.FillContains(new Point(101, 10)));
        Assert.True(clip.FillContains(new Point(10, 51)));
    }

    [Theory]
    [InlineData(ClipEdges.Left, -1, 25, 101, 25)]
    [InlineData(ClipEdges.Top, 50, -1, 50, 51)]
    [InlineData(ClipEdges.Right, 101, 25, -1, 25)]
    [InlineData(ClipEdges.Bottom, 50, 51, 50, -1)]
    public void ClipToBoundsEdges_EachEdgeOperatesIndependently(
        ClipEdges edge,
        double clippedX,
        double clippedY,
        double openX,
        double openY)
    {
        var element = new ProbeElement
        {
            ClipToBounds = true,
            ClipToBoundsEdges = edge
        };

        element.Measure(new Size(100, 50));
        element.Arrange(new Rect(0, 0, 100, 50));

        var clip = Assert.IsType<RectangleGeometry>(element.ExposedLayoutClip());

        Assert.Equal(edge, clip.BoundsClipEdges);
        Assert.False(clip.FillContains(new Point(clippedX, clippedY)));
        Assert.True(clip.FillContains(new Point(openX, openY)));
    }

    [Fact]
    public void ClipToBoundsEdges_None_DisablesOnlyTheImplicitBoundsClip()
    {
        var element = new ProbeElement
        {
            ClipToBounds = true,
            ClipToBoundsEdges = ClipEdges.None
        };

        element.Measure(new Size(100, 50));
        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Null(element.ExposedLayoutClip());

        var explicitClip = new RectangleGeometry(new Rect(-20, -10, 140, 70));
        element.Clip = explicitClip;

        Assert.Same(explicitClip, element.ExposedLayoutClip());
    }

    [Fact]
    public void ClipToBoundsEdges_RejectsUndefinedFlags()
    {
        var element = new ProbeElement();
        var invalid = (ClipEdges)(1 << 8);

        Assert.False(UIElement.ClipToBoundsEdgesProperty.IsValidValue(invalid));
        Assert.Throws<ArgumentException>(() => element.ClipToBoundsEdges = invalid);
    }

    [Fact]
    public void HitTesting_AllowsOverflowThroughAnUnselectedEdge()
    {
        var child = new Border
        {
            Width = 20,
            Height = 20,
            Background = new SolidColorBrush(Color.Black)
        };
        Canvas.SetLeft(child, 50);

        var canvas = new Canvas
        {
            Width = 40,
            Height = 30,
            ClipToBounds = true,
            ClipToBoundsEdges = ClipEdges.Left | ClipEdges.Top | ClipEdges.Bottom
        };
        canvas.Children.Add(child);
        canvas.Measure(new Size(40, 30));
        canvas.Arrange(new Rect(0, 0, 40, 30));

        Assert.Same(child, canvas.HitTest(new Point(55, 10))?.VisualHit);

        canvas.ClipToBoundsEdges = ClipEdges.All;
        Assert.Null(canvas.HitTest(new Point(55, 10)));
    }

    [Fact]
    public void Border_MasksCornersThatDoNotHaveBothAdjacentClipEdges()
    {
        var border = new ProbeBorder
        {
            ClipToBounds = true,
            ClipToBoundsEdges = ClipEdges.Left | ClipEdges.Top,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(8)
        };

        border.Measure(new Size(120, 40));
        border.Arrange(new Rect(0, 0, 120, 40));

        var clip = Assert.IsType<RectangleGeometry>(border.ExposedLayoutClip());

        Assert.Equal(new Rect(3, 3, 114, 34), clip.BoundsClipRect);
        Assert.Equal(ClipEdges.Left | ClipEdges.Top, clip.BoundsClipEdges);
        Assert.True(clip.Rect.Right > 100_000_000);
        Assert.True(clip.Rect.Bottom > 100_000_000);
        Assert.Equal(new CornerRadius(5, 0, 0, 0), clip.CornerRadius);
    }

    [Fact]
    public void ScrollViewer_UsesSelectedClipEdges()
    {
        var viewer = new ProbeScrollViewer
        {
            ClipToBounds = true,
            ClipToBoundsEdges = ClipEdges.Top | ClipEdges.Bottom
        };

        viewer.Measure(new Size(100, 50));
        viewer.Arrange(new Rect(0, 0, 100, 50));

        var clip = Assert.IsType<RectangleGeometry>(viewer.ExposedLayoutClip());

        Assert.Equal(new Rect(0, 0, 100, 50), clip.BoundsClipRect);
        Assert.Equal(ClipEdges.Top | ClipEdges.Bottom, clip.BoundsClipEdges);
        Assert.True(clip.Rect.Left < -100_000_000);
        Assert.True(clip.Rect.Right > 100_000_000);
    }

    [Fact]
    public void Renderer_ResolvesOpenEdgesAgainstTheVisibleLimit()
    {
        var resolved = RenderTargetDrawingContext.ResolveBoundsClip(
            new Rect(30, 20, 100, 50),
            ClipEdges.Right | ClipEdges.Bottom,
            new Rect(0, 0, 1920, 1080));

        Assert.Equal(new Rect(0, 0, 130, 70), resolved);
    }

    [Fact]
    public void XamlReader_ParsesClipEdgeFlagCombinations()
    {
        const string xaml = """
<Border ClipToBounds="True"
        ClipToBoundsEdges="Left,Bottom" />
""";

        var border = Assert.IsType<Border>(XamlReader.Parse(xaml));

        Assert.True(border.ClipToBounds);
        Assert.Equal(ClipEdges.Left | ClipEdges.Bottom, border.ClipToBoundsEdges);
    }

    [Fact]
    public void UiCompiler_AppliesClipEdgeFlagCombinations()
    {
        const string xaml = """
<Grid Width="40"
      Height="30"
      ClipToBounds="True"
      ClipToBoundsEdges="Left,Top,Bottom">
  <Border Width="80"
          Height="20"
          Click="OnClick" />
</Grid>
""";

        var compiler = new UICompiler
        {
            Options = new CompilerOptions
            {
                ViewportWidth = 100,
                ViewportHeight = 100
            }
        };

        var bundle = compiler.CompileSource(xaml);
        var region = Assert.Single(bundle.InteractiveRegions);

        Assert.Equal(0, region.ClipBounds.X);
        Assert.Equal(0, region.ClipBounds.Y);
        Assert.Equal(100, region.ClipBounds.Width);
        Assert.Equal(100, region.ClipBounds.Height);
    }

    private sealed class ProbeElement : FrameworkElement
    {
        public Geometry? ExposedLayoutClip() => GetLayoutClip();
    }

    private sealed class ProbeBorder : Border
    {
        public Geometry? ExposedLayoutClip() => GetLayoutClip();
    }

    private sealed class ProbeScrollViewer : ScrollViewer
    {
        public Geometry? ExposedLayoutClip() => GetLayoutClip();
    }
}
