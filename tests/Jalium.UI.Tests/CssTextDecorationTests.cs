using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextDecorationTests
{
    [Theory]
    [InlineData("text-decoration-line", "underline overline line-through")]
    [InlineData("text-decoration-line", "blink underline")]
    [InlineData("text-decoration-line", "spelling-error")]
    [InlineData("text-decoration-line", "grammar-error")]
    [InlineData("text-decoration", "red wavy underline 2px")]
    [InlineData("text-decoration", "spelling-error solid blue 3px")]
    [InlineData("text-decoration", "grammar-error dotted red 2px")]
    [InlineData("text-decoration", "underline from-font red")]
    [InlineData("text-decoration", "none dotted blue")]
    [InlineData("text-decoration-thickness", "50%")]
    [InlineData("text-underline-offset", "auto")]
    [InlineData("text-underline-offset", "-3px")]
    [InlineData("text-underline-offset", "25%")]
    [InlineData("text-underline-offset", "calc(50% - 2px)")]
    [InlineData("text-underline-position", "auto")]
    [InlineData("text-underline-position", "under")]
    [InlineData("text-underline-position", "from-font")]
    [InlineData("text-underline-position", "left")]
    [InlineData("text-underline-position", "right under")]
    [InlineData("text-underline-position", "from-font left")]
    [InlineData("text-decoration-skip-inset", "none")]
    [InlineData("text-decoration-skip-inset", "auto")]
    [InlineData("text-decoration-inset", "auto")]
    [InlineData("text-decoration-inset", "8px")]
    [InlineData("text-decoration-inset", "1em -10%")]
    [InlineData("text-decoration-inset", "calc(10% - 2px) 4px")]
    [InlineData("box-decoration-break", "slice")]
    [InlineData("box-decoration-break", "clone")]
    [InlineData("text-decoration-color", "color-mix(in srgb, currentColor 25%, red)")]
    public void AcceptsSupportedDecorationGrammar(string property, string value)
        => Assert.True(Css.Supports(property, value));

    [Theory]
    [InlineData("text-decoration-line", "none underline")]
    [InlineData("text-decoration-line", "underline underline")]
    [InlineData("text-decoration-line", "spelling-error underline")]
    [InlineData("text-decoration-line", "grammar-error spelling-error")]
    [InlineData("text-decoration-line", "blink grammar-error")]
    [InlineData("text-decoration", "underline underline")]
    [InlineData("text-decoration", "spelling-error overline")]
    [InlineData("text-decoration", "underline grammar-error")]
    [InlineData("text-decoration", "underline solid dashed")]
    [InlineData("text-decoration", "underline -2px")]
    [InlineData("text-decoration-thickness", "-2px")]
    [InlineData("text-underline-offset", "2")]
    [InlineData("text-underline-offset", "from-font")]
    [InlineData("text-underline-offset", "auto 2px")]
    [InlineData("text-underline-position", "auto left")]
    [InlineData("text-underline-position", "under from-font")]
    [InlineData("text-underline-position", "left right")]
    [InlineData("text-underline-position", "under under")]
    [InlineData("text-decoration-skip-inset", "auto none")]
    [InlineData("text-decoration-skip-inset", "2px")]
    [InlineData("text-decoration-inset", "auto 2px")]
    [InlineData("text-decoration-inset", "2px 3px 4px")]
    [InlineData("text-decoration-inset", "2")]
    [InlineData("text-decoration-inset", "inherit auto")]
    [InlineData("box-decoration-break", "auto")]
    [InlineData("box-decoration-break", "clone slice")]
    public void RejectsInvalidDecorationGrammar(string property, string value)
        => Assert.False(Css.Supports(property, value));

    [Fact]
    public void ShorthandAndLonghandDecorateText_AndNativeLocalValueWins()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 45, FontSize = 24,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(text, "text-decoration: underline wavy red 3px");
        CssBorderRadiusTests.Layout(text, 130, 45);
        Assert.True(ColoredPixels(text, red: true) > 0);

        Css.SetStyle(text, "text-decoration: underline dashed blue 3px; text-decoration-color: red");
        CssBorderRadiusTests.Layout(text, 130, 45);
        Assert.True(ColoredPixels(text, red: true) > 0);

        text.TextDecorations = new TextDecorationCollection();
        Assert.Equal(0, ColoredPixels(text, red: true));

        text.ClearValue(TextBlock.TextDecorationsProperty);
        Assert.True(ColoredPixels(text, red: true) > 0);

        Css.SetStyle(text, "text-decoration: none");
        CssBorderRadiusTests.Layout(text, 130, 45);
        Assert.Equal(0, ColoredPixels(text, red: true));
    }

    [Theory]
    [InlineData("spelling-error", true)]
    [InlineData("grammar-error", false)]
    public void ErrorDecorationsUseUaColorAndNativeLocalValueWins(string line, bool spelling)
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 45, FontSize = 24,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(text, $"text-decoration: {line} solid blue 3px");
        CssBorderRadiusTests.Layout(text, 130, 45);
        Assert.Equal(spelling ? CssTextDecorationLine.SpellingError : CssTextDecorationLine.GrammarError,
            CssTextDecorationProperties.Line(text));
        Assert.Equal(spelling ? Colors.Red : Colors.Green,
            Assert.IsType<SolidColorBrush>(Assert.Single(text.TextDecorations!).Brush).Color);
        Assert.True(HasErrorWave(text, spelling ? Colors.Red : Colors.Green));
        Assert.True(ErrorPixels(text, spelling) > 0);
        Assert.Equal(0, ColoredPixels(text, red: false));

        Css.SetStyle(text, $"text-decoration: underline; text-decoration-line: {line}; " +
            "text-decoration-style: solid; text-decoration-color: blue; text-decoration-thickness: 3px");
        Assert.Equal(spelling ? CssTextDecorationLine.SpellingError : CssTextDecorationLine.GrammarError,
            CssTextDecorationProperties.Line(text));
        Assert.True(HasErrorWave(text, spelling ? Colors.Red : Colors.Green));
        Assert.True(ErrorPixels(text, spelling) > 0);

        text.TextDecorations = new TextDecorationCollection();
        Assert.Equal(0, ErrorPixels(text, spelling));

        text.ClearValue(TextBlock.TextDecorationsProperty);
        Assert.True(ErrorPixels(text, spelling) > 0);
    }

    [Fact]
    public void ThicknessPercentageUsesTheComputedFontSize_AndShorthandResetsIt()
    {
        var text = new TextBlock { Text = "M", FontSize = 20 };
        Css.SetStyle(text, "text-decoration-line: underline; text-decoration-thickness: 50%");
        CssBorderRadiusTests.Layout(text, 80, 40);
        Assert.Equal(10, CssTextDecorationProperties.Thickness(text));

        Css.SetStyle(text, "text-decoration-thickness: 50%; text-decoration: underline");
        CssBorderRadiusTests.Layout(text, 80, 40);
        Assert.True(double.IsNaN(CssTextDecorationProperties.Thickness(text)));
    }

    [Fact]
    public void FromFontThicknessUsesAvailableMetrics_AndShorthandResetsIt()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 160, Height = 70, FontSize = 40,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(text, "text-decoration: underline red; text-decoration-thickness: from-font");
        CssBorderRadiusTests.Layout(text, 160, 70);
        Assert.True(CssTextDecorationProperties.ThicknessFromFont(text));
        var metrics = CssTextDecorationPainter.GetSourceFontMetrics(text, 40);
        var expected = (metrics.Available & 64) != 0
            ? Math.Max(1, metrics.UnderlineThickness) : 1;
        var stroke = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(expected, Assert.IsType<Pen>(stroke.A).Thickness, 5);

        Css.SetStyle(text, "text-decoration: underline solid red from-font");
        CssBorderRadiusTests.Layout(text, 160, 70);
        Assert.True(CssTextDecorationProperties.ThicknessFromFont(text));
        stroke = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(expected, Assert.IsType<Pen>(stroke.A).Thickness, 5);

        Css.SetStyle(text, "text-decoration-thickness: from-font; text-decoration: underline red");
        CssBorderRadiusTests.Layout(text, 160, 70);
        Assert.False(CssTextDecorationProperties.ThicknessFromFont(text));
        stroke = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(1, Assert.IsType<Pen>(stroke.A).Thickness, 5);
    }

    [Fact]
    public void DecorationInsetMovesLogicalEndpoints_AndShorthandLeavesItIntact()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 180, Height = 60, FontSize = 24,
            Foreground = Brushes.Black,
        };
        const string decoration = "text-decoration: underline solid red 2px";
        Css.SetStyle(text, decoration);
        CssBorderRadiusTests.Layout(text, 180, 60);
        var full = Assert.Single(RecordedRedStrokes(text));
        var width = full.V2 - full.V0;

        Css.SetStyle(text, decoration + "; text-decoration-inset: 10px 20px");
        var trimmed = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(full.V0 + 10, trimmed.V0, 5);
        Assert.Equal(full.V2 - 20, trimmed.V2, 5);

        Css.SetStyle(text, "text-decoration-inset: 10% 20%; " + decoration);
        var percent = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(full.V0 + width * 0.1, percent.V0, 5);
        Assert.Equal(full.V2 - width * 0.2, percent.V2, 5);

        Css.SetStyle(text, "text-decoration-inset: calc(10% - 2px) -4px; " + decoration);
        var extended = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(full.V0 + width * 0.1 - 2, extended.V0, 5);
        Assert.Equal(full.V2 + 4, extended.V2, 5);

        Css.SetStyle(text, decoration + "; text-decoration-inset: 10px 20px; direction: rtl");
        var rtlFull = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(full.V0 + 20, rtlFull.V0, 5);
        Assert.Equal(full.V2 - 10, rtlFull.V2, 5);

        Css.SetStyle(text, "text-decoration-inset: 10px 20px; all: initial; " + decoration);
        var reset = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(full.V0, reset.V0, 5);
        Assert.Equal(full.V2, reset.V2, 5);

        text.TextDecorations = new TextDecorationCollection
        {
            new TextDecoration(TextDecorationLocation.Underline, Brushes.Red, 2, 0,
                TextDecorationUnit.Pixel, TextDecorationUnit.Pixel),
        };
        Css.SetStyle(text, decoration + "; text-decoration-inset: 10px 20px");
        var native = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(full.V0, native.V0, 5);
        Assert.Equal(full.V2, native.V2, 5);
    }

    [Fact]
    public void DecorationInsetSliceAppliesAtFirstAndLastWrappedFragment()
    {
        static DrawCommand[] Strokes(Visual visual)
            => RecordedRedStrokes(visual).OrderBy(static stroke => stroke.V1).ToArray();

        static void Check(Visual visual, DependencyObject source, int width, int height)
        {
            Css.SetStyle(source, "text-decoration: underline solid red 2px");
            if (visual is FrameworkElement element)
                CssBorderRadiusTests.Layout(element, width, height);
            var full = Strokes(visual);
            Assert.True(full.Length >= 2, $"Expected wrapped decorations; got {full.Length}");
            var last = full.Length - 1;

            Css.SetStyle(source,
                "text-decoration: underline solid red 2px; text-decoration-inset: 8px 16px");
            var sliced = Strokes(visual);
            Assert.Equal(full.Length, sliced.Length);
            Assert.Equal(full[0].V0 + 8, sliced[0].V0, 5);
            Assert.Equal(full[0].V2, sliced[0].V2, 5);
            for (var index = 1; index < last; index++)
            {
                Assert.Equal(full[index].V0, sliced[index].V0, 5);
                Assert.Equal(full[index].V2, sliced[index].V2, 5);
            }
            Assert.Equal(full[last].V0, sliced[last].V0, 5);
            Assert.Equal(full[last].V2 - 16, sliced[last].V2, 5);

            var totalWidth = full.Sum(static stroke => stroke.V2 - stroke.V0);
            Css.SetStyle(source,
                "text-decoration: underline solid red 2px; text-decoration-inset: 10% 20%");
            var percent = Strokes(visual);
            Assert.Equal(full[0].V0 + totalWidth * 0.1, percent[0].V0, 5);
            Assert.Equal(full[last].V2 - totalWidth * 0.2, percent[last].V2, 5);

            Css.SetStyle(source,
                "text-decoration: underline solid red 2px; " +
                "text-decoration-inset: 10% 20%; box-decoration-break: clone");
            var cloned = Strokes(visual);
            Assert.Equal(full.Length, cloned.Length);
            for (var index = 0; index < full.Length; index++)
            {
                var fragmentWidth = full[index].V2 - full[index].V0;
                Assert.Equal(full[index].V0 + fragmentWidth * 0.1, cloned[index].V0, 5);
                Assert.Equal(full[index].V2 - fragmentWidth * 0.2, cloned[index].V2, 5);
            }

            Css.SetStyle(source,
                "text-decoration: underline solid red 2px; " +
                "text-decoration-inset: -4px 6px; box-decoration-break: clone");
            var extendedClones = Strokes(visual);
            Assert.Equal(full.Length, extendedClones.Length);
            for (var index = 0; index < full.Length; index++)
            {
                Assert.Equal(full[index].V0 - 4, extendedClones[index].V0, 5);
                Assert.Equal(full[index].V2 - 6, extendedClones[index].V2, 5);
            }

            Css.SetStyle(source,
                "text-decoration: underline solid red 2px; " +
                "text-decoration-inset: calc(100% - 20px) 0");
            var carriedStart = Assert.Single(Strokes(visual));
            Assert.Equal(full[last].V2 - 20, carriedStart.V0, 5);
            Assert.Equal(full[last].V2, carriedStart.V2, 5);

            Css.SetStyle(source,
                "text-decoration: underline solid red 2px; " +
                "text-decoration-inset: 0 calc(100% - 20px)");
            var carriedEnd = Assert.Single(Strokes(visual));
            Assert.Equal(full[0].V0, carriedEnd.V0, 5);
            Assert.Equal(full[0].V0 + 20, carriedEnd.V2, 5);

            Css.SetStyle(source,
                "text-decoration: underline solid red 2px; text-decoration-inset: 60% 60%");
            Assert.Empty(Strokes(visual));
        }

        var direct = new TextBlock
        {
            Text = "MMMM\nMMMM", Width = 180, Height = 120,
            FontSize = 24, Foreground = Brushes.Black,
        };
        Check(direct, direct, 180, 120);

        var inline = new Span();
        inline.Inlines.Add(new Run("MMMM"));
        inline.Inlines.Add(new LineBreak());
        inline.Inlines.Add(new Run("MMMM"));
        var block = new TextBlock(inline)
        {
            Width = 180, Height = 120, FontSize = 24, Foreground = Brushes.Black,
        };
        Check(block, inline, 180, 120);

        var box = new TextBox
        {
            Text = "MMMMMMMM", Width = 85, Height = 180, FontSize = 24,
            Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap,
        };
        Check(box, box, 85, 180);

        var multilineBox = new TextBox
        {
            Text = "MMMM\nMMMM", Width = 120, Height = 120, FontSize = 24,
            Foreground = Brushes.Black,
        };
        Check(multilineBox, multilineBox, 120, 120);

        var richSpan = new Span();
        richSpan.Inlines.Add(new Run("MMMM") { FontSize = 24, Foreground = Brushes.Black });
        richSpan.Inlines.Add(new Run("MMMM") { FontSize = 24, Foreground = Brushes.Black });
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(richSpan);
        var rich = new RichTextBox(new FlowDocument(paragraph)) { Width = 100, Height = 120 };
        Check(rich, richSpan, 100, 120);
        Check(rich, paragraph, 100, 120);
    }

    [Fact]
    public void DecorationInsetDoesNotInheritOrChangeAncestorDecoration()
    {
        var run = new Run("MMMM");
        var block = new TextBlock(run)
        {
            Width = 180, Height = 60, FontSize = 24, Foreground = Brushes.Black,
        };
        Css.SetStyle(block,
            "text-decoration: underline solid red 2px; text-decoration-inset: 7px 11px");
        Css.SetStyle(run, "text-decoration: underline solid blue 2px");
        CssBorderRadiusTests.Layout(block, 180, 60);

        var parent = Assert.Single(RecordedRedStrokes(block));
        var child = Assert.Single(RecordedStrokes(block, Colors.Blue));
        Assert.Equal(CssTextDecorationInset.Zero, CssTextDecorationProperties.Inset(run));
        Assert.Equal(child.V0 + 7, parent.V0, 5);
        Assert.Equal(child.V2 - 11, parent.V2, 5);

        Css.SetStyle(run,
            "text-decoration: underline solid blue 2px; text-decoration-inset: 4px 6px");
        var changedParent = Assert.Single(RecordedRedStrokes(block));
        var changedChild = Assert.Single(RecordedStrokes(block, Colors.Blue));
        Assert.Equal(parent.V0, changedParent.V0, 5);
        Assert.Equal(parent.V2, changedParent.V2, 5);
        Assert.Equal(child.V0 + 4, changedChild.V0, 5);
        Assert.Equal(child.V2 - 6, changedChild.V2, 5);

        Css.SetStyle(run,
            "text-decoration: underline solid blue 2px; text-decoration-inset: inherit");
        Assert.Equal(CssTextDecorationProperties.Inset(block), CssTextDecorationProperties.Inset(run));
        var inheritedChild = Assert.Single(RecordedStrokes(block, Colors.Blue));
        Assert.Equal(parent.V0, inheritedChild.V0, 5);
        Assert.Equal(parent.V2, inheritedChild.V2, 5);
    }

    [Fact]
    public void DecorationInsetAutoSeparatesAdjacentDecoratingRuns()
    {
        var first = new Run("MM");
        var second = new Run("MM");
        var block = new TextBlock(first)
        {
            Width = 180, Height = 60, FontSize = 24, Foreground = Brushes.Black,
        };
        block.Inlines.Add(second);
        const string decoration = "text-decoration: underline solid red 2px";
        Css.SetStyle(first, decoration);
        Css.SetStyle(second, decoration);
        CssBorderRadiusTests.Layout(block, 180, 60);
        var full = RecordedRedStrokes(block).OrderBy(static stroke => stroke.V0).ToArray();
        Assert.Equal(2, full.Length);

        Css.SetStyle(first, decoration + "; text-decoration-inset: auto");
        Css.SetStyle(second, decoration + "; text-decoration-inset: auto");
        var separated = RecordedRedStrokes(block).OrderBy(static stroke => stroke.V0).ToArray();
        Assert.Equal(2, separated.Length);
        Assert.True(separated[1].V0 - separated[0].V2 > full[1].V0 - full[0].V2 + 2);
    }

    [Fact]
    public void BoxDecorationBreakIsNotInherited_AndCssWideKeywordsResetIt()
    {
        var run = new Run("MMMM");
        var block = new TextBlock(run) { Width = 180, Height = 60, FontSize = 24 };
        Css.SetStyle(block, "box-decoration-break: clone");
        CssBorderRadiusTests.Layout(block, 180, 60);
        Assert.Equal(CssBoxDecorationBreak.Clone, CssBoxDecorationBreakProperties.Value(block));
        Assert.Equal(CssBoxDecorationBreak.Slice, CssBoxDecorationBreakProperties.Value(run));

        Css.SetStyle(run, "box-decoration-break: inherit");
        Assert.Equal(CssBoxDecorationBreak.Clone, CssBoxDecorationBreakProperties.Value(run));

        Css.SetStyle(run, "box-decoration-break: clone; all: initial");
        Assert.Equal(CssBoxDecorationBreak.Slice, CssBoxDecorationBreakProperties.Value(run));
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_FromFontDecorationUsesSelectedFontMetrics()
        => VerifyFromFontDecorationUsesSelectedFontMetrics(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_FromFontDecorationUsesSelectedFontMetrics()
        => VerifyFromFontDecorationUsesSelectedFontMetrics(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_FromFontDecorationUsesSelectedFontMetrics()
        => VerifyFromFontDecorationUsesSelectedFontMetrics(RenderBackend.Vulkan);

    private static void VerifyFromFontDecorationUsesSelectedFontMetrics(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
        var text = new TextBlock
        {
            Text = "MM", Width = 300, Height = 160, FontSize = 80,
            FontFamily = new FontFamily("Arial"), Foreground = Brushes.Black,
        };
        const string decoration = "text-decoration: underline solid red; " +
            "text-decoration-thickness: from-font; text-underline-offset: 0px; ";
        Css.SetStyle(text, decoration + "text-underline-position: auto");
        CssBorderRadiusTests.Layout(text, 300, 160);
        var baseline = Assert.Single(RecordedRedStrokes(text));

        var metrics = CssTextDecorationPainter.GetSourceFontMetrics(text, 80);
        Assert.Equal(96u, metrics.Available & 96u);
        Assert.True(metrics.UnderlineThickness > 1);
        Assert.Equal(metrics.UnderlineThickness, Assert.IsType<Pen>(baseline.A).Thickness, 5);

        Css.SetStyle(text, decoration + "text-underline-position: from-font");
        CssBorderRadiusTests.Layout(text, 300, 160);
        var fromFont = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(baseline.V1 + metrics.UnderlinePosition, fromFont.V1, 4);
        Assert.Equal(metrics.UnderlineThickness, Assert.IsType<Pen>(fromFont.A).Thickness, 5);
    }

    [Fact]
    public void UnderlineOffsetMovesOnlyUnderline_AndDecorationShorthandDoesNotResetIt()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 55, FontSize = 24, Foreground = Brushes.Black,
        };
        Css.SetStyle(text, "text-decoration: underline overline solid red 2px; text-underline-offset: 0px");
        CssBorderRadiusTests.Layout(text, 130, 55);
        var zero = RecordedRedLines(text).Order().ToArray();
        Assert.Equal(2, zero.Length);

        Css.SetStyle(text, "text-underline-offset: 8px; text-decoration: underline overline solid red 2px");
        var positive = RecordedRedLines(text).Order().ToArray();
        Assert.Equal(zero[0], positive[0], 5);
        Assert.Equal(zero[1] + 8, positive[1], 5);

        Css.SetStyle(text, "text-decoration: underline overline solid red 2px; text-underline-offset: -3px");
        var negative = RecordedRedLines(text).Order().ToArray();
        Assert.Equal(zero[0], negative[0], 5);
        Assert.Equal(zero[1] - 3, negative[1], 5);
    }

    [Fact]
    public void InheritedPercentageUsesChildFontSize_AndExplicitInheritTracksParent()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 70, FontSize = 20, Foreground = Brushes.Black,
        };
        var parent = new Border { Child = text, Width = 130, Height = 70 };
        Css.SetStyle(parent, "text-underline-offset: 50%");
        Css.SetStyle(text, "text-decoration: underline solid red 2px");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(10, CssTextDecorationProperties.ResolveUnderlineOffset(text, text.FontSize));

        text.FontSize = 28;
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(14, CssTextDecorationProperties.ResolveUnderlineOffset(text, text.FontSize));

        Css.SetStyle(text, "text-decoration: underline solid red 2px; text-underline-offset: inherit");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(14, CssTextDecorationProperties.ResolveUnderlineOffset(text, text.FontSize));
        var inheritedRow = Assert.Single(RecordedRedLines(text));

        Css.SetStyle(parent, "text-underline-offset: -4px");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(-4, CssTextDecorationProperties.ResolveUnderlineOffset(text, text.FontSize));
        Assert.Equal(inheritedRow - 18, Assert.Single(RecordedRedLines(text)), 5);

        Css.SetStyle(parent, "text-underline-offset: calc(50% - 2px)");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(12, CssTextDecorationProperties.ResolveUnderlineOffset(text, text.FontSize), 5);
        text.FontSize = 32;
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(14, CssTextDecorationProperties.ResolveUnderlineOffset(text, text.FontSize), 5);
    }

    [Fact]
    public void NativeLocalUnderlineIgnoresCssOffset()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 55, FontSize = 24, Foreground = Brushes.Black,
            TextDecorations = new TextDecorationCollection
            {
                new TextDecoration(TextDecorationLocation.Underline, Brushes.Red, 2, 0,
                    TextDecorationUnit.Pixel, TextDecorationUnit.Pixel),
            },
        };
        Css.SetStyle(text, "text-decoration: underline red 2px; text-underline-offset: 0px");
        CssBorderRadiusTests.Layout(text, 130, 55);
        var original = Assert.Single(RecordedRedLines(text));

        Css.SetStyle(text, "text-decoration: underline red 2px; text-underline-offset: 8px");
        CssBorderRadiusTests.Layout(text, 130, 55);
        Assert.Equal(original, Assert.Single(RecordedRedLines(text)), 5);
    }

    [Fact]
    public void AncestorUnderlineUsesItsOwnFontSizeAndOffsetAcrossChildRun()
    {
        var run = new Run("MMMM") { FontSize = 32 };
        var span = new Span(run) { FontSize = 20 };
        var block = new TextBlock(span)
        {
            Width = 160, Height = 70, FontSize = 20, Foreground = Brushes.Black,
        };
        Css.SetStyle(span, "text-decoration: underline solid red 2px; text-underline-offset: 0px");
        CssBorderRadiusTests.Layout(block, 160, 70);
        var zero = Assert.Single(RecordedRedLines(block));

        Css.SetStyle(span, "text-decoration: underline solid red 2px; text-underline-offset: 50%");
        CssBorderRadiusTests.Layout(block, 160, 70);
        var inherited = Assert.Single(RecordedRedLines(block));
        Assert.Equal(zero + 10, inherited, 5);

        Css.SetStyle(run, "text-underline-offset: 20px");
        CssBorderRadiusTests.Layout(block, 160, 70);
        Assert.Equal(inherited, Assert.Single(RecordedRedLines(block)), 5);
    }

    [Fact]
    public void UnderlinePositionInheritsAndTracksParentWithoutShorthandReset()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 70, FontSize = 24, Foreground = Brushes.Black,
        };
        var parent = new Border { Child = text, Width = 130, Height = 70 };
        Css.SetStyle(text, "text-decoration: underline solid red 2px; text-underline-offset: 0px");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        var baseline = Assert.Single(RecordedRedLines(text));

        Css.SetStyle(parent, "text-underline-position: under");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        var under = Assert.Single(RecordedRedLines(text));
        Assert.True(under > baseline + 1);
        Assert.Equal(CssTextUnderlinePosition.Under, CssTextDecorationProperties.UnderlinePosition(text));

        Css.SetStyle(text, "text-underline-position: inherit; text-underline-offset: 0px; text-decoration: underline solid red 2px");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(under, Assert.Single(RecordedRedLines(text)), 5);

        Css.SetStyle(parent, "text-underline-position: auto");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(baseline, Assert.Single(RecordedRedLines(text)), 5);
    }

    [Fact]
    public void UnderPositionUsesOnlyTextDecoratedByTheInlineSource()
    {
        static double EdgeDistance(int siblingSize)
        {
            var span = new Span(new Run("MM") { FontSize = 20 });
            var block = new TextBlock(span)
            {
                Width = 300, Height = 100, FontSize = 20, Foreground = Brushes.Black,
            };
            block.Inlines.Add(new Run("MM") { FontSize = siblingSize });
            Css.SetStyle(span, "text-decoration: underline solid red 2px; text-underline-offset: 0px; text-underline-position: auto");
            CssBorderRadiusTests.Layout(block, 300, 100);
            var baseline = Assert.Single(RecordedRedLines(block));

            Css.SetStyle(span, "text-decoration: underline solid red 2px; text-underline-offset: 0px; text-underline-position: under");
            CssBorderRadiusTests.Layout(block, 300, 100);
            return Assert.Single(RecordedRedLines(block)) - baseline;
        }

        var small = EdgeDistance(20);
        var large = EdgeDistance(60);
        Assert.True(small > 1);
        Assert.Equal(small, large, 3);
    }

    [Fact]
    public void UnderlineOffsetAndPositionPaintInDirectAndInlineTextControls()
    {
        static (DependencyObject Source, FrameworkElement Visual) Block()
        {
            var text = new TextBlock { Text = "MMMM", FontSize = 24, Foreground = Brushes.Black };
            return (text, text);
        }
        static (DependencyObject Source, FrameworkElement Visual) Box()
        {
            var text = new TextBox { Text = "MMMM", FontSize = 24, Foreground = Brushes.Black };
            return (text, text);
        }
        static (DependencyObject Source, FrameworkElement Visual) Access()
        {
            var text = new AccessText { Text = "MMMM", FontSize = 24, Foreground = Brushes.Black };
            return (text, text);
        }
        static (DependencyObject Source, FrameworkElement Visual) Inline()
        {
            var run = new Run("MMMM");
            return (run, new TextBlock(run) { FontSize = 24, Foreground = Brushes.Black });
        }
        static (DependencyObject Source, FrameworkElement Visual) Rich()
        {
            var run = new Run("MMMM") { FontSize = 24, Foreground = Brushes.Black };
            return (run, new RichTextBox { Document = new FlowDocument(new Paragraph(run)) });
        }

        foreach (var create in new Func<(DependencyObject Source, FrameworkElement Visual)>[]
                 { Block, Box, Access, Inline, Rich })
        {
            var (source, visual) = create();
            visual.Width = 130;
            visual.Height = 70;
            Css.SetStyle(source, "text-decoration: underline solid red 2px; text-underline-offset: 0px");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            var zero = Assert.Single(RecordedRedLines(visual));

            Css.SetStyle(source, "text-decoration: underline solid red 2px; text-underline-offset: 8px");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            var shifted = Assert.Single(RecordedRedLines(visual));
            Assert.Equal(zero + 8, shifted, 5);

            Css.SetStyle(source, "text-decoration: underline solid red 2px; text-underline-offset: 0px; text-underline-position: under");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            var under = Assert.Single(RecordedRedLines(visual));
            Assert.True(under > zero + 1, $"{visual.GetType().Name}: under={under}, baseline={zero}");

            Css.SetStyle(source, "text-decoration: underline solid red 2px; text-underline-offset: 0px; text-underline-position: right");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            Assert.Equal(zero, Assert.Single(RecordedRedLines(visual)), 5);

            Css.SetStyle(source, "text-decoration: underline solid red 2px; text-underline-offset: 0px; text-underline-position: from-font");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            var fontMetrics = CssTextDecorationPainter.GetSourceFontMetrics(source, 24);
            var fontOrigin = (fontMetrics.Available & 32) != 0
                ? fontMetrics.UnderlinePosition : 0;
            Assert.Equal(zero + fontOrigin, Assert.Single(RecordedRedLines(visual)), 5);

            Css.SetStyle(source, "text-decoration: underline solid red 2px; text-underline-offset: 0px; text-underline-position: under right");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            Assert.Equal(under, Assert.Single(RecordedRedLines(visual)), 5);
        }
    }

    [Fact]
    public void SkipInsetShortensCssLinesAcrossDirectAndInlineTextControls()
    {
        static (DependencyObject Source, FrameworkElement Visual) Create(int kind)
        {
            if (kind == 0)
            {
                var block = new TextBlock { Text = "MMMM", FontSize = 24, Foreground = Brushes.Black };
                return (block, block);
            }
            if (kind == 1)
            {
                var box = new TextBox { Text = "MMMM", FontSize = 24, Foreground = Brushes.Black };
                return (box, box);
            }
            if (kind == 2)
            {
                var access = new AccessText { Text = "MMMM", FontSize = 24, Foreground = Brushes.Black };
                return (access, access);
            }
            var run = new Run("MMMM") { FontSize = 24, Foreground = Brushes.Black };
            return kind == 3
                ? (run, new TextBlock(run) { FontSize = 24, Foreground = Brushes.Black })
                : (run, new RichTextBox { Document = new FlowDocument(new Paragraph(run)) });
        }

        for (var kind = 0; kind < 5; kind++)
        {
            var (source, visual) = Create(kind);
            visual.Width = 130;
            visual.Height = 70;
            Css.SetStyle(source,
                "text-decoration: underline overline line-through solid red 8px; text-decoration-skip-inset: none");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            var full = RecordedRedStrokes(visual).OrderBy(static stroke => stroke.V1).ToArray();
            Assert.Equal(3, full.Length);

            Css.SetStyle(source,
                "text-decoration-skip-inset: auto; text-decoration: underline overline line-through solid red 8px");
            CssBorderRadiusTests.Layout(visual, 130, 70);
            var inset = RecordedRedStrokes(visual).OrderBy(static stroke => stroke.V1).ToArray();
            Assert.Equal(full.Length, inset.Length);
            Assert.Equal(CssTextDecorationSkipInset.Auto, CssTextDecorationProperties.SkipInset(source));
            for (var i = 0; i < full.Length; i++)
            {
                Assert.Equal(full[i].V0 + 4, inset[i].V0, 5);
                Assert.Equal(full[i].V2 - 4, inset[i].V2, 5);
            }
        }
    }

    [Fact]
    public void SkipInsetInheritsAndExplicitInheritTracksParent()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 70, FontSize = 24, Foreground = Brushes.Black,
        };
        var parent = new Border { Child = text, Width = 130, Height = 70 };
        Css.SetStyle(parent, "text-decoration-skip-inset: auto");
        Css.SetStyle(text, "text-decoration: underline solid red 8px");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(CssTextDecorationSkipInset.Auto, CssTextDecorationProperties.SkipInset(text));
        var inherited = Assert.Single(RecordedRedStrokes(text));

        Css.SetStyle(text, "text-decoration: underline solid red 8px; text-decoration-skip-inset: inherit");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(inherited.V0, Assert.Single(RecordedRedStrokes(text)).V0, 5);

        Css.SetStyle(parent, "text-decoration-skip-inset: none");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(CssTextDecorationSkipInset.None, CssTextDecorationProperties.SkipInset(text));
        var full = Assert.Single(RecordedRedStrokes(text));
        Assert.Equal(inherited.V0 - 4, full.V0, 5);
        Assert.Equal(inherited.V2 + 4, full.V2, 5);

        Css.SetStyle(parent, "text-decoration-skip-inset: auto");
        Css.SetStyle(text, "text-decoration-skip-inset: auto; all: initial");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(CssTextDecorationSkipInset.None, CssTextDecorationProperties.SkipInset(text));

        Css.SetStyle(text, "text-decoration-skip-inset: none; all: unset");
        CssBorderRadiusTests.Layout(parent, 130, 70);
        Assert.Equal(CssTextDecorationSkipInset.Auto, CssTextDecorationProperties.SkipInset(text));
    }

    [Fact]
    public void SkipInsetInheritsIntoInlineRunWithoutChangingNativeLocalLines()
    {
        var run = new Run("MMMM") { FontSize = 24, Foreground = Brushes.Black };
        var span = new Span(run);
        var block = new TextBlock(span)
        {
            Width = 130, Height = 70, FontSize = 24, Foreground = Brushes.Black,
        };
        Css.SetStyle(span, "text-decoration-skip-inset: auto");
        Css.SetStyle(run, "text-decoration: underline solid red 8px");
        CssBorderRadiusTests.Layout(block, 130, 70);
        Assert.Equal(CssTextDecorationSkipInset.Auto, CssTextDecorationProperties.SkipInset(run));
        var cssLine = Assert.Single(RecordedRedStrokes(block));

        run.TextDecorations = new TextDecorationCollection
        {
            new TextDecoration(TextDecorationLocation.Underline, Brushes.Red, 8, 0,
                TextDecorationUnit.Pixel, TextDecorationUnit.Pixel),
        };
        var nativeLine = Assert.Single(RecordedRedStrokes(block));
        Assert.Equal(cssLine.V0 - 4, nativeLine.V0, 5);
        Assert.Equal(cssLine.V2 + 4, nativeLine.V2, 5);
    }

    [Fact]
    public void CurrentColorTracksForegroundChanges()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 45, FontSize = 24,
            Foreground = Brushes.Blue,
        };
        Css.SetStyle(text, "text-decoration: underline currentColor 3px");
        CssBorderRadiusTests.Layout(text, 130, 45);
        Assert.True(ColoredPixels(text, red: false) > 0);

        text.Foreground = Brushes.Red;
        CssBorderRadiusTests.Layout(text, 130, 45);
        Assert.True(ColoredPixels(text, red: true) > 0);
        Assert.Equal(0, ColoredPixels(text, red: false));
    }

    [Fact]
    public void InheritedDecorationColorTracksTheParentsComputedColor()
    {
        var text = new TextBlock
        {
            Text = "MMMM", Width = 130, Height = 45, FontSize = 24,
            Foreground = Brushes.Blue,
        };
        var parent = new Border { Child = text, Width = 130, Height = 45 };
        Css.SetStyle(parent, "text-decoration-color: red");
        Css.SetStyle(text, "text-decoration-line: underline; text-decoration-color: inherit");
        CssBorderRadiusTests.Layout(parent, 130, 45);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(CssTextDecorationProperties.Color(text)).Color);

        Css.SetStyle(parent, "text-decoration-color: green");
        CssBorderRadiusTests.Layout(parent, 130, 45);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(CssTextDecorationProperties.Color(text)).Color);
    }

    [Fact]
    public void CssLineDoesNotFreezeTheNativeForegroundBrush()
    {
        var foreground = new SolidColorBrush(Colors.Blue);
        var text = new TextBlock { Text = "M", Foreground = foreground };
        Css.SetStyle(text, "text-decoration-line: underline");
        CssBorderRadiusTests.Layout(text, 80, 40);
        Assert.False(foreground.IsFrozen);
        foreground.Color = Colors.Red;
        Assert.Equal(Colors.Red, foreground.Color);
    }

    [Fact]
    public void ExistingTextDecorationTargetsRetainCssLineMappings()
    {
        var input = new TextBox();
        Css.SetStyle(input, "text-decoration: underline red 2px");
        Assert.Equal(TextDecorationLocation.Underline, Assert.Single(input.TextDecorations!).Location);

        var access = new AccessText();
        Css.SetStyle(access, "text-decoration-line: overline");
        Assert.Equal(TextDecorationLocation.OverLine, Assert.Single(access.TextDecorations).Location);

        var run = new Run("text");
        Css.SetStyle(run, "text-decoration: line-through blue 2px");
        var decoration = Assert.Single(run.TextDecorations!);
        Assert.Equal(TextDecorationLocation.Strikethrough, decoration.Location);
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(decoration.Brush).Color);

        Css.SetStyle(run, "text-decoration: none");
        Assert.Null(run.TextDecorations);
    }

    [Fact]
    public void TextBoxPaintsCssStyles_AndNativeLocalDecorationsTakePrecedence()
    {
        var input = new TextBox
        {
            Text = "MMMM", Width = 130, Height = 45, FontSize = 24,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(input, "text-decoration: underline dotted red 3px");
        CssBorderRadiusTests.Layout(input, 130, 45);
        Assert.True(ColoredPixels(input, red: true) > 0);

        Css.SetStyle(input, "text-decoration: underline wavy red 3px");
        CssBorderRadiusTests.Layout(input, 130, 45);
        Assert.True(ColoredPixels(input, red: true) > 0);

        input.TextDecorations = new TextDecorationCollection();
        Assert.Equal(0, ColoredPixels(input, red: true));
        input.ClearValue(TextBox.TextDecorationsProperty);
        Assert.True(ColoredPixels(input, red: true) > 0);
    }

    [Fact]
    public void AccessTextPaintsCssStyles_AndNativeLocalDecorationsTakePrecedence()
    {
        var access = new AccessText
        {
            Text = "MMMM", Width = 130, Height = 45, FontSize = 24,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(access, "text-decoration: underline dotted red 3px");
        CssBorderRadiusTests.Layout(access, 130, 45);
        Assert.True(ColoredPixels(access, red: true) > 0);

        Css.SetStyle(access, "text-decoration: underline wavy red 3px");
        CssBorderRadiusTests.Layout(access, 130, 45);
        Assert.True(ColoredPixels(access, red: true) > 0);

        access.TextDecorations = new TextDecorationCollection();
        Assert.Equal(0, ColoredPixels(access, red: true));
        access.ClearValue(AccessText.TextDecorationsProperty);
        Assert.True(ColoredPixels(access, red: true) > 0);
    }

    [Fact]
    public void RichTextBoxPaintsRunCssDecorations_AndNativeLocalDecorationsTakePrecedence()
    {
        var run = new Run("MMMM") { FontSize = 24, Foreground = Brushes.Black };
        var document = new FlowDocument(new Paragraph(run));
        var editor = new RichTextBox { Document = document, Width = 130, Height = 45 };
        Css.SetStyle(run, "text-decoration: underline wavy red 3px");
        CssBorderRadiusTests.Layout(editor, 130, 45);
        Assert.True(ColoredPixels(editor, red: true) > 0);

        editor.ClearRenderDirty();
        Css.SetStyle(run, "text-decoration: underline dotted red 3px");
        Assert.True(editor.IsRenderDirty);
        Assert.True(ColoredPixels(editor, red: true) > 0);

        editor.ClearRenderDirty();
        run.TextDecorations = new TextDecorationCollection();
        Assert.True(editor.IsRenderDirty);
        Assert.Equal(0, ColoredPixels(editor, red: true));
        run.ClearValue(TextElement.TextDecorationsProperty);
        Assert.True(ColoredPixels(editor, red: true) > 0);

        var replacement = new Run("MMMM") { FontSize = 24 };
        editor.Document = new FlowDocument(new Paragraph(replacement));
        editor.ClearRenderDirty();
        Css.SetStyle(run, "text-decoration: none");
        Assert.False(editor.IsRenderDirty);
        Css.SetStyle(replacement, "text-decoration: underline red 3px");
        Assert.True(editor.IsRenderDirty);
    }

    [Fact]
    public void SpanDecorationContinuesAcrossRuns_AndChildNoneDoesNotCancelIt()
    {
        var first = new Run("MM") { FontSize = 24, Foreground = Brushes.Black };
        var second = new Run("MM") { FontSize = 24, Foreground = Brushes.Blue };
        Css.SetStyle(second, "text-decoration: none");
        var span = new Span(first);
        var editor = new RichTextBox
        {
            Document = new FlowDocument(new Paragraph(span)), Width = 130, Height = 45,
        };
        Css.SetStyle(span, "text-decoration: underline dashed red 3px");
        CssBorderRadiusTests.Layout(editor, 130, 45);
        var firstEnd = MaxRedX(editor);
        Assert.True(firstEnd >= 0);

        span.Inlines.Add(second);
        CssBorderRadiusTests.Layout(editor, 130, 45);
        Assert.True(MaxRedX(editor) > firstEnd + 5);

        editor.ClearRenderDirty();
        span.TextDecorations = new TextDecorationCollection();
        Assert.True(editor.IsRenderDirty);
        Assert.Equal(0, ColoredPixels(editor, red: true));
        span.ClearValue(TextElement.TextDecorationsProperty);
        Assert.True(ColoredPixels(editor, red: true) > 0);
    }

    [Fact]
    public void TextBlockInlineRunsPaintTheirOwnDecorations_AndNativeLocalValueWins()
    {
        var first = new Run("MM");
        var second = new Run("MM");
        var block = new TextBlock(first)
        {
            Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black,
        };
        Css.SetStyle(first, "text-decoration: underline wavy red 3px");
        CssBorderRadiusTests.Layout(block, 130, 45);
        var firstEnd = MaxRedX(block);
        Assert.True(firstEnd >= 0);

        block.Inlines.Add(second);
        CssBorderRadiusTests.Layout(block, 130, 45);
        Assert.InRange(MaxRedX(block), firstEnd - 1, firstEnd + 1);

        Css.SetStyle(second, "color: blue; text-decoration: overline dotted currentColor 3px");
        Assert.True(ColoredPixels(block, red: false) > 0);
        Assert.True(MinBlueX(block) >= firstEnd - 2);

        block.ClearRenderDirty();
        first.TextDecorations = new TextDecorationCollection();
        Assert.True(block.IsRenderDirty);
        Assert.Equal(0, ColoredPixels(block, red: true));
        first.ClearValue(TextElement.TextDecorationsProperty);
        Assert.True(ColoredPixels(block, red: true) > 0);
    }

    [Fact]
    public void TextBlockSpanDecorationContinuesAcrossRunsAndWrappedLines()
    {
        var first = new Run("MM");
        var second = new Run("MM");
        Css.SetStyle(second, "text-decoration: none");
        var span = new Span(first);
        var block = new TextBlock(span)
        {
            Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black,
        };
        Css.SetStyle(span, "text-decoration: underline dashed red 3px");
        CssBorderRadiusTests.Layout(block, 130, 45);
        var firstEnd = MaxRedX(block);
        Assert.True(firstEnd >= 0);

        span.Inlines.Add(second);
        CssBorderRadiusTests.Layout(block, 130, 45);
        Assert.True(MaxRedX(block) > firstEnd + 5);

        block.Width = 50;
        block.Height = 100;
        block.TextWrapping = TextWrapping.Wrap;
        CssBorderRadiusTests.Layout(block, 50, 100);
        Assert.True(RedRowSpan(block, 50, 100) > 12);
    }

    [Fact]
    public void TextBlockInlineStylesheetSelectorTracksSiblingChanges()
    {
        var first = new Run("MM");
        var second = new Run("MM");
        var block = new TextBlock(first)
        {
            Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black,
        };
        block.Inlines.Add(second);
        Css.SetStyleSheet(block, "Run + Run { color:blue; text-decoration: underline wavy red 3px }");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        CssBorderRadiusTests.Layout(block, 130, 45);
        Assert.True(MinRedX(block) > 10);
        Assert.Contains(Colors.Blue, RecordedTextColors(block));

        block.Inlines.Remove(first);
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        CssBorderRadiusTests.Layout(block, 130, 45);
        Assert.Equal(0, ColoredPixels(block, red: true));
        Assert.DoesNotContain(Colors.Blue, RecordedTextColors(block));
    }

    [Fact]
    public void TextBlockRunForegroundPaintsItsOwnGlyphs_AndTracksCssChanges()
    {
        var first = new Run("MM");
        var second = new Run("MM");
        var block = new TextBlock(first)
        {
            Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black,
        };
        block.Inlines.Add(second);
        Css.SetStyle(second, "color:red");
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(second.Foreground).Color);
        CssBorderRadiusTests.Layout(block, 130, 45);
        Assert.Contains(Colors.Red, RecordedTextColors(block));

        second.Foreground = Brushes.Blue;
        Assert.DoesNotContain(Colors.Red, RecordedTextColors(block));
        Assert.Contains(Colors.Blue, RecordedTextColors(block));
        second.ClearValue(TextElement.ForegroundProperty);
        Assert.Contains(Colors.Red, RecordedTextColors(block));

        block.ClearRenderDirty();
        Css.SetStyle(second, "color:blue");
        Assert.True(block.IsRenderDirty);
        Assert.DoesNotContain(Colors.Red, RecordedTextColors(block));
        Assert.Contains(Colors.Blue, RecordedTextColors(block));
    }

    [Fact]
    public void TextBlockSpanForegroundFlowsToNestedRun()
    {
        var span = new Span(new Run("MMMM"));
        var block = new TextBlock(span)
        {
            Width = 130, Height = 45, FontSize = 24, Foreground = Brushes.Black,
        };
        Css.SetStyle(span, "color:red");
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(Assert.Single(span.Inlines).Foreground).Color);
        CssBorderRadiusTests.Layout(block, 130, 45);
        Assert.Contains(Colors.Red, RecordedTextColors(block));
    }

    private static Color[] RecordedTextColors(TextBlock block)
    {
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        block.Render(recorder);
        return TextColors(recorder.Commit()).ToArray();

        static IEnumerable<Color> TextColors(RecordedDrawing drawing)
        {
            foreach (var command in drawing.Commands)
            {
                if (command.Kind == DrawCommandKind.DrawText)
                    yield return Assert.IsType<SolidColorBrush>(
                        Assert.IsType<FormattedText>(command.A).Foreground).Color;
                else if (command.Kind == DrawCommandKind.DrawRecordedDrawing &&
                         command.A is RecordedDrawing nested)
                    foreach (var color in TextColors(nested)) yield return color;
            }
        }
    }

    private static IEnumerable<double> RecordedRedLines(Visual visual)
        => RecordedRedStrokes(visual).Select(static command => command.V1);

    private static IEnumerable<DrawCommand> RecordedRedStrokes(Visual visual)
        => RecordedStrokes(visual, Colors.Red);

    private static IEnumerable<DrawCommand> RecordedStrokes(Visual visual, Color color)
    {
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        visual.Render(recorder);
        return RedLines(recorder.Commit()).ToArray();

        IEnumerable<DrawCommand> RedLines(RecordedDrawing drawing)
        {
            foreach (var command in drawing.Commands)
            {
                if (command.Kind == DrawCommandKind.DrawLine &&
                    command.A is Pen { Brush: SolidColorBrush brush } && brush.Color == color)
                    yield return command;
                else if (command.Kind == DrawCommandKind.DrawRecordedDrawing &&
                         command.A is RecordedDrawing nested)
                    foreach (var stroke in RedLines(nested)) yield return stroke;
            }
        }
    }

    [Fact]
    public void ParagraphDecorationContinuesAcrossRuns_WithIndependentChildDecoration()
    {
        var first = new Run("MM") { FontSize = 24, Foreground = Brushes.Black };
        var second = new Run("MM") { FontSize = 24, Foreground = Brushes.Black };
        var paragraph = new Paragraph(first);
        var editor = new RichTextBox
        {
            Document = new FlowDocument(paragraph), Width = 130, Height = 45,
        };
        Css.SetStyle(paragraph, "text-decoration: underline dashed red 3px");
        CssBorderRadiusTests.Layout(editor, 130, 45);
        var firstEnd = MaxRedX(editor);
        Assert.True(firstEnd >= 0);

        Css.SetStyle(second, "text-decoration: overline wavy blue 3px");
        paragraph.Inlines.Add(second);
        CssBorderRadiusTests.Layout(editor, 130, 45);
        Assert.True(MaxRedX(editor) > firstEnd + 5);
        Assert.True(ColoredPixels(editor, red: false) > 0);

        editor.ClearRenderDirty();
        Css.SetStyle(paragraph, "text-decoration: underline dotted red 3px");
        Assert.True(editor.IsRenderDirty);

        editor.Width = 50;
        editor.Height = 100;
        CssBorderRadiusTests.Layout(editor, 50, 100);
        Assert.True(RedRowSpan(editor, 50, 100) > 12);
    }

    private static int RedRowSpan(Visual visual, int width, int height)
    {
        var pixels = CssBorderRadiusTests.Pixels(visual, width, height);
        var firstRedRow = height;
        var lastRedRow = -1;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 2] <= 180 || pixels[index + 1] >= 80 || pixels[index] >= 80)
                continue;
            var row = index / 4 / width;
            firstRedRow = Math.Min(firstRedRow, row);
            lastRedRow = Math.Max(lastRedRow, row);
        }
        return lastRedRow - firstRedRow;
    }

    private static int MaxRedX(Visual visual)
    {
        var pixels = CssBorderRadiusTests.Pixels(visual, 130, 45);
        var max = -1;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 2] > 180 && pixels[i + 1] < 80 && pixels[i] < 80)
                max = Math.Max(max, i / 4 % 130);
        return max;
    }

    private static int MinRedX(Visual visual)
    {
        var pixels = CssBorderRadiusTests.Pixels(visual, 130, 45);
        var min = 130;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 2] > 180 && pixels[i + 1] < 80 && pixels[i] < 80)
                min = Math.Min(min, i / 4 % 130);
        return min;
    }

    private static int MinBlueX(Visual visual)
    {
        var pixels = CssBorderRadiusTests.Pixels(visual, 130, 45);
        var min = 130;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] > 180 && pixels[i + 1] < 80 && pixels[i + 2] < 80)
                min = Math.Min(min, i / 4 % 130);
        return min;
    }

    private static int ColoredPixels(Visual visual, bool red)
    {
        var pixels = CssBorderRadiusTests.Pixels(visual, 130, 45);
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var redChannel = pixels[i + 2];
            if (pixels[i + 3] > 80 && green < 80 &&
                (red ? redChannel > 180 && blue < 80 : blue > 180 && redChannel < 80))
                count++;
        }
        return count;
    }

    private static int ErrorPixels(Visual visual, bool spelling)
    {
        if (spelling) return ColoredPixels(visual, red: true);
        var pixels = CssBorderRadiusTests.Pixels(visual, 130, 45);
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 80 && pixels[i + 1] > 80 &&
                pixels[i] < 80 && pixels[i + 2] < 80)
                count++;
        return count;
    }

    private static bool HasErrorWave(Visual visual, Color color)
    {
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        visual.Render(recorder);
        return ContainsWave(recorder.Commit());

        bool ContainsWave(RecordedDrawing drawing)
        {
            foreach (var command in drawing.Commands)
            {
                if (command.Kind == DrawCommandKind.DrawGeometry &&
                    command.B is Pen { Brush: SolidColorBrush brush } && brush.Color == color &&
                    command.C is PathGeometry geometry &&
                    geometry.Figures.Any(static figure =>
                        figure.Segments.Any(static segment => segment is QuadraticBezierSegment)))
                    return true;
                if (command.Kind == DrawCommandKind.DrawRecordedDrawing &&
                    command.A is RecordedDrawing nested && ContainsWave(nested))
                    return true;
            }
            return false;
        }
    }
}
