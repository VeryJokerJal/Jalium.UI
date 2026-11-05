using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssLetterSpacingPercentageTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("normal", 0)] [InlineData("0", 0)] [InlineData("10%", 3.2)] [InlineData("-5%", -1.6)]
    [InlineData("0%", 0)] [InlineData("calc(10% + 2px)", 5.2)] [InlineData("min(10%, 2px)", 2)]
    [InlineData("max(-10%, -2px)", -2)] [InlineData("clamp(1px, 10%, 8px)", 3.2)]
    [InlineData("calc(.1em + 5%)", 4.8)] [InlineData("calc(1em - 10%)", 28.8)]
    public void LengthPercentageGrammarControlsActualShapedAdvances(string value, double spacing)
    {
        Assert.True(CssAuthoring.IsValueSupported("letter-spacing", value));
        var text = Box("AVATAR"); Style(text, "font-size:32px;letter-spacing:" + value); Layout(text);
        Assert.Equal(Natural(text.Text, 32) + 5 * spacing, Extent(text), 3);
        text.SelectAll(); Assert.Equal("AVATAR", text.SelectedText);
    }

    [Theory]
    [InlineData("auto")] [InlineData("none")] [InlineData("1")] [InlineData("2px 10%")]
    [InlineData("normal 0")] [InlineData("calc(1px + 1)")] [InlineData("10% 0")]
    [InlineData("'10%'")] [InlineData("calc(2% + 1s)")] [InlineData("normal, 0")]
    public void InvalidSpacingCannotDisplaceAValidCascadeValue(string value)
    {
        Assert.False(CssAuthoring.IsValueSupported("letter-spacing", value));
        var text = Box("AV"); Style(text, "font-size:32px;letter-spacing:10%;letter-spacing:" + value); Layout(text);
        Assert.Equal(Natural("AV", 32) + 3.2, Extent(text), 3);
    }

    [Theory]
    [InlineData("10%", 1.6, 4.8)] [InlineData(".1em", 3.2, 3.2)]
    [InlineData("calc(.1em + 5%)", 4, 5.6)] [InlineData("calc(2px + 10%)", 3.6, 6.8)]
    [InlineData("min(10%, .1em)", 1.6, 3.2)]
    public void InheritanceKeepsPercentagesAndTheDeclaringFontContext(string value, double at16, double at48)
    {
        var root = new StackPanel(); var text = Box("AV"); root.Children.Add(text);
        Style(root, "display:block;font-size:32px;letter-spacing:" + value); Style(text, "font-size:16px"); Layout(root);
        Assert.Equal(Natural("AV", 16) + at16, Extent(text), 3);
        text.FontSize = 48; Layout(root); Assert.Equal(Natural("AV", 48) + at48, Extent(text), 3);
        text.ClearValue(TextBlock.FontSizeProperty); Layout(root); Assert.Equal(Natural("AV", 16) + at16, Extent(text), 3);
        Assert.Same(root, text.Parent);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AdjacentFontSizesUseTheirOwnPercentageHalves(bool nested)
    {
        var text = Box(""); var a = new Run("A"); var v = new Run("V");
        if (nested) { var span = new Span(); span.Inlines.Add(a); text.Inlines.Add(span); }
        else text.Inlines.Add(a);
        text.Inlines.Add(v); Style(text, "font-size:32px;letter-spacing:10%"); Css.SetStyle(a, "font-size:16px"); Layout(text);
        string family = FontFeatureRenderingSource.Wrap("Seravek", [new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1)]);
        using var natural = NativeTextParagraph.TryCreate([new("A", family, 16, 400, 0, Colors.Black), new("V", family, 32, 400, 0, Colors.Black)],
            family, 32, 100000, 44, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(natural); Assert.Equal(natural.Lines[0].Metrics.Line.Width + 2.4, Extent(text), 3);
        Assert.Equal("AV", text.Text);
    }

    [Theory] [InlineData("10%", 3.2, 4.8)] [InlineData("2px", 2, 2)] [InlineData("normal", 0, 0)]
    public void SharedSourcesRebuildWhenOnlyTheirInheritedFontSizeChanges(string spacing, double at32, double at48)
    {
        var root = new StackPanel(); var first = Box("A"); var last = Box("V"); root.Children.Add(first); root.Children.Add(last);
        Css.SetStyleSheet(root, "TextBlock{display:inline}");
        try
        {
            foreach (int size in new[] { 32, 48, 32, 48 })
            {
                Style(root, $"display:block;font-size:{size}px;letter-spacing:{spacing}"); Layout(root);
                var end = last.GetSourceRangeSegments(0, 1).Single().EndX + last.TransformToAncestor(root).X;
                Assert.Equal(Natural("AV", size) + (size == 32 ? at32 : at48), end, 3);
            }
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Fact]
    public void UsedZeroSpacingRestoresOptionalLigaturesWhenTheFontBecomesZero()
    {
        var text = Box("ffi"); Style(text, "font-size:32px;letter-spacing:10%;font-feature-settings:normal"); Layout(text);
        Assert.Equal(0u, CssFontLigatures.SpacingFeatures(text).Single(feature => feature.Tag == FontFeature.TagOf("liga")).Value);
        text.FontSize = 0; Layout(text); Assert.Empty(CssFontLigatures.SpacingFeatures(text));
        text.ClearValue(TextBlock.FontSizeProperty); Layout(text);
        Assert.Equal(0u, CssFontLigatures.SpacingFeatures(text).Single(feature => feature.Tag == FontFeature.TagOf("liga")).Value);
    }

    [Theory] [InlineData("letter-spacing")] [InlineData("word-spacing")]
    public void ContainerQueriesCompareComputedValuesInsteadOfCoincidentUsedPixels(string property)
    {
        var root = new StackPanel(); var text = Box("AV"); root.Children.Add(text); Css.SetClass(text, "target");
        Css.SetStyleSheet(root, $"@container style({property}:10%){{.target{{opacity:.25}}}} @container style({property}:2px){{.target{{opacity:.75}}}}");
        try
        {
            foreach (var item in new[] { ("10%", .25), ("2px", .75), ("calc(1px + 1px)", .75), ("10%", .25) })
            { Style(root, "display:block;font-size:20px;" + property + ":" + item.Item1); Layout(root); Assert.Equal(item.Item2, text.Opacity, 3); }
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [Fact]
    public void MixedTransitionTracksAChangedFontAndRestoresTheAuthorValue()
    {
        var text = Box("AV"); Style(text, "font-size:32px;letter-spacing:10%"); var from = text.GetValue(CssFlowProperties.LetterSpacingProperty)!;
        Style(text, "font-size:32px;letter-spacing:8px"); var to = text.GetValue(CssFlowProperties.LetterSpacingProperty)!;
        var animation = Assert.IsType<CssTextSpacingAnimation>(CssTransitions.Create(text, CssFlowProperties.LetterSpacingProperty, from, to,
            new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(200), new("linear"), false)));
        Assert.Equal(TimeSpan.FromSeconds(2), animation.Duration.TimeSpan); Assert.Equal(TimeSpan.FromMilliseconds(200), animation.BeginTime);
        var clock = animation.CreateClock(); clock.SetProgressForSampling(.5);
        text.SetCssAnimatedValue(CssFlowProperties.LetterSpacingProperty, animation.GetCurrentValue(from, to, clock)); Layout(text);
        Assert.Equal(Natural("AV", 32) + 5.6, Extent(text), 3);
        text.FontSize = 64; Layout(text); Assert.Equal(Natural("AV", 64) + 7.2, Extent(text), 3);
        text.ClearCssAnimatedValue(CssFlowProperties.LetterSpacingProperty); Layout(text); Assert.Equal(Natural("AV", 64) + 8, Extent(text), 3);
    }

    [Theory] [InlineData("10%", "8px", 5.6, 7.2)]
    [InlineData("clamp(2px, 10%, 4px)", "max(10%, 1em)", 17.6, 18)]
    public void AuthoredKeyframesInterpolateComputedLengthsAndUnresolvedPercentages(string before, string after, double at32, double at64)
    {
        var text = Box("AV"); Style(text, "font-size:32px;letter-spacing:normal");
        Css.SetStyleSheet(text, $"@keyframes tracking{{from{{letter-spacing:{before}}}to{{letter-spacing:{after}}}}}");
        try
        {
            Layout(text); var node = CssNode.Get(text); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "tracking")!);
            var sampler = new CssPropertySampler(CssFlowProperties.LetterSpacingProperty, tracks[CssFlowProperties.LetterSpacingProperty]);
            text.SetCssAnimatedValue(CssFlowProperties.LetterSpacingProperty, sampler.Sample(.5, new("linear"))); Layout(text);
            Assert.Equal(Natural("AV", 32) + at32, Extent(text), 3);
            text.FontSize = 64; Layout(text); Assert.Equal(Natural("AV", 64) + at64, Extent(text), 3);
        }
        finally { text.ClearCssAnimatedValue(CssFlowProperties.LetterSpacingProperty); Css.SetStyleSheets(text, null); }
    }

    [Fact]
    public void ReversingMixedTrackingRepeatedlyKeepsAUsableComputedValue()
    {
        var text = Box("AV"); Style(text, "font-size:32px;letter-spacing:10%"); object from = text.GetValue(CssFlowProperties.LetterSpacingProperty)!;
        for (int i = 0; i < 200; i++)
        {
            object to = i % 2 == 0 ? 8d : CssLayoutLength.Percent(.1);
            var animation = Assert.IsType<CssTextSpacingAnimation>(CssTextSpacing.Animation(CssFlowProperties.LetterSpacingProperty, from, to));
            var clock = animation.CreateClock(); clock.SetProgressForSampling(.5); from = animation.GetCurrentValue(from, to, clock);
            text.SetCssAnimatedValue(CssFlowProperties.LetterSpacingProperty, from); Layout(text);
            Assert.InRange(Extent(text) - Natural("AV", 32), 3.199, 8.001);
        }
        text.ClearCssAnimatedValue(CssFlowProperties.LetterSpacingProperty);
    }

    private static TextBlock Box(string value) => new() { Text = value, IsTextSelectionEnabled = true };
    private static void Style(DependencyObject target, string css) => Css.SetStyle(target,
        "font-family:Seravek;white-space:pre;text-autospace:no-autospace;font-kerning:normal;font-feature-settings:'liga' 1;" + css);
    private double Natural(string text, double size)
    {
        using var font = _context.CreateTextFormat("Seravek", (float)size, 400, 0); font.SetFontFeatures([new(FontFeature.TagOf("kern"), 1), new(FontFeature.TagOf("liga"), 1)]);
        return font.MeasureText(text, 100000, 1000).WidthIncludingTrailingWhitespace;
    }
    private static double Extent(TextBlock text)
    { var ranges = text.GetSourceRangeSegments(0, text.Text.Length); return ranges.Max(range => range.EndX) - ranges.Min(range => range.StartX); }
    private static void Layout(FrameworkElement view)
    { for (int i = 0; i < 3; i++) { CssEvaluationScheduler.FlushIfPending(view.Dispatcher); view.Measure(new(1000, 400)); view.Arrange(new(0, 0, 1000, 400)); view.UpdateLayout(); } }
}
