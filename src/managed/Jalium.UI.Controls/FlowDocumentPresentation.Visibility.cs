using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private readonly Dictionary<Table, CssTableVisibility> _tableVisibility = [];
    private readonly Dictionary<Table, double> _tableWidthReduction = [];
    private readonly HashSet<int> _clippedTableLines = [];

    private CssTableVisibility TableVisibility(Table table, TableRow[] rows, int columns)
    {
        var result = new CssTableVisibility(rows.Length, columns);
        for (var row = 0; row < rows.Length; row++)
            result.Rows[row] = CssTableVisibility.Collapsed(rows[row]) || BoxRowGroup(rows[row]) is { } group && !_documentAnonymousTableBoxes.Contains(group) && CssTableVisibility.Collapsed(group);
        for (var column = 0; column < DisplayColumns(table).Length; column++)
            result.Columns[column] = CssTableVisibility.Collapsed(DisplayColumns(table)[column]);
        _tableVisibility[table] = result; return result;
    }

    private static Rect MergeClip(Rect? prior, Rect next)
    {
        var result = prior is { } clip ? Rect.Intersect(clip, next) : next;
        return result.IsEmpty ? new Rect(next.X, next.Y, 0, 0) : result;
    }

    private void MoveCellContent(CellContent content, double dx, double dy, Rect? clip, bool removed)
    {
        for (var i = content.FirstParagraph; i < content.EndParagraph; i++)
        {
            var view = _paragraphs[i]; view.Bounds = Shift(view.Bounds, dx, dy);
            if (view.TableClip is { } previous) view.TableClip = Shift(previous, dx, dy);
            if (view.ColumnClips is { } pieces) view.ColumnClips = pieces.Select(piece => Shift(piece, dx, dy)).ToArray();
            if (clip is { } bounds) view.TableClip = MergeClip(view.TableClip, bounds);
            view.TableSuppressed |= removed;
        }
        for (var i = content.FirstLine; i < content.EndLine; i++)
        {
            var line = _lines[i]; var top = line.Top + dy; var bottom = top + line.Height;
            if (clip is { } bounds) { top = Math.Max(top, bounds.Top); bottom = Math.Min(bottom, bounds.Bottom); }
            var height = removed ? 0 : Math.Max(0, bottom - top);
            if (removed || line.Height > 0 && height == 0) _clippedTableLines.Add(i);
            _lines[i] = line with { Top = top, Height = height, Left = line.Left + dx };
        }
        for (var i = content.FirstPaint + 3 + content.Track.ColumnSpan; i < content.EndPaint; i++)
        {
            var block = _blocks[i]; var previous = block.Clip is { } old ? Shift(old, dx, dy) : (Rect?)null;
            _blocks[i] = block with { Bounds = Shift(block.Bounds, dx, dy), Clip = clip is { } bounds ? MergeClip(previous, bounds) : previous,
                PaintBounds = block.PaintBounds is { } pb ? Shift(pb, dx, dy) : null };
        }
        for (var i = content.FirstCollapsedFrame; i < content.EndCollapsedFrame; i++)
        {
            var frame = _collapsedTableFrames[i]; var previous = frame.Clip is { } old ? Shift(old, dx, dy) : (Rect?)null;
            _collapsedTableFrames[i] = frame with { Origin = new(frame.Origin.X + dx, frame.Origin.Y + dy), Clip = clip is { } bounds ? MergeClip(previous, bounds) : previous,
                Projection = frame.Geometry is null ? frame.Projection : new(frame.Projection.X + dx, frame.Projection.Y + dy) };
        }
        for (var i = 0; i < _repeatableTables.Count; i++)
        {
            var rt = _repeatableTables[i];
            if (!Within(rt.Table, content.Track.Source)) continue;
            _repeatableTables[i] = rt with
            {
                TableBounds = Shift(rt.TableBounds, dx, dy),
                HeaderBounds = rt.Header is not null ? Shift(rt.HeaderBounds, dx, dy) : default,
                FooterBounds = rt.Footer is not null ? Shift(rt.FooterBounds, dx, dy) : default,
                BodyTop = rt.BodyTop + dy,
                BodyBottom = rt.BodyBottom + dy,
            };
        }
        MoveNestedColumns(content.Track.Source, dx, dy, clip, removed);
    }

    private void RemoveCollapsedLines()
    {
        if (_clippedTableLines.Count == 0) return;
        var prefix = new int[_lines.Count + 1];
        for (var i = 0; i < _lines.Count; i++) prefix[i + 1] = prefix[i] + (_clippedTableLines.Contains(i) ? 0 : 1);
        for (var i = 0; i < _fragmentBoxes.Count; i++)
        {
            var box = _fragmentBoxes[i];
            _fragmentBoxes[i] = box with { FirstLine = prefix[box.FirstLine], EndLine = prefix[box.EndLine] };
        }
        var visible = _lines.Where((_, index) => !_clippedTableLines.Contains(index)).ToArray();
        _lines.Clear(); _lines.AddRange(visible); _clippedTableLines.Clear();
    }

    internal override Geometry? GetAdditionalChildLayoutClip(Visual child)
    {
        if (_document is null) return null;
        var view = _paragraphs.FirstOrDefault(view => ReferenceEquals(view.Text, child));
        if (view is null) return null;
        var p = EffectivePagePadding;
        if (view.IsPageFixed) return null;
        var dy = MapDocumentYToPageOffset(view.Source, out var bandClip);
        if (view.ColumnClips is { } clips)
            return ColumnClip(clips.Select(clip =>
            {
                var shifted = Shift(clip, p.Left + view.VisualOffset.X, dy + view.VisualOffset.Y);
                return bandClip is { } bc ? Rect.Intersect(shifted, bc) : shifted;
            }).Where(r => !r.IsEmpty && r.Width > 0 && r.Height > 0));
        if (view.TableClip is { } clip)
        {
            var cellRect = new Rect(clip.X + p.Left + view.VisualOffset.X, clip.Y + dy + view.VisualOffset.Y, clip.Width, clip.Height);
            if (bandClip is { } bc) cellRect = Rect.Intersect(cellRect, bc);
            return new RectangleGeometry(cellRect.IsEmpty ? new Rect(0, 0, 0, 0) : cellRect);
        }
        return bandClip is { } band ? new RectangleGeometry(band) : null;
    }
}
