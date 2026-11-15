using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Style-only text projections: selectors see the block, inheritance sees the enclosing inline.</summary>
internal static class CssFirstLetter
{
    private sealed class Target { internal bool Potential; internal int Signature; }
    private sealed class Projection(CssNode origin, DependencyObject parent)
    {
        internal readonly CssNode Origin = origin;
        internal readonly DependencyObject Parent = parent;
        internal bool Refreshing;
    }
    private static readonly ConditionalWeakTable<DependencyObject, Target> s_targets = new();
    private static readonly ConditionalWeakTable<DependencyObject, Projection> s_projections = new();
    private static bool s_active;
    private static int s_revision;
    internal static int Revision => Volatile.Read(ref s_revision);
    internal static void ContentChanged() { if (s_active) Interlocked.Increment(ref s_revision); }
    internal static bool HasTargetOrAncestor(DependencyObject target)
    {
        if (!s_active) return false;
        var depth = 0;
        for (var node = CssNode.Get(target); node is not null && depth++ < 4096; node = node.FrameworkParent)
            if (s_targets.TryGetValue(node.Target, out var value) && value.Potential) return true;
        return false;
    }

    internal static CssNode? Origin(DependencyObject target) => s_active && s_projections.TryGetValue(target, out var value) ? value.Origin : null;
    internal static DependencyObject? StyleParent(DependencyObject target) => s_active && s_projections.TryGetValue(target, out var value) ? value.Parent : null;

    internal static Run Project(CssNode origin, DependencyObject parent, string text, Action changed)
    {
        var run = new Run(text);
        s_projections.Add(run, new(origin, parent));
        // A style parent does not add the projection to a public InlineCollection
        // or logical child list. Original pointers and selector indices stay intact.
        run.SetContentParent(parent);
        run.SetLayerValue(FrameworkElement.LanguageProperty, parent.GetValue(FrameworkElement.LanguageProperty), DependencyObject.LayerValueSource.StyleSetter);
        CssEngine.EvaluateElement(run);
        run.TextContentChanged += (_, _) =>
        {
            if (!s_projections.TryGetValue(run, out var projection) || !projection.Refreshing) changed();
        };
        return run;
    }

    internal static void Refresh(Run run, string text)
    {
        var projection = s_projections.GetValue(run, _ => throw new InvalidOperationException("Unknown first-letter projection."));
        projection.Refreshing = true;
        try
        {
            run.Text = text;
            CssEngine.EvaluateElement(run);
        }
        finally { projection.Refreshing = false; }
    }

    internal static void Update(CssNode origin, CssPseudoElement kinds, int signature)
    {
        if (origin.Target is ICssStyledBox or ICssAnonymousBox || origin.Target is TextBlock { IsCssTypographyProjection: true }) return;
        var selected = (kinds & CssPseudoElement.FirstLetter) != 0;
        var hadPotential = s_targets.TryGetValue(origin.Target, out var previous) && previous.Potential;
        if (hadPotential == selected && (!selected || previous!.Signature == signature)) return;
        s_active = true;
        Interlocked.Increment(ref s_revision);
        var target = s_targets.GetOrCreateValue(origin.Target);
        target.Potential = selected; target.Signature = signature;
        if (origin.Target is TextElement text) text.NotifyTextContentChanged();
        else if (origin.Target is FlowDocument document) document.NotifyPaginationStyleChanged();
        else if (origin.Target is Visual visual) CssFlowProperties.InvalidateInheritedTextLayout(visual);
    }

    internal static CssNode[] Origins(TextBlock text)
    {
        if (!s_active) return [];
        var source = text.DocumentFontSource ?? text;
        var result = new List<CssNode>();
        var depth = 0;
        for (var node = CssNode.Get(source); node is not null && depth++ < 4096; node = node.FrameworkParent)
            if (s_targets.TryGetValue(node.Target, out var target) && target.Potential && IsBlockContainer(node) &&
                SameSource(FirstContent(node, true, 0).Source, source)) result.Add(node);
        result.Reverse();
        return result.ToArray();
    }

