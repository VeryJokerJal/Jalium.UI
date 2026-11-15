using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>One conflict map for both visual and document table adapters. Coordinates are logical tracks.</summary>
internal sealed class CssCollapsedTableBorders
{
    internal enum Kind { Table, ColumnGroup, Column, RowGroup, Row, Cell }
    private sealed record Edge(double Width, CssBorderLineStyle Style, Brush Brush, Kind Kind, int Row, int Column, DependencyObject Source)
    {
        internal double UsedWidth => CssBorderStyles.IsHidden(Style) ? 0 : Width;
    }
    private readonly int _rows, _columns;
    private readonly bool _rtl;
    private DependencyObject? _table;
    internal bool HasGrid => _rows > 0 && _columns > 0;
    private readonly Dictionary<(int Line, int Segment), Edge> _vertical = [], _horizontal = [];
    private readonly Dictionary<int, List<(int Start, int End)>> _blockedVertical = [], _blockedHorizontal = [];
    private sealed record Part(DependencyObject Source, Kind Kind, int Row, int Column, int Rows, int Columns);
    private readonly List<Part> _parts = [];
    private (CssTableVisibility Visibility, CssCollapsedTableBorders Borders)? _projection;

    internal CssCollapsedTableBorders(int rows, int columns, bool rtl, IReadOnlyList<CssTableTracks.Cell> cells)
    {
        _rows = rows; _columns = columns; _rtl = rtl;
        foreach (var cell in cells)
        {
            for (var line = cell.Column + 1; line < cell.Column + cell.ColumnSpan; line++)
                Block(_blockedVertical, line, cell.Row, cell.Row + cell.RowSpan);
            for (var line = cell.Row + 1; line < cell.Row + cell.RowSpan; line++)
                Block(_blockedHorizontal, line, cell.Column, cell.Column + cell.ColumnSpan);
        }
    }

    private static void Block(Dictionary<int, List<(int Start, int End)>> map, int line, int start, int end)
    {
        if (!map.TryGetValue(line, out var intervals)) map[line] = intervals = [];
        intervals.Add((start, end));
    }

    internal static Thickness SpecifiedBorder(DependencyObject source) =>
        CssDependencyPropertyLookup.Find(source.GetType(), "BorderThickness") is { } property && source.GetValue(property) is Thickness native
            ? native : source.GetValue(CssBorderUsedThicknessProperties.ValueProperty) is Thickness css ? css : default;

    internal void Add(DependencyObject source, Kind kind, int row, int column, int rows, int columns)
    {
        _projection = null;
        if (kind == Kind.Table) _table = source;
        _parts.Add(new(source, kind, row, column, rows, columns));
        if (_rows == 0 || _columns == 0 || rows == 0 || columns == 0) return;
        var thickness = SpecifiedBorder(source);
        var colors = CssBorderPaintProperties.Get(source);
        var brush = CssDependencyPropertyLookup.Find(source.GetType(), "BorderBrush") is { } property ? source.GetValue(property) as Brush : null;
        var styles = CssBorderStyleProperties.Get(source);
        Edge At(int side)
        {
            var width = side switch { 0 => thickness.Left, 1 => thickness.Top, 2 => thickness.Right, _ => thickness.Bottom };
            var style = styles?.Edge(side) ?? (brush is not null && width > 0 ? CssBorderLineStyle.Solid : CssBorderLineStyle.None);
            return new(double.IsFinite(width) ? Math.Max(0, width) : 0, style, colors?.Edge(side) ?? brush ?? Brushes.Black, kind, row, column, source);
        }
        var left = At(_rtl ? 2 : 0); var right = At(_rtl ? 0 : 2); var top = At(1); var bottom = At(3);
        for (var r = row; r < Math.Min(_rows, row + rows); r++)
        {
            Offer(_vertical, _blockedVertical, column, r, left);
            Offer(_vertical, _blockedVertical, Math.Min(_columns, column + columns), r, right);
        }
        for (var c = column; c < Math.Min(_columns, column + columns); c++)
        {
            Offer(_horizontal, _blockedHorizontal, row, c, top);
            Offer(_horizontal, _blockedHorizontal, Math.Min(_rows, row + rows), c, bottom);
        }
    }

    private static void Offer(Dictionary<(int, int), Edge> map, Dictionary<int, List<(int Start, int End)>> blocked, int line, int segment, Edge candidate)
    {
        if (blocked.TryGetValue(line, out var intervals) && intervals.Any(range => segment >= range.Start && segment < range.End)) return;
        var key = (line, segment);
        if (!map.TryGetValue(key, out var previous) || Compare(candidate, previous) > 0) map[key] = candidate;
    }

