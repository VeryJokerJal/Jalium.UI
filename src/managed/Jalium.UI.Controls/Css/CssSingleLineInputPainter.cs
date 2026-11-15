using System.Runtime.CompilerServices;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Shared shaping, annotation and selection coordinates for single-line editors.</summary>
internal static class CssSingleLineInputPainter
{
    private static readonly ConditionalWeakTable<Control, Cache> Caches = new();

    internal static Frame Create(Control owner, string text, Rect content, double scroll, Brush ink,
        bool topAligned = false, bool opaqueCharacters = false)
    {
        var cache = Caches.GetValue(owner, static control =>
        {
            var value = new Cache();
            control.Unloaded += (_, _) => value.Clear();
            return value;
        });
        return new(owner, cache.Get(owner, text, content.Width, ink), content, scroll, ink, topAligned, opaqueCharacters);
    }

    internal static double LineHeight(Control owner) => new CssInputTextEmphasis(owner).Height;

    internal readonly record struct Key(RenderContext? Context, string Text, string Family, double Size,
        int Weight, int Style, int Stretch, double Width, TextAlignment Alignment,
        FlowDirection Direction, bool Natural, uint Color, double Opacity, long FontEpoch, CssInputTextSpacing Spacing, Brush? FallbackInk,
        int RenderingMode, int FormattingMode, int HintingMode);

    private sealed class Cache
    {
        private Key _key;
        private Shape? _shape;
        internal Shape Get(Control owner, string text, double width, Brush ink)
        {
            // Native single-line formats still give hard breaks special meaning.
            // Display one space for each source code unit, preserving every offset.
            var display = text.IndexOfAny(['\r', '\n']) >= 0 ? text.Replace('\r', ' ').Replace('\n', ' ') : text;
            var family = owner.FontFamily?.GetRenderingSource(owner) ?? FrameworkElement.DefaultFontFamilyName;
            var stretch = owner.FontStretch.ToOpenTypeStretch();
            family = FontWidthRenderingSource.ForFormattedText(family, stretch);
            var alignment = CssFlowProperties.ToNativeTextAlignment(
                CssFlowProperties.LineAlignment(owner, true), owner.FlowDirection == FlowDirection.RightToLeft);
            var natural = !owner.HasValueAboveInherited(FrameworkElement.FlowDirectionProperty) &&
                owner.GetUncoercedBaseValueInternal(FrameworkElement.FlowDirectionProperty).source == BaseValueSource.Default;
            var solid = ink as SolidColorBrush;
            var key = new Key(RenderContext.Current, display, family, owner.FontSize,
                owner.FontWeight.ToOpenTypeWeight(), owner.FontStyle.ToOpenTypeStyle(), stretch,
                (float)Math.Max(1, double.IsFinite(width) ? width : 100000), alignment, owner.FlowDirection,
                natural, solid?.Color.ToArgb() ?? 0, solid?.Opacity ?? 1, TextMeasurement.FontCacheEpoch, CssInputTextSpacing.For(owner), solid is null ? ink : null,
                (int)TextOptions.GetTextRenderingMode(owner), (int)TextOptions.GetTextFormattingMode(owner), (int)TextOptions.GetTextHintingMode(owner));
            if (_shape is not null && key == _key) return _shape;
            Clear(); _key = key; _shape = new Shape(owner, key, ink);
            return _shape;
        }
        internal void Clear() { _shape?.Dispose(); _shape = null; }
    }

