using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public class CssGridLayoutTests
{
    private static Border Child(Panel parent, string css = "")
    {
        var child = new Border();
        parent.Children.Add(child);
        Css.SetStyle(child, css);
        return child;
    }

    private static void Layout(Panel panel, double width = 420, double height = 120)
    {
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void FixedAndFractionTracks_UseExistingChildrenWithoutChangingTheirOwnership()
    {
        var panel = new StackPanel();
        var a = Child(panel); var b = Child(panel); var c = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-template-columns:100px 1fr 2fr; gap:10px");
        Layout(panel);
        Assert.Equal(100, a.ActualWidth, 6); Assert.Equal(100, b.ActualWidth, 6); Assert.Equal(200, c.ActualWidth, 6);
        Assert.Equal(110, b.VisualBounds.X, 6); Assert.Equal(220, c.VisualBounds.X, 6);
        Assert.Same(panel, a.VisualParent); Assert.Same(panel, b.Parent);
        Assert.Same(a, panel.Children[0]); Assert.Same(b, panel.Children[1]); Assert.Same(c, panel.Children[2]);
    }

    [Fact]
    public void PercentAndCalcTracks_RecomputeWhenTheContainerResizes()
    {
        var panel = new StackPanel(); var a = Child(panel); var b = Child(panel); var c = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-template-columns:20% calc(30% - 10px) 1fr; column-gap:10px");
        Layout(panel, 400);
        Assert.Equal(80, a.ActualWidth, 6); Assert.Equal(110, b.ActualWidth, 6); Assert.Equal(190, c.ActualWidth, 6);
        Layout(panel, 600);
        Assert.Equal(120, a.ActualWidth, 6); Assert.Equal(170, b.ActualWidth, 6); Assert.Equal(290, c.ActualWidth, 6);
    }

    [Theory]
    [InlineData("auto-fill", 105)]
    [InlineData("auto-fit", 220)]
    public void AutomaticRepetition_DistinguishesEmptyTracks(string repetition, double expected)
    {
        var panel = new StackPanel(); var a = Child(panel); var b = Child(panel);
        Css.SetStyle(panel, $"display:grid; grid-template-columns:repeat({repetition}, minmax(100px, 1fr)); gap:10px");
        Layout(panel, 450);
        Assert.Equal(expected, a.ActualWidth, 6); Assert.Equal(expected + 10, b.VisualBounds.X, 6);
        Layout(panel, 190);
        Assert.Equal(190, a.ActualWidth, 6); Assert.Equal(0, b.VisualBounds.X, 6);
        Assert.True(b.VisualBounds.Y > 0);
    }

    [Fact]
    public void NamedAreas_SpanRowsWithoutNativeGridDefinitions()
    {
        var panel = new StackPanel(); var nav = Child(panel, "grid-area:nav");
        var main = Child(panel, "grid-area:Main"); var foot = Child(panel, "grid-area:foot");
        Css.SetStyle(panel, "display:grid; grid-template-areas:'nav Main' 'nav foot'; grid-template-columns:100px 1fr; grid-template-rows:40px 20px; gap:10px");
        Layout(panel);
        Assert.Equal(100, nav.ActualWidth, 6); Assert.Equal(70, nav.ActualHeight, 6);
        Assert.Equal(110, main.VisualBounds.X, 6); Assert.Equal(40, main.ActualHeight, 6);
        Assert.Equal(50, foot.VisualBounds.Y, 6); Assert.Equal(310, foot.ActualWidth, 6);
    }

    [Fact]
    public void NamedLinesAndNegativeIndices_ResolveAgainstTheExplicitGrid()
    {
        var panel = new StackPanel(); var span = Child(panel, "grid-column:first / last; grid-row:1");
        var last = Child(panel, "grid-column:-2 / -1; grid-row:2");
        Css.SetStyle(panel, "display:grid; grid-template-columns:[first] 50px [middle] 100px [last]; grid-auto-rows:20px; gap:10px");
        Layout(panel);
        Assert.Equal(160, span.ActualWidth, 6);
        Assert.Equal(60, last.VisualBounds.X, 6); Assert.Equal(100, last.ActualWidth, 6);
        Assert.Equal(30, last.VisualBounds.Y, 6);
    }

    [Theory]
    [InlineData("row", 25)]
    [InlineData("row dense", 0)]
    public void DensePlacement_FillsEarlierHoles(string flow, double thirdY)
    {
        var panel = new StackPanel(); Child(panel, "grid-column:span 2"); Child(panel, "grid-column:span 2"); var c = Child(panel);
        Css.SetStyle(panel, $"display:grid; grid-template-columns:repeat(3,50px); grid-auto-rows:20px; gap:5px; grid-auto-flow:{flow}");
        Layout(panel);
        Assert.Equal(110, c.VisualBounds.X, 6); Assert.Equal(thirdY, c.VisualBounds.Y, 6);
    }

    [Fact]
    public void ColumnFlow_AddsImplicitColumns()
    {
        var panel = new StackPanel(); var a = Child(panel); var b = Child(panel); var c = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-template-rows:repeat(2,40px); grid-auto-columns:70px; grid-auto-flow:column; gap:5px");
        Layout(panel);
        Assert.Equal(0, a.VisualBounds.Y, 6); Assert.Equal(45, b.VisualBounds.Y, 6);
        Assert.Equal(75, c.VisualBounds.X, 6); Assert.Equal(0, c.VisualBounds.Y, 6);
    }

    [Fact]
    public void ImplicitTracksBeforeTheExplicitGrid_UseTheReversedAutoTrackPattern()
    {
        var panel = new StackPanel(); var a = Child(panel, "grid-column:-5 / -4; grid-row:1");
        var b = Child(panel, "grid-column:1; grid-row:1");
        Css.SetStyle(panel, "display:grid; grid-template-columns:repeat(3,50px); grid-auto-columns:20px 40px");
        Layout(panel);
        Assert.Equal(40, a.ActualWidth, 6); Assert.Equal(40, b.VisualBounds.X, 6);
    }

    [Fact]
    public void LocalAttachedValuesAndLocalDimensions_WinOverCss()
    {
        var panel = new StackPanel(); var child = Child(panel, "grid-column:2; width:200px; justify-self:start");
        Grid.SetColumn(child, 0); child.Width = 30; child.HorizontalAlignment = HorizontalAlignment.Right;
        Css.SetStyle(panel, "display:grid; grid-template-columns:100px 1fr");
        Layout(panel);
        Assert.Equal(30, child.ActualWidth, 6); Assert.Equal(70, child.VisualBounds.X, 6);
        Assert.Equal(0, Grid.GetColumn(child));
    }

    [Fact]
    public void CssItemAlignment_DoesNotWriteNativeLocalValuesOrApplyPercentagesTwice()
    {
        var panel = new StackPanel(); var child = Child(panel, "width:50%; height:20px; justify-self:center; align-self:end");
        Css.SetStyle(panel, "display:grid; grid-template-columns:200px; grid-template-rows:80px");
        Layout(panel);
        Assert.Equal(100, child.ActualWidth, 6); Assert.Equal(50, child.VisualBounds.X, 6); Assert.Equal(60, child.VisualBounds.Y, 6);
        Assert.Same(DependencyProperty.UnsetValue, child.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty));
        Assert.Same(DependencyProperty.UnsetValue, child.ReadLocalValue(FrameworkElement.WidthProperty));
    }

    [Fact]
    public void ClearingCssGrid_RestoresNativeDefinitionsBindingsAndPlacement()
    {
        var panel = new Grid(); var firstDefinition = new ColumnDefinition { Width = new GridLength(120) };
        var secondDefinition = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        panel.ColumnDefinitions.Add(firstDefinition); panel.ColumnDefinitions.Add(secondDefinition);
        var a = Child(panel); var b = Child(panel); Grid.SetColumn(b, 1);
        var source = new Border { Width = 60 };
        b.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.Width)) { Source = source });
        var binding = b.GetBindingExpression(FrameworkElement.WidthProperty);
        Css.SetStyle(panel, "display:grid; grid-template-columns:210px 210px"); Layout(panel);
        Assert.Equal(210, a.ActualWidth, 6); Assert.Equal(60, b.ActualWidth, 6);
        Css.SetStyle(panel, ""); Layout(panel);
        Assert.Equal(120, a.ActualWidth, 6); Assert.Equal(60, b.ActualWidth, 6);
        Assert.Same(firstDefinition, panel.ColumnDefinitions[0]); Assert.Same(secondDefinition, panel.ColumnDefinitions[1]);
        Assert.Same(binding, b.GetBindingExpression(FrameworkElement.WidthProperty));
    }

    [Fact]
    public void ExistingAliasesVariablesAndCustomConverters_ParticipateInGridLayout()
    {
        CssMappings.RegisterAlias("test-grid-columns", "grid-template-columns");
        CssMappings.RegisterProperty("test-grid-width", FrameworkElement.WidthProperty, new WidthConverter());
        var panel = new StackPanel(); var a = Child(panel, "test-grid-width:30px"); var b = Child(panel);
        Css.SetStyle(panel, "--tracks:1fr 3fr; display:grid; test-grid-columns:var(--tracks)"); Layout(panel, 400);
        Assert.Equal(30, a.ActualWidth, 6); Assert.Equal(0, a.VisualBounds.X, 6);
        Assert.Equal(100, b.VisualBounds.X, 6); Assert.Equal(300, b.ActualWidth, 6);
        Css.SetStyle(panel, "--tracks:3fr 1fr; display:grid; test-grid-columns:var(--tracks)"); Layout(panel, 400);
        Assert.Equal(300, b.VisualBounds.X, 6); Assert.Equal(100, b.ActualWidth, 6);
    }

    [Fact]
    public void OrderAffectsPlacementWithoutChangingTheNativeCollection()
    {
        var panel = new StackPanel(); var a = Child(panel, "order:2"); var b = Child(panel, "order:-1");
        Css.SetStyle(panel, "display:grid; grid-template-columns:100px 100px"); Layout(panel);
        Assert.Equal(100, a.VisualBounds.X, 6); Assert.Equal(0, b.VisualBounds.X, 6);
        Assert.Same(a, panel.Children[0]); Assert.Same(b, panel.Children[1]);
    }

    [Fact]
    public void FractionSumBelowOne_LeavesUnclaimedSpace()
    {
        var panel = new StackPanel(); var a = Child(panel); var b = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-template-columns:0.2fr 0.2fr"); Layout(panel, 500);
        Assert.Equal(100, a.ActualWidth, 6); Assert.Equal(100, b.ActualWidth, 6);
    }

    [Theory]
    [InlineData("minmax(1fr, 100px)")]
    [InlineData("repeat(0, 1fr)")]
    [InlineData("repeat(auto-fit, 1fr)")]
    [InlineData("repeat(2, repeat(3, 10px))")]
    [InlineData("-10px 1fr")]
    [InlineData("1fr garbage")]
    public void InvalidTrackGrammar_DoesNotReportSupport(string value)
        => Assert.False(Css.Supports("grid-template-columns", value));

    [Fact]
    public void NonRectangularAreas_AndFlexLengthsOutsideTracks_AreRejected()
    {
        Assert.False(Css.Supports("grid-template-areas", "'a a' 'a b'"));
        Assert.False(Css.Supports("width", "1fr"));
        Assert.False(Css.Supports("width", "calc(1fr)"));
    }

    [Theory]
    [InlineData("grid-template", "'nav Main' 40px 'nav foot' 20px / 100px 1fr")]
    [InlineData("grid", "'nav Main' 40px 'nav foot' 20px / 100px 1fr")]
    public void TemplateShorthands_ExpandAreasRowsAndColumns(string property, string value)
    {
        var panel = new StackPanel(); var nav = Child(panel, "grid-area:nav"); var footer = Child(panel, "grid-area:foot");
        Css.SetStyle(panel, $"display:grid; {property}:{value}; gap:10px"); Layout(panel);
        Assert.Equal(70, nav.ActualHeight, 6); Assert.Equal(100, nav.ActualWidth, 6);
        Assert.Equal(110, footer.VisualBounds.X, 6); Assert.Equal(50, footer.VisualBounds.Y, 6);
    }

    [Fact]
    public void GridShorthand_ResetsImplicitProperties_AndSupportsAutoFlow()
    {
        var panel = new StackPanel(); Child(panel); Child(panel); var third = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-auto-flow:column; grid-auto-rows:90px; grid:auto-flow 20px / repeat(2,100px)");
        Layout(panel);
        Assert.Equal(0, third.VisualBounds.X, 6); Assert.Equal(20, third.VisualBounds.Y, 6); Assert.Equal(20, third.ActualHeight, 6);
        Css.SetStyle(panel, "display:grid; grid-template:30px 40px / 100px 100px"); Layout(panel);
        Assert.Equal(30, third.VisualBounds.Y, 6); Assert.Equal(40, third.ActualHeight, 6);
    }

    [Fact]
    public void ImportantGapLonghand_WinsOverLaterShorthand_AndUsesTheCorrectAxisBasis()
    {
        var panel = new StackPanel(); var first = Child(panel); var second = Child(panel); var third = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-template-columns:1fr 1fr; grid-template-rows:20px 20px; column-gap:10% !important; gap:calc(5% + 2px) 1em");
        Layout(panel, 400, 100);
        Assert.Equal(180, first.ActualWidth, 6); Assert.Equal(220, second.VisualBounds.X, 6);
        Assert.Equal(27, third.VisualBounds.Y, 6);
    }

    [Fact]
    public void InheritedComputedTracks_KeepTheParentFontBasis()
    {
        var outer = new StackPanel(); var inner = new StackPanel(); outer.Children.Add(inner); var child = Child(inner);
        Css.SetStyle(outer, "font-size:10px; grid-template-columns:2em");
        Css.SetStyle(inner, "display:grid; font-size:20px; grid-template-columns:inherit");
        Layout(outer);
        Assert.Equal(20, child.ActualWidth, 6);
    }

    [Fact]
    public void ClearingPlacementAndDisplay_RestoresNativeAlignment()
    {
        var panel = new StackPanel(); var child = Child(panel, "width:30px; height:20px; grid-column:2");
        Css.SetStyle(panel, "display:grid; grid-template-columns:100px 100px"); Layout(panel, 200);
        Assert.Equal(100, child.VisualBounds.X, 6);
        Css.SetStyle(child, "width:30px; height:20px"); Layout(panel, 200);
        Assert.Equal(0, child.VisualBounds.X, 6);
        Css.SetStyle(panel, ""); Layout(panel, 200);
        Assert.Equal(85, child.VisualBounds.X, 6);
    }

    [Fact]
    public void LocalNativeSpacing_HasPriorityOverCssGap()
    {
        var panel = new Grid { ColumnSpacing = 30 }; var first = Child(panel); var second = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-template-columns:1fr 1fr; column-gap:10%"); Layout(panel, 400);
        Assert.Equal(185, first.ActualWidth, 6); Assert.Equal(215, second.VisualBounds.X, 6);
        Assert.Equal(30, panel.ColumnSpacing, 6);
    }

    [Fact]
    public void GridRectangles_DriveSoftwarePixelsAndNativeHitTesting()
    {
        var panel = new StackPanel(); var red = Child(panel, "background-color:red"); var blue = Child(panel, "background-color:blue");
        Css.SetStyle(panel, "display:grid; grid-template:40px / 30px 50px; gap:10px"); Layout(panel, 90, 40);
        var target = new RenderTargetBitmap(90, 40, 96, 96, PixelFormat.Bgra32);
        target.Clear(Color.FromRgb(0, 0, 0)); target.Render(panel);
        var pixels = new byte[90 * 40 * 4]; target.CopyPixels(new Int32Rect(0, 0, 90, 40), pixels, 90 * 4, 0);
        Assert.Equal(255, pixels[(20 * 90 + 15) * 4 + 2]);
        Assert.Equal(255, pixels[(20 * 90 + 65) * 4]);
        Assert.Equal(0, pixels[(20 * 90 + 35) * 4]); Assert.Equal(0, pixels[(20 * 90 + 35) * 4 + 2]);
        Assert.Same(red, VisualTreeHelper.HitTest(panel, new Point(15, 20))?.VisualHit);
        Assert.Same(blue, VisualTreeHelper.HitTest(panel, new Point(65, 20))?.VisualHit);
    }

    [Fact]
    public void FlexAlsoUsesSharedPercentageAndFontRelativeGaps()
    {
        var panel = new StackPanel(); var first = Child(panel, "flex:1"); var second = Child(panel, "flex:1");
        Css.SetStyle(panel, "display:flex; column-gap:calc(10% + 1em); font-size:20px"); Layout(panel, 400);
        Assert.Equal(170, first.ActualWidth, 6); Assert.Equal(230, second.VisualBounds.X, 6);
    }

    private sealed class WidthConverter : ICssValueConverter
    {
        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
            => CssValueParsing.TryParseLength(rawValue, out var value) ? value : null;
    }

    [Fact]
    public void ExistingNativeAlignmentConverter_IsRetainedUnlessCssAlignmentIsExplicit()
    {
        CssMappings.RegisterProperty("test-grid-native-align", FrameworkElement.HorizontalAlignmentProperty, new AlignmentConverter());
        var panel = new StackPanel(); var child = Child(panel, "test-grid-native-align:right; width:30px");
        Css.SetStyle(panel, "display:grid; grid-template-columns:100px"); Layout(panel);
        Assert.Equal(70, child.VisualBounds.X, 6);
        Css.SetStyle(child, "test-grid-native-align:right; width:30px; justify-self:center"); Layout(panel);
        Assert.Equal(35, child.VisualBounds.X, 6);
        Assert.Same(DependencyProperty.UnsetValue, child.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty));
    }

    [Fact]
    public void FitContentTrack_UsesNativeIntrinsicContributionsAndRespectsTheLimit()
    {
        var panel = new StackPanel(); var intrinsic = new IntrinsicBox(); panel.Children.Add(intrinsic); var rest = Child(panel);
        Css.SetStyle(panel, "display:grid; grid-template-columns:fit-content(80px) 1fr"); Layout(panel, 300);
        Assert.Equal(80, intrinsic.ActualWidth, 6); Assert.Equal(80, rest.VisualBounds.X, 6); Assert.Equal(220, rest.ActualWidth, 6);
    }

    private sealed class AlignmentConverter : ICssValueConverter
    {
        public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture) => HorizontalAlignment.Right;
    }

    private sealed class IntrinsicBox : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize)
            => new(double.IsPositiveInfinity(availableSize.Width) ? 200 : 40, 20);
    }
}