    private static int StyleRank(CssBorderLineStyle style) => style switch
    {
        CssBorderLineStyle.Double => 8, CssBorderLineStyle.Solid => 7, CssBorderLineStyle.Dashed => 6, CssBorderLineStyle.Dotted => 5,
        CssBorderLineStyle.Ridge => 4, CssBorderLineStyle.Outset => 3, CssBorderLineStyle.Groove => 2, CssBorderLineStyle.Inset => 1, _ => 0,
    };

    internal CssCollapsedTableBorders Project(CssTableVisibility visibility)
    {
        if (!visibility.Any) return this;
        if (_projection is { } cached && ReferenceEquals(cached.Visibility, visibility)) return cached.Borders;
        static int[] Lines(bool[] hidden)
        {
            var result = new int[hidden.Length + 1];
            for (var i = 0; i < hidden.Length; i++) result[i + 1] = result[i] + (hidden[i] ? 0 : 1);
            return result;
        }
        var rows = Lines(visibility.Rows); var columns = Lines(visibility.Columns);
        var parts = _parts.Select(part => part with { Row = rows[part.Row], Column = columns[part.Column],
            Rows = rows[Math.Min(_rows, part.Row + part.Rows)] - rows[part.Row],
            Columns = columns[Math.Min(_columns, part.Column + part.Columns)] - columns[part.Column] }).ToArray();
        var cells = parts.Where(part => part.Kind == Kind.Cell && part.Rows > 0 && part.Columns > 0)
            .Select(part => new CssTableTracks.Cell(part.Source, part.Row, part.Column, part.Rows, part.Columns)).ToArray();
        var projected = new CssCollapsedTableBorders(rows[^1], columns[^1], _rtl, cells) { _table = _table };
        foreach (var part in parts) projected.Add(part.Source, part.Kind, part.Row, part.Column, part.Rows, part.Columns);
        _projection = (visibility, projected);
        return projected;
    }
    private static int Compare(Edge a, Edge b)
    {
        var hidden = (a.Style == CssBorderLineStyle.Hidden).CompareTo(b.Style == CssBorderLineStyle.Hidden);
        if (hidden != 0) return hidden;
        var visible = (a.Style != CssBorderLineStyle.None).CompareTo(b.Style != CssBorderLineStyle.None);
        if (visible != 0) return visible;
        var width = a.Width.CompareTo(b.Width); if (width != 0) return width;
        var style = StyleRank(a.Style).CompareTo(StyleRank(b.Style)); if (style != 0) return style;
        var kind = a.Kind.CompareTo(b.Kind); if (kind != 0) return kind;
        var column = b.Column.CompareTo(a.Column); return column != 0 ? column : b.Row.CompareTo(a.Row);
    }
    private double Vertical(int line, int start, int count) => Enumerable.Range(start, count)
        .Select(segment => _vertical.GetValueOrDefault((line, segment))?.UsedWidth ?? 0).DefaultIfEmpty().Max();
    private double Horizontal(int line, int start, int count) => Enumerable.Range(start, count)
        .Select(segment => _horizontal.GetValueOrDefault((line, segment))?.UsedWidth ?? 0).DefaultIfEmpty().Max();

    internal Thickness TableInsets
    {
        get
        {
            if (!HasGrid) return _table is null ? default : SpecifiedBorder(_table);
            var first = Vertical(0, 0, Math.Min(1, _rows)) / 2; var last = Vertical(_columns, 0, Math.Min(1, _rows)) / 2;
            return new(_rtl ? last : first, Horizontal(0, 0, _columns) / 2, _rtl ? first : last, Horizontal(_rows, 0, _columns) / 2);
        }
    }
    internal Thickness Overflow
    {
        get
        {
            var inset = TableInsets; var first = Vertical(0, 0, _rows) / 2; var last = Vertical(_columns, 0, _rows) / 2;
            return new(Math.Max(0, (_rtl ? last : first) - inset.Left), 0, Math.Max(0, (_rtl ? first : last) - inset.Right), 0);
        }
    }

