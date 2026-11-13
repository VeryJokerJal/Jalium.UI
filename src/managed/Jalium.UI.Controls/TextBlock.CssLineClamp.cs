using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private int _cssClampStamp;
    private CssClampMarker? _cssClampMarker;
    private int _cssClampEnd = -1;

    internal IEnumerable<CssClampLine> CssClampLines()
    {
        if (_displayText.Length == 0) yield break;
        var y = TextContentOffset.Y + GetVerticalContentOffset(GetLineHeight());
        for (var i = 0; i < _layoutLines.Count; i++)
        {
            var line = _layoutLines[i];
            yield return new(new(this), this, i, y, line.Height, line.StartIndex + line.Length);
            y += line.Height;
        }
    }

    private void PrepareCssClamp()
    {
        _cssClampStamp = CssLineClampLayout.PassStamp;
        CssLineClampLayout.Register(this);
        _cssClampMarker = null; _cssClampEnd = -1;
        if (CssLineClampLayout.Limit(this) is not { Count: > 0 } limit) return;
        _cssClampEnd = limit.End ?? -1;
        var definition = (CssBlockEllipsis)GetValue(CssLineClampProperties.EllipsisProperty)!;
        if (definition.HasContent) _cssClampMarker = new(this, definition.Value);
    }

    private int CssClampSourceEnd
    {
        get
        {
            var limit = CssLineClampLayout.Limit(this);
            if (limit is { Count: 0 }) return 0;
            return limit?.End is { } end &&
                (CssLineClampLayout.UseEndpoint || DocumentClampLimit is not null && !CssLineClampLayout.HasDocumentPass)
                ? Math.Clamp(end, 0, _visualText.Length) : _visualText.Length;
        }
    }

    private void ApplyCssClamp(double contentWidth)
    {
        if (CssLineClampLayout.Limit(this) is not { } limit) return;
        if (limit.Count == 0) { _layoutLines.Clear(); return; }
        if (_layoutLines.Count > limit.Count) _layoutLines.RemoveRange(limit.Count, _layoutLines.Count - limit.Count);
        if (_layoutLines.Count == 0) return;
        var last = _layoutLines[^1];
        var end = last.StartIndex + last.Length;
        var available = GetContentWidth(contentWidth) - ResolveTextIndent(last.IndentApplies, GetContentWidth(contentWidth)) -
            last.StartPadding - last.EndPadding;
        var prefix = last.Length;
        var fitted = limit.End == end && !CssLineClampLayout.Stable(this) &&
            (CssLineClampLayout.UseEndpoint || DocumentClampLimit is not null && !CssLineClampLayout.HasDocumentPass);
        if (_cssClampMarker is { } marker && !fitted)
        {
            var bounds = GraphemeClusters.GetBoundaries(_visualText);
            prefix = 0;
            foreach (var boundary in bounds)
            {
                if (boundary <= last.StartIndex || boundary > end) continue;
                var naturalBreak = IsBreakableWhitespace(_visualText[boundary - 1]) &&
                    (boundary == _visualText.Length || _visualText[boundary] is not ('\u00a0' or '\u202f' or '\u2060' or '\ufeff')) ||
                    boundary == end && (end == _visualText.Length || IsBreakableWhitespace(_visualText[end]) || _visualText[end] is '\n' or '\r') ||
                    IsStandardLineBreakOpportunity(boundary, last.StartIndex, _visualText.Length);
                if (!naturalBreak) continue;
                var visible = boundary;
                while (visible > last.StartIndex && IsBreakableWhitespace(_visualText[visible - 1])) visible--;
                var width = MeasureInlineRange(last.StartIndex, visible - last.StartIndex, includeTrailingWhitespace: true,
                    tabOrigin: ResolveTextIndent(last.IndentApplies, GetContentWidth(contentWidth)) + GetLineLeftPadding(last));
                if (width + marker.Advance <= available + .000001) prefix = visible - last.StartIndex;
            }
        }
        var advance = MeasureInlineRange(last.StartIndex, prefix, includeTrailingWhitespace: true,
            tabOrigin: ResolveTextIndent(last.IndentApplies, GetContentWidth(contentWidth)) + GetLineLeftPadding(last));
        var metrics = GetInlineLineMetrics(last.StartIndex, prefix);
        PaintWrapLine? clampedPaintWrap = last.PaintWrap;
        if (clampedPaintWrap is { } pw && prefix < last.Length)
        {
            var pwEnd = pw.First;
            while (pwEnd < pw.End && pw.Units[pwEnd].SourceEnd <= last.StartIndex + prefix) pwEnd++;
            clampedPaintWrap = pwEnd > pw.First ? new(pw.Units, pw.First, pwEnd) : null;
        }
        _layoutLines[^1] = new(last.StartIndex, prefix, advance, false, metrics.Height, metrics.Baseline,
            metrics.GlyphHeight, canJustify: false, indentApplies: last.IndentApplies,
            paintWrap: clampedPaintWrap,
            startPadding: last.StartPadding, endPadding: last.EndPadding);
        ClearNativeBidiLines();
        _cssClampEnd = last.StartIndex + prefix; limit.End = _cssClampEnd;
    }

    private double CssClampAdvance(in TextLayoutLine line) => _cssClampMarker is { } marker &&
        line.StartIndex + line.Length == _cssClampEnd ? marker.Advance : 0;

    private bool TryCssClampMarkerHit(Point local, out int end)
    {
        end = 0;
        if (HasCssInlineFragments)
        {
            if (_cssClampHitRect?.Contains(local) != true) return false;
            end = _cssClampHitEnd; return true;
        }
        if (_cssClampMarker is not { } marker || _layoutLines.Count == 0) return false;
        var line = _layoutLines[^1];
        var x = GetLineOriginX(line, GetContentWidth(TextContentSize.Width));
        x = UsedTextFlowDirection == FlowDirection.RightToLeft ? x - marker.Advance : GetInlineBoundaryX(line, x, line.StartIndex + line.Length);
        var y = GetVerticalContentOffset(GetLineHeight()) + _layoutLines.Take(_layoutLines.Count - 1).Sum(static line => line.Height);
        if (!new Rect(x, y, marker.Advance, line.Height).Contains(local)) return false;
        end = _cssClampEnd; return true;
    }

    private bool DrawCssClampedLine(DrawingContext context, in TextLayoutLine line, double x, double y)
    {
        if (_cssClampMarker is not { } marker || line.StartIndex + line.Length != _cssClampEnd) return false;
        if (!DrawCssBidiLine(context, line, x, y) && !DrawNativeBidiLine(context, line, x, y))
        {
            if (TryGetJustifiedLine(line, GetContentWidth(TextContentSize.Width), out var justified)) DrawJustifiedLine(context, line, x, y, justified);
            else DrawStyledInlineLine(context, line, x, y);
        }
        var markerX = UsedTextFlowDirection == FlowDirection.RightToLeft ? x - marker.Advance : GetInlineBoundaryX(line, x, line.StartIndex + line.Length);
        marker.Draw(context, markerX, y + line.Baseline); return true;
    }

    internal sealed class CssClampMarker
    {
        private readonly TextBlock _text;
        internal TextBlock TextControl => _text;
        internal double Advance { get; }
        internal double Baseline { get; }
        internal CssClampMarker(FrameworkElement source, string value)
        {
            _text = new TextBlock { _wordSpacingStyleSource = source, Text = value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') };
            foreach (var property in new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty,
                TextElement.FontStyleProperty, TextElement.FontStretchProperty, TextElement.ForegroundProperty, FrameworkElement.FlowDirectionProperty,
                LineHeightProperty })
                _text.SetLayerValue(property, source.GetValue(property), LayerValueSource.StyleSetter);
            foreach (var property in CssPropertyRegistry.GetInheritedStorageProperties())
                _text.SetLayerValue(property, source.GetValue(property), LayerValueSource.StyleSetter);
            _text.SetRootDpi(source.DpiScale);
            _text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            _text.Arrange(new(default(Point), _text.DesiredSize));
            Advance = _text._layoutLines.Select(static line => line.Width).DefaultIfEmpty(0).Max();
            Baseline = _text.GetFirstBaselineOffset(_text.DesiredSize.Height);
        }
        internal void Draw(DrawingContext context, double x, double baseline, double? clipLeft = null, double? clipRight = null)
        {
            var clipped = clipLeft is { } left && clipRight is { } right && double.IsFinite(left) && double.IsFinite(right);
            if (clipped)
            {
                var clip = _text.GetRenderClip().Rect;
                context.PushClip(new RectangleGeometry(new Rect(clipLeft!.Value, baseline - Baseline + clip.Y,
                    Math.Max(0, clipRight!.Value - clipLeft.Value), clip.Height)));
            }
            context.PushTransform(new TranslateTransform(x, baseline - Baseline));
            try { _text.Render(context); } finally { context.Pop(); if (clipped) context.Pop(); }
        }
    }
}
