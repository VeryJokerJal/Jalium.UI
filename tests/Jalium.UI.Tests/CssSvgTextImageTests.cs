using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSvgTextImageTests
{
    public static IEnumerable<object[]> Scenes()
    {
        foreach (var body in new[]
        {
            "<text x='12' y='60'>AVfi</text>",
            "<text x='12' y='60'>轮廓</text>",
            "<text x='12' y='60'>e&#x301;</text>",
            "<text x='12' y='60' style='font-style:italic'>f</text>",
            "<text x='12' y='60' opacity='.5'>AV</text>",
            "<text x='12' y='60' dx='0 -24' fill-opacity='.5'>HH</text>",
            "<text x='12' y='60' rotate='12'>H</text>",
            "<text x='12' y='60' textLength='92' lengthAdjust='spacingAndGlyphs'>AV</text>",
            "<text x='12' y='60'>A<tspan fill='blue'>V</tspan></text>",
            "<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><text x='12' y='60' fill='url(#g)'>AV</text>",
            "<defs><clipPath id='c'><rect x='24' width='100' height='90'/></clipPath></defs><text x='12' y='60' clip-path='url(#c)'>AV</text>",
            "<defs><mask id='m'><rect x='24' width='100' height='90' fill='white'/></mask></defs><text x='12' y='60' mask='url(#m)'>AV</text>",
        })
        foreach (var route in new[] { 0, 1, 2, 3, 4 }) yield return [body, route];
    }

    [Theory, MemberData(nameof(Scenes))]
    public void OrdinarySvgTextPaintsAcrossDrawingImageBrushAndReplay(string body, int route)
    {
        using var image = SvgImage.FromSvgString(Xml(body));
        using var reference = SvgImage.FromSvgStringWithTextOutlines(Xml(body));
        var actual = Paint(image, route);
        var expected = Paint(reference, route);
        Assert.Contains(expected.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        AssertPixels(expected, actual);
    }

    public static IEnumerable<object[]> UserSpacePaints()
    {
        foreach (var gradient in new[]
        {
            "<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='100' y2='0'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "<radialGradient id='g' gradientUnits='userSpaceOnUse' cx='48' cy='42' r='42'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
        })
        foreach (var placement in new[] { "", "rotate='12'", "textLength='92' lengthAdjust='spacingAndGlyphs'" })
        foreach (var route in new[] { 0, 1, 2, 3, 4 }) yield return [gradient, placement, route];
    }

    [Theory, MemberData(nameof(UserSpacePaints))]
    public void OrdinarySvgTextKeepsPaintServersInUserCoordinates(string gradient, string placement, int route)
    {
        var xml = Xml("<defs>" + gradient + "</defs><text x='28' y='60' " + placement + " fill='url(#g)'>AV</text>");
        using var image = SvgImage.FromSvgString(xml);
        using var reference = SvgImage.FromSvgStringWithTextOutlines(xml);
        var expected = Paint(reference, route);
        Assert.Contains(expected.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        AssertPixels(expected, Paint(image, route));
    }

    [Fact]
    public void APlainTextNodePreservesItsFontMetadataAndRealInkBounds()
    {
        using var image = SvgImage.FromSvgString(Xml("<text x='12' y='60' style='font-weight:bold'>Hi</text>"));
        var glyph = Assert.Single(Glyphs(image.Drawing!));
        Assert.Equal("Hi", glyph.FormattedText!.Text);
        Assert.Equal(700, glyph.FormattedText.FontWeight);
        Assert.Equal(48, glyph.FormattedText.FontSize);
        Assert.True(glyph.TryGetSvgOutline(out var outline));
        Assert.True(outline.IsFrozen);
        Assert.Equal(outline.Bounds, glyph.Bounds);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FrozenAndClonedSvgGlyphsCanBeRasterizedOnAnotherThread(bool currentValue)
    {
        using var image = SvgImage.FromSvgString(Xml("<text x='12' y='60'>轮廓</text>"));
        var drawing = currentValue ? image.Drawing!.CloneCurrentValue() : image.Drawing!.Clone();
        drawing.Freeze();
        var expected = SoftwareVectorRasterizer.Rasterize(image.Drawing!, 160, 90, new Rect(0, 0, 160, 90))!;
        Assert.Contains(expected.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        byte[]? actual = null; Exception? error = null;
        var thread = new Thread(() =>
        {
            try { actual = SoftwareVectorRasterizer.Rasterize(drawing, 160, 90, new Rect(0, 0, 160, 90)); }
            catch (Exception failure) { error = failure; }
        });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10))); Assert.Null(error);
        AssertPixels(expected, actual!);
    }

    [Fact]
    public void ForegroundAndOriginEditsUpdateTheSvgVectorInk()
    {
        using var image = SvgImage.FromSvgString(Xml("<text x='12' y='60'>H</text>"));
        var glyph = Assert.Single(Glyphs(image.Drawing!));
        var before = image.ContentGeneration;
        glyph.ForegroundBrush = Brushes.Blue;
        glyph.Origin = new Point(glyph.Origin.X + 12, glyph.Origin.Y);
        Assert.True(image.ContentGeneration > before);
        using var reference = SvgImage.FromSvgStringWithTextOutlines(Xml("<text x='24' y='60' style='fill:blue'>H</text>"));
        AssertPixels(Paint(reference, 3), Paint(image, 3));
    }

    [Fact]
    public void IncompatibleFormattedTextDoesNotReuseTheOldSvgOutline()
    {
        using var image = SvgImage.FromSvgString(Xml("<text x='12' y='60'>H</text>"));
        var glyph = Assert.Single(Glyphs(image.Drawing!));
        Assert.True(glyph.TryGetSvgOutline(out _));
        glyph.FormattedText!.FontWeight = 700;
        Assert.False(glyph.TryGetSvgOutline(out _));
    }

    private static string Xml(string body) =>
        "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='90' fill='red'><style>text{font:48px Helvetica}</style>" + body + "</svg>";

    private static IEnumerable<GlyphRunDrawing> Glyphs(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var item in Glyphs(child)) yield return item;
    }

    private static byte[] Paint(SvgImage image, int route)
    {
        if (route == 0) return SoftwareVectorRasterizer.Rasterize(image.Drawing!, 160, 90, new Rect(0, 0, 160, 90))!;
        var bitmap = new RenderTargetBitmap(160, 90, 96, 96, PixelFormats.Bgra32);
        var context = new SoftwareDrawingContext(bitmap);
        if (route == 1) image.Drawing!.RenderTo(context);
        else if (route == 2)
        {
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); image.Drawing!.RenderTo(recorder);
            DrawingReplayer.Replay(recorder.Commit(), context);
        }
        else if (route == 3) context.DrawImage(image, new Rect(0, 0, 160, 90));
        else context.DrawRectangle(new ImageBrush(image) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 160, 90));
        context.Close(); return bitmap.GetPixelBuffer();
    }

    private static void AssertPixels(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 3);
    }
}