    internal void DrawEmpty(DrawingContext context, Rect bounds, Border painter)
    {
        if (HasGrid || _table is null) return;
        var colors = CssBorderPaintProperties.Get(_table);
        if (colors is null && CssDependencyPropertyLookup.Find(_table.GetType(), "BorderBrush") is { } property && _table.GetValue(property) is Brush brush)
            colors = new(brush, brush, brush, brush);
        if (colors is not null && (_table.GetValue(CssBorderImageProperties.SourceProperty) is not CssBorderImageSource image || image.Resolve(painter, bounds.Size) is null))
            painter.DrawCssSideColorsFor(context, colors, CssBorderStyleProperties.Get(_table), bounds, TableInsets, default);
    }
    internal Thickness CellInsets(CssTableTracks.Cell cell)
    {
        if (cell.Source.GetValue(CssBorderImageProperties.SourceProperty) is not null)
        {
            var own = SpecifiedBorder(cell.Source); return new(own.Left / 2, own.Top / 2, own.Right / 2, own.Bottom / 2);
        }
        var first = Vertical(cell.Column, cell.Row, cell.RowSpan) / 2;
        var last = Vertical(cell.Column + cell.ColumnSpan, cell.Row, cell.RowSpan) / 2;
        return new(_rtl ? last : first, Horizontal(cell.Row, cell.Column, cell.ColumnSpan) / 2,
            _rtl ? first : last, Horizontal(cell.Row + cell.RowSpan, cell.Column, cell.ColumnSpan) / 2);
    }

    private sealed record Stroke(bool Horizontal, int Line, int Segment, Edge Edge, Rect Bounds);

