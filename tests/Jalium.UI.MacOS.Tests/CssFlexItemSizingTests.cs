using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFlexItemSizingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    public static IEnumerable<object[]> GeometryCases()
    { foreach (var c in Cases) foreach (var native in new[] { false, true }) yield return [c.Name, native]; }
    public static IEnumerable<object[]> AxisModes()
    { foreach (var axis in new[] { "row", "column" }) foreach (var native in new[] { false, true }) yield return [axis, native]; }

    [Theory] [MemberData(nameof(GeometryCases))]
    public void FlexibleMainSizesRespectBasesBoundsMarginsAndContent(string name, bool native)
    {
        var c = Cases.Single(c => c.Name == name); using var view = new Fixture(c, native); view.Layout();
        for (var i = 0; i < view.Items.Count; i++)
        {
            var item = view.Items[i]; var at = item.TransformToAncestor(view.Root); var expected = c.Expected[i];
            Near(expected.Width, item.ActualWidth); Near(expected.Height, item.ActualHeight); Near(expected.Left, at.X); Near(expected.Top, at.Y);
            Assert.True(item.IsMeasureValid); Assert.True(item.IsArrangeValid);
        }
        view.Original();
    }

    [Theory] [MemberData(nameof(AxisModes))]
    public void NativePreferredMainSizeRemainsTheInputToAutoBasis(string axis, bool native)
    {
        var row = axis == "row"; var dimension = row ? "width" : "height";
        using var view = new Fixture(Make(axis, [$"{dimension}:400px;", $"{dimension}:200px;"]), native);
        var property = row ? FrameworkElement.WidthProperty : FrameworkElement.HeightProperty;
        view.Items[0].SetValue(property, 200d); view.Layout();
        Near(row ? 150 : 100, Main(view.Items[0], row)); Near(row ? 150 : 100, Main(view.Items[1], row)); Assert.Equal(200d, view.Items[0].GetValue(property));
        view.Items[0].ClearValue(property); view.Layout(); Near(row ? 200 : 400d / 3, Main(view.Items[0], row)); Near(row ? 100 : 200d / 3, Main(view.Items[1], row)); view.Original();
    }

    [Theory] [MemberData(nameof(AxisModes))]
    public void ContentBasisAndSameValuedNativeAutoCanBeRestored(string axis, bool native)
    {
        var row = axis == "row"; var dimension = row ? "width" : "height";
        using var view = new Fixture(Make(axis, [$"{dimension}:160px;flex:0 0 content;"]), native);
        view.Layout(); Near(40, Main(view.Items[0], row));
        FlexPanel.SetBasis(view.Items[0], double.NaN); view.Layout(); Near(160, Main(view.Items[0], row));
        view.Items[0].ClearValue(FlexPanel.BasisProperty); view.Layout(); Near(40, Main(view.Items[0], row));
        FlexPanel.SetBasis(view.Items[0], 80); view.Layout(); Near(80, Main(view.Items[0], row));
        view.Items[0].ClearValue(FlexPanel.BasisProperty); view.Layout(); Near(40, Main(view.Items[0], row));
        view.Style(0, $"{dimension}:160px;flex:none;"); view.Layout(); Near(160, Main(view.Items[0], row));
        view.Style(0, $"{dimension}:160px;flex-basis:content;flex-shrink:0;"); view.Layout(); Near(40, Main(view.Items[0], row)); view.Original();
    }

    [Theory] [MemberData(nameof(AxisModes))]
    public void ExplicitZeroMinimumOverridesAnEqualAutomaticMinimumSentinel(string axis, bool native)
    {
        var row = axis == "row"; var dimension = row ? "width" : "height";
        var c = Make(axis, [$"{dimension}:160px;min-{dimension}:auto;"]) with { Width = row ? 80 : 300, Height = row ? 200 : 80, Cw = row ? 120 : 40, Ch = row ? 40 : 120 };
        using var view = new Fixture(c, native); var property = row ? FrameworkElement.MinWidthProperty : FrameworkElement.MinHeightProperty;
        view.Layout(); Near(120, Main(view.Items[0], row));
        view.Items[0].SetValue(property, 0d); view.Layout(); Near(80, Main(view.Items[0], row));
        view.Items[0].ClearValue(property); view.Layout(); Near(120, Main(view.Items[0], row)); view.Original();
    }

    [Theory] [MemberData(nameof(AxisModes))]
    public void ResizingRestoresUsedSizesAndAFormerFlexItemRecoversItsAuthorSize(string axis, bool native)
    {
        var row = axis == "row"; var dimension = row ? "width" : "height";
        using var view = new Fixture(Make(axis, [$"{dimension}:400px;", $"{dimension}:200px;"]), native);
        foreach (var size in new[] { 300, 180, 420, 300 })
        {
            view.Resize(row ? size : 300, row ? 200 : size); view.Layout();
            Near(size * 2d / 3, Main(view.Items[0], row)); Near(size / 3d, Main(view.Items[1], row)); view.Original();
        }
        var item = view.Items[0]; view.Root.Children.Remove(item); var border = new Border { Child = item }; Layout(border, 600, 600);
        Near(400, Main(item, row)); border.Child = null; view.Root.Children.Insert(0, item); view.Layout(); Near(200, Main(item, row)); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ZeroLengthAndPercentageBasesRemainDistinctOnAnIndefiniteAxis(bool native)
    {
        using var view = new Fixture(Make("column", ["height:160px;flex:1;"]), native);
        Css.SetStyle(view.Root, "display:flex;flex-direction:column;align-items:flex-start;width:300px;");
        LayoutIndefinite(view.Root); Near(0, view.Root.DesiredSize.Height);
        view.Style(0, "height:160px;flex:1 1 0px;"); LayoutIndefinite(view.Root); Near(0, view.Root.DesiredSize.Height);
        // An unresolved percentage basis is content, not auto: the original
        // 40px child determines the basis rather than the preferred 160px height.
        view.Style(0, "height:160px;flex:1 1 0%;"); LayoutIndefinite(view.Root); Near(40, view.Root.DesiredSize.Height); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TablesUseTheirFlexAllocationWhileRetainingNaturalContentMinima(bool native)
    {
        // WPT table-as-item-specified-height: 500px is a preferred height, not an extra minimum.
        using var view = new Fixture(Make("column", ["height:500px;flex:0 0 100px;"]) with { Kind = "table" }, native);
        view.Layout(); Near(100, view.Items[0].ActualHeight);
        // The table's natural 50px row cannot shrink away even with an explicit zero minimum.
        using var small = new Fixture(Make("column", ["height:auto;", "height:50px;flex:none;"]) with { Kind = "table", Ch = 50, Height = 0 }, native);
        small.Layout(); Near(50, small.Items[0].ActualHeight); Near(50, small.Items[1].TransformToAncestor(small.Root).Y);
        view.Original(); small.Original();
    }

    [Fact]
    public void ContentKeywordIsAvailableToAuthoring()
    { Assert.Contains("content", CssAuthoring.GetValues("flex-basis")); }

    public static IEnumerable<object[]> InvalidShorthands()
    { foreach (var axis in new[] { "row", "column" }) foreach (var native in new[] { false, true }) foreach (var invalid in new[] { "0 content 0", "1 0% 1", "2 80px 1", "0 auto 0" }) yield return [axis, native, invalid]; }

    [Theory] [MemberData(nameof(InvalidShorthands))]
    public void InvalidBasisPlacementRetainsTheCascadedValue(string axis, bool native, string invalid)
    {
        var row = axis == "row"; var dimension = row ? "width" : "height";
        using var view = new Fixture(Make(axis, [$"{dimension}:160px;flex:{invalid};"]), native);
        Css.SetStyleSheet(view.Root, ".item{display:block;min-width:0;min-height:0;flex:0 0 80px;}"); view.Layout(); Near(80, Main(view.Items[0], row));
        view.Style(0, $"{dimension}:160px;flex:content 0 0;"); view.Layout(); Near(40, Main(view.Items[0], row));
        view.Style(0, $"{dimension}:160px;flex:{invalid};"); view.Layout(); Near(80, Main(view.Items[0], row)); view.Original();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AutomaticMinimumMeasuresTheOriginalUnbreakableText(bool native)
    {
        Panel root = native ? new FlexPanel() : new StackPanel(); var text = new TextBlock { Text = "unbreakableOriginalWord", FontFamily = "Menlo", FontSize = 16, LineHeight = 24 }; root.Children.Add(text);
        Css.SetStyle(root, "display:flex;width:50px;height:100px;align-items:flex-start;");
        Css.SetStyle(text, "width:400px;min-width:auto;white-space:normal;overflow-wrap:normal;"); Layout(root, 50, 100); Assert.InRange(text.ActualWidth, 100, 300);
        Css.SetStyle(text, "width:400px;min-width:0;white-space:normal;overflow-wrap:normal;"); Layout(root, 50, 100); Near(50, text.ActualWidth); Assert.Equal("unbreakableOriginalWord", text.Text); root.Children.Clear();
    }

    [Theory] [MemberData(nameof(AxisModes))]
    public void ReplacedAutomaticMinimumUsesTheSmallerContentSuggestion(string axis, bool native)
    {
        var row = axis == "row"; Panel root = native ? new FlexPanel() : new StackPanel();
        var source = new DrawingImage(new GeometryDrawing(Brushes.Green, null, new RectangleGeometry(new Rect(0, 0, 40, 40))));
        var image = new Image { Source = source }; root.Children.Add(image);
        Css.SetStyle(root, $"display:flex;align-items:flex-start;flex-direction:{axis};width:{(row ? 80 : 300)}px;height:{(row ? 200 : 45)}px;");
        Css.SetStyle(image, row ? "width:160px;height:60px;min-width:auto;aspect-ratio:2;" : "height:160px;width:100px;min-height:auto;aspect-ratio:2;");
        Layout(root, row ? 80 : 300, row ? 200 : 45); Near(row ? 80 : 45, Main(image, row)); Assert.Same(source, image.Source);
        root.Children.Clear();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AFailedUsedSizeMeasureRestoresTheNativeLayoutContext(bool native)
    {
        Panel root = native ? new FlexPanel() : new StackPanel(); var item = new ThrowAtAllocation(); var sibling = new Border(); root.Children.Add(item); root.Children.Add(sibling);
        Css.SetStyle(root, "display:flex;width:300px;height:200px;"); Css.SetStyle(item, "width:200px;height:40px;min-width:0;"); Css.SetStyle(sibling, "width:200px;height:40px;min-width:0;");
        Dispatcher.CurrentDispatcher.ProcessQueue(); Assert.Throws<InvalidOperationException>(() => root.Measure(new(300, 200)));
        item.Fail = false; root.Children.Remove(item); var holder = new Border { Child = item }; Css.SetStyle(item, "width:250px;height:40px;"); Layout(holder, 600, 600); Near(250, item.ActualWidth);
        holder.Child = null; root.Children.Clear();
    }

    private sealed class ThrowAtAllocation : FrameworkElement
    {
        internal bool Fail = true;
        protected override Size MeasureOverride(Size availableSize)
        { if (Fail && Math.Abs(availableSize.Width - 150) < .01) throw new InvalidOperationException("allocation probe"); return new(10, 40); }
    }

    private static double Main(FrameworkElement item, bool row) => row ? item.ActualWidth : item.ActualHeight;
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - .055, expected + .055);
    private static void Layout(FrameworkElement root, double width, double height)
    { for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
    private static void LayoutIndefinite(FrameworkElement root)
    { for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); root.Measure(new(300, double.PositiveInfinity)); } }
    private static ReferenceCase Make(string axis, string[] items) => new("local", axis, 300, 200, "", "block", 40, 40, items, []);

    private sealed class Fixture : IDisposable
    {
        internal readonly Panel Root;
        internal readonly List<FrameworkElement> Items = [];
        private readonly List<TextBlock> _labels = [];
        private readonly List<Border> _contents = [];
        private readonly ReferenceCase _case;
        private int _width, _height;
        internal Fixture(ReferenceCase c, bool native)
        {
            _case = c; _width = c.Width; _height = c.Height; Root = native ? new FlexPanel() : new StackPanel();
            foreach (var style in c.Items)
            {
                var label = new TextBlock { Text = "原文", FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24 }; _labels.Add(label);
                var content = new Border { Width = c.Cw, Height = c.Ch, Child = label }; _contents.Add(content); FrameworkElement item;
                if (c.Kind == "table") { var table = new StackPanel(); var row = new StackPanel(); Css.SetClass(row, "row"); table.Children.Add(row); var cell = new Border { Child = content }; Css.SetClass(cell, "cell"); row.Children.Add(cell); item = table; Css.SetClass(item, "item table"); }
                else { item = new Border { Child = content }; Css.SetClass(item, "item"); }
                Items.Add(item); Root.Children.Add(item); Style(Items.Count - 1, style);
            }
            Css.SetStyleSheet(Root, ".item{display:block;min-width:0;min-height:0;box-sizing:border-box;}.table{display:table;border:0;padding:0;border-spacing:0;}.row{display:table-row;height:40px;}.cell{display:table-cell;padding:0;border:0;}");
            Resize(c.Width, c.Height);
        }
        internal void Style(int index, string style) => Css.SetStyle(Items[index], (_case.Axis.StartsWith("row") ? "height:60px;" : "width:100px;") + style);
        internal void Resize(int width, int height) { _width = width; _height = height; Css.SetStyle(Root, $"display:flex;align-items:flex-start;width:{width}px;height:{height}px;flex-direction:{_case.Axis};" + _case.Host); }
        internal void Layout() => CssFlexItemSizingTests.Layout(Root, _width, _height);
        internal void Original() { Assert.Equal(Items, Root.Children.Cast<FrameworkElement>()); for (var i = 0; i < _labels.Count; i++) { Assert.Same(_labels[i], _contents[i].Child); Assert.Equal("原文", _labels[i].Text); } }
        public void Dispose() => Root.Children.Clear();
    }

    private sealed record ExpectedBox(double Width, double Height, double Left, double Top);
    private sealed record ReferenceCase(string Name, string Axis, int Width, int Height, string Host, string Kind, int Cw, int Ch, string[] Items, ExpectedBox[] Expected);
    // CSS Flexbox 9.2/9.7 and the referenced WPT table-item sizing contracts.
    private static readonly ReferenceCase[] Cases =
    [
        new("row-shrink", "row", 300, 200, "", "block", 40, 40, ["width:400px;"], [new(300, 60, 0, 0)]),
        new("row-grow", "row", 300, 200, "", "block", 40, 40, ["width:40px;flex-grow:1;"], [new(300, 60, 0, 0)]),
        new("row-two", "row", 300, 200, "", "block", 40, 40, ["width:200px;", "width:200px;"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-weighted", "row", 300, 200, "", "block", 40, 40, ["width:400px;", "width:200px;"], [new(200, 60, 0, 0), new(100, 60, 200, 0)]),
        new("row-none", "row", 300, 200, "", "block", 40, 40, ["width:400px;flex:none;"], [new(400, 60, 0, 0)]),
        new("row-fractional-grow", "row", 300, 200, "", "block", 40, 40, ["width:40px;flex-grow:.25;", "width:40px;flex-grow:.25;"], [new(95, 60, 0, 0), new(95, 60, 95, 0)]),
        new("row-max-base", "row", 300, 200, "", "block", 40, 40, ["flex-basis:400px;max-width:300px;", "flex-basis:400px;"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-min-base", "row", 300, 200, "", "block", 40, 40, ["flex-basis:40px;min-width:100px;flex-grow:1;", "flex-basis:40px;flex-grow:1;"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-content", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex:0 0 content;"], [new(40, 60, 0, 0)]),
        new("row-content-longhand", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex-basis:content;flex-shrink:0;"], [new(40, 60, 0, 0)]),
        new("row-basis-fixed", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex:1 1 80px;", "width:160px;flex:1 1 80px;"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-basis-percent", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex:1 1 25%;", "width:160px;flex:1 1 25%;"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-basis-calc", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex:1 1 calc(25% + 10px);", "width:160px;flex:1 1 calc(25% + 10px);"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-margin", "row", 300, 200, "", "block", 40, 40, ["width:200px;margin:10px;", "width:200px;"], [new(140, 60, 10, 10), new(140, 60, 160, 0)]),
        new("row-content-box", "row", 300, 200, "", "block", 40, 40, ["flex:1 1 100px;box-sizing:content-box;padding:10px;border:2px solid green;", "flex:1 1 100px;"], [new(162, 84, 0, 0), new(138, 60, 162, 0)]),
        new("row-border-box-shrink", "row", 300, 200, "", "block", 40, 40, ["flex-basis:200px;padding:10px;border:2px solid green;", "flex-basis:200px;"], [new(153.1875, 60, 0, 0), new(146.8125, 60, 153.1875, 0)]),
        new("row-percent-min", "row", 300, 200, "", "block", 40, 40, ["width:40px;min-width:60%;flex:1 1 auto;", "width:40px;flex:1 1 auto;"], [new(180, 60, 0, 0), new(120, 60, 180, 0)]),
        new("row-percent-max", "row", 300, 200, "", "block", 40, 40, ["width:40px;max-width:50%;flex:2 1 auto;", "width:40px;flex:1 1 auto;"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-auto-min", "row", 80, 200, "", "block", 120, 40, ["width:160px;min-width:auto;"], [new(120, 60, 0, 0)]),
        new("row-clip-min", "row", 80, 200, "", "block", 120, 40, ["width:160px;min-width:auto;overflow:clip;"], [new(120, 60, 0, 0)]),
        new("row-hidden-min", "row", 80, 200, "", "block", 120, 40, ["width:160px;min-width:auto;overflow:hidden;"], [new(80, 60, 0, 0)]),
        new("row-auto-overflow-min", "row", 80, 200, "", "block", 120, 40, ["width:160px;min-width:auto;overflow:auto;"], [new(80, 60, 0, 0)]),
        new("row-specified-min-cap", "row", 40, 200, "", "block", 120, 40, ["width:60px;min-width:auto;"], [new(60, 60, 0, 0)]),
        new("row-explicit-min-zero", "row", 40, 200, "", "block", 120, 40, ["width:60px;min-width:0;"], [new(40, 60, 0, 0)]),
        new("row-table-shrink", "row", 300, 200, "", "table", 40, 40, ["width:400px;"], [new(300, 60, 0, 0)]),
        new("row-table-grow", "row", 300, 200, "", "table", 40, 40, ["width:40px;flex:1 1 auto;"], [new(300, 60, 0, 0)]),
        new("row-table-content", "row", 300, 200, "", "table", 40, 40, ["width:160px;flex:0 0 content;"], [new(40, 60, 0, 0)]),
        new("row-table-two", "row", 300, 200, "", "table", 40, 40, ["width:200px;", "width:200px;"], [new(150, 60, 0, 0), new(150, 60, 150, 0)]),
        new("row-reverse", "row-reverse", 300, 200, "", "block", 40, 40, ["width:200px;", "width:200px;"], [new(150, 60, 150, 0), new(150, 60, 0, 0)]),
        new("row-rtl", "row", 300, 200, "direction:rtl;", "block", 40, 40, ["width:200px;", "width:200px;"], [new(150, 60, 150, 0), new(150, 60, 0, 0)]),
        new("row-gap", "row", 300, 200, "gap:12px;", "block", 40, 40, ["width:200px;", "width:200px;"], [new(144, 60, 0, 0), new(144, 60, 156, 0)]),
        new("row-wrap", "row", 300, 200, "flex-wrap:wrap;", "block", 40, 40, ["width:160px;flex-grow:1;", "width:160px;flex-grow:1;"], [new(300, 60, 0, 0), new(300, 60, 0, 100)]),
        new("column-shrink", "column", 300, 200, "", "block", 40, 40, ["height:400px;"], [new(100, 200, 0, 0)]),
        new("column-grow", "column", 300, 200, "", "block", 40, 40, ["height:40px;flex-grow:1;"], [new(100, 200, 0, 0)]),
        new("column-two", "column", 300, 200, "", "block", 40, 40, ["height:200px;", "height:200px;"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-weighted", "column", 300, 200, "", "block", 40, 40, ["height:400px;", "height:200px;"], [new(100, 133.328125, 0, 0), new(100, 66.671875, 0, 133.328125)]),
        new("column-none", "column", 300, 200, "", "block", 40, 40, ["height:400px;flex:none;"], [new(100, 400, 0, 0)]),
        new("column-fractional-grow", "column", 300, 200, "", "block", 40, 40, ["height:40px;flex-grow:.25;", "height:40px;flex-grow:.25;"], [new(100, 70, 0, 0), new(100, 70, 0, 70)]),
        new("column-max-base", "column", 300, 200, "", "block", 40, 40, ["flex-basis:400px;max-height:300px;", "flex-basis:400px;"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-min-base", "column", 300, 200, "", "block", 40, 40, ["flex-basis:40px;min-height:100px;flex-grow:1;", "flex-basis:40px;flex-grow:1;"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-content", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex:0 0 content;"], [new(100, 40, 0, 0)]),
        new("column-content-longhand", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex-basis:content;flex-shrink:0;"], [new(100, 40, 0, 0)]),
        new("column-basis-fixed", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex:1 1 80px;", "height:160px;flex:1 1 80px;"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-basis-percent", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex:1 1 25%;", "height:160px;flex:1 1 25%;"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-basis-calc", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex:1 1 calc(25% + 10px);", "height:160px;flex:1 1 calc(25% + 10px);"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-margin", "column", 300, 200, "", "block", 40, 40, ["height:200px;margin:10px;", "height:200px;"], [new(100, 90, 10, 10), new(100, 90, 0, 110)]),
        new("column-content-box", "column", 300, 200, "", "block", 40, 40, ["flex:1 1 100px;box-sizing:content-box;padding:10px;border:2px solid green;", "flex:1 1 100px;"], [new(124, 112, 0, 0), new(100, 88, 0, 112)]),
        new("column-border-box-shrink", "column", 300, 200, "", "block", 40, 40, ["flex-basis:200px;padding:10px;border:2px solid green;", "flex-basis:200px;"], [new(100, 106.390625, 0, 0), new(100, 93.609375, 0, 106.390625)]),
        new("column-percent-min", "column", 300, 200, "", "block", 40, 40, ["height:40px;min-height:60%;flex:1 1 auto;", "height:40px;flex:1 1 auto;"], [new(100, 120, 0, 0), new(100, 80, 0, 120)]),
        new("column-percent-max", "column", 300, 200, "", "block", 40, 40, ["height:40px;max-height:50%;flex:2 1 auto;", "height:40px;flex:1 1 auto;"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-auto-min", "column", 300, 80, "", "block", 40, 120, ["height:160px;min-height:auto;"], [new(100, 120, 0, 0)]),
        new("column-clip-min", "column", 300, 80, "", "block", 40, 120, ["height:160px;min-height:auto;overflow:clip;"], [new(100, 120, 0, 0)]),
        new("column-hidden-min", "column", 300, 80, "", "block", 40, 120, ["height:160px;min-height:auto;overflow:hidden;"], [new(100, 80, 0, 0)]),
        new("column-auto-overflow-min", "column", 300, 80, "", "block", 40, 120, ["height:160px;min-height:auto;overflow:auto;"], [new(100, 80, 0, 0)]),
        new("column-specified-min-cap", "column", 300, 40, "", "block", 40, 120, ["height:60px;min-height:auto;"], [new(100, 60, 0, 0)]),
        new("column-explicit-min-zero", "column", 300, 40, "", "block", 40, 120, ["height:60px;min-height:0;"], [new(100, 40, 0, 0)]),
        new("column-table-shrink", "column", 300, 200, "", "table", 40, 40, ["height:400px;"], [new(100, 200, 0, 0)]),
        new("column-table-grow", "column", 300, 200, "", "table", 40, 40, ["height:40px;flex:1 1 auto;"], [new(100, 200, 0, 0)]),
        new("column-table-content", "column", 300, 200, "", "table", 40, 40, ["height:160px;flex:0 0 content;"], [new(100, 40, 0, 0)]),
        new("column-table-two", "column", 300, 200, "", "table", 40, 40, ["height:200px;", "height:200px;"], [new(100, 100, 0, 0), new(100, 100, 0, 100)]),
        new("column-reverse", "column-reverse", 300, 200, "", "block", 40, 40, ["height:200px;", "height:200px;"], [new(100, 100, 0, 100), new(100, 100, 0, 0)]),
        new("column-rtl", "column", 300, 200, "direction:rtl;", "block", 40, 40, ["height:200px;", "height:200px;"], [new(100, 100, 200, 0), new(100, 100, 200, 100)]),
        new("column-gap", "column", 300, 200, "gap:12px;", "block", 40, 40, ["height:200px;", "height:200px;"], [new(100, 94, 0, 0), new(100, 94, 0, 106)]),
        new("column-wrap", "column", 300, 200, "flex-wrap:wrap;", "block", 40, 40, ["height:160px;flex-grow:1;", "height:160px;flex-grow:1;"], [new(100, 200, 0, 0), new(100, 200, 150, 0)]),
        new("row-table-content-peer", "row", 300, 200, "", "table", 40, 40, ["width:160px;flex:0 0 content;", "width:40px;flex:none;"], [new(40, 60, 0, 0), new(40, 60, 40, 0)]),
        new("row-table-basis-peer", "row", 300, 200, "", "table", 40, 40, ["width:160px;flex:0 0 80px;", "width:40px;flex:none;"], [new(80, 60, 0, 0), new(40, 60, 80, 0)]),
        new("row-table-auto-min-peer", "row", 300, 200, "", "table", 40, 40, ["width:400px;min-width:auto;", "width:40px;flex:none;"], [new(260, 60, 0, 0), new(40, 60, 260, 0)]),
        new("column-table-content-peer", "column", 300, 200, "", "table", 40, 40, ["height:160px;flex:0 0 content;", "height:40px;flex:none;"], [new(100, 40, 0, 0), new(100, 40, 0, 40)]),
        new("column-table-basis-peer", "column", 300, 200, "", "table", 40, 40, ["height:160px;flex:0 0 80px;", "height:40px;flex:none;"], [new(100, 80, 0, 0), new(100, 40, 0, 80)]),
        new("column-table-auto-min-peer", "column", 300, 200, "", "table", 40, 40, ["height:400px;min-height:auto;", "height:40px;flex:none;"], [new(100, 160, 0, 0), new(100, 40, 0, 160)]),
        new("row-table-auto-grow", "row", 300, 200, "", "table", 40, 40, ["width:auto;flex:1 1 auto;"], [new(300, 60, 0, 0)]),
        new("column-table-auto-grow", "column", 300, 200, "", "table", 40, 40, ["height:auto;flex:1 1 auto;"], [new(100, 200, 0, 0)]),
        new("row-mixed-calc", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex:0 0 calc(25% + 10px);", "flex:0 0 40px;"], [new(85, 60, 0, 0), new(40, 60, 85, 0)]),
        new("row-content-middle", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex:0 content 0;"], [new(160, 60, 0, 0)]),
        new("row-ratio-content", "row", 300, 200, "", "block", 40, 40, ["width:160px;flex:0 0 content;aspect-ratio:2;"], [new(120, 60, 0, 0)]),
        new("row-ratio-minimum", "row", 80, 200, "", "block", 40, 40, ["width:160px;min-width:auto;aspect-ratio:2;"], [new(120, 60, 0, 0)]),
        new("column-mixed-calc", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex:0 0 calc(25% + 10px);", "flex:0 0 40px;"], [new(100, 60, 0, 0), new(100, 40, 0, 60)]),
        new("column-content-middle", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex:0 content 0;"], [new(100, 160, 0, 0)]),
        new("column-ratio-content", "column", 300, 200, "", "block", 40, 40, ["height:160px;flex:0 0 content;aspect-ratio:2;"], [new(100, 50, 0, 0)]),
        new("column-ratio-minimum", "column", 300, 45, "", "block", 40, 40, ["height:160px;min-height:auto;aspect-ratio:2;"], [new(100, 50, 0, 0)]),
        new("row-fractional-shrink", "row", 300, 200, "", "block", 40, 40, ["width:400px;flex-shrink:.25;", "width:200px;flex-shrink:.25;"], [new(300, 60, 0, 0), new(150, 60, 300, 0)]),
        new("row-percent-padding", "row", 300, 200, "", "block", 40, 40, ["flex:1 1 100px;box-sizing:content-box;padding:10%;", "flex:1 1 100px;"], [new(180, 120, 0, 0), new(120, 60, 180, 0)]),
        new("column-fractional-shrink", "column", 300, 200, "", "block", 40, 40, ["height:400px;flex-shrink:.25;", "height:200px;flex-shrink:.25;"], [new(100, 266.6666666666667, 0, 0), new(100, 133.33333333333334, 0, 266.6666666666667)]),
        new("column-percent-padding", "column", 300, 200, "", "block", 40, 40, ["flex:1 1 100px;box-sizing:content-box;padding:10%;", "flex:1 1 100px;"], [new(160, 130, 0, 0), new(100, 70, 0, 130)]),
    ];
}
