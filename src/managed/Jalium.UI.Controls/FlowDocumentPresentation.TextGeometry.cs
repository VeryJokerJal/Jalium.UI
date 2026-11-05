using Jalium.UI.Automation;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private void EnsureTextGeometry() => EnsureViewerLayout(_pageSize?.Width ?? (RenderSize.Width > 0 ? RenderSize.Width : _viewportWidth));

    internal int OffsetFromPoint(Point point) => TryOffsetFromPoint(point, out var offset) ? offset : 0;

    private bool TryOffsetFromPoint(Point point, out int offset)
    {
        offset = 0;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || _pageBlank) return false;
        EnsureTextGeometry();
        if (_document is null || _paragraphs.Count == 0) return false;
        if (_documentCssRoot is not null) return TryOffsetFromDocumentCssPoint(point, out offset);
        var p = EffectivePagePadding;
        var page = GetChildLayoutClip()?.Bounds;
        ParagraphView? closest = null; var distance = double.PositiveInfinity; var hit = default(Point);
        foreach (var view in _paragraphs)
        {
            if (!view.Text.IsVisible) continue;
            Rect? itemBandClip = null;
            var dy = view.IsPageFixed ? p.Top : MapDocumentYToPageOffset(view.Source, out itemBandClip);
            var dx = p.Left + view.VisualOffset.X;
            var totalDy = dy + view.VisualOffset.Y;
            var rectangles = view.Text.DocumentTextBounds(0, view.Text.Text.Length).Select(rect => Shift(rect, view.Bounds.X, view.Bounds.Y));
            if (view.ContentsFlow && view.Text.DocumentContentsMarkerBounds is { } marker)
                rectangles = rectangles.Append(Shift(marker, view.Bounds.X, view.Bounds.Y));
            if (!HasColumns && !view.ContentsFlow && view.Bounds.Width > 0) rectangles = rectangles.Append(view.Bounds);
            var boxes = rectangles.SelectMany(rect => view.ColumnClips is { } pieces ? ClipPieces(rect, pieces)
                : view.TableClip is { } clip ? [Rect.Intersect(rect, clip)] : new[] { rect });
            foreach (var candidate in boxes)
            {
                var pageCandidate = Shift(candidate, dx, totalDy);
                var bounds = page is { } clip ? Rect.Intersect(pageCandidate, clip) : pageCandidate;
                if (itemBandClip is { } bc && !bounds.IsEmpty) bounds = Rect.Intersect(bounds, bc);
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
                var next = DistanceToBox(point, bounds);
                if (next >= distance) continue;
                closest = view; distance = next;
                // A captured pointer may be outside the page or a clipped cell.
                // Hit the nearest visible part of that paragraph, rather than an
                // off-page line in the same original paragraph.
                var clampedPageX = Math.Clamp(point.X, bounds.Left, Math.Max(bounds.Left, Math.BitDecrement(bounds.Right)));
                var clampedPageY = Math.Clamp(point.Y, bounds.Top, Math.Max(bounds.Top, Math.BitDecrement(bounds.Bottom)));
                hit = new(clampedPageX - dx, clampedPageY - totalDy);
            }
        }
        if (closest is null) return false;
        offset = closest.Offset + closest.Text.DocumentCharacterFromPoint(new(hit.X - closest.Bounds.X, hit.Y - closest.Bounds.Y));
        return true;
    }

    internal IReadOnlyList<Rect> TextBounds(int start, int length, bool clip = true)
    {
        if (_pageBlank) return [];
        EnsureTextGeometry();
        if (_documentCssRoot is not null) return DocumentCssTextBounds(start, length, clip);
        var result = new List<Rect>();
        var page = clip ? GetChildLayoutClip()?.Bounds : null;
        var padding = EffectivePagePadding;
        foreach (var view in _paragraphs)
        {
            if (!view.Text.IsVisible) continue;
            var from = Math.Max(start, view.Offset);
            var to = Math.Min(start + length, view.Offset + view.Text.Text.Length);
            if (to < from || to == from && length != 0) continue;
            var offset = view.Text.TransformToAncestor(this);
            Rect? bandClip = null;
            var dy = view.IsPageFixed ? padding.Top : MapDocumentYToPageOffset(view.Source, out bandClip);
            var dx = padding.Left + view.VisualOffset.X;
            var totalDy = dy + view.VisualOffset.Y;
            foreach (var rect in view.Text.DocumentTextBounds(from - view.Offset, to - from))
            {
                var bounds = new Rect(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
                if (view.TableClip is { } cell)
                {
                    bounds = Rect.Intersect(bounds, new Rect(cell.X + dx, cell.Y + totalDy, cell.Width, cell.Height));
                }
                var parts = view.ColumnClips is { } pieces
                    ? ClipPieces(bounds, pieces.Select(piece => Shift(piece, dx, totalDy))) : new[] { bounds };
                foreach (var part in parts)
                {
                    var visible = page is { } viewport ? Rect.Intersect(part, viewport) : part;
                    if (clip && !view.IsPageFixed && bandClip is { } bc && !visible.IsEmpty)
                        visible = Rect.Intersect(visible, bc);
                    if (!visible.IsEmpty && visible.Width > 0 && visible.Height > 0) result.Add(visible);
                }
            }
        }
        return clip ? AutomationVisibility.ClipRectangles(this, result) : result;
    }
}
