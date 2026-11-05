using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontWidthAnimationTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private static DependencyProperty Width => CssFontStretchValue.PercentageProperty;
    private const string Sample = "AVATAR office 012345 中文";
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("normal", 100)] [InlineData("83.2%", 83.2)] [InlineData("0%", 0)]
    [InlineData("calc(100% - 16.8%)", 83.2)] [InlineData("calc(-20%)", 0)]
    [InlineData("min(83.2%, 100%)", 83.2)] [InlineData("clamp(75%, 87.1%, 100%)", 87.1)]
    public void AliasesRetainComputedPercentagesAndClampMathAtComputedTime(string value, double expected)
    {
        foreach (string name in new[] { "font-width", "font-stretch" })
        {
            Assert.True(CssAuthoring.IsValueSupported(name, value));
            var box = Box(); Style(box, name + ":" + value);
            Assert.Equal(expected, Computed(box), 8);
            Assert.Equal(expected, (double)box.GetValue(Width)!, 8);
        }
    }

    [Theory]
    [InlineData("normal", 0)] [InlineData("1.5", 48)] [InlineData("150%", 48)]
    [InlineData("calc(1em + 6px)", 38)] [InlineData("calc(100% + 8px)", 40)]
    [InlineData("min(2em, 50px)", 50)] [InlineData("calc(1 + .25)", 40)]
    public void FontShorthandAcceptsNormalAndCalculatedLineHeights(string height, double pixels)
    {
        string css = "italic 600 condensed 32px/" + height + " 'SF Pro'";
        Assert.True(CssAuthoring.IsValueSupported("font", css));
        var box = Box(); Style(box, "font:" + css);
        Assert.Equal(32, box.FontSize); Assert.Equal(75, Computed(box));
        Assert.Equal(FontStyles.Italic, box.FontStyle); Assert.Equal(600, box.FontWeight.ToOpenTypeWeight());
        Assert.Equal("SF Pro", box.FontFamily.Source);
        var line = CssComputedLineHeightValue.Inherited(CssNode.Get(box));
        Assert.Equal(pixels, line.Kind == CssLineHeightKind.Number ? line.Value * box.FontSize : line.Value, 7);
    }

    [Theory]
    [InlineData("normal normal normal normal 20px serif")]
    [InlineData("normal bold italic condensed 20px/normal serif")]
    [InlineData("normal normal bold 20px/calc(1em + 1px) serif")]
    public void FontPrefixNormalTokensOccupyAtMostFourGrammarSlots(string value)
        => Assert.True(CssAuthoring.IsValueSupported("font", value));

    [Theory]
    [InlineData("normal normal normal normal normal 20px serif")]
    [InlineData("normal normal bold italic condensed 20px serif")]
    [InlineData("condensed expanded 20px serif")]
    [InlineData("20px/-1 serif")] [InlineData("20px/calc(1s) serif")]
    [InlineData("20px/normal/normal serif")] [InlineData("20px/normal")]
    [InlineData("75% 20px serif")]
    public void InvalidFontShorthandLeavesEveryEarlierLonghandIntact(string value)
    {
        Assert.False(CssAuthoring.IsValueSupported("font", value));
        var box = Box(); Style(box, "font-size:23px;font-width:83.2%;font-family:Seravek;font:" + value);
        Assert.Equal(23, box.FontSize); Assert.Equal(83.2, Computed(box), 8); Assert.Equal("Seravek", box.FontFamily.Source);
    }

    [Theory] [InlineData("font-width")] [InlineData("font-stretch")] [InlineData("all")]
    public void TransitionConfigurationAndSamplingUseOneContinuousNumericTrack(string property)
    {
        var box = Box(); Style(box, $"font-width:92.8%;transition:{property} 1s linear .2s");
        var data = Assert.IsType<CssTransitionData>(box.GetValue(CssTransitions.DataProperty)).Find(box, Width)!;
        Assert.NotNull(data); Assert.Equal(TimeSpan.FromMilliseconds(200), data.Delay);
        var animation = Assert.IsAssignableFrom<AnimationTimeline>(CssTransitions.Create(box, Width, 83.2, 92.8, data));
        var clock = animation.CreateClock(); var bucket = box.FontStretch;
        try
        {
            foreach (double progress in new[] { 0, .25, .5, .75, 1, .1 })
            {
                clock.SetProgressForSampling(progress);
                box.SetCssAnimatedValue(Width, animation.GetCurrentValue(83.2, 92.8, clock)); Layout(box);
                Assert.Equal(83.2 + 9.6 * progress, Computed(box), 7);
                Assert.Equal(bucket, box.FontStretch);
                AssertNativeMetrics(box, Computed(box));
            }
            Assert.Null(CssTransitions.Create(box, TextBlock.FontStretchProperty, FontStretches.Condensed,
                FontStretches.Expanded, data with { AllowDiscrete = true }));
        }
        finally { box.ClearCssAnimatedValue(Width); }
        Assert.Equal(92.8, Computed(box), 8);
    }

    [Theory] [InlineData("font-width")] [InlineData("font-stretch")]
    public void KeyframesMergeAliasOffsetsAndInterpolateExactPercentages(string property)
    {
        var box = Box(); Style(box, "font-width:95%");
        Css.SetStyleSheet(box, "@keyframes width{from{font-stretch:83.2%}50%{font-width:88%;font-stretch:87%}to{" + property + ":92.8%}}");
        try
        {
            Evaluate(box); var node = CssNode.Get(box); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "width")!);
            Assert.Single(tracks); Assert.False(tracks.ContainsKey(TextBlock.FontStretchProperty));
            var sampler = new CssPropertySampler(Width, tracks[Width]);
            foreach (var item in new[] { (0d, 83.2), (.25, 85.1), (.5, 87d), (.75, 89.9), (1d, 92.8) })
            { box.SetCssAnimatedValue(Width, sampler.Sample(item.Item1, new("linear"))); Layout(box); Assert.Equal(item.Item2, Computed(box), 7); AssertNativeMetrics(box, item.Item2); }
        }
        finally { box.ClearCssAnimatedValue(Width); Css.SetStyleSheets(box, null); }
        Assert.Equal(95, Computed(box));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void MissingKeyframeEndpointUsesInheritedOrOwnUnderlyingPercentage(bool inherited)
    {
        var root = new StackPanel(); var box = Box(); root.Children.Add(box);
        Style(inherited ? root : box, "font-width:83.2%");
        Css.SetStyleSheet(box, "@keyframes width{to{font-width:92.8%}}");
        try
        {
            Evaluate(root); var node = CssNode.Get(box); var rule = CssKeyframes.Find(node, "width")!;
            box.SetCssAnimatedValue(Width, 110d);
            var tracks = CssKeyframes.BuildPropertyTracks(node, rule); Assert.Equal(83.2, tracks[Width][0].Value);
            var sampler = new CssPropertySampler(Width, tracks[Width]);
            box.SetCssAnimatedValue(Width, sampler.Sample(.5, new("linear"))); Layout(root); Assert.Equal(88, Computed(box), 7);
        }
        finally { box.ClearCssAnimatedValue(Width); Css.SetStyleSheets(box, null); root.Children.Clear(); }
    }

    [Fact]
    public void KeywordEndpointsInterpolateTheirPercentagesAcrossNativeWidthClasses()
    {
        var box = Box(); Style(box, "font-width:normal"); Css.SetStyleSheet(box, "@keyframes width{from{font-width:condensed}to{font-stretch:expanded}}");
        try
        {
            Evaluate(box); var node = CssNode.Get(box); var tracks = CssKeyframes.BuildPropertyTracks(node, CssKeyframes.Find(node, "width")!);
            var sampler = new CssPropertySampler(Width, tracks[Width]);
            foreach (double progress in new[] { 0d, .25, .5, .75, 1d })
            {
                box.SetCssAnimatedValue(Width, sampler.Sample(progress, new("linear"))); Layout(box);
                Assert.Equal(75 + progress * 50, Computed(box), 7); AssertNativeMetrics(box, Computed(box));
            }
        }
        finally { box.ClearCssAnimatedValue(Width); Css.SetStyleSheets(box, null); }
    }

    [Fact]
    public void AnimatedWidthInheritanceHonorsLocalNativeAndExplicitCssOverrides()
    {
        var root = new StackPanel(); var parent = new StackPanel(); var child = Box(); root.Children.Add(parent); parent.Children.Add(child);
        Style(root, "font-width:83.2%"); Style(parent, "font-width:inherit"); Evaluate(root);
        try
        {
            parent.SetCssAnimatedValue(Width, 91.1d); Layout(root); Assert.Equal(91.1, Computed(child), 7);
            Style(child, "font-width:88%"); Assert.Equal(88, Computed(child));
            Style(child, ""); child.FontStretch = FontStretches.Condensed; Assert.Equal(75, Computed(child));
            child.ClearValue(TextBlock.FontStretchProperty); Assert.Equal(91.1, Computed(child), 7);
            parent.ClearCssAnimatedValue(Width); Assert.Equal(83.2, Computed(child), 7);
        }
        finally { parent.ClearCssAnimatedValue(Width); parent.Children.Clear(); root.Children.Clear(); }
    }

    [Fact]
    public void AnimatedWidthRecomputesFontRelativeRulersAndStyleQueries()
    {
        var root = new StackPanel(); var child = Box(); root.Children.Add(child); Css.SetClass(child, "target");
        Style(root, "font-width:83.2%"); Style(child, "width:10ch");
        Css.SetStyleSheet(root, "@container style(font-width:88%){.target{opacity:.25}}");
        try
        {
            foreach (double width in new[] { 88d, 91d, 88d })
            {
                root.SetCssAnimatedValue(Width, width); Layout(root);
                Assert.Equal(width, Computed(child), 7);
                Assert.Equal(width == 88 ? .25 : 1, child.Opacity, 3);
                Assert.Equal(10 * TextMeasurement.GetFontUnitMetrics(child.FontFamily.GetRenderingSource(child), child.FontSize).ZeroAdvance, child.Width, 3);
            }
        }
        finally { root.ClearCssAnimatedValue(Width); Css.SetStyleSheets(root, null); root.Children.Clear(); }
    }

    [Fact]
    public void ImportantWidthWinsOverKeyframesAndOutOfRangeAnimationIsClamped()
    {
        var box = Box(); Style(box, "font-width:88%!important");
        try
        {
            box.SetCssAnimatedValue(Width, 110d); Assert.Equal(88, Computed(box));
            Style(box, "font-width:88%"); box.SetCssAnimatedValue(Width, -10d); Assert.Equal(0, Computed(box));
            box.SetCssAnimatedValue(Width, 110d); Assert.Equal(110, Computed(box));
        }
        finally { box.ClearCssAnimatedValue(Width); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AuthoredAnimationRunnerUsesContinuousWidthAndRestoresItsUnderlyingStyle(bool inherited)
    {
        var root = new StackPanel(); var box = Box(); root.Children.Add(box); var target = inherited ? (DependencyObject)root : box;
        Style(target, "font-width:83.2%");
        Css.SetStyleSheet(root, "@keyframes width{from{font-width:83.2%}to{font-stretch:92.8%}}");
        try
        {
            foreach (double progress in new[] { .25, .5, .75 })
            {
                Style(target, "font-width:83.2%;animation:width 100s linear both paused;animation-delay:" +
                    (-progress * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s");
                Assert.NotNull(CssNode.Get(target).CssRuntimeState!.AnimationRunner);
                Layout(root); Assert.Equal(83.2 + 9.6 * progress, Computed(box), 6); AssertNativeMetrics(box, Computed(box));
            }
            Style(target, "font-width:83.2%;animation:none"); Layout(root); Assert.Equal(83.2, Computed(box), 7);
            Assert.False(target.HasCssAnimatedValue(Width));
        }
        finally { Css.SetStyleSheets(root, null); root.Children.Clear(); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AuthoredStyleChangeStartsOnePercentageTransitionAndCanBeReversed(bool inherited)
    {
        var root = new StackPanel(); var box = Box(); root.Children.Add(box);
        if (inherited) Style(root, "font-width:83.2%");
        Style(box, inherited ? "transition:font-width 100s linear" : "font-width:83.2%;transition:font-width 100s linear"); Layout(root);
        CssNode.Get(box).CssRuntimeState!.HadRenderedStyle = true;
        typeof(UIElement).GetMethod("ScheduleAutomaticTransitionArm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(box, null);
        try
        {
            Style(box, "font-width:92.8%;transition:font-stretch 100s linear");
            Assert.True(box.HasAutomaticTransition(Width)); Assert.False(box.HasAutomaticTransition(TextBlock.FontStretchProperty));
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            ((Jalium.UI.Animation.IFrameAnimatable)box).OnAnimationFrame(start);
            ((Jalium.UI.Animation.IFrameAnimatable)box).OnAnimationFrame(start + System.Diagnostics.Stopwatch.Frequency * 50);
            Assert.InRange(Computed(box), 87.99, 88.01);
            Style(box, "font-width:83.2%;transition:font-width 100s linear"); Assert.True(box.HasAutomaticTransition(Width));
            Assert.InRange(Computed(box), 87.99, 88.01);
        }
        finally { box.StopAutomaticTransition(Width, true); Css.SetStyleSheets(root, null); root.Children.Clear(); }
    }

    [Fact]
    public void DocumentProjectionAndMarkersFollowContinuousWidthWithoutEditingSource()
    {
        var run = new Run(Sample); var span = new Span(run); var document = new FlowDocument(new Paragraph(span)) { FontFamily = "SF Pro", FontSize = 32, PagePadding = new(0) };
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation(); one.SetDocument(document); two.SetDocument(document);
        Style(document, "font-width:83.2%");
        try
        {
            foreach (double width in new[] { 83.2, 88.1, 92.8, 83.2 })
            {
                document.SetCssAnimatedValue(Width, width); Layout(one); Layout(two);
                Assert.Equal(width, Computed(run), 7); Assert.Equal(one.DesiredSize, two.DesiredSize);
                Assert.Same(span, run.Parent); Assert.Equal(Sample + "\n", document.GetText());
            }
        }
        finally { document.ClearCssAnimatedValue(Width); }
        var item = Box(); Style(item, "display:list-item;font-width:83.2%"); Evaluate(item);
        var marker = Assert.Single(CssGeneratedContent.Markers(CssNode.Get(item)));
        try { item.SetCssAnimatedValue(Width, 91.2d); Evaluate(item); Assert.Equal(91.2, Computed(marker), 7); }
        finally { item.ClearCssAnimatedValue(Width); }
    }

    [Theory] [InlineData("root")] [InlineData("run")] [InlineData("span")] [InlineData("paragraph")] [InlineData("section")]
    public void DocumentKeyframesResolveInTheOriginalDocumentScopeForSharedReaders(string kind)
    {
        var run = new Run(Sample); var span = new Span(run); var paragraph = new Paragraph(span); var section = new Section(paragraph);
        var document = new FlowDocument(section) { FontFamily = "SF Pro", FontSize = 32, PagePadding = new(0) };
        using var one = new FlowDocumentPresentation(); using var two = new FlowDocumentPresentation(); one.SetDocument(document); two.SetDocument(document);
        DependencyObject target = kind switch { "run" => run, "span" => span, "paragraph" => paragraph, "section" => section, _ => document };
        Css.SetStyleSheet(document, "@keyframes width{from{font-width:83.2%}to{font-stretch:92.8%}}");
        try
        {
            foreach (double progress in new[] { .25, .5, 1d })
            {
                Style(target, "font-width:83.2%;animation:width 100s linear both paused;animation-delay:" +
                    (-progress * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s");
                Layout(one); Layout(two); Assert.Equal(83.2 + 9.6 * progress, Computed(run), 6);
                Assert.Equal(one.DesiredSize, two.DesiredSize); Assert.Same(span, run.Parent); Assert.Equal(Sample + "\n", document.GetText());
            }
        }
        finally { Css.SetStyle(target, ""); Css.SetStyleSheets(document, null); }
    }

    private void AssertNativeMetrics(TextBlock box, double width)
    {
        using var expected = _context.CreateTextFormat("SF Pro", 32, 400, 0, (float)width);
        Assert.True(expected.TrySetFontVariations([new(0x6f70737a, 32)]));
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(_context, box.FontFamily.GetRenderingSource(box), 32, 400, 0);
        Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, actual.MeasureText(Sample, 100000, 1000).Width, 3);
        foreach (uint position in new uint[] { 0, 1, 6, 15, (uint)Sample.Length })
        {
            Assert.True(expected.HitTestTextPosition(Sample, 100000, 1000, position, false, out var reference));
            Assert.True(actual.HitTestTextPosition(Sample, 100000, 1000, position, false, out var result));
            Assert.Equal(reference.CaretX, result.CaretX, 3); Assert.Equal(reference.CaretY, result.CaretY, 3);
        }
        Assert.Equal(Sample, box.Text);
    }

    private static TextBlock Box() => new() { Text = Sample };
    private static double Computed(DependencyObject owner) => CssFontStretchValue.Computed(CssNode.Get(owner));
    private static void Style(DependencyObject owner, string css) { Css.SetStyle(owner, "font-family:'SF Pro';font-size:32px;" + css); Evaluate(owner); }
    private static void Evaluate(DependencyObject owner) { CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner)); CssEvaluationScheduler.FlushIfPending(owner.Dispatcher); }
    private static void Layout(FrameworkElement view) { for (int i = 0; i < 3; i++) { Evaluate(view); view.Measure(new(500, 240)); view.Arrange(new(0, 0, 500, 240)); view.UpdateLayout(); } }
}
