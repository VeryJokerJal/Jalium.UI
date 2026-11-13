using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSharedCapsContextTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("Ο", "Σ", "Α", "σ")]
    [InlineData("Ο", "Σ", "", "ς")]
    [InlineData("Ο'", "Σ", "Α", "σ")]
    [InlineData("Ο", "Σ'", "Α", "σ'")]
    [InlineData("Ο\u0301", "Σ", "Α", "σ")]
    [InlineData("Ο\n", "Σ", "", "σ")]
    [InlineData("Ο", "Σ", "\nΑ", "ς")]
    [InlineData("Ο ", "Σ", "", "σ")]
    public void LogicalContextSurvivesSourceAndSoftLineBoundaries(string before, string source, string after, string paint)
    {
        foreach (bool nested in new[] { false, true })
        {
            var actual = new Fixture(before, source, after, caps:true, nested);
            var reference = new Fixture(before, paint, after, caps:false, nested);
            foreach (int width in new[] { 24, 28, 32, 36, 40, 44, 48, 64, 200, 40 })
            {
                actual.Layout(width); reference.Layout(width); EqualGeometry(actual, reference);
                actual.AssertSources(before, source, after);
            }
            if (before == "Ο" && source == "Σ" && after == "Α")
            {
                actual.Layout(40);
                Assert.Equal(44, actual.Texts[1].TransformToAncestor(actual.Root).Y, 3);
                Assert.Equal(88, actual.Texts[2].TransformToAncestor(actual.Root).Y, 3);
            }
        }
    }

    [Theory]
    [InlineData("padding-left:1px;")]
    [InlineData("border-left:1px solid red;")]
    [InlineData("margin-left:1px;")]
    [InlineData("vertical-align:super;")]
    public void PhysicalShapingEdgesKeepLogicalCaseContext(string edge)
    {
        var actual = new Fixture("Ο", "Σ", "Α", true, nested:true);
        var reference = new Fixture("Ο", "σ", "Α", false, nested:true);
        Css.SetStyle(actual.Root.Children[1], "display:inline;" + edge);
        Css.SetStyle(reference.Root.Children[1], "display:inline;" + edge);
        foreach (int width in new[] { 24, 40, 48, 200 })
        { actual.Layout(width); reference.Layout(width); EqualGeometry(actual, reference); }
    }

    [Fact]
    public void TextEditsReorderAndRemovalInvalidateBothContextDirections()
    {
        var actual = new Fixture("Ο", "Σ", "Α", true);
        var reference = new Fixture("Ο", "σ", "Α", false);
        actual.Layout(40); reference.Layout(40); EqualGeometry(actual, reference);
        actual.Texts[2].Text = ""; reference.Texts[2].Text = ""; reference.Texts[1].Text = "ς";
        actual.Layout(40); reference.Layout(40); EqualGeometry(actual, reference);
        actual.Texts[0].Text = ""; reference.Texts[0].Text = ""; reference.Texts[1].Text = "σ";
        actual.Layout(40); reference.Layout(40); EqualGeometry(actual, reference);
        actual.Texts[0].Text = "Ο"; reference.Texts[0].Text = "Ο";
        actual.Root.Children.Remove(actual.Texts[0]); reference.Root.Children.Remove(reference.Texts[0]);
        actual.Root.Children.Add(actual.Texts[0]); reference.Root.Children.Add(reference.Texts[0]);
        actual.Layout(40); reference.Layout(40); EqualGeometry(actual, reference);
        actual.Root.Children.Remove(actual.Texts[0]); reference.Root.Children.Remove(reference.Texts[0]);
        reference.Texts[1].Text = "σ";
        actual.Layout(40); reference.Layout(40); EqualGeometry(actual, reference);
        Css.SetStyle(actual.Texts[1], "font-variant-caps:normal;"); reference.Texts[1].Text = "Σ";
        actual.Layout(40); reference.Layout(40); EqualGeometry(actual, reference);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReplacedAndBlockBoxesEndContext(bool block)
    {
        var actual = new Fixture("Ο", "Σ", "", true);
        var reference = new Fixture("Ο", "σ", "", false);
        foreach (var f in new[] { actual, reference })
        {
            var box = new Border { Width = 4, Height = 8 };
            Css.SetStyle(box, block ? "display:block;" : "display:inline-block;");
            f.Root.Children.Insert(1, box);
        }
        foreach (int width in new[] { 24, 40, 200 })
        { actual.Layout(width); reference.Layout(width); EqualGeometry(actual, reference); }
    }

    [Theory] [InlineData(-1d)] [InlineData(1.5d)] [InlineData(3d)]
    public void TrackingAndWordSpacingUseTheSameSharedContext(double spacing)
    {
        var actual = new Fixture("Ο", "Σ", " Α", true) { ExtraCss = $"letter-spacing:{spacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;word-spacing:3px;" };
        var reference = new Fixture("Ο", "ς", " Α", false) { ExtraCss = actual.ExtraCss };
        foreach (int width in new[] { 24, 40, 48, 64, 200, 40 })
        { actual.Layout(width); reference.Layout(width); EqualGeometry(actual, reference); }
    }

    [Fact]
    public void DetachedTextDropsThePreviousParagraphContext()
    {
        var fixture = new Fixture("Ο", "Σ", "Α", true); fixture.Layout(40);
        var text = fixture.Texts[1]; fixture.Root.Children.Remove(text);
        Css.SetStyle(text, "font-family:Seravek;font-size:32px;line-height:44px;white-space:pre-wrap;font-variant-caps:unicase;font-feature-settings:'smcp' off;");
        var reference = new TextBlock { Text = "σ" };
        Css.SetStyle(reference, "font-family:Seravek;font-size:32px;line-height:44px;white-space:pre-wrap;");
        foreach (var item in new[] { text, reference }) { item.Measure(new(200, 200)); item.Arrange(new(0, 0, 200, 200)); item.UpdateLayout(); }
        Assert.Equal(reference.DesiredSize.Width, text.DesiredSize.Width, 3);
        Assert.Null(text.Parent); Assert.Equal("Σ", text.Text);
    }

    [Fact]
    public void ExplicitInlineRunBoxesShareTheParagraphContext()
    {
        var actual = new TextBlock(); var reference = new TextBlock();
        Run[] source = [new("Ο"), new("Σ"), new("Α")];
        Run[] paint = [new("Ο"), new("σ"), new("Α")];
        foreach (var run in source) actual.Inlines.Add(run);
        foreach (var run in paint) reference.Inlines.Add(run);
        var parent = source[1].Parent;
        foreach (var text in new[] { actual, reference })
        {
            Css.SetStyleSheet(text, "Run{display:inline}");
            Css.SetStyle(text, "display:block;font-family:Seravek;font-size:32px;line-height:44px;white-space:pre-wrap;word-break:break-all;overflow-wrap:anywhere;text-autospace:no-autospace;");
        }
        Css.SetStyle(source[1], "font-variant-caps:unicase;font-feature-settings:'smcp' off;");
        foreach (int width in new[] { 24, 40, 48, 200, 40 })
        {
            foreach (var text in new[] { actual, reference })
                for (int i = 0; i < 3; ++i) { text.Measure(new(width, 400)); text.Arrange(new(0, 0, width, 400)); text.UpdateLayout(); }
            for (int at = 0; at < 3; ++at)
            {
                var cells = actual.GetSourceRangeSegments(at, 1); var expected = reference.GetSourceRangeSegments(at, 1);
                Assert.Equal(expected.Count, cells.Count);
                for (int i = 0; i < cells.Count; ++i)
                { Assert.Equal(expected[i].StartX, cells[i].StartX, 3); Assert.Equal(expected[i].EndX, cells[i].EndX, 3); Assert.Equal(expected[i].Baseline, cells[i].Baseline, 3); }
            }
        }
        Assert.Equal("Σ", source[1].Text); Assert.Same(parent, source[1].Parent); Assert.Same(source[1], actual.Inlines[1]);
    }

    private static void EqualGeometry(Fixture actual, Fixture reference)
    {
        for (int i = 0; i < actual.Texts.Length; ++i)
        {
            var a = actual.Texts[i]; var b = reference.Texts[i];
            if (a.Parent is null) continue;
            var x = a.TransformToAncestor(actual.Root); var y = b.TransformToAncestor(reference.Root);
            Assert.Equal(y.X, x.X, 3); Assert.Equal(y.Y, x.Y, 3);
            for (int at = 0; at < a.Text.Length; ++at)
            {
                var expected = b.GetSourceRangeSegments(at, 1); var cells = a.GetSourceRangeSegments(at, 1);
                Assert.Equal(expected.Count, cells.Count);
                for (int j = 0; j < cells.Count; ++j)
                { Assert.Equal(expected[j].StartX, cells[j].StartX, 3); Assert.Equal(expected[j].EndX, cells[j].EndX, 3); Assert.Equal(expected[j].Baseline, cells[j].Baseline, 3); }
            }
        }
    }

    private sealed class Fixture
    {
        internal readonly StackPanel Root = new();
        internal readonly TextBlock[] Texts;
        internal string ExtraCss = "";
        private readonly DependencyObject?[] _parents;
        internal Fixture(string before, string source, string after, bool caps, bool nested = false)
        {
            Texts = [new() { Text = before }, new() { Text = source }, new() { Text = after }];
            foreach (var text in Texts)
            {
                if (nested) { var frame = new StackPanel(); Css.SetStyle(frame, "display:inline"); frame.Children.Add(text); Root.Children.Add(frame); }
                else Root.Children.Add(text);
            }
            _parents = Texts.Select(text => text.Parent).ToArray();
            Css.SetStyleSheet(Root, "TextBlock{display:inline}");
            if (caps) Css.SetStyle(Texts[1], "font-variant-caps:unicase;font-feature-settings:'smcp' off;");
        }
        internal void Layout(int width)
        {
            Css.SetStyle(Root, $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;white-space:pre-wrap;word-break:break-all;overflow-wrap:anywhere;text-autospace:no-autospace;" + ExtraCss);
            for (int i = 0; i < 3; ++i) { Root.Measure(new(width, 500)); Root.Arrange(new(0, 0, width, 500)); Root.UpdateLayout(); }
        }
        internal void AssertSources(params string[] expected)
        {
            for (int i = 0; i < Texts.Length; ++i)
            { Assert.Equal(expected[i], Texts[i].Text); Assert.Same(_parents[i], Texts[i].Parent); Texts[i].SelectAll(); Assert.Equal(expected[i], Texts[i].SelectedText); Texts[i].Select(0, 0); }
        }
    }
}
