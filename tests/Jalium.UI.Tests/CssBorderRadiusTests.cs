using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssBorderRadiusTests
{
    [Fact]
    public void Percentage_UsesBothBorderBoxAxes_AndUpdatesAfterResize()
    {
        var border = Box("border-radius:50%", 200, 100);
        Assert.Equal(new Size(100, 50), Used(border).TopLeft);
        Assert.Null(border.HitTest(new(2, 2)));
        Assert.Same(border, border.HitTest(new(100, 2))?.VisualHit);
        Layout(border, 100, 200);
        Assert.Equal(new Size(50, 100), Used(border).TopLeft);
        Assert.Same(border, border.HitTest(new(50, 100))?.VisualHit);
    }

    [Fact]
    public void Shorthand_ExpandsAxesIndependently_AndLonghandWinsItsCorner()
    {
        var border = Box("border-radius:10px 20px 30px / 4px 8px; border-bottom-left-radius:12px 6px", 200, 100);
        var radii = Used(border);
        Assert.Equal(new Size(10, 4), radii.TopLeft);
        Assert.Equal(new Size(20, 8), radii.TopRight);
        Assert.Equal(new Size(30, 4), radii.BottomRight);
        Assert.Equal(new Size(12, 6), radii.BottomLeft);
    }

    [Fact]
    public void OverlappingCorners_ScaleEveryAxisByTheSameFactor()
    {
        var border = Box("border-radius:80px 40px 20px 10px / 30px 20px 10px 50px", 100, 40);
        var radii = Used(border); // The left edge requires 40 / (30 + 50).
        Assert.Equal(new Size(40, 15), radii.TopLeft);
        Assert.Equal(new Size(20, 10), radii.TopRight);
        Assert.Equal(new Size(10, 5), radii.BottomRight);
        Assert.Equal(new Size(5, 25), radii.BottomLeft);
    }

    [Fact]
    public void CalcAndVariables_RetainPercentageBasisUntilRenderSize()
    {
        var border = Box("--r:calc(20% + 2px); border-radius:var(--r) / calc(10% + 4px)", 100, 200);
        Assert.Equal(new Size(22, 24), Used(border).TopLeft);
        Layout(border, 200, 100);
        Assert.Equal(new Size(42, 14), Used(border).TopLeft);
        Css.SetStyle(border, "border-radius:calc(10px - 30px) / 10px");
        Assert.Equal(default, Used(border).TopLeft);
    }

    [Theory]
    [InlineData("-1px")]
    [InlineData("1")]
    [InlineData("10px / -1px")]
    [InlineData("10px /")]
    [InlineData("/ 10px")]
    [InlineData("1px 2px 3px 4px 5px")]
    [InlineData("1px / 1px 2px 3px 4px 5px")]
    [InlineData("1px / 2px / 3px")]
    [InlineData("1px, 2px")]
    [InlineData("1px / red")]
    [InlineData("auto")]
    public void InvalidShorthand_DoesNotEraseEarlierCorners(string invalid)
    {
        var border = Box("border-radius:8px / 4px; border-radius:" + invalid, 100, 100);
        Assert.Equal(new Size(8, 4), Used(border).TopLeft);
        Assert.Equal(new Size(8, 4), Used(border).BottomRight);
    }

    [Fact]
    public void InvalidLonghand_AndImportantShorthand_PreserveCascade()
    {
        var border = Box("border-radius:10px / 5px!important; border-top-left-radius:20px 10px", 100, 100);
        Assert.Equal(new Size(10, 5), Used(border).TopLeft);
        Css.SetStyle(border, "border-radius:10px / 5px; border-top-left-radius:1px 2px 3px");
        Assert.Equal(new Size(10, 5), Used(border).TopLeft);
    }

    [Fact]
    public void Inherit_CopiesComputedLengthsButUsesTheChildsPercentageBasis()
    {
        var child = new Border { Width = 80, Height = 40 };
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "font-size:20px; border-radius:1em / 25%");
        Css.SetStyle(child, "font-size:40px; border-radius:inherit; border-bottom-right-radius:initial");
        Layout(parent, 200, 100);
        Assert.Equal(new Size(20, 10), Used(child).TopLeft);
        Assert.Equal(default, Used(child).BottomRight);
    }

    [Fact]
    public void NativeValuesBindingsAndRemoval_RestoreTheOriginalContract()
    {
        var border = Box("border-radius:50%", 200, 100);
        var source = new Border { CornerRadius = new(7) };
        BindingOperations.SetBinding(border, Border.CornerRadiusProperty,
            new Binding(nameof(Border.CornerRadius)) { Source = source });
        Assert.Null(CssBorderRadiusProperties.Get(border));
        Assert.Equal(new CornerRadius(7), border.CornerRadius);
        source.CornerRadius = new(9);
        Assert.Equal(new CornerRadius(9), border.CornerRadius);
        BindingOperations.ClearBinding(border, Border.CornerRadiusProperty);
        border.ClearValue(Border.CornerRadiusProperty);
        Assert.Equal(new Size(100, 50), Used(border).TopLeft);
        Css.SetStyle(border, "");
        Assert.Null(CssBorderRadiusProperties.Get(border));
        Assert.Equal(default, border.CornerRadius);
    }

    [Fact]
    public void EqualNativeValueAndAnimation_InvalidateRetainedEllipticalPaint()
    {
        var border = Box("border-radius:50%", 120, 60);
        var circular = border.CornerRadius;
        border.ClearRenderDirty();
        border.CornerRadius = circular;
        Assert.True(border.IsRenderDirty);
        Assert.Null(CssBorderRadiusProperties.Get(border));
        border.ClearRenderDirty();
        border.ClearValue(Border.CornerRadiusProperty);
        Assert.True(border.IsRenderDirty);
        border.ClearRenderDirty();
        Assert.True(border.SetAnimatedValue(Border.CornerRadiusProperty, circular, false));
        Assert.True(border.IsRenderDirty);
        Assert.Null(CssBorderRadiusProperties.Get(border));
        border.ClearAnimatedValue(Border.CornerRadiusProperty);
        Assert.NotNull(CssBorderRadiusProperties.Get(border));
    }

    [Fact]
    public void EitherZeroAxis_MakesThatCornerSquare()
    {
        var border = Box("border-radius:40px / 0; background-color:lime", 100, 60);
        Assert.Equal(default, Used(border).TopLeft);
        Assert.Same(border, border.HitTest(new(1, 1))?.VisualHit);
        Assert.Equal((byte)255, Pixels(border, 100, 60)[(1 * 100 + 1) * 4 + 1]);
    }

    [Fact]
    public void PercentageEllipse_PixelsMatchAnIndependentNativeEllipse()
    {
        var border = Box("border-radius:50%; background-color:lime", 120, 60);
        var reference = new EllipseVisual();
        Layout(reference, 120, 60);
        Assert.Equal(Pixels(reference, 120, 60), Pixels(border, 120, 60));
    }

    [Fact]
    public void PanelAndTextBlock_PaintCssEllipsesWithoutNativeCornerRadius()
    {
        foreach (FrameworkElement element in new FrameworkElement[] { new Grid(), new TextBlock() })
        {
            Css.SetStyle(element, "border-radius:50%; background-color:lime");
            Layout(element, 120, 60);
            var radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(element));
            Assert.Equal(new Size(60, 30), radius.Resolve(element.RenderSize).TopLeft);
            var pixels = Pixels(element, 120, 60);
            Assert.Equal((byte)0, pixels[(1 * 120 + 1) * 4 + 3]);
            Assert.Equal((byte)255, pixels[(30 * 120 + 60) * 4 + 1]);
            Assert.Null(element.HitTest(new(1, 1)));
            Assert.Same(element, element.HitTest(new(60, 30))?.VisualHit);

            Css.SetStyle(element, "border-radius:50%; background-color:lime; overflow:hidden");
            Assert.IsType<CssRoundedRectangleGeometry>(element.GetLayoutClip());

            Layout(element, 60, 30);
            Assert.Equal(new Size(30, 15), radius.Resolve(element.RenderSize).TopLeft);
            Css.SetStyle(element, "background-color:lime");
            Assert.Null(CssBorderRadiusProperties.Get(element));
            Layout(element, 120, 60);
            Assert.Equal((byte)255, Pixels(element, 120, 60)[(1 * 120 + 1) * 4 + 3]);
        }
    }

    [Fact]
    public void PanelOverflow_ClipsChildrenAtCssRadius_AndNativeBackgroundKeepsPrecedence()
    {
        var child = new Border { Background = Brushes.Red };
        var panel = new Grid { Background = Brushes.Lime };
        panel.Children.Add(child);
        Css.SetStyle(panel, "border-radius:50%; background-color:blue; overflow:hidden");
        Layout(panel, 120, 60);
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(panel.GetLayoutClip());
        Assert.Equal(new Size(60, 30), clip.Radii.TopLeft);
        Assert.Null(panel.HitTest(new(1, 1)));
        Assert.Same(child, panel.HitTest(new(60, 30))?.VisualHit);
        var pixels = Pixels(panel, 120, 60);
        Assert.Equal((byte)0, pixels[(1 * 120 + 1) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(30 * 120 + 60) * 4 + 2]);

        Css.SetStyle(panel, "border-radius:50%; background-color:blue");
        Layout(panel, 120, 60);
        Assert.Same(child, panel.HitTest(new(1, 1))?.VisualHit);
        Assert.Equal((byte)255, Pixels(panel, 120, 60)[(1 * 120 + 1) * 4 + 2]);

        panel.Children.Clear();
        Layout(panel, 120, 60);
        pixels = Pixels(panel, 120, 60);
        Assert.Equal((byte)255, pixels[(30 * 120 + 60) * 4 + 1]);
        Assert.Equal((byte)0, pixels[(30 * 120 + 60) * 4 + 0]);
        Assert.Equal((byte)0, pixels[(1 * 120 + 1) * 4 + 3]);
    }

    [Fact]
    public void ElementsWithoutNativeCornerRadius_InheritComputedPercentOnTheirOwnSize()
    {
        var child = new TextBlock { Width = 60, Height = 30 };
        var parent = new Grid();
        parent.Children.Add(child);
        Css.SetStyle(parent, "border-radius:50%");
        Css.SetStyle(child, "border-radius:inherit; background-color:lime");
        Layout(parent, 120, 60);
        var radius = Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(child));
        Assert.Equal(new Size(30, 15), radius.Resolve(child.RenderSize).TopLeft);
        Assert.Null(child.HitTest(new(1, 1)));
        Assert.Equal((byte)0, Pixels(child, 60, 30)[(1 * 60 + 1) * 4 + 3]);
    }

    [Fact]
    public void PanelAndTextBlock_ClipBackgroundColorToInsetEllipse()
    {
        foreach (FrameworkElement element in new FrameworkElement[]
                 {
                     new Grid(), new TextBlock { Padding = new Thickness(10) }
                 })
        {
            Css.SetStyle(element,
                "border-radius:50%;padding:10px;background-color:lime;background-clip:content-box");
            Layout(element, 100, 60);
            var pixels = Pixels(element, 100, 60);
            Assert.Equal((byte)0, pixels[(12 * 100 + 12) * 4 + 3]);
            Assert.Equal((byte)255, pixels[(30 * 100 + 50) * 4 + 1]);
        }
    }

    [Fact]
    public void Overflow_ClipsChildrenAtTheEllipticalPaddingEdge_AndPreservesTheBorderBackground()
    {
        var child = new Border { Background = Brushes.Red };
        var border = new Border { Child = child, BorderThickness = new(10, 4, 20, 8), BorderBrush = Brushes.Transparent };
        Css.SetStyle(border, "border-radius:50%; background-color:lime; overflow:hidden");
        Layout(border, 120, 60);
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(border.GetLayoutClip());
        Assert.Equal(new Size(50, 26), clip.Radii.TopLeft);
        Assert.Equal(new Size(40, 22), clip.Radii.BottomRight);
        Assert.False(clip.FillContains(new Point(11, 5)));
        Assert.True(clip.FillContains(new Point(60, 30)));
        Assert.Same(child, border.HitTest(new(60, 30))?.VisualHit);
        Assert.Null(border.HitTest(new(1, 1)));
        var pixels = Pixels(border, 120, 60);
        Assert.Equal((byte)255, pixels[(2 * 120 + 60) * 4 + 1]); // background under transparent top border
        Assert.Equal((byte)255, pixels[(30 * 120 + 60) * 4 + 2]); // child in the centre
        Assert.Equal((byte)0, pixels[(5 * 120 + 11) * 4 + 2]); // clipped child, no red
    }

    [Fact]
    public void ThickOppositeBorders_TrimTheInnerArcWithoutRescalingIt()
    {
        var border = Box("border-radius:0 100% 0 0; border:30px solid blue; overflow:hidden", 100, 100);
        var clip = Assert.IsType<CssRoundedRectangleGeometry>(border.GetLayoutClip());
        Assert.Equal(new Rect(30, 30, 40, 40), clip.Rect);
        Assert.Equal(new Size(70, 70), clip.Radii.TopRight);
        foreach (var point in new[] { new Point(40, 40), new Point(35, 65), new Point(65, 65) })
            Assert.Equal(clip.FillContains(point), clip.Path.FillContains(point));
        Assert.True(clip.FillContains(new Point(35, 65)));
        Assert.False(clip.FillContains(new Point(40, 40)));
    }

    [Fact]
    public void VisibleOverflow_KeepsChildInputOutsideTheRoundedParent()
    {
        var child = new Border { Width = 20, Height = 20, Margin = new(-20, -20, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "border-radius:50%");
        Layout(parent, 100, 60);
        Assert.Same(child, parent.HitTest(new(-10, -10))?.VisualHit);
    }

    [Fact]
    public void ExplicitNativeClip_RemainsAuthoritative()
    {
        var border = Box("border-radius:50%; overflow:hidden", 120, 60);
        border.Clip = new RectangleGeometry(new Rect(40, 20, 20, 20));
        Assert.Same(border.Clip, border.GetLayoutClip());
        Assert.Null(border.HitTest(new(80, 30)));
        Assert.Same(border, border.HitTest(new(50, 30))?.VisualHit);
    }

    internal static Border Box(string css, double width, double height)
    {
        var border = new Border();
        Css.SetStyle(border, css);
        Layout(border, width, height);
        return border;
    }

    internal static void Layout(FrameworkElement element, double width, double height)
    {
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
    }

    internal static CssUsedBorderRadii Used(Border border) => Assert.IsType<CssBorderRadiusValue>(CssBorderRadiusProperties.Get(border)).Resolve(border.RenderSize);
    internal static byte[] Pixels(Visual visual, int width, int height)
    {
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        target.Render(visual);
        var bytes = new byte[width * height * 4];
        target.CopyPixels(new Int32Rect(0, 0, width, height), bytes, width * 4, 0);
        return bytes;
    }

    private sealed class EllipseVisual : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
            => dc.DrawEllipse(Brushes.Lime, null, new(RenderSize.Width / 2, RenderSize.Height / 2), RenderSize.Width / 2, RenderSize.Height / 2);
    }
}