    /// <summary>Reuse the shared border line painter; partition junctions so translucent winners are painted once.</summary>
    internal Rect Draw(DrawingContext context, CssTableTracks.Axis horizontal, CssTableTracks.Axis vertical, Point origin, Border painter)
    {
        if (_rows == 0 || _columns == 0) return Rect.Empty;
        double X(int line) => origin.X + (_rtl ? horizontal.Extent - (line == _columns ? horizontal.Extent : horizontal.Starts[line]) : line == _columns ? horizontal.Extent : horizontal.Starts[line]);
        double Y(int line) => origin.Y + (line == _rows ? vertical.Extent : vertical.Starts[line]);
        double V(int row, int column) => Math.Max(_vertical.GetValueOrDefault((column, row - 1))?.UsedWidth ?? 0, _vertical.GetValueOrDefault((column, row))?.UsedWidth ?? 0) / 2;
        double H(int row, int column) => Math.Max(_horizontal.GetValueOrDefault((row, column - 1))?.UsedWidth ?? 0, _horizontal.GetValueOrDefault((row, column))?.UsedWidth ?? 0) / 2;
        var strokes = new List<Stroke>();
        var vertices = new Dictionary<(int Row, int Column), List<Stroke>>();
        void Add(bool h, int line, int segment, Edge edge)
        {
            if (edge.UsedWidth <= 0 || edge.Kind is Kind.Cell or Kind.Table && edge.Source.GetValue(CssBorderImageProperties.SourceProperty) is CssBorderImageSource image &&
                image.Resolve(painter, new(horizontal.Extent, vertical.Extent)) is not null) return;
            var start = h ? (line, segment) : (segment, line); var end = h ? (line, segment + 1) : (segment + 1, line);
            Rect bounds;
            if (h)
            {
                var first = _rtl ? end : start; var last = _rtl ? start : end;
                var left = X(first.Item2) - V(first.Item1, first.Item2); var right = X(last.Item2) + V(last.Item1, last.Item2);
                bounds = new(left, Y(line) - edge.UsedWidth / 2, Math.Max(0, right - left), edge.UsedWidth);
            }
            else
            {
                var top = Y(start.Item1) - H(start.Item1, start.Item2); var bottom = Y(end.Item1) + H(end.Item1, end.Item2);
                bounds = new(X(line) - edge.UsedWidth / 2, top, edge.UsedWidth, Math.Max(0, bottom - top));
            }
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var stroke = new Stroke(h, line, segment, edge, bounds); strokes.Add(stroke);
            foreach (var key in new[] { start, end })
            {
                if (!vertices.TryGetValue(key, out var incident)) vertices[key] = incident = [];
                incident.Add(stroke);
            }
        }
        foreach (var (key, edge) in _horizontal) Add(true, key.Line, key.Segment, edge);
        foreach (var (key, edge) in _vertical) Add(false, key.Line, key.Segment, edge);
        bool Wins(Stroke a, Stroke b)
        {
            var conflict = Compare(a.Edge, b.Edge); if (conflict != 0) return conflict > 0;
            if (a.Horizontal != b.Horizontal) return a.Horizontal;
            return a.Line != b.Line ? a.Line < b.Line : a.Segment < b.Segment;
        }
        static bool SameEdge(Edge a, Edge b) => ReferenceEquals(a.Source, b.Source) &&
            a.Style == b.Style && a.Width == b.Width && Equals(a.Brush, b.Brush);
        var ink = Rect.Empty;
        foreach (var stroke in strokes)
        {
            var start = stroke.Horizontal ? (stroke.Line, stroke.Segment) : (stroke.Segment, stroke.Line);
            var end = stroke.Horizontal ? (stroke.Line, stroke.Segment + 1) : (stroke.Segment + 1, stroke.Line);
            var pieces = new List<Rect> { stroke.Bounds };
            foreach (var other in vertices[start].Concat(vertices[end]).Distinct())
                if (!ReferenceEquals(stroke, other) && Wins(other, stroke)) pieces = pieces.SelectMany(piece => Subtract(piece, other.Bounds)).ToList();
            if (pieces.Count == 0) continue;
            var map = stroke.Horizontal ? _horizontal : _vertical;
            var firstSeg = stroke.Segment;
            while (firstSeg > 0 && map.TryGetValue((stroke.Line, firstSeg - 1), out var prev) && SameEdge(prev, stroke.Edge)) firstSeg--;
            var lastSeg = stroke.Segment;
            while (map.TryGetValue((stroke.Line, lastSeg + 1), out var next) && SameEdge(next, stroke.Edge)) lastSeg++;
            var spanBounds = stroke.Bounds;
            if (firstSeg < lastSeg)
                foreach (var peer in strokes.Where(other => other.Horizontal == stroke.Horizontal && other.Line == stroke.Line && other.Segment >= firstSeg && other.Segment <= lastSeg))
                    spanBounds = Rect.Union(spanBounds, peer.Bounds);
            var clip = new PathGeometry();
            foreach (var piece in pieces) clip.Figures.Add(CssRoundedRectangleGeometry.Figure(piece, default));
            clip.Freeze(); context.PushClip(clip);
            try
            {
                var style = stroke.Edge.Style switch { CssBorderLineStyle.Inset => CssBorderLineStyle.Ridge, CssBorderLineStyle.Outset => CssBorderLineStyle.Groove, _ => stroke.Edge.Style };
                var colors = new CssBorderPaint(stroke.Edge.Brush, stroke.Edge.Brush, stroke.Edge.Brush, stroke.Edge.Brush);
                var styles = stroke.Horizontal ? new CssBorderStyles(CssBorderLineStyle.None, style, CssBorderLineStyle.None, CssBorderLineStyle.None) : new CssBorderStyles(style, CssBorderLineStyle.None, CssBorderLineStyle.None, CssBorderLineStyle.None);
                var border = stroke.Horizontal ? new Thickness(0, stroke.Edge.UsedWidth, 0, 0) : new Thickness(stroke.Edge.UsedWidth, 0, 0, 0);
                painter.DrawCssSideColorsFor(context, colors, styles, spanBounds, border, default);
            }
            finally { context.Pop(); }
            ink = ink.IsEmpty ? stroke.Bounds : Rect.Union(ink, stroke.Bounds);
        }
        return ink;
    }

    private static IEnumerable<Rect> Subtract(Rect box, Rect cut)
    {
        var overlap = Rect.Intersect(box, cut);
        if (overlap.IsEmpty || overlap.Width <= 0 || overlap.Height <= 0) { yield return box; yield break; }
        if (overlap.Top > box.Top) yield return new(box.Left, box.Top, box.Width, overlap.Top - box.Top);
        if (overlap.Bottom < box.Bottom) yield return new(box.Left, overlap.Bottom, box.Width, box.Bottom - overlap.Bottom);
        if (overlap.Left > box.Left) yield return new(box.Left, overlap.Top, overlap.Left - box.Left, overlap.Height);
        if (overlap.Right < box.Right) yield return new(overlap.Right, overlap.Top, box.Right - overlap.Right, overlap.Height);
    }

    internal static bool DocumentPart(DependencyObject source)
    {
        if (source is not (Table or TableCell or TableRow or TableRowGroup or TableColumn)) return false;
        for (var current = source as FrameworkContentElement; current is not null; current = current.Parent as FrameworkContentElement)
            if (current is Table table) return CssTableProperties.Collapsed(table);
        return false;
    }
}
