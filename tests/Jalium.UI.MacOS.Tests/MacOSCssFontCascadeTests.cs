using System.Collections.Concurrent;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSCssFontCascadeTests : MacOSGeometryTestBase
{
    private const string Mixed = "abc אבג tail";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CssInstalledList_UsesEveryFamilyInOrder(bool reverse)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        string hebrew = reverse ? "New Peninim MT" : "Arial Hebrew";
        var box = Owner(Mixed, "", $"'__Missing, Font43__', 'Avenir Next', '{hebrew}', '{(reverse ? "Arial Hebrew" : "New Peninim MT")}'");
        try
        {
            string source = Source(box);
            Assert.Contains(hebrew, source);
            using var expected = TextMeasurement.CreateTextFormatFromFamilyList(context, $"Avenir Next, {hebrew}", 20, 400, 0);
            Assert.Equal(expected.MeasureText(Mixed, 100000, 1000).Width, Measure(source, Mixed));
        }
        finally { Clear(box); }
    }

    [Fact]
    public void UnusedLaterFace_DoesNotDownloadUntilActualCaptionNeedsItsGlyphs()
    {
        using var context = new RenderContext(RenderBackend.Metal); var resolver = new Resolver();
        var owner = Owner("Latin", "@font-face {font-family: Later43; src:url('hebrew.ttf'); font-display:swap;}",
            "'Avenir Next', Later43, 'New Peninim MT'", resolver);
        try
        {
            string source = Source(owner); Measure(source, "Latin"); Assert.Empty(resolver.Requested);
            // Drawing/measurement can receive a placeholder distinct from owner.Text.
            Measure(source, "אבג"); resolver.Await("hebrew.ttf");
            resolver.Complete("hebrew.ttf", "/System/Library/Fonts/ArialHB.ttc"); Wait(owner);
            using var expected = context.CreateTextFormat("Arial Hebrew", 20, 400, 0);
            Assert.Equal(expected.MeasureText("אבג", 100000, 1000).Width, Measure(source, "אבג"));
            Assert.Single(resolver.Requested);
        }
        finally { resolver.CompleteAll(); Clear(owner); }
    }

    [Fact]
    public void MissingLoadedGlyph_StartsNextWebfontOnlyAfterFirstFaceFinishes()
    {
        using var context = new RenderContext(RenderBackend.Metal); var resolver = new Resolver();
        var owner = Owner("אבג", """
            @font-face {font-family: Earlier43; src:url('mono.ttf'); unicode-range:U+590-5FF; font-display:swap;}
            @font-face {font-family: Later43; src:url('hebrew.ttf'); unicode-range:U+590-5FF; font-display:swap;}
            """, "Earlier43, Later43, 'New Peninim MT'", resolver);
        try
        {
            string source = Source(owner); resolver.Await("mono.ttf"); Assert.DoesNotContain("hebrew.ttf", resolver.Requested);
            resolver.Complete("mono.ttf", "/System/Library/Fonts/Supplemental/Andale Mono.ttf"); Wait(owner);
            Measure(source, owner.Text); resolver.Await("hebrew.ttf");
            resolver.Complete("hebrew.ttf", "/System/Library/Fonts/ArialHB.ttc"); Wait(owner);
            Assert.Equal(Measure("Arial Hebrew", owner.Text), Measure(source, owner.Text));
        }
        finally { resolver.CompleteAll(); Clear(owner); }
    }

    [Fact]
    public void CanonicalCluster_UsesPrecomposedGlyphFromDeclaredRange()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = Owner("A\u0301", "@font-face {font-family: Canonical43; src:local('Andale Mono'); unicode-range:U+C1; font-display:swap;}", "Canonical43, Helvetica");
        try
        {
            Source(owner); Wait(owner); var source = Source(owner);
            Assert.Equal(Measure("Andale Mono", owner.Text), Measure(source, owner.Text));
            using var actual = Layout(source, owner.Text); using var expected = Layout("Andale Mono", owner.Text);
            Assert.Equal(expected.Caret(0, owner.Text.Length, false).X, actual.Caret(0, owner.Text.Length, false).X);
        }
        finally { Clear(owner); }
    }

    [Fact]
    public void CompositeSubsets_OnlyRequestUsedRangesAndChooseDescriptorsBeforeUnicode()
    {
        using var context = new RenderContext(RenderBackend.Metal); var resolver = new Resolver();
        var owner = Owner("אבג", """
            @font-face {font-family: Split43; src:url('latin.ttf'); unicode-range:U+0-7F; font-display:swap;}
            @font-face {font-family: Split43; src:url('hebrew.ttf'); unicode-range:U+590-5FF; font-display:swap;}
            @font-face {font-family: Split43; src:url('bold.ttf'); font-weight:700; unicode-range:U+0-7F; font-display:swap;}
            """, "Split43, 'New Peninim MT'", resolver);
        try
        {
            Measure(Source(owner), owner.Text); resolver.Await("hebrew.ttf");
            Assert.Equal(["hebrew.ttf"], resolver.Requested.Order().ToArray());
            Css.SetStyle(owner, "font-family:Split43, 'New Peninim MT'; font-weight:700");
            string bold = Source(owner); Measure(bold, owner.Text);
            Assert.DoesNotContain("bold.ttf", resolver.Requested); Assert.DoesNotContain("latin.ttf", resolver.Requested);
            Assert.True(CssFontRenderingPlan.TryDecode(bold, out var plan));
            Assert.DoesNotContain(plan.Faces, face => face.Waiting);
        }
        finally { resolver.CompleteAll(); Clear(owner); }
    }

    [Theory]
    [InlineData("block")] [InlineData("swap")]
    public void PartialDisplay_PreservesMeasurementCaretsAndSelection(string display)
    {
        using var context = new RenderContext(RenderBackend.Metal); var resolver = new Resolver();
        var owner = Owner(Mixed, $"@font-face {{font-family: Partial43; src:url('hebrew.ttf'); unicode-range:U+590-5FF; font-display:{display};}}",
            "'Avenir Next', Partial43, 'New Peninim MT'", resolver);
        try
        {
            string source = Source(owner); resolver.Await("hebrew.ttf");
            Assert.False(CssFontFaces.IsBlocked(source));
            Assert.True(CssFontRenderingPlan.TryDecode(source, out var plan));
            Assert.Equal(display == "block", Assert.Single(plan.Faces, face => face.Waiting).Blocked);
            using var actual = Layout(source, Mixed); using var reference = Layout("Avenir Next, New Peninim MT", Mixed);
            for (int position = 0; position <= Mixed.Length; position++)
            {
                Assert.Equal(reference.Caret(0, position, false).X, actual.Caret(0, position, false).X);
                Assert.True(TextMeasurement.HitTestTextPositionWrapped(Mixed, source, 20, 400, 0, float.PositiveInfinity,
                    (uint)position, false, out var caret));
                Assert.Equal(reference.Caret(0, position, false).X, caret.CaretX);
            }
            var expected = reference.Selection(0, 4, 3); var selected = actual.Selection(0, 4, 3);
            Assert.Equal(expected.Select(range => (range.X, range.Width)), selected.Select(range => (range.X, range.Width)));
            Assert.Equal(Measure("Avenir Next, New Peninim MT", Mixed), Measure(source, Mixed));
        }
        finally { resolver.CompleteAll(); Clear(owner); }
    }

    [Theory]
    [InlineData("abc אְבּג tail")] [InlineData("abc אבג 👩‍👩‍👧‍👦 🇨🇳 tail")]
    public void LoadedSubset_CombiningAndEmojiStillUseOrderedFallback(string text)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = Owner(text, "@font-face {font-family: Hebrew43; src:local('Arial Hebrew'); unicode-range:U+590-5FF; font-display:swap;}",
            "'Avenir Next', Hebrew43, 'New Peninim MT'");
        try
        {
            Source(owner); Wait(owner); string source = Source(owner);
            using var actual = Layout(source, text); using var expected = Layout("Avenir Next, Arial Hebrew, New Peninim MT", text);
            for (int i = 0; i <= text.Length; i++) Assert.Equal(expected.Caret(0, i, false).X, actual.Caret(0, i, false).X);
        }
        finally { Clear(owner); }
    }

    [Fact]
    public void LoadedUnicodeRange_ExcludesCharactersPresentInUnderlyingFont()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = Owner(Mixed, "@font-face {font-family: Latin43; src:local('Arial Hebrew'); unicode-range:U+0-7F; font-display:swap;}",
            "Latin43, 'Avenir Next', 'New Peninim MT'");
        try
        {
            // Demand Latin directly so the face is ready while its Hebrew is excluded.
            Measure(Source(owner), "abc"); Wait(owner);
            Css.SetStyle(owner, "font-family:'Avenir Next', Latin43, 'New Peninim MT'");
            using var actual = Layout(Source(owner), Mixed); using var expected = Layout("Avenir Next, New Peninim MT", Mixed);
            Assert.Equal(expected.Caret(0, Mixed.Length, false).X, actual.Caret(0, Mixed.Length, false).X);
        }
        finally { Clear(owner); }
    }

    [Fact]
    public void FaceDeclaration_ShadowsInstalledFamilyEvenWhenLoadedCmapLacksGlyph()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = Owner("אבג", "@font-face {font-family:'Arial Hebrew'; src:local('Andale Mono'); unicode-range:U+590-5FF; font-display:swap;}",
            "'Arial Hebrew', 'New Peninim MT'");
        try
        {
            Source(owner); Wait(owner); string source = Source(owner);
            Assert.Equal(Measure("New Peninim MT", owner.Text), Measure(source, owner.Text));
            Assert.NotEqual(Measure("Arial Hebrew", owner.Text), Measure(source, owner.Text));
        }
        finally { Clear(owner); }
    }

    [Fact]
    public void DescriptorWeight_ClampsFaceAndMetricsUseFirstSpaceEligibleFont()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = Owner("abc", "@font-face {font-family: Weight43; src:local('Avenir Next'); font-weight:400; font-display:swap;}", "Weight43, Helvetica");
        try
        {
            Css.SetStyle(owner, "font-family:Weight43, Helvetica; font-weight:900"); Source(owner); Wait(owner);
            string source = Source(owner); Assert.Equal(Measure("Avenir Next", "abc"), Measure(source, "abc", 900));
            Assert.NotEqual(Measure("Avenir Next", "abc", 900), Measure(source, "abc", 900));
            Css.SetStyleSheets(owner, null);
            Css.SetStyleSheet(owner, "@font-face {font-family: NoSpace43; src:local('Andale Mono'); unicode-range:U+41-5A; font-display:swap;}");
            Css.SetStyle(owner, "font-family:NoSpace43, Helvetica"); owner.Text = "ABC";
            Source(owner); Wait(owner); source = Source(owner);
            var expected = TextMeasurement.GetFontMetrics("Helvetica", 20);
            var actual = TextMeasurement.GetFontMetrics(source, 20);
            Assert.Equal(expected.Ascent, actual.Ascent); Assert.Equal(expected.Descent, actual.Descent);
        }
        finally { Clear(owner); }
    }

    private static TextBlock Owner(string text, string rules, string family, ICssResourceResolver? resolver = null)
    {
        var owner = new TextBlock { Text = text, FontSize = 20 };
        Css.GetStyleSheets(owner).Add(resolver is null ? CssStyleSheet.Parse(rules) :
            CssStyleSheet.Parse(rules, "CSS Window font43", new Uri("https://font43.invalid/"), resolver));
        Css.SetStyle(owner, "font-family:" + family); return owner;
    }
    private static string Source(TextBlock owner) => owner.FontFamily.GetRenderingSource(owner);
    private static void Clear(TextBlock owner) { Css.SetStyleSheets(owner, null); Css.SetStyle(owner, string.Empty); }
    private static void Wait(TextBlock owner)
    {
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(owner).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(owner.Dispatcher); Assert.Empty(Css.GetFontLoadErrors(owner));
    }
    private static double Measure(string source, string text, int weight = 400)
    {
        var value = new FormattedText(text, source, 20) { FontWeight = weight, MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(value)); return value.Width;
    }
    private static NativeTextParagraph Layout(string source, string text) => NativeTextParagraph.TryCreate(
        [new(text, source, 20, 400, 0, Colors.Black)], "Avenir Next", 20, 100000, 48, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)!;
    private sealed class Resolver : ICssResourceResolver
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _bytes = new();
        internal IEnumerable<string> Requested => _bytes.Keys;
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken token = default)
        {
            var task = _bytes.GetOrAdd(Path.GetFileName(uri.LocalPath), static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            return new(uri, new MemoryStream(await task.Task.WaitAsync(token)), "font/ttf");
        }
        internal void Await(string name) => Assert.True(SpinWait.SpinUntil(() => _bytes.ContainsKey(name), TimeSpan.FromSeconds(5)));
        internal void Complete(string name, string path) => _bytes[name].TrySetResult(File.ReadAllBytes(path));
        internal void CompleteAll() { foreach (var task in _bytes.Values) task.TrySetResult(File.ReadAllBytes("/System/Library/Fonts/Supplemental/Andale Mono.ttf")); }
    }
}
