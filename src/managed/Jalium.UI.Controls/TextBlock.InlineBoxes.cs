using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Documents;
using Jalium.UI.Styling;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
public partial class TextBlock
{
    // Only view-owned visuals participate in layout; the authored inline tree is
    // still the sole source of text, inheritance, selectors and pointer offsets.
    private sealed class InlineSourceBox(Inline source) : Border, ICssStyledBox
    {
        internal readonly List<UIElement> Items = [];
        private Border? _borderPainter;
        public DependencyObject StyleSource => source;
        protected override bool SupportsCssInlineContentLayout => source is not InlineUIContainer;
        protected override int VisualChildrenCount => Items.Count;

        protected override Visual? GetVisualChild(int index) => Items[index];
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
        internal void Append(UIElement child)
        {
            Items.Add(child);
            AddVisualChild(child);
        }

        internal UIElement[] DetachContents()
        {
            var children = Items.ToArray();
            foreach (var child in children) RemoveVisualChild(child);
            Items.Clear();
            return children;
        }

        internal void Release()
        {
            CssGeneratedContent.UnbindProjectedHost(source, this);
            if (CssMultiColumnLayout.Try(this, out var columns))
                columns!.Clear();
            CssLineClampLayout.For(this).Clear();
            CssTextFlowLayout.ReleaseInlineTree(this, this);
            foreach (var child in Items.ToArray())
            {
                if (child is InlineSourceBox box)
                    box.Release();
                if (child is TextBlock text)
                    text.InvalidateCaches();
                RemoveVisualChild(child);
            }

            Items.Clear();
        }

        protected override void OnPostRender(DrawingContext context)
        {
            if (CssBorderPaintProperties.Get(this) is null)
            {
                base.OnPostRender(context);
                return;
            }

            if (!CssInlineBoxPainter.Border(this, context, ref _borderPainter))
                CssBorderAdornment.Draw(this, context, ref _borderPainter);
        }
    }

    private sealed class InlineSourceText(TextBlock owner, int offset) : TextBlock, ICssAnonymousTextBox
    {
        protected override bool ParticipatesInRenderCache => SourceOwner.DocumentWritingToolsOwner is null && !SourceOwner.IsTextSelectionEnabled;
        internal readonly TextBlock SourceOwner = owner;
        internal readonly int SourceOffset = offset;
        internal DependencyObject? ProjectedStyleSource;
        internal CssFirstLine.Context? FirstLineContext;
        internal double FirstLineWidth;
        FrameworkElement ICssAnonymousBox.Owner => this;

        double ICssAnonymousBox.BaselineOffset(double height, bool last) => last ? GetLastBaselineOffset(height) : GetFirstBaselineOffset(height);
        protected override bool SupportsCssContentLayout => false;
        protected override bool SupportsCssInlineContentLayout => true;

        protected override AutomationPeer? OnCreateAutomationPeer() => null;
        internal override DependencyObject? DocumentDecorationSource => SourceOwner.DocumentDecorationSource;
        protected override Size MeasureOverride(Size availableSize) => TryCssInlineAllocation(out var size) ? size : base.MeasureOverride(availableSize);
        protected override void OnRender(DrawingContext context)
        {
            var from = Math.Clamp(SourceOwner._selectionStart - SourceOffset, 0, Text.Length);
            var to = Math.Clamp(SourceOwner._selectionStart + SourceOwner._selectionLength - SourceOffset, 0, Text.Length);
            _selectionStart = from;
            _selectionLength = Math.Max(0, to - from);
            base.OnRender(context);
        }
    }

    private readonly List<InlineSourceText> _inlineSourceTexts = [];
    private readonly List<(InlineSourceBox Box, int Offset)> _inlineSourceObjects = [];
    private readonly List<IDisposable> _inlineSourceConsumers = [];
    private readonly Dictionary<(CssNode Origin, DependencyObject Parent), Run> _firstLetterProjections = [];
    private Run? _inlineImplicitSource;
    private bool _inlineBoxesDirty = true;
    internal InlineCollection? CssOriginalInlines => _inlines;
    internal bool HasCssInlineBoxes => _inlines is not null && (_inlines.Any(NeedsInlineSourceBox) || _inlines.Count > 0 && IsFlexOrGridInlineContainer) || HasCssFirstLetter || HasCssFirstLine || HasInitialLetterTextScope;
    private bool IsFlexOrGridInlineContainer => CssDisplayMode is CssDisplayMode.Flex or CssDisplayMode.Grid ||
        CssContentsLayout.IsContents(this) && CssContentsLayout.BoxParent(this) is FrameworkElement { CssDisplayMode: CssDisplayMode.Flex or CssDisplayMode.Grid };

