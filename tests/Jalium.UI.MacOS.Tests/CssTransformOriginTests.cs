using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed partial class CssTransformOriginTests
{
    public static IEnumerable<object[]> RubyCases()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr" })
        foreach (var merge in new[] { "separate", "merge", "auto" })
        foreach (var direction in new[] { "ltr", "rtl" })
        foreach (double scale in new[] { .75, 1.5 }) yield return [mode, merge, direction, scale];
    }

    [Theory, MemberData(nameof(RubyCases))]
    public void NativeDefaultOriginHitsTheOriginalRubySource(string mode, string merge, string direction, double scale)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var canvas = new Canvas();
        var root = new StackPanel { Width = 220, Height = 260, RenderTransform = new ScaleTransform(scale, scale) };
        var basis = new TextBlock { Text = "東", IsTextSelectionEnabled = true };
        var note = new TextBlock { Text = "とうとうとう", IsTextSelectionEnabled = true };
        root.Children.Add(basis); root.Children.Add(note); canvas.Children.Add(root);
        Canvas.SetLeft(root, 40); Canvas.SetTop(root, 60);
        Css.SetStyle(root, $"display:ruby;writing-mode:{mode};ruby-merge:{merge};direction:{direction};ruby-position:over;ruby-align:center;font-family:'Hiragino Sans';font-size:32px;line-height:40px;white-space:pre-wrap;text-autospace:no-autospace;");
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;");
        Css.SetStyle(note, "display:ruby-text;font-size:16px;line-height:96px;");
        Layout(canvas);
        var point = note.GetRenderMatrix().Transform(new Point(note.RenderSize.Width / 2, note.RenderSize.Height / 2));
        Assert.Same(note, canvas.InputHitTest(point));
        Assert.Equal(default(Point), root.RenderTransformOrigin);
        Assert.Same(DependencyProperty.UnsetValue, root.ReadLocalValue(UIElement.RenderTransformOriginProperty));
        note.Select(0, 2); Assert.Equal("とう", note.SelectedText);
        note.Select(0, 6); Assert.Equal("とうとうとう", note.SelectedText);
        Assert.Same(root, note.VisualParent);
    }

    public static IEnumerable<object[]> BoxCases()
    {
        foreach (bool css in new[] { false, true })
        foreach (bool border in new[] { false, true })
        foreach (var origin in new[] { "default", "zero", "center" })
        foreach (var transform in new[] { "shrink", "grow", "rotate", "matrix" })
            yield return [css, border, origin, transform];
    }

    [Theory, MemberData(nameof(BoxCases))]
    public void PaintCoordinatesAndHitTestingUseTheSameOrigin(bool css, bool border, string origin, string transform)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { Text = "字形 ABC", Width = 120, Height = 80, FontSize = 16, LineHeight = 20 };
        FrameworkElement box = border ? new Border { Child = text, Width = 120, Height = 80 } : text;
        var (literal, declaration) = Transform(transform);
        if (css) Css.SetStyle(box, "transform:" + declaration + ";");
        else box.RenderTransform = new MatrixTransform(literal);
        if (origin != "default") box.RenderTransformOrigin = origin == "center" ? new Point(.5, .5) : default;
        var canvas = new Canvas(); canvas.Children.Add(box); Canvas.SetLeft(box, 40); Canvas.SetTop(box, 60); Layout(canvas);
        double factor = origin == "center" || origin == "default" && css ? .5 : 0;
        var expected = At(literal, factor);
        Assert.Equal(new Point(factor, factor), box.RenderTransformOrigin);
        foreach (var point in new[] { new Point(8, 8), new Point(60, 40), new Point(112, 72) })
        {
            Equal(expected.Transform(point), box.GetRenderMatrix().Transform(point));
            Equal(expected.Transform(point), box.TransformToVisual(canvas)!.Transform(point));
        }
        Assert.Same(text, canvas.InputHitTest(expected.Transform(new Point(60, 40))));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SingularTransformsDoNotHitTheCollapsedSource(bool css)
    {
        var text = new TextBlock { Text = "collapsed", Width = 120, Height = 80 };
        if (css) Css.SetStyle(text, "transform:matrix(0,0,0,1,0,0);");
        else text.RenderTransform = new ScaleTransform(0, 1);
        var canvas = new Canvas(); canvas.Children.Add(text); Canvas.SetLeft(text, 40); Canvas.SetTop(text, 60); Layout(canvas);
        Assert.NotSame(text, canvas.InputHitTest(new Point(css ? 100 : 40, 80)));
        Assert.False(text.GetRenderMatrix().HasInverse);
    }

    [Fact]
    public void NativeParentAndCssChildUseTheirOwnOrigins()
    {
        var root = new Canvas(); var parent = new Canvas { Width = 300, Height = 200, RenderTransform = new ScaleTransform(.5, .5) };
        var child = new TextBlock { Text = "nested", Width = 120, Height = 80 };
        root.Children.Add(parent); parent.Children.Add(child); Canvas.SetLeft(parent, 20); Canvas.SetTop(parent, 30); Canvas.SetLeft(child, 40); Canvas.SetTop(child, 60);
        Css.SetStyle(child, "transform:scale(.75);"); Layout(root);
        var expected = new Point(47.5 + 8 * .375, 65 + 8 * .375);
        Equal(expected, child.GetRenderMatrix().Transform(new Point(8, 8)));
        Assert.Same(child, root.InputHitTest(expected));
    }

    [Fact]
    public void SameTransformSwitchesBetweenCssAndNativeWithoutWritingAnOrigin()
    {
        var text = new TextBlock { Width = 120, Height = 80 };
        var changes = OriginChanges(text);
        Css.SetStyle(text, "transform:scale(.75);");
        Assert.Equal(new Point(.5, .5), text.RenderTransformOrigin);
        var shared = text.RenderTransform!; changes.Clear();
        text.RenderTransform = shared;
        Assert.Equal(default(Point), text.RenderTransformOrigin);
        text.ClearValue(UIElement.RenderTransformProperty);
        Assert.Equal(new Point(.5, .5), text.RenderTransformOrigin);
        Assert.Equal(new[] { default(Point), new Point(.5, .5) }, changes);
        Css.SetStyle(text, ""); Assert.Equal(default(Point), text.RenderTransformOrigin);
        Assert.Same(DependencyProperty.UnsetValue, text.ReadLocalValue(UIElement.RenderTransformOriginProperty));
    }

    [Fact]
    public void LocalOriginAndCurrentOriginOverrideTheCssInitialValue()
    {
        var text = new TextBlock(); Css.SetStyle(text, "transform:scale(.75);");
        text.RenderTransformOrigin = default;
        Assert.Equal(default(Point), text.RenderTransformOrigin);
        text.ClearValue(UIElement.RenderTransformOriginProperty);
        Assert.Equal(new Point(.5, .5), text.RenderTransformOrigin);
        text.SetCurrentValue(UIElement.RenderTransformOriginProperty, default(Point));
        Assert.Equal(default(Point), text.RenderTransformOrigin);
    }

    [Fact]
    public void CssAnimationOnlyTransformUsesCssInitialOriginAndRestoresNativeDefault()
    {
        var text = new TextBlock(); var native = new ScaleTransform(.75, .75);
        text.SetLayerValue(UIElement.RenderTransformProperty, native, DependencyObject.LayerValueSource.StyleSetter);
        var changes = OriginChanges(text);
        text.SetCssAnimatedValue(UIElement.RenderTransformProperty, native);
        Assert.Equal(new Point(.5, .5), text.RenderTransformOrigin);
        text.ClearCssAnimatedValue(UIElement.RenderTransformProperty);
        Assert.Equal(default(Point), text.RenderTransformOrigin);
        Assert.Equal(new[] { new Point(.5, .5), default(Point) }, changes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeAnimationSourceChangesRefreshOriginEvenForTheSameTransform(bool holdEnd)
    {
        var text = new TextBlock(); Css.SetStyle(text, "transform:scale(.75);");
        var shared = text.RenderTransform!; var changes = OriginChanges(text);
        Assert.True(text.SetAnimatedValue(UIElement.RenderTransformProperty, shared, holdEnd));
        Assert.Equal(default(Point), text.RenderTransformOrigin);
        Assert.False(text.SetAnimatedValue(UIElement.RenderTransformProperty, shared, holdEnd));
        text.ClearAnimatedValue(UIElement.RenderTransformProperty);
        Assert.Equal(new Point(.5, .5), text.RenderTransformOrigin);
        Assert.Equal(new[] { default(Point), new Point(.5, .5) }, changes);
    }

    [Fact]
    public void AnimatedOriginsAreNotReplacedByTheImplicitCssCenter()
    {
        var text = new TextBlock(); Css.SetStyle(text, "transform:scale(.75);");
        text.SetCssAnimatedValue(UIElement.RenderTransformOriginProperty, new Point(.2, .3));
        Assert.Equal(new Point(.2, .3), text.RenderTransformOrigin);
        text.ClearCssAnimatedValue(UIElement.RenderTransformOriginProperty);
        Assert.Equal(new Point(.5, .5), text.RenderTransformOrigin);
        text.SetAnimatedValue(UIElement.RenderTransformOriginProperty, default(Point), false);
        Assert.Equal(default(Point), text.RenderTransformOrigin);
        text.ClearAnimatedValue(UIElement.RenderTransformOriginProperty);
        Assert.Equal(new Point(.5, .5), text.RenderTransformOrigin);
    }

    public static IEnumerable<object[]> ReferenceCases()
    {
        foreach (var box in new[] { "content-box", "fill-box", "border-box", "stroke-box", "view-box" })
        foreach (var transform in new[] { "shrink", "rotate", "matrix" }) yield return [box, transform];
    }

    [Theory, MemberData(nameof(ReferenceCases))]
    public void ImplicitCssOriginUsesTheActualTransformReferenceBox(string referenceBox, string transform)
    {
        var box = new Border { Width = 160, Height = 100, Padding = new Thickness(12, 8, 28, 16), BorderThickness = new Thickness(2, 4, 6, 8) };
        var canvas = new Canvas(); canvas.Children.Add(box); Canvas.SetLeft(box, 40); Canvas.SetTop(box, 60);
        var (matrix, css) = Transform(transform);
        Css.SetStyle(box, $"transform:{css};transform-box:{referenceBox};"); Layout(canvas);
        var pivot = referenceBox is "content-box" or "fill-box" ? new Point(70, 44) : new Point(80, 50);
        Equal(new Point(pivot.X / 160, pivot.Y / 100), box.RenderTransformOrigin);
        var expected = new Matrix(matrix.M11, matrix.M12, matrix.M21, matrix.M22,
            40 + pivot.X - pivot.X * matrix.M11 - pivot.Y * matrix.M21 + matrix.OffsetX,
            60 + pivot.Y - pivot.X * matrix.M12 - pivot.Y * matrix.M22 + matrix.OffsetY);
        Equal(expected.Transform(new Point(8, 8)), box.GetRenderMatrix().Transform(new Point(8, 8)));
    }

    [Fact]
    public void ReferenceBoxSizeAndInsetsRefreshTheImplicitOriginAndNotifyObservers()
    {
        var box = new Border { Width = 160, Height = 100, Padding = new Thickness(12, 8, 28, 16), BorderThickness = new Thickness(2, 4, 6, 8) };
        var canvas = new Canvas(); canvas.Children.Add(box);
        var changes = OriginChanges(box);
        Css.SetStyle(box, "transform:scale(.75);transform-box:content-box;"); Layout(canvas);
        Equal(new Point(70.0 / 160, .44), box.RenderTransformOrigin);
        box.Width = 220; box.Height = 140; Layout(canvas);
        Equal(new Point(100.0 / 220, 64.0 / 140), box.RenderTransformOrigin);
        box.Padding = new Thickness(32, 8, 28, 16); Layout(canvas);
        Equal(new Point(.5, 64.0 / 140), box.RenderTransformOrigin);
        Assert.Contains(changes, p => Math.Abs(p.X - 100.0 / 220) < 1e-6 && Math.Abs(p.Y - 64.0 / 140) < 1e-6);
        Css.SetStyle(box, "transform:scale(.75);transform-box:border-box;"); Layout(canvas);
        Equal(new Point(.5, .5), box.RenderTransformOrigin);
        Assert.Same(DependencyProperty.UnsetValue, box.ReadLocalValue(UIElement.RenderTransformOriginProperty));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PublicPausedCssKeyframesSupplyTheCssDefaultOrigin(bool explicitOrigin)
    {
        var canvas = new Canvas(); var box = new Border { Width = 120, Height = 80 }; canvas.Children.Add(box);
        Css.SetStyleSheets(canvas, new CssStyleSheetCollection { CssStyleSheet.Parse("@keyframes origin-zoom { from { transform:scale(.5); } to { transform:scale(1); } }") });
        Css.SetStyle(box, "animation:origin-zoom 10s linear -5s both paused;" + (explicitOrigin ? "transform-origin:20% 30%;" : ""));
        Layout(canvas); Assert.NotNull(box.RenderTransform); Assert.Equal(.75, box.RenderTransform!.Value.M11, 4);
        Equal(explicitOrigin ? new Point(.2, .3) : new Point(.5, .5), box.RenderTransformOrigin);
        Css.SetStyle(box, "animation:none;"); Layout(canvas);
        Assert.Null(box.RenderTransform); Equal(default, box.RenderTransformOrigin);
    }

    [Fact]
    public void PercentageInsetsTrackTheContainingWidthInTheImplicitContentOrigin()
    {
        var canvas = new Canvas();
        Css.SetStyle(canvas, "display:block;width:300px;height:240px;");
        var box = new Border { Width = 200, Height = 160, BorderThickness = new Thickness(2, 4, 6, 8) };
        var changes = OriginChanges(box);
        canvas.Children.Add(box);
        Css.SetStyle(box, "transform:scale(.75);transform-box:content-box;padding:5% 10% 15% 20%;");
        Layout(canvas);
        Assert.Equal(300, box.CssLayout!.ContainingWidthCache);
        Equal(new Point(113.0 / 200, 63.0 / 160), box.RenderTransformOrigin);
        Css.SetStyle(canvas, "display:block;width:400px;height:240px;"); Layout(canvas);
        Assert.Equal(400, box.CssLayout!.ContainingWidthCache);
        Equal(new Point(118.0 / 200, 58.0 / 160), box.RenderTransformOrigin);
        Assert.Contains(changes, p => Math.Abs(p.X - 118.0 / 200) < 1e-6 && Math.Abs(p.Y - 58.0 / 160) < 1e-6);
    }

    [Fact]
    public void NativeOriginBindingRemainsAnIndependentAuthoredValue()
    {
        var source = new Border { Tag = default(Point) }; var box = new Border();
        Css.SetStyle(box, "transform:scale(.75);");
        box.SetBinding(UIElement.RenderTransformOriginProperty, new Binding("Tag") { Source = source });
        Equal(default, box.RenderTransformOrigin);
        source.Tag = new Point(.2, .3); Equal(new Point(.2, .3), box.RenderTransformOrigin);
        Assert.NotNull(box.GetBindingExpression(UIElement.RenderTransformOriginProperty));
    }

    [Fact]
    public void PublicTransformTransitionUsesAndRetainsTheCssOrigin()
    {
        var root = new Canvas(); var box = new Border { Width = 120, Height = 80 }; root.Children.Add(box);
        Css.SetStyle(box, "transform:scale(.5);transition:transform 10s linear;"); Layout(root);
        Css.SetStyle(box, "transform:scale(1);transition:transform 10s linear;"); Layout(root);
        Assert.True(CssTransitions.HasActiveTransition(box, UIElement.RenderTransformProperty));
        Equal(new Point(.5, .5), box.RenderTransformOrigin);
        var frame = (Jalium.UI.Animation.IFrameAnimatable)box;
        long start = System.Diagnostics.Stopwatch.GetTimestamp(); frame.OnAnimationFrame(start);
        frame.OnAnimationFrame(start + 5 * System.Diagnostics.Stopwatch.Frequency);
        Assert.Equal(.75, box.RenderTransform!.Value.M11, 4); Equal(new Point(.5, .5), box.RenderTransformOrigin);
        frame.OnAnimationFrame(start + 10 * System.Diagnostics.Stopwatch.Frequency); Equal(new Point(.5, .5), box.RenderTransformOrigin);
        Assert.False(CssTransitions.HasActiveTransition(box, UIElement.RenderTransformProperty));
        Css.SetStyle(box, "transform:none;transition:none;"); Layout(root);
        Assert.Null(box.RenderTransform); Equal(new Point(.5, .5), box.RenderTransformOrigin);
        Css.SetStyle(box, ""); Layout(root); Equal(default, box.RenderTransformOrigin);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ThreeDimensionalScaleUsesTheSameOriginAndOriginalHit(bool authoredZero)
    {
        var root = new Canvas(); var box = new Border { Width = 120, Height = 80, Background = Brushes.White };
        root.Children.Add(box); Canvas.SetLeft(box, 40); Canvas.SetTop(box, 60);
        Css.SetStyle(box, "transform:scale3d(.75,.75,1);" + (authoredZero ? "transform-origin:0 0;" : "")); Layout(root);
        var origin = authoredZero ? 0 : .5; Equal(new Point(origin, origin), box.RenderTransformOrigin);
        var point = At(new Matrix(.75, 0, 0, .75, 0, 0), origin).Transform(new Point(8, 8));
        Equal(point, box.GetRenderMatrix().Transform(new Point(8, 8)));
        Assert.Same(box, root.InputHitTest(point));
    }

    [Theory]
    [InlineData("none")] [InlineData("opacity")] [InlineData("removed")] [InlineData("native")]
    public void CssTransitionCancellationRevealsTheCurrentSourceWithoutPromotingTheAnimatedFrame(string cancellation)
    {
        var root = new Canvas(); var box = new Border { Width = 120, Height = 80 }; root.Children.Add(box);
        Css.SetStyle(box, "transform:scale(.5);transition:transform 10s linear;"); Layout(root);
        Css.SetStyle(box, "transform:scale(1);transition:transform 10s linear;"); Layout(root);
        var frame = (Jalium.UI.Animation.IFrameAnimatable)box; long start = System.Diagnostics.Stopwatch.GetTimestamp();
        frame.OnAnimationFrame(start); frame.OnAnimationFrame(start + 5 * System.Diagnostics.Stopwatch.Frequency);
        Assert.Equal(.75, box.RenderTransform!.Value.M11, 4);
        if (cancellation == "native") box.RenderTransform = new ScaleTransform(1.5, 1.5);
        else Css.SetStyle(box, cancellation == "removed" ? "" : "transform:scale(1);transition:" + (cancellation == "none" ? "none;" : "opacity 10s linear;"));
        Layout(root); Assert.False(CssTransitions.HasActiveTransition(box, UIElement.RenderTransformProperty));
        if (cancellation == "removed") { Assert.Null(box.RenderTransform); Equal(default, box.RenderTransformOrigin); }
        else { Assert.Equal(cancellation == "native" ? 1.5 : 1, box.RenderTransform!.Value.M11, 6); Equal(cancellation == "native" ? default : new Point(.5, .5), box.RenderTransformOrigin); }
    }

    private static List<Point> OriginChanges(UIElement element)
    {
        var changes = new List<Point>();
        element.PropertyChangedInternal += (property, _, value) => { if (property == UIElement.RenderTransformOriginProperty) changes.Add((Point)value!); };
        return changes;
    }
    private static void Layout(FrameworkElement root) { root.Measure(new Size(800, 800)); root.Arrange(new Rect(0, 0, 800, 800)); }
    private static void Equal(Point expected, Point actual) { Assert.Equal(expected.X, actual.X, 6); Assert.Equal(expected.Y, actual.Y, 6); }
    private static (Matrix Matrix, string Css) Transform(string name) => name switch
    {
        "shrink" => (new(.75, 0, 0, .75, 0, 0), "scale(.75)"),
        "grow" => (new(1.5, 0, 0, 1.5, 0, 0), "scale(1.5)"),
        "rotate" => (new(Math.Sqrt(3) / 2, .5, -.5, Math.Sqrt(3) / 2, 0, 0), "rotate(30deg)"),
        _ => (new(.75, .1, .2, .8, 12, 8), "matrix(.75,.1,.2,.8,12,8)"),
    };
    private static Matrix At(Matrix m, double origin) => new(m.M11, m.M12, m.M21, m.M22,
        40 + 120 * origin - 120 * origin * m.M11 - 80 * origin * m.M21 + m.OffsetX,
        60 + 80 * origin - 120 * origin * m.M12 - 80 * origin * m.M22 + m.OffsetY);
}
