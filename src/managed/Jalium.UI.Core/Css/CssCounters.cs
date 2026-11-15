using System.Runtime.CompilerServices;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

internal readonly record struct CssCounterOperation(string Name, int Value, bool Reversed = false, bool Automatic = false);
internal sealed class CssCounterOperations(CssCounterOperation[] entries) : IEquatable<CssCounterOperations>
{
    internal static readonly CssCounterOperations None = new([]);
    internal readonly CssCounterOperation[] Entries = entries;
    public bool Equals(CssCounterOperations? other) => other is not null && Entries.AsSpan().SequenceEqual(other.Entries);
    public override bool Equals(object? obj) => obj is CssCounterOperations other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); foreach (var entry in Entries) hash.Add(entry); return hash.ToHashCode();
    }
}

internal sealed class CssQuotes(string mode, string[] pairs) : IEquatable<CssQuotes>
{
    internal static readonly CssQuotes Auto = new("auto", []);
    internal readonly string Mode = mode;
    internal readonly string[] Pairs = pairs;
    public bool Equals(CssQuotes? other) => other is not null && Mode == other.Mode && Pairs.AsSpan().SequenceEqual(other.Pairs);
    public override bool Equals(object? obj) => obj is CssQuotes other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Mode); foreach (var pair in Pairs) hash.Add(pair); return hash.ToHashCode();
    }
}

/// <summary>
/// Counters inherit identities from the previous sibling (or parent), and values in tree order.
/// Resolve after the cascade, in two passes: bind identities and automatic reversed starts, then
/// replay values and quotes. Earlier generated strings must not see later counter mutations.
/// </summary>
internal static class CssCounters
{
    internal static readonly DependencyProperty ResetProperty = Property("CssCounterReset");
    internal static readonly DependencyProperty IncrementProperty = Property("CssCounterIncrement");
    internal static readonly DependencyProperty SetProperty = Property("CssCounterSet");
    internal static readonly DependencyProperty QuotesProperty = DependencyProperty.RegisterAttached(
        "CssQuotes", typeof(CssQuotes), typeof(CssCounters),
        new FrameworkPropertyMetadata(CssQuotes.Auto, FrameworkPropertyMetadataOptions.Inherits, Changed));