    private static bool SameSource(DependencyObject? a, DependencyObject? b) =>
        ReferenceEquals(a, b) ||
        a is CssGeneratedText ga && b is CssGeneratedText gb &&
        ReferenceEquals(ga.Origin, gb.Origin) && ga.Kind == gb.Kind;

    private static bool IsBlockContainer(CssNode node)
    {
        var display = CssDisplayProperties.Computed(node);
        if (display.None || display.Mode is CssDisplayMode.Contents or CssDisplayMode.Flex or CssDisplayMode.Grid or
            CssDisplayMode.Table or CssDisplayMode.TableRow or CssDisplayMode.TableRowGroup or CssDisplayMode.TableHeaderGroup or
            CssDisplayMode.TableFooterGroup or CssDisplayMode.TableColumn or CssDisplayMode.TableColumnGroup) return false;
        if (node.Target is Paragraph or ListItem or Section) return !display.Inline || display.Mode == CssDisplayMode.Native;
        if (node.Target is TextBlock text) return !text.CssInlineOuter || display.Mode == CssDisplayMode.FlowRoot;
        return node.Target is FrameworkElement element && CssDisplayLayout.IsFlow(element) && !element.CssInlineOuter;
    }

    private readonly record struct First(DependencyObject? Source, bool Stops);
    private static First FirstContent(CssNode node, bool root, int depth)
    {
        if (depth > 512) return new(null, true);
        if (CssDisplayProperties.SuppressesBoxTree(node.Target) || node.Target is UIElement { Visibility: Visibility.Collapsed }) return default;
        if (!root && (node.CssLayout?.IsOutOfFlow == true || node.GetValue(CssFloatProperties.FloatProperty) is CssFloatSide side && side != CssFloatSide.None)) return default;
        var display = CssDisplayProperties.Computed(node);
        if (!root && display.Inline && display.Mode is CssDisplayMode.FlowRoot or CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table)
            return new(null, true);
        if (node.Target is FrameworkElement element && CssReplacedContent.IsActive(element) ||
            node.Target is FrameworkContentElement contentElement && CssReplacedContent.IsActive(contentElement) ||
            node.Target is CssGeneratedText { HasImageContent: true }) return new(null, true);
        foreach (var before in CssGeneratedContent.Befores(node))
        {
            var first = FirstContent(CssNode.Get(before), false, depth + 1);
            if (first.Stops) return first;
        }
        if (node.Target is TextBlock text)
        {
            if (text.DocumentFontSource is null && text.CssOriginalInlines is { Count: > 0 } inlines)
            {
                var first = InlinesFirstContent(inlines, text, depth + 1);
                if (first.Stops) return first;
            }
            else if (text.CssFirstLetterHasContent) return new(node.Target, true);
        }
        else if (node.Target is ContentControl contentControl && contentControl.CssContentAdapter is { } adapter)
        {
            if (adapter.CssFirstLetterHasContent) return new(node.Target, true);
        }
        else if (node.Target is Paragraph paragraph)
        {
            var first = InlinesFirstContent(paragraph.Inlines, paragraph, depth + 1);
            if (first.Stops) return first;
        }
        else
        {
            // Reading a panel's sorted visual children asks for CSS paint rank,
            // which itself needs to know whether a TextBlock has a first letter.
            // Selection follows authored order and never enters that paint path.
            var children = SourceChildren(node).ToArray();
            foreach (var child in children)
            {
                var first = FirstContent(child, false, depth + 1);
                if (first.Stops) return first;
            }
            if (children.Length == 0 && node.Target is UIElement and not Panel and not Border) return new(null, true);
        }
        foreach (var after in CssGeneratedContent.Afters(node))
        {
            var first = FirstContent(CssNode.Get(after), false, depth + 1);
            if (first.Stops) return first;
        }
        return default;
    }

