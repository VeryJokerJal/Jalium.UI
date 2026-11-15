using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBox
{
    private const long MaxCachedTextBytes = 512 * 1024;
    private readonly Dictionary<MacOSLayoutKey, MacOSAlignedTextLayout> _macOSAlignedLayouts = [];
    private readonly record struct MacOSLayoutKey(RenderContext? Context, string Text, string Family,
        double Size, int Weight, int Style, double Width, TextAlignment Alignment, bool NoWrap, long FontEpoch, CssInputTextSpacing Spacing, TextWrapping Wrap, Brush? Ink);

    // The renderer and every geometry query use this finite layout box and
    // native alignment. No-wrap is a separate format flag, not an infinite
    // width which would discard the viewport's right/center alignment.
    private MacOSAlignedTextLayout? GetMacOSAlignedLayout(string text, double viewportWidth)
    {
        var context = RenderContext.Current;
        var tracking = CssInputTextSpacing.For(this);
        var native = OperatingSystem.IsMacOS() && context is { IsValid: true, Backend: RenderBackend.Metal or RenderBackend.Software };
        if ((!native && !tracking.Active) || FontSize <= 0 || !double.IsFinite(viewportWidth) || viewportWidth <= 0) return null;
        bool noWrap = TextWrapping == TextWrapping.NoWrap;
        double width = Math.Max(1, viewportWidth);
        // Native layout coordinates are floats on both drawing and query paths.
        width = (float)width;
        var alignment = CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, isLast: noWrap);
        var lastAlignment = CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, isLast: true);
        if (!noWrap && alignment != lastAlignment && GetVisualRowsForMacOSAlignment(text, viewportWidth) <= 1)
            alignment = lastAlignment;
        string family = FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName;
        var key = new MacOSLayoutKey(context, text, family, FontSize, FontWeight.ToOpenTypeWeight(),
            FontStyle.ToOpenTypeStyle(), width, alignment, noWrap, TextMeasurement.FontCacheEpoch, CssInputTextSpacing.For(this), TextWrapping, tracking.Active ? ResolveTextForegroundBrush() : null);
        if (_macOSAlignedLayouts.TryGetValue(key, out var cached))
        {
            if (cached.IsValid) return cached;
            _macOSAlignedLayouts.Remove(key); cached.Dispose();
        }
        // A bounded owner cache avoids holding every former edit/font/context.
        // Formats own backend leases and must be retired on unload as well.
        // A single oversized current line remains usable, but it must not
        // retain a second text/viewport/font layout alongside it.
        if (_macOSAlignedLayouts.Count >= 16 ||
            _macOSAlignedLayouts.Keys.Any(item => item.Context != context || item.FontEpoch != key.FontEpoch) ||
            _macOSAlignedLayouts.Keys.Sum(item => 2L * item.Text.Length) + 2L * text.Length > MaxCachedTextBytes)
            ClearMacOSAlignedLayouts();
        var format = native ? TextMeasurement.CreateTextFormatFromFamilyList(context!, family, (float)FontSize, key.Weight, key.Style) : null;
        format?.SetTextAlignment(alignment); format?.SetNoWrap(noWrap);
        var layout = new MacOSAlignedTextLayout(key, format, key.Spacing.Active
            ? new TextBlock.EditorLayout(this, text, width, TextWrapping, alignment, ResolveTextForegroundBrush()) : null);
        _macOSAlignedLayouts.Add(key, layout);
        return layout;
    }

    private int GetVisualRowsForMacOSAlignment(string text, double width)
    {
        if (CssInputTextSpacing.For(this).Active)
        {
            using var layout = new TextBlock.EditorLayout(this, text, width, TextWrapping, TextAlignment.Left, ResolveTextForegroundBrush());
            return layout.Count;
        }
        // Counts do not depend on horizontal alignment. This also works before
        // the control's visual-row cache has been populated by its first draw.
        var measured = new FormattedText(text, FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName, FontSize)
        { MaxTextWidth = width, FontWeight = FontWeight.ToOpenTypeWeight(), FontStyle = FontStyle.ToOpenTypeStyle() };
        return TextMeasurement.MeasureText(measured) ? measured.LineCount : 1;
    }

    private void ClearMacOSAlignedLayouts()
    {
        if (_macOSAlignedLayouts.Count == 0) return;
        var layouts = _macOSAlignedLayouts.Values.ToArray();
        _macOSAlignedLayouts.Clear();
        foreach (var layout in layouts) layout.Dispose();
    }

    private sealed class MacOSAlignedTextLayout(MacOSLayoutKey key, NativeTextFormat? format, TextBlock.EditorLayout? spacing = null) : IDisposable
    {
        private NativeTextParagraph? _selectionParagraph;
        internal NativeTextFormat? Format { get; } = format;
        internal bool IsValid => spacing is not null || Format?.IsValid == true;
        internal float Width { get; } = spacing is not null ? (float)spacing.Width : (float)(key.NoWrap && key.Text.Length > 0
            ? Math.Max(key.Width, format!.MeasureText(key.Text, 100000, 100000).WidthIncludingTrailingWhitespace)
            : key.Width);
        internal double ExtentWidth => spacing?.ExtentWidth ?? (key.Text.Length > 0 && NoWrap && Row(0, false, out var row)
            ? Math.Max(Width, row.X + row.Width) : Width);
        internal TextAlignment Alignment => key.Alignment;
        internal bool NoWrap => key.NoWrap;
        internal bool HasSpacing => spacing is not null;
        internal int? SpacedRows => spacing?.Count;
        internal void AddWordParagraphs(List<(NativeTextParagraph Paragraph, int Offset)> target, int sourceStart)
        {
            if (spacing is not null) foreach (var row in spacing.WordParagraphs)
                target.Add((row.Paragraph, sourceStart + row.Offset));
        }
        internal void Draw(DrawingContext context, FormattedText text, Point point) => spacing!.Draw(context, text, point);
        internal bool Caret(uint position, bool trailing, out TextHitTestResult hit)
        {
            if (spacing is not null) return spacing.Caret(position, trailing, out hit);
            if (key.Text.Length > 0) return Format!.HitTestTextPosition(key.Text, Width, 100000, position, trailing, out hit);
            hit = new TextHitTestResult { CaretX = Alignment == TextAlignment.Right ? Width : Alignment == TextAlignment.Center ? Width / 2 : 0 };
            return true;
        }
        internal bool Hit(float x, float y, out TextHitTestResult hit) =>
            spacing is not null ? spacing.Hit(x, y, out hit) : Format!.HitTestPoint(key.Text, Width, 100000, x, y, out hit);
        internal bool Row(uint position, bool backward, out TextLineMetrics row) =>
            spacing is not null ? spacing.Row(position, backward, out row) : Format!.GetLineMetrics(key.Text, Width, 100000, position, backward, out row);
        internal bool Range(uint start, uint length, out TextRangeMetrics range) =>
            spacing is not null ? spacing.Range(start, length, out range) : Format!.HitTestTextRange(key.Text, Width, 100000, start, length, out range);
        internal TextRangeMetrics[]? Selection(int start, int length)
        {
            if (spacing is not null) return spacing.Selection(start, length);
            _selectionParagraph ??= NativeTextParagraph.TryCreate([
                new(key.Text, key.Family, key.Size, key.Weight, key.Style, Colors.Black)],
                key.Family, key.Size, Width, 1, key.Alignment, FlowDirection.LeftToRight,
                naturalDirection: true, noWrap: key.NoWrap);
            if (_selectionParagraph is null) return null;
            var result = new List<TextRangeMetrics>();
            foreach (var line in _selectionParagraph.Lines)
            {
                var metrics = line.Metrics.Line;
                if (start >= metrics.TextPosition + metrics.Length || start + length <= metrics.TextPosition) continue;
                // Paragraph selection supplies disjoint bidi fragments. Use the
                // format's row pitch, which is also used to paint glyphs; fallback
                // emoji fonts can have a taller natural paragraph line box.
                if (!Row(metrics.TextPosition, false, out var painted)) return null;
                foreach (var rectangle in _selectionParagraph.Selection(line.Index, start, length))
                    result.Add(rectangle with { Y = painted.Y, Height = painted.Height });
            }
            return result.ToArray();
        }
        public void Dispose() { spacing?.Dispose(); _selectionParagraph?.Dispose(); Format?.Dispose(); }
    }
}
