using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWritingToolsPresentationBoundaryTests
{
    [Theory]
    [InlineData(RenderBackend.Metal)]
    [InlineData(RenderBackend.Software)]
    public void OverlappingAndAdjacentRequestsUseOneExactNativeInterval(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
        var owner = new TextBox(); var source = new Source { Text = "0123456789abcdef" };
        try
        {
            foreach (var (start, count) in new[] { (9, 2), (1, 3), (2, 7), (11, 2) })
                MacOSWritingToolsPresentation.Prepare(owner, source, "range" + start, start, count, 0, () => true);
            var text = new FormattedText(source.Text, "Helvetica Neue", 18);
            MacOSWritingToolsPresentation.ApplyGlyphRanges(owner, text, 0);
            Assert.Equal(2, text.GlyphVisibility); Assert.Equal(new uint[] { 1, 12 }, text.GlyphRanges);
            var clipped = new FormattedText(source.Text[5..12], "Helvetica Neue", 18);
            MacOSWritingToolsPresentation.ApplyGlyphRanges(owner, clipped, 5);
            Assert.Equal(new uint[] { 0, 7 }, clipped.GlyphRanges);
        }
        finally { MacOSWritingToolsPresentation.Clear(owner); }
    }

    [Theory]
    [InlineData(RenderBackend.Metal, false)]
    [InlineData(RenderBackend.Software, false)]
    [InlineData(RenderBackend.Metal, true)]
    [InlineData(RenderBackend.Software, true)]
    public void ExcessRequestsStayBalancedWithoutExceedingTheNativeBudget(RenderBackend backend, bool sameToken)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
        var owner = new TextBox(); var source = new Source { Text = new string('x', 10000) };
        string Token(int i) => sameToken ? "shared" : "range" + i;
        uint[] Ranges()
        {
            var text = new FormattedText(source.Text, "Helvetica Neue", 18);
            MacOSWritingToolsPresentation.ApplyGlyphRanges(owner, text, 0);
            return text.GlyphRanges ?? [];
        }
        try
        {
            for (int i = 0; i < 5000; i++) MacOSWritingToolsPresentation.Prepare(owner, source, Token(i), i * 2, 1, 0, () => true);
            var ranges = Ranges(); Assert.Equal(8192, ranges.Length); Assert.Equal(8190u, ranges[^2]);
            Assert.All(ranges.Where((_, i) => i % 2 == 1), length => Assert.Equal(1u, length));
            if (sameToken)
            {
                for (int i = 4999; i >= 4096; --i) MacOSWritingToolsPresentation.Finish(owner, Token(i));
                Assert.Equal(ranges, Ranges()); // Finishing deferred requests cannot pop a presented one.
            }
            else
            {
                MacOSWritingToolsPresentation.Finish(owner, Token(0));
                ranges = Ranges(); Assert.Equal(2u, ranges[0]); Assert.Equal(8192u, ranges[^2]);
            }
            for (int i = 0; i < 5000; i++) MacOSWritingToolsPresentation.Finish(owner, Token(i));
            Assert.False(MacOSWritingToolsPresentation.IsPresenting(owner)); Assert.Empty(Ranges());
        }
        finally { MacOSWritingToolsPresentation.Clear(owner); }
    }

    public static IEnumerable<object[]> Edits()
    {
        foreach (string[] edit in new[]
        {
            new[] { "😀B", "😁D" }, new[] { "A😀", "D🈀" }, new[] { "e\u0301B", "e\u0300D" },
            new[] { "A\r\nB", "D\nC" }, new[] { "👩‍💻B", "👩‍🔬D" },
            new[] { "A🇨🇳", "D🇺🇳" }, new[] { "A👍🏻B", "A👍🏿D" }, new[] { "1️⃣B", "2️⃣D" },
            new[] { "keep e\u0301 middle 👩‍💻 tail", "keep e\u0300 middle 👩‍🔬 tail" },
            new[] { "A B", "A e\u0301B" }, new[] { "A e\u0301B", "A B" }, new[] { "😀B", "" }
        }) foreach (bool endRange in new[] { false, true }) yield return [edit[0], edit[1], endRange];
    }
    [Theory]
    [MemberData(nameof(Edits))]
    public void RemappedAnimationNeverCutsAComposedCharacter(string old, string changed, bool endRange)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal, forceReplace: true);
        var owner = new TextBox(); var source = new Source { Text = old };
        int start = endRange ? GraphemeClusters.PreviousBoundary(old, old.Length) : 0;
        int count = endRange ? old.Length - start : GraphemeClusters.NextBoundary(old, 0);
        try
        {
            MacOSWritingToolsPresentation.Prepare(owner, source, "edit", start, count, 0, () => true);
            source.Text = changed; MacOSWritingToolsPresentation.ObserveText(owner, changed);
            var text = new FormattedText(changed, "Helvetica Neue", 18);
            MacOSWritingToolsPresentation.ApplyGlyphRanges(owner, text, 0);
            var ranges = text.GlyphRanges ?? [];
            if (changed.Length != 0) Assert.NotEmpty(ranges);
            for (int i = 0; i < ranges.Length; i += 2)
            {
                Assert.True(GraphemeClusters.IsBoundary(changed, (int)ranges[i]));
                Assert.True(GraphemeClusters.IsBoundary(changed, (int)(ranges[i] + ranges[i + 1])));
            }
            Assert.Equal(changed, source.Text);
            MacOSWritingToolsPresentation.Finish(owner, "edit"); Assert.False(MacOSWritingToolsPresentation.IsPresenting(owner));
        }
        finally { MacOSWritingToolsPresentation.Clear(owner); }
    }

    private sealed class Source : IAutomationTextProviderSource
    {
        public string Text { get; set; } = "";
        public int SelectionStart => 0;
        public int SelectionLength => 0;
        public bool IsReadOnly => false;
        public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;
        public void Select(int start, int length) { }
        public IReadOnlyList<Rect> GetBoundingRectangles(int start, int length) => [];
        public void ScrollIntoView(int start, int length) { }
    }
}
