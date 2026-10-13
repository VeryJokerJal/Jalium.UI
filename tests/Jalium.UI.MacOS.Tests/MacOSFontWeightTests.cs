using System.Collections;
using System.Buffers.Binary;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFontWeightTests : MacOSGeometryTestBase
{
    private const string Sample = "MMMM iii1 abc אבג 123 xyz tail";
    private static readonly string[] Faces = ["AvenirNext-UltraLight", "AvenirNext-UltraLight", "AvenirNext-UltraLight",
        "AvenirNext-Regular", "AvenirNext-Medium", "AvenirNext-DemiBold", "AvenirNext-Bold", "AvenirNext-Heavy", "AvenirNext-Heavy"];

    public static IEnumerable<object[]> NamedCases()
    {
        for (int weight = 100; weight <= 900; weight += 100)
            for (int style = 0; style <= 2; style++) yield return [weight, style, Faces[weight / 100 - 1]];
    }

    [Theory]
    [MemberData(nameof(NamedCases))]
    public void NamedWeight_MeasurementParagraphAndCaretUseRequestedFace(int weight, int style, string face)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var expected = context.CreateTextFormat(face, 20, 400, style); expected.SetNoWrap(true);
        string family = "__MissingWeightFont41__, Avenir Next";
        var formatted = new FormattedText(Sample, family, 20) { FontWeight = weight, FontStyle = style,
            MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(formatted));
        Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, formatted.Width);
        Assert.True(expected.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var caret));
        Assert.True(TextMeasurement.HitTestTextPositionWrapped(Sample, family, 20, weight, style,
            float.PositiveInfinity, (uint)Sample.Length, false, out var shared));
        Assert.Equal(caret.CaretX, shared.CaretX);
        using var paragraph = Paragraph(family, weight, style);
        Assert.Equal(caret.CaretX, paragraph.Caret(0, Sample.Length, false).X, 3);
        using var reference = Paragraph(face, 400, style);
        Assert.Equal(reference.Selection(0, 0, Sample.Length).Select(rect => rect.Width),
            paragraph.Selection(0, 0, Sample.Length).Select(rect => rect.Width));
    }

    [Theory]
    [InlineData(100)] [InlineData(200)] [InlineData(300)] [InlineData(400)] [InlineData(500)]
    [InlineData(600)] [InlineData(700)] [InlineData(800)] [InlineData(900)]
    public void PrivateCollection_SelectsWeightAndStyleWithinThePreparedBytes(int weight)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/Avenir Next.ttc"));
        Assert.NotNull(resource);
        foreach (int style in new[] { 0, 1, 2 })
        {
            using var actual = context.CreateTextFormat(resource.Family, 20, weight, style);
            using var expected = context.CreateTextFormat(Faces[weight / 100 - 1], 20, 400, style);
            Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, actual.MeasureText(Sample, 100000, 1000).Width);
            using var paragraph = Paragraph(resource.Family, weight, style);
            using var reference = Paragraph(Faces[weight / 100 - 1], 400, style);
            Assert.Equal(reference.Caret(0, Sample.Length, false).X, paragraph.Caret(0, Sample.Length, false).X);
        }
    }

    [Fact]
    public void SystemFont_MeasurementCacheKeepsAllNineDistinctWeights()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var widths = new HashSet<double>();
        for (int weight = 100; weight <= 900; weight += 100)
        {
            var text = new FormattedText(Sample, "system-ui", 20) { FontWeight = weight, MaxTextWidth = double.PositiveInfinity };
            Assert.True(TextMeasurement.MeasureText(text)); widths.Add(text.Width);
        }
        Assert.Equal(9, widths.Count);
    }

    [Fact]
    public void SystemAndPrivateVariableFonts_PreserveAllNineWeights()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/SFNS.ttf"));
        Assert.NotNull(resource);
        foreach (string family in new[] { "system-ui", resource.Family })
        {
            double previous = 0; var widths = new HashSet<double>();
            for (int weight = 100; weight <= 900; weight += 100)
            {
                var text = new FormattedText(Sample, family, 20) { FontWeight = weight, MaxTextWidth = double.PositiveInfinity };
                Assert.True(TextMeasurement.MeasureText(text)); Assert.True(text.Width > previous);
                widths.Add(text.Width); previous = text.Width;
            }
            Assert.Equal(9, widths.Count);
        }
    }

    [Fact]
    public void TextBox_ChangingWeightRecalculatesImeCaretWithoutChangingText()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); using var context = new RenderContext(RenderBackend.Metal);
        var box = new TextBox { Text = Sample, FontFamily = new FontFamily("Avenir Next"), FontSize = 20,
            Padding = new Thickness(0), BorderThickness = new Thickness(0), TextWrapping = TextWrapping.NoWrap };
        var window = new DisplayedTestWindow { Content = box };
        try
        {
            Assert.True(box.Focus());
            for (int weight = 100; weight <= 900; weight += 100)
            {
                box.FontWeight = FontWeight.FromOpenTypeWeight(weight); Arrange(box); box.Select(Sample.Length, 0);
                using var expected = Paragraph(Faces[weight / 100 - 1], 400, 0);
                Assert.Equal(Math.Round(expected.Caret(0, Sample.Length, false).X), ((IImeSupport)box).GetImeCaretRectangle().X);
            }
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void EditControl_ChangingWeightRecalculatesTheSameLineGeometry()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var box = new EditControl { Text = Sample, FontFamily = new FontFamily("Avenir Next"), FontSize = 20, ShowLineNumbers = false };
        for (int weight = 100; weight <= 900; weight += 100)
        {
            box.FontWeight = FontWeight.FromOpenTypeWeight(weight); Arrange(box);
            var support = (IImeSupport)box;
            Assert.True(support.TrySetImeSelection(0, 0)); double first = support.GetImeCaretRectangle().X;
            Assert.True(support.TrySetImeSelection(Sample.Length, 0)); double end = support.GetImeCaretRectangle().X;
            using var expected = Paragraph(Faces[weight / 100 - 1], 400, 0);
            Assert.Equal(expected.Caret(0, Sample.Length, false).X, end - first, 2);
        }
    }

    [Fact]
    public void EditControl_WeightAndStyleChangesInvalidateCachedScrollExtent()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var box = new EditControl { Text = Sample, FontFamily = new FontFamily("Avenir Next"), FontSize = 20, ShowLineNumbers = false };
        Arrange(box); var support = (IImeSupport)box;
        var width = typeof(EditControl).GetMethod("GetDocumentTextContentWidth", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var (weight, style) in new[] { (400, 0), (500, 0), (900, 0), (900, 1), (400, 0) })
        {
            box.FontWeight = FontWeight.FromOpenTypeWeight(weight); box.FontStyle = FontStyle.FromOpenTypeStyle(style);
            _ = support.GetImeCaretRectangle();
            double actual = (double)width.Invoke(box, null)!;
            using var reference = Paragraph(Faces[weight / 100 - 1], 400, style);
            Assert.Equal(reference.Caret(0, Sample.Length, false).X + 16, actual, 2);
        }
    }

    [Fact]
    public void PrivateFont_WithInstalledPostScriptNameRetainsItsOwnModifiedAdvances()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        byte[] bytes = File.ReadAllBytes("/System/Library/Fonts/Supplemental/Andale Mono.ttf");
        int hhea = TableOffset(bytes, 0x68686561), hmtx = TableOffset(bytes, 0x686d7478);
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(hhea + 34));
        for (int i = 0; i < count; i++)
        {
            var advance = bytes.AsSpan(hmtx + i * 4, 2);
            BinaryPrimitives.WriteUInt16BigEndian(advance, checked((ushort)(BinaryPrimitives.ReadUInt16BigEndian(advance) + 256)));
        }
        byte[] unchanged = (byte[])bytes.Clone();
        using var installed = context.CreateTextFormat("Andale Mono", 20);
        double before = installed.MeasureText("MMMM", 100000, 1000).Width;
        using var resource = CssNativeFontResource.Create(bytes); Assert.NotNull(resource);
        using var modified = context.CreateTextFormat(resource.Family, 20);
        Assert.True(modified.MeasureText("MMMM", 100000, 1000).Width > before + 5);
        using var stillInstalled = context.CreateTextFormat("Andale Mono", 20);
        Assert.Equal(before, stillInstalled.MeasureText("MMMM", 100000, 1000).Width);
        Assert.Equal(unchanged, bytes);
    }

    [Fact]
    public void PrivateCollection_ExplicitFragmentRetainsOnlyRequestedFace()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/Avenir Next.ttc"), "AvenirNext-DemiBold");
        Assert.NotNull(resource);
        using var actual = context.CreateTextFormat(resource.Family, 20);
        using var expected = context.CreateTextFormat("AvenirNext-DemiBold", 20);
        Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, actual.MeasureText(Sample, 100000, 1000).Width);
        using var missing = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/Avenir Next.ttc"), "NoSuchFace41");
        Assert.Null(missing);
    }

    private static int TableOffset(byte[] bytes, uint tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int at = 12 + i * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at)) == tag)
                return checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 8)));
        }
        throw new InvalidDataException("Font fixture table missing");
    }

    [Fact]
    public void RichInheritedTypography_RebuildsParagraphWithoutAnotherArrange()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var run = new Run(Sample) { FontFamily = new FontFamily("Avenir Next") };
        var document = new FlowDocument { FontFamily = "Avenir Next" };
        document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        var box = new RichTextBox(document) { FontSize = 20, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        Arrange(box);
        var previous = NativeParagraph(box);
        foreach (var row in NamedCases())
        {
            int weight = (int)row[0], style = (int)row[1]; string face = (string)row[2];
            box.FontWeight = FontWeight.FromOpenTypeWeight(weight);
            box.FontStyle = FontStyle.FromOpenTypeStyle(style);
            Assert.Equal(box.FontWeight, run.FontWeight);
            Assert.Equal(box.FontStyle, run.FontStyle);
            // Setting the already inherited value need not raise a content event.
            run.FontWeight = box.FontWeight; run.FontStyle = box.FontStyle;
            var paragraph = NativeParagraph(box);
            using var expected = Paragraph(face, 400, style);
            Assert.Equal(expected.Caret(0, Sample.Length, false).X, paragraph.Caret(0, Sample.Length, false).X, 3);
            if (previous != paragraph) Assert.True(previous.IsDisposed);
            previous = paragraph;
            run.ClearValue(TextElement.FontWeightProperty); run.ClearValue(TextElement.FontStyleProperty);
        }
        box.FontSize = 32;
        Assert.Equal(32, run.FontSize);
        var resized = NativeParagraph(box);
        using var reference = NativeTextParagraph.TryCreate([new(Sample, "Avenir Next", 32, 900, 2, Colors.Black)],
            "Avenir Next", 32, 650, 48, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(reference);
        Assert.Equal(reference.Caret(0, Sample.Length, false).X, resized.Caret(0, Sample.Length, false).X, 3);
    }

    [Fact]
    public void RichRun_ChangingWeightAndStyleRebuildsCachedParagraph()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var run = new Run(Sample) { FontFamily = new FontFamily("Avenir Next"), FontSize = 20 };
        var document = new FlowDocument { FontFamily = "Avenir Next", FontSize = 20 };
        document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        var box = new RichTextBox(document) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        NativeTextParagraph? previous = null;
        foreach (var row in NamedCases())
        {
            int weight = (int)row[0], style = (int)row[1]; string face = (string)row[2];
            run.FontWeight = FontWeight.FromOpenTypeWeight(weight); run.FontStyle = FontStyle.FromOpenTypeStyle(style); Arrange(box);
            var paragraph = NativeParagraph(box);
            if (previous is not null && previous != paragraph) Assert.True(previous.IsDisposed);
            using var expected = Paragraph(face, 400, style);
            Assert.Equal(expected.Caret(0, Sample.Length, false).X, paragraph.Caret(0, Sample.Length, false).X, 3);
            previous = paragraph;
        }
    }

    private static void Arrange(Control control) { control.Measure(new Size(650, 180)); control.Arrange(new Rect(0, 0, 650, 180)); }
    private static NativeTextParagraph Paragraph(string family, int weight, int style)
    {
        var paragraph = NativeTextParagraph.TryCreate([new(Sample, family, 20, weight, style, Colors.Black)],
            family, 20, 650, 28, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(paragraph); return paragraph;
    }
    private static NativeTextParagraph NativeParagraph(RichTextBox box)
    {
        object layout = typeof(RichTextBox).GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, [650d])!;
        object block = ((IList)layout.GetType().GetProperty("Blocks")!.GetValue(layout)!)[0]!;
        return (NativeTextParagraph)block.GetType().GetProperty("NativeParagraph")!.GetValue(block)!;
    }
}
