using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Text;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe partial class MacOSTextStyleAccessibilityTests : MacOSGeometryTestBase
{
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void TextAttributesFollowCurrentTypographyWithoutEditing(int type)
    {
        Control editor = type switch { 0 => new TextBox { Text = "中文🙂" }, 1 => new RichTextBox(FlowDocument.FromText("中文🙂")), _ => new EditControl { Text = "中文🙂" } };
        editor.FontFamily = new FontFamily("Arial"); editor.FontSize = 21;
        if (editor is RichTextBox rich) { rich.Document.FontFamily = editor.FontFamily; rich.Document.FontSize = editor.FontSize; }
        var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var range = provider.DocumentRange.FindText("中文🙂", false, false)!;
        Assert.Equal("Arial", range.GetAttributeValue(40005)); Assert.Equal(21d, range.GetAttributeValue(40006));
        if (editor is RichTextBox changed) changed.Document.FontSize = 25; else editor.FontSize = 25;
        Assert.Equal(25d, range.GetAttributeValue(40006)); Assert.Equal("中文🙂", range.GetText(-1));
    }

    [Fact]
    public void RichMixedAttributesAndFormatRangesRespectNestedSpans()
    {
        var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run("plain "));
        paragraph.Inlines.Add(new Bold(new Underline(new Run("Bold🙂") { FontSize = 27 })));
        var document = new FlowDocument { FontFamily = new FontFamily("Arial"), FontSize = 21 }; document.Blocks.Add(paragraph);
        var editor = new RichTextBox(document);
        var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var bold = provider.DocumentRange.FindText("Bold🙂", false, false)!;
        Assert.Equal(27d, bold.GetAttributeValue(40006)); Assert.Equal(700, bold.GetAttributeValue(40007));
        Assert.Equal(1, bold.GetAttributeValue(40030));
        var emoji = provider.DocumentRange.FindText("🙂", false, false)!; emoji.ExpandToEnclosingUnit(TextUnit.Format);
        Assert.Equal("Bold🙂", emoji.GetText(-1));
        Assert.Same(AutomationTextAttributeValues.Mixed, provider.DocumentRange.GetAttributeValue(40006));
        Assert.Null(provider.DocumentRange.GetAttributeValue(-1));
    }

    [Fact]
    public void FindAttributeReturnsMatchingContiguousRangesInEitherDirection()
    {
        var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run("base"));
        paragraph.Inlines.Add(new Run("first") { FontSize = 25 }); paragraph.Inlines.Add(new Run("second") { FontSize = 25 });
        paragraph.Inlines.Add(new Run("base")); paragraph.Inlines.Add(new Run("last🙂") { FontSize = 25 });
        var document = new FlowDocument { FontSize = 18 }; document.Blocks.Add(paragraph);
        var provider = (ITextProvider)new RichTextBox(document).GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        Assert.Equal("firstsecond", provider.DocumentRange.FindAttribute(40006, 25d, false)!.GetText(-1));
        Assert.Equal("last🙂", provider.DocumentRange.FindAttribute(40006, 25d, true)!.GetText(-1));
        Assert.Null(provider.DocumentRange.FindAttribute(40006, 55d, false));
    }

    [Fact]
    public void FormatEndpointMovesAcrossStylesRatherThanSingleCharacters()
    {
        var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run("one")); paragraph.Inlines.Add(new Run("two🙂") { FontSize = 25 });
        paragraph.Inlines.Add(new Run("three")); var doc = new FlowDocument { FontSize = 18 }; doc.Blocks.Add(paragraph);
        var provider = (ITextProvider)new RichTextBox(doc).GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var range = provider.DocumentRange.FindText("one", false, false)!;
        Assert.Equal(1, range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Format, 1));
        Assert.Equal("onetwo🙂", range.GetText(-1));
        Assert.Equal(-1, range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Format, -1));
        Assert.Equal("one", range.GetText(-1));
    }

    [Fact]
    public void FormatMovePreservesCaretsAndStopsWithoutChangingRanges()
    {
        var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run("one")); paragraph.Inlines.Add(new Run("two🙂") { FontSize = 25 });
        paragraph.Inlines.Add(new Run("three")); var document = new FlowDocument { FontSize = 18 }; document.Blocks.Add(paragraph);
        var provider = (ITextProvider)new RichTextBox(document).GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var first = provider.DocumentRange.FindText("ne", false, false)!;
        Assert.Equal(0, first.Move(TextUnit.Format, -1)); Assert.Equal("ne", first.GetText(-1));
        Assert.Equal(1, first.Move(TextUnit.Format, 1)); Assert.Equal("two🙂", first.GetText(-1));
        Assert.Equal(1, first.Move(TextUnit.Format, int.MaxValue)); Assert.Equal("three" + Environment.NewLine, first.GetText(-1));
        Assert.Equal(0, first.Move(TextUnit.Format, 1)); Assert.Equal("three" + Environment.NewLine, first.GetText(-1));
        Assert.Equal(-2, first.Move(TextUnit.Format, int.MinValue)); Assert.Equal("one", first.GetText(-1));
        first.MoveEndpointByRange(TextPatternRangeEndpoint.End, first, TextPatternRangeEndpoint.Start);
        Assert.Equal(1, first.Move(TextUnit.Format, 1)); Assert.Equal("", first.GetText(-1));
        Assert.Equal(3, first.CompareEndpoints(TextPatternRangeEndpoint.Start, provider.DocumentRange, TextPatternRangeEndpoint.Start));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void StyleSnapshotsPreserveRawUtf16AndCallerBufferLimits(int type)
    {
        using var fixture = new StyleFixture(type, "A🙂e\u0301中文");
        using var payload = fixture.Read(2, 1);
        byte[] raw = payload.RootElement.GetProperty("text16").GetBytesFromBase64();
        Assert.Equal(new byte[] { 0x42, 0xde }, raw);
        var run = Assert.Single(payload.RootElement.GetProperty("runs").EnumerateArray());
        Assert.Equal(0, run.GetProperty("start").GetInt32()); Assert.Equal(1, run.GetProperty("length").GetInt32());
        char* buffer = stackalloc char[1]; buffer[0] = '\uffff';
        var request = fixture.Request(MacOSAXOperation.TextStyles, 0, 1); request.Text = buffer; request.TextCapacity = 0;
        Assert.False(fixture.Tree.Handle(ref request)); Assert.True(request.TextCount > 0); Assert.Equal('\uffff', buffer[0]);
        request.TextCapacity = -1; Assert.False(fixture.Tree.Handle(ref request)); Assert.Equal('\uffff', buffer[0]);
        Assert.False(fixture.TryRead(-1, 1)); Assert.False(fixture.TryRead(0, int.MaxValue)); Assert.False(fixture.TryRead(int.MaxValue, 0));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void StyleQueriesAreReadableOffscreenButRejectHiddenAndDetachedOwners(int type)
    {
        using var fixture = new StyleFixture(type, "中文🙂");
        fixture.Parent.Clip = new RectangleGeometry(new Rect(1000, 1000, 1, 1));
        Assert.True(fixture.TryRead(0, 1));
        fixture.Parent.Visibility = Visibility.Hidden; Assert.False(fixture.TryRead(0, 1));
        fixture.Parent.Visibility = Visibility.Visible; Assert.True(fixture.TryRead(0, 1));
        fixture.Window.Content = null; Assert.False(fixture.TryRead(0, 1));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void EmptyDocumentsAndEofStyleMatchAppKitWithoutChangingSelection(int type)
    {
        using var fixture = new StyleFixture(type, "");
        using var payload = fixture.Read(fixture.Source.Text.Length, 0);
        Assert.Empty(payload.RootElement.GetProperty("text16").GetBytesFromBase64()); Assert.Empty(payload.RootElement.GetProperty("runs").EnumerateArray());
        var request = fixture.Request(MacOSAXOperation.TextStyleRange, fixture.Source.Text.Length, 0);
        Assert.True(fixture.Tree.Handle(ref request)); Assert.Equal((0, 0), (request.TextStart, request.TextLength));
        Assert.Equal(0, fixture.Source.SelectionStart); Assert.Equal(0, fixture.Source.SelectionLength);
    }

    [Fact]
    public void SyntaxFormattingUsesTheRendererBrushResolverAndRevalidatesOwner()
    {
        using var fixture = new StyleFixture(2, "plain kw🙂\nother");
        var editor = (EditControl)fixture.Editor; editor.SyntaxHighlighter = new StyleHighlighter();
        var view = (EditorView)typeof(EditControl).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        view.ClassificationBrushResolver = (classification, brush) => classification == TokenClassification.Keyword ? new SolidColorBrush(Color.FromRgb(1, 2, 3)) : brush;
        var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        Assert.Equal(0x030201, provider.DocumentRange.FindText("kw🙂", false, false)!.GetAttributeValue(40008));
        Assert.Equal(0, fixture.Source.SelectionLength);
        view.ClassificationBrushResolver = (_, brush) => { fixture.Parent.Visibility = Visibility.Hidden; return brush; };
        Assert.False(fixture.TryRead(0, 1));
        Assert.Equal("plain kw🙂\nother", editor.Text);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void EditorDrawnTypographyMatchesItsTextAttributes(bool highlighted)
    {
        using var fixture = new StyleFixture(2, "plain kw🙂");
        var editor = (EditControl)fixture.Editor; editor.FontWeight = FontWeights.Bold; editor.FontStyle = FontStyles.Italic;
        if (highlighted) editor.SyntaxHighlighter = new StyleHighlighter();
        var recorder = new DrawingRecorder(); recorder.Bind(new object()); editor.Render(recorder);
        var drawing = recorder.Commit();
        var text = drawing.Commands.Where(command => command.A is FormattedText value && value.Text.Contains(highlighted ? "kw" : "plain", StringComparison.Ordinal))
            .Select(command => (FormattedText)command.A!).ToArray();
        Assert.NotEmpty(text); Assert.All(text, value => { Assert.Equal(700, value.FontWeight); Assert.Equal(2, value.FontStyle); });
        var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        Assert.Equal(700, provider.DocumentRange.GetAttributeValue(40007)); Assert.Equal(true, provider.DocumentRange.GetAttributeValue(40014));
    }

    [Fact]
    public void FormattingAppendsToTheUnchangedAccessibilityAbi()
    {
        Assert.Equal(136, Marshal.SizeOf<MacOSAXRequest>()); Assert.Equal(12, (int)MacOSAXOperation.TextNavigation);
        Assert.Equal(13, (int)MacOSAXOperation.TextStyles); Assert.Equal(14, (int)MacOSAXOperation.TextStyleRange);
        Assert.Equal(1u << 23, (uint)MacOSAXFlags.Offscreen); Assert.Equal(1u << 24, (uint)MacOSAXFlags.StyledText);
    }

    [Fact]
    public void AttributeSearchMergesSameAttributeAcrossDifferentFormatsAndCropsToTheRange()
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("first") { FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(1, 2, 3)) });
        paragraph.Inlines.Add(new Bold(new Run("second") { FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(1, 2, 3)) }));
        paragraph.Inlines.Add(new Italic(new Underline(new Run("third") { Foreground = new SolidColorBrush(Color.FromRgb(4, 5, 6)) })
            { Foreground = new SolidColorBrush(Color.FromRgb(10, 20, 30)) }));
        var document = new FlowDocument { FontSize = 18 }; document.Blocks.Add(paragraph);
        var provider = (ITextProvider)new RichTextBox(document).GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        Assert.Equal("firstsecond", provider.DocumentRange.FindAttribute(40006, 25d, false)!.GetText(-1));
        var cropped = provider.DocumentRange.FindText("rstsec", false, false)!;
        Assert.Equal("rstsec", cropped.FindAttribute(40008, 0x030201, true)!.GetText(-1));
        var decorated = provider.DocumentRange.FindText("third", false, false)!;
        Assert.Equal(true, decorated.GetAttributeValue(40014)); Assert.Equal(1, decorated.GetAttributeValue(40030));
        Assert.Equal(0x1e140a, decorated.GetAttributeValue(40029));
        Assert.Same(AutomationTextAttributeValues.Mixed, provider.DocumentRange.GetAttributeValue(40007));
    }

    private sealed class StyleHighlighter : ISyntaxHighlighter
    {
        public object? GetInitialState() => null;
        public (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(int number, string text, object? state) =>
            (text.Length > 6 ? [new SyntaxToken(0, 6, TokenClassification.PlainText), new SyntaxToken(6, text.Length - 6, TokenClassification.Keyword)]
                : [new SyntaxToken(0, text.Length, TokenClassification.PlainText)], null);
    }

    private sealed class StyleFixture : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly Border Parent;
        internal readonly Control Editor;
        internal readonly MacOSAccessibilityTree Tree;
        internal readonly IAutomationTextProviderSource Source;
        private readonly ulong _id;
        internal StyleFixture(int type, string text)
        {
            Editor = type switch { 0 => new TextBox { Text = text }, 1 => new RichTextBox(FlowDocument.FromText(text)), _ => new EditControl { Text = text, ShowLineNumbers = false } };
            Editor.FontFamily = new FontFamily("Arial"); Editor.FontSize = 21; Editor.Height = 100;
            if (Editor is RichTextBox rich) { rich.Document.FontFamily = Editor.FontFamily; rich.Document.FontSize = 21; }
            Parent = new Border { Child = Editor };
            Window = new DisplayedTestWindow { Width = 360, Height = 180, TitleBarStyle = WindowTitleBarStyle.Native, Content = Parent };
            Window.Measure(new Size(360, 180)); Window.Arrange(new Rect(0, 0, 360, 180));
            Tree = new(Window); Assert.True(Tree.TryGetId(Editor.GetAutomationPeer()!, out _id)); Source = (IAutomationTextProviderSource)Editor.GetAutomationPeer()!;
        }
        internal MacOSAXRequest Request(MacOSAXOperation operation, int start, int length) => new() { NodeId = _id, Operation = operation, TextStart = start, TextLength = length };
        internal bool TryRead(int start, int length) { var request = Request(MacOSAXOperation.TextStyles, start, length); return Tree.Handle(ref request); }
        internal JsonDocument Read(int start, int length)
        {
            var request = Request(MacOSAXOperation.TextStyles, start, length); Assert.True(Tree.Handle(ref request));
            var buffer = new char[request.TextCount]; fixed (char* characters = buffer)
            { request.Text = characters; request.TextCapacity = buffer.Length; Assert.True(Tree.Handle(ref request)); }
            return JsonDocument.Parse(new string(buffer));
        }
        public void Dispose() { Window.Close(); }
    }
}
