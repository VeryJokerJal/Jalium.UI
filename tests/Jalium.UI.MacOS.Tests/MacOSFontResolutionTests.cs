using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Controls.Helpers;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFontResolutionTests : MacOSGeometryTestBase
{
    private const string Sample = "MMMM iii1 abc אבג xyz tail";
    private const string MonoPath = "/System/Library/Fonts/Supplemental/Andale Mono.ttf";

    [Fact]
    public void RichDocument_IsLogicalChildAndReplacementDetachesOldDocument()
    {
        var box = new RichTextBox(); var old = box.Document;
        Assert.Same(box, old.Parent);
        var replacement = FlowDocument.FromText("replacement"); box.Document = replacement;
        Assert.Null(old.Parent); Assert.Same(box, replacement.Parent);
        Assert.Same(replacement, Assert.Single(Enumerate(box.LogicalChildren)));
    }

    [Fact]
    public void SharedRichDocument_IsRejectedWithoutChangingEitherOwner()
    {
        var first = new RichTextBox(); var second = new RichTextBox(); var original = second.Document;
        Assert.Throws<InvalidOperationException>(() => second.Document = first.Document);
        Assert.Same(original, second.Document); Assert.Same(second, original.Parent); Assert.Same(first, first.Document.Parent);
    }

    [Fact]
    public void DetachedRichDocument_DoesNotResolveFormerHostsFontFace()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var run = new Run(Sample) { FontSize = 20 }; var box = Rich(run); var old = box.Document;
        Css.SetStyleSheet(box, "@font-face { font-family: DetachedFont40; src: local('Andale Mono'); font-display: swap; }");
        Css.SetStyle(run, "font-family: DetachedFont40, Helvetica");
        _ = run.FontFamily.GetRenderingSource(run);
#pragma warning disable xUnit1031 // Font notifications must run on this dispatcher thread.
        Css.WaitForFontsAsync(box).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(box.Dispatcher);
        Assert.StartsWith("Andale Mono", run.FontFamily.GetRenderingSource(run));
        box.Document = FlowDocument.FromText("replacement");
        Assert.Null(old.Parent);
        Assert.Equal("DetachedFont40, Helvetica", run.FontFamily.GetRenderingSource(run));
        Css.SetStyleSheets(box, null);
    }

    [Theory]
    [InlineData("Menlo, Helvetica")]
    [InlineData("__MissingWindowFont40__, Menlo")]
    [InlineData("\"Missing, Window Font40\", Menlo")]
    [InlineData(" 'Menlo' , Helvetica ")]
    [InlineData(" , , Menlo, ")]
    [InlineData("menlo, Helvetica")]
    [InlineData("\"Missing \\\"Quoted\\\" Font40\", Menlo")]
    public void FamilyStack_UsesFirstAvailableFaceForMeasurementAndCaret(string stack)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var expected = context.CreateTextFormat("Menlo", 20);
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(context, stack, 20, 400, 0);
        var wanted = expected.MeasureText(Sample, 100000, 1000);
        var measured = actual.MeasureText(Sample, 100000, 1000);
        Assert.Equal(wanted.Width, measured.Width);
        Assert.True(TextMeasurement.HitTestTextPositionWrapped(Sample, stack, 20, 400, 0,
            float.PositiveInfinity, (uint)Sample.Length, false, out var caret));
        Assert.True(expected.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var reference));
        Assert.Equal(reference.CaretX, caret.CaretX);
    }

    [Theory]
    [InlineData("SF Pro")]
    [InlineData("system-ui")]
    [InlineData("__MissingWindowFont40__, __AnotherMissingFont40__")]
    public void DefaultAndAllMissingStack_UseActualSystemFont(string stack)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(context, stack, 20, 400, 0);
        using var expected = context.CreateTextFormat(".AppleSystemUIFont", 20);
        var measured = actual.MeasureText(Sample, 100000, 1000);
        var reference = expected.MeasureText(Sample, 100000, 1000);
        Assert.Equal(reference.Width, measured.Width);
        Assert.Equal(reference.Ascent, measured.Ascent);
    }

    [Theory]
    [InlineData("Menlo, Helvetica")]
    [InlineData("__MissingWindowFont40__, Menlo")]
    [InlineData("\"Missing, Font40\", 'Menlo'")]
    public void NativeParagraph_UsesTheSameResolvedFontForRowsCaretsAndSelection(string family)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        string text = Sample + " second row 中文 👩‍👩‍👧‍👦 tail";
        using var actual = Paragraph(text, family, 130);
        using var expected = Paragraph(text, "Menlo", 130);
        Assert.Equal(expected.Lines.Select(row => row.Metrics.Line.Length), actual.Lines.Select(row => row.Metrics.Line.Length));
        foreach (var row in expected.Lines)
        {
            int start = (int)row.Metrics.Line.TextPosition, end = start + (int)row.Metrics.Line.Length;
            for (int position = start; position <= end; position = GraphemeClusters.NextBoundary(text, position))
            {
                Assert.Equal(expected.Caret(row.Index, position, false).X, actual.Caret(row.Index, position, false).X);
                if (position == end) break;
            }
            Assert.Equal(expected.Selection(row.Index, start, end - start).Select(r => r.Width),
                actual.Selection(row.Index, start, end - start).Select(r => r.Width));
        }
    }

    [Fact]
    public void CssPrivateFont_UsesPreparedBytesAndRetainsParagraphAfterResourceDisposal()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes(MonoPath));
        Assert.NotNull(resource);
        using var expected = context.CreateTextFormat("Andale Mono", 20);
        using var actual = context.CreateTextFormat(resource.Family, 20);
        var wanted = expected.MeasureText(Sample, 100000, 1000);
        var measured = actual.MeasureText(Sample, 100000, 1000);
        Assert.Equal(wanted.Width, measured.Width);
        using var paragraph = Paragraph(Sample, resource.Family, 100000);
        using var reference = Paragraph(Sample, "Andale Mono", 100000);
        resource.Dispose(); actual.Dispose();
        Assert.Equal(reference.Caret(0, Sample.Length, false).X, paragraph.Caret(0, Sample.Length, false).X);
        Assert.NotEmpty(paragraph.Selection(0, 0, Sample.Length));
    }

    [Fact]
    public void QuotedPrivateFamily_CanContainCommaAndEscapedQuote()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        const string family = "Window, \"Font40\"";
        using var resource = Register(family);
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(context,
            "\"Window, \\\"Font40\\\"\", Helvetica", 20, 400, 0);
        using var expected = context.CreateTextFormat("Andale Mono", 20);
        var measured = actual.MeasureText(Sample, 100000, 1000);
        var reference = expected.MeasureText(Sample, 100000, 1000);
        Assert.Equal(reference.Width, measured.Width);
    }

    [Fact]
    public unsafe void FontCatalog_ProvidesInstalledFamiliesAndChecksUtf8BufferBounds()
    {
        int count = FontApi.Count(); Assert.InRange(count, 21, 99999);
        var names = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            int required = FontApi.Copy(i, null, 0); Assert.InRange(required, 2, 65536);
            var bytes = Enumerable.Repeat((byte)0x7e, required + 2).ToArray();
            fixed (byte* pointer = bytes)
            {
                Assert.Equal(required, FontApi.Copy(i, pointer, required));
                Assert.Equal(0, bytes[required - 1]); Assert.Equal(0x7e, bytes[required]);
                Assert.Equal(0, FontApi.Copy(i, pointer, 1)); Assert.Equal(0, bytes[0]);
            }
            fixed (byte* pointer = bytes) Assert.Equal(required, FontApi.Copy(i, pointer, required));
            names.Add(Encoding.UTF8.GetString(bytes, 0, required - 1));
        }
        Assert.Contains("Menlo", names); Assert.Contains("Helvetica", names);
        Assert.Equal(0, FontApi.Copy(-1, null, 0)); Assert.Equal(0, FontApi.Copy(count, null, 0));
        var installed = FontEnumerationHelper.EnumerateSystemFontFamilies(); Assert.NotNull(installed);
        Assert.Contains("Menlo", installed); Assert.Contains(FrameworkElement.DefaultFontFamilyName, installed);
    }

    [Fact]
    public void FamilyAvailability_RejectsSilentSubstitutionAndRecognizesFacesAliasesAndPrivateFonts()
    {
        foreach (string family in new[] { "Menlo", "menlo", "Menlo-Regular", "SF Pro", "SYSTEM-UI", "monospace" })
            Assert.Equal(1, FontApi.Available(family));
        Assert.Equal(0, FontApi.Available("__MissingWindowFont40__"));
        using var resource = Register("AvailableWindowFont40");
        Assert.Equal(1, FontApi.Available("AvailableWindowFont40"));
        resource.Dispose(); Assert.Equal(0, FontApi.Available("AvailableWindowFont40"));
    }

    [Theory]
    [InlineData("system-ui", ".AppleSystemUIFont")]
    [InlineData("monospace", "Menlo")]
    [InlineData("serif", "Times New Roman")]
    public void CssGenerics_UseMacOSFamilies(string generic, string expected)
    {
        var text = new TextBlock { Text = Sample };
        Css.SetStyle(text, $"font-family: {generic}; font-size: 20px");
        Assert.Equal(expected, text.FontFamily.Source);
    }

    [Fact]
    public void FontArrival_RebuildsPlainWordLayoutWithTheSameTextAndFamily()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var layout = new MacOSWordNavigationLayout();
        const string family = "WordWindowFont40, Helvetica";
        Assert.True(layout.TryNavigate(Sample, family, 20, 400, 0, 130, 28, 0, 0, 0, true, false, out _));
        var field = typeof(MacOSWordNavigationLayout).GetField("_paragraph", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var before = (NativeTextParagraph)field.GetValue(layout)!;
        using var resource = Register("WordWindowFont40"); TextMeasurement.ClearCache();
        Assert.True(layout.TryNavigate(Sample, family, 20, 400, 0, 130, 28, 0, 0, 0, true, false, out _));
        var after = (NativeTextParagraph)field.GetValue(layout)!;
        Assert.True(before.IsDisposed);
        using var expected = Paragraph(Sample, "Andale Mono", 130);
        Assert.Equal(expected.Lines.Select(row => row.Metrics.Line.Length), after.Lines.Select(row => row.Metrics.Line.Length));
    }

    [Fact]
    public void FontArrival_RecalculatesEditorGeometryWithoutChangingFontProperties()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        const string family = "EditorWindowFont40, Helvetica";
        var box = new EditControl { Text = Sample, FontFamily = new FontFamily(family), FontSize = 20, ShowLineNumbers = false };
        Arrange(box);
        var view = (EditorView)typeof(EditControl).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!;
        var before = view.GetPointFromOffset(Sample.Length, false);
        using var resource = Register("EditorWindowFont40"); TextMeasurement.ClearCache(); view.UpdateLayout(family, 20);
        var after = view.GetPointFromOffset(Sample.Length, false);
        using var expected = context.CreateTextFormat("Andale Mono", 20);
        Assert.True(expected.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var caret));
        Assert.NotEqual(before.X, after.X); Assert.Equal(caret.CaretX, after.X, 2);
    }

    [Theory]
    [InlineData("Menlo, Helvetica", "Menlo")]
    [InlineData("__MissingWindowFont40__, Menlo", "Menlo")]
    public void RichParagraph_ResolvesRunFamilyStacks(string family, string expected)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var run = new Run(Sample) { FontFamily = new FontFamily(family), FontSize = 20 };
        var box = Rich(run); Arrange(box);
        var paragraph = NativeParagraph(box);
        using var reference = Paragraph(Sample, expected, 360);
        Assert.Equal(reference.Caret(0, Sample.Length, false).X, paragraph.Caret(0, Sample.Length, false).X);
    }

    [Fact]
    public void RichParagraph_FontArrivalInvalidatesCachedNativeFont()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var run = new Run(Sample) { FontFamily = new FontFamily("RichWindowFont40, Helvetica"), FontSize = 20 };
        var box = Rich(run); Arrange(box); var before = NativeParagraph(box);
        using var resource = Register("RichWindowFont40"); TextMeasurement.ClearCache(); var after = NativeParagraph(box);
        Assert.True(before.IsDisposed);
        using var reference = Paragraph(Sample, "Andale Mono", 360);
        Assert.Equal(reference.Caret(0, Sample.Length, false).X, after.Caret(0, Sample.Length, false).X);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("swap")]
    public void RichCssFontFace_UsesLoadedAliasAndHonorsBlockPeriod(string display)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var resolver = new FontResolver(); var run = new Run(Sample) { FontSize = 20 };
        var box = Rich(run);
        Css.GetStyleSheets(box).Add(CssStyleSheet.Parse($"@font-face {{ font-family: WindowCss40; src: url('mono.ttf'); font-display: {display}; }}",
            "Window font test", new Uri("https://window-font-test.invalid/"), resolver));
        Css.SetStyle(run, "font-family: WindowCss40, Helvetica");
        try
        {
            Arrange(box); var before = NativeParagraph(box);
            Assert.Equal(display == "block", CssFontFaces.IsBlocked(run.FontFamily.GetRenderingSource(run)));
            var layout = NativeBlock(box);
            var colors = (IList)layout.GetType().GetProperty("NativeColors")!.GetValue(layout)!;
            double opacity = ((ValueTuple<Run?, Color, double>)colors[0]!).Item3;
            Assert.Equal(display == "block" ? 0 : 1, opacity);
            resolver.Complete(File.ReadAllBytes(MonoPath));
#pragma warning disable xUnit1031 // Keep the native context and font callbacks on their owning dispatcher.
            Css.WaitForFontsAsync(box).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            CssFontFaces.FlushNotifications(box.Dispatcher);
            Assert.Empty(Css.GetFontLoadErrors(box));
            Assert.StartsWith("JaliumCss", run.FontFamily.GetRenderingSource(run));
            var after = NativeParagraph(box); Assert.True(before.IsDisposed);
            using var expected = Paragraph(Sample, "Andale Mono", 360);
            Assert.Equal(expected.Caret(0, Sample.Length, false).X, after.Caret(0, Sample.Length, false).X);
        }
        finally { resolver.Complete(File.ReadAllBytes(MonoPath)); Css.SetStyleSheets(box, null); }
    }

    private static void Arrange(Control box) { box.Measure(new Size(360, 180)); box.Arrange(new Rect(0, 0, 360, 180)); }
    private static IEnumerable<object> Enumerate(IEnumerator children) { while (children.MoveNext()) yield return children.Current; }
    private static RichTextBox Rich(Run run)
    {
        var document = new FlowDocument { FontFamily = "Helvetica", FontSize = 20 };
        document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        return new RichTextBox(document) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
    }
    private static object NativeBlock(RichTextBox box)
    {
        object layout = typeof(RichTextBox).GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, [box.RenderSize.Width])!;
        return ((IList)layout.GetType().GetProperty("Blocks")!.GetValue(layout)!)[0]!;
    }
    private static NativeTextParagraph NativeParagraph(RichTextBox box)
    {
        object block = NativeBlock(box);
        return (NativeTextParagraph)block.GetType().GetProperty("NativeParagraph")!.GetValue(block)!;
    }
    private static NativeTextParagraph Paragraph(string text, string family, double width)
    {
        var result = NativeTextParagraph.TryCreate([new(text, family, 20, 400, 0, Colors.Black)],
            family, 20, width, 28, TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.NotNull(result); return result;
    }
    private static unsafe PrivateResource Register(string family)
    {
        byte[] bytes = File.ReadAllBytes(MonoPath);
        fixed (byte* pointer = bytes)
        {
            nint resource = NativeMethods.FontResourceRegister(family, (nint)pointer, (uint)bytes.Length);
            Assert.NotEqual(0, resource); return new(resource);
        }
    }
    private sealed class PrivateResource(nint handle) : IDisposable
    {
        private nint _handle = handle;
        public void Dispose() { nint resource = Interlocked.Exchange(ref _handle, 0); if (resource != 0) NativeMethods.FontResourceRelease(resource); }
    }
    private sealed class FontResolver : ICssResourceResolver
    {
        private readonly TaskCompletionSource<byte[]> _bytes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(byte[] bytes) => _bytes.TrySetResult(bytes);
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
            => new(uri, new MemoryStream(await _bytes.Task.WaitAsync(cancellationToken)), "font/ttf");
    }
    private static class FontApi
    {
        [DllImport("jalium.native.core", EntryPoint = "jalium_font_family_is_available", CharSet = CharSet.Unicode)]
        internal static extern int Available(string name);
        [DllImport("jalium.native.core", EntryPoint = "jalium_font_get_system_family_count")]
        internal static extern int Count();
        [DllImport("jalium.native.core", EntryPoint = "jalium_font_copy_system_family")]
        internal static extern unsafe int Copy(int index, byte* buffer, int capacity);
    }
}
