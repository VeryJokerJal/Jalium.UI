using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

internal sealed partial class WritingToolsDocumentFragment
{
    private WholeInlineSource? _wholeInlineSource;
    private sealed record SourceLeaf(Inline Element, int Start, int Length, string? Context, SourceParagraph Source);
    private sealed record RunUpdate(Run Target, string Text, Run Incoming, JsonElement? Before, JsonElement After);
    private sealed record ParagraphUpdate(Paragraph Built, Paragraph Target, Inline[] Children, Paragraph? Before);
    private sealed class SourceParagraph(Paragraph paragraph, Row row)
    {
        internal readonly Paragraph Paragraph = paragraph;
        internal readonly Row Row = row;
        internal readonly List<SourceLeaf> Leaves = [];
        internal readonly List<(int Start, int Length, JsonElement Style)> Styles = [];
        internal readonly Dictionary<string, Queue<SourceLeaf>> ByContext = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, SourceLeaf> Templates = new(StringComparer.Ordinal);
        internal SourceLeaf? At(int offset) => Find(Leaves, offset, value => (value.Start, value.Length));
        internal JsonElement? StyleAt(int offset)
        {
            int low = 0, high = Styles.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2; var item = Styles[mid];
                if (offset < item.Start) high = mid - 1;
                else if (offset >= item.Start + item.Length) low = mid + 1;
                else return item.Style;
            }
            return null;
        }
        private static T? Find<T>(List<T> values, int offset, Func<T, (int Start, int Length)> range) where T : class
        {
            int low = 0, high = values.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2; var item = values[mid]; var part = range(item);
                if (offset < part.Start) high = mid - 1;
                else if (offset >= part.Start + part.Length) low = mid + 1;
                else return item;
            }
            return null;
        }
    }

    private static string? ValidateInlineContext(JsonElement style)
    {
        if (!style.TryGetProperty("inlineContext", out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 4096 } context)
            throw new FormatException("Invalid inline provenance.");
        var path = context.Split('/');
        if (path.Length > 64 || path.Any(id => !Guid.TryParseExact(id, "N", out _)) || path.Distinct().Count() != path.Length)
            throw new FormatException("Invalid inline ancestry.");
        return context;
    }

    // Preparation is read-only. Stable identities refer only to the captured
    // source tree; mutations remain inside the editor's snapshot transaction.
    private bool PrepareWholeInlineSource(FlowDocument document, WritingToolsDocumentFragment original, int sourceStart)
    {
        var spans = new Dictionary<string, Span>(StringComparer.Ordinal);
        var sources = new Dictionary<Paragraph, SourceParagraph>(ReferenceEqualityComparer.Instance);
        var originals = new Dictionary<string, SourceParagraph>(StringComparer.Ordinal);
        var contexts = new Dictionary<string, Queue<SourceLeaf>>(StringComparer.Ordinal);
        var templates = new Dictionary<string, SourceLeaf>(StringComparer.Ordinal);
        foreach (var row in original._rows)
        {
            if (row.Paragraph is null) continue;
            var paragraph = document.GetPositionAtOffset(sourceStart + row.Start, LogicalDirection.Forward)?.Paragraph;
            if (paragraph is null) return false;
            if (!sources.TryGetValue(paragraph, out var source))
            {
                source = new(paragraph, row); int offset = 0;
                foreach (var run in row.Paragraph.Inlines.OfType<Run>())
                {
                    if (original._runStyles.TryGetValue(run, out var style)) source.Styles.Add((offset, run.Text.Length, style));
                    offset += run.Text.Length;
                }
                offset = 0;
                if (!Visit(paragraph.Inlines, null, 0)) return false;
                sources.Add(paragraph, source);
                bool Visit(IEnumerable<Inline> children, string? context, int depth)
                {
                    foreach (var child in children)
                        if (child is Span span)
                        {
                            if (depth == 64) return false;
                            string identity = OriginFor(span); spans[identity] = span;
                            if (!Visit(span.Inlines, context is null ? identity : context + "/" + identity, depth + 1)) return false;
                        }
                        else
                        {
                            int length = TextElement.GetContentLength(child);
                            var leaf = new SourceLeaf(child, offset, length, context, source);
                            source.Leaves.Add(leaf);
                            if (!source.ByContext.TryGetValue(context ?? "", out var local)) source.ByContext[context ?? ""] = local = new();
                            local.Enqueue(leaf); source.Templates.TryAdd(context ?? "", leaf);
                            if (context is not null)
                            {
                                if (!contexts.TryGetValue(context, out var global)) contexts[context] = global = new();
                                global.Enqueue(leaf); templates.TryAdd(context, leaf);
                            }
                            // A stale/forged source identity must never resolve to
                            // a different semantic inline with the same text.
                            for (int at = offset; at < offset + length;)
                            {
                                var style = source.StyleAt(at);
                                if (style is null) return false;
                                string? claimed = ValidateInlineContext(style.Value);
                                if (claimed is not null && claimed != context) return false;
                                int end = style.Value.GetProperty("start").GetInt32() + style.Value.GetProperty("length").GetInt32() - row.Start;
                                if (end <= at) return false;
                                at = Math.Min(offset + length, end);
                            }
                            offset += length;
                        }
                    return true;
                }
            }
            if (row.Origin is { } id) originals[id] = source;
        }
        if (spans.Count == 0) return !_runStyles.Values.Any(style => ValidateInlineContext(style) is not null);
        var plan = new WholeInlineSource(this, document);
        foreach (var source in sources.Values)
        {
            plan.Remember(source.Paragraph);
            foreach (var leaf in source.Leaves) plan.Remember(leaf.Element);
        }
        foreach (var span in spans.Values) plan.Remember(span);
        var used = new HashSet<Inline>(ReferenceEqualityComparer.Instance);
        var assignedSpans = new Dictionary<Span, Paragraph>(ReferenceEqualityComparer.Instance);
        var assignedParagraphs = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        bool carriesContexts = _runStyles.Values.Any(style => ValidateInlineContext(style) is not null);
        foreach (var row in _rows)
        {
            if (row.Paragraph is not { } built) continue;
            SourceParagraph? source = row.Origin is { } id && originals.TryGetValue(id, out var found) ? found : null;
            if (row.Origin is not null && source is null) return false;
            if (source is null && sources.Count == 1 && original._rows.Count == 1) source = sources.Values.Single();
            bool unchanged = source is not null && original._text.AsSpan(source.Row.Start, source.Row.Length).SequenceEqual(_text.AsSpan(row.Start, row.Length));
            if (unchanged && source is not null && TextElement.GetContentLength(source.Paragraph) == 0 && assignedParagraphs.Add(source.Paragraph))
            {
                plan.Paragraphs.Add(new(built, source.Paragraph, source.Paragraph.Inlines.ToArray(), source.Row.Paragraph));
                continue;
            }
            bool sourceHasContexts = source?.ByContext.Keys.Any(key => key.Length != 0) == true;
            var roots = new List<Inline>(); int offset = 0;
            foreach (var incoming in built.Inlines.OfType<Run>())
            {
                if (incoming.Text.Length == 0) continue;
                if (!_runStyles.TryGetValue(incoming, out var style)) return false;
                string? context = ValidateInlineContext(style); int through = 0;
                // Older attributed replacements may omit private attributes. A
                // uniform source path is unambiguous; mixed paths must not flatten.
                if (!unchanged && context is null && source is not null && sourceHasContexts)
                {
                    if (source.ByContext.Count == 1) context = source.ByContext.Keys.Single();
                    else if (!carriesContexts) return false;
                }
                do
                {
                    int absolute = offset + through;
                    var leaf = unchanged ? source?.At(absolute) : null;
                    int count = leaf is not null ? Math.Min(incoming.Text.Length - through, leaf.Start + leaf.Length - absolute) : incoming.Text.Length - through;
                    string? path = context ?? leaf?.Context;
                    if (leaf?.Context != path) leaf = null;
                    var owner = roots; Span? previous = null;
                    foreach (string identity in path?.Split('/') ?? [])
                    {
                        if (!spans.TryGetValue(identity, out var span) ||
                            previous is not null && !ReferenceEquals(span.Parent, previous)) return false;
                        if (previous is null && span.Parent is Span) return false;
                        if (assignedSpans.TryGetValue(span, out var assigned) && !ReferenceEquals(assigned, built)) return false;
                        assignedSpans[span] = built;
                        if (!plan.Children.TryGetValue(span, out var children))
                        { owner.Add(span); plan.Children.Add(span, children = []); plan.SpanOrder.Add(span); }
                        else if (owner.Count == 0 || !ReferenceEquals(owner[^1], span)) return false;
                        owner = children; previous = span;
                    }
                    Queue<SourceLeaf>? candidates = null;
                    if (path is not null) contexts.TryGetValue(path, out candidates);
                    else source?.ByContext.TryGetValue("", out candidates);
                    while (candidates?.Count > 0 && used.Contains(candidates.Peek().Element)) candidates.Dequeue();
                    leaf ??= candidates?.Count > 0 ? candidates.Dequeue() : null;
                    leaf ??= path is not null ? templates.GetValueOrDefault(path) : source?.Templates.GetValueOrDefault("");
                    string text = incoming.Text.Substring(through, count);
                    Inline target;
                    if (leaf is { Element: Run oldRun } && used.Add(oldRun)) target = oldRun;
                    else if (unchanged && leaf is { Element: LineBreak or InlineUIContainer } && count == leaf.Length)
                    { target = leaf.Element; if (!used.Add(target)) return false; }
                    else
                    {
                        var fresh = new Run(text);
                        if (leaf is { Element: { } template })
                            DocumentTextEditing.ApplyCharacterFormatting(fresh, DocumentTextEditing.CaptureCharacterFormatting(template), false);
                        target = fresh; plan.Remember(fresh);
                    }
                    if (target is Run run)
                    {
                        JsonElement? beforeStyle = leaf?.Source.StyleAt(unchanged ? absolute : leaf.Start);
                        if (leaf is not null && !WholeInlineSource.CanPreserveAncestorPainting(leaf.Element, beforeStyle, style)) return false;
                        plan.Runs.Add(new(run, text, incoming, beforeStyle, style));
                    }
                    owner.Add(target); through += count;
                } while (through < incoming.Text.Length);
                offset += incoming.Text.Length;
            }
            Paragraph destination = source is not null && assignedParagraphs.Add(source.Paragraph) ? source.Paragraph : built;
            plan.Paragraphs.Add(new(built, destination, roots.ToArray(), source?.Row.Paragraph));
        }
        _wholeInlineSource = plan; return true;
    }

    private sealed class WholeInlineSource(WritingToolsDocumentFragment fragment, FlowDocument document)
    {
        internal readonly Dictionary<Span, List<Inline>> Children = new(ReferenceEqualityComparer.Instance);
        internal readonly List<Span> SpanOrder = [];
        internal readonly List<RunUpdate> Runs = [];
        internal readonly List<ParagraphUpdate> Paragraphs = [];
        private readonly Dictionary<TextElement, DependencyObject?> _parents = new(ReferenceEqualityComparer.Instance);
        private bool _applied;
        private static readonly (string Name, DependencyProperty Property)[] Properties =
        [
            ("family", TextElement.FontFamilyProperty), ("size", TextElement.FontSizeProperty),
            ("weight", TextElement.FontWeightProperty), ("italic", TextElement.FontStyleProperty),
            ("foreground", TextElement.ForegroundProperty), ("background", TextElement.BackgroundProperty),
            ("language", FrameworkContentElement.LanguageProperty)
        ];
        private static readonly string[] Decorations = ["underline", "underlineColor", "strike", "strikeColor"];

        internal static bool CanPreserveAncestorPainting(Inline source, JsonElement? before, JsonElement after)
        {
            if (Changed(before, after, "background") &&
                (!after.TryGetProperty("background", out var background) || background.ValueKind != JsonValueKind.Number || background.GetUInt32() >> 24 != 255))
                for (var parent = source.Parent as Inline; parent is not null; parent = parent.Parent as Inline)
                    if (parent.Background is { Opacity: > 0 } brush && (brush is not SolidColorBrush solid || solid.Color.A != 0)) return false;
            var inherited = AutomationTextStyleFactory.Decorations(default, source.Parent);
            return !(inherited.Underline != 0 && (Changed(before, after, "underline") || Changed(before, after, "underlineColor")) ||
                inherited.Strikethrough != 0 && (Changed(before, after, "strike") || Changed(before, after, "strikeColor")));
        }

        internal void Remember(TextElement element)
        {
            for (TextElement? item = element; item is not null && !_parents.ContainsKey(item); item = item.Parent as TextElement)
                _parents.Add(item, item.Parent);
        }
        private bool Owned(TextElement element)
        {
            if (element.GetFlowDocument() is { } owner && !ReferenceEquals(owner, document)) return false;
            for (TextElement? item = element; item is not null; item = item.Parent as TextElement)
                if (_parents.TryGetValue(item, out var expected) && !ReferenceEquals(item.Parent, expected)) return false;
            return true;
        }
        private bool Live(TextElement element, Func<bool> current) => current() && Owned(element);
        private void Parent(TextElement element, DependencyObject? parent)
        { if (_parents.ContainsKey(element)) _parents[element] = parent; }
        internal bool BeforeClear(Func<bool> current)
        {
            if (!current() || _parents.Keys.Any(item => !Owned(item))) return false;
            foreach (var block in document.Blocks) Parent(block, null);
            return true;
        }
        internal bool Attach(Block block, Func<bool> current)
        {
            if (!Live(block, current)) return false;
            Parent(block, null); document.Blocks.Add(block);
            return Live(block, current);
        }
        internal bool Complete(Func<bool> current) => current() && _parents.Keys.All(Owned);
        internal bool Apply(Func<bool> current)
        {
            if (_applied) return false;
            _applied = true;
            foreach (var update in Runs)
            {
                if (!Live(update.Target, current)) return false;
                if (update.Target.Text != update.Text) update.Target.Text = update.Text;
                foreach (var (name, property) in Properties)
                    if (Changed(update.Before, update.After, name))
                    {
                        if (!Live(update.Target, current)) return false;
                        update.Target.SetValue(property, update.Incoming.GetValue(property));
                    }
                if (Decorations.Any(name => Changed(update.Before, update.After, name)))
                {
                    var decorations = new TextDecorationCollection();
                    bool underline = Changed(update.Before, update.After, "underline") || Changed(update.Before, update.After, "underlineColor");
                    bool strike = Changed(update.Before, update.After, "strike") || Changed(update.Before, update.After, "strikeColor");
                    foreach (var decoration in update.Target.TextDecorations ?? [])
                        if (!(underline && decoration.Location == TextDecorationLocation.Underline ||
                            strike && decoration.Location == TextDecorationLocation.Strikethrough)) decorations.Add(decoration);
                    foreach (var decoration in update.Incoming.TextDecorations ?? [])
                        if (underline && decoration.Location == TextDecorationLocation.Underline ||
                            strike && decoration.Location == TextDecorationLocation.Strikethrough) decorations.Add(decoration);
                    if (!Live(update.Target, current)) return false; update.Target.TextDecorations = decorations;
                }
                if (!Live(update.Target, current)) return false;
            }
            for (int at = SpanOrder.Count - 1; at >= 0; --at)
                if (!SetChildren(SpanOrder[at].Inlines, Children[SpanOrder[at]], current)) return false;
            foreach (var entry in Paragraphs)
            {
                if (!SetChildren(entry.Target.Inlines, entry.Children, current)) return false;
                if (ReferenceEquals(entry.Target, entry.Built)) continue;
                if (entry.Before?.TextAlignment != entry.Built.TextAlignment)
                { if (!Live(entry.Target, current)) return false; entry.Target.TextAlignment = entry.Built.TextAlignment; }
                if (entry.Before?.FlowDirection != entry.Built.FlowDirection)
                { if (!Live(entry.Target, current)) return false; entry.Target.FlowDirection = entry.Built.FlowDirection; }
                if (!Live(entry.Target, current)) return false;
                var oldOwner = entry.Target.OwnerCollection; Parent(entry.Target, null); oldOwner?.Remove(entry.Target);
                if (!Live(entry.Target, current)) return false;
                if (entry.Built.OwnerCollection is { } collection)
                {
                    int index = collection.IndexOf(entry.Built); collection.Remove(entry.Built);
                    if (!Live(entry.Target, current)) return false;
                    Parent(entry.Target, collection.Parent as TextElement); collection.Insert(index, entry.Target);
                    if (!Live(entry.Target, current)) return false;
                }
                else
                {
                    int index = fragment.Blocks.IndexOf(entry.Built); if (index < 0) return false;
                    fragment.Blocks[index] = entry.Target;
                }
            }
            return current() && _parents.Keys.All(Owned);
        }

        private bool SetChildren(InlineCollection destination, IEnumerable<Inline> desired, Func<bool> current)
        {
            if (destination.Parent is not { } parent || !Live(parent, current)) return false;
            var items = desired.ToArray();
            if (destination.SequenceEqual(items)) return true;
            foreach (var old in destination) Parent(old, null);
            destination.Clear();
            foreach (var item in items)
            {
                if (!Live(parent, current) || !Owned(item)) return false;
                var oldOwner = item.OwnerCollection; Parent(item, null); oldOwner?.Remove(item);
                if (!Live(parent, current) || !Owned(item)) return false;
                Parent(item, parent); destination.Add(item);
                if (!Live(item, current) || !Owned(parent)) return false;
            }
            return Live(parent, current);
        }

        private static bool Changed(JsonElement? before, JsonElement after, string name)
        {
            if (before is not { } original) return true;
            bool a = original.TryGetProperty(name, out var left), b = after.TryGetProperty(name, out var right);
            if (!a || left.ValueKind == JsonValueKind.Null) return b && right.ValueKind != JsonValueKind.Null;
            if (!b || right.ValueKind == JsonValueKind.Null || left.ValueKind != right.ValueKind) return true;
            return left.ValueKind == JsonValueKind.Number ? left.GetDouble() != right.GetDouble() : left.GetRawText() != right.GetRawText();
        }
    }
}