    private static DependencyProperty Property(string name) => DependencyProperty.RegisterAttached(
        name, typeof(CssCounterOperations), typeof(CssCounters), new PropertyMetadata(CssCounterOperations.None, Changed));
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (e.Property != ResetProperty && target is UIElement visual)
        {
            for (var cur = visual.VisualParent; cur is not null; cur = cur.VisualParent)
            {
                if (cur is FrameworkElement fe &&
                    ((CssContainmentProperties.GetContain(fe) & CssContainFlags.Style) != 0 ||
                     CssContainmentProperties.GetContentVisibility(fe) is CssContentVisibility.Auto or CssContentVisibility.Hidden))
                {
                    CssContainmentProperties.SyncStyleContainmentCounterScope(fe);
                }
            }
        }
        Invalidate(CssNode.Get(target));
    }

    private sealed class DispatcherState
    {
        internal HashSet<CssNode> Dirty = new();
        internal bool Resolving;
    }
    private sealed class Observation
    {
        internal Observation(CssNode node) => node.PropertyChangedInternal += (property, _, _) =>
        {
            if (property == UIElement.VisibilityProperty || property == FrameworkElement.LanguageProperty ||
                property == Jalium.UI.Documents.List.StartIndexProperty || property == Jalium.UI.Documents.List.MarkerStyleProperty ||
                property == CssDisplayProperties.SpecificationProperty) Invalidate(node);
        };
    }
    private static readonly ConditionalWeakTable<Dispatcher, DispatcherState> States = new();
    private static readonly ConditionalWeakTable<CssNode, Observation> Observations = new();
    private static readonly ConditionalWeakTable<CssNode, object> ActiveRoots = new();
    private static bool s_active;

    internal static void Invalidate(CssNode node)
    {
        s_active = true;
        ActiveRoots.GetValue(CssMatcher.Root(node), static _ => new());
        NoteEvaluated(node);
        CssEvaluationScheduler.InvalidateElement(node);
    }

    internal static void NoteEvaluated(CssNode node)
    {
        if (!s_active) return;
        var root = CssMatcher.Root(node);
        if (node.Target is CssGeneratedText && node.GetValue(CssGeneratedContent.ContentProperty) is
            CssGeneratedContentValue { Sequence: not null } or CssGeneratedContentValue { AlternativeSequence: not null })
            ActiveRoots.GetValue(root, static _ => new());
        if (ActiveRoots.TryGetValue(root, out _)) States.GetValue(node.Dispatcher, static _ => new()).Dirty.Add(root);
    }

    internal static bool HasPending(Dispatcher dispatcher)
        => s_active && States.TryGetValue(dispatcher, out var state) && state.Dirty.Count > 0;

    internal static void Flush(Dispatcher dispatcher)
    {
        if (!s_active || !States.TryGetValue(dispatcher, out var state) || state.Resolving || state.Dirty.Count == 0) return;
        state.Resolving = true;
        var roots = state.Dirty; state.Dirty = new();
        try
        {
            var visited = new HashSet<CssNode>();
            foreach (var root in roots)
                if (visited.Add(CssMatcher.Root(root))) Resolve(CssMatcher.Root(root));
        }
        finally { state.Resolving = false; }
    }

    internal static void Register()
    {
        RegisterOperations("counter-reset", ResetProperty, reset: true);
        RegisterOperations("counter-increment", IncrementProperty, increment: true);
        RegisterOperations("counter-set", SetProperty);
        CssPropertyRegistry.Register(new()
        {
            Name = "quotes", Kind = CssPropertyKind.Longhand, StorageProperty = QuotesProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (reader.TryReadIdent(out var ident))
                {
                    var mode = ident.ToString().ToLowerInvariant();
                    return reader.AtEnd && mode is "auto" or "none" or "match-parent"
                        ? new CssImmediateValue(QuotesProperty, mode == "auto" ? CssQuotes.Auto : new CssQuotes(mode, [])) : null;
                }
                var pairs = new List<string>();
                while (reader.TryReadString(out var value)) pairs.Add(value);
                return reader.AtEnd && pairs.Count > 0 && pairs.Count % 2 == 0
                    ? new CssImmediateValue(QuotesProperty, new CssQuotes("strings", pairs.ToArray())) : null;
            },
        });
    }

    private static void RegisterOperations(string name, DependencyProperty property, bool reset = false, bool increment = false)
        => CssPropertyRegistry.Register(new()
        {
            Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = property,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var keyword) && keyword.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
                { reader = probe; return new CssImmediateValue(property, CssCounterOperations.None); }
                var values = new List<CssCounterOperation>();
                while (!reader.AtEnd)
                {
                    string counter; var reversed = false;
                    if (reader.TryReadIdent(out var ident))
                    { if (!IsName(ident)) return null; counter = ident.ToString(); }
                    else if (reset && reader.TryReadFunction(out var function, out var args) &&
                        function.Equals("reversed", StringComparison.OrdinalIgnoreCase) && args.TryReadIdent(out ident) && IsName(ident) && args.AtEnd)
                    { reversed = true; counter = ident.ToString(); }
                    else return null;
                    var explicitValue = ReadInteger(ref reader, out var value);
                    if (!explicitValue) value = increment ? 1 : 0;
                    values.Add(new(counter, value, reversed, reversed && !explicitValue));
                }
                return values.Count > 0 ? new CssImmediateValue(property, new CssCounterOperations(values.ToArray())) : null;
            },
        });

    internal static bool ReadInteger(ref CssTokenReader reader, out int value)
    {
        value = 0;
        var probe = reader; var input = probe.Remaining; var start = probe.Position;
        if (!probe.TryReadNumber(out var number, out var unit) || unit != CssUnit.None || double.IsNaN(number)) return false;
        var token = input[..(probe.Position - start)];
        if (probe.NumberWasCalculated) number = Math.Floor(number + .5);
        else if (number != Math.Truncate(number) || token.Contains('.') || token.Contains('e') || token.Contains('E')) return false;
        value = (int)Math.Clamp(number, int.MinValue, int.MaxValue);
        reader = probe; return true;
    }

    internal static bool IsName(ReadOnlySpan<char> name) => name.Length > 0 &&
        name.ToString().ToLowerInvariant() is not ("none" or "default" or "initial" or "inherit" or "unset" or "revert" or "revert-layer");

    private sealed class Counter(string name, Frame creator, int initial, bool automatic, bool reversed)
    {
        internal readonly string Name = name;
        internal readonly Frame Creator = creator;
        internal int Initial = initial, Value;
        internal readonly bool Automatic = automatic;
        internal readonly bool Reversed = reversed;
        internal List<AutoEvent>? Events;
    }
    private readonly record struct CounterAction(Counter Counter, int Value, bool Set);
    private sealed class AutoEvent(Frame frame)
    {
        internal readonly Frame Frame = frame;
        internal long Increment;
        internal int? Set;
    }
    private sealed class Frame(CssNode node, Frame? parent, Frame? previous)
    {
        internal readonly CssNode Node = node;
        internal readonly Frame? Parent = parent;
        internal readonly List<Counter> Counters = new(previous?.Counters ?? parent?.Counters ?? []);
        internal readonly List<Counter> Births = new();
        internal readonly List<CounterAction> Actions = new();
        internal string[] Quotes = [];
    }

    private static void Resolve(CssNode root)
    {
        var styles = CssCounterStyles.For(root);
        var frames = new List<Frame>(); var instances = new List<Counter>();
        var lastChild = new Dictionary<Frame, Frame>(); var visited = new HashSet<CssNode>();
        var pending = new Stack<(CssNode Node, Frame? Parent)>(); pending.Push((root, null));
        while (pending.TryPop(out var next))
        {
            var node = next.Node;
            if (!visited.Add(node)) continue;
            if (CssReplacedContent.IsSuppressed(node.Target)) continue;
            if (CssDisplayProperties.SuppressesBoxTree(node.Target)) continue;
            Observations.GetValue(node, static n => new(n));
            var automaticMarker = node.Target is CssGeneratedText { Kind: var kind, Selected: true } && kind.IsMarker() &&
                node.GetValue(CssGeneratedContent.ContentProperty) is CssGeneratedContentValue { Text: null, IsNone: false };
            if (node.Target is UIElement { Visibility: Visibility.Collapsed } && !automaticMarker) continue;
            Frame? previous = null;
            if (next.Parent is { } parent) lastChild.TryGetValue(parent, out previous);
            var frame = new Frame(node, next.Parent, previous);
            if (next.Parent is not null) lastChild[next.Parent] = frame;
            frames.Add(frame);
            frame.Quotes = ResolveQuotes(frame);
            var resets = ((CssCounterOperations)node.GetValue(ResetProperty)!).Entries;
            if (node.Target is Jalium.UI.Documents.List nativeList && node.Target.GetEffectiveValueLayer(ResetProperty) is null)
                Instantiate(frame, "list-item", Clamp((long)nativeList.StartIndex - 1), false, instances);
            for (var i = 0; i < resets.Length; i++)
            {
                var operation = resets[i];
                // A duplicate reset has one effect, from its last occurrence.
                if (Array.FindIndex(resets, i + 1, other => other.Name == operation.Name) >= 0) continue;
                Instantiate(frame, operation.Name, operation.Value, operation.Automatic, instances, operation.Reversed);
            }
            ApplyOperations(frame, IncrementProperty, false, instances);
            if (CssListProperties.IsItem(node) && !((CssCounterOperations)node.GetValue(IncrementProperty)!).Entries.Any(e => e.Name == "list-item"))
            {
                var counter = Ensure(frame, "list-item", instances);
                RecordAction(frame, counter, counter.Reversed ? -1 : 1, false);
            }
            ApplyOperations(frame, SetProperty, true, instances);
            if (node.Target is CssGeneratedText || CssReplacedContent.IsActive(node.Target))
            {
                var content = (CssGeneratedContentValue)node.GetValue(CssGeneratedContent.ContentProperty)!;
                Bind(content.Sequence); Bind(content.AlternativeSequence);
                if (automaticMarker) Ensure(frame, "list-item", instances);
            }

            if (node.Target is CssGeneratedText generatedText)
            {
                if (CssGeneratedContent.Get(generatedText)?.Marker is { } nestedMarker) pending.Push((CssNode.Get(nestedMarker), frame));
                continue;
            }
            foreach (var after in CssGeneratedContent.Afters(node).Reverse()) pending.Push((CssNode.Get(after), frame));
            foreach (var child in CssMatcher.Children(node).Reverse()) pending.Push((child, frame));
            foreach (var before in CssGeneratedContent.Befores(node).Reverse()) pending.Push((CssNode.Get(before), frame));
            foreach (var marker in CssGeneratedContent.Markers(node).Reverse()) pending.Push((CssNode.Get(marker), frame));

            void Bind(CssContentSequence? sequence)
            {
                if (sequence is null) return;
                foreach (var part in sequence.Parts)
                    if (part.Kind is CssContentKind.Counter or CssContentKind.Counters) Ensure(frame, part.Text, instances);
            }
        }

        foreach (var instance in instances)
        {
            if (!instance.Automatic) continue;
            long total = 0, last = 0;
            if (instance.Events is { } events)
                foreach (var entry in events)
                {
                    var negated = -entry.Increment;
                    if (negated != 0) last = negated;
                    if (entry.Set is { } set) { total += set; break; }
                    total += negated;
                }
            instance.Initial = Clamp(total + last);
        }
        var quoteDepth = 0;
        foreach (var frame in frames)
        {
            foreach (var counter in frame.Births) counter.Value = counter.Initial;
            foreach (var action in frame.Actions)
                action.Counter.Value = action.Set ? action.Value : Clamp((long)action.Counter.Value + action.Value);
            if (CssReplacedContent.IsActive(frame.Node.Target))
            {
                var replacement = (CssGeneratedContentValue)frame.Node.Target.GetValue(CssGeneratedContent.ContentProperty)!;
                CssReplacedContent.SetAlternative(frame.Node.Target, ResolveText(replacement.AlternativeSequence, frame, styles, ref quoteDepth, out _) ?? replacement.Alternative);
                continue;
            }
            if (frame.Node.Target is not CssGeneratedText text) continue;
            var content = (CssGeneratedContentValue)text.GetValue(CssGeneratedContent.ContentProperty)!;
            if (text.Kind.IsMarker() && content is { Text: null, IsNone: false })
            {
                var listItemValue = frame.Counters.Last(c => c.Name == "list-item").Value;
                var defaultText = CssListProperties.DefaultMarker(text, styles, listItemValue, out var symbolParts, out var spokenText);
                text.SetAutomaticImageMarker(defaultText,
                    text.Origin.GetValue(CssListProperties.ImageProperty) as CssImageValue, symbolParts, spokenText);
                continue;
            }
            var fragments = new List<CssResolvedContent>();
            var resolvedText = ResolveText(content.Sequence, frame, styles, ref quoteDepth, out var spokenSequence, fragments);
            var hasImages = fragments.Any(part => part.Image is not null);
            text.SetResolvedContent(resolvedText ?? content.Text ?? string.Empty,
                ResolveText(content.AlternativeSequence, frame, styles, ref quoteDepth, out _) ?? content.Alternative,
                hasImages ? fragments.ToArray() : null, spokenSequence);
        }
    }

    private static Counter Instantiate(Frame frame, string name, int initial, bool automatic, List<Counter> instances, bool reversed = false)
    {
        var inner = frame.Counters.FindLastIndex(c => c.Name == name);
        if (inner >= 0 && (frame.Counters[inner].Creator == frame ||
            frame.Parent is not null && frame.Counters[inner].Creator.Parent == frame.Parent)) frame.Counters.RemoveAt(inner);
        var counter = new Counter(name, frame, initial, automatic, reversed);
        frame.Counters.Add(counter); frame.Births.Add(counter); instances.Add(counter); return counter;
    }
    private static Counter Ensure(Frame frame, string name, List<Counter> instances)
        => frame.Counters.LastOrDefault(c => c.Name == name) ?? Instantiate(frame, name, 0, false, instances);

    private static void ApplyOperations(Frame frame, DependencyProperty property, bool set, List<Counter> instances)
    {
        foreach (var operation in ((CssCounterOperations)frame.Node.GetValue(property)!).Entries)
        {
            var counter = Ensure(frame, operation.Name, instances);
            RecordAction(frame, counter, operation.Value, set);
        }
    }

    private static void RecordAction(Frame frame, Counter counter, int value, bool set)
    {
        frame.Actions.Add(new(counter, value, set));
        if (!counter.Automatic) return;
        var events = counter.Events ??= new();
        var entry = events.LastOrDefault();
        if (entry?.Frame != frame) { entry = new(frame); events.Add(entry); }
        if (set) entry!.Set = value; else entry!.Increment += value;
    }

    private static string? ResolveText(CssContentSequence? sequence, Frame frame, CssCounterStyleMap styles, ref int depth,
        out string? spoken, List<CssResolvedContent>? fragments = null)
    {
        spoken = null;
        if (sequence is null) return null;
        var output = new StringBuilder();
        var spokenBuilder = new StringBuilder();
        var hasCustomSpoken = false;
        var start = 0;
        foreach (var part in sequence.Parts)
        {
            switch (part.Kind)
            {
                case CssContentKind.Text:
                    output.Append(part.Text);
                    spokenBuilder.Append(part.Text);
                    break;
                case CssContentKind.Image:
                    if (output.Length > start) fragments?.Add(new(output.ToString(start, output.Length - start)));
                    fragments?.Add(new(Image: part.Image)); start = output.Length; break;
                case CssContentKind.Counter:
                {
                    var val = frame.Counters.Last(c => c.Name == part.Text).Value;
                    var formatted = styles.FormatWithImages(val, part.Style, includeAffixes: false, out var counterParts);
                    if (counterParts is not null && fragments is not null)
                    {
                        if (output.Length > start) fragments.Add(new(output.ToString(start, output.Length - start)));
                        fragments.AddRange(counterParts);
                        output.Append(formatted);
                        start = output.Length;
                    }
                    else output.Append(formatted);
                    if (styles.FormatSpoken(val, part.Style, includeAffixes: false) is { } counterSpoken)
                    { hasCustomSpoken = true; spokenBuilder.Append(counterSpoken); }
                    else spokenBuilder.Append(formatted);
                    break;
                }
                case CssContentKind.Counters:
                {
                    var matching = frame.Counters.Where(c => c.Name == part.Text).ToArray();
                    for (var i = 0; i < matching.Length; i++)
                    {
                        if (i > 0) { output.Append(part.Separator); spokenBuilder.Append(part.Separator); }
                        var val = matching[i].Value;
                        var formatted = styles.FormatWithImages(val, part.Style, includeAffixes: false, out var counterParts);
                        if (counterParts is not null && fragments is not null)
                        {
                            if (output.Length > start) fragments.Add(new(output.ToString(start, output.Length - start)));
                            fragments.AddRange(counterParts);
                            output.Append(formatted);
                            start = output.Length;
                        }
                        else output.Append(formatted);
                        if (styles.FormatSpoken(val, part.Style, includeAffixes: false) is { } counterSpoken)
                        { hasCustomSpoken = true; spokenBuilder.Append(counterSpoken); }
                        else spokenBuilder.Append(formatted);
                    }
                    break;
                }
                case CssContentKind.OpenQuote:
                {
                    var q = Quote(frame.Quotes, depth, false);
                    output.Append(q); spokenBuilder.Append(q); depth = Clamp((long)depth + 1); break;
                }
                case CssContentKind.NoOpenQuote: depth = Clamp((long)depth + 1); break;
                case CssContentKind.CloseQuote:
                    if (depth > 0) { depth--; var q = Quote(frame.Quotes, depth, true); output.Append(q); spokenBuilder.Append(q); } break;
                case CssContentKind.NoCloseQuote: if (depth > 0) depth--; break;
            }
        }
        if (output.Length > start) fragments?.Add(new(output.ToString(start, output.Length - start)));
        if (hasCustomSpoken) spoken = spokenBuilder.ToString();
        return output.ToString();
    }

    private static string Quote(string[] pairs, int depth, bool close)
        => pairs.Length == 0 ? string.Empty : pairs[Math.Min(depth, pairs.Length / 2 - 1) * 2 + (close ? 1 : 0)];

    private static string[] ResolveQuotes(Frame frame)
    {
        var quotes = (CssQuotes)frame.Node.GetValue(QuotesProperty)!;
        if (quotes.Mode == "strings") return quotes.Pairs;
        if (quotes.Mode == "none") return [];
        if (quotes.Mode == "match-parent" && frame.Parent is { } parent) return parent.Quotes;
        var languageNode = frame.Parent?.Node ?? frame.Node;
        var language = (languageNode.GetValue(FrameworkElement.LanguageProperty) as XmlLanguage)?.IetfLanguageTag ?? "en-US";
        return language.Split('-')[0].ToLowerInvariant() switch
        {
            "ja" => ["「", "」", "『", "』"],
            "fr" => ["«", "»", "‹", "›"],
            "de" => ["„", "“", "‚", "‘"],
            "ru" => ["«", "»", "„", "“"],
            _ => ["“", "”", "‘", "’"],
        };
    }

    private static int Clamp(long number) => (int)Math.Clamp(number, int.MinValue, int.MaxValue);
}
