using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontEmojiTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("normal", 0)] [InlineData("text", 1)] [InlineData("emoji", 2)] [InlineData("unicode", 3)]
    [InlineData("EmOjI", 2)] [InlineData("e\\6d oji/**/", 2)]
    public void KeywordsUseRuntimeAndAuthoringGrammar(string value, int expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant-emoji", value));
        var text = Box(); Style(text, "font-variant-emoji:" + value);
        Assert.Equal((CssEmojiVariant)expected, Emoji(text));
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("font-variant-emoji").Support);
    }

    [Theory]
    [InlineData("")] [InlineData("none")] [InlineData("auto")] [InlineData("emoji text")]
    [InlineData("normal emoji")] [InlineData("'emoji'")] [InlineData("emoji()")] [InlineData("emoji,text")]
    public void InvalidValuesPreservePreviousDeclaration(string value)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variant-emoji", value));
        var text = Box(); Style(text, "font-variant-emoji:text;font-variant-emoji:" + value);
        Assert.Equal(CssEmojiVariant.Text, Emoji(text));
    }

    [Theory]
    [InlineData("text", "♥⌚☀©1", "♥\ufe0e⌚\ufe0e☀\ufe0e©\ufe0e1\ufe0e")]
    [InlineData("emoji", "♥⌚☀©1", "♥\ufe0f⌚\ufe0f☀\ufe0f©\ufe0f1\ufe0f")]
    [InlineData("unicode", "♥⌚☀©1", "♥\ufe0e⌚\ufe0f☀\ufe0e©\ufe0e1\ufe0e")]
    [InlineData("text", "♥\ufe0f⌚\ufe0f", "♥\ufe0f⌚\ufe0f")]
    [InlineData("emoji", "♥\ufe0e⌚\ufe0e", "♥\ufe0e⌚\ufe0e")]
    [InlineData("text", "A😀𐐨", "A😀𐐨")]
    [InlineData("emoji", "1\u20e3♥\u0301", "1\ufe0f\u20e3♥\ufe0f\u0301")]
    [InlineData("emoji", "♥\ufe00", "♥\ufe0f\ufe00")]
    public void ParagraphMatchesLiteralSelectorsAndKeepsSourceIndices(string mode, string source, string reference)
    {
        var text = Box(source); Style(text, "font-variant-emoji:" + mode);
        string key = text.FontFamily.GetRenderingSource(text);
        using var actual = Paragraph(source, key); using var expected = Paragraph(reference, "Arial");
        Assert.NotNull(actual); Assert.NotNull(expected);
        var line = Assert.Single(actual.Lines); var wanted = Assert.Single(expected.Lines);
        Assert.Equal(wanted.Metrics.Line.Width, line.Metrics.Line.Width, 3);
        Assert.Equal(wanted.Metrics.Baseline, line.Metrics.Baseline, 3);
        Assert.Equal(source, actual.Text);
        Assert.Equal((uint)source.Length, line.Metrics.Line.Length);
        Assert.Equal((uint)source.Length, actual.Caret(0, source.Length, false).TextPosition);
        Layout(text); text.SelectAll(); Assert.Equal(source, text.Text); Assert.Equal(source, text.SelectedText);
        Assert.Equal("Arial", text.FontFamily.Source);
    }

    [Theory] [InlineData("inherit", 2)] [InlineData("unset", 2)] [InlineData("initial", 0)]
    [InlineData("font:32px Arial", 0)] [InlineData("font-variant:normal", 0)] [InlineData("all:initial", 0)]
    public void FontAndGlobalResetsUseComputedParent(string value, int expected)
    {
        var text = Box(); var root = new Border { Child = text };
        Style(root, "font-variant-emoji:emoji");
        Style(text, value.Contains(':') ? value : "font-variant-emoji:" + value); Evaluate(root);
        Assert.Equal((CssEmojiVariant)expected, Emoji(text));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AuthoredSelectorAcrossSourcesOverridesCssAndInvalidatesAfterEdit(bool edge)
    {
        var flow = new StackPanel(); var first = Box("♥"); var second = Box("\ufe0e");
        flow.Children.Add(first); flow.Children.Add(second);
        Css.SetStyleSheet(flow, "TextBlock{display:inline}");
        Style(flow, "display:block;font-variant-emoji:emoji;font-family:Arial;font-size:32px;line-height:44px;text-autospace:no-autospace");
        if (edge) Style(second, "padding-left:1px;color:red");
        using var reference = Paragraph("♥\ufe0e", "Arial");
        foreach (int width in new[] { 80, 200, 80 })
        {
            Layout(flow, width);
            var range = first.GetSourceRangeSegments(0, 1);
            // A shared grapheme belongs to its final source. At a physical edge
            // it is painted separately, with the authored external selector.
            if (edge) Assert.Equal(reference!.Lines[0].Metrics.Line.Width, Assert.Single(range).EndX - range[0].StartX, 3);
            first.SelectAll(); Assert.Equal("♥", first.SelectedText);
        }
        second.Text = ""; Layout(flow);
        using var emoji = Paragraph("♥\ufe0f", "Arial");
        var restored = Assert.Single(first.GetSourceRangeSegments(0, 1));
        Assert.Equal(emoji!.Lines[0].Metrics.Line.Width, restored.EndX - restored.StartX, 3);
    }

    [Fact]
    public void EmojiIdentityRetainsOtherFeaturesAndUsesBoundedFragmentContext()
    {
        foreach (byte mode in new byte[] { 0, 1, 2, 3 }) foreach (byte after in new byte[] { 0, 1, 2 })
        {
            var context = new FontFeatureRenderingSource.CapsContext("lt", true, 12, 1, 2, true, 2, mode, after);
            string key = FontFeatureRenderingSource.Wrap("Arial:Font", [new(FontFeature.TagOf("kern"), 0)], CssCapsVariant.SmallCaps, context);
            key = FontWidthRenderingSource.ForFormattedText(key, 4);
            Assert.True(FontFeatureRenderingSource.TryUnwrap(key, out var features, out _, out var caps, out var decoded));
            Assert.Equal(context, decoded); Assert.Equal(CssCapsVariant.SmallCaps, caps); Assert.Single(features);
        }
        string fragment = FontFeatureRenderingSource.WithCapsFragment("Arial", 0, 0, 0, 1);
        Assert.True(FontFeatureRenderingSource.TryUnwrap(fragment, out _, out _, out _, out var decodedTail));
        Assert.Equal(1, decodedTail.EmojiAfter);
        Assert.Equal("Arial", FontFeatureRenderingSource.WithCapsFragment(fragment, 0, 0));
    }

    [Fact]
    public void VariablesComputedInvalidityAndStyleQueriesFollowTheLonghand()
    {
        var text = Box(); var root = new Border { Child = text };
        Style(root, "font-variant-emoji:emoji;--choice:text"); Style(text, "font-variant-emoji:var(--choice)"); Evaluate(root);
        Assert.Equal(CssEmojiVariant.Text, Emoji(text));
        var node = CssNode.Get(text); Assert.True(CssOrdinaryStyleQuery.Matches("font-variant-emoji", "text", node, default));
        Style(text, "font-variant-emoji:var(--missing)"); Assert.Equal(CssEmojiVariant.Emoji, Emoji(text));
        Assert.True(CssOrdinaryStyleQuery.Matches("font-variant-emoji", "inherit", node, default));
    }

    [Theory] [InlineData("text")] [InlineData("emoji")] [InlineData("unicode")]
    public void NonParticipatingCharactersKeepOrdinaryLayout(string mode)
    {
        var actual = Box("A😀𐐨"); var expected = Box("A😀𐐨");
        Style(actual, "font-variant-emoji:" + mode);
        foreach (int width in new[] { 40, 80, 240 })
        {
            Layout(actual, width); Layout(expected, width);
            Assert.Equal(expected.DesiredSize, actual.DesiredSize);
            Assert.Equal(expected.GetSourceRangeSegments(0, 5), actual.GetSourceRangeSegments(0, 5));
        }
    }

    [Theory] [InlineData("small-caps")] [InlineData("all-small-caps")]
    public void PositionCapsAndEmojiAlsoApplyPresentationSelectorsToDigits(string caps)
    {
        var actual = Box("aß123♥"); var expected = Box("aß1\ufe0f2\ufe0f3\ufe0f♥\ufe0f");
        string common = $"font-variant-caps:{caps};font-variant-position:super;font-variant-numeric:oldstyle-nums;";
        Style(actual, common + "font-variant-emoji:emoji"); Style(expected, common);
        foreach (int width in new[] { 40, 80, 240 })
        { Layout(actual, width); Layout(expected, width); Assert.Equal(expected.DesiredSize, actual.DesiredSize); }
    }

    [Theory] [InlineData("Arial")] [InlineData("Apple Color Emoji")] [InlineData("Times New Roman")]
    public void AuthoredSelectorUsesTheBaseFontAcrossFontChanges(string selectorFamily)
    {
        var text = Box("♥"); Style(text, "font-variant-emoji:emoji");
        string key = text.FontFamily.GetRenderingSource(text);
        using var actual = NativeTextParagraph.TryCreate([new("♥", key, 32, 400, 0, Colors.Black), new("\ufe0e", selectorFamily, 32, 400, 0, Colors.Black)],
            "Arial", 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
        using var expected = Paragraph("♥\ufe0e", "Arial");
        Assert.NotNull(actual); Assert.Equal(expected!.Lines[0].Metrics.Line.Width, actual.Lines[0].Metrics.Line.Width, 3);
    }

    [Fact]
    public void DiscreteAnimationsAndShorthandKeyframesUseAllComputedComponents()
    {
        var text = Box(); var property = CssFontEmoji.VariantProperty;
        Style(text, "font-variant-emoji:text"); var from = text.GetValue(property)!;
        Style(text, "font-variant-emoji:emoji;transition:font-variant-emoji 1s linear allow-discrete"); var to = text.GetValue(property)!;
        var data = Assert.IsType<CssTransitionData>(text.GetValue(CssTransitions.DataProperty)).Find(text, property)!;
        var animation = Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(text, property, from, to, data));
        var clock = animation.CreateClock();
        foreach (double progress in new[] { .1, .49, .5, .9, .1 })
        { clock.SetProgressForSampling(progress); Assert.Equal(progress < .5 ? from : to, animation.GetCurrentValue(from, to, clock)); }
        Css.SetStyleSheet(text, "@keyframes variant{from{font-variant:small-caps sub text}to{font-variant:all-small-caps super emoji}}"); Evaluate(text);
        var node = CssNode.Get(text); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "variant")!);
        Assert.Equal(CssFontVariant.Longhands.Length, tracks.Count);
        var sampler = new CssPropertySampler(property, tracks[property]);
        Assert.Equal(CssEmojiVariant.Text, sampler.Sample(.49, new("linear")));
        Assert.Equal(CssEmojiVariant.Emoji, sampler.Sample(.5, new("linear")));
    }

    [Fact]
    public void DocumentProjectionsAndMarkersInheritWithoutReplacingSourceNodes()
    {
        var run = new Run("♥⌚"); var span = new Span(run); var paragraph = new Paragraph(span);
        var document = new FlowDocument(paragraph) { FontFamily = "Arial", FontSize = 32 };
        using var first = new FlowDocumentPresentation(); using var second = new FlowDocumentPresentation();
        first.SetDocument(document); second.SetDocument(document);
        foreach (string mode in new[] { "emoji", "text", "unicode", "normal" })
        {
            Style(document, "font-variant:" + mode); Layout(first); Layout(second);
            Assert.Equal(Emoji(document), Emoji(run)); Assert.Same(span, run.Parent); Assert.Equal("♥⌚\n", document.GetText());
        }
        var item = Box("正文"); Style(item, "display:list-item;list-style-type:decimal;font-variant-emoji:emoji"); Evaluate(item);
        Assert.Equal(CssEmojiVariant.Emoji, Emoji(Assert.Single(CssGeneratedContent.Markers(CssNode.Get(item)))));
    }

    private static CssEmojiVariant Emoji(DependencyObject owner) => (CssEmojiVariant)owner.GetValue(CssFontEmoji.VariantProperty)!;
    private static TextBlock Box(string text = "♥⌚") => new() { Text = text, FontFamily = "Arial", FontSize = 32, IsTextSelectionEnabled = true };
    private static NativeTextParagraph? Paragraph(string text, string family) => NativeTextParagraph.TryCreate([new(text, family, 32, 400, 0, Colors.Black)], "Arial", 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
    private static void Style(DependencyObject owner, string css) { Css.SetStyle(owner, css); CssEngine.EvaluateElement(CssNode.Get(owner)); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view, int width = 200) { for (int i = 0; i < 3; i++) { view.Measure(new(width, 400)); view.Arrange(new(0, 0, width, 400)); view.UpdateLayout(); } }
}
