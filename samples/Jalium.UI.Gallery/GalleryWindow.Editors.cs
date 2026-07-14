using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Document and code-editing controls: the syntax-highlighting code editor, the
/// rich-text (FlowDocument) editor, and document viewers.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildEditorsSection() => Section(
        "Documents & Editors",
        "Code editing with syntax highlighting, rich-text editing and document viewing.",
        Card("EditControl", EditControlDemo(), width: 480),
        Card("RichTextBox", RichTextBoxDemo(), width: 380),
        Card("DocumentViewer", Placeholder("DocumentViewer", "Paginated document viewing with zoom, page navigation and search.")),
        Card("FlowDocumentScrollViewer", Placeholder("FlowDocumentScrollViewer", "Continuously scrolling flow-document host.")));

    private static UIElement EditControlDemo() => new EditControl
    {
        Text = "public static int Fib(int n)\n{\n    if (n < 2) return n;\n    return Fib(n - 1) + Fib(n - 2);\n}",
        Language = "csharp",
        ShowLineNumbers = true,
        Width = 452,
        Height = 170,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static UIElement RichTextBoxDemo()
    {
        var editor = new RichTextBox(FlowDocument.FromText(
            "RichTextBox edits a FlowDocument model: runs, paragraphs, lists, inline formatting and embedded UI — grapheme-cluster aware for correct caret and selection."))
        {
            Width = 352,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return editor;
    }
}
