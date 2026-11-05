using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCapsCombiningContextTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("Seravek", "tr", "I", 5, 2, 4)]
    [InlineData("Seravek", "az-Latn", "I", 5, 2, 4)]
    [InlineData("Arial", "lt", "i", 1, 1, 8)]
    [InlineData("Arial", "lt", "j", 1, 1, 8)]
    public void ALeadingMarkUsesTheResolvedParagraphOperationAndKeepsItsSource(string family, string language,
        string prefix, int mode, byte operation, byte boundary)
    {
        string wholeFamily = FontFeatureRenderingSource.Wrap(family, [new(FontFeature.TagOf("smcp"), 0)],
            (CssCapsVariant)mode, new(language));
        using var whole = NativeTextParagraph.TryCreate([new(prefix + "\u0307", wholeFamily, 32, 400, 0, Colors.Black)],
            family, 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.NotNull(whole); Assert.Equal(new[] { operation, operation }, whole.CapsOperations());
        string fragment = FontFeatureRenderingSource.WithCapsFragment(wholeFamily, boundary, operation);
        using var mark = NativeTextParagraph.TryCreateAdvanced([new("\u0307", fragment, 32, 400, 0, Colors.Red)],
            family, 32, 10000, 44, FlowDirection.LeftToRight, [new() { TextPosition = 0, Length = 1 }], contextPaint:true);
        Assert.NotNull(mark); Assert.Equal("\u0307", mark.Text);
        var line = Assert.Single(mark.Lines); Assert.Equal(0u, line.Metrics.Line.TextPosition); Assert.Equal(1u, line.Metrics.Line.Length);
        Assert.Equal(0, line.Metrics.Line.Width);
        Assert.Equal(0u, mark.Caret(0, 0, false).TextPosition); Assert.Equal(1u, mark.Caret(0, 1, false).TextPosition);
        var cell = Assert.Single(mark.Selection(0, 0, 1)); Assert.Equal(0u, cell.TextPosition); Assert.Equal(1u, cell.Length); Assert.Equal(0, cell.Width);
        Assert.Equal(new[] { operation }, mark.CapsOperations());
    }

    [Fact]
    public void TheBaseFaceDeterminesTheOperationAcrossFallbackAndPolicyChanges()
    {
        string Caps(string family, bool disabled = false, CssCapsVariant mode = CssCapsVariant.Unicase, string language = "tr") =>
            FontFeatureRenderingSource.Wrap(family, [new(FontFeature.TagOf("smcp"), 0)], mode, new(language, disabled));
        foreach (var row in new[] {
            (Caps("Seravek"), Caps("Arial"), (byte)2, (byte)2),
            (Caps("Arial", true), Caps("Seravek", true), (byte)0, (byte)0),
            (Caps("Seravek"), Caps("Seravek", mode:CssCapsVariant.Normal), (byte)2, (byte)0) })
        {
            using var paragraph = NativeTextParagraph.TryCreate([new("I", row.Item1, 32, 400, 0, Colors.Black),
                new("\u0307", row.Item2, 32, 400, 0, Colors.Red)], "Seravek", 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight);
            Assert.NotNull(paragraph); Assert.Equal(new[] { row.Item3, row.Item4 }, paragraph.CapsOperations());
        }
    }

    [Fact]
    public void FragmentIdentityIsBoundedAndSurvivesMaterializationAndWidthChanges()
    {
        for (byte boundary = 0; boundary < 64; ++boundary)
        for (byte operation = 0; operation < 3; ++operation)
        {
            var context = new FontFeatureRenderingSource.CapsContext("lt", true, boundary, operation);
            string source = FontFeatureRenderingSource.Wrap(CssFontFaces.BlockPrefix + "Arial", [], CssCapsVariant.SmallCaps, context);
            source = CssFontFaces.MaterializeSource(source, "\u0307"); source = FontWidthRenderingSource.ForFormattedText(source, 4);
            source = FontFeatureRenderingSource.Wrap(source, [new(FontFeature.TagOf("kern"), 0)]);
            Assert.True(CssFontFaces.IsBlocked(source));
            Assert.True(FontFeatureRenderingSource.TryUnwrap(source, out var features, out var family, out var mode, out var decoded));
            Assert.Equal(context, decoded); Assert.Equal(CssCapsVariant.SmallCaps, mode); Assert.Single(features);
            Assert.True(FontWidthRenderingSource.TryUnwrap(family, out var width, out family)); Assert.Equal(87.5, width); Assert.Equal("Arial", family);
        }
        string plain = FontFeatureRenderingSource.Wrap("Arial", [], CssCapsVariant.SmallCaps);
        Assert.Equal(plain, FontFeatureRenderingSource.WithCapsFragment(FontFeatureRenderingSource.WithCapsFragment(plain, 8, 1), 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontFeatureRenderingSource.WithCapsFragment(plain, 8, 3));
    }

    [Theory] [InlineData("3")] [InlineData("-1")] [InlineData("+1")] [InlineData("256")] [InlineData("")]
    public void InvalidContinuationDoesNotPartiallyDecode(string operation)
    {
        string source = "\u0004font-features-caps-continuation:5:1:tr:4:" + operation + ":736D6370=0:Seravek";
        Assert.False(FontFeatureRenderingSource.TryUnwrap(source, out var features, out var family, out var mode, out var context));
        Assert.Empty(features); Assert.Equal(source, family); Assert.Equal(CssCapsVariant.Normal, mode); Assert.Equal(default, context);
    }

    [Theory]
    [InlineData("Seravek", "tr", "I", "i", "unicase")]
    [InlineData("Seravek", "az-Latn", "I", "i", "unicase")]
    [InlineData("Arial", "lt", "i", "I", "small-caps")]
    [InlineData("Arial", "lt", "j", "J", "small-caps")]
    public void SplitCombiningSourcesKeepDeletedMarksAndPhysicalEdges(string family, string language,
        string source, string paint, string variant)
    {
        foreach (bool physicalEdge in new[] { false, true })
        {
            var root = new StackPanel { Language = XmlLanguage.GetLanguage(language) };
            var first = new TextBlock { Text = source }; var mark = new TextBlock { Text = "\u0307" };
            root.Children.Add(first); root.Children.Add(mark);
            Css.SetStyleSheet(root, "TextBlock{display:inline}");
            Css.SetStyle(root, $"display:block;font-family:'{family}';font-size:32px;line-height:44px;font-variant-caps:{variant};font-feature-settings:'smcp' off;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;");
            if (physicalEdge) Css.SetStyle(mark, "padding-left:1px");
            using var reference = _context.CreateTextFormat(family, variant == "small-caps" ? 25.6f : 32, 400, 0);
            double width = reference.MeasureText(paint, 10000, 1000).Width;
            foreach (int viewport in new[] { 24, 40, 200, 40 })
            {
                for (int i = 0; i < 3; ++i) { root.Measure(new(viewport, 400)); root.Arrange(new(0, 0, viewport, 400)); root.UpdateLayout(); }
                var firstCell = Assert.Single(first.GetSourceRangeSegments(0, source.Length));
                var empty = Assert.Single(mark.GetSourceRangeSegments(0, 1));
                // A joined source grapheme can be owned by either element.
                // The separated zero-paint fragment keeps its own source range.
                if (physicalEdge) {
                    Assert.Equal(width, firstCell.EndX - firstCell.StartX, 3);
                    Assert.Equal(0, empty.EndX - empty.StartX, 3);
                }
                double right = Math.Max(first.TransformToAncestor(root).X + firstCell.EndX,
                    mark.TransformToAncestor(root).X + empty.EndX);
                Assert.Equal(width + (physicalEdge ? 1 : 0), right, 3);
                Assert.Equal(firstCell.Baseline + first.TransformToAncestor(root).Y,
                    empty.Baseline + mark.TransformToAncestor(root).Y, 3);
                mark.SelectAll(); Assert.Equal("\u0307", mark.SelectedText); mark.Select(0, 0);
            }
            Assert.Equal(source, first.Text); Assert.Equal("\u0307", mark.Text); Assert.Same(root, first.Parent); Assert.Same(root, mark.Parent);
        }
    }
}