    internal sealed class Shape : IDisposable
    {
        internal readonly Key Key;
        internal readonly NativeTextParagraph? Native;
        internal readonly NativeTextParagraph.Line? Line;
        internal readonly TextBlock.EditorLayout? Fallback;
        internal readonly double Height, Baseline, Width, Left, AlignmentShift;
        internal Shape(Control owner, Key key, Brush foreground)
        {
            var ink = foreground as SolidColorBrush;
            Key = key;
            var metrics = TextMeasurement.GetFontMetrics(key.Family, key.Size, key.Weight, key.Style);
            var measured = new FormattedText(key.Text, key.Family, key.Size)
            { FontWeight = key.Weight, FontStyle = key.Style, FontStretch = key.Stretch, NativeNoWrap = true };
            TextMeasurement.MeasureText(measured);
            Height = Math.Max(1, Math.Max(metrics.LineHeight, measured.Height));
            Baseline = metrics.Ascent > 0 ? metrics.Ascent : Height * .8;
            Width = measured.WidthIncludingTrailingWhitespace;
            if (ink is not null && key.Text.Length > 0)
                Native = key.Spacing.Active
                    ? NativeTextParagraph.TryCreateAdvanced([new(key.Text, key.Family, key.Size, key.Weight, key.Style, ink.Color, ink.Opacity)],
                        key.Family, key.Size, key.Width, Height, key.Direction, key.Spacing.Units(key.Text), naturalDirection: key.Natural)
                    : NativeTextParagraph.TryCreate([new(key.Text, key.Family, key.Size, key.Weight, key.Style, ink.Color, ink.Opacity)],
                        key.Family, key.Size, key.Width, Height, key.Alignment, key.Direction, key.Natural, noWrap: true, textOptionsOwner: owner);
            Line = Native?.Lines.FirstOrDefault();
            if (Line is { } line)
            {
                Height = Math.Max(Height, line.Metrics.Line.Height);
                Baseline = line.Metrics.Baseline;
                Left = line.Metrics.Line.X;
                Width = line.Metrics.Line.Width;
                if (key.Spacing.Active) AlignmentShift = key.Alignment == TextAlignment.Right ? key.Width - Width
                    : key.Alignment == TextAlignment.Center ? (key.Width - Width) / 2 : 0;
            }
            else if (key.Spacing.Active)
            {
                Fallback = new TextBlock.EditorLayout(owner, key.Text, key.Width, TextWrapping.NoWrap, key.Alignment, foreground);
                Fallback.Row(0, false, out var row);
                Height = Fallback.Height; Baseline = Fallback.Baseline;
                Width = row.Width; Left = row.X;
            }
            else Left = key.Alignment == TextAlignment.Right ? key.Width - Width
                : key.Alignment == TextAlignment.Center ? (key.Width - Width) / 2 : 0;
        }
        public void Dispose() { Native?.Dispose(); Fallback?.Dispose(); }
    }