    internal static bool InlineNeedsBox(Inline source) => NeedsInlineSourceBox(source);
    private static bool ContainsHostedUI(Inline source) => source is InlineUIContainer { Child: not null } ||
        source is Span span && span.Inlines.Any(ContainsHostedUI);
    private static bool NeedsInlineSourceBox(Inline source)
    {
        if (CssDisplayProperties.SuppressesBoxTree(source))
            return false;
        if (source is InlineUIContainer || CssGeneratedContent.HasProjectedTarget(source) || CssReplacedContent.IsActive(source))
            return true;
        if (CssContainmentProperties.GetContain(source) != CssContainFlags.None ||
            CssContainmentProperties.GetContentVisibility(source) != CssContentVisibility.Visible) return true;
        var authored = source.GetEffectiveValueLayer(CssDisplayProperties.SpecificationProperty) is not null;
        if (authored || CssNode.Get(source).CssLayout is not null ||
            (CssFloatSide)source.GetValue(CssFloatProperties.FloatProperty)! != CssFloatSide.None ||
            source.GetValue(CssBorderPaintProperties.ValueProperty) is not null || source.GetValue(CssBorderRadiusProperties.ValueProperty) is not null)
            return true;
        return source is Span span && span.Inlines.Any(NeedsInlineSourceBox);
    }

