using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private sealed class InitialFragmentBox(InitialLetterPlacement placement)
    {
        internal readonly InitialLetterPlacement Placement = placement;
        internal double Width, Height, Top, ShiftX, GridOffset;
        internal double Advance;
        internal bool Overflow;
        internal CssFloatShape? Shape;
    }

    internal sealed partial class CssTextFlowLayout
    {
        private bool AppendInlineInitial(InitialLetterPlacement data, List<CssInlineUnit> pending,
            ref bool collapsedSpace, CssInlineFrame? parent)
        {
            data.Inline = true; data.Scope.Fragmented = true;
            var frame = new CssInlineFrame(data.Scope.Root, parent, data.Insets, data.Margin) { Initial = data };
            data.Frame = frame; _frames.Add(frame); _initialLetter = data;
            var first = pending.Count; var sourceStart = _sources.Count;
            foreach (var child in new CssPanelChildren(data.Scope.Root)) AppendElement(child, pending, ref collapsedSpace, frame);
            if (pending.Count == first) AppendFrameMarker(frame, pending);
            // A terminal forced break has a real empty fragment on the next
            // line. Keep its original source offset for caret navigation.
            if (pending[^1] is { HardBreak: true } breaker)
            {
                pending.Add(new(breaker.Source, breaker.End, breaker.End, false, false, false));
                breaker.Source.LastUnit = pending.Count - 1;
            }
            frame.LastUnit = pending.Count - 1;
            foreach (var source in _sources.Skip(sourceStart)) source.Initial = data;
            collapsedSpace = false;
            return true;
        }

        private static bool HasContinuingInitial(List<CssInlineFragment> fragments) =>
            FramePieces(fragments).Any(static piece => piece.Frame.Initial is { Inline: true } && !piece.Last);

        private static double InitialFrameShift(CssInlineFrame frame, IReadOnlyList<CssInlineFramePiece> pieces)
        {
            for (var parent = frame.Parent; parent is not null; parent = parent.Parent)
                if (parent.Initial is { Inline: true })
                    return pieces.FirstOrDefault(piece => ReferenceEquals(piece.Frame, parent))?.Initial?.ShiftX ?? 0;
            return 0;
        }

        private void PrepareInlineInitial(List<CssInlineFragment> fragments, Dictionary<CssInlineFrame, InlineMetricNode> nodes)
        {
            foreach (var fragment in fragments) { fragment.InitialAdvance = 0; fragment.InitialShiftX = 0; }
            var pieces = FramePieces(fragments);
            foreach (var piece in pieces)
            {
                if (piece.Frame.Initial is not { Inline: true } data) continue;
                var node = nodes[piece.Frame]; var scope = data.Scope;
                var members = fragments.Where(fragment => ReferenceEquals(fragment.Source.Initial, data)).ToArray();
                if (members.Length == 0) continue;
                var positions = new Dictionary<CssInlineFragment, double>();
                var rtl = owner.FlowDirection == FlowDirection.RightToLeft;
                var x = rtl ? fragments.Sum(static fragment => fragment.Width + fragment.MarginLeft + fragment.MarginRight) : 0d;
                foreach (var fragment in fragments)
                {
                    var advance = fragment.Width + fragment.MarginLeft + fragment.MarginRight;
                    if (rtl) x -= advance;
                    positions.Add(fragment, x + fragment.OuterLeft + fragment.MarginLeft + fragment.Left +
                        (rtl ? fragment.Line.EndPadding : fragment.Line.StartPadding) - fragment.Line.HangingStart);
                    if (!rtl) x += advance;
                }
                var first = fragments[piece.LeftIndex]; var last = fragments[piece.RightIndex];
                var start = positions[first] - first.Left - first.MarginLeft - first.Line.StartPadding + first.Line.HangingStart - piece.Left;
                var rawWidth = positions[last] + last.BoxWidth - last.Left - last.Line.StartPadding + last.Line.HangingStart + piece.Right - start;
                // Text bearings and atomic margin boxes share the same local
                // coordinates used by the containing inline metric tree.
                Rect? bounds = null;
                foreach (var fragment in members)
                {
                    Rect ink;
                    if (fragment.Source.Atomic)
                        ink = new(positions[fragment] - fragment.Source.Margin.Left, fragment.MetricTop,
                            fragment.Source.AtomicSize.Width + fragment.Source.Margin.Left + fragment.Source.Margin.Right,
                            fragment.Line.Height);
                    else
                    {
                        var formatted = InitialFragmentText(fragment);
                        if (!TextMeasurement.TryGetFontInkBounds(formatted, out var glyph) || glyph.IsEmpty) continue;
                        ink = new(positions[fragment] + glyph.X, fragment.MetricTop + fragment.Line.Baseline + glyph.Y,
                            glyph.Width, glyph.Height);
                    }
                    bounds = bounds is { } previous ? Rect.Union(previous, ink) : ink;
                }
                var over = node.Ascent - scope.Baseline;
                var inkBounds = bounds ?? new Rect(start + piece.Left, over, 0, scope.Height);
                var top = data.Insets.Top > 0 ? Math.Min(over, inkBounds.Top) : over;
                var height = Math.Max(over + scope.Height, inkBounds.Bottom) - top + data.Insets.Top + data.Insets.Bottom;
                var width = scope.AutoWidth ? inkBounds.Width + piece.Left + piece.Right : data.PreferredWidth;
                if (!double.IsFinite(width)) width = rawWidth;
                width = Math.Clamp(Math.Max(0, width), data.MinWidth, data.MaxWidth);
                height = Math.Clamp(double.IsNaN(data.PreferredHeight) ? height : data.PreferredHeight, data.MinHeight, data.MaxHeight);
                var blockAlignment = 0d;
                if (!double.IsNaN(data.PreferredHeight))
                {
                    var family = (FontFamily)scope.Root.GetValue(TextElement.FontFamilyProperty)!;
                    var font = TextMeasurement.GetFontMetrics(family.GetRenderingSource(scope.Root), scope.FontSize,
                        ((FontWeight)scope.Root.GetValue(TextElement.FontWeightProperty)!).ToOpenTypeWeight(),
                        ((FontStyle)scope.Root.GetValue(TextElement.FontStyleProperty)!).ToOpenTypeStyle());
                    top = Math.Min(node.Ascent - font.Ascent, 0);
                    var naturalHeight = Math.Max(node.Height, node.Ascent + font.Descent - top);
                    var free = height - data.Insets.Top - data.Insets.Bottom - naturalHeight;
                    blockAlignment = (CssBoxAlignment)scope.Root.GetValue(CssGridProperties.AlignContentProperty)! switch
                    { CssBoxAlignment.End => free, CssBoxAlignment.Center => free / 2, _ => 0 };
                }
                var baseline = node.Ascent - top + data.Insets.Top + blockAlignment;
                var under = data.Align.BorderBox ? height : baseline + data.UnderPoint;
                var relativeTop = data.Value.Size >= data.Value.Sink
                    ? data.RootBaseline + data.RootUnder + Math.Min(1048576, (data.Value.Sink - 1) * data.Interval) - under
                    : data.RootBaseline - data.RootOver - (data.Align.BorderBox ? 0 : data.Insets.Top);
                var shiftX = scope.AutoWidth ? rtl ? start + rawWidth - piece.Right - inkBounds.Right : start + piece.Left - inkBounds.Left : 0;
                if (!scope.AutoWidth)
                {
                    var free = width - rawWidth;
                    shiftX = CssFlowProperties.LineAlignment(scope.Root, piece.Last) switch
                    {
                        CssFlowTextAlignment.Right => free,
                        CssFlowTextAlignment.End when scope.Root.FlowDirection == FlowDirection.LeftToRight => free,
                        CssFlowTextAlignment.Start when scope.Root.FlowDirection == FlowDirection.RightToLeft => free,
                        CssFlowTextAlignment.Center => free / 2,
                        _ => 0,
                    };
                }
                var box = new InitialFragmentBox(data) { Width = width, Height = height, Top = relativeTop,
                    ShiftX = shiftX, Advance = width,
                    Overflow = !scope.AutoWidth && rawWidth > width + .000001 || rawWidth > data.MaxWidth + .000001 };
                piece.Initial = box;
                var shiftY = relativeTop + data.Insets.Top - top + blockAlignment;
                foreach (var fragment in members) { fragment.MetricTop += shiftY; fragment.InitialShiftX = shiftX; }
                foreach (var descendant in pieces)
                    if (!ReferenceEquals(descendant, piece) && BelongsToFrame(descendant.Frame, piece.Frame))
                    { descendant.Top += shiftY; descendant.Baseline += shiftY; }
                piece.Baseline = relativeTop + baseline;
                if (data.Wrap.Kind == CssInitialLetterWrapKind.Grid)
                {
                    var family = (FontFamily)owner.GetValue(TextElement.FontFamilyProperty)!;
                    var unit = TextMeasurement.GetFontUnitMetrics(family.GetRenderingSource(owner),
                        (double)owner.GetValue(TextElement.FontSizeProperty)!,
                        ((FontWeight)owner.GetValue(TextElement.FontWeightProperty)!).ToOpenTypeWeight(),
                        ((FontStyle)owner.GetValue(TextElement.FontStyleProperty)!).ToOpenTypeStyle()).IdeographicAdvance + CssTextSpacing.Letter(owner);
                    var outer = width + piece.MarginLeft + piece.MarginRight;
                    box.Advance = unit > 0 && double.IsFinite(unit) ? Math.Ceiling(outer / unit) * unit - piece.MarginLeft - piece.MarginRight : width;
                    var gridAlignment = (CssBoxAlignment)scope.Root.GetValue(CssGridProperties.JustifySelfProperty)! switch
                    { CssBoxAlignment.End => box.Advance - width, CssBoxAlignment.Center => (box.Advance - width) / 2, _ => 0 };
                    box.GridOffset = rtl ? box.Advance - width - gridAlignment : gridAlignment;
                    box.ShiftX += box.GridOffset;
                    foreach (var fragment in members) fragment.InitialShiftX += box.GridOffset;
                }
                if (piece.Last)
                {
                    var next = fragments.SkipWhile(fragment => !ReferenceEquals(fragment, members[^1])).Skip(1).FirstOrDefault();
                    var spaceAfter = next is { Source.Atomic: false } &&
                        System.Text.Rune.TryGetRuneAt(next.Source.Text._visualText, next.Line.StartIndex, out var rune) &&
                        System.Text.Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.SpaceSeparator;
                    if (data.Wrap.Kind is CssInitialLetterWrapKind.First or CssInitialLetterWrapKind.All)
                    {
                        box.Shape = CssShapeOutsideProperties.Resolve(scope.Root, new(width, height), data.Margin, _containing.Width);
                        if (box.Shape is null && InitialFragmentOutline(members, positions, start, relativeTop) is { } outline)
                        {
                            var distance = ((CssLayoutLength)scope.Root.GetValue(CssShapeOutsideProperties.MarginProperty)!).Resolve(_containing.Width, 0);
                            box.Shape = CssFloatShape.Create(outline, data.Margin.Left, data.Margin.Top,
                                Math.Max(0, width + data.Margin.Left + data.Margin.Right), Math.Max(0, height + data.Margin.Top + data.Margin.Bottom),
                                double.IsFinite(distance) ? Math.Max(0, distance) : 0);
                        }
                        if (box.Shape is { } shape && (!spaceAfter || data.Wrap.Kind == CssInitialLetterWrapKind.All))
                        {
                            var band = shape.Band(-relativeTop + data.Margin.Top, CssFlowProperties.LineMetrics(owner).Height);
                            box.Advance = Math.Max(0, !band.Active ? 0 : rtl ? width - band.Left : band.Right - data.Margin.Left - data.Margin.Right);
                        }
                    }
                    if (data.Wrap.Kind == CssInitialLetterWrapKind.Offset && !spaceAfter) box.Advance += data.Wrap.Offset.Resolve(width, 0);
                }
                // Adjust the fitting advance without changing native glyph
                // advances or the original text/source maps.
                members[^1].InitialAdvance = box.Advance - rawWidth;
                if (rtl) members[^1].InitialShiftX += members[^1].InitialAdvance;
            }
        }

        private static bool BelongsToFrame(CssInlineFrame frame, CssInlineFrame ancestor)
        {
            for (var current = frame.Parent; current is not null; current = current.Parent)
                if (ReferenceEquals(current, ancestor)) return true;
            return false;
        }

        private static FormattedText InitialFragmentText(CssInlineFragment fragment)
        {
            var text = fragment.Source.Text;
            var formatted = new FormattedText(text.PaintSlice(fragment.Line.StartIndex, fragment.Line.Length),
                text.FontFamily.GetRenderingSource(text), text.FontSize)
                { FontWeight = text.FontWeight.ToOpenTypeWeight(), FontStyle = text.FontStyle.ToOpenTypeStyle() };
            formatted.ApplyTextOptionsFrom(text);
            return formatted;
        }

        private static Geometry? InitialFragmentOutline(CssInlineFragment[] members,
            Dictionary<CssInlineFragment, double> positions, double start, double top)
        {
            var group = new GeometryGroup();
            foreach (var fragment in members)
            {
                if (fragment.Source.Atomic)
                {
                    group.Children.Add(new RectangleGeometry(new(positions[fragment] + fragment.InitialShiftX - start - fragment.Source.Margin.Left,
                        fragment.MetricTop - top, fragment.Source.AtomicSize.Width + fragment.Source.Margin.Left + fragment.Source.Margin.Right, fragment.Line.Height)));
                    continue;
                }
                var text = fragment.Source.Text; var family = text.FontFamily.GetRenderingSource(text);
                if (FontFeatureRenderingSource.TryUnwrap(family, out var features, out var inner, out var caps, out var options))
                {
                    if (features.Length != 0 || caps != CssCapsVariant.Normal || options.Position != 0 || options.Emoji != 0 ||
                        options.Variations is { Length: > 0 } || options.SizeAdjust is { Metric: not 0 } || options.FaceMetrics is { IsInitial: false }) return null;
                    family = inner;
                }
                if (CssFontFaces.IsBlocked(family) || CssFontRenderingPlan.TryDecode(family, out _) ||
                    FontWidthRenderingSource.TryUnwrap(family, out _, out _) || text._hasLetterSpacing || text._hasWordSpacing) return null;
                var formatted = InitialFragmentText(fragment);
                if (formatted.Text.Length == 0) continue;
                if (!NativeTextOutline.TryGetPath(formatted.Text, family, (float)text.FontSize, text.FontWeight.ToOpenTypeWeight(),
                    text.FontStyle.ToOpenTypeStyle(), out var path, out _, out var baseline)) return null;
                if (path.Length == 0) continue;
                var outline = Geometry.Parse(path);
                if (!TextMeasurement.TryGetFontInkBounds(formatted, out var ink)) return null;
                var bounds = outline.Bounds;
                if (Math.Abs(bounds.Left - ink.Left) > .05 || Math.Abs(bounds.Top - baseline - ink.Top) > .05 ||
                    Math.Abs(bounds.Width - ink.Width) > .05 || Math.Abs(bounds.Height - ink.Height) > .05) return null;
                outline.Transform = new TranslateTransform(positions[fragment] + fragment.InitialShiftX - start,
                    fragment.MetricTop + fragment.Line.Baseline - baseline - top);
                group.Children.Add(outline);
            }
            return group;
        }

        private static double InitialLineAdvance(List<CssInlineFragment> fragments, double ordinary)
        {
            foreach (var piece in FramePieces(fragments))
                if (piece is { Last: false, Initial: { } initial })
                    ordinary = Math.Max(ordinary, initial.Height + piece.Frame.Margin.Top + piece.Frame.Margin.Bottom);
            return ordinary;
        }

        private void PlaceInlineInitial(List<CssInlineFragment> fragments)
        {
            foreach (var piece in FramePieces(fragments))
            {
                if (piece.Initial is not { } box) continue;
                var data = box.Placement;
                if (!data.Placed) data.OriginLineTop = _y;
                data.Rect = data.Placed ? Rect.Union(data.Rect, piece.Rect) : piece.Rect; data.Placed = true;
                if (!piece.Last) continue;
                var rtl = owner.FlowDirection == FlowDirection.RightToLeft;
                data.Region = new(rtl ? CssFloatSide.Right : CssFloatSide.Left,
                    piece.Rect.X - data.Margin.Left - box.GridOffset, piece.Rect.Y - data.Margin.Top,
                    Math.Max(0, (data.Wrap.Kind == CssInitialLetterWrapKind.Grid ? box.Advance : box.Width) + data.Margin.Left + data.Margin.Right),
                    Math.Max(0, box.Height + data.Margin.Top + data.Margin.Bottom),
                    data.Wrap.Kind == CssInitialLetterWrapKind.All ? box.Shape : null, IsInitial: true, InitialLineTop: data.OriginLineTop);
            }
        }
    }
}
