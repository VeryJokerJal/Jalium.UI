using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Interop;
using Jalium.UI.Styling;
using Xunit.Abstractions;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTextStyleRangeSnapshotTests(ITestOutputHelper output)
{
    private static readonly AutomationTextStyle Style = new("Helvetica Neue", 21, 400, 0, 0xff102030);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SmallQueriesDoNotCopyTheLargeIntersectingOrOffRangeRun(int kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        const int count = 2 * 1024 * 1024;
        var source = new Source(new string('A', count) + "Z", [new(0, count, Style), new(count, 1, Style)]);
        int start = kind == 1 ? count : 1, length = kind == 2 ? 0 : 1;
        AssertBoundedQuery(source, start, length);
    }

    [Fact]
    public void OneCharacterQueriesDoNotAllocateAFontArrayForAllDocumentRuns()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        const int count = 100_000;
        var runs = Enumerable.Range(0, count).Select(i => new AutomationTextStyleSpan(i, 1, Style)).ToArray();
        AssertBoundedQuery(new Source(new string('A', count), runs), count / 2, 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PrivateProvenanceIsFilteredWithinTheRequestedRangeWithoutCopyingTheDocument(bool alternatingFonts)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        const int count = 100_000;
        var runs = Enumerable.Range(0, count).Select(i => new AutomationTextStyleSpan(i, 1,
            Style with { FontWeight = alternatingFonts && i % 2 == 1 ? 700 : 400, InlineContext = i.ToString("x32") })).ToArray();
        var source = new Source(new string('A', count), runs);
        foreach (bool includePrivate in new[] { false, true })
        {
            AssertBoundedQuery(source, count / 2, 1, includePrivate);
            AssertBoundedQuery(source, count / 2, 0, includePrivate);
            using var packet = JsonDocument.Parse(Snapshot(source, count / 2, 1, includePrivate)!);
            Assert.Equal(includePrivate, packet.RootElement.GetProperty("runs")[0].TryGetProperty("inlineContext", out _));
        }
        var publicRange = AutomationTextStyles.At(runs, count / 2, count, ignoreInlineContext: true)!.Value;
        Assert.Null(publicRange.Style.InlineContext);
        Assert.Equal(alternatingFonts ? 1 : count, publicRange.Length);
    }

    private void AssertBoundedQuery(Source source, int start, int length, bool includePrivate = true)
    {
        // Warm native format/JIT caches before measuring the sizing-and-copy
        // pair. A tiny AX read must not copy megabytes or allocate per document run.
        for (int i = 0; i < 2; ++i) Assert.NotNull(Snapshot(source, start, length, includePrivate));
        long before = GC.GetAllocatedBytesForCurrentThread();
        string first = Snapshot(source, start, length, includePrivate)!, second = Snapshot(source, start, length, includePrivate)!;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"two snapshots: {bytes} managed bytes; document={source.Text.Length}, query={start}:{length}");
        Assert.Equal(first, second);
        using var packet = JsonDocument.Parse(second);
        Assert.Equal(MemoryMarshal.AsBytes(source.Text.AsSpan(start, length)).ToArray(), packet.RootElement.GetProperty("text16").GetBytesFromBase64());
        if (length == 0) Assert.Empty(packet.RootElement.GetProperty("runs").EnumerateArray());
        else
        {
            var run = Assert.Single(packet.RootElement.GetProperty("runs").EnumerateArray());
            Assert.Equal(0, run.GetProperty("start").GetInt32()); Assert.Equal(length, run.GetProperty("length").GetInt32());
        }
        Assert.InRange(bytes, 0, 512 * 1024);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void QueriesOnlyDemandCssFontsForIntersectingCharacters(bool splitRuns)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var owner = new TextBlock { Text = "A" };
        var resolver = new PendingFont();
        Css.GetStyleSheets(owner).Add(CssStyleSheet.Parse(
            "@font-face{font-family:RangeDemand;src:url(range.otf);unicode-range:U+005A;font-display:swap}",
            "range snapshot fixture", new Uri("https://font.invalid/"), resolver));
        Css.SetStyle(owner, "font-family:RangeDemand,'Helvetica Neue'");
        CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);
        try
        {
            string family = owner.FontFamily.GetRenderingSource(owner);
            Assert.True(CssFontRenderingPlan.TryDecode(family, out var plan)); Assert.NotEqual(0, plan.RequestId);
            Assert.True(Css.WaitForFontsAsync(owner).IsCompletedSuccessfully);
            var style = Style with { FontFamily = family };
            var source = new Source("AZ", splitRuns ? [new(0, 1, style), new(1, 1, style)] : [new(0, 2, style)]);
            Assert.NotNull(Snapshot(source, 0, 0));
            Assert.True(Css.WaitForFontsAsync(owner).IsCompletedSuccessfully);
            Assert.NotNull(Snapshot(source, 0, 1)); Assert.NotNull(Snapshot(source, 0, 1));
            Assert.True(Css.WaitForFontsAsync(owner).IsCompletedSuccessfully);
            Assert.NotNull(Snapshot(source, 1, 1));
            Assert.False(Css.WaitForFontsAsync(owner).IsCompleted);
            Assert.True(resolver.Started.Wait(TimeSpan.FromSeconds(5)), "intersecting character must still demand its font");
        }
        finally { Css.SetStyleSheets(owner, null); Css.SetStyle(owner, string.Empty); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void NarrowAndEmptyQueriesStillValidateEveryStyleRun(int kind)
    {
        var runs = new AutomationTextStyleSpan[] { new(0, 1, Style), new(1, 1, Style) };
        runs[1] = kind switch
        {
            0 => runs[1] with { Style = Style with { FontSize = double.NaN } },
            1 => runs[1] with { Start = 0 },
            2 => runs[1] with { Length = int.MaxValue },
            3 => runs[1] with { Style = Style with { FontFamily = string.Empty } },
            4 => runs[1] with { Style = Style with { FontWeight = 0 } },
            5 => runs[1] with { Style = Style with { FontStyle = 3 } },
            _ => runs[1] with { Style = Style with { Direction = 2 } }
        };
        var source = new Source("AZ", runs);
        Assert.Null(Snapshot(source, 0, 1)); Assert.Null(Snapshot(source, 0, 0)); Assert.Null(Snapshot(source, 2, 0));
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(1, 2)] [InlineData(4, 2)]
    public void ClippingRetainsRawUtf16AndRelativeStyleOffsets(int start, int length)
    {
        var source = new Source("A🙂e\u0301中文", [new(0, 2, Style), new(2, 5, Style with { FontSize = 25 })]);
        using var packet = JsonDocument.Parse(Snapshot(source, start, length)!);
        Assert.Equal(MemoryMarshal.AsBytes(source.Text.AsSpan(start, length)).ToArray(), packet.RootElement.GetProperty("text16").GetBytesFromBase64());
        int through = 0;
        foreach (var run in packet.RootElement.GetProperty("runs").EnumerateArray())
        {
            Assert.Equal(through, run.GetProperty("start").GetInt32());
            int count = run.GetProperty("length").GetInt32();
            Assert.Equal(start + through < 2 ? 21 : 25, run.GetProperty("size").GetDouble()); through += count;
        }
        Assert.Equal(length, through); Assert.Equal(0, source.SelectionLength);
    }

    [Fact]
    public void TextEditedWhileCapturingStylesInvalidatesTheSnapshot()
    {
        var source = new Source("AZ", [new(0, 2, Style)]);
        source.OnStyles = () => source.Text = "BY";
        Assert.Null(Snapshot(source, 0, 1));
        Assert.Equal("BY", source.Text);
    }

    private static string? Snapshot(Source source, int start, int length, bool includePrivate = true) =>
        MacOSAccessibilityTree.CreateTextStyleSnapshot(source, start, length, () => true, includeInlineContext: includePrivate);

    private sealed class Source(string text, IReadOnlyList<AutomationTextStyleSpan> runs)
        : IAutomationTextProviderSource, IAutomationTextStyleSource
    {
        public string Text { get; set; } = text;
        internal Action? OnStyles;
        public IReadOnlyList<AutomationTextStyleSpan> GetTextStyles() { OnStyles?.Invoke(); return runs; }
        public int SelectionStart => 0;
        public int SelectionLength => 0;
        public bool IsReadOnly => false;
        public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;
        public void Select(int start, int length) => throw new InvalidOperationException("Snapshot changed selection");
        public IReadOnlyList<Rect> GetBoundingRectangles(int start, int length) => throw new NotSupportedException();
        public void ScrollIntoView(int start, int length) => throw new InvalidOperationException("Snapshot scrolled the source");
    }

    private sealed class PendingFont : ICssResourceResolver
    {
        internal readonly ManualResetEventSlim Started = new();
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Started.Set(); await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Pending fixture unexpectedly completed");
        }
    }
}