    internal sealed class Frame
    {
        private readonly Control _owner;
        private readonly Shape _shape;
        private readonly Rect _content;
        private readonly Brush _ink;
        private readonly CssInputTextEmphasis _emphasis;
        private readonly double _x, _scroll;
        private readonly bool _topAligned, _opaque;
        internal Frame(Control owner, Shape shape, Rect content, double scroll, Brush ink, bool topAligned, bool opaque)
        {
            _owner = owner; _shape = shape; _content = content; _ink = ink;
            _topAligned = topAligned; _opaque = opaque;
            _emphasis = new(owner); _scroll = Math.Round(scroll); _x = content.X - _scroll + shape.AlignmentShift;
        }
        internal double Width => _shape.Width;
        internal double BodyHeight => _shape.Height;
        internal double Height => BodyHeight + _emphasis.Above + _emphasis.Below;
        internal double Top => _content.Y + (_topAligned ? 0 : (_content.Height - Height) / 2);
        internal double BodyTop => Top + _emphasis.Above;
        internal double Baseline => BodyTop + _shape.Baseline;
        internal double Position(int index) => Position(index, false);
        internal double Position(int index, bool backward)
        {
            index = Math.Clamp(index, 0, _shape.Key.Text.Length);
            if (_shape.Native is { } native && _shape.Line is { } line)
                return _x + native.Caret(line.Index, index, backward).X;
            if (_shape.Fallback is { } fallback && fallback.Caret((uint)index, false, out var caret))
                return _x + caret.CaretX;
            if (TextMeasurement.HitTestTextRangeWrapped(_shape.Key.Text, _shape.Key.Family, _shape.Key.Size,
                _shape.Key.Weight, _shape.Key.Style, float.PositiveInfinity, (uint)index, 0, out var range))
                return _x + _shape.Left + range.X;
            var text = new FormattedText(_shape.Key.Text[..index], _shape.Key.Family, _shape.Key.Size)
            { FontWeight = _shape.Key.Weight, FontStyle = _shape.Key.Style };
            TextMeasurement.MeasureText(text);
            return _x + _shape.Left + text.WidthIncludingTrailingWhitespace;
        }
        internal int Hit(Point point) => Hit(point, out _);
        internal int Hit(Point point, out bool backward)
        {
            backward = false;
            if (_shape.Native is { } native && _shape.Line is { } line)
            {
                var nativeHit = native.HitTest(line.Index, point.X - _x);
                backward = nativeHit.BackwardAffinity != 0;
                return Math.Clamp((int)nativeHit.TextPosition, 0, _shape.Key.Text.Length);
            }
            if (_shape.Fallback is { } fallback && fallback.Hit((float)(point.X - _x), 0, out var fallbackHit))
                return Math.Clamp((int)fallbackHit.TextPosition, 0, _shape.Key.Text.Length);
            if (TextMeasurement.HitTestPointWrapped(_shape.Key.Text, _shape.Key.Family, _shape.Key.Size,
                _shape.Key.Weight, _shape.Key.Style, float.PositiveInfinity, (float)(point.X - _x - _shape.Left), 0, out var hit))
                return Math.Clamp(hit.IsTrailingHit == 0 ? (int)hit.TextPosition : GraphemeClusters.NextBoundary(_shape.Key.Text, (int)hit.TextPosition), 0, _shape.Key.Text.Length);
            var nearest = 0; var distance = double.PositiveInfinity;
            for (var index = 0; index <= _shape.Key.Text.Length; index = index == _shape.Key.Text.Length ? index + 1 : GraphemeClusters.NextBoundary(_shape.Key.Text, index))
            { var delta = Math.Abs(Position(index) - point.X); if (delta < distance) { distance = delta; nearest = index; } }
            return nearest;
        }
        internal double ScrollToCaret(int index, bool backward)
        {
            if (_content.Width <= 0 || _shape.Width <= _content.Width) return 0;
            var scroll = _scroll;
            var minimum = Math.Min(0, _shape.Left + _shape.AlignmentShift);
            var maximum = Math.Max(0, _shape.Left + _shape.AlignmentShift + _shape.Width - _content.Width + 2);
            var caret = Position(index, backward) - _content.X + scroll;
            if (caret < scroll) scroll = caret;
            else if (caret > scroll + _content.Width - 2) scroll = caret - _content.Width + 2;
            return Math.Round(Math.Clamp(scroll, minimum, maximum));
        }
        internal Rect[] Selection(int start, int length)
        {
            start = Math.Clamp(start, 0, _shape.Key.Text.Length);
            length = Math.Clamp(length, 0, _shape.Key.Text.Length - start);
            if (length == 0) return [];
            if (_shape.Native is { } native && _shape.Line is { } line)
                return native.Selection(line.Index, start, length).Select(rect => new Rect(_x + rect.X, Top, Math.Max(1, rect.Width), Height)).ToArray();
            if (_shape.Fallback is { } fallback)
                return fallback.Selection(start, length).Select(rect => new Rect(_x + rect.X, Top, Math.Max(1, rect.Width), Height)).ToArray();
            if (Range((uint)start, (uint)length, out var range)) return [new(_x + range.X, Top, Math.Max(1, range.Width), Height)];
            var x = Position(start); var end = Position(start + length);
            return [new(Math.Min(x, end), Top, Math.Max(1, Math.Abs(end - x)), Height)];
        }
        private bool Row(uint position, bool backward, out TextLineMetrics row)
        {
            if (_shape.Line is { } line) { row = line.Metrics.Line; return true; }
            if (_shape.Fallback is { } fallback) return fallback.Row(position, backward, out row);
            return TextMeasurement.TryGetVisualLineMetrics(_shape.Key.Text, _shape.Key.Family, _shape.Key.Size,
                _shape.Key.Weight, _shape.Key.Style, float.PositiveInfinity, position, backward, out row);
        }
        private bool Range(uint start, uint length, out TextRangeMetrics range)
        {
            if (_shape.Native is { } native && _shape.Line is { } line && native.Selection(line.Index, (int)start, (int)length) is { Length: > 0 } boxes)
            {
                var left = boxes.Min(rect => rect.X); var right = boxes.Max(rect => rect.X + rect.Width);
                range = new() { TextPosition = start, Length = length, X = left, Width = right - left, Height = (float)BodyHeight };
                return true;
            }
            if (_shape.Fallback is { } fallback) return fallback.Range(start, length, out range);
            return TextMeasurement.HitTestTextRangeWrapped(_shape.Key.Text, _shape.Key.Family, _shape.Key.Size,
                _shape.Key.Weight, _shape.Key.Style, float.PositiveInfinity, start, length, out range);
        }
        private void Draw(DrawingContext context)
        {
            if (_shape.Key.Text.Length == 0) return;
            var key = _shape.Key;
            var text = new FormattedText(key.Text, key.Family, key.Size)
            {
                Foreground = _ink, FontWeight = key.Weight, FontStyle = key.Style, FontStretch = key.Stretch,
                MaxTextWidth = key.Width, MaxTextHeight = Math.Max(1, BodyHeight),
                NativeNoWrap = true, NativeTextAlignment = key.Alignment, FlowDirection = key.Direction,
                PlatformTextLine = _shape.Line, Trimming = _owner is TextBoxBase box ? box.TextTrimming : TextTrimming.None,
            };
            var shadow = CssTextShadowPainter.Begin(context, _owner,
                new Rect(_x + _shape.Left - key.Size, Top - key.Size, Math.Max(1, _shape.Width) + 2 * key.Size, Height + 2 * key.Size));
            try
            {
                using var textInk = MacOSWritingToolsPresentation.BeginCssTextInk(CssSelectionPainter.Unwrap(context));
                Action<DrawingContext, FormattedText, Point>? draw = _shape.Fallback is { } fallback ? fallback.Draw : null;
                if (!_emphasis.Draw(context, text, new Point(_x, Top), Row, Range, opaqueCharacters: _opaque, drawText: draw))
                {
                    if (draw is not null) draw(context, text, new Point(_x, BodyTop));
                    else context.DrawText(text, new Point(_x, BodyTop));
                }
                CssTextDecorationPainter.Draw(context, _owner, _owner,
                    CssTextDecorationProperties.Color(_owner) ?? _ink, CssTextDecorationProperties.Line(_owner),
                    _x + _shape.Left, _x + _shape.Left + _shape.Width, BodyTop, Baseline, key.Size,
                    BodyTop + BodyHeight, _shape.Width);
            }
            finally { shadow?.End(); }
        }
        internal void DrawText(DrawingContext context) => Draw(context);
        internal void Paint(DrawingContext context, int selectionStart, int selectionLength,
            bool showSelection, Brush? selectionBrush, Brush? selectionTextBrush)
        {
            var rectangles = showSelection ? Selection(selectionStart, selectionLength) : [];
            var style = rectangles.Length > 0 ? CssSelection.For(_owner) : null;
            if (rectangles.Length > 0 && (style is { HasPaint: true } || selectionTextBrush is not null))
            {
                var regions = rectangles.Select(rect => new CssSelectionPainter.Region(rect, style, _ink,
                    Baseline, TextTop: BodyTop)).ToArray();
                CssSelectionPainter.Paint(context, new RectangleGeometry(_content), regions, selectionBrush, selectionTextBrush,
                    Draw, (dc, region) => CssSelectionPainter.Decorations(dc, region, _owner));
            }
            else
            {
                if (selectionBrush is not null) foreach (var rect in rectangles) context.DrawRectangle(selectionBrush, null, rect);
                Draw(context);
            }
        }
    }
}
