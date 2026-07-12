using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.TextFormatting;

namespace Jalium.UI.Tests;

public sealed class FormattedGlyphWpfParityTests
{
    [Fact]
    public void FormattedTextExposesWpfConstructorsAndReadOnlyMeasurements()
    {
        Type type = typeof(FormattedText);
        Assert.NotNull(type.GetConstructor([
            typeof(string), typeof(CultureInfo), typeof(FlowDirection), typeof(Typeface),
            typeof(double), typeof(Brush), typeof(NumberSubstitution), typeof(TextFormattingMode), typeof(double)]));
        Assert.False(type.GetProperty(nameof(FormattedText.Width))!.SetMethod!.IsPublic);
        Assert.False(type.GetProperty(nameof(FormattedText.Height))!.SetMethod!.IsPublic);
        Assert.False(type.GetProperty(nameof(FormattedText.Baseline))!.SetMethod!.IsPublic);

        var text = new FormattedText(
            "hello world",
            CultureInfo.GetCultureInfo("en-US"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Test Sans"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            20,
            Brushes.Black,
            new NumberSubstitution(),
            TextFormattingMode.Display,
            1.5);

        Assert.True(text.Width > 0);
        Assert.True(text.Height > 0);
        Assert.True(text.MinWidth > 0);
        Assert.Equal(1.5, text.PixelsPerDip);
        Assert.Equal((int)TextFormattingMode.Display, text.TextFormattingMode);
        Assert.Equal(text.Height, text.Extent);
        Assert.True(text.WidthIncludingTrailingWhitespace >= text.Width);
    }

    [Fact]
    public void FormattedTextRangeFormattingWrappingAndGeometryAreFunctional()
    {
        var text = new FormattedText("alpha beta gamma", "Test Sans", 10);
        text.SetFontSize(20, 6, 4);
        text.SetFontWeight(FontWeights.Bold, 0, 5);
        text.SetCulture(CultureInfo.InvariantCulture);
        text.SetForegroundBrush(Brushes.Red);
        text.SetTextDecorations(new TextDecorationCollection(TextDecorations.Underline));
        text.SetMaxTextWidths([45, 60]);
        text.MaxLineCount = 3;
        text.TextAlignment = TextAlignment.Center;

        double[] widths = text.GetMaxTextWidths();
        Assert.Equal([45d, 60d], widths);
        widths[0] = 1;
        Assert.Equal(45, text.GetMaxTextWidths()[0]);
        Assert.InRange(text.LineCount, 1, 3);

        Geometry highlight = text.BuildHighlightGeometry(new Point(10, 20), 6, 4);
        Assert.False(highlight.Bounds.IsEmpty);
        Assert.True(highlight.Bounds.X >= 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => text.SetFontSize(12, 15, 10));
        text.PixelsPerDip = 0;
        Assert.Equal(0, text.PixelsPerDip);
    }

    [Fact]
    public void FamilyTypefaceCarriesDeviceMetricsAndValueEquality()
    {
        var language = XmlLanguage.GetLanguage("en-us");
        var first = new FamilyTypeface
        {
            Weight = FontWeights.Bold,
            Style = FontStyles.Italic,
            Stretch = FontStretches.Expanded,
            DeviceFontName = "Device Face",
            CapsHeight = 0.72,
            XHeight = 0.51,
        };
        first.AdjustedFaceNames[language] = "Adjusted";
        first.DeviceFontCharacterMetrics[65] = new CharacterMetrics("0.5,0.8,0.7");

        var second = new FamilyTypeface
        {
            Weight = FontWeights.Bold,
            Style = FontStyles.Italic,
            Stretch = FontStretches.Expanded,
            DeviceFontName = "Device Face",
            CapsHeight = 0.72,
            XHeight = 0.51,
        };
        second.AdjustedFaceNames[language] = "Adjusted";
        second.DeviceFontCharacterMetrics[65] = new CharacterMetrics("0.5,0.8,0.7");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(typeof(FamilyTypeface).IsSealed);
        Assert.Equal(typeof(IDictionary<XmlLanguage, string>),
            typeof(FamilyTypeface).GetProperty(nameof(FamilyTypeface.AdjustedFaceNames))!.PropertyType);
    }

    [Fact]
    public void GlyphTypefaceProvidesMetricMapsNamesAndOutlines()
    {
        var typeface = new GlyphTypeface(new Uri("TestFont.ttf", UriKind.Relative), StyleSimulations.BoldSimulation);

        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(GlyphTypeface)));
        Assert.Equal(StyleSimulations.BoldSimulation, typeface.StyleSimulations);
        Assert.True(typeface.GlyphCount >= 256);
        Assert.True(typeface.CharacterToGlyphMap.TryGetValue('A', out ushort glyph));
        Assert.True(typeface.AdvanceWidths[glyph] > 0);
        Assert.False(typeface.GetGlyphOutline(glyph, 20, 20).Bounds.IsEmpty);
        Assert.Contains(typeface.FamilyNames.Values, static name => name == "TestFont");
    }

    [Fact]
    public void GlyphRunSupportsGeometryAlignmentAndCaretNavigation()
    {
        var typeface = new GlyphTypeface(new Uri("TestFont.ttf", UriKind.Relative));
        var run = new GlyphRun(
            typeface,
            bidiLevel: 0,
            isSideways: false,
            renderingEmSize: 20,
            pixelsPerDip: 1.25f,
            glyphIndices: [65, 66, 67],
            baselineOrigin: new Point(10, 30),
            advanceWidths: [8d, 9d, 10d],
            glyphOffsets: [default, default, default],
            characters: ['A', 'B', 'C'],
            deviceFontName: "Device Face",
            clusterMap: [0, 1, 2],
            caretStops: [true, true, true, true],
            language: XmlLanguage.GetLanguage("en-us"));

        Assert.True(run.IsHitTestable);
        Assert.Equal(1.25f, run.PixelsPerDip);
        Assert.Equal(27, run.ComputeAlignmentBox().Width);
        Assert.Equal(27, run.ComputeInkBoundingBox().Width);
        Assert.False(run.BuildGeometry().Bounds.IsEmpty);
        Assert.Equal(8, run.GetDistanceFromCaretCharacterHit(new CharacterHit(1, 0)));
        Assert.Equal(new CharacterHit(1, 0), run.GetNextCaretCharacterHit(new CharacterHit(0, 0)));
        Assert.Equal(new CharacterHit(0, 0), run.GetPreviousCaretCharacterHit(new CharacterHit(1, 0)));

        CharacterHit hit = run.GetCaretCharacterHitFromDistance(13, out bool inside);
        Assert.True(inside);
        Assert.Equal(1, hit.FirstCharacterIndex);
        Assert.Equal(1, hit.TrailingLength);
    }
}