    private static First InlinesFirstContent(IEnumerable<Inline> inlines, DependencyObject ownerSource, int depth)
    {
        if (depth > 512) return new(null, true);
        foreach (var inline in inlines)
        {
            if (CssDisplayProperties.SuppressesBoxTree(inline)) continue;
            var inlineNode = CssNode.Get(inline);
            if (inlineNode.CssLayout?.IsOutOfFlow == true ||
                inline.GetValue(CssFloatProperties.FloatProperty) is CssFloatSide side && side != CssFloatSide.None)
                continue;
            var display = CssDisplayProperties.Computed(inlineNode);
            if (display.Mode is CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table or
                CssDisplayMode.TableRow or CssDisplayMode.TableRowGroup or CssDisplayMode.TableHeaderGroup or
                CssDisplayMode.TableFooterGroup or CssDisplayMode.TableColumn or CssDisplayMode.TableColumnGroup ||
                display.Inline && display.Mode == CssDisplayMode.FlowRoot ||
                inline is InlineUIContainer || CssReplacedContent.IsActive(inline))
                return new(null, true);
            foreach (var before in CssGeneratedContent.Befores(inlineNode))
            {
                var first = FirstContent(CssNode.Get(before), false, depth + 1);
                if (first.Stops) return first;
            }
            if (inline is Span span)
            {
                var first = InlinesFirstContent(span.Inlines, ownerSource, depth + 1);
                if (first.Stops) return first;
            }
            else if (inline is LineBreak)
            {
                return new(null, true);
            }
            else if (inline is Run run && RunHasFirstLetterContent(inlineNode, run.Text))
            {
                return new(ownerSource, true);
            }
            foreach (var after in CssGeneratedContent.Afters(inlineNode))
            {
                var first = FirstContent(CssNode.Get(after), false, depth + 1);
                if (first.Stops) return first;
            }
        }
        return default;
    }

