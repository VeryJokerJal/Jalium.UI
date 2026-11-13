using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssWritingToolsTests : IDisposable
{
    private readonly Interop.RenderContext _context = Interop.RenderContext.GetOrCreateCurrent(Interop.RenderBackend.Metal);

    [Theory]
    [InlineData("document", "display:block;padding:8px;border:2px solid green;")]
    [InlineData("section", "display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;")]
    [InlineData("paragraph", "display:grid;grid-template-columns:40px 70px;gap:7px;")]
    [InlineData("span", "display:flex;gap:8px;")]
    [InlineData("run", "display:inline-block;width:60px;")]
    [InlineData("document", "display:block;columns:2;height:48px;column-fill:auto;")]
    [InlineData("document", "display:block;width:90px;line-clamp:2;")]
    public void PresentationFiltersOriginalTextInEveryProjectedBox(string target, string style)
    {
        using var f = new CssRichTextBoxTests.Fixture(target); f.Style(style); f.Layout();
        var source = Source(f); var before = f.Document.GetText(); var original = Paint(f);
        int start = source.Text.IndexOf("BB", StringComparison.Ordinal);
        MacOSWritingToolsPresentation.Prepare(f.Editor, source, "range", start, 2, 0, () => true);
        try
        {
            var hidden = Paint(f);
            Assert.Contains("BB", hidden.HiddenText);
            Assert.DoesNotContain("AA", hidden.HiddenText);
            Assert.DoesNotContain("CC", hidden.HiddenText);
            Assert.DoesNotContain("ZZ", hidden.HiddenText);
            Assert.Equal(original.AllText, hidden.AllText);
            Assert.Equal(before, f.Document.GetText()); f.OriginalNodes();
        }
        finally { MacOSWritingToolsPresentation.Finish(f.Editor, "range"); }
        Assert.Equal("", Paint(f).HiddenText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExposedRangesSkipHiddenUtf16BeforeAndInsideAParagraph(bool hideParagraph)
    {
        using var f = new CssRichTextBoxTests.Fixture("document");
        f.Runs[0].Text = "👨‍👩‍👧‍👦隱藏"; f.Runs[1].Text = "隱藏";
        Css.SetStyle(f.Runs[0], "display:none;"); Css.SetStyle(f.Runs[1], "display:none;");
        f.Style("display:block;padding:4px;");
        if (hideParagraph) Css.SetStyle(f.Paragraph, "display:none;");
        f.Layout(); var source = Source(f); var word = hideParagraph ? "ZZ" : "CC";
        Assert.DoesNotContain("隱藏", source.Text);
        Assert.True(f.Document.GetText().IndexOf(word, StringComparison.Ordinal) > source.Text.IndexOf(word, StringComparison.Ordinal));
        MacOSWritingToolsPresentation.Prepare(f.Editor, source, "visible", source.Text.IndexOf(word, StringComparison.Ordinal), 2, 0, () => true);
        try { Assert.Contains(word, Paint(f).HiddenText); Assert.DoesNotContain(hideParagraph ? "CC" : "DD", Paint(f).HiddenText); }
        finally { MacOSWritingToolsPresentation.Clear(f.Editor); }
        Assert.Equal("", Paint(f).HiddenText); f.OriginalNodes();
    }

    [Theory]
    [InlineData("text-transform:uppercase;", "aßb", 1, 1, "SS")]
    [InlineData("text-transform:full-width;", "abc", 1, 1, "ｂ")]
    [InlineData("white-space:normal;", "a   b", 4, 1, "b")]
    [InlineData("letter-spacing:2px;", "abc", 1, 1, "b")]
    [InlineData("word-spacing:5px;", "aa bb", 3, 2, "bb")]
    [InlineData("font-size:24px;font-style:italic;", "fff", 0, 3, "fff")]
    [InlineData("text-transform:uppercase;letter-spacing:2px;", "aßb", 1, 1, "SS")]
    [InlineData("text-transform:uppercase;overflow-wrap:anywhere;", "ßßßßßßßß", 0, 8, "SS")]
    public void PaintedGlyphIndicesFollowSourceUtf16AfterTextTransformation(string style, string text, int start, int length, string expected)
    {
        using var f = new CssRichTextBoxTests.Fixture("document");
        f.Runs[1].Text = text; Css.SetStyle(f.Runs[1], style);
        f.Style("display:block;width:90px;"); f.Layout(160, 600); var source = Source(f);
        int offset = source.Text.IndexOf(text, StringComparison.Ordinal);
        MacOSWritingToolsPresentation.Prepare(f.Editor, source, "paint", offset + start, length, 0, () => true);
        try
        {
            var hidden = Paint(f).HiddenText; Assert.Contains(expected, hidden);
            Assert.DoesNotContain("AA", hidden); Assert.DoesNotContain("CC", hidden); Assert.DoesNotContain("ZZ", hidden);
        }
        finally { MacOSWritingToolsPresentation.Clear(f.Editor); }
        Assert.Equal("", Paint(f).HiddenText);
    }

    [Theory]
    [InlineData("finish")]
    [InlineData("clear")]
    [InlineData("stale")]
    public void CachedTextRestoresAfterEachPresentationLifetime(string action)
    {
        using var f = new CssRichTextBoxTests.Fixture("span"); f.Style("display:inline;padding:2px;"); f.Layout();
        var source = Source(f); bool current = true;
        MacOSWritingToolsPresentation.Prepare(f.Editor, source, "first", source.Text.IndexOf("BB", StringComparison.Ordinal), 2, 0, () => current);
        try
        {
            Assert.Contains("BB", Paint(f).HiddenText);
            MacOSWritingToolsPresentation.Prepare(f.Editor, source, "second", source.Text.IndexOf("CC", StringComparison.Ordinal), 2, 0, () => current);
            Assert.Contains("CC", Paint(f).HiddenText);
            if (action == "finish") { MacOSWritingToolsPresentation.Finish(f.Editor, "first"); Assert.DoesNotContain("BB", Paint(f).HiddenText); MacOSWritingToolsPresentation.Finish(f.Editor, "second"); }
            else if (action == "clear") MacOSWritingToolsPresentation.Clear(f.Editor);
            else current = false;
            Assert.Equal("", Paint(f).HiddenText);
        }
        finally { MacOSWritingToolsPresentation.Clear(f.Editor); }
    }

    [Fact]
    public void ReflowAndSourceInsertionRefreshTheSameOriginalRange()
    {
        using var f = new CssRichTextBoxTests.Fixture("document"); f.Style("display:block;width:160px;"); f.Layout(); var source = Source(f);
        MacOSWritingToolsPresentation.Prepare(f.Editor, source, "edit", source.Text.IndexOf("CC", StringComparison.Ordinal), 2, 0, () => true);
        try
        {
            Assert.Contains("CC", Paint(f).HiddenText);
            f.Runs[0].Text = "甲乙🙂 AA "; f.Style("display:block;width:90px;columns:2;height:72px;"); f.Layout();
            MacOSWritingToolsPresentation.ObserveText(f.Editor, source.Text);
            Assert.Contains("CC", Paint(f).HiddenText); Assert.DoesNotContain("DD", Paint(f).HiddenText); f.OriginalNodes();
        }
        finally { MacOSWritingToolsPresentation.Clear(f.Editor); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayChangesAndReturningToNativeLayoutKeepTheServiceRange(bool native)
    {
        using var f = new CssRichTextBoxTests.Fixture("document"); f.Style("display:block;padding:4px;"); f.Layout();
        var source = Source(f); var original = f.Document.GetText();
        MacOSWritingToolsPresentation.Prepare(f.Editor, source, "flow", source.Text.IndexOf("CC", StringComparison.Ordinal), 2, 0, () => true);
        try
        {
            Css.SetStyle(f.Runs[0], "display:none;"); f.Layout(140, 300); MacOSWritingToolsPresentation.ObserveText(f.Editor, source.Text);
            Assert.Contains("CC", Paint(f).HiddenText); Assert.DoesNotContain("DD", Paint(f).HiddenText);
            if (native)
            {
                Css.SetStyle(f.Runs[0], ""); Css.SetStyle(f.Document, ""); Css.SetStyleSheet(f.Document, ""); f.Layout(240, 300);
                MacOSWritingToolsPresentation.ObserveText(f.Editor, source.Text);
                Assert.Null(typeof(RichTextBox).GetField("_cssDocumentView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(f.Editor));
                Assert.Contains("CC", Paint(f).HiddenText);
            }
            Assert.Equal(original, f.Document.GetText()); f.OriginalNodes();
        }
        finally { MacOSWritingToolsPresentation.Clear(f.Editor); }
        Assert.Equal("", Paint(f).HiddenText);
    }

    private static IAutomationTextProviderSource Source(CssRichTextBoxTests.Fixture f) => ((AutomationTextProvider)f.Provider).Source;
    private static Capture Paint(CssRichTextBoxTests.Fixture f) { var capture = new Capture(); f.Editor.Render(capture); return capture; }
    private sealed class Capture : DrawingContextAdapter
    {
        internal string AllText = "", HiddenText = "";
        public override void DrawText(FormattedText text, Point point)
        {
            AllText += text.Text;
            if (text.GlyphVisibility == 2 && text.GlyphRanges is { } ranges)
                for (int i = 0; i < ranges.Length; i += 2) HiddenText += text.Text.Substring((int)ranges[i], (int)ranges[i + 1]);
        }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radii) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double value) { }
        public override void Pop() { }
        public override void Close() { }
    }
    public void Dispose() { _ = _context; }
}