    private void EnsureInlineSourceBoxes()
    {
        if (_cssTextBody is null)
            return;
        if (!HasCssInlineBoxes)
        {
            ClearInlineSourceBoxes();
            _firstLetterProjections.Clear();
            ReleaseFirstLineContexts();
            _inlineImplicitSource = null;
            return;
        }

        if (!_inlineBoxesDirty && _cssTextBody.InlineChildren.Count > 0)
            return;
        ClearInlineSourceBoxes();
        var offset = 0;
        var firstLetter = GetFirstLetterPlan();
        var firstLine = HasCssFirstLine;
        var isFlexOrGrid = IsFlexOrGridInlineContainer;
        var usedProjections = new HashSet<(CssNode Origin, DependencyObject Parent)>();
        var usedLineContexts = new HashSet<CssFirstLine.Context>();
        if (_inlines is not null)
            foreach (var source in _inlines)
                foreach (var child in Build(source)) _cssTextBody.AppendInline(child);
        else
        {
            var implicitParent = DocumentFontSource ?? this;
            if (_inlineImplicitSource is null || !ReferenceEquals(_inlineImplicitSource.Parent, implicitParent))
            {
                _inlineImplicitSource = new Run(_displayText);
                _inlineImplicitSource.SetContentParent(implicitParent);
            }
            else _inlineImplicitSource.Text = _displayText;
            var implicitSource = _inlineImplicitSource;
            foreach (var child in Build(implicitSource, implicitSource: true)) _cssTextBody.AppendInline(child);
        }
        CssContentsLayout.Observe(CssDisplayMode.Contents);
        _cssTextBody.SetValue(CssDisplayProperties.SpecificationProperty, new CssDisplaySpecification(CssDisplayMode.Contents));
        _cssTextBody.SetValue(CssDisplayLayout.ModeProperty, CssDisplayMode.Contents);
        foreach (var key in _firstLetterProjections.Keys.Where(key => !usedProjections.Contains(key)).ToArray())
            _firstLetterProjections.Remove(key);
        ReleaseFirstLineContexts(usedLineContexts);
        _inlineBoxesDirty = false;
        UIElement[] Build(Inline source, bool implicitSource = false)
        {
            var box = new InlineSourceBox(source)
            {
                IsHitTestVisible = ContainsHostedUI(source)
            };
            MirrorInlineSourceStyle(source, box);
            var unboxed = (firstLetter is not null || firstLine || isFlexOrGrid) && source is Run && !NeedsInlineSourceBox(source) &&
                !CssDisplayProperties.SuppressesBoxTree(source) &&
                source.Background is null && source.TextDecorations is not { Count: > 0 } &&
                CssNode.Get(source).CssRuntimeState?.Applied?.Keys.Any(property => !property.GetMetadata(source.GetType()).Inherits) != true;
            box.SetRootDpi(DpiScale);
            if (!unboxed)
            {
                _inlineSourceConsumers.Add(CssDocumentConsumers.Register(source, box));
                if (!implicitSource && CssGeneratedContent.HasProjectedTarget(source))
                    CssGeneratedContent.BindProjectedHost(source, box, () =>
                    {
                        _inlineBoxesDirty = true; InvalidateCaches(); InvalidateMeasure(); InvalidateVisual();
                    });
            }
            if (source is Span span)
                foreach (var child in span.Inlines)
                    foreach (var visual in Build(child)) box.Append(visual);
            else if (source is InlineUIContainer uiContainer)
            {
                _inlineSourceObjects.Add((box, offset));
                offset += 1;
                if (uiContainer.Child is { } uiChild)
                {
                    if (uiChild.VisualParent is Visual oldParent && !ReferenceEquals(oldParent, box))
                        uiChild.DetachFromVisualParent();
                    box.Append(uiChild);
                }
            }
            else
            {
                var value = source is Run run ? run.Text : source is LineBreak ? "\n" : "";
                var start = offset;
                offset += value.Length;
                if (isFlexOrGrid && unboxed && ReferenceEquals(source.Parent, DocumentFontSource ?? this) &&
                    value.All(static c => c is ' ' or '\t' or '\r' or '\n' or '\f'))
                    return [];
                var lineContext = firstLine ? FirstLineContextFor(implicitSource ? DocumentFontSource ?? this : source) : null;
                if (lineContext is not null) usedLineContexts.Add(lineContext);
                if (_firstLetterRevision != CssFirstLetter.Revision)
                    firstLetter = GetFirstLetterPlan();
                var limit = lineContext is null ? 0 : FirstLineStateFor(lineContext).Limit;
                var stops = new SortedSet<int> { start, offset };
                if (limit > start && limit < offset) stops.Add(limit);
                if (firstLetter is { } range)
                {
                    if (range.Start > start && range.Start < offset) stops.Add(range.Start);
                    if (range.End > start && range.End < offset) stops.Add(range.End);
                }
                var boundaries = stops.ToArray();
                InlineSourceBox? lineBox = null;
                for (var index = 0; index + 1 < boundaries.Length; index++)
                {
                    var from = boundaries[index]; var to = boundaries[index + 1];
                    var onFirstLine = lineContext is not null && from < limit;
                    Inline styleSource = source;
                    InlineSourceBox container = box;
                    if (onFirstLine)
                    {
                        var original = implicitSource ? DocumentFontSource ?? this : source;
                        styleSource = lineContext!.StyleFor(original);
                        if (lineBox is null)
                        {
                            lineBox = new(lineContext.RootStyle) { IsHitTestVisible = false };
                            MirrorInlineSourceStyle(lineContext.RootStyle, lineBox); lineBox.SetRootDpi(DpiScale);
                            // The fictional inline paints once outside all real
                            // descendants, including atoms and decorated spans.
                            lineBox.Background = null; lineBox.Opacity = 1;
                            box.Append(lineBox);
                        }
                        container = lineBox;
                    }
                    DependencyObject parent = onFirstLine ? styleSource : implicitSource ? DocumentFontSource ?? this : source;
                    var letter = value[(from - start)..(to - start)];
                    if (source is Run && firstLetter is { } plan && from >= plan.Start && from < plan.End)
                    foreach (var origin in plan.Origins)
                    {
                        // Retain computed pseudo styles across formatting passes.
                        // Recreating a Run would reschedule its initial font
                        // cascade and restart decorated-text layout indefinitely.
                        var key = (origin, parent);
                        usedProjections.Add(key);
                        if (_firstLetterProjections.TryGetValue(key, out var projected)) CssFirstLetter.Refresh(projected, letter);
                        else
                        {
                            projected = CssFirstLetter.Project(origin, parent, letter, () =>
                            {
                                InvalidateCaches(); InvalidateMeasure(); InvalidateVisual();
                            });
                            _firstLetterProjections.Add(key, projected);
                        }
                        var pseudoBox = new InlineSourceBox(projected) { IsHitTestVisible = false };
                        MirrorInlineSourceStyle(projected, pseudoBox);
                        pseudoBox.SetRootDpi(DpiScale);
                        _inlineSourceConsumers.Add(CssDocumentConsumers.Register(projected, pseudoBox));
                        container.Append(pseudoBox); container = pseudoBox; parent = projected;
                    }
                    var usesProjectedStyle = parent is Inline inline && !ReferenceEquals(inline, source);
                    AppendText(container, parent as Inline ?? styleSource, letter, from, usesProjectedStyle,
                        lineContext);
                }
                if (boundaries.Length == 1) AppendText(box, source, value, start, context: lineContext);
            }

            // A source Run without its own decoration has no formatting frame.
            // Put its split leaves directly in the existing inline context;
            // a display:contents wrapper still incurs intrinsic trial layouts.
            return unboxed ? box.DetachContents() : [box];
        }

        void AppendText(InlineSourceBox box, Inline source, string value, int start, bool projected = false, CssFirstLine.Context? context = null)
        {
            var text = new InlineSourceText(this, start)
            {
                Text = value, IsDocumentBoxContent = true, IsHitTestVisible = false, Focusable = false,
                ProjectedStyleSource = projected ? source : null, FirstLineContext = context,
                FirstLineWidth = context is null ? double.NaN : FirstLineStateFor(context).Width
            };
            CopyInlineTextStyle(source, text);
            text.DocumentFontSource = source;
            if (source is LineBreak) text.SetValue(CssFlowProperties.WhiteSpaceProperty, CssWhiteSpaceCollapse.PreserveBreaks);
            text.CssInlineOuter = true;
            box.Append(text); _inlineSourceTexts.Add(text);
        }
    }

