using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private readonly List<CssClampContent> _clampContents = [];
        private readonly Dictionary<CssInlineSource, CssClampMarker> _clampMarkerSources = [];
        private readonly Dictionary<CssInlineSource, (CssInlineSource Source, int End)> _clampMarkerTargets = [];
        private readonly List<(CssClampMarker Marker, CssInlineFragment Fragment)> _clampMarkers = [];
        private int _clampUnitsBefore, _clampLineEnd, _clampLastFloat;
        private UIElement ClampKey => _run && _sources.Count > 0 ? _sources[0].Element : owner;
        internal IEnumerable<CssClampContent> ClampContents => _clampContents;
        internal bool ContainsClampFloat(UIElement element) => _floatEvents.Any(item => ReferenceEquals(item.Element, element));

        private void BeginClamp()
        {
            _clampContents.Clear(); _clampMarkerSources.Clear(); _clampMarkerTargets.Clear(); _clampMarkers.Clear();
            _clampUnitsBefore = _clampLineEnd = _clampLastFloat = 0;
        }

        private CssInlineSource MarkerSource(List<CssInlineUnit> units, int start, int end)
        {
            var value = ((CssBlockEllipsis)owner.GetValue(CssLineClampProperties.EllipsisProperty)!).Value;
            var marker = new CssClampMarker(owner, value);
            var metrics = CssFlowProperties.LineMetrics(owner);
            var source = new CssInlineSource(marker.TextControl, marker.TextControl, false, default, default)
                { Atomic = true, AtomicSize = new(marker.Advance, metrics.Height), AtomicBaseline = metrics.Baseline };
            _clampMarkerSources.Add(source, marker);
            // The anonymous marker is not a character in the public document.
            // A hit on it resolves to the retained original source boundary.
            var target = end > start ? units[end - 1] : start < units.Count ? units[start] : default;
            if (target.Source is { Atomic: false, EmptyFrame: false } textSource)
                _clampMarkerTargets.Add(source, (textSource, end > start ? target.End : target.Start));
            return source;
        }

        private List<CssInlineUnit> ClampUnits(List<CssInlineUnit> units)
        {
            if (!CssLineClampLayout.UseEndpoint || CssLineClampLayout.Limit(ClampKey, true) is not { IsClampPoint: true, End: { } endpoint } ||
                endpoint > _clampUnitsBefore + units.Count) return units;
            var count = Math.Clamp(endpoint - _clampUnitsBefore, 0, units.Count);
            var visible = units.GetRange(0, count);
            if (!CssLineClampLayout.Stable(owner) && endpoint >= _clampUnitsBefore &&
                owner.GetValue(CssLineClampProperties.EllipsisProperty) is CssBlockEllipsis { HasContent: true })
                visible.Add(new(MarkerSource(units, 0, count), 0, 0, false, false, false));
            foreach (var item in _floatEvents.Where(item => item.Position >= count)) CssLineClampLayout.Suppress(item.Element);
            return visible;
        }

        private LinePlan ClampLastLine(List<CssInlineUnit> units, int start, LinePlan plan, double indent)
        {
            if (CssLineClampLayout.Limit(ClampKey, true) is not { IsClampPoint: true } limit || _lines.Count + 1 != limit.Count ||
                !CssLineClampLayout.Stable(owner) && limit.End is not null) return plan;
            var end = plan.End;
            if (owner.GetValue(CssLineClampProperties.EllipsisProperty) is CssBlockEllipsis { HasContent: true })
            {
                var marker = MarkerSource(units, start, start);
                var available = FloatBand(FragmentMetrics(plan.Fragments).Height);
                var width = Math.Max(0, available.Right - available.Left) - indent - marker.AtomicSize.Width;
                var selected = start;
                List<CssInlineFragment> prefix = [];
                for (var candidate = start; candidate <= end; candidate++)
                {
                    var valid = candidate == start || candidate == units.Count || candidate < units.Count &&
                        BreakBetween(units[candidate - 1], units[candidate], ignoreNoWrap: true);
                    if (!valid) continue;
                    var visible = candidate;
                    while (visible > start && units[visible - 1].Collapse) visible--;
                    var pieces = FragmentsFor(units, start, visible, indent, candidate, 0);
                    if (pieces.Any(static fragment => fragment.HangingWhitespace > 0))
                    {
                        while (visible > start && units[visible - 1].Space) visible--;
                        pieces = FragmentsFor(units, start, visible, indent, candidate, 0);
                    }
                    if (FragmentWidth(pieces) > width + .000001) continue;
                    selected = visible; prefix = pieces;
                }
                end = selected;
                _clampMarkerTargets.Remove(marker);
                var target = end > start ? units[end - 1] : units[start];
                if (target.Source is { Atomic: false, EmptyFrame: false } textSource)
                    _clampMarkerTargets.Add(marker, (textSource, end > start ? target.End : target.Start));
                var metrics = CssFlowProperties.LineMetrics(owner);
                prefix.Add(new(marker, new TextLayoutLine(0, 0, marker.AtomicSize.Width, false,
                    metrics.Height, metrics.Baseline, metrics.Height)));
                plan = new(end, plan.Consumed, false, prefix);
            }
            limit.End = _clampUnitsBefore + end;
            return plan with { HardBreak = false };
        }

        private bool ClampExhausted => CssLineClampLayout.Limit(ClampKey, true) is { } limit && _lines.Count >= limit.Count;

        private void RecordClampLine(List<CssInlineFragment> fragments, double height, bool empty)
        {
            if (CssLineClampLayout.PassStamp == 0) return;
            while (_clampLastFloat < _nextFloat)
                _clampContents.Add(new(Box: _floatEvents[_clampLastFloat++].Element, Floating: true));
            if (!empty)
            {
                var top = _run ? CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width).Top : _insets.Top;
                var end = _clampUnitsBefore + _clampLineEnd;
                if (CssLineClampLayout.UseEndpoint && CssLineClampLayout.Limit(ClampKey, true)?.End is { } endpoint) end = Math.Min(end, endpoint);
                _clampContents.Add(new(Line: new(new(ClampKey, CssClampParagraphKind.Shared), owner, _lines.Count, _y + top, height,
                    end)));
            }
            foreach (var fragment in fragments)
                if (_clampMarkerSources.TryGetValue(fragment.Source, out var marker))
                {
                    _clampMarkers.Add((marker, fragment));
                    if (_clampMarkerTargets.TryGetValue(fragment.Source, out var target))
                    { target.Source.ClampMarker = fragment; target.Source.ClampEnd = target.End; }
                }
        }

        private void SuppressUnplacedClampSources()
        {
            // Intrinsic probes share the outer plan, but cannot publish their
            // temporary omissions as the final visibility of an atomic box.
            if (_intrinsic || CssLineClampLayout.Limit(ClampKey, true) is null) return;
            foreach (var source in _sources)
                // Split blocks use BlockRect, not inline fragments. Preserve
                // placed blocks while still hiding discarded/unplaced sources.
                if (source.Atomic && !source.EmptyFrame &&
                    (source.Block ? !source.BlockPlaced : source.Fragments.Count == 0)) CssLineClampLayout.Suppress(source.Element);
            if (ClampExhausted)
            {
                while (_nextFloat < _floatEvents.Count) CssLineClampLayout.Suppress(_floatEvents[_nextFloat++].Element);
            }
        }

        internal void PaintClampMarkers(DrawingContext context, Point offset)
        {
            // Build() stores its own insets, FormatRun() receives the outer
            // block's insets from the flow exporter.
            var dx = _run ? offset.X : offset.X - _insets.Left;
            var dy = _run ? offset.Y : offset.Y - _insets.Top;
            foreach (var item in _clampMarkers)
            {
                var fragment = item.Fragment;
                item.Marker.Draw(context, fragment.TextX + _insets.Left + dx,
                    fragment.TextY + fragment.Line.Baseline + _insets.Top + dy,
                    fragment.ParagraphClipLeft + _insets.Left + dx, fragment.ParagraphClipRight + _insets.Left + dx);
            }
        }
    }
}