    private static bool RunHasFirstLetterContent(CssNode node, string text)
    {
        var mode = (CssWhiteSpaceCollapse)node.GetValue(CssFlowProperties.WhiteSpaceProperty)!;
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value is not (' ' or '\t' or '\r' or '\n' or '\f' or '\uFEFF' or '\u00AD')) return true;
            if (mode is CssWhiteSpaceCollapse.Preserve or CssWhiteSpaceCollapse.BreakSpaces ||
                mode == CssWhiteSpaceCollapse.PreserveBreaks && value is '\r' or '\n' or '\f') return true;
        }
        return false;
    }

    private static IEnumerable<CssNode> SourceChildren(CssNode node)
    {
        System.Collections.IEnumerable? children = node.Target switch
        {
            Panel panel => panel.Children,
            Border border => border.Child is { } child ? new[] { child } : [],
            FlowDocument document => document.Blocks,
            Section section => section.Blocks,
            ListItem item => item.Blocks,
            Jalium.UI.Documents.List list => list.ListItems,
            _ => null,
        };
        if (children is not null)
        {
            foreach (var value in children)
                if (value is DependencyObject child) yield return CssNode.Get(child);
            yield break;
        }
        var logical = node.Target switch
        {
            FrameworkElement element => element.LogicalChildren,
            FrameworkContentElement content => content.LogicalChildren,
            _ => null,
        };
        if (logical is not null)
            while (logical.MoveNext())
                if (logical.Current is FrameworkElement or FrameworkContentElement)
                    yield return CssNode.Get((DependencyObject)logical.Current);
    }

    internal static bool Applies(string name) => name.StartsWith("--", StringComparison.Ordinal) ||
        name.StartsWith("font-", StringComparison.Ordinal) || name.StartsWith("background-", StringComparison.Ordinal) ||
        name.StartsWith("border-", StringComparison.Ordinal) || name.StartsWith("margin-", StringComparison.Ordinal) ||
        name.StartsWith("padding-", StringComparison.Ordinal) || name.StartsWith("text-decoration-", StringComparison.Ordinal) ||
        name.StartsWith("hyphenate-", StringComparison.Ordinal) || name.StartsWith("text-emphasis-", StringComparison.Ordinal) ||
        name is "color" or "opacity" or "float" or "box-shadow" or "line-height" or "vertical-align" or
            "initial-letter" or "initial-letter-align" or "initial-letter-wrap" or
            "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height" or "box-sizing" or
            "text-align" or "align-content" or "justify-self" or "shape-outside" or "shape-margin" or "shape-image-threshold" or
            "text-transform" or "text-autospace" or "text-shadow" or "text-combine-upright" or
            "text-underline-offset" or "text-underline-position" or "text-wrap-mode" or "text-wrap-style" or
            "white-space-collapse" or "white-space-trim" or "word-spacing" or "word-space-transform" or "letter-spacing" or
            "word-break" or "line-break" or "overflow-wrap" or "tab-size" or "hyphens" or "hanging-punctuation";

    internal static (int Start, int End) Select(string source, string language, bool preserveBreaks,
        bool[]? explicitBreaks = null, bool[]? hidden = null, bool collapseSpaces = true)
    {
        // Whitespace processing precedes pseudo-element selection. Preserve UTF-16
        // offsets while classifying segment breaks and collapsed tabs as spaces.
        if (!preserveBreaks) source = source.Replace('\r', ' ').Replace('\n', ' ').Replace('\f', ' ');
        if (collapseSpaces) source = source.Replace('\t', ' ');
        var index = 0;
        while (index < source.Length)
        {
            if (hidden?[index] == true) { index++; continue; }
            if (explicitBreaks?[index] == true || preserveBreaks && source[index] is '\r' or '\n' or '\f') return default;
            if (!Collapsible(source[index]) && Category(source, index) != UnicodeCategory.SpaceSeparator &&
                source[index] is not '\uFEFF' and not '\u00AD') break;
            index++;
        }
        var start = index;
        var punctuation = false;
        while (index < source.Length)
        {
            var category = Category(source, index);
            if (!Punctuation(category) && !(punctuation && TypographicSpace(source, index, false))) break;
            punctuation = true;
            index += StringInfo.GetNextTextElementLength(source, index);
        }
        if (index >= source.Length || explicitBreaks?[index] == true) return default;
        var first = Category(source, index);
        if (first is not (UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.MathSymbol or
            UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol)) return default;
        var letter = index;
        index += StringInfo.GetNextTextElementLength(source, index);
        if ((language.Equals("nl", StringComparison.OrdinalIgnoreCase) || language.StartsWith("nl-", StringComparison.OrdinalIgnoreCase)) &&
            source[letter] is 'i' or 'I' && index < source.Length && source[index] is 'j' or 'J')
            index += StringInfo.GetNextTextElementLength(source, index);
        var end = index;
        while (index < source.Length && explicitBreaks?[index] != true && hidden?[index] != true)
        {
            var category = Category(source, index);
            if (Punctuation(category) && category is not UnicodeCategory.OpenPunctuation and not UnicodeCategory.DashPunctuation)
            {
                index += StringInfo.GetNextTextElementLength(source, index); end = index;
            }
            else if (TypographicSpace(source, index, true)) index += StringInfo.GetNextTextElementLength(source, index);
            else break;
        }
        for (var offset = start; offset < end; offset++)
            if (hidden?[offset] == true || explicitBreaks?[offset] == true) return default;
        return (start, end);
    }

    private static UnicodeCategory Category(string source, int index) => Rune.GetUnicodeCategory(
        Rune.TryGetRuneAt(source, index, out var rune) ? rune : Rune.ReplacementChar);
    private static bool Collapsible(char value) => value is ' ' or '\t' or '\r' or '\n' or '\f';
    private static bool TypographicSpace(string source, int index, bool after) => source[index] != '\u3000' &&
        (!after || source[index] is not ' ' and not '\u00A0') && Category(source, index) == UnicodeCategory.SpaceSeparator;
    private static bool Punctuation(UnicodeCategory value) => value is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or
        UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or
        UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
}
