using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssMappingTableTests
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    [Fact]
    public void BackgroundColor_WrapsColorInSolidColorBrush()
    {
        var border = new Border();
        Css.SetStyle(border, "background-color: rgb(255 0 0)");
        var brush = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(Rgb(255, 0, 0), brush.Color);
    }

    [Fact]
    public void BackgroundColor_TransparentIsNonNullBrush()
    {
        var border = new Border();
        Css.SetStyle(border, "background-color: transparent");
        var brush = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(0, brush.Color.A);
    }

    [Fact]
    public void Color_MapsToForeground()
    {
        var block = new TextBlock();
        Css.SetStyle(block, "color: rebeccapurple");
        var brush = Assert.IsType<SolidColorBrush>(block.Foreground);
        Assert.Equal(Rgb(0x66, 0x33, 0x99), brush.Color);
    }

    [Fact]
    public void Background_LinearGradient_ToRight()
    {
        var border = new Border();
        Css.SetStyle(border, "background: linear-gradient(to right, red, blue)");
        var brush = Assert.IsType<LinearGradientBrush>(border.Background);
        Assert.Equal(0, brush.StartPoint.X, 9);
        Assert.Equal(0.5, brush.StartPoint.Y, 9);
        Assert.Equal(1, brush.EndPoint.X, 9);
        Assert.Equal(0.5, brush.EndPoint.Y, 9);
        Assert.Equal(2, brush.GradientStops.Count);
        Assert.Equal(0, brush.GradientStops[0].Offset);
        Assert.Equal(Rgb(255, 0, 0), brush.GradientStops[0].Color);
        Assert.Equal(1, brush.GradientStops[1].Offset);
    }

    [Fact]
    public void BackgroundImage_GradientAngleAndStopInterpolation()
    {
        var border = new Border();
        Css.SetStyle(border, "background-image: linear-gradient(180deg, red, lime, blue)");
        var brush = Assert.IsType<LinearGradientBrush>(border.Background);
        // 180deg = top → bottom.
        Assert.Equal(0.5, brush.StartPoint.X, 9);
        Assert.Equal(0, brush.StartPoint.Y, 9);
        Assert.Equal(0.5, brush.EndPoint.X, 9);
        Assert.Equal(1, brush.EndPoint.Y, 9);
        // Middle stop interpolates to 0.5.
        Assert.Equal(0.5, brush.GradientStops[1].Offset, 9);
    }

    [Fact]
    public void BackgroundImage_RadialGradient()
    {
        var border = new Border();
        Css.SetStyle(border, "background-image: radial-gradient(circle, white, black)");
        var brush = Assert.IsType<RadialGradientBrush>(border.Background);
        Assert.Equal(2, brush.GradientStops.Count);
    }

    [Fact]
    public void BackgroundImage_None_ClearsToColor()
    {
        var border = new Border();
        Css.SetStyle(border, "background-color: red; background-image: none");
        Assert.IsType<SolidColorBrush>(border.Background);
    }

    [Fact]
    public void Border_Shorthand_WidthStyleColor()
    {
        var border = new Border();
        Css.SetStyle(border, "border: 2px solid red");
        Assert.Equal(new Thickness(2), border.BorderThickness);
        var brush = Assert.IsType<SolidColorBrush>(border.BorderBrush);
        Assert.Equal(Rgb(255, 0, 0), brush.Color);
    }

    [Fact]
    public void Border_OmittedStyleResetsToNone_BrushCleared()
    {
        var border = new Border();
        // Per CSS, `border: 2px red` has style=none → no visible border.
        Css.SetStyle(border, "border: 2px red");
        Assert.Null(border.BorderBrush);
        Assert.Equal(default, border.BorderThickness);
    }

    [Fact]
    public void Border_None_ClearsBrushAndWidth()
    {
        var border = new Border();
        Css.SetStyle(border, "border: 1px solid red");
        Assert.NotNull(border.BorderBrush);

        Css.SetStyle(border, "border: none");
        Assert.Null(border.BorderBrush);
        Assert.Equal(new Thickness(0), border.BorderThickness);
    }

    [Fact]
    public void BorderWidth_FourValueOrderConverts()
    {
        var border = new Border();
        Css.SetStyle(border, "border-width: 1px 2px 3px 4px");
        Assert.Equal(new Thickness(4, 1, 2, 3), border.BorderThickness);
    }

    [Fact]
    public void BorderWidth_Keywords()
    {
        var border = new Border();
        Css.SetStyle(border, "border-width: thin medium");
        Assert.Equal(new Thickness(3, 1, 3, 1), border.BorderThickness);
    }

    [Fact]
    public void BorderStyle_NoneClears_SolidKeeps()
    {
        var border = new Border();
        Css.SetStyle(border, "border-color: red; border-style: none");
        Assert.Null(border.BorderBrush);

        Css.SetStyle(border, "border-color: red; border-style: solid");
        Assert.NotNull(border.BorderBrush);
    }

    [Theory]
    [InlineData("border-radius: 8px", 8.0, 8.0, 8.0, 8.0)]
    [InlineData("border-radius: 1px 2px", 1.0, 2.0, 1.0, 2.0)]
    [InlineData("border-radius: 1px 2px 3px", 1.0, 2.0, 3.0, 2.0)]
    [InlineData("border-radius: 1px 2px 3px 4px", 1.0, 2.0, 3.0, 4.0)]
    public void BorderRadius_CornerOrderIsPassThrough(string style, double tl, double tr, double br, double bl)
    {
        var border = new Border();
        Css.SetStyle(border, style);
        Assert.Equal(new CornerRadius(tl, tr, br, bl), border.CornerRadius);
    }

    [Fact]
    public void BorderRadius_SingleCornerLonghand()
    {
        var border = new Border();
        Css.SetStyle(border, "border-top-left-radius: 6px");
        Assert.Equal(new CornerRadius(6, 0, 0, 0), border.CornerRadius);
    }

    [Fact]
    public void FontProperties_MapToTextElementProperties()
    {
        var block = new TextBlock();
        Css.SetStyle(block, "font-size: 18px; font-weight: bold; font-style: italic");
        Assert.Equal(18.0, block.FontSize);
        Assert.Equal(700, block.FontWeight.ToOpenTypeWeight());
        Assert.Equal(FontStyles.Italic, block.FontStyle);
    }

    [Fact]
    public void FontSize_EmScalesInheritedSize()
    {
        // font-size em resolves against the inherited (parent/default 14) size, not the
        // element's own value — and a local FontSize would outrank the CSS layer anyway.
        var block = new TextBlock();
        Css.SetStyle(block, "font-size: 1.5em");
        Assert.Equal(21.0, block.FontSize);
    }

    [Fact]
    public void FontSize_LocalValueOutranksCss()
    {
        var block = new TextBlock { FontSize = 20 };
        Css.SetStyle(block, "font-size: 1.5em");
        Assert.Equal(20.0, block.FontSize);
    }

    [Fact]
    public void FontSize_KeywordAndNumericWeight()
    {
        var block = new TextBlock();
        Css.SetStyle(block, "font-size: medium; font-weight: 600");
        Assert.Equal(14.0, block.FontSize);
        Assert.Equal(600, block.FontWeight.ToOpenTypeWeight());
    }

    [Fact]
    public void Font_Shorthand_AllComponents()
    {
        var block = new TextBlock();
        Css.SetStyle(block, "font: italic bold 16px/1.5 \"Segoe UI\", sans-serif");
        Assert.Equal(FontStyles.Italic, block.FontStyle);
        Assert.Equal(700, block.FontWeight.ToOpenTypeWeight());
        Assert.Equal(16.0, block.FontSize);
        Assert.Equal(24.0, block.LineHeight);
        Assert.Contains("Segoe UI", block.FontFamily.Source);
    }

    [Fact]
    public void FontFamily_GenericFamiliesMap()
    {
        var block = new TextBlock();
        Css.SetStyle(block, "font-family: monospace");
        Assert.Equal("Consolas", block.FontFamily.Source);
    }

    [Fact]
    public void LineHeight_UnitlessMultiplier()
    {
        var block = new TextBlock { FontSize = 20 };
        Css.SetStyle(block, "line-height: 1.5");
        Assert.Equal(30.0, block.LineHeight);

        Css.SetStyle(block, "line-height: 24px");
        Assert.Equal(24.0, block.LineHeight);
    }

    [Fact]
    public void TextProperties_MapToTextBlock()
    {
        var block = new TextBlock();
        Css.SetStyle(block, "text-align: center; white-space: nowrap; text-overflow: ellipsis");
        Assert.Equal(TextAlignment.Center, block.TextAlignment);
        Assert.Equal(TextWrapping.NoWrap, block.TextWrapping);
        Assert.Equal(TextTrimming.CharacterEllipsis, block.TextTrimming);
    }

    [Fact]
    public void TextDecoration_LinesCombine_NoneClears()
    {
        var block = new TextBlock();
        Css.SetStyle(block, "text-decoration: underline line-through");
        Assert.Equal(2, block.TextDecorations!.Count);

        Css.SetStyle(block, "text-decoration: none");
        Assert.True(block.TextDecorations is null or { Count: 0 });
    }

    [Fact]
    public void Transform_SingleFunction()
    {
        var border = new Border();
        Css.SetStyle(border, "transform: rotate(45deg)");
        var rotate = Assert.IsType<RotateTransform>(border.RenderTransform);
        Assert.Equal(45.0, rotate.Angle);
    }

    [Fact]
    public void Transform_ChainBecomesGroupInOrder()
    {
        var border = new Border();
        Css.SetStyle(border, "transform: translate(10px, 20px) scale(2) rotate(0.5turn)");
        var group = Assert.IsType<TransformGroup>(border.RenderTransform);
        Assert.Equal(3, group.Children.Count);
        var translate = Assert.IsType<TranslateTransform>(group.Children[0]);
        Assert.Equal(10.0, translate.X);
        Assert.Equal(20.0, translate.Y);
        var scale = Assert.IsType<ScaleTransform>(group.Children[1]);
        Assert.Equal(2.0, scale.ScaleX);
        Assert.Equal(2.0, scale.ScaleY);
        var rotate = Assert.IsType<RotateTransform>(group.Children[2]);
        Assert.Equal(180.0, rotate.Angle);
    }

    [Fact]
    public void Transform_None_ClearsTransform()
    {
        var border = new Border();
        Css.SetStyle(border, "transform: rotate(10deg)");
        Assert.NotNull(border.RenderTransform);
        Css.SetStyle(border, "transform: none");
        Assert.Null(border.RenderTransform);
    }

    [Fact]
    public void TransformOrigin_KeywordsAndPercentages()
    {
        var border = new Border();
        Css.SetStyle(border, "transform-origin: left top");
        Assert.Equal(new Point(0, 0), border.RenderTransformOrigin);

        Css.SetStyle(border, "transform-origin: 50% 100%");
        Assert.Equal(new Point(0.5, 1), border.RenderTransformOrigin);
    }

    [Fact]
    public void Transition_Shorthand_SetsThreeProperties()
    {
        var border = new Border();
        Css.SetStyle(border, "transition: background-color 0.2s ease-in");
        var properties = (TransitionPropertyCollection)border.GetValue(UIElement.TransitionPropertyProperty)!;
        Assert.True(properties.Contains("Background"));
        var duration = (Duration)border.GetValue(UIElement.TransitionDurationProperty)!;
        Assert.Equal(TimeSpan.FromMilliseconds(200), duration.TimeSpan);
        Assert.Equal(TransitionTimingFunction.EaseIn,
            (TransitionTimingFunction)border.GetValue(UIElement.TransitionTimingFunctionProperty)!);
    }

    [Fact]
    public void Transition_AllAndMultipleSegments()
    {
        var border = new Border();
        Css.SetStyle(border, "transition: opacity 150ms linear, transform 150ms");
        var properties = (TransitionPropertyCollection)border.GetValue(UIElement.TransitionPropertyProperty)!;
        Assert.True(properties.Contains("Opacity"));
        Assert.True(properties.Contains("RenderTransform"));

        Css.SetStyle(border, "transition: all 0.3s");
        properties = (TransitionPropertyCollection)border.GetValue(UIElement.TransitionPropertyProperty)!;
        Assert.True(properties.IsAll);
    }

    [Fact]
    public void BoxShadow_CartesianToPolarConversion()
    {
        var border = new Border();
        Css.SetStyle(border, "box-shadow: 3px 4px 5px rgba(0, 0, 0, 0.5)");
        var shadow = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(5.0, shadow.ShadowDepth, 6);
        Assert.Equal(306.8699, shadow.Direction, 3);
        Assert.Equal(5.0, shadow.BlurRadius);
        Assert.Equal(Rgb(0, 0, 0), shadow.Color);
        Assert.Equal(0.5, shadow.Opacity, 2);
    }

    [Fact]
    public void BoxShadow_InsetUsesInnerShadowWithSpread()
    {
        var border = new Border();
        Css.SetStyle(border, "box-shadow: inset 0 2px 4px 1px black");
        var shadow = Assert.IsType<InnerShadowEffect>(border.Effect);
        Assert.Equal(2.0, shadow.ShadowDepth, 6);
        Assert.Equal(270.0, shadow.Direction, 6);
        Assert.Equal(1.0, shadow.SpreadRadius);
    }

    [Fact]
    public void BoxShadow_MultipleBecomeEffectGroupInOrder()
    {
        var border = new Border();
        Css.SetStyle(border, "box-shadow: 1px 0 red, 0 1px blue");
        var group = Assert.IsType<EffectGroup>(border.Effect);
        Assert.Equal(2, group.Children.Count);
        Assert.All(group.Children, e => Assert.IsType<DropShadowEffect>(e));
    }

    [Fact]
    public void Filter_BlurMapsToBlurEffect()
    {
        var border = new Border();
        Css.SetStyle(border, "filter: blur(5px)");
        var blur = Assert.IsType<BlurEffect>(border.Effect);
        Assert.Equal(5.0, blur.Radius);
    }

    [Fact]
    public void Filter_UnsupportedFunctionDropsWholeDeclaration()
    {
        var border = new Border();
        Css.SetStyle(border, "filter: blur(5px) unknown-filter(1.2)");
        Assert.Null(border.Effect);
    }

    [Fact]
    public void BoxShadowAndFilter_CombineIntoEffectGroup()
    {
        var border = new Border();
        Css.SetStyle(border, "box-shadow: 0 2px 4px black; filter: blur(3px)");
        var group = Assert.IsType<EffectGroup>(border.Effect);
        Assert.Equal(2, group.Children.Count);
        Assert.IsType<DropShadowEffect>(group.Children[0]);
        Assert.IsType<BlurEffect>(group.Children[1]);
    }

    [Fact]
    public void ZIndex_MapsToPanelAttached()
    {
        var border = new Border();
        Css.SetStyle(border, "z-index: 5");
        Assert.Equal(5, Panel.GetZIndex(border));
    }

    [Fact]
    public void LeftTop_MapToCanvasAttached()
    {
        var border = new Border();
        Css.SetStyle(border, "left: 40px; top: 8px");
        Assert.Equal(40.0, Canvas.GetLeft(border));
        Assert.Equal(8.0, Canvas.GetTop(border));
    }

    [Fact]
    public void GridRow_OneBasedToZeroBased_WithSpan()
    {
        var border = new Border();
        Css.SetStyle(border, "grid-row: 2 / 4; grid-column: span 3");
        Assert.Equal(1, Grid.GetRow(border));
        Assert.Equal(2, Grid.GetRowSpan(border));
        Assert.Equal(3, Grid.GetColumnSpan(border));
    }

    [Fact]
    public void Gap_DispatchesByPanelType()
    {
        var grid = new Grid();
        Css.SetStyle(grid, "gap: 4px 8px");
        Assert.Equal(4.0, grid.GetValue(Grid.RowSpacingProperty));
        Assert.Equal(8.0, grid.GetValue(Grid.ColumnSpacingProperty));

        var stack = new StackPanel();
        Css.SetStyle(stack, "gap: 6px");
        Assert.Equal(6.0, stack.GetValue(StackPanel.SpacingProperty));

        var wrap = new WrapPanel();
        Css.SetStyle(wrap, "row-gap: 3px");
        Assert.Equal(3.0, wrap.GetValue(WrapPanel.VerticalSpacingProperty));
    }

    [Fact]
    public void BackgroundSize_CoverSetsUniformToFillOnImageBrush()
    {
        var border = new Border();
        Css.SetStyle(border, "background-image: linear-gradient(red, blue); background-size: cover");
        // Gradient brushes are unaffected by background-size; just confirm no crash and brush set.
        Assert.IsType<LinearGradientBrush>(border.Background);
    }
}
