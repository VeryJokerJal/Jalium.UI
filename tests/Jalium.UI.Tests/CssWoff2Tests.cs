using System.Buffers.Binary;
using System.Collections.Concurrent;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssWoff2Tests
{
    internal static byte[] Fixture(string name)
    {
        using var stream = typeof(CssWoff2Tests).Assembly.GetManifestResourceStream("Jalium.UI.Tests.TestAssets.Fonts." + name)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }

    [Theory]
    [InlineData("Ahem-transformed.woff2", "Ahem.ttf")]
    [InlineData("Ahem-null.woff2", "Ahem.ttf")]
    [InlineData("Ahem-hmtx.woff2", "Ahem.ttf")]
    [InlineData("Ahem-hmtx-only.woff2", "Ahem.ttf")]
    [InlineData("Ahem-collection.woff2", "Ahem.ttf")]
    [InlineData("Woff2Shapes.woff2", "Woff2Shapes.ttf")]
    public void RealFonts_ReconstructTablesAndGlyphMetrics(string compressed, string original)
    {
        var decoded = CssFontData.DecodeWoff2(Fixture(compressed));
        Assert.Equal(0xb1b0afbAu, CssFontData.Checksum(decoded));
        var before = Tables(Fixture(original)); var after = Tables(decoded);
        Assert.Equal(before["maxp"], after["maxp"]);
        Assert.Equal(before["cmap"], after["cmap"]);
        Assert.Equal(Metrics(before), Metrics(after));
        Assert.True(CssFontData.TryPrepare(Fixture(compressed), "PrivateWoff2", out var privateFont));
        Assert.Equal(0xb1b0afbAu, CssFontData.Checksum(privateFont));
        if (Environment.GetEnvironmentVariable("JALIUM_WOFF2_EVIDENCE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, compressed + ".ttf"), decoded);
        }
    }

    [Theory]
    [InlineData("Ahem-two-face.ttc")]
    [InlineData("Ahem-two-face.woff2")]
    public void FontCollections_SelectTheNamedFaceAndKeepTheFirstFaceAsDefault(string collection)
    {
        var bytes = Fixture(collection);
        var first = Tables(Fixture("Ahem.ttf"));
        var second = Tables(Fixture("Ahem-Second.ttf"));
        Assert.True(CssFontData.TryPrepare(bytes, "First", out var defaultFace));
        Assert.True(CssFontData.TryPrepare(bytes, "Second", out var selected, "Ahem-Second"));
        Assert.Equal(0xb1b0afbAu, CssFontData.Checksum(defaultFace));
        Assert.Equal(0xb1b0afbAu, CssFontData.Checksum(selected));
        Assert.Equal(Metrics(first), Metrics(Tables(defaultFace)));
        Assert.Equal(Metrics(second), Metrics(Tables(selected)));
        Assert.NotEqual(Metrics(Tables(defaultFace)), Metrics(Tables(selected)));
        Assert.True(CssFontData.TryPrepare(bytes, "NamedFirst", out var namedFirst, "Ahem"));
        Assert.Equal(Metrics(first), Metrics(Tables(namedFirst)));
        Assert.False(CssFontData.TryPrepare(bytes, "WrongCase", out _, "ahem-second"));
        Assert.False(CssFontData.TryPrepare(bytes, "Missing", out _, "Absent"));
    }

    [Fact]
    public void CollectionFragment_RequiresAnEscapedValidPostScriptName()
    {
        Assert.True(CssFontData.TryParseFontFragment("fonts/collection.woff2#Ahem%2DSecond", out var resource, out var name));
        Assert.Equal("fonts/collection.woff2", resource);
        Assert.Equal("Ahem-Second", name);
        Assert.True(CssFontData.TryParseFontFragment("font.woff2#Caret%5Estick", out _, out name));
        Assert.Equal("Caret^stick", name);
        Assert.True(CssFontData.TryParseFontFragment("fonts/collection.woff2", out resource, out name));
        Assert.Equal("fonts/collection.woff2", resource);
        Assert.Null(name);
        foreach (var fragment in new[] { "", "bad/name", "bad%2Fname", "bad%", "bad%ZZ", "bad^name", "bad#name", new string('X', 64) })
            Assert.False(CssFontData.TryParseFontFragment("font.woff2#" + fragment, out _, out _));
    }

    [Fact]
    public void FontFace_UsesASelectedCollectionFaceAndFallsBackAfterAMissingName()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        var text = new TextBlock { Text = "XXXX", FontSize = 20 };
        var root = new Border { Child = text };
        var resolver = new CollectionResources();
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            "@font-face{font-family:Collection;src:url(skip.woff2#bad%2Fname) format(woff2)," +
            "url(missing.woff2#Absent) format(woff2)," +
            "url(collection.woff2#Ahem%2DSecond) format(woff2);font-display:swap}" +
            "TextBlock{font-family:Collection}",
            "collection", new Uri("https://fonts.test/main.css"), resolver));
        root.Measure(new Size(400, 100)); root.Arrange(new Rect(0, 0, 400, 100)); root.UpdateLayout();
        CompleteFontRequests(root); root.UpdateLayout();
        Assert.Equal(2, resolver.Requests.Count);
        Assert.All(resolver.Requests, uri => Assert.Equal("", uri.Fragment));
        Assert.DoesNotContain(resolver.Requests, uri => uri.AbsolutePath == "/skip.woff2");
        Assert.Single(Css.GetFontLoadErrors(root));
        Assert.Equal(122, text.DesiredSize.Width, 3);
        Assert.Equal(20, text.FontSize); Assert.Equal("XXXX", text.Text);
        Css.GetStyleSheets(root).Clear(); TextMeasurement.ClearCache();
    }

    internal static Dictionary<string, byte[]> Tables(byte[] font)
    {
        var result = new Dictionary<string, byte[]>();
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4, 2));
        for (var i = 0; i < count; i++)
        {
            var at = 12 + 16 * i;
            var tag = System.Text.Encoding.ASCII.GetString(font, at, 4);
            var start = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(at + 8, 4));
            var length = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(at + 12, 4));
            result[tag] = font.AsSpan((int)start, (int)length).ToArray();
        }
        return result;
    }

    private static (int Advance, int Bearing)[] Metrics(Dictionary<string, byte[]> tables)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(tables["maxp"].AsSpan(4, 2));
        var full = BinaryPrimitives.ReadUInt16BigEndian(tables["hhea"].AsSpan(34, 2));
        var result = new (int, int)[count]; var advance = 0;
        for (var i = 0; i < count; i++)
        {
            if (i < full) advance = BinaryPrimitives.ReadUInt16BigEndian(tables["hmtx"].AsSpan(i * 4, 2));
            var at = i < full ? i * 4 + 2 : full * 4 + (i - full) * 2;
            result[i] = (advance, BinaryPrimitives.ReadInt16BigEndian(tables["hmtx"].AsSpan(at, 2)));
        }
        return result;
    }

    [Fact]
    public void ReservedHeaderAndAdvisorySizes_DoNotRejectAValidFont()
    {
        var bytes = Fixture("Ahem-transformed.woff2");
        bytes[14] = 0xff; bytes[15] = 0xff;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), uint.MaxValue);
        Assert.True(CssFontData.TryPrepare(bytes, "AdvisorySize", out _));
    }

    [Fact]
    public void TruncatedOrUnknownTransforms_AreRejected()
    {
        var original = Fixture("Ahem-transformed.woff2");
        foreach (var size in new[] { 0, 1, 47, 48, original.Length - 1 })
            Assert.False(CssFontData.TryPrepare(original[..size], "Truncated", out _));
        var changed = (byte[])original.Clone();
        changed[48] = (byte)(changed[48] | 0xc0); // cmap has no non-null transform.
        Assert.False(CssFontData.TryPrepare(changed, "UnknownTransform", out _));
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0x01 })]
    [InlineData(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 })]
    [InlineData(new byte[] { 0x90, 0x80, 0x80, 0x80, 0x00 })]
    public void InvalidBase128Lengths_AreRejectedBeforeDecompression(byte[] encoded)
    {
        var original = Fixture("Ahem-null.woff2");
        // The first table uses a known tag and a one-byte original length.
        Assert.NotEqual(63, original[48] & 63); Assert.True(original[49] < 128);
        var changed = original[..49].Concat(encoded).Concat(original[50..]).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(changed.AsSpan(8, 4), (uint)changed.Length);
        Assert.False(CssFontData.TryPrepare(changed, "BadLength", out _));
    }

    [Fact]
    public void Woff2FontFace_LoadsThroughTheExistingResourceAndNativePaths()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        var text = new TextBlock { Text = "XXXX", FontSize = 20 };
        var root = new Border { Child = text }; var resolver = new Woff2Resources();
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            "@font-face{font-family:Compressed;src:url(font.woff2) format(woff2);font-display:swap} TextBlock{font-family:Compressed}",
            "woff2", new Uri("https://fonts.test/main.css"), resolver));
        root.Measure(new Size(400, 100)); root.Arrange(new Rect(0, 0, 400, 100)); root.UpdateLayout();
        CompleteFontRequests(root); root.UpdateLayout();
        Assert.Empty(Css.GetFontLoadErrors(root));
        Assert.Equal(82, text.DesiredSize.Width, 3); Assert.Equal(20, text.FontSize); Assert.Equal("XXXX", text.Text);
        Assert.Equal(1, resolver.Requests);
        Css.GetStyleSheets(root).Clear(); TextMeasurement.ClearCache();
    }

    public static IEnumerable<object[]> Backends()
    {
        yield return [RenderBackend.Software]; yield return [RenderBackend.Vulkan];
        if (OperatingSystem.IsWindows()) yield return [RenderBackend.D3D12];
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void NativeBackends_UseReconstructedFontsAndNamedCollectionFaces(RenderBackend backend)
    {
        using var context = new RenderContext(backend);
        Assert.Equal(backend, context.Backend);
        using var font = CssNativeFontResource.Create(Fixture("Ahem-hmtx.woff2")); Assert.NotNull(font);
        using var face = context.CreateTextFormat(font.Family, 20);
        Assert.Equal(80, face.MeasureText("XXXX", 1000, 1000).WidthIncludingTrailingWhitespace, 3);
        Assert.Equal(20, face.GetFontUnitMetrics().ZeroAdvance, 3);
        using var cff = CssNativeFontResource.Create(Fixture("Woff2Wpt.valid-001.woff2")); Assert.NotNull(cff);
        using var cffFace = context.CreateTextFormat(cff.Family, 20);
        Assert.True(cffFace.MeasureText("P", 1000, 1000).WidthIncludingTrailingWhitespace > 0);
        foreach (var collection in new[] { "Ahem-two-face.ttc", "Ahem-two-face.woff2" })
        {
            using var selected = CssNativeFontResource.Create(Fixture(collection), "Ahem-Second");
            Assert.NotNull(selected);
            using var selectedFace = context.CreateTextFormat(selected.Family, 20);
            Assert.Equal(120, selectedFace.MeasureText("XXXX", 1000, 1000).WidthIncludingTrailingWhitespace, 3);
        }
    }

    [Fact]
    public void CorruptBrotliPayloads_DoNotEscapeTheValueParser()
    {
        var original = Fixture("Ahem-hmtx.woff2");
        for (var i = 128; i < original.Length; i += 23)
        {
            var corrupt = (byte[])original.Clone(); corrupt[i] ^= 0xff;
            _ = CssFontData.TryPrepare(corrupt, "Malformed", out _);
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ReconstructedGlyphPixels_MatchTheUncompressedFont()
    {
        using var original = CssNativeFontResource.Create(Fixture("Ahem.ttf")); Assert.NotNull(original);
        using var decoded = CssNativeFontResource.Create(Fixture("Ahem-hmtx.woff2")); Assert.NotNull(decoded);
        using var window = new HiddenNativeWindow(240, 50);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, 240, 50);
        using var brush = context.CreateSolidBrush(0, 1, 0, 1);
        byte[] Paint(string family)
        {
            using var face = context.CreateTextFormat(family, 20);
            face.SetTextRenderingMode(1);
            Assert.True(target.TryBeginDraw()); target.Clear(1, 1, 1, 1);
            target.DrawText("ABCDpxX水", face, 10, 10, 210, 30, brush);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback()); Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[240 * 50 * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 240 * 4u, out var width, out var height));
            Assert.Equal(240, width); Assert.Equal(50, height);
            return pixels;
        }
        var pixels = Paint(decoded.Family);
        Assert.Equal(Paint(original.Family), pixels);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4] < 16 && pixels[i * 4 + 1] > 240 && pixels[i * 4 + 2] < 16);
    }

    private sealed class Woff2Resources : ICssResourceResolver
    {
        internal int Requests;
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref Requests); return ValueTask.FromResult(new CssResource(uri, new MemoryStream(Fixture("Ahem-hmtx.woff2")))); }
    }

    private sealed class CollectionResources : ICssResourceResolver
    {
        internal ConcurrentQueue<Uri> Requests { get; } = new();
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(uri);
            return ValueTask.FromResult(new CssResource(uri, new MemoryStream(Fixture("Ahem-two-face.woff2"))));
        }
    }

    private static void CompleteFontRequests(FrameworkElement root)
    {
        var task = Css.WaitForFontsAsync(root);
        var deadline = Environment.TickCount64 + 10000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        { root.Dispatcher.ProcessQueue(); Thread.Sleep(1); }
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
}
