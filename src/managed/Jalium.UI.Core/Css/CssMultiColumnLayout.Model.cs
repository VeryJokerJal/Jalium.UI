using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal sealed partial class CssMultiColumnLayout
{
    private sealed class Node(FrameworkElement element, Node? parent, Rect bounds, bool atomic, bool spanner)
    {
        internal FrameworkElement Element = element;
        internal Size Containing = element.LastCssMeasureBox?.ContainingBlock ?? bounds.Size;
        internal Size NaturalSize = bounds.Size;
        internal Node? Parent = parent;
        internal Rect Natural = bounds, Bounds;
        internal bool Atomic = atomic, Spanner = spanner;
        internal CssColumnPiece[] PriorPieces = [new(new(bounds.Size), bounds)];
        internal Rect[] PriorRules = [];
        internal CssColumnPiece[] Pieces = [];
        internal readonly List<Node> Children = [];
    }
    private readonly record struct Slice(double Start, double End, Rect Bounds, double SourceX, bool Spanner = false);

    private void Capture()
    {
        _breaks = new();
        var paintTexts = new Dictionary<FrameworkElement, TextBlock.CssColumnTextSnapshot>();
        void Text(TextBlock text)
        {
            var snapshot = new TextBlock.CssColumnTextSnapshot(text, owner, this);
            _texts.Add(snapshot); paintTexts[snapshot.PaintElement] = snapshot;
            var lines = snapshot.Lines;
            foreach (var line in lines) _breaks.Keeps.Add(new(line.Top, line.Bottom));
            if (lines.Length > 0)
            {
                var bands = lines.GroupBy(line => Math.Round(line.Top * 1000)).OrderBy(group => group.Key)
                    .Select(group => (Top: group.Min(line => line.Top), Bottom: group.Max(line => line.Bottom))).ToArray();
                _breaks.Paragraphs.Add(new(bands.Select(band => band.Top).ToArray(), bands.Select(band => band.Bottom).ToArray(),
                    (int)text.GetValue(CssFragmentationProperties.OrphansProperty)!, (int)text.GetValue(CssFragmentationProperties.WidowsProperty)!));
            }
        }
        if (owner is TextBlock { HasCssInlineBoxes: false } textOwner) Text(textOwner);
        void Visit(FrameworkElement element, Node? parent)
        {
            if (element.Visibility == Visibility.Collapsed || CssContentsLayout.Suppressed(element)) return;
            if (CssContentsLayout.IsContents(element))
            {
                if (element is TextBlock { HasCssInlineBoxes: false } contentsText) Text(contentsText);
                foreach (var child in new CssPanelChildren(element).Enumerate().OfType<FrameworkElement>()) Visit(child, parent);
                return;
            }
            if (element.CssLayout?.IsOutOfFlow == true) return;
            var spanner = SpannerWidth(element) is not null;
            var isFloat = CssFloatProperties.ActiveSide(element) != CssFloatSide.None;
            var atomic = !spanner && !element.CanFragmentCssColumns && !paintTexts.ContainsKey(element) || isFloat;
            var origin = element.TransformToAncestor(owner);
            var node = new Node(element, parent, new(origin, element.RenderSize), atomic, spanner);
            if (element.CssColumnBox is { } priorBox && priorBox.Pieces.Length > 0)
            {
                node.NaturalSize = priorBox.NaturalSize;
                node.PriorPieces = priorBox.Pieces.Select(piece => new CssColumnPiece(piece.Source,
                    new(origin.X + piece.Destination.X, origin.Y + piece.Destination.Y, piece.Destination.Width, piece.Destination.Height))).ToArray();
            }
            if (Try(element, out var nestedLayout) && nestedLayout is { IsApplied: true })
                node.PriorRules = nestedLayout.Rules.Select(rule => new Rect(origin.X + rule.X, origin.Y + rule.Y, rule.Width, rule.Height)).ToArray();
            _nodes.Add(node); parent?.Children.Add(node);
            if (spanner) return;
            if (!atomic && element is TextBlock { HasCssInlineBoxes: false } text) Text(text);
            if (atomic)
            {
                if (!isFloat) _breaks.Keeps.Add(new(node.Natural.Top, node.Natural.Bottom));
                return;
            }
            if (CssFragmentationProperties.Inside(element) is CssFragmentBreak.Avoid or CssFragmentBreak.AvoidColumn)
                _breaks.Keeps.Add(new(node.Natural.Top, node.Natural.Bottom, true));
            if (CssFragmentationProperties.Before(element) == CssFragmentBreak.Column) _breaks.Forced.Add(node.Natural.Top);
            if (CssFragmentationProperties.After(element) == CssFragmentBreak.Column) _breaks.Forced.Add(node.Natural.Bottom);
            foreach (var child in element.SnapshotAttachedVisualChildren().OfType<FrameworkElement>()) Visit(child, node);
        }
        foreach (var child in owner.SnapshotAttachedVisualChildren().OfType<FrameworkElement>()) Visit(child, null);
        // Forced breaks also apply to atomic controls; their interior is protected above.
        foreach (var node in _nodes.Where(node => node.Atomic))
        {
            if (CssFragmentationProperties.Before(node.Element) == CssFragmentBreak.Column) _breaks.Forced.Add(node.Natural.Top);
            if (CssFragmentationProperties.After(node.Element) == CssFragmentBreak.Column) _breaks.Forced.Add(node.Natural.Bottom);
            if (CssFloatProperties.ActiveSide(node.Element) != CssFloatSide.None &&
                !_breaks.Forced.GetViewBetween(node.Natural.Top + .00001, Math.Max(node.Natural.Top + .00001, node.Natural.Bottom - .00001)).Any() &&
                (!double.IsFinite(_columnContainingHeight) || node.Natural.Top <= _insets.Top + .00001 || node.Natural.Bottom - _insets.Top <= _columnContainingHeight + .00001))
                _breaks.Keeps.Add(new(node.Natural.Top, node.Natural.Bottom));
        }
        foreach (var siblings in _nodes.GroupBy(node => node.Parent))
        {
            var ordered = siblings.Where(node => !node.Spanner).OrderBy(node => node.Natural.Top).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1]; var next = ordered[index];
                if (CssFloatProperties.ActiveSide(previous.Element) == CssFloatSide.None &&
                    CssFloatProperties.ActiveSide(next.Element) == CssFloatSide.None &&
                    next.Natural.Top > previous.Natural.Bottom + .00001 &&
                    CssFragmentationProperties.After(previous.Element) != CssFragmentBreak.Column &&
                    CssFragmentationProperties.Before(next.Element) != CssFragmentBreak.Column &&
                    !_breaks.Keeps.Any(keep => !keep.Soft && keep.Start < next.Natural.Top - .00001 && keep.End > previous.Natural.Bottom + .00001))
                    _breaks.Margins.Add((previous.Natural.Bottom, next.Natural.Top));
                if (CssFragmentationProperties.After(previous.Element) is not (CssFragmentBreak.Avoid or CssFragmentBreak.AvoidColumn) &&
                    CssFragmentationProperties.Before(next.Element) is not (CssFragmentBreak.Avoid or CssFragmentBreak.AvoidColumn)) continue;
                var before = _breaks.Keeps.Where(keep => !keep.Soft && keep.Start >= previous.Natural.Top && keep.End <= previous.Natural.Bottom)
                    .OrderBy(keep => keep.End).LastOrDefault(new CssColumnFragmentation.Keep(previous.Natural.Top, previous.Natural.Bottom));
                var after = _breaks.Keeps.Where(keep => !keep.Soft && keep.Start >= next.Natural.Top && keep.End <= next.Natural.Bottom)
                    .OrderBy(keep => keep.Start).FirstOrDefault(new CssColumnFragmentation.Keep(next.Natural.Top, next.Natural.Bottom));
                _breaks.Keeps.Add(new(before.Start, after.End, true));
            }
        }
    }

    private void Plan(double end, double limit)
    {
        var y = _insets.Top; var start = _insets.Top;
        var spanners = _nodes.Where(node => node.Spanner).OrderBy(node => node.Natural.Top).ToArray();
        var lastGroupOverflowed = false;
        void Group(double until, bool beforeSpanner)
        {
            lastGroupOverflowed = false;
            if (until <= start + .00001) return;
            var balance = beforeSpanner || (CssColumnFill)owner.GetValue(CssMultiColumnProperties.FillProperty)! != CssColumnFill.Auto;
            var remaining = double.IsFinite(limit) ? Math.Max(0, limit - (y - _insets.Top)) : limit;
            var height = balance ? _breaks.Balance(start, until, _count, remaining) : remaining;
            if (!double.IsFinite(height)) height = until - start;
            var breaks = _breaks.Breaks(start, until, height);
            lastGroupOverflowed = breaks.Count > _count;
            var used = 0d;
            for (var column = 0; column < breaks.Count; column++)
            {
                var (from, to) = breaks[column];
                var x = _insets.Left + (owner.FlowDirection == FlowDirection.RightToLeft ? _count - 1 - column : column) * (_columnWidth + _gap);
                _slices.Add(new(from, to, new(x, y, _columnWidth, to - from), _insets.Left));
                used = Math.Max(used, to - from);
            }
            // A definite column height remains available even when balanced content is shorter.
            var boxHeight = !beforeSpanner && double.IsFinite(remaining) ? Math.Max(used, remaining) : used;
            if (!beforeSpanner) boxHeight = Math.Max(boxHeight, _minimumHeight - (y - _insets.Top));
            for (var column = 1; column < breaks.Count; column++)
            {
                var x = owner.FlowDirection == FlowDirection.RightToLeft
                    ? _insets.Left + (_count - column) * (_columnWidth + _gap) - _gap / 2
                    : _insets.Left + column * (_columnWidth + _gap) - _gap / 2;
                _rules.Add(new(x, y, 0, boxHeight));
            }
            y += boxHeight; start = until;
        }
        foreach (var spanner in spanners)
        {
            var margin = CssBoxMetrics.Margin(spanner.Element, _width);
            var before = Math.Max(0, margin.Top); var after = Math.Max(0, margin.Bottom);
            var from = Math.Max(start, spanner.Natural.Top - before);
            Group(from, true);
            var to = Math.Min(end, spanner.Natural.Bottom + after);
            var spannerHeight = to - from;
            if (double.IsFinite(limit))
            {
                var keepAfterHeight = 0d;
                if (CssFragmentationProperties.After(spanner.Element) is CssFragmentBreak.Avoid or CssFragmentBreak.AvoidColumn)
                {
                    var nextKeep = _breaks.Keeps.Where(keep => !keep.Soft && keep.Start >= to - .00001).OrderBy(keep => keep.Start).FirstOrDefault();
                    if (nextKeep.End > nextKeep.Start) keepAfterHeight = nextKeep.End - to;
                }
                if (lastGroupOverflowed || y - _insets.Top + spannerHeight + keepAfterHeight > limit + .00001)
                {
                    if (_demotedSpanners.Add(spanner.Element)) _spannerDemoted = true;
                }
            }
            _slices.Add(new(from, to, new(_insets.Left, y, _width, spannerHeight), spanner.Natural.X, true));
            y += spannerHeight; start = to;
        }
        Group(end, false); _height = Math.Max(_minimumHeight, y - _insets.Top);
        foreach (var floatNode in _nodes.Where(node => CssFloatProperties.ActiveSide(node.Element) != CssFloatSide.None))
        {
            var slice = _slices.FirstOrDefault(s => !s.Spanner && floatNode.Natural.Top >= s.Start - .00001 && floatNode.Natural.Top < s.End - .00001);
            if (slice.Bounds.Width <= 0 || floatNode.Natural.Bottom <= slice.End + .00001) continue;
            if (double.IsFinite(limit) && floatNode.Natural.Top > slice.Start + .00001 && floatNode.Natural.Bottom - slice.Start > limit + .00001)
            {
                var nextSlice = _slices.FirstOrDefault(s => !s.Spanner && s.Start >= slice.End - .00001);
                var targetTop = nextSlice.Bounds.Width > 0 ? nextSlice.Start : slice.End;
                if (!_floatTopOverrides.TryGetValue(floatNode.Element, out var priorTop) || Math.Abs(priorTop - targetTop) > .01)
                {
                    _floatTopOverrides[floatNode.Element] = targetTop;
                    _floatConstraintsChanged = true;
                }
            }
            else if (!_floatWrapBottomLimits.TryGetValue(floatNode.Element, out var priorLimit) || Math.Abs(priorLimit - slice.End) > .01)
            {
                _floatWrapBottomLimits[floatNode.Element] = slice.End;
                _floatConstraintsChanged = true;
            }
        }
    }

    private Point Project(Point point)
    {
        var slice = _slices.FirstOrDefault(slice => point.Y >= slice.Start - .000001 && point.Y < slice.End - .000001);
        if (slice == default && _slices.Count > 0) slice = point.Y < _slices[0].Start ? _slices[0] : _slices[^1];
        return new(slice.Bounds.X + point.X - slice.SourceX, slice.Bounds.Y + point.Y - slice.Start);
    }

    private void Apply()
    {
        _fragmentedElements.Clear();
        foreach (var node in _nodes)
        {
            if (node.Atomic || node.Spanner)
            {
                node.Bounds = new(Project(node.Natural.Location), node.Natural.Size);
                node.Pieces = [new(new(node.Natural.Size), node.Bounds)];
            }
            else
            {
                _fragmentedElements.Add(node.Element);
                var pieces = new List<CssColumnPiece>(); var bounds = Rect.Empty;
                foreach (var prior in node.PriorPieces)
                    foreach (var slice in _slices)
                    {
                        if (slice.Spanner) continue;
                        var top = Math.Max(prior.Destination.Top, slice.Start); var bottom = Math.Min(prior.Destination.Bottom, slice.End);
                        if (bottom - top <= .00001) continue;
                        var source = new Rect(prior.Source.X, prior.Source.Y + (top - prior.Destination.Top), prior.Source.Width, bottom - top);
                        var destination = new Rect(slice.Bounds.X + prior.Destination.X - slice.SourceX, slice.Bounds.Y + top - slice.Start, prior.Destination.Width, bottom - top);
                        pieces.Add(new(source, destination)); bounds.Union(destination);
                    }
                if (bounds.IsEmpty) bounds = new(Project(node.Natural.Location), node.Natural.Size);
                node.Bounds = bounds; node.Pieces = pieces.ToArray();
            }
        }
        // A spanning descendant still belongs to its original ancestor. Its allocation
        // must be inside that ancestor's coordinate envelope, without painting an extra background.
        foreach (var node in _nodes.AsEnumerable().Reverse())
            if (!node.Atomic && !node.Spanner)
                foreach (var child in node.Children) node.Bounds.Union(child.Bounds);
        _applied = true;
        foreach (var text in _texts)
        {
            var node = _nodes.FirstOrDefault(node => ReferenceEquals(node.Element, text.PaintElement));
            if (node is not null) text.Apply(node.Bounds, Project);
        }
        var oldApplying = s_applying; s_applying = this;
        try
        {
            foreach (var node in _nodes)
            {
                var parentOrigin = node.Parent?.Bounds.Location ?? default;
                var localPieces = node.Pieces.Select(piece => new CssColumnPiece(piece.Source,
                    new(piece.Destination.X - node.Bounds.X, piece.Destination.Y - node.Bounds.Y, piece.Destination.Width, piece.Destination.Height))).ToArray();
                if (node.PriorRules.Length > 0 && Try(node.Element, out var nestedLayout) && nestedLayout is not null)
                {
                    var projectedRules = new List<Rect>();
                    foreach (var rule in node.PriorRules)
                        foreach (var slice in _slices)
                        {
                            if (slice.Spanner) continue;
                            var top = Math.Max(rule.Top, slice.Start); var bottom = Math.Min(rule.Bottom, slice.End);
                            if (bottom - top <= .00001) continue;
                            projectedRules.Add(new(slice.Bounds.X + rule.X - slice.SourceX - node.Bounds.X,
                                slice.Bounds.Y + top - slice.Start - node.Bounds.Y, 0, bottom - top));
                        }
                    nestedLayout.SetProjectedRules(projectedRules);
                }
                if (!node.Atomic && !node.Spanner)
                {
                    var inline = ProjectInlineFrames(node);
                    node.Element.CssColumnBox = new(node.Element, this, node.NaturalSize, node.Bounds.Size, localPieces,
                        DescendantRegions(node).Select(rect => new Rect(rect.X - node.Bounds.X, rect.Y - node.Bounds.Y, rect.Width, rect.Height)).ToArray(), inline);
                }
                node.Element.InvalidateCssAllocation();
                new CssLayoutBox(node.Element, 0).MeasureBorderBox(node.Bounds.Size, node.Containing);
                new CssLayoutBox(node.Element, 0).ArrangeBorderBox(new(node.Bounds.X - parentOrigin.X, node.Bounds.Y - parentOrigin.Y,
                    node.Bounds.Width, node.Bounds.Height), node.Containing);
            }
        }
        finally { s_applying = oldApplying; }
        UIElement.InvalidateHitTestCache(); owner.InvalidateVisual();
    }

    private CssInlineBoxFragment[]? ProjectInlineFrames(Node node)
    {
        if (!node.Element.IsCssInlineFrame) return null;
        var topInset = CssBoxMetrics.ContentInsets(node.Element, node.Element.CssLayout?.ContainingWidthCache ?? node.Containing.Width).Top;
        // Decoration follows its line's column, including ink outside the line
        // box. Cutting the ink rectangle would leak into an adjacent column.
        return node.Element.CssInlineBox!.Fragments.Select(fragment =>
        {
            var at = Project(new(node.Natural.X + fragment.Rect.X, node.Natural.Y + fragment.Rect.Y + topInset));
            return fragment with { Rect = new(at.X - node.Bounds.X, at.Y - node.Bounds.Y - topInset, fragment.Rect.Width, fragment.Rect.Height) };
        }).ToArray();
    }

    private IEnumerable<Rect> DescendantRegions(Node node)
    {
        foreach (var text in _texts.Where(text => ReferenceEquals(text.PaintElement, node.Element)))
            foreach (var rect in text.ProjectedInk(Project)) yield return rect;
        if (ProjectInlineFrames(node) is { } inline)
            foreach (var fragment in inline)
            {
                var rect = new Rect(node.Bounds.X + fragment.Rect.X, node.Bounds.Y + fragment.Rect.Y, fragment.Rect.Width, fragment.Rect.Height);
                var padding = Math.Max(1, node.Element.GetExtraDirtyPadding());
                rect.Inflate(padding, padding);
                yield return rect;
            }
        var extraDirty = node.Element.GetExtraDirtyPadding();
        var cloneBreak = CssBoxDecorationBreakProperties.Value(node.Element) == CssBoxDecorationBreak.Clone;
        foreach (var piece in node.Pieces)
        {
            var bounds = piece.Destination;
            if (!node.Atomic && !node.Spanner)
            {
                // The advance of an inline fragment is not an ink clipping
                // boundary. Keep the column's inline space available; native
                // and authored overflow clips still apply to the subtree.
                var top = node.Natural.Top + piece.Source.Top;
                var slice = _slices.FirstOrDefault(slice => !slice.Spanner &&
                    top >= slice.Start - .000001 && top < slice.End - .000001);
                if (slice.Bounds.Width > 0)
                {
                    var left = Math.Min(bounds.Left, slice.Bounds.Left);
                    var right = Math.Max(bounds.Right, slice.Bounds.Right);
                    bounds = new(left, bounds.Top, right - left, bounds.Height);
                }
            }
            if (extraDirty > 0)
            {
                var padTop = cloneBreak || piece.Source.Top <= .00001 ? extraDirty : 0;
                var padBottom = cloneBreak || piece.Source.Bottom >= node.NaturalSize.Height - .00001 ? extraDirty : 0;
                bounds = new(bounds.X - extraDirty, bounds.Y - padTop, bounds.Width + 2 * extraDirty, bounds.Height + padTop + padBottom);
            }
            yield return bounds;
        }
        foreach (var child in node.Children)
        {
            var childOffset = child.Element.CssLayout is { Position: CssPositionMode.Relative } rel
                ? CssRelativeLayout.Offset(child.Element, rel, CssRelativeLayout.ContainingBlock(child.Element, child.Bounds.Size))
                : default;
            foreach (var region in DescendantRegions(child))
                yield return new(region.X + childOffset.X, region.Y + childOffset.Y, region.Width, region.Height);
        }
    }
}
