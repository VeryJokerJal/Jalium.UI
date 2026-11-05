using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Documents;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssHangingWhitespaceTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    internal static readonly string[] Samples = ["MMMM MMMM", "MMMM  MMMM", "MMMM ", "MMMM    ", "MMMM\t", "   MMMM", "    ",
        "MMMM \nMMMM", "MMMM \nMMMM ", "\tMMMM", "MMMM\u2003", "MMMM\u00a0"];

    public static IEnumerable<object[]> IntrinsicCases()
    {
        foreach (var block in new[] { false, true })
        foreach (var mode in new[] { "normal", "pre", "pre-wrap", "pre-line", "break-spaces" })
        foreach (var dimension in new[] { "min-content", "max-content" })
        for (var sample = 0; sample < Samples.Length; sample++) yield return [block, mode, dimension, sample];
    }

    [Theory] [MemberData(nameof(IntrinsicCases))]
    public void PreservedWhitespaceUsesItsActualIntrinsicRule(bool block, string mode, string dimension, int sample)
    {
        var text = Create(Samples[sample]);
        Css.SetStyle(text, (block ? "display:block;" : "") + $"white-space:{mode};width:{dimension};tab-size:4;");
        Layout(text);
        Near(Expected(mode, dimension, sample), text.ActualWidth);
        Assert.Equal(Samples[sample], text.Text);
    }

    // Whole unwrapped strings supply glyph metrics independently of the minimum
    // contribution under test. The combinations come from Text3/4 and the selected
    // WPT/browser observations, including conditional hanging at paragraph ends.
    internal static double Expected(string mode, string dimension, int sample)
    {
        var word = Reference("MMMM"); var space = Reference(" ");
        if (sample is 10 or 11) return word + Reference(sample == 10 ? "\u2003" : "\u00a0");
        var preserved = sample switch
        {
            0 => 2 * word + space, 1 => 2 * word + 2 * space, 2 => word + space,
            3 => word + 4 * space, 4 or 9 => 8 * space, 5 => word + 3 * space,
            6 => 4 * space, 7 or 8 => word + space, _ => throw new ArgumentOutOfRangeException(nameof(sample)),
        };
        if (mode == "pre") return preserved;
        if (dimension == "max-content")
        {
            if (mode is "pre-wrap" or "break-spaces") return preserved;
            if (sample == 6) return 0;
            return sample is 0 or 1 || mode == "normal" && sample is 7 or 8 ? 2 * word + space : word;
        }
        if (mode == "break-spaces") return sample switch
        {
            0 or 1 or 2 or 3 or 7 or 8 => word + space,
            4 => 8 * space, 6 => space, _ => word,
        };
        return sample == 6 ? 0 : word;
    }

    internal static TextBlock Create(string text) => new() { Text = text, FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };

    public static IEnumerable<object[]> AlignmentCases()
    {
        foreach (var block in new[] { false, true })
        foreach (var direction in new[] { "ltr", "rtl" })
        foreach (var align in new[] { "left", "right", "center" })
        foreach (var columns in new[] { 4, 5, 6, 8 })
        foreach (var hard in new[] { false, true }) yield return [block, direction, align, columns, hard];
    }

    [Theory] [MemberData(nameof(AlignmentCases))]
    public void ConditionalHangingKeepsTheFittingSpacesAndPhysicalSourceGeometry(bool block, string direction, string align, int columns, bool hard)
    {
        var word = Reference("MMMM"); var unit = word / 4; var width = columns * unit;
        var text = Create("MMMM   " + (hard ? "\nM" : ""));
        Css.SetStyle(text, (block ? "display:block;" : "") + $"white-space:pre-wrap;direction:{direction};text-align:{align};width:{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;");
        Layout(text);
        var extra = Math.Max(0, width - 7 * unit);
        var offset = align == "right" ? extra : align == "center" ? extra / 2 : 0;
        if (direction == "rtl") offset += Math.Min(3 * unit, Math.Max(0, width - word));
        var segment = Assert.Single(text.GetSourceRangeSegments(0, 4));
        Near(offset, Math.Min(segment.StartX, segment.EndX));
        text.Select(4, 3); Assert.Equal("   ", text.SelectedText);
        Assert.Equal(hard ? 2 : 1, text.GetSourceRangeSegments(0, text.Text.Length).Count);
    }

    [Theory] [InlineData("flex")] [InlineData("grid")] [InlineData("table")]
    public void ContainerIntrinsicContributionsExcludeHangingWhitespace(string kind)
    {
        var text = Create("MMMM MMMM"); Css.SetStyle(text, "display:block;white-space:pre-wrap;");
        var badge = new Border(); Css.SetStyle(badge, "width:20px;height:24px;flex:none;");
        var root = new StackPanel();
        if (kind == "table")
        {
            var row = new StackPanel(); var cell = new StackPanel(); var second = new StackPanel();
            Css.SetStyle(root, "display:table;width:min-content;border-spacing:0;"); Css.SetStyle(row, "display:table-row;");
            Css.SetStyle(cell, "display:table-cell;padding:0;"); Css.SetStyle(second, "display:table-cell;padding:0;");
            root.Children.Add(row); row.Children.Add(cell); row.Children.Add(second); cell.Children.Add(text); second.Children.Add(badge);
        }
        else
        {
            Css.SetStyle(root, kind == "flex" ? "display:flex;width:min-content;" : "display:grid;width:min-content;grid-template-columns:min-content 20px;");
            root.Children.Add(text); root.Children.Add(badge);
        }
        Layout(root); Near(Reference("MMMM") + 20, root.ActualWidth); Near(Reference("MMMM"), text.ActualWidth);
    }

    [Theory]
    [InlineData("left", 4)] [InlineData("left", 6)] [InlineData("left", 8)]
    [InlineData("center", 4)] [InlineData("center", 6)] [InlineData("center", 8)]
    [InlineData("right", 4)] [InlineData("right", 6)] [InlineData("right", 8)]
    public void ASoftWrappedSpaceSequenceSpansOriginalSiblingSources(string alignment, int columns)
    {
        var unit = Reference("MMMM") / 4; var width = columns * unit;
        var a = Create(""); var run = new Run("MMMM "); a.Inlines.Add(run);
        var spaces = Create("  "); var b = Create("MMMM"); var root = new StackPanel();
        root.Children.Add(a); root.Children.Add(spaces); root.Children.Add(b);
        foreach (var child in new[] { a, spaces, b }) Css.SetStyle(child, "display:inline;");
        Css.SetStyle(root, $"display:block;white-space:pre-wrap;text-align:{alignment};font-family:'Courier New';font-size:16px;line-height:24px;");
        Layout(root, width);
        var offset = alignment == "right" ? width - 4 * unit : alignment == "center" ? (width - 4 * unit) / 2 : 0;
        var first = Assert.Single(a.GetSourceRangeSegments(0, 4));
        var blank = Assert.Single(spaces.GetSourceRangeSegments(0, 2));
        var second = Assert.Single(b.GetSourceRangeSegments(0, 4));
        Near(offset, a.TransformToAncestor(root).X + first.StartX);
        Near(offset + 5 * unit, spaces.TransformToAncestor(root).X + blank.StartX);
        Near(first.Baseline + a.TransformToAncestor(root).Y, blank.Baseline + spaces.TransformToAncestor(root).Y);
        // Run-backed and plain sources can have different native baseline
        // struts. The next word must follow within one row, with no blank row.
        var advance = second.Baseline + b.TransformToAncestor(root).Y - first.Baseline - a.TransformToAncestor(root).Y;
        Assert.InRange(advance, a.ActualHeight, a.ActualHeight + b.ActualHeight - .01);
        Assert.Same(run, Assert.Single(a.Inlines)); spaces.SelectAll(); Assert.Equal("  ", spaces.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TextStyleAndWidthReplaysKeepTheOriginalRunAndWhitespace(bool block)
    {
        var text = Create(""); var run = new Run("MMMM   MMMM"); text.Inlines.Add(run);
        void Apply(string mode) => Css.SetStyle(text, (block ? "display:block;" : "") + $"white-space:{mode};width:min-content;");
        Apply("pre-wrap"); Layout(text); var initial = text.GetSourceRangeSegments(0, text.Text.Length).ToArray();
        Near(Reference("MMMM"), text.ActualWidth);
        Apply("break-spaces"); Layout(text); Near(Reference("MMMM "), text.ActualWidth);
        run.Text = ""; Layout(text); Near(0, text.ActualWidth);
        run.Text = "MM   MM"; Apply("pre-wrap"); Layout(text, 70); Near(Reference("MM"), text.ActualWidth);
        run.Text = "MMMM   MMMM"; Layout(text, 500); Layout(text); Near(Reference("MMMM"), text.ActualWidth);
        Assert.Equal(initial, text.GetSourceRangeSegments(0, text.Text.Length));
        Assert.Same(run, Assert.Single(text.Inlines)); text.SelectAll(); Assert.Equal(run.Text, text.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RightToLeftGlyphRangesAndPointerHitsUseTheSameVisualCoordinates(bool block)
    {
        var text = Create("MMMM   ");
        Css.SetStyle(text, (block ? "display:block;" : "") + "white-space:pre-wrap;direction:rtl;width:76.8125px;");
        Layout(text);
        var range = Assert.Single(text.GetSourceRangeSegments(0, 4));
        var pointer = text.GetPositionFromPoint(new(range.StartX + Reference("M") * 1.1, range.Baseline - 2), true)!;
        Assert.Equal(1, text.ContentStart.GetOffsetToPosition(pointer));
        var blank = Assert.Single(text.GetSourceRangeSegments(4, 3));
        Near(3 * Reference(" "), blank.EndX - blank.StartX);
        Assert.True(blank.EndX <= range.StartX + .06);
    }
    internal static double Reference(string value)
    {
        var text = Create(value); Css.SetStyle(text, "display:block;white-space:pre;width:max-content;"); Layout(text); return text.ActualWidth;
    }
    internal static void Layout(FrameworkElement element, double width = 200, double height = 300)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout();
        }
    }
    internal static void Near(double expected, double actual) => Assert.InRange(actual, expected - .06, expected + .06);
}
