using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFlexIntrinsicSizingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> GeometryCases()
    { foreach (var c in Cases) foreach (var native in new[] { false, true }) yield return [c.Name, native]; }

    [Theory] [MemberData(nameof(GeometryCases))]
    public void IntrinsicContributionsRespectContentPreferredSizesAndFlexibility(string name, bool native)
    {
        using var view = new Fixture(Cases.Single(c => c.Name == name), native); view.Layout();
        Near(view.Case.Main, view.Case.Axis == "row" ? view.Flex.ActualWidth : view.Flex.ActualHeight);
        if (!double.IsNaN(view.Case.Cross)) Near(view.Case.Cross, view.Flex.ActualWidth);
        view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void IndefinitePercentageBasisUsesContentAndCanRestoreNativeAuto(bool native)
    {
        using var view = new Fixture(new("local", "column", "auto", "", ["height:160px;flex:0 0 50%;"], 40, 40, 40), native);
        view.Layout(); Near(40, view.Flex.ActualHeight); Near(40, view.Items[0].ActualHeight);
        Css.SetStyle(view.Root, "display:block;width:600px;height:500px;");
        view.Layout(); Near(40, view.Flex.ActualHeight); Near(40, view.Items[0].ActualHeight);
        FlexPanel.SetBasis(view.Items[0], double.NaN); view.Layout(); Near(160, view.Flex.ActualHeight);
        view.Items[0].ClearValue(FlexPanel.BasisProperty); view.Layout(); Near(40, view.Flex.ActualHeight);
        Css.SetStyle(view.Flex, "display:flex;flex-direction:column;align-items:flex-start;width:100px;height:200px;");
        view.Layout(); Near(200, view.Flex.ActualHeight); Near(100, view.Items[0].ActualHeight);
        Css.SetStyle(view.Flex, "display:flex;flex-direction:column;align-items:flex-start;width:100px;");
        view.Layout(); Near(40, view.Flex.ActualHeight); Near(40, view.Items[0].ActualHeight); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void IntrinsicCacheTracksStylesNativeSourcesAndOriginalContent(bool native)
    {
        using var view = new Fixture(new("local", "row", "max", "", ["width:160px;flex:0 0 content;"], 40, 40, 40), native);
        view.Layout(); Near(40, view.Flex.ActualWidth);
        FlexPanel.SetBasis(view.Items[0], double.NaN); view.Layout(); Near(160, view.Flex.ActualWidth);
        view.Items[0].ClearValue(FlexPanel.BasisProperty); view.Layout(); Near(40, view.Flex.ActualWidth);
        view.Items[0].MinWidth = 70; view.Layout(); Near(70, view.Flex.ActualWidth);
        view.Items[0].ClearValue(FrameworkElement.MinWidthProperty); view.Layout(); Near(40, view.Flex.ActualWidth);
        view.Contents[0].Width = 90; view.Layout(); Near(90, view.Flex.ActualWidth);
        view.Contents[0].Width = 40; view.Layout(); Near(40, view.Flex.ActualWidth);
        Css.SetStyle(view.Items[0], "width:160px;flex:1 1 0px;"); view.Layout(); Near(160, view.Flex.ActualWidth);
        Css.SetStyle(view.Root, "display:grid;width:600px;grid-template-columns:min-content;"); view.Layout(); Near(160, view.Flex.ActualWidth);
        view.Original();
    }

    [Theory] [InlineData("row", false)] [InlineData("column", false)] [InlineData("row", true)] [InlineData("column", true)]
    public void AnimatedBasisOverridesPercentageInputAndCanBeCleared(string axis, bool native)
    {
        using var view = new Fixture(new("local", axis, axis == "row" ? "max" : "auto", "",
            [axis == "row" ? "width:160px;flex:0 0 50%;" : "height:160px;flex:0 0 50%;"], 40, 40, 40), native);
        view.Layout(); Near(40, axis == "row" ? view.Flex.ActualWidth : view.Flex.ActualHeight);
        view.Items[0].SetAnimatedValue(FlexPanel.BasisProperty, 80d, false); view.Layout();
        Near(80, axis == "row" ? view.Flex.ActualWidth : view.Flex.ActualHeight);
        view.Items[0].ClearAnimatedValue(FlexPanel.BasisProperty); view.Layout();
        Near(40, axis == "row" ? view.Flex.ActualWidth : view.Flex.ActualHeight); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void InlineShrinkToFitSurvivesRepeatedNarrowAndWideConstraints(bool native)
    {
        using var view = new Fixture(new("local", "row", "inline", "gap:12px;", ["flex:1;", "flex:1;"], 40, 40, 92), native);
        foreach (var width in new[] { 600, 20, 130, 600 })
        {
            Css.SetStyle(view.Root, $"display:block;width:{width}px;"); view.Layout(width); Near(92, view.Flex.ActualWidth); view.Original();
        }
    }

    [Theory] [InlineData("min", false)] [InlineData("max", false)] [InlineData("min", true)] [InlineData("max", true)]
    public void RealTextContributionsMatchTheOriginalTextOutsideFlex(string mode, bool native)
    {
        var root = new StackPanel(); Panel flex = native ? new FlexPanel() : new StackPanel(); root.Children.Add(flex);
        var referenceRoot = new StackPanel(); var reference = Word(); referenceRoot.Children.Add(reference);
        var first = Word(); var second = Word(); flex.Children.Add(first); flex.Children.Add(second);
        Css.SetStyle(root, $"display:grid;width:600px;grid-template-columns:{mode}-content;");
        Css.SetStyle(referenceRoot, $"display:grid;width:600px;grid-template-columns:{mode}-content;");
        Css.SetStyle(flex, "display:flex;align-items:flex-start;gap:12px;");
        foreach (var text in new[] { first, second, reference }) Css.SetStyle(text, "min-width:0;flex:1 1 0px;white-space:normal;overflow-wrap:normal;");
        Layout(referenceRoot); Layout(root); Near(reference.ActualWidth * 2 + 12, flex.ActualWidth);
        Assert.InRange(reference.ActualWidth, 20, 200); Assert.Equal("MMMM MMMM", first.Text); Assert.Equal(first.Text, second.Text);
        root.Children.Clear(); flex.Children.Clear(); referenceRoot.Children.Clear();
    }

    [Theory] [InlineData("min", false)] [InlineData("max", false)] [InlineData("min", true)] [InlineData("max", true)]
    public void NestedFlexContainersContributeTheirOwnContents(string mode, bool native)
    {
        var root = new StackPanel(); Panel outer = native ? new FlexPanel() : new StackPanel(); Panel inner = native ? new FlexPanel() : new StackPanel();
        root.Children.Add(outer); outer.Children.Add(inner);
        var first = new Border { Child = new Border { Width = 40, Height = 40 } }; var second = new Border { Child = new Border { Width = 40, Height = 40 } };
        inner.Children.Add(first); inner.Children.Add(second);
        Css.SetStyle(root, $"display:grid;width:600px;grid-template-columns:{mode}-content;");
        Css.SetStyle(outer, "display:flex;align-items:flex-start;"); Css.SetStyle(inner, "display:flex;align-items:flex-start;min-width:0;flex:1 1 0px;");
        foreach (var item in new[] { first, second }) Css.SetStyle(item, "display:block;min-width:0;flex:1 1 0px;");
        Layout(root); Near(80, outer.ActualWidth); Near(80, inner.ActualWidth); Assert.Same(inner, outer.Children[0]); Assert.Same(first, inner.Children[0]);
        inner.Children.Clear(); outer.Children.Clear(); root.Children.Clear();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CollapsedAndPositionedItemsDoNotIncreaseIntrinsicMainSize(bool native)
    {
        using var view = new Fixture(new("local", "row", "max", "gap:12px;", ["width:160px;flex:1;", "width:80px;flex:1;visibility:collapse;", "width:400px;position:absolute;"], 40, 40, 160), native);
        view.Layout(); Near(160, view.Flex.ActualWidth); view.Original();
        Css.SetStyle(view.Items[1], "width:80px;flex:1;"); view.Layout(); Near(252, view.Flex.ActualWidth);
        Css.SetStyle(view.Items[1], "width:80px;flex:1;visibility:collapse;"); view.Layout(); Near(160, view.Flex.ActualWidth); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NegativeOuterContributionsCannotProduceAnInvalidContainerSize(bool native)
    {
        using var view = new Fixture(new("local", "row", "max", "", ["width:10px;flex:none;margin-left:-30px;"], 40, 40, 0), native);
        view.Layout(); Near(0, view.Flex.ActualWidth); Assert.True(double.IsFinite(view.Root.DesiredSize.Height)); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ATableItemRetainsItsNaturalGridInAnIntrinsicFlexContainer(bool native)
    {
        var root = new StackPanel(); Panel flex = native ? new FlexPanel() : new StackPanel(); root.Children.Add(flex);
        var table = new StackPanel(); var row = new StackPanel(); var content = new Border { Width = 40, Height = 40 }; var cell = new Border { Child = content };
        row.Children.Add(cell); table.Children.Add(row); flex.Children.Add(table);
        Css.SetStyle(root, "display:grid;width:600px;grid-template-columns:max-content;"); Css.SetStyle(flex, "display:flex;align-items:flex-start;");
        Css.SetStyle(table, "display:table;width:160px;min-width:0;flex:0 0 content;border:0;padding:0;border-spacing:0;");
        Css.SetStyle(row, "display:table-row;"); Css.SetStyle(cell, "display:table-cell;padding:0;border:0;");
        Layout(root); Near(40, flex.ActualWidth); Near(40, table.ActualWidth); Assert.Same(content, cell.Child);
        row.Children.Clear(); table.Children.Clear(); flex.Children.Clear(); root.Children.Clear();
    }

    [Theory]
    [InlineData("min", false, false)] [InlineData("max", false, false)] [InlineData("min", true, false)] [InlineData("max", true, false)]
    [InlineData("min", false, true)] [InlineData("max", false, true)] [InlineData("min", true, true)] [InlineData("max", true, true)]
    public void ReplacedCrossContributionsKeepTheSourceSizeAndPreferredRatio(string mode, bool native, bool ratio)
    {
        var root = new StackPanel(); Panel flex = native ? new FlexPanel() : new StackPanel(); root.Children.Add(flex);
        var source = new DrawingImage(new GeometryDrawing(Brushes.Green, null, new RectangleGeometry(new Rect(0, 0, 60, 40))));
        var image = new Image { Source = source }; flex.Children.Add(image);
        Css.SetStyle(root, $"display:grid;width:600px;grid-template-columns:{mode}-content;");
        Css.SetStyle(flex, "display:flex;flex-direction:column;align-items:flex-start;height:80px;");
        Css.SetStyle(image, "height:40px;min-width:0;flex:none;" + (ratio ? "aspect-ratio:2;" : ""));
        Layout(root); Near(ratio ? 80 : 60, flex.ActualWidth); Assert.Same(source, image.Source);
        flex.Children.Clear(); root.Children.Clear();
    }

    [Fact]
    public void NativeFlexWithoutADisplayOverrideUsesTheSameIntrinsicContributions()
    {
        var root = new StackPanel(); var flex = new FlexPanel(); var item = new Border { Child = new Border { Width = 40, Height = 40 } };
        root.Children.Add(flex); flex.Children.Add(item);
        Css.SetStyle(root, "display:grid;width:600px;grid-template-columns:max-content;"); Css.SetStyle(item, "width:160px;min-width:0;flex:1;");
        Layout(root); Near(160, flex.ActualWidth); Near(160, item.ActualWidth);
        flex.Children.Clear(); root.Children.Clear();
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(true, false, true)] [InlineData(false, true, true)] [InlineData(true, true, true)]
    public void GridNormalAlignmentRetainsAutoStretchInEitherDirection(bool native, bool fixedWidth, bool rtl)
    {
        using var view = new Fixture(new("local", "row", "max", "gap:12px;" + (fixedWidth ? "width:100px;" : ""),
            ["width:168px;flex:1;", "width:112px;flex:1;"], 40, 40, fixedWidth ? 100 : 300), native);
        Css.SetStyle(view.Root, $"display:grid;width:600px;grid-template-columns:300px;direction:{(rtl ? "rtl" : "ltr")};");
        view.Layout(); var expected = fixedWidth ? 100 : 300;
        Near(expected, view.Flex.ActualWidth); Near(rtl ? 600 - expected : 0, view.Flex.TransformToAncestor(view.Root).X);
        Near((expected - 12) / 2d, view.Items[0].ActualWidth); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AFailedIntrinsicMeasureRestoresTheItemContext(bool native)
    {
        var root = new StackPanel(); Panel flex = native ? new FlexPanel() : new StackPanel(); var item = new ThrowOnMinimum(); root.Children.Add(flex); flex.Children.Add(item);
        Css.SetStyle(root, "display:grid;width:600px;grid-template-columns:min-content;"); Css.SetStyle(flex, "display:flex;"); Css.SetStyle(item, "width:160px;min-width:0;flex:1;");
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Throws<InvalidOperationException>(() => root.Measure(new(600, double.PositiveInfinity)));
        item.Fail = false; flex.Children.Remove(item); var holder = new Border { Child = item }; Css.SetStyle(item, "width:200px;height:40px;");
        Layout(holder); Near(200, item.ActualWidth); holder.Child = null; flex.Children.Clear(); root.Children.Clear();
    }

    private sealed class ThrowOnMinimum : FrameworkElement
    {
        internal bool Fail = true;
        protected override Size MeasureOverride(Size availableSize)
        { if (Fail && availableSize.Width == 0) throw new InvalidOperationException("intrinsic contribution probe"); return new(40, 40); }
    }
    private static TextBlock Word() => new() { Text = "MMMM MMMM", FontFamily = "Menlo", FontSize = 16, LineHeight = 24 };
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - .055, expected + .055);
    private static void Layout(FrameworkElement root, double width = 600)
    { for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); root.Measure(new(width, double.PositiveInfinity)); root.Arrange(new(0, 0, width, root.DesiredSize.Height)); root.UpdateLayout(); } }

    private sealed class Fixture : IDisposable
    {
        internal readonly SpecCase Case;
        internal readonly StackPanel Root = new();
        internal readonly Panel Flex;
        internal readonly List<Border> Items = [], Contents = [];
        internal Fixture(SpecCase c, bool native)
        {
            Case = c; Flex = native ? new FlexPanel() : new StackPanel(); Root.Children.Add(Flex);
            for (var i = 0; i < c.Items.Length; i++)
            {
                var content = new Border { Width = c.Widths?[i] ?? c.Cw, Height = c.Ch }; var item = new Border { Child = content };
                Css.SetClass(item, "item"); Css.SetStyle(item, c.Items[i]); Flex.Children.Add(item); Items.Add(item); Contents.Add(content);
            }
            Css.SetStyleSheet(Root, ".item{display:block;min-width:0;min-height:0;box-sizing:border-box;}");
            Css.SetStyle(Root, "width:600px;" + (c.Mode is "min" or "max" ? $"display:grid;grid-template-columns:{c.Mode}-content;" : "display:block;"));
            Css.SetStyle(Flex, $"display:{(c.Mode == "inline" ? "inline-flex" : "flex")};flex-direction:{c.Axis};align-items:flex-start;" + (c.Mode == "float" ? "float:left;" : "") + (c.Axis == "row" ? "height:60px;" : "width:100px;") + c.Flex);
        }
        internal void Layout(double width = 600) => CssFlexIntrinsicSizingTests.Layout(Root, width);
        internal void Original()
        {
            Assert.Equal(Items.Count, Flex.Children.Count);
            for (var i = 0; i < Items.Count; i++) { Assert.Same(Items[i], Flex.Children[i]); Assert.Same(Contents[i], Items[i].Child); }
        }
        public void Dispose() { Flex.Children.Clear(); Root.Children.Clear(); }
    }
    private sealed record SpecCase(string Name, string Axis, string Mode, string Flex, string[] Items, int Cw, int Ch, double Main, int[]? Widths = null, double Cross = double.NaN);
    // MATRIX START
    private static readonly SpecCase[] Cases =
    [
        new("row-min-zero-grow-preferred", "row", "min", "", ["width:160px;flex:1;"], 40, 40, 160, null, double.NaN),
        new("row-min-zero-inflexible", "row", "min", "", ["width:160px;flex:0 0 0px;"], 40, 40, 0, null, double.NaN),
        new("row-min-zero-percent-inflexible", "row", "min", "", ["width:160px;flex:0 0 0%;"], 40, 40, 40, null, double.NaN),
        new("row-min-percent-inflexible", "row", "min", "", ["width:160px;flex:0 0 50%;"], 40, 40, 40, null, double.NaN),
        new("row-min-math-percent-inflexible", "row", "min", "", ["width:160px;flex:0 0 calc(50% + 10px);"], 40, 40, 40, null, double.NaN),
        new("row-min-auto-preferred", "row", "min", "", ["width:160px;flex:none;"], 40, 40, 160, null, double.NaN),
        new("row-min-content-inflexible", "row", "min", "", ["width:160px;flex:0 0 content;"], 40, 40, 40, null, double.NaN),
        new("row-min-basis-shrink", "row", "min", "", ["flex:1 1 80px;"], 40, 40, 40, null, double.NaN),
        new("row-min-basis-no-shrink", "row", "min", "", ["flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-min-basis-no-grow", "row", "min", "", ["flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-min-basis-grow", "row", "min", "", ["flex:1 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-min-partial-grow", "row", "min", "", ["flex:.25 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-min-partial-shrink", "row", "min", "", ["flex:1 .25 80px;"], 40, 40, 40, null, double.NaN),
        new("row-min-explicit-min", "row", "min", "", ["min-width:90px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-min-explicit-max", "row", "min", "", ["max-width:30px;flex:1 1 80px;"], 40, 40, 30, null, double.NaN),
        new("row-min-min-over-max", "row", "min", "", ["min-width:90px;max-width:30px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-min-two-preferred", "row", "min", "", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 240, null, double.NaN),
        new("row-min-two-content", "row", "min", "", ["flex:1;", "flex:1;"], 40, 40, 80, null, double.NaN),
        new("row-min-no-grow-preferred", "row", "min", "", ["width:160px;flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-min-no-shrink-preferred", "row", "min", "", ["width:10px;flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-max-zero-grow-preferred", "row", "max", "", ["width:160px;flex:1;"], 40, 40, 160, null, double.NaN),
        new("row-max-zero-inflexible", "row", "max", "", ["width:160px;flex:0 0 0px;"], 40, 40, 0, null, double.NaN),
        new("row-max-zero-percent-inflexible", "row", "max", "", ["width:160px;flex:0 0 0%;"], 40, 40, 40, null, double.NaN),
        new("row-max-percent-inflexible", "row", "max", "", ["width:160px;flex:0 0 50%;"], 40, 40, 40, null, double.NaN),
        new("row-max-math-percent-inflexible", "row", "max", "", ["width:160px;flex:0 0 calc(50% + 10px);"], 40, 40, 40, null, double.NaN),
        new("row-max-auto-preferred", "row", "max", "", ["width:160px;flex:none;"], 40, 40, 160, null, double.NaN),
        new("row-max-content-inflexible", "row", "max", "", ["width:160px;flex:0 0 content;"], 40, 40, 40, null, double.NaN),
        new("row-max-basis-shrink", "row", "max", "", ["flex:1 1 80px;"], 40, 40, 40, null, double.NaN),
        new("row-max-basis-no-shrink", "row", "max", "", ["flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-max-basis-no-grow", "row", "max", "", ["flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-max-basis-grow", "row", "max", "", ["flex:1 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-max-partial-grow", "row", "max", "", ["flex:.25 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-max-partial-shrink", "row", "max", "", ["flex:1 .25 80px;"], 40, 40, 40, null, double.NaN),
        new("row-max-explicit-min", "row", "max", "", ["min-width:90px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-max-explicit-max", "row", "max", "", ["max-width:30px;flex:1 1 80px;"], 40, 40, 30, null, double.NaN),
        new("row-max-min-over-max", "row", "max", "", ["min-width:90px;max-width:30px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-max-two-preferred", "row", "max", "", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 240, null, double.NaN),
        new("row-max-two-content", "row", "max", "", ["flex:1;", "flex:1;"], 40, 40, 80, null, double.NaN),
        new("row-max-no-grow-preferred", "row", "max", "", ["width:160px;flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-max-no-shrink-preferred", "row", "max", "", ["width:10px;flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-inline-zero-grow-preferred", "row", "inline", "", ["width:160px;flex:1;"], 40, 40, 160, null, double.NaN),
        new("row-inline-zero-inflexible", "row", "inline", "", ["width:160px;flex:0 0 0px;"], 40, 40, 0, null, double.NaN),
        new("row-inline-zero-percent-inflexible", "row", "inline", "", ["width:160px;flex:0 0 0%;"], 40, 40, 40, null, double.NaN),
        new("row-inline-percent-inflexible", "row", "inline", "", ["width:160px;flex:0 0 50%;"], 40, 40, 40, null, double.NaN),
        new("row-inline-math-percent-inflexible", "row", "inline", "", ["width:160px;flex:0 0 calc(50% + 10px);"], 40, 40, 40, null, double.NaN),
        new("row-inline-auto-preferred", "row", "inline", "", ["width:160px;flex:none;"], 40, 40, 160, null, double.NaN),
        new("row-inline-content-inflexible", "row", "inline", "", ["width:160px;flex:0 0 content;"], 40, 40, 40, null, double.NaN),
        new("row-inline-basis-shrink", "row", "inline", "", ["flex:1 1 80px;"], 40, 40, 40, null, double.NaN),
        new("row-inline-basis-no-shrink", "row", "inline", "", ["flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-inline-basis-no-grow", "row", "inline", "", ["flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-inline-basis-grow", "row", "inline", "", ["flex:1 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-inline-partial-grow", "row", "inline", "", ["flex:.25 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-inline-partial-shrink", "row", "inline", "", ["flex:1 .25 80px;"], 40, 40, 40, null, double.NaN),
        new("row-inline-explicit-min", "row", "inline", "", ["min-width:90px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-inline-explicit-max", "row", "inline", "", ["max-width:30px;flex:1 1 80px;"], 40, 40, 30, null, double.NaN),
        new("row-inline-min-over-max", "row", "inline", "", ["min-width:90px;max-width:30px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-inline-two-preferred", "row", "inline", "", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 240, null, double.NaN),
        new("row-inline-two-content", "row", "inline", "", ["flex:1;", "flex:1;"], 40, 40, 80, null, double.NaN),
        new("row-inline-no-grow-preferred", "row", "inline", "", ["width:160px;flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-inline-no-shrink-preferred", "row", "inline", "", ["width:10px;flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-float-zero-grow-preferred", "row", "float", "", ["width:160px;flex:1;"], 40, 40, 160, null, double.NaN),
        new("row-float-zero-inflexible", "row", "float", "", ["width:160px;flex:0 0 0px;"], 40, 40, 0, null, double.NaN),
        new("row-float-zero-percent-inflexible", "row", "float", "", ["width:160px;flex:0 0 0%;"], 40, 40, 40, null, double.NaN),
        new("row-float-percent-inflexible", "row", "float", "", ["width:160px;flex:0 0 50%;"], 40, 40, 40, null, double.NaN),
        new("row-float-math-percent-inflexible", "row", "float", "", ["width:160px;flex:0 0 calc(50% + 10px);"], 40, 40, 40, null, double.NaN),
        new("row-float-auto-preferred", "row", "float", "", ["width:160px;flex:none;"], 40, 40, 160, null, double.NaN),
        new("row-float-content-inflexible", "row", "float", "", ["width:160px;flex:0 0 content;"], 40, 40, 40, null, double.NaN),
        new("row-float-basis-shrink", "row", "float", "", ["flex:1 1 80px;"], 40, 40, 40, null, double.NaN),
        new("row-float-basis-no-shrink", "row", "float", "", ["flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-float-basis-no-grow", "row", "float", "", ["flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-float-basis-grow", "row", "float", "", ["flex:1 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-float-partial-grow", "row", "float", "", ["flex:.25 1 10px;"], 40, 40, 40, null, double.NaN),
        new("row-float-partial-shrink", "row", "float", "", ["flex:1 .25 80px;"], 40, 40, 40, null, double.NaN),
        new("row-float-explicit-min", "row", "float", "", ["min-width:90px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-float-explicit-max", "row", "float", "", ["max-width:30px;flex:1 1 80px;"], 40, 40, 30, null, double.NaN),
        new("row-float-min-over-max", "row", "float", "", ["min-width:90px;max-width:30px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("row-float-two-preferred", "row", "float", "", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 240, null, double.NaN),
        new("row-float-two-content", "row", "float", "", ["flex:1;", "flex:1;"], 40, 40, 80, null, double.NaN),
        new("row-float-no-grow-preferred", "row", "float", "", ["width:160px;flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("row-float-no-shrink-preferred", "row", "float", "", ["width:10px;flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("column-auto-zero-grow-preferred", "column", "auto", "", ["height:160px;flex:1 1 0px;"], 40, 40, 0, null, double.NaN),
        new("column-auto-zero-inflexible", "column", "auto", "", ["height:160px;flex:0 0 0px;"], 40, 40, 0, null, double.NaN),
        new("column-auto-zero-percent-inflexible", "column", "auto", "", ["height:160px;flex:0 0 0%;"], 40, 40, 40, null, double.NaN),
        new("column-auto-percent-inflexible", "column", "auto", "", ["height:160px;flex:0 0 50%;"], 40, 40, 40, null, double.NaN),
        new("column-auto-math-percent-inflexible", "column", "auto", "", ["height:160px;flex:0 0 calc(50% + 10px);"], 40, 40, 40, null, double.NaN),
        new("column-auto-auto-preferred", "column", "auto", "", ["height:160px;flex:none;"], 40, 40, 160, null, double.NaN),
        new("column-auto-content-inflexible", "column", "auto", "", ["height:160px;flex:0 0 content;"], 40, 40, 40, null, double.NaN),
        new("column-auto-basis-shrink", "column", "auto", "", ["flex:1 1 80px;"], 40, 40, 80, null, double.NaN),
        new("column-auto-basis-no-shrink", "column", "auto", "", ["flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("column-auto-basis-no-grow", "column", "auto", "", ["flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("column-auto-basis-grow", "column", "auto", "", ["flex:1 1 10px;"], 40, 40, 10, null, double.NaN),
        new("column-auto-partial-grow", "column", "auto", "", ["flex:.25 1 10px;"], 40, 40, 10, null, double.NaN),
        new("column-auto-partial-shrink", "column", "auto", "", ["flex:1 .25 80px;"], 40, 40, 80, null, double.NaN),
        new("column-auto-explicit-min", "column", "auto", "", ["min-height:90px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("column-auto-explicit-max", "column", "auto", "", ["max-height:30px;flex:1 1 80px;"], 40, 40, 30, null, double.NaN),
        new("column-auto-min-over-max", "column", "auto", "", ["min-height:90px;max-height:30px;flex:1 1 10px;"], 40, 40, 90, null, double.NaN),
        new("column-auto-two-preferred", "column", "auto", "", ["height:160px;flex:1 1 0px;", "height:80px;flex:1 1 0px;"], 40, 40, 0, null, double.NaN),
        new("column-auto-two-content", "column", "auto", "", ["flex:1 1 0px;", "flex:1 1 0px;"], 40, 40, 0, null, double.NaN),
        new("column-auto-no-grow-preferred", "column", "auto", "", ["height:160px;flex:0 1 10px;"], 40, 40, 10, null, double.NaN),
        new("column-auto-no-shrink-preferred", "column", "auto", "", ["height:10px;flex:1 0 80px;"], 40, 40, 80, null, double.NaN),
        new("row-min-wrap", "row", "min", "flex-wrap:wrap;gap:12px;", ["flex:1;", "flex:1;"], 40, 40, 40, null, double.NaN),
        new("row-max-wrap", "row", "max", "flex-wrap:wrap;gap:12px;", ["flex:1;", "flex:1;"], 40, 40, 92, null, double.NaN),
        new("row-inline-wrap", "row", "inline", "flex-wrap:wrap;gap:12px;", ["flex:1;", "flex:1;"], 40, 40, 92, null, double.NaN),
        new("row-min-gap", "row", "min", "gap:12px;", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 252, null, double.NaN),
        new("row-min-chrome", "row", "min", "", ["padding:5px;border:2px solid #146342;flex:1 1 0px;"], 40, 40, 54, null, double.NaN),
        new("row-min-margin", "row", "min", "", ["margin:0 7px 0 5px;flex:1 1 0px;"], 40, 40, 52, null, double.NaN),
        new("row-max-gap", "row", "max", "gap:12px;", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 252, null, double.NaN),
        new("row-max-chrome", "row", "max", "", ["padding:5px;border:2px solid #146342;flex:1 1 0px;"], 40, 40, 54, null, double.NaN),
        new("row-max-margin", "row", "max", "", ["margin:0 7px 0 5px;flex:1 1 0px;"], 40, 40, 52, null, double.NaN),
        new("row-inline-gap", "row", "inline", "gap:12px;", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 252, null, double.NaN),
        new("row-inline-chrome", "row", "inline", "", ["padding:5px;border:2px solid #146342;flex:1 1 0px;"], 40, 40, 54, null, double.NaN),
        new("row-inline-margin", "row", "inline", "", ["margin:0 7px 0 5px;flex:1 1 0px;"], 40, 40, 52, null, double.NaN),
        new("row-float-gap", "row", "float", "gap:12px;", ["width:160px;flex:1;", "width:80px;flex:1;"], 40, 40, 252, null, double.NaN),
        new("row-float-chrome", "row", "float", "", ["padding:5px;border:2px solid #146342;flex:1 1 0px;"], 40, 40, 54, null, double.NaN),
        new("row-float-margin", "row", "float", "", ["margin:0 7px 0 5px;flex:1 1 0px;"], 40, 40, 52, null, double.NaN),
        new("column-min-cross-nowrap", "column", "min", "width:auto;", ["flex:none;", "flex:none;", "flex:none;"], 40, 40, 120, [40, 80, 120], 120),
        new("column-max-cross-nowrap", "column", "max", "width:auto;", ["flex:none;", "flex:none;", "flex:none;"], 40, 40, 120, [40, 80, 120], 120),
        new("column-min-cross-wrap", "column", "min", "width:auto;height:80px;flex-wrap:wrap;gap:12px;", ["flex:none;", "flex:none;", "flex:none;"], 40, 40, 80, [40, 80, 120], 120),
        new("column-max-cross-wrap", "column", "max", "width:auto;height:80px;flex-wrap:wrap;gap:12px;", ["flex:none;", "flex:none;", "flex:none;"], 40, 40, 80, [40, 80, 120], 264),
    ];
    // MATRIX END
}
