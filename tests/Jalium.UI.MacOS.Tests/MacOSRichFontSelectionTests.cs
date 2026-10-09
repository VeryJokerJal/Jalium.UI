using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRichFontSelectionTests : MacOSGeometryTestBase
{
    [Theory]
    [InlineData(0, 0)] [InlineData(0, 2)]
    [InlineData(1, 0)] [InlineData(1, 2)]
    [InlineData(2, 0)] [InlineData(2, 2)]
    [InlineData(3, 0)] [InlineData(3, 2)]
    public void UnchangedTypographyWriteThenSelectionSurvivesFormattingUndoRedo(int property, int length)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var document = FlowDocument.FromText("Miii");
        document.FontStyle = FontStyles.Italic;
        var editor = new RichTextBox(document);
        // Pin the current effective value locally, as a font settings panel
        // does even when that particular field has not changed.
        switch (property)
        {
            case 0: document.FontStretch = document.FontStretch; break;
            case 1: document.FontWeight = document.FontWeight; break;
            case 2: document.FontSize = document.FontSize; break;
            case 3: document.FontFamily = document.FontFamily; break;
        }
        Assert.True(((IImeSupport)editor).TrySetImeSelection(1, length));
        document.FontStyle = FontStyles.Normal;
        CheckSelection();
        Assert.True(editor.Undo());
        Assert.Equal(FontStyles.Italic, document.FontStyle);
        CheckSelection();
        Assert.True(editor.Redo());
        Assert.Equal(FontStyles.Normal, document.FontStyle);
        CheckSelection();

        void CheckSelection()
        {
            Assert.Equal("Miii" + Environment.NewLine, document.GetText());
            Assert.Equal(1, editor.Selection.Start.DocumentOffset);
            Assert.Equal(1 + length, editor.Selection.End.DocumentOffset);
            Assert.Equal(length == 0 ? "" : "ii", editor.Selection.Text);
            Assert.Same(document, editor.Document);
        }
    }
}
