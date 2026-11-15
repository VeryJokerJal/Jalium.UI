using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildEditorsSection() => new GallerySampleEditorsSection();
}

/// <summary>Documents &amp; Editors examples with replaceable default templates.</summary>
[TemplatePart(Name = "PART_CodeEditor", Type = typeof(EditControl))]
public class GallerySampleEditorsSection : GallerySampleHeader
{
    public GallerySampleEditorsSection() => DefaultStyleKey = typeof(GallerySampleEditorsSection);
}

/// <summary>Retains the caller's editable document when the view template changes.</summary>
[TemplatePart(Name = "PART_Editor", Type = typeof(RichTextBox))]
public class GallerySampleRichTextDemo : GallerySampleDemo
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(nameof(Document), typeof(FlowDocument), typeof(GallerySampleRichTextDemo), new PropertyMetadata(null, (d, _) => ((GallerySampleRichTextDemo)d).UpdateDocument()));
    private RichTextBox? _editor;
    public FlowDocument? Document { get => (FlowDocument?)GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public GallerySampleRichTextDemo()
    {
        DefaultStyleKey = typeof(GallerySampleRichTextDemo);
        SetCurrentValue(DocumentProperty, FlowDocument.FromText("RichTextBox edits a FlowDocument model: runs, paragraphs, lists, inline formatting and embedded UI — grapheme-cluster aware for correct caret and selection."));
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); _editor = GetTemplateChild("PART_Editor") as RichTextBox; UpdateDocument();
    }
    protected override void OnTemplateChanging()
    {
        // Disconnect the retired editor's document subscription before replacement.
        if (_editor is not null) _editor.Document = new FlowDocument();
        _editor = null; base.OnTemplateChanging();
    }
    private void UpdateDocument() { if (_editor is not null) _editor.Document = Document ?? new FlowDocument(); }
}