    private void ClearInlineSourceBoxes()
    {
        if (_cssTextBody is null || _cssTextBody.InlineChildren.Count == 0 && _inlineSourceConsumers.Count == 0)
            return;
        foreach (var child in _cssTextBody.InlineChildren.ToArray())
        {
            if (child is InlineSourceBox box)
                box.Release();
            _cssTextBody.RemoveInline(child);
        }

        foreach (var consumer in _inlineSourceConsumers)
            consumer.Dispose();
        _inlineSourceConsumers.Clear();
        _inlineSourceTexts.Clear();
        _inlineSourceObjects.Clear();
        _cssTextBody.ClearValue(CssDisplayProperties.SpecificationProperty);
        _cssTextBody.ClearValue(CssDisplayLayout.ModeProperty);
    }

    private static readonly DependencyProperty[] InlineNativeProperties = [FrameworkElement.WidthProperty, FrameworkElement.HeightProperty, FrameworkElement.MinWidthProperty, FrameworkElement.MinHeightProperty, FrameworkElement.MaxWidthProperty, FrameworkElement.MaxHeightProperty, TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.FontStyleProperty, TextElement.FontStretchProperty, TextElement.ForegroundProperty, TextElement.BackgroundProperty, FrameworkElement.FlowDirectionProperty, FrameworkElement.LanguageProperty];
    private void MirrorInlineSourceStyle(Inline source, InlineSourceBox box)
    {
        var state = CssNode.Get(source).CssRuntimeState;
        var properties = new HashSet<DependencyProperty>(CssPropertyRegistry.GetInheritedStorageProperties());
        properties.UnionWith(InlineNativeProperties);
        if (state?.Applied is { } applied)
            properties.UnionWith(applied.Keys);
        foreach (var property in properties)
        {
            if (property.ReadOnly || property == CssDisplayProperties.SpecificationProperty || property == CssDisplayLayout.ModeProperty || property == CssDisplayLayout.InlineProperty)
                continue;
            var target = property == CssBorderUsedThicknessProperties.ValueProperty ? Border.BorderThicknessProperty : CssDependencyPropertyLookup.Find(box.GetType(), property.Name) ?? property;
            if (target.PropertyType != property.PropertyType)
                continue;
            if (source.HasLocalOrAnimatedValue(property))
                box.SetValue(target, source.GetValue(property));
            else
                box.SetLayerValue(target, source.GetValue(property), state?.Applied?.TryGetValue(property, out var css) == true ? css.Layer : LayerValueSource.StyleSetter);
        }

        if (CssDependencyPropertyLookup.Find(source.GetType(), "BorderBrush") is null && source.GetValue(CssBorderPaintProperties.ValueProperty) is CssBorderPaint paint && state?.Applied?.TryGetValue(CssBorderPaintProperties.ValueProperty, out var borderPaint) == true)
            box.SetLayerValue(Border.BorderBrushProperty, paint.Top, borderPaint.Layer);
        box.CssLayout = CssNode.Get(source).CssLayout?.CloneForLayout();
        var display = CssDisplayProperties.Computed(CssNode.Get(source));
        if (display.Mode == CssDisplayMode.Native)
            display = display with
            {
                Mode = CssDisplayMode.Block
            };
        box.SetValue(CssDisplayProperties.SpecificationProperty, display);
        box.SetValue(CssDisplayLayout.ModeProperty, display.Mode);
        box.SetValue(CssDisplayLayout.InlineProperty, display.Inline);
        if (display.None)
            box.SetLayerValue(UIElement.VisibilityProperty, Visibility.Collapsed, LayerValueSource.CssBase);
        box.SetValue(CssComputedLineHeightValue.Property, InlineSourceLineHeight(source));
        CopyCssFontState(source, box);
    }

