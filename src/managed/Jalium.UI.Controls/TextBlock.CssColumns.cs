using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private CssColumnTextSnapshot? _cssColumns;
    private Dictionary<int, JustifiedLine>? _cssColumnJustifiedLines;
    private bool HasCssColumnText => _cssColumns?.IsActive == true;

    internal static bool PaintColumnInline(FrameworkElement source, DrawingContext context, bool border)
    {
        if (source is not TextBlock { IsCssInlineBox: true } text || !text.HasCssColumnText) return false;
        // These text fragments already use projected column coordinates. A
        // second projection of their envelope would fill neighboring sources.
        if (border) text.OnPostRender(context);
        else { CssBorderImagePainter.EmptyBackground(text, context); text.OnRender(context); }
        return true;
    }

    internal static bool PaintColumnText(FrameworkElement source, DrawingContext context)
    {
        var text = source as TextBlock ?? (source as ICssAnonymousTextBox)?.Owner as TextBlock;
        if (text?._cssColumns is not { IsActive: true } snapshot || !ReferenceEquals(source, snapshot.PaintElement)) return false;
        text.RenderTextContent(context); return true;
    }

    /// <summary>Projects the existing shaped lines; source offsets and inline runs remain unchanged.</summary>
    internal sealed class CssColumnTextSnapshot
    {
        private readonly TextBlock _text;
        private readonly WeakReference<ICssColumnLayout> _layout;
        private readonly Dictionary<int, CssInlineFragment>? _original;
        private readonly CssInlineFragment[]? _originalList;
        private readonly CssColumnTextSnapshot? _previous;
        private readonly Dictionary<int, JustifiedLine>? _originalJustification;
        private readonly Dictionary<int, JustifiedLine> _justification = [];
        private readonly CssInlineBoxFragment[] _decorations;
        private readonly bool _inlineBox;
        private readonly Size _allocation;
        private readonly CssInlineFragment[] _fragments;
        private readonly Point _origin;
        private readonly Visual? _parent;
        private readonly (CssClampMarker Marker, Rect Bounds, Point Origin, double Baseline, double? ClipLeft, double? ClipRight)? _documentMarker;
        private readonly Rect? _clampHitRect;
        internal FrameworkElement PaintElement { get; }
        internal bool IsActive => ReferenceEquals(_text._cssColumns, this) && _layout.TryGetTarget(out var layout) &&
            layout.IsApplied && layout.Contains(_text) && ReferenceEquals(PaintElement.VisualParent, _parent);
        internal (double Top, double Bottom)[] Lines => _fragments.Select(fragment =>
            (_origin.Y + fragment.TextY, _origin.Y + fragment.TextY + fragment.Line.Height)).ToArray();

        internal IEnumerable<Rect> ProjectedInk(Func<Point, Point> project)
        {
            foreach (var fragment in _fragments)
            {
                var at = project(new(_origin.X + fragment.Rect.X, _origin.Y + fragment.TextY));
                var rect = new Rect(at.X, at.Y + fragment.Rect.Y - fragment.TextY, fragment.Rect.Width, fragment.Rect.Height);
                var padding = Math.Max(1, _text.GetExtraDirtyPadding());
                if (padding > 0) rect.Inflate(padding, padding);
                yield return rect;
            }
        }

        internal CssColumnTextSnapshot(TextBlock text, FrameworkElement root, ICssColumnLayout layout)
        {
            _text = text; _layout = new(layout); PaintElement = text.UsesCssTextBody ? text._cssTextBody! : text;
            _previous = text.HasCssColumnText ? text._cssColumns : null;
            _originalJustification = text._cssColumnJustifiedLines;
            _origin = PaintElement.TransformToAncestor(root); _parent = PaintElement.VisualParent;
            text.EnsureLayout(text.GetLayoutConstraintWidth(text.TextContentSize.Width));
            _original = text._cssInlineFragments; _originalList = text._cssInlineFragmentList; _decorations = text._cssDecorationFragments; _allocation = text._cssInlineAllocation;
            _inlineBox = text._cssInlineBox;
            _documentMarker = text._documentSharedMarker; _clampHitRect = text._cssClampHitRect;
            var fragments = new List<CssInlineFragment>();
            var y = text.GetVerticalContentOffset(text.GetLineHeight());
            var source = new CssInlineSource(PaintElement, text, true, default, default);
            foreach (var line in text._layoutLines)
            {
                if (text.TryGetJustifiedLine(line, text.GetContentWidth(text.TextContentSize.Width), out var justified))
                    _justification[line.StartIndex] = justified;
                if (text.InlineFragment(line) is { } existing) fragments.Add(existing.Copy());
                else
                {
                    var x = text.GetLineOriginX(line, text.GetContentWidth(text.TextContentSize.Width));
                    var top = text.TextLineTop(line, y);
                    fragments.Add(new(source, line) { TextX = x, TextY = top, TabOrigin = x - text.GetTextInsets().Left,
                        // The painted advance includes justification, hanging
                        // text and tab stops. The natural width can exclude the
                        // last glyphs from ordinary (non-snapping) hit testing.
                        Rect = new(x, top, Math.Max(0, text.GetTextDecorationWidth(line) +
                            line.StartPadding + line.EndPadding), line.Height) });
                }
                y += line.Height;
            }
            _fragments = fragments.ToArray();
        }

        internal void Apply(Rect bounds, Func<Point, Point> project)
        {
            var fragments = new Dictionary<int, CssInlineFragment>();
            var fragmentList = new CssInlineFragment[_fragments.Length];
            for (var index = 0; index < _fragments.Length; index++)
            {
                var original = _fragments[index];
                var fragment = original.Copy();
                fragment.Line = fragment.Line.WithInlineFragmentIndex(index);
                var origin = project(new(_origin.X + fragment.TextX, _origin.Y + fragment.TextY));
                var rect = project(new(_origin.X + fragment.Rect.X, _origin.Y + fragment.TextY));
                var dx = origin.X - bounds.X - fragment.TextX;
                fragment.TextX = origin.X - bounds.X; fragment.TextY = origin.Y - bounds.Y;
                fragment.Rect = new(rect.X - bounds.X, origin.Y - bounds.Y + original.Rect.Y - original.TextY,
                    original.Rect.Width, original.Rect.Height);
                // Project the source, retaining the tab coordinate measured in
                // its original column. Moving the ruler coordinate as well
                // would make the next column paint against the first one's edge.
                fragment.FlowLeft += dx; fragment.FlowRight += dx;
                fragment.ParagraphClipLeft += dx; fragment.ParagraphClipRight += dx;
                fragments[fragment.Line.StartIndex] = fragment;
                fragmentList[index] = fragment;
            }
            _text._cssColumns = this; _text._cssInlineFragments = fragments; _text._cssInlineFragmentList = fragmentList;
            _text._cssInlineBox = _inlineBox;
            if (_documentMarker is { } marker)
            {
                var location = project(new(_origin.X + marker.Origin.X, _origin.Y + marker.Origin.Y));
                var corner = project(new(_origin.X + marker.Bounds.X, _origin.Y + marker.Bounds.Y));
                var rect = new Rect(corner.X - bounds.X, corner.Y - bounds.Y, marker.Bounds.Width, marker.Bounds.Height);
                var dx = location.X - bounds.X - marker.Origin.X;
                _text._documentSharedMarker = (marker.Marker, rect, new(location.X - bounds.X, location.Y - bounds.Y), marker.Baseline,
                    marker.ClipLeft + dx, marker.ClipRight + dx);
                _text._cssClampHitRect = rect;
            }
            _text._cssColumnJustifiedLines = _justification;
            _text._cssInlineAllocation = bounds.Size;
            _text._cssDecorationFragments = fragmentList.Select(fragment => new CssInlineBoxFragment(fragment.Rect,
                fragment.HasLeftEdge, fragment.HasRightEdge)).ToArray();
            _text._cssDecorationLookup.Clear();
            foreach (var piece in _text._cssDecorationFragments) _text._cssDecorationLookup[piece.Rect] = piece;
            _text._formattedLinesCacheDirty = true;
            PaintElement.InvalidateVisual(); _text.InvalidateVisual();
        }

        internal void Release()
        {
            if (!ReferenceEquals(_text._cssColumns, this)) return;
            _text._cssColumns = _previous; _text._cssInlineFragments = _original; _text._cssInlineFragmentList = _originalList;
            _text._cssInlineBox = _inlineBox;
            _text._documentSharedMarker = _documentMarker; _text._cssClampHitRect = _clampHitRect;
            _text._cssColumnJustifiedLines = _originalJustification;
            _text._cssInlineAllocation = _allocation; _text._cssDecorationFragments = _decorations;
            _text._cssDecorationLookup.Clear();
            foreach (var piece in _decorations) _text._cssDecorationLookup[piece.Rect] = piece;
            _text._formattedLinesCacheDirty = true; PaintElement.InvalidateCssAllocation(); _text.InvalidateVisual();
        }
    }
}
