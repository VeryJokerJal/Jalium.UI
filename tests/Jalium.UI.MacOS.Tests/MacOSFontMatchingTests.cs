using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFontMatchingTests : MacOSGeometryTestBase
{
    private const string Sample = "MMMM iii1 abc אבג xyz tail";

    [Theory]
    [InlineData(750, 0)] [InlineData(800, 0)] [InlineData(900, 0)]
    [InlineData(750, 1)] [InlineData(800, 1)] [InlineData(900, 1)]
    public void NormalWidth_FamilyWeightCannotSelectACondensedFace(int weight, int style)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var expected = context.CreateTextFormat("HelveticaNeue-Bold", 20, 400, style); expected.SetNoWrap(true);
        var formatted = new FormattedText(Sample, "Helvetica Neue", 20)
            { FontWeight = weight, FontStyle = style, MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(formatted));
        Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, formatted.Width);
        using var paragraph = NativeTextParagraph.TryCreate([new(Sample, "Helvetica Neue", 20, weight, style, Colors.Black)],
            "Helvetica Neue", 20, 650, 48, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(paragraph);
        Assert.True(expected.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var caret));
        Assert.Equal(caret.CaretX, paragraph.Caret(0, Sample.Length, false).X, 3);
    }

    [Theory]
    [InlineData(450, "HelveticaNeue-Medium")]
    [InlineData(501, "HelveticaNeue-Bold")]
    [InlineData(650, "HelveticaNeue-Bold")]
    [InlineData(750, "HelveticaNeue-Bold")]
    public void MissingWeight_UsesTheSpecifiedSearchDirection(int weight, string face)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var expected = context.CreateTextFormat(face, 20); expected.SetNoWrap(true);
        var formatted = new FormattedText(Sample, "Helvetica Neue", 20)
            { FontWeight = weight, MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(formatted));
        Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, formatted.Width);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Editors_DefaultAndCssNormalWidthRefreshHeavyFamilyCaret(int kind)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var run = new Run(Sample) { FontFamily = new FontFamily("Helvetica Neue"), FontSize = 20 };
        var document = new FlowDocument { FontFamily = "Helvetica Neue", FontSize = 20 };
        document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        Control editor = kind switch { 0 => new TextBox { Text = Sample, TextWrapping = TextWrapping.NoWrap },
            1 => new EditControl { Text = Sample, ShowLineNumbers = false }, _ => new RichTextBox(document) };
        editor.FontFamily = new FontFamily("Helvetica Neue"); editor.FontSize = 20;
        editor.Padding = new Thickness(0); editor.BorderThickness = new Thickness(0);
        var support = (IImeSupport)editor;
        foreach (int weight in new[] { 400, 900, 400, 900 })
        {
            editor.FontWeight = FontWeight.FromOpenTypeWeight(weight); run.FontWeight = editor.FontWeight;
            editor.Measure(new Size(650, 180)); editor.Arrange(new Rect(0, 0, 650, 180));
            Assert.True(support.TrySetImeSelection(0, 0)); double first = support.GetImeCaretRectangle().X;
            Assert.True(support.TrySetImeSelection(Sample.Length, 0));
            using var expected = context.CreateTextFormat(weight == 400 ? "HelveticaNeue" : "HelveticaNeue-Bold", 20);
            expected.SetNoWrap(true); Assert.True(expected.HitTestTextPosition(Sample, 100000, 1000,
                (uint)Sample.Length, false, out var caret));
            Assert.Equal(kind == 0 ? Math.Round(caret.CaretX) : caret.CaretX, support.GetImeCaretRectangle().X - first, 2);
        }
        Css.SetStyle(editor, "font-width:100%"); Css.SetStyle(run, "font-width:100%");
        editor.Measure(new Size(650, 180)); editor.Arrange(new Rect(0, 0, 650, 180));
        using var bold = context.CreateTextFormat("HelveticaNeue-Bold", 20); bold.SetNoWrap(true);
        Assert.True(bold.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var end));
        Assert.True(support.TrySetImeSelection(0, 0)); double origin = support.GetImeCaretRectangle().X;
        Assert.True(support.TrySetImeSelection(Sample.Length, 0));
        Assert.Equal(kind == 0 ? Math.Round(end.CaretX) : end.CaretX, support.GetImeCaretRectangle().X - origin, 2);
    }

    [Fact]
    public void CssAndPrivateCollection_NormalWidthSelectNormalBold()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var expected = context.CreateTextFormat("HelveticaNeue-Bold", 20); expected.SetNoWrap(true);
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc"));
        Assert.NotNull(resource);
        foreach (string family in new[] { "Helvetica Neue", resource.Family })
        {
            var text = new TextBlock { Text = Sample, FontSize = 20 };
            Css.SetStyle(text, $"font-family:'{family}';font-weight:900;font-width:100%");
            var value = new FormattedText(Sample, text.FontFamily.GetRenderingSource(text), 20)
                { FontWeight = text.FontWeight.ToOpenTypeWeight(), MaxTextWidth = double.PositiveInfinity };
            Assert.True(TextMeasurement.MeasureText(value));
            Assert.Equal(expected.MeasureText(Sample, 100000, 1000).Width, value.Width);
        }
    }

    [Fact]
    public void ExplicitNormalWidthFactory_UsesFullMatching()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var normal = context.CreateTextFormat("Helvetica Neue", 20, 900, 0, 100);
        using var bold = context.CreateTextFormat("HelveticaNeue-Bold", 20);
        Assert.Equal(bold.MeasureText(Sample, 100000, 1000).Width, normal.MeasureText(Sample, 100000, 1000).Width);
    }

    [Fact]
    public void ExplicitFaceAndLegacyFactory_RetainRequestedNativeFace()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var explicitFace = context.CreateTextFormat("HelveticaNeue-CondensedBlack", 20);
        var value = new FormattedText(Sample, "HelveticaNeue-CondensedBlack", 20) { MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(value));
        Assert.Equal(explicitFace.MeasureText(Sample, 100000, 1000).Width, value.Width);
        using var legacy = context.CreateTextFormat("Helvetica Neue", 20, 900, 0);
        Assert.Equal(explicitFace.MeasureText(Sample, 100000, 1000).Width, legacy.MeasureText(Sample, 100000, 1000).Width);
    }
}
