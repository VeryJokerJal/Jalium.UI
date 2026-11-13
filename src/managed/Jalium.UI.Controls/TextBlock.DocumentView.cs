using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal bool DocumentDisplaySuppressed { get; set; }
    internal bool IsDocumentParagraph { get; private set; }
    // Borrow font definitions and feature values from the original document's
    // CSS scope, rather than treating the proxy's copied font as a native override.
    internal DependencyObject? DocumentFontSource { get; set; }
    internal virtual DependencyObject? DocumentDecorationSource => null;

    private void DrawDocumentOwnerDecorations(DrawingContext context, in TextLayoutLine line,
        double x, double y, double width, double totalInlineSize, bool first, bool last,
        double inlineSizeBefore, double inlineSizeAfter)
    {
        if (DocumentDecorationSource is not { } source || width <= 0) return;
        var lines = CssTextDecorationProperties.Line(source);
        if (lines == CssTextDecorationLine.None) return;
        var brush = CssTextDecorationProperties.Color(source) ?? CssUsedForeground.Get(source) ?? Foreground;
        if (brush is null) return;
        using var clip = PushDocumentWritingToolsDecorations(context, line.StartIndex, line.Length);
        CssTextDecorationPainter.Draw(context, source, this, brush, lines,
            x, x + width, y, y + line.Baseline, FontSize, y + line.GlyphHeight,
            totalInlineSize, first, last, inlineSizeBefore, inlineSizeAfter);
    }
    private (CssClampMarker Marker, Rect Bounds, Point Origin, double Baseline, double? ClipLeft, double? ClipRight)? _documentSharedMarker;
    internal Rect? DocumentContentsMarkerBounds => IsDocumentParagraph && HasCssInlineFragments ? _cssClampHitRect : null;

    private void PaintDocumentSharedMarker(DrawingContext context)
    {
        if (IsDocumentParagraph && !IsDocumentBoxContent && HasCssInlineFragments && _documentSharedMarker is { } marker)
            marker.Marker.Draw(context, marker.Origin.X, marker.Origin.Y + marker.Baseline,
                marker.ClipLeft, marker.ClipRight);
    }
    internal void PresentDocumentContentsContext(DependencyObject source, FlowDocument document, Brush? defaultForeground = null)
    {
        DocumentFontSource = source;
        FontFamily = (FontFamily)source.GetValue(TextElement.FontFamilyProperty)!;
        FontSize = (double)source.GetValue(TextElement.FontSizeProperty)!;
        FontWeight = (FontWeight)source.GetValue(TextElement.FontWeightProperty)!;
        FontStyle = (FontStyle)source.GetValue(TextElement.FontStyleProperty)!;
        FontStretch = (FontStretch)source.GetValue(TextElement.FontStretchProperty)!;
        CopyCssFontState(source, this);
        Foreground = source.GetValue(TextElement.ForegroundProperty) as Brush ?? defaultForeground;
        FlowDirection = (FlowDirection)source.GetValue(FrameworkElement.FlowDirectionProperty)!;
        var height = (double)source.GetValue(Block.LineHeightProperty)!;
        if (!double.IsFinite(height)) height = document.LineHeight;
        if (CssComputedLineHeightValue.HasCssSource(source))
        {
            ClearValue(LineHeightProperty);
            SetLayerValue(LineHeightProperty, height, LayerValueSource.StyleSetter);
            SetLayerValue(CssComputedLineHeightValue.Property, CssComputedLineHeightValue.Inherited(CssNode.Get(source)), LayerValueSource.StyleSetter);
        }
        else LineHeight = height;
        if (source.HasLocalOrAnimatedValue(Block.TextAlignmentProperty)) TextAlignment = (TextAlignment)source.GetValue(Block.TextAlignmentProperty)!;
        else SetLayerValue(TextAlignmentProperty, source.GetValue(Block.TextAlignmentProperty), LayerValueSource.StyleSetter);
        foreach (var property in DocumentTextProperties())
            if (HasAuthoredFlowValue(source, property)) SetValue(property, source.GetValue(property));
        if (source.GetValueSourceInternal(CssFlowProperties.TextIndentProperty).BaseValueSource == BaseValueSource.Default)
            SetValue(CssFlowProperties.TextIndentProperty, new CssTextIndent(CssLayoutLength.Px(source is Paragraph paragraph ? paragraph.TextIndent : 0), false, false));
    }
    internal CssClampParagraphLimit? DocumentClampLimit { get; set; }
    // A document view borrows the collection, including the computed styles of its
    // original nodes. It never adds, removes, reparents or clones an inline.
    internal void PresentDocumentParagraph(Paragraph paragraph, FlowDocument document, Brush? defaultForeground = null)
    {
        DocumentFontSource = paragraph;
        IsDocumentParagraph = true;
        _inlines = paragraph.Inlines;
        OnInlinesChanged();
        FontFamily = paragraph.FontFamily;
        FontSize = paragraph.FontSize;
        FontWeight = paragraph.FontWeight;
        FontStyle = paragraph.FontStyle;
        FontStretch = paragraph.FontStretch;
        CopyCssFontState(paragraph, this);
        Foreground = paragraph.Foreground ?? defaultForeground;
        FlowDirection = paragraph.FlowDirection;
        PresentDocumentAlignment(paragraph, document);
        PresentDocumentLineHeight(paragraph, document);
        TextDecorations = paragraph.TextDecorations ?? [];
        // The proxy's native fallback is not an authored override. CSS white-space
        // and overflow-wrap still need to control line breaks and intrinsic widths.
        ClearValue(TextWrappingProperty);
        SetLayerValue(TextWrappingProperty, TextWrapping.Wrap, LayerValueSource.StyleSetter);
        foreach (var property in DocumentTextProperties())
        {
            if (HasAuthoredFlowValue(paragraph, property))
                SetValue(property, paragraph.GetValue(property));
            else ClearValue(property);
        }
        if (paragraph.GetValueSourceInternal(CssFlowProperties.TextIndentProperty).BaseValueSource == BaseValueSource.Default)
            SetValue(CssFlowProperties.TextIndentProperty,
                new CssTextIndent(CssLayoutLength.Px(paragraph.TextIndent), false, false));
    }

    private void PresentDocumentLineHeight(Paragraph paragraph, FlowDocument document)
    {
        var height = double.IsFinite(paragraph.LineHeight) ? paragraph.LineHeight : document.LineHeight;
        if (CssComputedLineHeightValue.HasCssSource(paragraph))
        {
            ClearValue(LineHeightProperty);
            SetLayerValue(LineHeightProperty, height, LayerValueSource.StyleSetter);
            SetLayerValue(CssComputedLineHeightValue.Property, CssComputedLineHeightValue.Inherited(CssNode.Get(paragraph)), LayerValueSource.StyleSetter);
        }
        else
        {
            ClearLayerValue(CssComputedLineHeightValue.Property, LayerValueSource.StyleSetter);
            LineHeight = height;
        }
        var source = paragraph.GetValueSourceInternal(Block.LineStackingStrategyProperty).BaseValueSource;
        if (source is not BaseValueSource.Default and not BaseValueSource.Inherited)
            LineStackingStrategy = paragraph.LineStackingStrategy;
        else
        {
            ClearValue(LineStackingStrategyProperty);
            SetLayerValue(LineStackingStrategyProperty, paragraph.LineStackingStrategy, LayerValueSource.StyleSetter);
        }
    }

    private void PresentDocumentAlignment(Paragraph paragraph, FlowDocument document)
    {
        DependencyObject owner = document; var property = FlowDocument.TextAlignmentProperty;
        for (DependencyObject? current = paragraph; current is FrameworkContentElement content; current = content.Parent)
        {
            var candidate = current is TableCell ? TableCell.TextAlignmentProperty : Block.TextAlignmentProperty;
            if (current.GetValueSourceInternal(candidate).BaseValueSource is BaseValueSource.Default or BaseValueSource.Inherited) continue;
            owner = current; property = candidate; break;
        }
        static bool HasAlignmentCss(DependencyObject source) => CssNode.Get(source).CssRuntimeState?.Applied is { } applied &&
            (applied.ContainsKey(CssFlowProperties.TextAlignProperty) || applied.ContainsKey(CssFlowProperties.TextAlignLastProperty));
        var authoredCss = HasAlignmentCss(document);
        for (DependencyObject? current = paragraph; current is FrameworkContentElement content; current = content.Parent)
            authoredCss |= HasAlignmentCss(current);
        var cssMirror = CssNode.Get(owner).CssRuntimeState?.Applied?.ContainsKey(property) == true;
        var nativeWins = !authoredCss || owner.HasLocalOrAnimatedValue(property) || !cssMirror && ReferenceEquals(owner, paragraph) &&
            paragraph.GetValueSourceInternal(CssFlowProperties.TextAlignProperty).BaseValueSource is BaseValueSource.Default or BaseValueSource.Inherited;
        ClearLayerValue(TextAlignmentProperty, LayerValueSource.StyleSetter);
        if (nativeWins) SetValue(TextAlignmentProperty, owner.GetValue(property));
        else
        {
            // A proxy's fallback is not an authored native override. Keeping it
            // below CSS lets inherited start/end and last-line alignment survive.
            ClearValue(TextAlignmentProperty);
            SetLayerValue(TextAlignmentProperty, owner.GetValue(property), LayerValueSource.StyleSetter);
        }
    }

    private static int s_documentPropertiesVersion = -1;
    private static DependencyProperty[] s_documentProperties = [];
    private static DependencyProperty[] DocumentTextProperties()
    {
        if (s_documentPropertiesVersion == CssPropertyRegistry.Version) return s_documentProperties;
        s_documentProperties = CssPropertyRegistry.GetInheritedStorageProperties()
            .Where(p => p.OwnerType == typeof(CssFlowProperties) ||
                p.OwnerType == typeof(CssTextDecorationProperties) || p.OwnerType == typeof(CssTextShadowProperties) ||
                p == CssLineClampProperties.EllipsisProperty)
            .Distinct().ToArray();
        s_documentPropertiesVersion = CssPropertyRegistry.Version;
        return s_documentProperties;
    }

    internal readonly record struct DocumentLine(int Start, int End, double Top, double Height);

    internal object? DocumentSharedFlow => HasCssInlineFragments ? _cssInlineParagraph : null;
    internal object? DocumentSharedLine(int start, int end) => this is InlineSourceText && InitialScope(this) is { Fragmented: false, OriginLine: { } initialLine }
        ? initialLine : HasCssInlineBoxes
        ? _inlineSourceTexts.FirstOrDefault(text=>start>=text.SourceOffset && end<=text.SourceOffset+text.Text.Length) is {} source
            ? source.DocumentSharedLine(start-source.SourceOffset,end-source.SourceOffset):null
        : HasCssInlineFragments
        ? _cssInlineParagraph?.Lines.FirstOrDefault(line => line.Sources.Any(source =>
            ReferenceEquals(source.Element, _cssInlineLayoutElement) && source.Start <= start && source.End >= end))
        : null;
    internal bool IsDocumentBoxContent { get; set; }

    // A drop initial occupies several lines visually but belongs to the first
    // logical line. Selection keeps the full glyph box; line navigation and
    // document pagination use the ordinary containing line instead.
    internal IReadOnlyList<Rect> DocumentTextLineBounds(int start, int length)
    {
        if (HasCssInlineBoxes) return InlineSourceBounds(start, length, lineBounds: true);
        var bounds = DocumentTextBounds(start, length);
        if (this is not InlineSourceText || InitialScope(this) is not { OriginOwner: { } owner, OriginLine: { } line } scope ||
            owner.TransformToVisual(this) is not { } transform) return bounds;
        if (scope.Fragmented && _cssInlineParagraph is { } paragraph)
        {
            var lines = paragraph.Lines.Where(candidate => candidate.Sources.Any(source =>
                ReferenceEquals(source.Element, _cssInlineLayoutElement) && source.End > start && source.Start < start + length)).ToArray();
            return bounds.Select((rect, index) =>
            {
                var actual = lines.Length > 0 ? lines[Math.Min(index, lines.Length - 1)] : line;
                return new Rect(rect.X, transform.Transform(new Point(0, actual.Top + scope.OriginInset)).Y, rect.Width, actual.Height);
            }).ToArray();
        }
        var top = transform.Transform(new Point(0, line.Top + scope.OriginInset)).Y;
        return bounds.Select(rect => new Rect(rect.X, top, rect.Width, line.Height)).ToArray();
    }

    internal IReadOnlyList<DocumentLine> GetDocumentLines(double width)
    {
        if (DocumentDisplaySuppressed) return [];
        if (HasCssInlineBoxes) return InlineSourceLines();
        EnsureLayout(GetLayoutConstraintWidth(width));
        var result = new List<DocumentLine>(_layoutLines.Count);
        var sharedRows = new Dictionary<(CssInlineBidiNative,uint),int>();
        // Document fragmentation uses typographic line boxes. Optical centering
        // moves the ink inside those boxes; it must not add another leading gap
        // or make a 280px line consume 410px after its proxy has been arranged.
        var y = GetTextInsets().Top;
        foreach (var line in _layoutLines)
        {
            AddSharedBidiDocumentRow(result,sharedRows,new(line.StartIndex,line.StartIndex+line.Length,TextLineTop(line,y),line.Height),
                SharedShape(line) as CssInlineBidiPart);
            y += line.Height;
        }
        return result;
    }

    private static void AddSharedBidiDocumentRow(List<DocumentLine> rows,Dictionary<(CssInlineBidiNative,uint),int> groups,
        DocumentLine next,CssInlineBidiPart? part)
    {
        // Multiple visual runs on a native row are one document line. Native
        // row identity also keeps separate columns/paragraphs at the same Y.
        if(part is null){rows.Add(next);return;}
        var key=(part.Native,part.Row);
        if(!groups.TryGetValue(key,out int index)){groups.Add(key,rows.Count);rows.Add(next);return;}
        var previous=rows[index];double top=Math.Min(previous.Top,next.Top);
        rows[index]=new(Math.Min(previous.Start,next.Start),Math.Max(previous.End,next.End),top,
            Math.Max(previous.Top+previous.Height,next.Top+next.Height)-top);
    }

    internal int DocumentCharacterFromPoint(Point point) => GetCharacterIndexFromPosition(point);

    internal IReadOnlyList<Rect> DocumentTextBounds(int start, int length)
    {
        if (DocumentDisplaySuppressed || CssRubyVisibility.Suppressed(this) || CssLineClampLayout.SuppressedInTree(this)) return [];
        if (length > 0)
        {
            var boundaryEnd = GraphemeClusters.Snap(_displayText,(int)Math.Clamp((long)start+length,0,_displayText.Length),forward:true);
            start = GraphemeClusters.Snap(_displayText,Math.Clamp(start,0,_displayText.Length),forward:false);
            length = Math.Max(0,boundaryEnd-start);
        }
        if (HasCssInlineBoxes) return InlineSourceBounds(start,length);
        EnsureLayout(GetLayoutConstraintWidth(TextContentSize.Width));
        var result = new List<Rect>();
        void Append(Rect logical, double top, double height)
        {
            var rect = TextWritingRect(logical, top, height);
            rect.Offset(TextContentOffset.X, TextContentOffset.Y);
            result.Add(InitialLetterSelectionRect(rect));
        }
        var end = start + length;
        var y = GetVerticalContentOffset(GetLineHeight());
        foreach (var line in _layoutLines)
        {
            var from = Math.Max(start, line.StartIndex);
            var to = Math.Min(end, line.StartIndex + line.Length);
            if ((to > from || length == 0 && start >= line.StartIndex && start <= line.StartIndex + line.Length) && HasDisplayText(from, to-from))
            {
                var origin = GetLineOriginX(line, GetContentWidth(TextContentSize.Width));
                if (length > 0 && TryCssBidiLine(line, out var cssBidi))
                {
                    foreach (var rect in cssBidi.Selection(from - line.StartIndex, to - from))
                        Append(new(origin + rect.X, TextLineTop(line, y), Math.Max(1, rect.Width), line.Height),
                            TextLineTop(line, y), line.Height);
                    y += line.Height;
                    continue;
                }
                if (length > 0 && SharedShape(line) is { } shared)
                {
                    foreach (var rect in shared.Selection(from, to - from))
                        Append(new(origin + rect.X, TextLineTop(line, y), Math.Max(1, rect.Width), line.Height),
                            TextLineTop(line, y), line.Height);
                    y += line.Height;
                    continue;
                }
                var left = GetInlineBoundaryX(line, origin, from);
                var right = GetInlineBoundaryX(line, origin, to);
                Append(new(Math.Min(left, right), TextLineTop(line, y), Math.Max(1, Math.Abs(right - left)), line.Height),
                    TextLineTop(line, y), line.Height);
                if (length == 0 && !HasSharedBidiSourceParts) break;
            }
            y += line.Height;
        }
        return result;
    }
}