    private CssLineHeight InlineSourceLineHeight(Inline source)
    {
        var height = CssComputedLineHeightValue.Inherited(CssNode.Get(source));
        if (!CssComputedLineHeightValue.HasCssSource(source) && height.Kind == CssLineHeightKind.Normal && double.IsFinite(LineHeight))
            height = new(CssLineHeightKind.Pixels, LineHeight);
        return height;
    }

    private void CopyInlineTextStyle(Inline source, InlineSourceText text)
    {
        foreach (var property in CssPropertyRegistry.GetInheritedStorageProperties())
            text.SetLayerValue(property, source.GetValue(property), LayerValueSource.StyleSetter);
        foreach (var property in InlineNativeProperties.Where(p => p.Name is "FontFamily" or "FontSize" or "FontWeight" or "FontStyle" or "FontStretch" or "Foreground" or "FlowDirection" or "Language"))
            text.SetValue(property, source.GetValue(property) ?? (property == ForegroundProperty ? Foreground : null));
        if (source.GetValueSourceInternal(LanguageProperty).BaseValueSource == BaseValueSource.Default &&
            GetValueSourceInternal(LanguageProperty).BaseValueSource != BaseValueSource.Default)
            text.SetValue(LanguageProperty, Language);
        text.SetValue(CssComputedLineHeightValue.Property, InlineSourceLineHeight(source));
        text.ClearValue(TextWrappingProperty);
        text.SetLayerValue(TextWrappingProperty, TextWrapping.Wrap, LayerValueSource.StyleSetter);
        CopyCssFontState(source, text);
    }

    internal static void CopyCssFontState(DependencyObject source, DependencyObject target)
    {
        var sourceNode = CssNode.Get(source);
        var targetState = CssNode.Get(target).CssRuntimeState ??= new();
        var stretch = CssFontStretchValue.Computed(sourceNode);
        targetState.FontStretchPercentage = stretch;
        targetState.FontStyleComputed = CssFontStyleValue.Computed(sourceNode);
        targetState.FontFamilyComputed = CssFontFamilyValue.Computed(sourceNode);
        target.ClearValue(TextElement.FontStretchProperty);
        target.ClearLayerValue(TextElement.FontStretchProperty, LayerValueSource.StyleSetter);
        target.SetLayerValue(TextElement.FontStretchProperty, CssFontStretchValue.Native(stretch), LayerValueSource.CssBase);
        target.SetLayerValue(CssFontStretchValue.PercentageProperty, stretch, LayerValueSource.CssBase);
        if (targetState.FontStyleComputed is { } style)
        {
            target.ClearValue(TextElement.FontStyleProperty);
            target.ClearLayerValue(TextElement.FontStyleProperty, LayerValueSource.StyleSetter);
            target.SetLayerValue(TextElement.FontStyleProperty, style.Native, LayerValueSource.CssBase);
        }
        if (source.GetValue(TextElement.FontFamilyProperty) is FontFamily { IsCssFamily: true } family)
        {
            target.ClearValue(TextElement.FontFamilyProperty);
            target.ClearLayerValue(TextElement.FontFamilyProperty, LayerValueSource.StyleSetter);
            target.SetLayerValue(TextElement.FontFamilyProperty, family, LayerValueSource.CssBase);
        }
    }

