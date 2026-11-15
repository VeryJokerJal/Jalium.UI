using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssFontFaceTests
{
    internal static byte[] Ahem()
    {
        using var stream = typeof(CssFontFaceTests).Assembly.GetManifestResourceStream("Jalium.UI.Tests.TestAssets.Fonts.Ahem.ttf")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }

    [Fact]
    public void Parser_PreservesDescriptorsAndRejectsInvalidReplacements()
    {
        var sheet = CssStyleSheet.Parse("""
            @font-face {
              font-family:'App Font'; src:local('Missing'),url(font.woff) format('woff');
              font-weight:100 900; font-weight:wrong; font-style:italic;
              font-stretch:75% 125%; unicode-range:U+20-7F,U+4??;
              font-display:swap; font-display:optional !important;
            }
            """, "fonts", new Uri("https://fonts.test/styles/main.css"));
        var font = Assert.Single(sheet.FontFaces);
        Assert.Equal("App Font", font.Family); Assert.Equal(100, font.MinimumWeight); Assert.Equal(900, font.MaximumWeight);
        Assert.Equal(1, font.Style); Assert.Equal(75, font.MinimumWidth); Assert.Equal(125, font.MaximumWidth);
        Assert.Equal("swap", font.Display); Assert.Equal(2, font.Sources.Length);
        Assert.True(font.Sources[0].Local); Assert.Equal("woff", font.Sources[1].Format);
        Assert.True(font.Contains(0x41)); Assert.True(font.Contains(0x4ab)); Assert.False(font.Contains(0x500));
        Assert.Equal("https://fonts.test/styles/main.css", font.BaseUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("@font-face {src:url(x.ttf)}")]
    [InlineData("@font-face {font-family:App}")]
    [InlineData("@font-face named {font-family:App;src:url(x.ttf)}")]
    [InlineData("@font-face {font-family:App;src:local('App') format('truetype')}")]
    public void InvalidRules_DoNotCreateFontFaces(string css) => Assert.Empty(CssStyleSheet.Parse(css).FontFaces);

    [Fact]
    public void NestedAndImportedRules_PreserveResourceBasesConditionsAndLayers()
    {
        var resolver = new FontResources();
        resolver.Styles["/main.css"] = "@import 'sub/face.css' layer(web); @media (min-width:500px){@font-face{font-family:Other;src:url(other.ttf)}}";
        resolver.Styles["/sub/face.css"] = "@font-face {font-family:App;src:url(font.ttf)}";
        var sheet = CssStyleSheet.LoadAsync(new Uri("https://fonts.test/main.css"), resolver).GetAwaiter().GetResult();
        Assert.Equal(2, sheet.FontFaces.Length);
        Assert.Equal("web", sheet.FontFaces[0].LayerName);
        Assert.Equal("https://fonts.test/sub/face.css", sheet.FontFaces[0].BaseUri!.AbsoluteUri);
        Assert.NotNull(sheet.FontFaces[1].Condition);
        Assert.Equal(0, resolver.FontRequests);
    }

    [Fact]
    public void FontData_RewritesNamesAndRepairsSfntChecksums()
    {
        var original = Ahem();
        Assert.True(CssFontData.TryPrepare(original, "JaliumCssTest", out var rewritten));
        Assert.NotEqual(original, rewritten);
        Assert.Equal(0xb1b0afbAu, CssFontData.Checksum(rewritten));
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(rewritten.AsSpan(4, 2));
        Assert.True(tableCount > 5);
        Assert.Contains(System.Text.Encoding.BigEndianUnicode.GetBytes("JaliumCssTest"), AllNameStrings(rewritten));
    }

    private static IEnumerable<byte[]> AllNameStrings(byte[] bytes)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
        for (var i = 0; i < count; i++)
        {
            var entry = 12 + i * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry, 4)) != 0x6e616d65) continue;
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 8, 4));
            var records = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2, 2));
            var storage = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4, 2));
            for (var j = 0; j < records; j++)
            {
                var at = offset + 6 + j * 12;
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at + 8, 2));
                var start = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at + 10, 2));
                yield return bytes.AsSpan(offset + storage + start, length).ToArray();
            }
        }
    }

    [Fact]
    public void WoffTables_DecompressToAValidPrivateSfnt()
    {
        var source = Ahem(); var count = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(4, 2));
        var entries = new List<(uint Tag, uint Checksum, int Length, byte[] Data)>();
        for (var i = 0; i < count; i++)
        {
            var at = 12 + i * 16;
            uint Read(int delta) => BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(at + delta, 4));
            var bytes = source.AsSpan((int)Read(8), (int)Read(12)).ToArray();
            using var compressed = new MemoryStream();
            using (var compressor = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) compressor.Write(bytes);
            var data = compressed.Length < bytes.Length ? compressed.ToArray() : bytes;
            entries.Add((Read(0), Read(4), bytes.Length, data));
        }
        var offset = 44 + count * 20;
        var woff = new byte[offset + entries.Sum(e => (e.Data.Length + 3) & ~3)];
        void Write(int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(at, 4), value);
        Write(0, 0x774f4646); Write(4, BinaryPrimitives.ReadUInt32BigEndian(source)); Write(8, (uint)woff.Length);
        BinaryPrimitives.WriteUInt16BigEndian(woff.AsSpan(12, 2), count);
        Write(16, (uint)(12 + count * 16 + entries.Sum(e => (e.Length + 3) & ~3)));
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var at = 44 + i * 20;
            Write(at, entry.Tag); Write(at + 4, (uint)offset); Write(at + 8, (uint)entry.Data.Length);
            Write(at + 12, (uint)entry.Length); Write(at + 16, entry.Checksum);
            entry.Data.CopyTo(woff, offset); offset += (entry.Data.Length + 3) & ~3;
        }
        Assert.True(CssFontData.TryPrepare(woff, "JaliumWoff", out var prepared));
        Assert.Equal(0xb1b0afbAu, CssFontData.Checksum(prepared));
    }

    [Fact]
    public void LocalFontFamilyValues_DoNotStartCssFontLoads()
    {
        var resolver = new FontResources();
        var text = new TextBlock { Text = "native", FontFamily = new FontFamily("Arial") };
        var root = new Border { Child = text };
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@font-face{font-family:Arial;src:url(face.ttf)} TextBlock{font-family:Arial}",
            "font", new Uri("https://fonts.test/main.css"), resolver));
        Layout(root); Pump(Css.WaitForFontsAsync(root), root);
        Assert.Equal(0, resolver.FontRequests); Assert.Equal("Arial", text.FontFamily.Source);
    }

    [Fact]
    public void RemovingAFontRule_CancelsItsOutstandingRequest()
    {
        var resolver = new FontResources { HoldFonts = true };
        var text = new TextBlock { Text = "XXXX" }; var root = new Border { Child = text };
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@font-face{font-family:App;src:url(face.ttf);font-display:swap} TextBlock{font-family:App}",
            "font", new Uri("https://fonts.test/main.css"), resolver));
        Layout(root); WaitUntil(() => resolver.FontRequests == 1, root);
        var ready = Css.WaitForFontsAsync(root);
        Css.GetStyleSheets(root).Clear(); Pump(ready, root);
        Assert.Equal(1, resolver.CanceledRequests);
    }

    [Fact]
    public void SourceFallback_SkipsUnsupportedHintsAndContinuesAfterInvalidData()
    {
        var resolver = new FontResources(); resolver.Invalid.Add("/bad.ttf");
        var text = new TextBlock { Text = "XXXX" }; var root = new Border { Child = text };
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@font-face{font-family:App;src:url(skip.unknown) format(unsupported),url(bad.ttf),url(good.ttf);font-display:swap} TextBlock{font-family:App}",
            "font", new Uri("https://fonts.test/main.css"), resolver));
        Layout(root); Pump(Css.WaitForFontsAsync(root), root); Layout(root);
        Assert.Equal(2, resolver.FontRequests); Assert.Single(Css.GetFontLoadErrors(root));
        Assert.StartsWith("JaliumCss", text.FontFamily.GetRenderingSource(text));
        Css.GetStyleSheets(root).Clear();
    }

    [Fact]
    public void SourceHints_SkipUnsupportedTechAndLoadSupportedOpenTypeFeatures()
    {
        var resolver = new FontResources();
        var text = new TextBlock { Text = "XXXX" }; var root = new Border { Child = text };
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@font-face{font-family:App;src:url(skip.ttf) format(truetype) tech(variations),url(good.ttf) format(truetype) tech(features-opentype);font-display:swap} TextBlock{font-family:App}",
            "font", new Uri("https://fonts.test/main.css"), resolver));
        Layout(root); Pump(Css.WaitForFontsAsync(root), root); Layout(root);
        Assert.Equal(1, resolver.FontRequests);
        Assert.Empty(Css.GetFontLoadErrors(root));
        Assert.StartsWith("JaliumCss", text.FontFamily.GetRenderingSource(text));
        Css.GetStyleSheets(root).Clear();
    }

    [Fact]
    public void QuotedFamilyContainingComma_LoadsFontAndSuppliesFontRelativeMetrics()
    {
        var resolver = new FontResources();
        var text = new TextBlock { Text = "XXXX", FontSize = 20 };
        var root = new Border { Child = text };
        Css.SetStyle(text, "width:2ch");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
            "@font-face{font-family:'ACME, Inc.';src:url(ahem.ttf);font-display:swap} " +
            "TextBlock{font-family:'ACME, Inc.',Arial}",
            "font", new Uri("https://fonts.test/main.css"), resolver));

        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Layout(root); Pump(Css.WaitForFontsAsync(root), root); Layout(root);
        Assert.Equal(1, resolver.FontRequests);
        Assert.Equal("\"ACME, Inc.\", Arial", text.FontFamily.Source);
        var rendered = text.FontFamily.GetRenderingSource(text);
        Assert.StartsWith("JaliumCss", rendered);
        Assert.Equal(20, TextMeasurement.GetFontUnitMetrics(rendered, 20).ZeroAdvance, 3);
        var node = CssNode.Get(text);
        Assert.NotNull(CssFontInfo.Read(node).ComputedFamily);
        Assert.Equal(20, CssFontInfo.Read(node).Metrics(20, new CssFontDependency(node)).ZeroAdvance, 3);
        Assert.Equal(40, text.Width, 3);
        Css.GetStyleSheets(root).Clear();
    }

    [Theory]
    [InlineData(RenderBackend.Software)]
    [InlineData(RenderBackend.D3D12)]
    [InlineData(RenderBackend.Vulkan)]
    public void NativeFontRegistration_UsesPrivateFamilyAndSurvivesOwnerRelease(RenderBackend backend)
    {
        if (!OperatingSystem.IsWindows() && backend == RenderBackend.D3D12) return;
        using var resource = CssNativeFontResource.Create(Ahem()); Assert.NotNull(resource);
        using var context = new RenderContext(backend);
        Assert.Equal(backend, context.Backend);
        using var format = context.CreateTextFormat(resource.Family, 20);
        Assert.Equal(80, format.MeasureText("XXXX", 1000, 1000).WidthIncludingTrailingWhitespace, 3);
        var metrics = format.GetFontUnitMetrics(); Assert.Equal(20, metrics.ZeroAdvance, 3);
        resource.Dispose();
        Assert.Equal(80, format.MeasureText("XXXX", 1000, 1000).WidthIncludingTrailingWhitespace, 3);
    }

    [Fact]
    public void Fonts_AreLazyAndSwapAfterArrivalWithoutChangingTextOrNativeValues()
    {
        var source = new TextBlock { Text = "XXXX" };
        var text = new TextBlock { FontSize = 20 };
        var binding = text.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = source });
        var root = new Border { Child = text }; var resolver = new FontResources { HoldFonts = true };
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@font-face{font-family:App;src:url(ahem.ttf);font-display:swap} @font-face{font-family:Unused;src:url(unused.ttf)} TextBlock{font-family:App,Arial}",
            "fonts", new Uri("https://fonts.test/styles/main.css"), resolver));
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Layout(root); WaitUntil(() => resolver.FontRequests == 1, root);
        Assert.Equal("App, Arial", text.FontFamily.Source);
        resolver.Release.TrySetResult(); Pump(Css.WaitForFontsAsync(root), root); Layout(root);
        Assert.Empty(Css.GetFontLoadErrors(root));
        var renderedFamily = text.FontFamily.GetRenderingSource(text);
        Assert.StartsWith("JaliumCss", renderedFamily);
        using (var direct = context.CreateTextFormat(renderedFamily, 20))
            Assert.Equal(80, direct.MeasureText("XXXX", 1000, 1000).WidthIncludingTrailingWhitespace, 3);
        // TextBlock retains its native two-DIP measurement allowance.
        Assert.Equal(1, resolver.FontRequests); Assert.Equal(82, text.DesiredSize.Width, 3);
        Assert.Equal(20, text.FontSize); Assert.Equal("XXXX", text.Text);
        Assert.Same(binding, text.GetBindingExpression(TextBlock.TextProperty));
        Css.GetStyleSheets(root).Clear(); Layout(root);
        Assert.Equal("XXXX", text.Text); Assert.Equal(20, text.FontSize);
        TextMeasurement.ClearCache();
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void DownloadedFont_PaintsItsActualGlyphsThroughTheNativeBackend()
    {
        using var resource = CssNativeFontResource.Create(Ahem()); Assert.NotNull(resource);
        using var window = new HiddenNativeWindow(120, 50);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, 120, 50);
        using var format = context.CreateTextFormat(resource.Family, 20);
        using var brush = context.CreateSolidBrush(0, 1, 0, 1);
        format.SetTextRenderingMode(1); // Aliased: the Ahem square has exact integer pixel edges.
        byte[] Paint(NativeTextFormat face)
        {
            Assert.True(target.TryBeginDraw()); target.Clear(1, 1, 1, 1);
            target.DrawText("XXXX", face, 10, 10, 90, 30, brush);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback()); Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[120 * 50 * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 120 * 4u, out var width, out var height));
            Assert.Equal(120, width); Assert.Equal(50, height); return pixels;
        }
        var actual = Paint(format);
        // Compare with the unmodified fixture installed through the OS test API.
        // This isolates font rewriting/loading from the renderer's rasterization.
        var bytes = Ahem(); var original = AddFontMemResourceEx(bytes, (uint)bytes.Length, 0, out var faces);
        Assert.NotEqual(0, original); Assert.True(faces > 0);
        try
        {
            using var reference = context.CreateTextFormat("Ahem", 20);
            reference.SetTextRenderingMode(1);
            Assert.Equal(Paint(reference), actual);
            Assert.Contains(Enumerable.Range(0, actual.Length / 4), i => actual[i * 4] < 16 && actual[i * 4 + 1] > 240 && actual[i * 4 + 2] < 16);
        }
        finally { Assert.True(RemoveFontMemResourceEx(original)); }
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint AddFontMemResourceEx(byte[] data, uint size, nint reserved, out uint fonts);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveFontMemResourceEx(nint handle);

    private static void Layout(FrameworkElement root)
    {
        root.Dispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(400, 100)); root.Arrange(new Rect(0, 0, 400, 100)); root.UpdateLayout();
    }
    private static void WaitUntil(Func<bool> condition, FrameworkElement root)
    {
        var end = Environment.TickCount64 + 10000;
        while (!condition() && Environment.TickCount64 < end) { root.Dispatcher.ProcessQueue(); Thread.Sleep(1); }
        Assert.True(condition());
    }
    private static void Pump(Task task, FrameworkElement root)
    {
        WaitUntil(() => task.IsCompleted, root); task.GetAwaiter().GetResult(); root.Dispatcher.ProcessQueue();
    }

    private sealed class FontResources : ICssResourceResolver
    {
        internal readonly Dictionary<string, string> Styles = new();
        internal readonly HashSet<string> Invalid = new();
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HoldFonts;
        internal int FontRequests;
        internal int CanceledRequests;
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (Styles.TryGetValue(uri.AbsolutePath, out var css)) return new(uri, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(css)));
            Interlocked.Increment(ref FontRequests);
            if (HoldFonts)
            {
                try { await Release.Task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { Interlocked.Increment(ref CanceledRequests); throw; }
            }
            return new(uri, new MemoryStream(Invalid.Contains(uri.AbsolutePath) ? [1, 2, 3] : Ahem()));
        }
    }
}
