using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private static IEnumerable<DependencyObject> DisplayChildren(DependencyObject source) => source switch
    {
        FlowDocument doc => doc.Blocks.Cast<DependencyObject>(),
        Paragraph paragraph => paragraph.Inlines.Cast<DependencyObject>(),
        Span span => span.Inlines.Cast<DependencyObject>(),
        Section section => section.Blocks.Cast<DependencyObject>(),
        DocList list => list.ListItems.Cast<DependencyObject>(),
        ListItem item => item.Blocks.Cast<DependencyObject>(),
        Table table => table.RowGroups.Cast<DependencyObject>(),
        TableRowGroup group => group.Rows.Cast<DependencyObject>(),
        TableRow row => row.Cells.Cast<DependencyObject>(),
        TableCell cell => cell.Blocks.Cast<DependencyObject>(),
        _ => [],
    };
    private static IEnumerable<Paragraph> DisplayParagraphs(DependencyObject source)
    {
        if (source is Paragraph paragraph) yield return paragraph;
        else foreach (var child in DisplayChildren(source)) foreach (var found in DisplayParagraphs(child)) yield return found;
    }
    private bool SkipDisplayNone(DependencyObject source, ref int index, double y)
    {
        if (!CssDisplayProperties.SuppressesBoxTree(source) && !_documentTableWhitespace.Contains(source)) return false;
        foreach (var paragraph in DisplayParagraphs(source))
        {
            var view = _paragraphs[index++];
            view.Text.DocumentDisplaySuppressed = true; view.Text.Visibility = Visibility.Collapsed;
            view.Bounds = new(0, y, 0, 0);
        }
        return true;
    }
    private void ResetDisplaySuppression()
    {
        foreach (var view in _paragraphs)
        {
            if (!view.Text.DocumentDisplaySuppressed) continue;
            view.Text.DocumentDisplaySuppressed = false;
            view.Text.InvalidateCssTextLayout(); view.Text.InvalidateMeasure();
        }
    }
    private void LayoutWithoutDocumentBoxes()
    {
        ClearDocumentColumns(); ClearDocumentContentsRuns(); ClearDocumentClamping();
        _lines.Clear(); _positionLines.Clear(); _blocks.Clear(); _fragmentBoxes.Clear();
        _tableCells.Clear(); _tableGeometry.Clear(); _tableGeometryKeys.Clear(); ClearCollapsedTables();
        _heightSpaces.Clear(); _hasDocumentHeights = _hasNestedColumns = false;
        _extentWidth = _extentHeight = 0;
        var index = 0; SkipDisplayNone(_document!, ref index, 0);
    }
    private TableColumn[] DisplayColumns(Table table) => DocumentTableModel(table).Columns.ToArray();

    // Source APIs retain the original document. Text providers expose the text
    // which has boxes, and map selection, hits and geometry back to that source.
    private sealed class DocumentExposure
    {
        private readonly record struct Piece(int Source, int Exposed, int Length);
        private readonly List<Piece> _pieces = [];
        internal readonly string Text;
        internal DocumentExposure(string source, IReadOnlyList<(int Start, int End)> removed)
        {
            var builder = new StringBuilder(); var at = 0;
            foreach (var range in removed.OrderBy(range => range.Start))
            {
                var start = Math.Clamp(range.Start, at, source.Length); var end = Math.Clamp(range.End, start, source.Length);
                Keep(start); at = end;
            }
            Keep(source.Length); Text = builder.ToString();
            void Keep(int end)
            {
                if (end <= at) return;
                _pieces.Add(new(at, builder.Length, end-at)); builder.Append(source, at, end-at);
            }
        }
        internal int FromSource(int offset)
        {
            foreach (var piece in _pieces)
                if (offset <= piece.Source+piece.Length) return piece.Exposed+Math.Clamp(offset-piece.Source,0,piece.Length);
            return Text.Length;
        }
        internal int ToSource(int offset, bool forward=true)
        {
            offset=Math.Clamp(offset,0,Text.Length);
            foreach(var piece in _pieces)
                if(offset < piece.Exposed+piece.Length || !forward && offset==piece.Exposed+piece.Length)
                    return piece.Source+Math.Clamp(offset-piece.Exposed,0,piece.Length);
            return _pieces.Count==0?0:_pieces[^1].Source+_pieces[^1].Length;
        }
        internal IEnumerable<(int Start,int Length)> Ranges(int start,int length)
        {
            if(length==0){if(Text.Length>0)yield return(ToSource(start),0);yield break;}
            foreach(var piece in _pieces)
            {
                var from=Math.Max(start,piece.Exposed);var to=Math.Min((long)start+length,piece.Exposed+piece.Length);
                if(to>from)yield return(piece.Source+from-piece.Exposed,(int)(to-from));
            }
        }
    }
    private DocumentExposure _documentExposure = new("", []);
    private DocumentExposure Exposure { get { EnsureTextGeometry(); return _documentExposure; } }
    private void RebuildDocumentExposure()
    {
        if (_document is null) { _documentExposure = new("", []); return; }
        var removed = new List<(int Start,int End)>(); var cursor=0;
        void Walk(DependencyObject source)
        {
            if(CssDisplayProperties.SuppressesBoxTree(source) ||
                CssContainmentProperties.HidesContentsFromUserAgent(source) || _documentTableWhitespace.Contains(source))
            {
                var length = source is FlowDocument doc ? doc.GetText().Length : source is TextElement text
                    ? TextElement.GetContentLength(text)+(source is Paragraph or TableCell or TableRow ? 1:0) : 0;
                removed.Add((cursor,cursor+length));cursor+=length;return;
            }
            if(source is Run run){cursor+=run.Text.Length;return;}
            if(source is LineBreak or InlineUIContainer){cursor++;return;}
            foreach(var child in DisplayChildren(source))Walk(child);
            if(source is Paragraph or TableCell or TableRow)
            {
                if(DocumentContents(source) || DocumentInlineSeparator(source))removed.Add((cursor,cursor+1));
                cursor++;
            }
        }
        Walk(_document); _documentExposure=new(_document.GetText(),removed);
    }
    private IReadOnlyList<Rect> ExposedTextBounds(int start,int length)
        => Exposure.Ranges(start,length).SelectMany(range=>TextBounds(range.Start,range.Length)).ToArray();
}