    private bool InlineSourceTransform(UIElement text, out Matrix transform, out AutomationVisibility visibility)
    {
        transform = Matrix.Identity;
        visibility = new(text, this);
        if (!text.IsVisible || !GetRenderMatrix().TryInvert(out var inverse))
            return false;
        transform = Matrix.Multiply(text.GetRenderMatrix(), inverse);
        return true;
    }

    private IReadOnlyList<Rect> InlineSourceBounds(int start, int length, bool lineBounds = false)
    {
        var result = new List<(int Source, Rect Bounds)>();
        var end = Math.Min(int.MaxValue, (long)start + length);
        foreach (var text in _inlineSourceTexts)
        {
            var from = Math.Max(start, text.SourceOffset);
            var to = (int)Math.Min(end, text.SourceOffset + text.Text.Length);
            if (length == 0 && !(start >= text.SourceOffset && start < text.SourceOffset + text.Text.Length || start == _displayText.Length && start == text.SourceOffset + text.Text.Length))
                continue;
            if ((length == 0 ? to < from : to <= from) || !InlineSourceTransform(text, out var transform, out var visibility))
                continue;
            foreach (var rect in lineBounds ? text.DocumentTextLineBounds(from - text.SourceOffset, to - from) : text.DocumentTextBounds(from - text.SourceOffset, to - from))
                if (visibility.TryClip(rect, transform, out var bounds))
                    result.Add((from, bounds));
        }
        foreach (var (box, offset) in _inlineSourceObjects)
        {
            if ((length == 0 ? start != offset && !(start == _displayText.Length && start == offset + 1)
                    : start >= offset + 1 || end <= offset) ||
                !InlineSourceTransform(box, out var transform, out var visibility)) continue;
            var rectangle = new Rect(box.RenderSize);
            if (length == 0) rectangle = new Rect(start == offset ? 0 : rectangle.Right, 0, 1, rectangle.Height);
            if (visibility.TryClip(rectangle, transform, out var bounds)) result.Add((offset, bounds));
        }
        return result.OrderBy(item => item.Source).Select(item => item.Bounds).ToArray();
    }

    private bool InlineSourceContains(Point point)
    {
        foreach (var (box, _) in _inlineSourceObjects)
            if (InlineSourceTransform(box, out var transform, out var visibility) && transform.TryInvert(out var inverse))
            {
                var local = inverse.Transform(point);
                if (new Rect(box.RenderSize).Contains(local) && visibility.Contains(local)) return true;
            }
        foreach (var text in _inlineSourceTexts)
        {
            if (!InlineSourceTransform(text, out var transform, out var visibility) || !transform.TryInvert(out var inverse))
                continue;
            var local = inverse.Transform(point);
            foreach (var rect in text.DocumentTextBounds(0, text.Text.Length))
                if (visibility.TryClip(rect, transform, out _) && visibility.TryGetPoint(rect, local, out _))
                    return true;
        }
        return false;
    }

