using System.Buffers.Binary;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFontCascadeTests : MacOSGeometryTestBase
{
    private const string Sample = "abc אבג tail";
    private const string Primary = "Avenir Next";
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool reverse in new[] { false, true })
            foreach (int weight in new[] { 400, 600, 900 })
                foreach (int style in new[] { 0, 1, 2 }) yield return [reverse, weight, style];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OrderedFonts_MeasurementParagraphCaretsAndSelectionMatchExplicitScriptFonts(bool reverse, int weight, int style)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        string first = reverse ? "New Peninim MT" : "Arial Hebrew";
        string second = reverse ? "Arial Hebrew" : "New Peninim MT";
        string family = $"'__Missing, Font42__', '{Primary}', '{first}', '{second}'";
        using var actual = Paragraph(family, Sample, weight, style);
        using var expected = Explicit(Sample, first, weight, style);
        CheckCaretsAndSelection(actual, expected);
        var formatted = new FormattedText(Sample, family, 20) { FontWeight = weight, FontStyle = style,
            MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(formatted));
        Assert.InRange(Math.Abs(expected.Caret(0, Sample.Length, false).X - formatted.Width), 0, .001);
        using var font = TextMeasurement.CreateTextFormatFromFamilyList(context, family, 20, weight, style);
        font.SetNoWrap(true);
        Assert.True(font.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var caret));
        Assert.Equal(expected.Caret(0, Sample.Length, false).X, caret.CaretX, 3);
        Assert.True(TextMeasurement.HitTestTextPositionWrapped(Sample, family, 20, weight, style,
            float.PositiveInfinity, (uint)Sample.Length, false, out var cached));
        Assert.Equal(caret.CaretX, cached.CaretX);
    }

    [Theory]
    [InlineData("abc אְבּג tail")]
    [InlineData("abc אבג 👩‍👩‍👧‍👦 tail")]
    [InlineData("abc אבג 中文 🇨🇳 tail")]
    public void OrderedFonts_KeepCombiningEmojiAndSystemFallbackGeometry(string text)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var actual = Paragraph("Avenir Next, New Peninim MT, Arial Hebrew", text);
        using var expected = Explicit(text, "New Peninim MT");
        CheckCaretsAndSelection(actual, expected);
    }

    [Fact]
    public void MeasurementCache_DistinguishesFontOrderWithoutClearing()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        double Measure(string family)
        {
            var value = new FormattedText("אבג", family, 20) { MaxTextWidth = double.PositiveInfinity };
            Assert.True(TextMeasurement.MeasureText(value)); return value.Width;
        }
        double arial = Measure("Avenir Next, Arial Hebrew, New Peninim MT");
        double peninim = Measure("Avenir Next, New Peninim MT, Arial Hebrew");
        Assert.NotEqual(arial, peninim);
        Assert.Equal(Measure("Arial Hebrew"), arial);
        Assert.Equal(Measure("New Peninim MT"), peninim);
        Assert.Equal(arial, Measure("Avenir Next, Arial Hebrew, New Peninim MT"));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Editors_ChangingFallbackOrderRefreshesPublicImeCaret(int kind)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        using var context = new RenderContext(RenderBackend.Metal);
        var run = new Run(Sample) { FontSize = 20 };
        var document = new FlowDocument { FontSize = 20 };
        document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        Control control = kind switch
        {
            0 => new TextBox { Text = Sample, TextWrapping = TextWrapping.NoWrap },
            1 => new EditControl { Text = Sample, ShowLineNumbers = false },
            _ => new RichTextBox(document)
        };
        control.FontSize = 20; control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
        var window = new DisplayedTestWindow { Content = control };
        try
        {
            Assert.True(control.Focus());
            foreach (string family in new[] { "Arial Hebrew", "New Peninim MT", "Arial Hebrew" })
            {
                control.FontFamily = new FontFamily($"Avenir Next, {family}");
                control.Measure(new Size(650, 180)); control.Arrange(new Rect(0, 0, 650, 180));
                var support = (IImeSupport)control;
                Assert.True(support.TrySetImeSelection(0, 0)); double origin = support.GetImeCaretRectangle().X;
                Assert.True(support.TrySetImeSelection(Sample.Length, 0));
                using var expected = Explicit(Sample, family);
                double width = expected.Caret(0, Sample.Length, false).X;
                Assert.Equal(kind == 0 ? Math.Round(width) : width, support.GetImeCaretRectangle().X - origin, 3);
                Assert.True(support.TryGetImeSurroundingText(out var text));
                Assert.Equal(Sample.Length, text.CursorIndex);
            }
        }
        finally { window.Content = null; Keyboard.ClearFocus(); }
    }

    [Fact]
    public void PrivateFallback_OwnsModifiedBytesAfterResourceAndTemporaryFormatsRelease()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        byte[] bytes = File.ReadAllBytes("/System/Library/Fonts/ArialHB.ttc");
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
        int hhea = Table(bytes, offset, "hhea"), hmtx = Table(bytes, offset, "hmtx");
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(hhea + 34));
        for (int i = 0; i < count; i++)
        {
            var advance = bytes.AsSpan(hmtx + i * 4, 2);
            BinaryPrimitives.WriteUInt16BigEndian(advance, checked((ushort)(BinaryPrimitives.ReadUInt16BigEndian(advance) + 256)));
        }
        using var resource = CssNativeFontResource.Create(bytes);
        Assert.NotNull(resource); string alias = resource.Family;
        using var installed = context.CreateTextFormat("Arial Hebrew", 20);
        using var privateFormat = context.CreateTextFormat(alias, 20);
        Assert.NotEqual(installed.MeasureText("אבג", 100000, 1000).Width, privateFormat.MeasureText("אבג", 100000, 1000).Width);
        using var expected = Explicit(Sample, alias);
        using var primary = TextMeasurement.CreateTextFormatFromFamilyList(context, $"Avenir Next, {alias}, Arial Hebrew", 20, 400, 0);
        privateFormat.Dispose(); resource.Dispose();
        Assert.Equal(1, NativeMethods.FontFamilyIsAvailable(alias));
        using var actual = Paragraph($"Avenir Next, {alias}, Arial Hebrew", Sample);
        CheckCaretsAndSelection(actual, expected);
        primary.Dispose();
        Assert.Equal(0, NativeMethods.FontFamilyIsAvailable(alias));
        CheckCaretsAndSelection(actual, expected);
    }

    [Fact]
    public void AFormatWithOnlyUnavailableSecondaryNamesKeepsItsPrimaryMetrics()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var actual = TextMeasurement.CreateTextFormatFromFamilyList(context,
            "Avenir Next, __MissingCascade42__, __MissingCascade42__", 20, 400, 0);
        using var expected = context.CreateTextFormat("Avenir Next", 20);
        Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, actual.MeasureText(Sample, 100000, 1000).Width);
    }

    private static int Table(byte[] bytes, uint offset, string name)
    {
        uint tag = BinaryPrimitives.ReadUInt32BigEndian(System.Text.Encoding.ASCII.GetBytes(name));
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan((int)offset + 4));
        for (int i = 0; i < count; i++)
        {
            int at = (int)offset + 12 + i * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at)) == tag)
                return checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 8)));
        }
        throw new InvalidDataException("Missing private-font fixture table");
    }

    private static NativeTextParagraph Paragraph(string family, string text, int weight = 400, int style = 0)
    {
        var value = NativeTextParagraph.TryCreate([new(text, family, 20, weight, style, Colors.Black)],
            Primary, 20, 650, 28, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(value); return value;
    }

    private static NativeTextParagraph Explicit(string text, string hebrew, int weight = 400, int style = 0)
    {
        int length = 0;
        while (4 + length < text.Length && text[4 + length] is >= '\u0590' and <= '\u05ff') length++;
        var value = NativeTextParagraph.TryCreate([new(text[..4], Primary, 20, weight, style, Colors.Black),
            new(text.Substring(4, length), hebrew, 20, weight, style, Colors.Black),
            new(text[(4 + length)..], Primary, 20, weight, style, Colors.Black)],
            Primary, 20, 650, 28, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(value); return value;
    }

    private static void CheckCaretsAndSelection(NativeTextParagraph actual, NativeTextParagraph expected)
    {
        Assert.Equal(expected.Text, actual.Text); Assert.Single(actual.Lines);
        for (int offset = 0; offset <= actual.Text.Length; offset++)
            foreach (bool backward in new[] { false, true })
                Assert.Equal(expected.Caret(0, offset, backward).X, actual.Caret(0, offset, backward).X, 3);
        foreach (int start in new[] { 0, 4, 5, 7 })
        {
            var reference = expected.Selection(0, start, expected.Text.Length - start);
            var rectangles = actual.Selection(0, start, actual.Text.Length - start);
            Assert.Equal(reference.Length, rectangles.Length);
            for (int i = 0; i < reference.Length; i++)
            {
                Assert.Equal(reference[i].X, rectangles[i].X, 3);
                Assert.Equal(reference[i].Width, rectangles[i].Width, 3);
            }
        }
    }
}
