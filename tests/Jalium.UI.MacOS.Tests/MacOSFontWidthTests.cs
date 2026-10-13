using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFontWidthTests : MacOSGeometryTestBase
{
    private const string Sample = "MMMM iii1 abc xyz tail";

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void FormattedText_StretchSelectsARealCondensedFaceAndSeparatesWarmCache(int stretch)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        using var reference = context.CreateTextFormat("HelveticaNeue-CondensedBold", 20, 400, 0);
        var value = new FormattedText(Sample, "Helvetica Neue", 20) { FontWeight = 700, MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(value)); double normal = value.Width;
        value.FontStretch = stretch;
        Assert.True(TextMeasurement.MeasureText(value));
        Assert.Equal(reference.MeasureText(Sample, 100000, 1000).Width, value.Width); Assert.NotEqual(normal, value.Width);
        value.FontStretch = 5;
        Assert.True(TextMeasurement.MeasureText(value)); Assert.Equal(normal, value.Width);
    }

    [Fact]
    public void FormattedText_TypefaceAndSetFontStretchUseTheSameCondensedMetrics()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var value = new FormattedText(Sample, "Helvetica Neue", 20) { FontWeight = 700, MaxTextWidth = double.PositiveInfinity };
        value.SetFontStretch(FontStretches.Condensed);
        using var reference = context.CreateTextFormat("HelveticaNeue-CondensedBold", 20);
        Assert.True(TextMeasurement.MeasureText(value)); Assert.Equal(reference.MeasureText(Sample, 100000, 1000).Width, value.Width);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Editors_LiveWidthChangesRefreshImeCaretAndSelection(int kind)
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); using var context = new RenderContext(RenderBackend.Metal);
        var document = new FlowDocument { FontSize = 20, FontFamily = new FontFamily("Helvetica Neue"), FontWeight = FontWeights.Bold };
        document.Blocks.Add(new Paragraph(new Run(Sample)) { Margin = new Thickness(0) });
        Control editor = kind switch { 0 => new TextBox { Text = Sample, TextWrapping = TextWrapping.NoWrap },
            1 => new EditControl { Text = Sample, ShowLineNumbers = false }, _ => new RichTextBox(document) };
        editor.FontFamily = new FontFamily("Helvetica Neue"); editor.FontSize = 20; editor.FontWeight = FontWeights.Bold;
        editor.Padding = new Thickness(0); editor.BorderThickness = new Thickness(0);
        var window = new DisplayedTestWindow { Content = editor };
        try
        {
            Assert.True(editor.Focus());
            foreach (bool condensed in new[] { false, true, false })
            {
                var stretch = condensed ? FontStretches.Condensed : FontStretches.Normal;
                editor.FontStretch = stretch; document.FontStretch = stretch;
                editor.Measure(new Size(650, 180)); editor.Arrange(new Rect(0, 0, 650, 180));
                var support = (IImeSupport)editor; Assert.True(support.TrySetImeSelection(0, 0)); double origin = support.GetImeCaretRectangle().X;
                Assert.True(support.TrySetImeSelection(Sample.Length, 0));
                string family = condensed ? "HelveticaNeue-CondensedBold" : "HelveticaNeue-Bold";
                using var reference = NativeTextParagraph.TryCreate([new(Sample, family, 20, 400, 0, Colors.Black)],
                    family, 20, 100000, 48, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)!;
                double x = reference.Caret(0, Sample.Length, false).X;
                Assert.Equal(kind == 0 ? Math.Round(x) : x, support.GetImeCaretRectangle().X - origin, 3);
                Assert.True(support.TrySetImeSelection(3, 4)); Assert.True(support.TryGetImeSurroundingText(out var text));
                Assert.Equal(3, Math.Min(text.CursorIndex, text.AnchorIndex)); Assert.Equal(7, Math.Max(text.CursorIndex, text.AnchorIndex));
            }
        }
        finally { window.Content = null; Keyboard.ClearFocus(); }
    }

    [Theory]
    [InlineData(75, 62.5, 90, "Andale Mono")]
    [InlineData(125, 100, 150, "Arial")]
    [InlineData(5000, 4999, 7500, "Arial")]
    public void CssWidthMatching_SearchesTheRequiredDirectionBeforeNearestDistance(double requested, double first, double second, string expected)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = new TextBlock { Text = Sample, FontSize = 20 };
        string Number(double n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Css.SetStyleSheet(owner, $$"""
            @font-face {font-family:Width44;src:local('Andale Mono');font-width:{{Number(first)}}%;font-display:swap;}
            @font-face {font-family:Width44;src:local('Arial');font-width:{{Number(second)}}%;font-display:swap;}
            """);
        Css.SetStyle(owner, $"font-family:Width44, Helvetica;font-width:{Number(requested)}%");
        try
        {
            _ = Source(owner); Wait(owner); string source = Source(owner);
            Assert.Equal(Measure(expected), Measure(source)); Assert.Equal("Width44, Helvetica", owner.FontFamily.Source);
        }
        finally { Clear(owner); }
    }

    [Fact]
    public void CssWidthMatching_SelectsWidthBeforeStyleWeightAndUnicodeRange()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = new TextBlock { Text = Sample, FontSize = 20 };
        Css.SetStyleSheet(owner, """
            @font-face {font-family:Order44;src:local('Andale Mono');font-width:75%;font-weight:400;font-display:swap;}
            @font-face {font-family:Order44;src:local('Arial');font-width:100%;font-weight:700;font-display:swap;}
            """);
        Css.SetStyle(owner, "font-family:Order44, Helvetica;font-width:75%;font-weight:700");
        try { _ = Source(owner); Wait(owner); Assert.Equal(Measure("Andale Mono"), Measure(Source(owner), weight:700)); }
        finally { Clear(owner); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CssWidth_WithLocalFamilyOrInheritedValueKeepsWidthAndNativePrecedence(bool inherited)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = new TextBlock { Text = Sample, FontFamily = new FontFamily("Helvetica Neue"), FontSize = 20, FontWeight = FontWeights.Bold };
        var parent = new StackPanel(); parent.Children.Add(owner);
        Css.SetStyle(inherited ? parent : owner, "font-width:75%");
        try
        {
            Assert.Equal(Measure("HelveticaNeue-CondensedBold"), Measure(Source(owner), weight:700));
            owner.FontStretch = FontStretches.Normal;
            Assert.Equal(Measure("HelveticaNeue-Bold"), Measure(Source(owner), weight:700));
            owner.ClearValue(TextBlock.FontStretchProperty);
            Assert.Equal(Measure("HelveticaNeue-CondensedBold"), Measure(Source(owner), weight:700));
        }
        finally { Clear(owner); Clear(parent); parent.Children.Clear(); }
    }

    [Fact]
    public void InlineWidth_UsesRunOwnerAndChangingItInvalidatesGeometry()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var text = new TextBlock { FontFamily = new FontFamily("Helvetica Neue"), FontSize = 20, FontWeight = FontWeights.Bold };
        var run = new Run(Sample) { FontStretch = FontStretches.Condensed }; text.Inlines.Add(run);
        text.Measure(new Size(800, 100)); double condensed = text.DesiredSize.Width;
        double Reference(string family)
        {
            var reference = new TextBlock { FontFamily = new FontFamily("Helvetica Neue"), FontSize = 20, FontWeight = FontWeights.Bold };
            reference.Inlines.Add(new Run(Sample) { FontFamily = new FontFamily(family), FontWeight = FontWeights.Normal });
            reference.Measure(new Size(800, 100)); return reference.DesiredSize.Width;
        }
        Assert.Equal(Reference("HelveticaNeue-CondensedBold"), condensed);
        run.FontStretch = FontStretches.Normal; text.Measure(new Size(800, 100));
        Assert.Equal(Reference("HelveticaNeue-Bold"), text.DesiredSize.Width); Assert.NotEqual(condensed, text.DesiredSize.Width);
    }

    [Fact]
    public void CssWidthDescriptor_ClampsAnInstalledFaceAndRetainsCaretHitTests()
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = new TextBlock { Text = Sample, FontSize = 20 };
        Css.SetStyleSheet(owner, "@font-face {font-family:Clamp44;src:local('Helvetica Neue');font-width:75%;font-weight:700;font-display:swap;}");
        Css.SetStyle(owner, "font-family:Clamp44, Helvetica;font-width:95%;font-weight:700");
        try
        {
            _ = Source(owner); Wait(owner); string source = Source(owner);
            Assert.Equal(Measure("HelveticaNeue-CondensedBold"), Measure(source, weight:700));
            Assert.True(TextMeasurement.HitTestTextPositionWrapped(Sample, source, 20, 700, 0, float.PositiveInfinity,
                (uint)Sample.Length, false, out var caret));
            using var reference = context.CreateTextFormat("HelveticaNeue-CondensedBold", 20); reference.SetNoWrap(true);
            Assert.True(reference.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var expected));
            Assert.Equal(expected.CaretX, caret.CaretX);
        }
        finally { Clear(owner); }
    }

    private static string Source(TextBlock owner) => owner.FontFamily.GetRenderingSource(owner);

    [Theory]
    [InlineData("font-width")] [InlineData("font-stretch")]
    public void CssWidth_ExactPercentagesWithinOneClassInvalidateLiveLayout(string property)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = new TextBlock { Text = Sample, FontSize = 20, FontFamily = new FontFamily("SF Pro") };
        try
        {
            Css.SetStyle(owner, $"{property}:83.2%"); owner.Measure(new Size(800, 100));
            double first = owner.DesiredSize.Width; var bucket = owner.FontStretch;
            Css.SetStyle(owner, $"{property}:92.8%"); owner.Measure(new Size(800, 100));
            Assert.Equal(bucket, owner.FontStretch); Assert.NotEqual(first, owner.DesiredSize.Width);
            Css.SetStyle(owner, $"{property}:83.2%"); owner.Measure(new Size(800, 100)); Assert.Equal(first, owner.DesiredSize.Width);
        }
        finally { Clear(owner); }
    }
    private static double Measure(string family, int weight = 400)
    {
        var value = new FormattedText(Sample, family, 20) { FontWeight = weight, MaxTextWidth = double.PositiveInfinity };
        Assert.True(TextMeasurement.MeasureText(value)); return value.Width;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CssWidth_FontRelativeRulersTrackExactAndInheritedWidth(bool inherited)
    {
        using var context = new RenderContext(RenderBackend.Metal);
        var owner = new TextBlock { Text = Sample, FontSize = 20, FontFamily = new FontFamily("SF Pro") };
        var parent = new StackPanel(); parent.Children.Add(owner);
        try
        {
            Css.SetStyle(owner, "width:10ch");
            Css.SetStyle(inherited ? parent : owner, inherited ? "font-width:83.2%" : "font-width:83.2%;width:10ch");
            CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);
            owner.Measure(new Size(800, 100));
            double first = owner.Width;
            Assert.Equal(10 * TextMeasurement.GetFontUnitMetrics(Source(owner), 20).ZeroAdvance, first, 3);
            Css.SetStyle(inherited ? parent : owner, inherited ? "font-width:92.8%" : "font-width:92.8%;width:10ch");
            CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);
            owner.Measure(new Size(800, 100));
            Assert.Equal(10 * TextMeasurement.GetFontUnitMetrics(Source(owner), 20).ZeroAdvance, owner.Width, 3);
            Assert.NotEqual(first, owner.Width);
        }
        finally { Clear(owner); Clear(parent); parent.Children.Clear(); }
    }
    private static void Wait(TextBlock owner)
    {
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(owner).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(owner.Dispatcher); Assert.Empty(Css.GetFontLoadErrors(owner));
    }
    private static void Clear(DependencyObject owner) { Css.SetStyleSheets(owner, null); Css.SetStyle(owner, string.Empty); }
}