    private int InlineSourceIndex(Point point)
    {
        foreach (var (box, offset) in _inlineSourceObjects)
            if (InlineSourceTransform(box, out var transform, out var visibility) && transform.TryInvert(out var inverse))
            {
                var local = inverse.Transform(point);
                if (new Rect(box.RenderSize).Contains(local) && visibility.Contains(local))
                {
                    if (CssWritingModeProperties.IsVertical(TextWritingMode))
                    {
                        var after = local.Y >= box.RenderSize.Height / 2;
                        return offset + ((TextWritingMode == CssWritingMode.SidewaysLr ? !after : after) ? 1 : 0);
                    }
                    return offset + (local.X >= box.RenderSize.Width / 2 ? 1 : 0);
                }
            }
        var bestBox = double.PositiveInfinity;
        var best = double.PositiveInfinity;
        var result = 0;
        foreach (var text in _inlineSourceTexts)
        {
            if (!InlineSourceTransform(text, out var transform, out var visibility) || !transform.TryInvert(out var inverse))
                continue;
            var local = inverse.Transform(point);
            foreach (var rect in text.DocumentTextBounds(0, text.Text.Length))
            {
                if (!visibility.TryClip(rect, transform, out var bounds) || !visibility.TryGetPoint(rect, local, out var inside))
                    continue;
                var bx = bounds.Left > point.X ? bounds.Left - point.X : Math.Max(0, point.X - bounds.Right);
                var by = bounds.Top > point.Y ? bounds.Top - point.Y : Math.Max(0, point.Y - bounds.Bottom);
                var boxDistance = bx * bx + by * by;
                if (boxDistance > bestBox + 1e-6)
                    continue;
                var index = text.DocumentCharacterFromPoint(inside);
                foreach (var caret in text.DocumentTextBounds(index, 0))
                {
                    if (!visibility.TryClip(caret, transform, out var at))
                        continue;
                    var dx = text.IsVerticalTextLayout
                        ? Math.Max(at.Left - point.X, Math.Max(0, point.X - at.Right)) : at.Left - point.X;
                    var dy = text.IsVerticalTextLayout ? at.Top - point.Y
                        : Math.Max(at.Top - point.Y, Math.Max(0, point.Y - at.Bottom));
                    var distance = dx * dx + dy * dy;
                    var candidate = text.SourceOffset + index;
                    if (boxDistance > bestBox - 1e-6 && (distance > best || distance == best && candidate <= result))
                        continue;
                    bestBox = boxDistance;
                    best = distance;
                    result = candidate;
                }
            }
        }

        return result;
    }

    private IReadOnlyList<DocumentLine> InlineSourceLines()
    {
        var result = new List<DocumentLine>();
        var sharedRows = new Dictionary<(CssInlineBidiNative,uint),int>();
        foreach (var text in _inlineSourceTexts)
        {
            if (!InlineSourceTransform(text, out var transform, out var visibility))
                continue;
            var lines = text.GetDocumentLines(text.RenderSize.Width);
            foreach (var line in lines)
            {
                bool sharedRange = lines.Count(candidate => candidate.Start == line.Start && candidate.End == line.End) > 1;
                var bounds = Rect.Empty;
                foreach (var rect in text.DocumentTextLineBounds(line.Start, line.End - line.Start))
                {
                    if (sharedRange && Math.Abs(rect.Top - text.TextContentOffset.Y - line.Top) > 0.5) continue;
                    if (visibility.TryClip(rect, transform, out var at))
                        bounds.Union(at);
                }
                if (!bounds.IsEmpty)
                {
                    var part = text.HasCssInlineFragments
                        ? (text.CssInlineFragmentValues.FirstOrDefault(f => f.Line.StartIndex == line.Start && Math.Abs(f.TextY - line.Top) < 0.01)
                            ?? (text._cssInlineFragments!.TryGetValue(line.Start, out var fragment) ? fragment : null))?.Shape as CssInlineBidiPart
                        : null;
                    AddSharedBidiDocumentRow(result, sharedRows, new(text.SourceOffset + line.Start, text.SourceOffset + line.End, bounds.Top, bounds.Height), part);
                }
            }
        }

        return result;
    }

    private IReadOnlyList<(double StartX, double EndX, double Baseline)> InlineSourceSegments(int start, int length)
    {
        var result = new List<(double, double, double)>();
        var end = Math.Min(int.MaxValue, (long)start + length);
        foreach (var text in _inlineSourceTexts)
        {
            var from = Math.Max(start, text.SourceOffset);
            var to = (int)Math.Min(end, text.SourceOffset + text.Text.Length);
            if (to <= from || !InlineSourceTransform(text, out var transform, out _))
                continue;
            foreach (var segment in text.GetSourceRangeSegments(from - text.SourceOffset, to - from))
            {
                var first = transform.Transform(new Point(segment.StartX, segment.Baseline));
                var last = transform.Transform(new Point(segment.EndX, segment.Baseline));
                result.Add((first.X, last.X, first.Y));
            }
        }

        foreach (var (box, offset) in _inlineSourceObjects)
            if (start < offset + 1 && end > offset && InlineSourceTransform(box, out var transform, out _))
            {
                var first = transform.Transform(new Point(0, box.RenderSize.Height));
                var last = transform.Transform(new Point(box.RenderSize.Width, box.RenderSize.Height));
                result.Add((first.X, last.X, first.Y));
            }
        return _inlineSourceTexts.Any(text=>text.HasSharedBidiSourceParts) ? JoinSharedBidiSourceSegments(result) : result;
    }
}
