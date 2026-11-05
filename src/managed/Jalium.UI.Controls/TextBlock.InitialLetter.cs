using System.Runtime.CompilerServices;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private sealed partial class InitialLetterScope(FrameworkElement root)
    {
        internal readonly FrameworkElement Root = root;
        internal FrameworkElement Work = root;
        internal readonly List<InlineSourceText> Texts = [];
        internal readonly List<FrameworkElement> Atoms = [];
        internal double ContainingWidth;
        internal double Height, Baseline, FontSize;
        internal bool Atomic;
        internal bool AutoWidth;
        internal bool Fragmented;
        internal CssTextFlowLayout? OriginFlow;
        internal FrameworkElement? OriginOwner;
        internal CssTextFlowLayout.CssSharedLine? OriginLine;
        internal double OriginInset;
    }
    private static readonly ConditionalWeakTable<FrameworkElement, InitialLetterScope> s_initialScopes = new();
    private double? _initialLetterOriginalFontSize;

    internal bool HasInitialLetterTextScope => this is not InlineSourceText && InitialScope(this) is { Atomic: false };
    private static InitialLetterScope? InitialScope(Visual visual)
    {
        for (Visual? current = visual; current is not null; current = current.VisualParent)
            if (current is FrameworkElement element)
            {
                if (s_initialScopes.TryGetValue(element, out var scope))
                {
                    if (InitialScopeIsCurrent(scope)) return scope;
                    ClearInitialScope(element);
                }
                if (InitialIsAtomic(element) || element.CssLayout?.IsOutOfFlow == true ||
                    CssFloatProperties.ActiveSide(element) != CssFloatSide.None) return null;
            }
        return null;
    }

    private static bool InitialIsAtomic(FrameworkElement element) =>
        element.LayoutTransform is not null ||
        element.CssDisplayMode is CssDisplayMode.FlowRoot or CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table ||
        CssReplacedContent.IsActive(element) || element is CssGeneratedText { HasImageContent: true } ||
        element is not TextBlock && element.CssDisplayMode != CssDisplayMode.Contents && !element.CanFragmentCssInlineContent;
    private static void UsedInitialMetrics(FrameworkElement element, CssInitialLetterProperties.UsedMetrics? metrics)
    {
        if (!CssInitialLetterProperties.SetMetrics(element, metrics)) return;
        if (element is TextBlock text) text.InvalidateCaches();
        element.InvalidateMeasure(); element.InvalidateVisual();
    }

    private Rect InitialLetterSelectionRect(Rect rect)
    {
        if (this is not InlineSourceText || InitialScope(this) is not { Atomic: false } scope ||
            scope.Root.TransformToVisual(this) is not { } transform) return rect;
        var box = transform.TransformBounds(new Rect(scope.Root.RenderSize));
        if (scope.Fragmented && scope.Root.CssInlineBox is { IsActive: true } allocation)
        {
            var bestOverlap = double.NegativeInfinity;
            foreach (var fragment in allocation.Fragments)
            {
                var candidate = transform.TransformBounds(fragment.Rect);
                var overlap = Math.Min(rect.Bottom, candidate.Bottom) - Math.Max(rect.Top, candidate.Top);
                if (overlap <= bestOverlap) continue;
                bestOverlap = overlap; box = candidate;
            }
        }
        var left = Math.Clamp(rect.Left, box.Left, box.Right); var right = Math.Clamp(rect.Right, box.Left, box.Right);
        return new(left, rect.Y, Math.Max(0, right - left), rect.Height);
    }

    private Rect? InitialLetterInkInText()
    {
        if (this is not InlineSourceText || InitialScope(this) is not { Atomic: false } scope ||
            scope.Work.TransformToVisual(this) is not { } transform) return null;
        return transform.TransformBounds(InitialGlyphBounds(scope));
    }

    private static void ClearInitialScope(FrameworkElement element)
    {
        if (!s_initialScopes.TryGetValue(element, out var scope)) return;
        s_initialScopes.Remove(element);
        scope.Root.PropertyChangedInternal -= scope.StateChanged;
        scope.OriginFlow?.RetireInitialAllocation(scope.Root);
        if (scope.OriginOwner is { } origin)
        {
            scope.Root.ReleaseCssInlineFrame(origin); scope.Work.ReleaseCssInlineFrame(origin);
            foreach (var text in scope.Texts) text.ReleaseCssInlineAllocation(origin);
        }
        UsedInitialMetrics(scope.Root, null); UsedInitialMetrics(scope.Work, null);
        foreach (var text in scope.Texts)
        {
            UsedInitialMetrics(text, null);
            if (text._initialLetterOriginalFontSize is { } original) text.FontSize = original;
            text._initialLetterOriginalFontSize = null;
            text.SourceOwner._inlineBoxesDirty = true;
            text.SourceOwner.InvalidateCaches();
            text.SourceOwner.InvalidateMeasure();
        }
        scope.OriginOwner?.InvalidateMeasure(); scope.OriginOwner?.InvalidateVisual();
        if (element is TextBlock block) block.EnsureInlineSourceBoxes();
    }

    private static IEnumerable<InlineSourceText> InitialTextSources(FrameworkElement root)
    {
        if (root is InlineSourceText leaf) { yield return leaf; yield break; }
        if (root is TextBlock text) text.EnsureInlineSourceBoxes();
        root.PrepareCssContent();
        foreach (var child in new CssPanelChildren(root))
            if (child is FrameworkElement element && element.Visibility != Visibility.Collapsed &&
                element.CssLayout?.IsOutOfFlow != true && CssFloatProperties.ActiveSide(element) == CssFloatSide.None && !InitialIsAtomic(element))
                foreach (var source in InitialTextSources(element)) yield return source;
    }

    private static IEnumerable<FrameworkElement> InitialAtomicSources(FrameworkElement root)
    {
        foreach (var child in new CssPanelChildren(root))
        {
            if (child is not FrameworkElement element || element.Visibility == Visibility.Collapsed ||
                element.CssLayout?.IsOutOfFlow == true || CssFloatProperties.ActiveSide(element) != CssFloatSide.None) continue;
            if (InitialIsAtomic(element)) yield return element;
            else foreach (var atom in InitialAtomicSources(element)) yield return atom;
        }
    }

    private static Rect InitialAtomicBounds(InitialLetterScope scope, FrameworkElement atom)
    {
        var delta = atom.TransformToAncestor(scope.Work);
        var margin = CssBoxMetrics.Margin(atom, scope.ContainingWidth);
        return new(delta.X - margin.Left, delta.Y - margin.Top,
            Math.Max(0, atom.RenderSize.Width + margin.Left + margin.Right), Math.Max(0, atom.RenderSize.Height + margin.Top + margin.Bottom));
    }

    private readonly record struct InitialAlignmentPoints(double Over, double Under)
    { internal double Span => Over + Under; }

    private static InitialAlignmentPoints InitialPoints(DependencyObject style, double fontSize, CssInitialLetterAlignment alignment)
    {
        var family = (FontFamily)style.GetValue(TextElement.FontFamilyProperty)!;
        var weight = ((FontWeight)style.GetValue(TextElement.FontWeightProperty)!).ToOpenTypeWeight();
        var slant = ((FontStyle)style.GetValue(TextElement.FontStyleProperty)!).ToOpenTypeStyle();
        var source = family.GetRenderingSource(style);
        var metrics = TextMeasurement.GetFontMetrics(source, fontSize, weight, slant);
        var units = TextMeasurement.GetFontUnitMetrics(source, fontSize, weight, slant);
        var ascent = Math.Max(0, metrics.Ascent); var descent = Math.Max(0, metrics.Descent);
        var emOver = ascent + descent > 0 ? fontSize * ascent / (ascent + descent) : fontSize * .8;
        // CSS Inline's baseline synthesis uses representative glyph ink when
        // a face has no BASE table exposed by the platform text API.
        if (alignment is CssInitialLetterAlignment.Ideographic or CssInitialLetterAlignment.Hanging)
        {
            var language = (style.GetValue(FrameworkElement.LanguageProperty) as Markup.XmlLanguage)?.IetfLanguageTag ?? "";
            var sample = alignment == CssInitialLetterAlignment.Ideographic ? "永" : language.Split('-')[0].ToLowerInvariant() switch
            { "bn" => "ক", "pa" => "ਕ", "bo" => "ཀ", "he" or "yi" => "ה", _ => "क" };
            var formatted = new FormattedText(sample, source, fontSize) { FontWeight = weight, FontStyle = slant };
            if (TextMeasurement.TryGetFontInkBounds(formatted, out var ink) && ink.Height > 0)
                return new(Math.Max(0, -ink.Top), alignment == CssInitialLetterAlignment.Ideographic ? Math.Max(0, ink.Bottom) : 0);
        }
        return alignment switch
        {
            CssInitialLetterAlignment.Ideographic => new(emOver, Math.Max(0, fontSize - emOver)),
            CssInitialLetterAlignment.Hanging => new(fontSize * .6, 0),
            CssInitialLetterAlignment.Leading => new(CssComputedLineHeightValue.LayoutBounds(style, fontSize, metrics).Baseline,
                Math.Max(0, CssComputedLineHeightValue.LayoutBounds(style, fontSize, metrics).Height - CssComputedLineHeightValue.LayoutBounds(style, fontSize, metrics).Baseline)),
            _ => new(units.CapHeight > 0 ? units.CapHeight : fontSize * .66, 0),
        };
    }

    private static double InitialFontSize(DependencyObject style, double target, CssInitialLetterAlignment alignment)
    {
        if (target <= 0) return 0;
        var size = Math.Clamp((double)style.GetValue(TextElement.FontSizeProperty)!, 1, 1048576);
        for (var pass = 0; pass < 8; pass++)
        {
            var span = InitialPoints(style, size, alignment).Span;
            if (span <= 0 || !double.IsFinite(span)) return size;
            var next = Math.Clamp(size * (target / span), 0, 1048576);
            if (Math.Abs(next - size) <= Math.Max(.00001, size * .000001)) return next;
            size = next;
        }
        return size;
    }

    private static CssInitialLetterAlignment InitialContentAlignment(InitialLetterScope scope)
    {
        foreach (var text in scope.Texts)
        foreach (var rune in text.Text.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune)) continue;
            if (rune.Value is >= 0x2e80 and <= 0x9fff or >= 0xac00 and <= 0xd7af or >= 0x20000 and <= 0x323af)
                return CssInitialLetterAlignment.Ideographic;
            if (rune.Value is >= 0x0900 and <= 0x0a7f or >= 0x0f00 and <= 0x0fff) return CssInitialLetterAlignment.Hanging;
            return CssInitialLetterAlignment.Alphabetic;
        }
        return CssInitialLetterAlignment.Alphabetic;
    }

    private static Rect InitialGlyphBounds(InitialLetterScope scope)
    {
        Rect? result = null;
        foreach (var text in scope.Texts)
        {
            var delta = text.TransformToAncestor(scope.Work);
            var y = 0d;
            foreach (var line in text._layoutLines)
            {
                var paint = text.PaintSlice(line.StartIndex, line.Length);
                if (paint.Length == 0) { y += line.Height; continue; }
                var formatted = new FormattedText(paint, text.FontFamily.GetRenderingSource(text), text.FontSize)
                    { FontWeight = text.FontWeight.ToOpenTypeWeight(), FontStyle = text.FontStyle.ToOpenTypeStyle() };
                formatted.ApplyTextOptionsFrom(text);
                if (TextMeasurement.TryGetFontInkBounds(formatted, out var ink))
                {
                    var x = text.GetLineOriginX(line, text.GetContentWidth(text.TextContentSize.Width)) + text.TextContentOffset.X;
                    var top = text.TextLineTop(line, y) + text.TextContentOffset.Y + line.Baseline;
                    var rect = new Rect(delta.X + x + ink.X, delta.Y + top + ink.Y, ink.Width, ink.Height);
                    result = result is { } previous ? Rect.Union(previous, rect) : rect;
                }
                y += line.Height;
            }
        }
        foreach (var atom in scope.Atoms)
        {
            var rect = InitialAtomicBounds(scope, atom);
            result = result is { } previous ? Rect.Union(previous, rect) : rect;
        }
        return result ?? default;
    }

    private sealed class InitialLetterPlacement(InitialLetterScope scope, CssInitialLetter value, CssInitialLetterAlign align,
        CssInitialLetterWrap wrap, Thickness insets, Thickness margin)
    {
        internal readonly InitialLetterScope Scope = scope;
        internal readonly CssInitialLetter Value = value;
        internal readonly CssInitialLetterAlign Align = align;
        internal readonly CssInitialLetterWrap Wrap = wrap;
        internal readonly Thickness Insets = insets, Margin = margin;
        internal Size Size;
        internal double RelativeTop;
        internal double OriginLineTop;
        internal double UnderPoint;
        internal double LastLineWidth;
        internal double ContentBaseline;
        internal double GridWidth, GridOffset;
        internal CssFloatShape? Shape;
        internal CssFloatRegion? Region;
        internal Rect Rect;
        internal bool Placed;
        internal bool Inline;
        internal CssInlineFrame? Frame;
        internal double RootBaseline, RootOver, RootUnder, Interval;
        internal double PreferredWidth, PreferredHeight, MinWidth, MaxWidth, MinHeight, MaxHeight;
    }

    internal sealed partial class CssTextFlowLayout
    {
        private InitialLetterPlacement? _initialLetter;
        private bool _initialAllowed;
        private readonly CssFloatSpace _initialEmptySpace = new(CssFloatEnvironment.Empty);

        private void BeginInitialLetter(bool firstLine)
        { _initialLetter = null; _initialAllowed = firstLine && InitialScope(owner) is null; }

        private bool TryAppendInitialLetter(UIElement element, List<CssInlineUnit> pending, ref bool collapsedSpace, CssInlineFrame? parent)
        {
            if (element is not FrameworkElement box) return false;
            var value = (CssInitialLetter)box.GetValue(CssInitialLetterProperties.ValueProperty)!;
            var eligible = _initialAllowed && _initialLetter is null && value.Active && box.CssInlineOuter &&
                box.CssLayout?.IsOutOfFlow != true && box.CssLayout?.Position is null or CssPositionMode.Static &&
                (CssFloatSide)box.GetValue(CssFloatProperties.FloatProperty)! == CssFloatSide.None &&
                pending.All(static unit => unit.Collapse);
            if (!eligible)
            {
                if (s_initialScopes.TryGetValue(box, out _) && InitialScope(owner) is null) ClearInitialScope(box);
                return false;
            }

            var atomic = InitialIsAtomic(box);
            if (s_initialScopes.TryGetValue(box, out var previousScope) && previousScope.Atomic != atomic) ClearInitialScope(box);
            var scope = s_initialScopes.GetValue(box, static element =>
            {
                var scope = new InitialLetterScope(element);
                element.PropertyChangedInternal += scope.StateChanged;
                return scope;
            }); scope.Atomic = atomic;
            scope.ContainingWidth = _containing.Width;
            scope.OriginFlow = this; scope.OriginOwner = owner; scope.OriginLine = null; scope.OriginInset = _insets.Top;
            var align = (CssInitialLetterAlign)box.GetValue(CssInitialLetterProperties.AlignProperty)!;
            var wrap = (CssInitialLetterWrap)box.GetValue(CssInitialLetterProperties.WrapProperty)!;
            var firstLineMetrics = _firstLineMetrics;
            _firstLineMetrics = true;
            FrameworkElement rootStyle;
            try { rootStyle = FirstLineMetricsElement(owner); }
            finally { _firstLineMetrics = firstLineMetrics; }
            var rootSize = (double)rootStyle.GetValue(TextElement.FontSizeProperty)!;
            var rootMetrics = CssFlowProperties.LineMetrics(rootStyle);
            var rootPoints = InitialPoints(rootStyle, rootSize, align.Alignment);
            // Alignment belongs to the root inline box (including ::first-line),
            // while the line-count interval belongs to the containing block.
            var interval = CssFlowProperties.LineMetrics(owner).Height;
            var target = Math.Clamp(rootPoints.Span + Math.Max(0, value.Size - 1) * interval, 0, 1048576);
            var insets = CssBoxMetrics.ContentInsets(box, _containing.Width);
            var margin = CssBoxMetrics.Margin(box, _containing.Width);
            var data = new InitialLetterPlacement(scope, value, align, wrap, insets, margin);
            var preferredWidth = box.CssPreferredWidth(_containing.Width);
            var preferredHeight = box.CssPreferredHeight(_containing.Height);
            scope.AutoWidth = double.IsNaN(preferredWidth) && box.CssLayout?.Width.Kind is null or CssLayoutLength.KindUnset or CssLayoutLength.KindAuto;

            if (!atomic)
            {
                scope.Texts.Clear(); scope.Texts.AddRange(InitialTextSources(box));
                scope.Atoms.Clear(); scope.Atoms.AddRange(InitialAtomicSources(box));
                scope.Work = box is TextBlock rootText && rootText._cssTextBody is { } body ? body : box;
                var ownAlignment = InitialContentAlignment(scope);
                var fontSize = InitialFontSize(box, Math.Max(0, target - (align.BorderBox ? insets.Top + insets.Bottom : 0)), ownAlignment);
                var specifiedSize = (double)box.GetValue(TextElement.FontSizeProperty)!;
                var ownPoints = InitialPoints(box, fontSize, ownAlignment);
                var height = ownPoints.Span;
                for (var fitting = 0; fitting < (align.BorderBox ? 6 : 1); fitting++)
                {
                    ownPoints = InitialPoints(box, fontSize, ownAlignment); height = ownPoints.Span;
                    foreach (var text in scope.Texts)
                    {
                        text._initialLetterOriginalFontSize ??= text.FontSize;
                        text.FontSize = specifiedSize > 0 ? text._initialLetterOriginalFontSize.Value * (fontSize / specifiedSize) : fontSize;
                        var formatted = new FormattedText(text.Text, text.FontFamily.GetRenderingSource(text), text.FontSize)
                            { FontWeight = text.FontWeight.ToOpenTypeWeight(), FontStyle = text.FontStyle.ToOpenTypeStyle() };
                        formatted.ApplyTextOptionsFrom(text);
                        if (TextMeasurement.TryGetFontInkBounds(formatted, out var ink)) height = Math.Max(height, ownPoints.Over + ink.Bottom);
                    }
                    var wanted = Math.Max(0, target - insets.Top - insets.Bottom);
                    if (!align.BorderBox || height <= 0 || Math.Abs(height - wanted) < .0001 || fitting == 5) break;
                    fontSize = Math.Clamp(fontSize * (wanted / height), 0, 1048576);
                }
                scope.Height = align.BorderBox ? height : ownPoints.Span; scope.Baseline = ownPoints.Over; scope.FontSize = fontSize;
                data.UnderPoint = ownPoints.Under;
                var metrics = new CssInitialLetterProperties.UsedMetrics(scope.Height, ownPoints.Over);
                UsedInitialMetrics(box, metrics); UsedInitialMetrics(scope.Work, metrics);
                foreach (var text in scope.Texts) UsedInitialMetrics(text, metrics);
                // A non-atomic initial stays in its containing inline formatter.
                // Each line obtains its own ink size and decoration fragment.
                data.RootBaseline = rootMetrics.Baseline; data.RootOver = rootPoints.Over;
                data.RootUnder = rootPoints.Under; data.Interval = interval;
                data.PreferredWidth = preferredWidth; data.PreferredHeight = preferredHeight;
                (data.MinWidth, data.MaxWidth) = box.CssWidthBounds(_containing.Width);
                (data.MinHeight, data.MaxHeight) = box.CssHeightBounds(_containing.Height);
                return AppendInlineInitial(data, pending, ref collapsedSpace, parent);
            }

            box.InvalidateCssAllocation();
            var layout = new CssLayoutBox(box, 0);
            layout.MeasureBorderBox(new(Math.Max(0, _containing.Width - margin.Left - margin.Right), double.PositiveInfinity), _containing);
            var measured = box.DesiredSize;
            var borderSize = new Size(Math.Max(0, measured.Width - margin.Left - margin.Right), Math.Max(0, measured.Height - margin.Top - margin.Bottom));
            if (double.IsNaN(preferredHeight)) borderSize = new(borderSize.Width, target + (align.BorderBox ? 0 : insets.Top + insets.Bottom));
            scope.Height = Math.Max(0, borderSize.Height - insets.Top - insets.Bottom); scope.Baseline = scope.Height;
            data.ContentBaseline = scope.Baseline; data.LastLineWidth = borderSize.Width;
            var widthBounds = box.CssWidthBounds(_containing.Width); var heightBounds = box.CssHeightBounds(_containing.Height);
            borderSize = new(Math.Clamp(borderSize.Width, widthBounds.Min, widthBounds.Max),
                Math.Clamp(borderSize.Height, heightBounds.Min, heightBounds.Max));
            data.LastLineWidth = Math.Clamp(data.LastLineWidth, widthBounds.Min, widthBounds.Max);
            data.Size = borderSize;
            var under = align.BorderBox ? borderSize.Height : insets.Top + data.ContentBaseline + data.UnderPoint;
            data.RelativeTop = value.Size >= value.Sink
                ? rootMetrics.Baseline + rootPoints.Under + Math.Min(1048576, (value.Sink - 1) * interval) - under
                : rootMetrics.Baseline - rootPoints.Over - (align.BorderBox ? 0 : insets.Top);
            if (wrap.Kind is CssInitialLetterWrapKind.All or CssInitialLetterWrapKind.First)
            {
                data.Shape = CssShapeOutsideProperties.Resolve(box, borderSize, margin, _containing.Width);

            }
            if (wrap.Kind == CssInitialLetterWrapKind.Grid)
            {
                var family = (FontFamily)owner.GetValue(TextElement.FontFamilyProperty)!;
                var unit = TextMeasurement.GetFontUnitMetrics(family.GetRenderingSource(owner), (double)owner.GetValue(TextElement.FontSizeProperty)!,
                    ((FontWeight)owner.GetValue(TextElement.FontWeightProperty)!).ToOpenTypeWeight(),
                    ((FontStyle)owner.GetValue(TextElement.FontStyleProperty)!).ToOpenTypeStyle()).IdeographicAdvance + CssTextSpacing.Letter(owner);
                var width = Math.Max(0, data.LastLineWidth + margin.Left + margin.Right);
                data.GridWidth = unit > 0 && double.IsFinite(unit) ? Math.Ceiling(width / unit) * unit : width;
                var extra = data.GridWidth - width;
                data.GridOffset = (CssBoxAlignment)box.GetValue(CssGridProperties.JustifySelfProperty)! switch
                { CssBoxAlignment.End => extra, CssBoxAlignment.Center => extra / 2, _ => 0 };
            }
            _y += Math.Max(0, margin.Top - (_y + data.RelativeTop));
            _maximumWidth = Math.Max(_maximumWidth, borderSize.Width + margin.Left + margin.Right);
            var source = new CssInlineSource(box, box as TextBlock, false, default, margin)
                { Atomic = true, AtomicSize = borderSize, AtomicBaseline = 0, AtomicWraps = WrapsAroundAtomic, LastUnit = pending.Count, Frame = parent, Initial = data };
            _sources.Add(source); pending.Add(new(source, 0, 0, false, false, false));
            _initialLetter = data; collapsedSpace = false;
            return true;
        }

        private double InitialLetterAdvance(CssInlineSource source, List<CssInlineUnit> units, int next)
        {
            if (source.Initial is not { Inline: false } data) return source.AtomicSize.Width;
            if (data.Wrap.Kind == CssInitialLetterWrapKind.Grid) return data.GridWidth - data.Margin.Left - data.Margin.Right;
            var right = data.LastLineWidth;
            if (data.Wrap.Kind is CssInitialLetterWrapKind.First or CssInitialLetterWrapKind.Offset)
            {
                while (next < units.Count && !units[next].Source.Atomic && units[next].Start == units[next].End) next++;
                if (next < units.Count && !units[next].Source.Atomic &&
                    Rune.TryGetRuneAt(units[next].Source.Text._visualText, units[next].Start, out var rune) &&
                    Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.SpaceSeparator) return right;
            }
            if (data.Wrap.Kind == CssInitialLetterWrapKind.Offset)
                return right + data.Wrap.Offset.Resolve(data.LastLineWidth, 0);
            if (data.Shape is { } shape && data.Wrap.Kind is CssInitialLetterWrapKind.First or CssInitialLetterWrapKind.All)
            {
                var band = shape.Band(-data.RelativeTop + data.Margin.Top, CssFlowProperties.LineMetrics(owner).Height);
                var edge = !band.Active ? 0 : owner.FlowDirection == FlowDirection.RightToLeft
                    ? data.Size.Width + data.Margin.Left + data.Margin.Right - band.Left : band.Right;
                return edge - data.Margin.Left - data.Margin.Right;
            }
            return right;
        }

        private void PlaceInitialLetter(CssInlineFragment fragment)
        {
            var data = fragment.Source.Initial!;
            var rtl = owner.FlowDirection == FlowDirection.RightToLeft;
            var shift = rtl ? fragment.Line.Width - data.Size.Width - data.GridOffset : data.GridOffset;
            fragment.Rect = new(fragment.Rect.X + shift, _y + data.RelativeTop, data.Size.Width, data.Size.Height);
            data.Rect = fragment.Rect; data.OriginLineTop = _y; data.Placed = true;
            var left = data.Rect.Left - data.Margin.Left + (rtl ? data.GridOffset - (data.GridWidth > 0 ? data.GridWidth - data.Size.Width - data.Margin.Left - data.Margin.Right : 0) : -data.GridOffset);
            var top = data.Rect.Top - data.Margin.Top;
            data.Region = new(owner.FlowDirection == FlowDirection.RightToLeft ? CssFloatSide.Right : CssFloatSide.Left,
                data.Wrap.Kind == CssInitialLetterWrapKind.All ? left : rtl ? data.Rect.Right - data.LastLineWidth - data.Margin.Left : left,
                top,
                Math.Max(0, data.Wrap.Kind == CssInitialLetterWrapKind.All ? data.Size.Width + data.Margin.Left + data.Margin.Right : data.GridWidth > 0 ? data.GridWidth : data.LastLineWidth + data.Margin.Left + data.Margin.Right),
                Math.Max(0, data.Size.Height + data.Margin.Top + data.Margin.Bottom),
                data.Wrap.Kind == CssInitialLetterWrapKind.All ? data.Shape : null, IsInitial: true, InitialLineTop: data.OriginLineTop);
        }

        private double InitialLetterBottom => _initialLetter is { Placed: true } data ? Math.Max(0, data.Rect.Bottom + data.Margin.Bottom) : 0;

        private void RecordInitialLetterLine(List<CssInlineFragment> fragments, CssSharedLine line)
        {
            foreach (var fragment in fragments)
                if (fragment.Source.Initial is { } data) data.Scope.OriginLine = line;
        }
    }
}
