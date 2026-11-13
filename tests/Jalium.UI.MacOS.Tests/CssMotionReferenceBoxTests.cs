using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed partial class CssMotionReferenceBoxTests
{
    private const string PathStyle = "offset-path:path('M200 100 L500 100');offset-distance:0px;";

    public static IEnumerable<object[]> Anchors()
    {
        foreach (var box in new[] { "content-box", "fill-box", "border-box", "stroke-box", "view-box" })
        foreach (var anchor in new[] { "auto", "percent", "zero", "end", "calc", "origin-percent", "origin-zero", "top", "reordered", "edge-offset", "three-value" })
        foreach (int rotation in new[] { 0, 90, -90 }) yield return [box, anchor, rotation];
    }

    [Theory, MemberData(nameof(Anchors))]
    public void AnchorPositionsUseTheLiteralReferenceRectangle(string referenceBox, string anchor, int rotation)
    {
        var (root, box) = Scene();
        Css.SetStyle(box, Style(referenceBox, anchor, rotation)); Layout(root);
        var expectedAnchor = LiteralAnchor(referenceBox, anchor);
        var pose = CssMotionPath.ResolveTransform(box);
        Equal(new Point(200, 100), new Point(pose.X, pose.Y));
        foreach (var point in new[] { new Point(0, 0), new Point(8, 8), new Point(150, 90) })
            Equal(OnPath(point, expectedAnchor, rotation), pose.Transform.Value.Transform(point));
        Assert.Same(DependencyProperty.UnsetValue, box.ReadLocalValue(UIElement.RenderTransformOriginProperty));
    }

    public static IEnumerable<object[]> Compositions()
    {
        foreach (var ordinary in new[] { "none", "css-scale", "native-scale", "css-matrix", "css-3d", "nested-native" })
        foreach (var anchor in new[] { "auto", "percent" })
        foreach (int angle in new[] { 0, 90, -90 }) yield return [ordinary, anchor, angle];
    }

    [Theory, MemberData(nameof(Compositions))]
    public void MotionAndOrdinaryTransformsPreserveLiteralCoordinatesAndOriginalHit(string ordinary, string anchor, int angle)
    {
        var (root, box) = Scene(); Canvas.SetLeft(box, 40); Canvas.SetTop(box, 60);
        var style = Style("content-box", anchor, angle);
        if (ordinary is "css-scale" or "nested-native") style += "transform:scale(.75);";
        if (ordinary == "css-matrix") style += "transform:matrix(.75,.1,.2,.8,12,8);";
        if (ordinary == "css-3d") style += "transform:scale3d(.75,.75,1);";
        if (ordinary == "native-scale") box.RenderTransform = new ScaleTransform(.75, .75);
        if (ordinary == "nested-native")
        {
            root.Children.Remove(box);
            var parent = new Canvas { Width = 600, Height = 500, RenderTransform = new ScaleTransform(.5, .5) };
            parent.Children.Add(box); root.Children.Add(parent); Canvas.SetLeft(parent, 20); Canvas.SetTop(parent, 30);
        }
        Css.SetStyle(box, style); Layout(root);
        foreach (var point in new[] { new Point(8, 8), new Point(80, 50), new Point(150, 90) })
        {
            var q = ordinary switch
            {
                "native-scale" => new Point(point.X * .75, point.Y * .75),
                "css-scale" or "css-3d" or "nested-native" => new Point(70 + (point.X - 70) * .75, 44 + (point.Y - 44) * .75),
                "css-matrix" => new Point(70 + (point.X - 70) * .75 + (point.Y - 44) * .2 + 12,
                    44 + (point.X - 70) * .1 + (point.Y - 44) * .8 + 8),
                _ => point,
            };
            var expected = OnPath(q, new Point(70, 44), angle); expected = new Point(expected.X + 40, expected.Y + 60);
            if (ordinary == "nested-native") expected = new Point(20 + expected.X * .5, 30 + expected.Y * .5);
            Equal(expected, box.GetRenderMatrix().Transform(point));
            Equal(expected, box.TransformToVisual(root)!.Transform(point));
            Assert.Same(box, root.InputHitTest(expected));
        }
    }

    [Theory]
    [InlineData("auto")] [InlineData("percent")]
    public void NativeInsetChangesRefreshTheCachedMotionWithoutASeparateLayout(string anchor)
    {
        var (root, box) = Scene(); Css.SetStyle(box, Style("content-box", anchor, 0)); Layout(root);
        RenderPoint(box, new Point(70, 44));
        box.Padding = new Thickness(32, 8, 28, 16); RenderPoint(box, new Point(80, 44));
        box.BorderThickness = new Thickness(10, 4, 6, 8); RenderPoint(box, new Point(84, 44));
        box.Padding = new Thickness(12, 8, 28, 16); box.BorderThickness = new Thickness(2, 4, 6, 8);
        RenderPoint(box, new Point(70, 44));
    }

    [Theory]
    [InlineData("auto")] [InlineData("percent")]
    public void SizeAndReferenceBoxChangesMoveAndRestoreTheAnchor(string anchor)
    {
        var (root, box) = Scene(); Css.SetStyle(box, Style("content-box", anchor, 0)); Layout(root);
        RenderPoint(box, new Point(70, 44)); box.Width = 220; box.Height = 140; Layout(root);
        RenderPoint(box, new Point(100, 64));
        Css.SetStyle(box, Style("border-box", anchor, 0)); Layout(root); RenderPoint(box, new Point(110, 70));
        box.Width = 160; box.Height = 100; Css.SetStyle(box, Style("content-box", anchor, 0)); Layout(root);
        RenderPoint(box, new Point(70, 44));
    }

    [Theory]
    [InlineData("auto")] [InlineData("percent")]
    public void PercentagePaddingTracksContainingWidthEvenWhenTheElementKeepsItsSize(string anchor)
    {
        var (root, box) = Scene(); box.Width = 200; box.Height = 160;
        root.HorizontalAlignment = HorizontalAlignment.Left; root.VerticalAlignment = VerticalAlignment.Top;
        box.ClearValue(Border.PaddingProperty);
        Css.SetStyle(root, "display:block;width:300px;height:240px;");
        Css.SetStyle(box, Style("content-box", anchor, 0) + "padding:5% 10% 15% 20%;"); Layout(root);
        Equal(new Point(87, 37), CssMotionPath.ResolveTransform(box).Transform.Value.Transform(default(Point)));
        Css.SetStyle(root, "display:block;width:400px;height:240px;"); Layout(root);
        Equal(new Point(82, 42), CssMotionPath.ResolveTransform(box).Transform.Value.Transform(default(Point)));
        Equal(new Point(82, 42), box.GetRenderMatrix().Transform(default(Point)));
    }

    [Theory]
    [InlineData("local")] [InlineData("current")] [InlineData("binding")]
    public void AnAuthoredZeroOriginChangesTheAutomaticAnchorEvenWhenThePointValueIsUnchanged(string source)
    {
        var (root, box) = Scene(); Css.SetStyle(box, Style("border-box", "auto", 0)); Layout(root);
        RenderPoint(box, new Point(80, 50)); Assert.Equal(default, box.RenderTransformOrigin);
        var binding = new Border { Tag = default(Point) };
        if (source == "local") box.RenderTransformOrigin = default;
        else if (source == "current") box.SetCurrentValue(UIElement.RenderTransformOriginProperty, default(Point));
        else box.SetBinding(UIElement.RenderTransformOriginProperty, new Binding("Tag") { Source = binding });
        RenderPoint(box, default);
        if (source == "binding")
        {
            binding.Tag = new Point(.2, .3); RenderPoint(box, new Point(32, 30));
            Assert.NotNull(box.GetBindingExpression(UIElement.RenderTransformOriginProperty));
        }
        box.ClearValue(UIElement.RenderTransformOriginProperty); RenderPoint(box, new Point(80, 50));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeOriginAnimationReportsTheFirstSourceChangeAndKeepsHoldEndSemantics(bool hold)
    {
        var (root, box) = Scene(); Css.SetStyle(box, Style("border-box", "auto", 0)); Layout(root);
        RenderPoint(box, new Point(80, 50));
        Assert.True(box.SetAnimatedValue(UIElement.RenderTransformOriginProperty, default(Point), hold)); RenderPoint(box, default);
        Assert.False(box.SetAnimatedValue(UIElement.RenderTransformOriginProperty, default(Point), hold));
        box.ClearAnimatedValue(UIElement.RenderTransformOriginProperty);
        RenderPoint(box, hold ? default : new Point(80, 50));
        box.ClearValue(UIElement.RenderTransformOriginProperty); RenderPoint(box, new Point(80, 50));
    }

    [Fact]
    public void CssOriginAnimationWithAnUnchangedPointRefreshesAndRestoresTheAutomaticAnchor()
    {
        var (root, box) = Scene(); Css.SetStyle(box, Style("border-box", "auto", 0)); Layout(root);
        RenderPoint(box, new Point(80, 50));
        Assert.True(box.SetCssAnimatedValue(UIElement.RenderTransformOriginProperty, default(Point))); RenderPoint(box, default);
        Assert.False(box.SetCssAnimatedValue(UIElement.RenderTransformOriginProperty, default(Point)));
        box.ClearCssAnimatedValue(UIElement.RenderTransformOriginProperty); RenderPoint(box, new Point(80, 50));
    }

    [Fact]
    public void PublicPausedAnchorKeyframesRetainPercentagesAndOffsetsDuringResize()
    {
        var (root, box) = Scene();
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("@keyframes anchor-slide { from { offset-anchor:0% 0%; } to { offset-anchor:100% 100%; } }") });
        Css.SetStyle(box, Style("content-box", "auto", 0) + "animation:anchor-slide 10s linear -2.5s both paused;"); Layout(root);
        RenderPoint(box, new Point(42, 28)); box.Width = 220; box.Height = 140; Layout(root);
        RenderPoint(box, new Point(57, 38));
        Css.SetStyle(box, Style("content-box", "auto", 0)); Layout(root); RenderPoint(box, new Point(100, 64));
    }

    [Fact]
    public void PublicAnchorTransitionInterpolatesDifferentUnitsAndEndsAtTheCurrentBaseValue()
    {
        var (root, box) = Scene();
        Css.SetStyle(box, Style("content-box", "zero", 0) + "transition:offset-anchor 10s linear;"); Layout(root);
        Css.SetStyle(box, Style("content-box", "end", 0) + "transition:offset-anchor 10s linear;"); Layout(root);
        Assert.True(CssTransitions.HasActiveTransition(box, CssMotionPath.AnchorProperty));
        var frame = (Jalium.UI.Animation.IFrameAnimatable)box; long start = System.Diagnostics.Stopwatch.GetTimestamp();
        frame.OnAnimationFrame(start); frame.OnAnimationFrame(start + 5 * System.Diagnostics.Stopwatch.Frequency);
        RenderPoint(box, new Point(70, 44));
        frame.OnAnimationFrame(start + 10 * System.Diagnostics.Stopwatch.Frequency);
        Assert.False(CssTransitions.HasActiveTransition(box, CssMotionPath.AnchorProperty)); RenderPoint(box, new Point(126, 76));
    }

    [Fact]
    public void PausedMixedLengthAndFarEdgeAnchorKeyframesResizeWithoutAPlaceholderBasis()
    {
        var (root, box) = Scene();
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("@keyframes mixed-anchor { from { offset-anchor:10px 20px; } to { offset-anchor:right 5px bottom 7px; } }") });
        var style = Style("content-box", "auto", 0) + "animation:mixed-anchor 10s linear -5s both paused;";
        Css.SetStyle(box, style); Layout(root); RenderPoint(box, new Point(72.5, 50.5));
        box.Width = 220; box.Height = 140; Layout(root); RenderPoint(box, new Point(102.5, 70.5));
    }

    [Fact]
    public void PausedMixedDistanceKeyframesUseTheCurrentPathLength()
    {
        var (root, box) = Scene();
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("@keyframes mixed-distance { from { offset-distance:10px; } to { offset-distance:calc(50% + 20px); } }") });
        var style = Style("content-box", "zero", 0) + "animation:mixed-distance 10s linear -5s both paused;";
        Css.SetStyle(box, style); Layout(root);
        Equal(new Point(276, 88), CssMotionPath.ResolveTransform(box).Transform.Value.Transform(default(Point)));
        Css.SetStyle(box, style.Replace("L500 100", "L700 100")); Layout(root);
        Equal(new Point(326, 88), CssMotionPath.ResolveTransform(box).Transform.Value.Transform(default(Point)));
    }

    [Fact]
    public void PausedPositionKeyframesRetainTheirContainingBlockBasis()
    {
        var (root, box) = Scene();
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("@keyframes mixed-position { from { offset-position:10px 20px; } to { offset-position:right 5px bottom 7px; } }") });
        Css.SetStyle(box, "transform-box:content-box;offset-path:ray(0deg);offset-distance:0;offset-anchor:0 0;offset-rotate:0deg;animation:mixed-position 10s linear -5s both paused;"); Layout(root);
        Equal(new Point(388.5, 394.5), CssMotionPath.ResolveTransform(box).Transform.Value.Transform(default(Point)));
        Css.SetStyle(root, "display:block;width:400px;height:300px;"); Layout(root);
        Equal(new Point(188.5, 144.5), CssMotionPath.ResolveTransform(box).Transform.Value.Transform(default(Point)));
    }

    [Theory]
    [InlineData("1em 2rem", 34, 32)] [InlineData("10cqw 10cqh", 54, 42)] [InlineData("10vw 10vh", 114, 62)]
    public void RelativeAnchorUnitsUseTheRealFontContainerAndViewportContext(string anchor, double x, double y)
    {
        var (root, box) = Scene();
        root.HorizontalAlignment = HorizontalAlignment.Left; root.VerticalAlignment = VerticalAlignment.Top;
        Css.SetStyle(root, "display:block;width:400px;height:300px;font-size:10px;container-type:size;");
        root.SetValue(Css.ViewportMetricsProperty, CssViewportMetrics.Uniform(new Size(1000, 500)));
        Css.SetStyle(box, PathStyle + $"transform-box:content-box;offset-anchor:{anchor};offset-rotate:0deg;font-size:20px;"); Layout(root);
        RenderPoint(box, new Point(x, y));
    }

    [Theory]
    [InlineData("left right")] [InlineData("top bottom")] [InlineData("right 10px 20px")] [InlineData("bottom 3px 4px")]
    [InlineData("auto center")] [InlineData("none")] [InlineData("left 5px bottom 2px extra")]
    public void InvalidPositionsRevealTheValidLowerCascadeValue(string invalid)
    {
        var (root, box) = Scene();
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("Border { offset-anchor:0 0; }") });
        Css.SetStyle(box, PathStyle + $"transform-box:content-box;offset-anchor:{invalid};offset-rotate:0deg;"); Layout(root);
        RenderPoint(box, new Point(14, 12));
    }

    [Theory]
    [InlineData(.25, 70, 44)] [InlineData(.75, 14, 12)]
    public void AutoToExplicitAnchorKeyframesKeepDiscreteValueSemantics(double progress, double x, double y)
    {
        var (root, box) = Scene();
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("@keyframes anchor-kind { from { offset-anchor:auto; } to { offset-anchor:0% 0%; } }") });
        Css.SetStyle(box, Style("content-box", "auto", 0) + $"animation:anchor-kind 10s linear -{progress * 10}s both paused;"); Layout(root);
        RenderPoint(box, new Point(x, y));
    }

    [Theory]
    [InlineData("auto")] [InlineData("percent")]
    public void TheEffectiveCssStatePaddingWinsOverANativeStyleTriggerInTheReferenceBox(string anchor)
    {
        var (root, box) = Scene(); box.ClearValue(Border.PaddingProperty);
        box.SetLayerValue(Border.PaddingProperty, new Thickness(20), DependencyObject.LayerValueSource.StyleTrigger);
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("Border { padding:8px 28px 16px 12px; } Border:disabled { padding:8px 28px 16px 32px; }") });
        Css.SetStyle(box, Style("content-box", anchor, 0)); Layout(root);
        Assert.Equal(new Thickness(20), box.Padding); RenderPoint(box, new Point(78, 48));
        box.IsEnabled = false; Layout(root);
        Assert.Equal(new Thickness(32, 8, 28, 16), box.Padding); RenderPoint(box, new Point(80, 44));
        box.IsEnabled = true; Layout(root); RenderPoint(box, new Point(78, 48));
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 90)] [InlineData(false, -90)]
    [InlineData(true, 0)] [InlineData(true, 90)] [InlineData(true, -90)]
    public void ReferenceBoxMotionPreservesRealDepthAndPerspective(bool perspective, int angle)
    {
        var (root, box) = Scene();
        var ordinary = perspective ? "transform:matrix3d(1,0,0,0,0,1,0,0,0,0,1,-.0025,10,20,30,1);" : "transform:translate3d(10px,20px,30px);";
        Css.SetStyle(box, Style("content-box", "percent", angle) + ordinary); Layout(root);
        Assert.True(CssTransform3D.Has3D(box.RenderTransform));
        var physical = Matrix3D.Identity; physical.Translate(new Vector3D(-70, -44, 0));
        physical *= CssTransform3D.GetTransformMatrix3D(box.RenderTransform);
        var restore = Matrix3D.Identity; restore.Translate(new Vector3D(70, 44, 0)); physical *= restore;
        var input = new Point3D(8, 8, 5); double w = perspective ? 1 - 5.0 / 400 : 1;
        var xy = OnPath(new Point(70 + (8 - 70 + 10) / w, 44 + (8 - 44 + 20) / w), new Point(70, 44), angle);
        var actual = physical.Transform(input); Equal(xy, new Point(actual.X, actual.Y)); Assert.Equal(35 / w, actual.Z, 6);
        Css.SetStyle(box, ordinary + "offset-path:none;"); Layout(root);
        Assert.True(CssTransform3D.Has3D(box.RenderTransform));
        Assert.Equal(30, CssTransform3D.GetTransformMatrix3D(box.RenderTransform).OffsetZ, 6);
    }

    [Theory]
    [InlineData("ray(90deg at right 10px bottom 20px)", 790, 780)]
    [InlineData("ray(at right 10px bottom 20px 90deg closest-side contain)", 790, 780)]
    [InlineData("ray(contain at bottom 20px right 10px closest-side 90deg)", 790, 780)]
    [InlineData("circle(20px at right 10px bottom 20px)", 810, 780)]
    [InlineData("ellipse(20px 30px at bottom 20px right 10px)", 810, 780)]
    public void ShapeAndRayCentersUseTheSharedFarEdgePositionGrammar(string path, double x, double y)
    {
        var (root, box) = Scene();
        Css.SetStyle(box, $"transform-box:content-box;offset-path:{path};offset-anchor:0 0;offset-distance:0;offset-rotate:0deg;"); Layout(root);
        var pose = CssMotionPath.ResolveTransform(box); Equal(new Point(x, y), new Point(pose.X, pose.Y));
        Equal(new Point(x - 14, y - 12), pose.Transform.Value.Transform(default(Point)));
    }

    [Theory]
    [InlineData("ray(90deg at top left at bottom right)")] [InlineData("ray(90deg 45deg)")]
    [InlineData("ray(90deg contain contain)")] [InlineData("ray(at left)")]
    public void InvalidRayArgumentsRevealTheLowerPathDeclaration(string invalid)
    {
        var (root, box) = Scene();
        Css.SetStyleSheets(root, new CssStyleSheetCollection { CssStyleSheet.Parse("Border { " + PathStyle + " }") });
        Css.SetStyle(box, $"transform-box:content-box;offset-path:{invalid};offset-anchor:0 0;offset-rotate:0deg;"); Layout(root);
        RenderPoint(box, new Point(14, 12));
    }

    public static IEnumerable<object[]> ClosedShapeQuarters()
    {
        foreach (bool ellipse in new[] { false, true })
        foreach (bool implicitCenter in new[] { false, true })
        foreach (int quarter in new[] { 0, 1, 2, 3, 4 }) yield return [ellipse, implicitCenter, quarter];
    }

    [Theory, MemberData(nameof(ClosedShapeQuarters))]
    public void CircleAndEllipseEquivalentPathsStartAtTheRightAndProceedClockwise(bool ellipse, bool implicitCenter, int quarter)
    {
        var (root, box) = Scene();
        var path = (ellipse ? "ellipse(20px 30px" : "circle(20px") + (implicitCenter ? ")" : " at 100px 100px)");
        Css.SetStyle(box, $"offset-path:{path};offset-position:100px 100px;offset-distance:{quarter * 25}%;offset-rotate:auto;"); Layout(root);
        double phi = quarter * Math.PI / 2, ry = ellipse ? 30 : 20;
        var pose = CssMotionPath.ResolveTransform(box); Equal(new Point(100 + 20 * Math.Cos(phi), 100 + ry * Math.Sin(phi)), new Point(pose.X, pose.Y));
        Assert.Equal((quarter * 90 + 90) % 360, (pose.AngleDegrees % 360 + 360) % 360, 6);
    }

    [Fact]
    public void ARunningMixedAnchorTransitionKeepsItsPercentBasisWhenResized()
    {
        var (root, box) = Scene();
        var style = Style("content-box", "auto", 0) + "transition:offset-anchor 10s linear;offset-anchor:";
        Css.SetStyle(box, style + "10px 20px;"); Layout(root);
        Css.SetStyle(box, style + "right 5px bottom 7px;"); Layout(root);
        var frame = (Jalium.UI.Animation.IFrameAnimatable)box; long start = System.Diagnostics.Stopwatch.GetTimestamp();
        frame.OnAnimationFrame(start); frame.OnAnimationFrame(start + 5 * System.Diagnostics.Stopwatch.Frequency);
        RenderPoint(box, new Point(72.5, 50.5)); box.Width = 220; box.Height = 140; Layout(root);
        RenderPoint(box, new Point(102.5, 70.5));
        frame.OnAnimationFrame(start + 10 * System.Diagnostics.Stopwatch.Frequency);
        RenderPoint(box, new Point(181, 109)); Assert.False(CssTransitions.HasActiveTransition(box, CssMotionPath.AnchorProperty));
    }

    [Fact]
    public void AnchorsOutsideTheReferenceBoxAreNotClamped()
    {
        var (root, box) = Scene(); Css.SetStyle(box, Style("content-box", "auto", 0) + "offset-anchor:-25% 150%;"); Layout(root);
        RenderPoint(box, new Point(-14, 108));
    }

    [Theory]
    [InlineData("auto")] [InlineData("percent")]
    public void EmptyContentRectanglesKeepFiniteAnchorsAndRestore(string anchor)
    {
        var (root, box) = Scene(); box.Width = box.Height = 0;
        Css.SetStyle(box, Style("content-box", anchor, 0)); Layout(root);
        var pose = CssMotionPath.ResolveTransform(box, new Size(0, 0), new Size(800, 800));
        Assert.True(double.IsFinite(pose.Value.OffsetX) && double.IsFinite(pose.Value.OffsetY));
        box.Width = 160; box.Height = 100; Layout(root); RenderPoint(box, new Point(70, 44));
    }

    private static (Canvas Root, Border Box) Scene()
    {
        var root = new Canvas(); var box = new Border { Width = 160, Height = 100, Padding = new Thickness(12, 8, 28, 16),
            BorderThickness = new Thickness(2, 4, 6, 8), Background = Brushes.White };
        root.Children.Add(box); return (root, box);
    }
    private static string Style(string referenceBox, string anchor, int rotation)
    {
        var value = anchor switch { "percent" => "50% 50%", "zero" => "0 0", "end" => "100% 100%", "calc" => "calc(25% + 6px) calc(75% - 2px)",
            "top" => "top", "reordered" => "bottom right", "edge-offset" => "right 10px bottom 20px", "three-value" => "right 10px bottom", _ => "auto" };
        return PathStyle + $"transform-box:{referenceBox};offset-anchor:{value};offset-rotate:{rotation}deg;" +
            (anchor == "origin-percent" ? "transform-origin:50% 50%;" : anchor == "origin-zero" ? "transform-origin:0 0;" : "");
    }
    private static Point LiteralAnchor(string box, string anchor)
    {
        bool content = box is "content-box" or "fill-box";
        double x = content ? 14 : 0, y = content ? 12 : 0, width = content ? 112 : 160, height = content ? 64 : 100;
        return anchor switch
        {
            "zero" or "origin-zero" => new(x, y), "end" or "reordered" => new(x + width, y + height),
            "calc" => new(x + width / 4 + 6, y + height * .75 - 2), "top" => new(x + width / 2, y),
            "edge-offset" => new(x + width - 10, y + height - 20), "three-value" => new(x + width - 10, y + height),
            _ => new(x + width / 2, y + height / 2),
        };
    }
    private static Point OnPath(Point point, Point anchor, int rotation) => rotation switch
    {
        90 => new(200 - point.Y + anchor.Y, 100 + point.X - anchor.X),
        -90 => new(200 + point.Y - anchor.Y, 100 - point.X + anchor.X),
        _ => new(200 + point.X - anchor.X, 100 + point.Y - anchor.Y),
    };
    private static void RenderPoint(Border box, Point anchor)
    {
        var expected = new Point(208 - anchor.X, 108 - anchor.Y);
        Equal(expected, box.GetRenderMatrix().Transform(new Point(8, 8)));
    }
    private static void Equal(Point expected, Point actual) { Assert.Equal(expected.X, actual.X, 6); Assert.Equal(expected.Y, actual.Y, 6); }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(800, 800)); root.Arrange(new Rect(0, 0, 800, 800)); root.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher ?? Dispatcher.CurrentDispatcher); root.UpdateLayout();
    }
}
