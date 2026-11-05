using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>Fictional first-line inheritance without changing the authored document tree.</summary>
internal static class CssFirstLine
{
    private sealed class Target { internal bool Potential; internal int Signature; }
    private sealed class Projection(CssNode original, DependencyObject parent, CssPseudoElement kind, bool alias, Context context)
    {
        internal readonly CssNode Original = original;
        internal readonly DependencyObject Parent = parent;
        internal readonly CssPseudoElement Kind = kind;
        internal readonly bool Alias = alias;
        internal readonly Context Context = context;
        internal bool Refreshing;
        internal int Generation = -1;
        internal readonly HashSet<DependencyProperty> Native = [];
    }
    private static readonly ConditionalWeakTable<DependencyObject, Target> s_targets = new();
    private static readonly ConditionalWeakTable<DependencyObject, Projection> s_projections = new();
    private static readonly ConditionalWeakTable<CssNode, Context> s_contexts = new();
    private static bool s_active;
    private static int s_revision;
    private sealed class PendingLayout { internal readonly HashSet<TextBlock> Text = []; }
    private static readonly ConditionalWeakTable<Dispatcher, PendingLayout> s_pending = new();
    internal static int Revision => Volatile.Read(ref s_revision);
    internal static void ContentChanged() { if (s_active) Interlocked.Increment(ref s_revision); }
    internal static void StructureChanged(DependencyObject source, DependencyObject? previousParent = null)
    {
        if (source is ICssStyledBox or ICssAnonymousBox or ICssGeneratedContentBox or CssGeneratedText ||
            source is TextBlock { IsCssTypographyProjection: true }) return;
        ContentChanged();
        CssFirstLetter.ContentChanged();
        // Removing the first block can change the next sibling's fictional
        // inheritance while its own properties and measure constraint stay
        // equal. Invalidate consumers under both sides of the tree change.
        var parent = CssNode.Get(source).FrameworkParent?.Target;
        foreach (var target in new[] { parent, previousParent }.Distinct())
            if (target is Visual visual && (HasTargetOrAncestor(target) || CssFirstLetter.HasTargetOrAncestor(target)))
                CssFlowProperties.InvalidateInheritedTextLayout(visual);
    }
    internal static int OriginSignature(CssNode origin, int rules)
    {
        var hash = new HashCode();
        hash.Add(rules); hash.Add(CssEngine.CascadeVersion);
        hash.Add(origin.CssRuntimeState?.InlineText, StringComparer.Ordinal);
        if (origin.CssRuntimeState?.CustomProperties is { } custom)
            foreach (var entry in custom.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hash.Add(entry.Key, StringComparer.Ordinal); hash.Add(entry.Value, StringComparer.Ordinal);
            }
        return hash.ToHashCode();
    }
    internal static bool HasTargetOrAncestor(DependencyObject target)
    {
        if (!s_active) return false;
        var depth = 0;
        for (var node = CssNode.Get(target); node is not null && depth++ < 4096; node = node.FrameworkParent)
            if (s_targets.TryGetValue(node.Target, out var value) && value.Potential) return true;
        return false;
    }
    internal static bool HasPending(Dispatcher dispatcher) => s_active && s_pending.TryGetValue(dispatcher, out var pending) && pending.Text.Count > 0;
    internal static void ScheduleLayout(TextBlock text)
    {
        var dispatcher = text.Dispatcher;
        if (dispatcher.HasShutdownStarted) return;
        var pending = s_pending.GetValue(dispatcher, static _ => new());
        var first = pending.Text.Count == 0;
        pending.Text.Add(text);
        if (first) dispatcher.BeginInvoke(DispatcherPriority.Render, () => FlushLayout(dispatcher));
    }
    internal static void FlushLayout(Dispatcher dispatcher)
    {
        if (!s_pending.TryGetValue(dispatcher, out var pending) || pending.Text.Count == 0) return;
        var text = pending.Text.ToArray(); pending.Text.Clear();
        foreach (var source in text) source.InvalidateFirstLineAllocation();
    }
    internal static bool IsProjection(CssNode node) => s_active && s_projections.TryGetValue(node.Target, out _);
    internal static CssPseudoElement Kind(DependencyObject target) => s_active && s_projections.TryGetValue(target, out var p) ? p.Kind : CssPseudoElement.None;
    internal static CssNode? Origin(DependencyObject target)
        => s_active && s_projections.TryGetValue(target, out var p)
            ? p.Alias ? CssGeneratedContent.Origin(p.Original) ?? p.Original : p.Original : null;
    internal static CssNode? RuleOrigin(CssNode node)
        => s_active && s_projections.TryGetValue(node.Target, out var p) ? p.Original : null;
    internal static CssNode? AliasOrigin(CssNode node)
        => s_active && s_projections.TryGetValue(node.Target, out var p) && p.Alias ? p.Original : null;
    internal static DependencyObject? StyleParent(DependencyObject target)
        => s_active && s_projections.TryGetValue(target, out var p) ? p.Parent : null;
    internal static CssNode? CustomParent(CssNode node)
        => s_active && s_projections.TryGetValue(node.Target, out var p)
            ? p.Alias ? p.Original.FrameworkParent : p.Original : null;
    internal static CssNode? InheritanceParent(CssNode node, string name)
        => !CssPropertyMetadata.IsInherited(name) || name is "direction" or "writing-mode" or "text-orientation"
            ? CustomParent(node) : null;

    internal static void Update(CssNode origin, CssPseudoElement targets, int signature)
    {
        if (origin.Target is ICssStyledBox or ICssAnonymousBox || origin.Target is TextBlock {IsCssTypographyProjection:true}) return;
        var selected = (targets & CssPseudoElement.FirstLine) != 0;
        var hadPotential = s_targets.TryGetValue(origin.Target, out var old) && old.Potential;
        if (hadPotential == selected && (!selected || old!.Signature == signature)) return;
        s_active = true;
        Interlocked.Increment(ref s_revision);
        var target = s_targets.GetOrCreateValue(origin.Target);
        target.Potential = selected; target.Signature = signature;
        if (origin.Target is TextElement text) text.NotifyTextContentChanged();
        else if (origin.Target is FlowDocument document) document.NotifyPaginationStyleChanged();
        else if (origin.Target is Visual visual) CssFlowProperties.InvalidateInheritedTextLayout(visual);
    }

    internal static Context? For(TextBlock text) => For(text.DocumentFontSource ?? text, text);
    internal static Context? For(DependencyObject content, TextBlock? text)
    {
        if (!s_active) return null;
        var source = CssNode.Get(content);
        var scope = source;
        for (var depth = 0; !BlockContainer(scope) && depth < 4096; depth++)
        {
            if (Barrier(scope) || scope.FrameworkParent is not { } parent) return null;
            scope = parent;
        }
        var origins = new List<CssNode>();
        var count = 0;
        for (var node = scope; node is not null && count++ < 4096; node = node.FrameworkParent)
        {
            if (s_targets.TryGetValue(node.Target, out var target) && target.Potential && BlockContainer(node) &&
                SameContext(FirstContext(node, 0), scope)) origins.Add(node);
            var display = CssDisplayProperties.Computed(node);
            if (node.CssLayout?.IsOutOfFlow == true || Floating(node) ||
                display.Inline && display.Mode is CssDisplayMode.FlowRoot or CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table) break;
        }
        if (origins.Count == 0) return null;
        origins.Reverse();
        var context = s_contexts.GetValue(scope, static root => new(root));
        context.Use(origins.ToArray(), text);
        return context;
    }

    private static bool SameContext(CssNode? a, CssNode? b) =>
        ReferenceEquals(a, b) ||
        a?.Target is CssGeneratedText ga && b?.Target is CssGeneratedText gb &&
        ReferenceEquals(ga.Origin, gb.Origin) && ga.Kind == gb.Kind;

    internal static bool HasNestedTargets(IEnumerable<Inline>? inlines, TextBlock consumer) => s_active && inlines is not null && inlines.Any(inline =>
        BlockContainer(CssNode.Get(inline)) && For(inline,consumer) is not null || inline is Span span && HasNestedTargets(span.Inlines,consumer));

    private static bool BlockContainer(CssNode node)
    {
        var display = CssDisplayProperties.Computed(node);
        if (display.None || display.Mode is CssDisplayMode.Contents or CssDisplayMode.Flex or CssDisplayMode.Grid or
            CssDisplayMode.Table or CssDisplayMode.TableRow or CssDisplayMode.TableRowGroup or CssDisplayMode.TableHeaderGroup or
            CssDisplayMode.TableFooterGroup or CssDisplayMode.TableColumn or CssDisplayMode.TableColumnGroup) return false;
        if (node.Target is Paragraph or ListItem or Section) return !display.Inline || display.Mode == CssDisplayMode.Native;
        if (node.Target is Inline) return !display.Inline && display.Mode != CssDisplayMode.Native || Floating(node);
        if (node.Target is TextBlock text) return !text.CssInlineOuter || display.Mode == CssDisplayMode.FlowRoot;
        return node.Target is FrameworkElement element && CssDisplayLayout.IsFlow(element) && !element.CssInlineOuter;
    }

    private static bool Barrier(CssNode node) => CssDisplayProperties.Computed(node).Mode is
        CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table;

    private static CssNode? FirstContext(CssNode node, int depth)
    {
        if (depth > 512 || Barrier(node)) return null;
        var children = new List<CssNode>();
        foreach (var before in CssGeneratedContent.Befores(node))
            children.Add(CssNode.Get(before));
        if (node.Target is Run) return node;
        if (node.Target is TextBlock text)
        {
            if (text.CssOriginalInlines is { } inlines) children.AddRange(inlines.Select(CssNode.Get));
            else if (text.Text.Length > 0)
            {
                foreach (var child in children)
                    if (!Ignored(child)) return CssDisplayProperties.Computed(child).Inline ? node : FirstContext(child, depth + 1);
                return node;
            }
        }
        else if (node.Target is ContentControl contentControl && contentControl.CssContentAdapter is { } adapter)
        {
            if (adapter.Text.Length > 0)
            {
                foreach (var child in children)
                    if (!Ignored(child)) return CssDisplayProperties.Computed(child).Inline ? node : FirstContext(child, depth + 1);
                return node;
            }
        }
        else children.AddRange(Children(node));
        foreach (var after in CssGeneratedContent.Afters(node))
            children.Add(CssNode.Get(after));
        foreach (var child in children)
        {
            if (Ignored(child)) continue;
            var display = CssDisplayProperties.Computed(child);
            if (display.Mode == CssDisplayMode.Contents)
            {
                var found = FirstContext(child, depth + 1);
                if (found is not null) return ReferenceEquals(found, child) ? node : found;
                continue;
            }
            if (display.Inline)
            {
                if (child.Target is TextBlock leaf && leaf.Text.Length == 0 && !CssGeneratedContent.HasBoxes(leaf)) continue;
                if (child.Target is Run run && !InlineTextCreatesLine(child,run.Text) && !InlineEdgesCreateLine(child)) continue;
                if (child.Target is Span)
                {
                    var found = FirstContext(child, depth + 1);
                    if (found is null) continue;
                    if (found is not null && !ReferenceEquals(found, child)) return found;
                }
                return node;
            }
            return FirstContext(child, depth + 1) ?? child;
        }
        return BlockContainer(node) || InlineEdgesCreateLine(node) ? node : null;
    }

    private static bool InlineTextCreatesLine(CssNode node, string value)
    {
        if (value.Length == 0) return false;
        var mode = (CssWhiteSpaceCollapse)node.GetValue(CssFlowProperties.WhiteSpaceProperty)!;
        if (mode == CssWhiteSpaceCollapse.Discard) return false;
        return value.Any(character => character is not (' ' or '\t' or '\r' or '\n' or '\f')) ||
            mode is CssWhiteSpaceCollapse.Preserve or CssWhiteSpaceCollapse.PreserveSpaces or CssWhiteSpaceCollapse.BreakSpaces ||
            mode == CssWhiteSpaceCollapse.PreserveBreaks && value.Any(character => character is '\r' or '\n');
    }

    private static bool InlineEdgesCreateLine(CssNode node)
    {
        var border = (Thickness)node.GetValue(CssBorderUsedThicknessProperties.ValueProperty)!;
        if (border.Left != 0 || border.Top != 0 || border.Right != 0 || border.Bottom != 0) return true;
        if (node.CssLayout is not { } state) return false;
        return state.HasPadding && new[] {state.PaddingLeft,state.PaddingTop,state.PaddingRight,state.PaddingBottom}.Any(value => value.Resolve(1,0) != 0) ||
            state.HasMargin && new[] {state.MarginLeft,state.MarginTop,state.MarginRight,state.MarginBottom}.Any(value => value.Resolve(1,0) != 0);
    }

    private static bool Ignored(CssNode node) => CssDisplayProperties.SuppressesBoxTree(node.Target) ||
        node.Target is UIElement { Visibility: Visibility.Collapsed } || node.CssLayout?.IsOutOfFlow == true ||
        Floating(node) || CssGeneratedContent.Kind(node).IsMarker();
    private static bool Floating(CssNode node) => node.GetValue(CssFloatProperties.FloatProperty) is CssFloatSide side && side != CssFloatSide.None;

    private static IEnumerable<CssNode> Children(CssNode node)
    {
        System.Collections.IEnumerable? children = node.Target switch
        {
            Panel panel => panel.Children,
            Border border => border.Child is { } child ? new[] { child } : [],
            FlowDocument document => document.Blocks,
            Section section => section.Blocks,
            Paragraph paragraph => paragraph.Inlines,
            Span span => span.Inlines,
            ListItem item => item.Blocks,
            Jalium.UI.Documents.List list => list.ListItems,
            _ => null,
        };
        if (children is not null)
        {
            foreach (var value in children) if (value is DependencyObject child) yield return CssNode.Get(child);
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
                if (logical.Current is FrameworkElement or FrameworkContentElement) yield return CssNode.Get((DependencyObject)logical.Current);
    }

    internal static bool Applies(string name) => name.StartsWith("--", StringComparison.Ordinal) ||
        name.StartsWith("font-", StringComparison.Ordinal) || name.StartsWith("background-", StringComparison.Ordinal) ||
        name.StartsWith("text-decoration-", StringComparison.Ordinal) || name.StartsWith("text-emphasis-", StringComparison.Ordinal) || name.StartsWith("hyphenate-", StringComparison.Ordinal) ||
        name is "color" or "opacity" or "line-height" or "vertical-align" or "text-transform" or "text-autospace" or "text-shadow" or "text-justify" or
            "text-combine-upright" or "text-underline-offset" or "text-underline-position" or "white-space-collapse" or
            "white-space-trim" or "word-spacing" or "word-space-transform" or "letter-spacing" or "word-break" or "line-break" or
            "overflow-wrap" or "tab-size" or "hyphens" or "hanging-punctuation";

    internal sealed class Context(CssNode root)
    {
        internal CssNode Root => root;
        internal bool HasCurrentLine => _origins.Length > 0 && _origins.All(origin =>
            s_targets.TryGetValue(origin.Target,out var target) && target.Potential && SameContext(FirstContext(origin,0),root));
        private CssNode[] _origins = [];
        private int _revision = -1, _generation;
        private readonly Dictionary<(CssNode Original, DependencyObject Parent, bool Alias), Run> _styles = [];
        private readonly Dictionary<Run, FrameworkElement> _metrics = [];
        private readonly Dictionary<double,CssFloatEnvironment> _environments = [];
        private readonly List<WeakReference<TextBlock>> _consumers = [];
        private int _refreshing;

        internal void Use(CssNode[] origins, TextBlock? consumer)
        {
            var changed = !_origins.SequenceEqual(origins);
            if (_revision != Revision || changed)
            {
                _revision = Revision; _origins = origins; _generation++;
                if (changed) { _styles.Clear(); _metrics.Clear(); _environments.Clear(); }
                else PruneStyles();
            }
            _consumers.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (consumer is not null && !_consumers.Any(reference => reference.TryGetTarget(out var current) && ReferenceEquals(current, consumer)))
                _consumers.Add(new(consumer));
        }

        internal void Release(TextBlock consumer)
        {
            _consumers.RemoveAll(reference => !reference.TryGetTarget(out var current) || ReferenceEquals(current, consumer));
            if (_consumers.Count == 0) { _styles.Clear(); _metrics.Clear(); _environments.Clear(); }
            else PruneStyles();
        }

        internal bool NoteEnvironment(double width,CssFloatEnvironment environment)
        {
            var changed = _environments.TryGetValue(width,out var previous) && !previous.Equals(environment);
            if (_environments.Count >= 24 && !_environments.ContainsKey(width)) _environments.Remove(_environments.Keys.First());
            _environments[width] = environment;
            return changed;
        }

        private void PruneStyles()
        {
            // A long-lived paragraph must not retain runs removed by an editor,
            // or aliases made before a run moved to a different inline parent.
            foreach (var key in _styles.Keys.Where(key => key.Alias && !CurrentParent(key)).ToArray())
            {
                _metrics.Remove(_styles[key]);
                _styles.Remove(key);
            }
        }

        private bool CurrentParent((CssNode Original, DependencyObject Parent, bool Alias) key)
        {
            if (!Contains(key.Original.Target) || !s_projections.TryGetValue(key.Parent, out var parent)) return false;
            var actual = key.Original.FrameworkParent;
            return ReferenceEquals(actual, parent.Alias ? parent.Original : root);
        }

        internal Run RootStyle
        {
            get
            {
                DependencyObject parent = root.Target;
                foreach (var origin in _origins) parent = Style(origin, parent, false);
                return (Run)parent;
            }
        }

        internal Run[] RootStyles()
        {
            _ = RootStyle;
            DependencyObject parent = root.Target;
            return _origins.Select(origin => (Run)(parent = Style(origin, parent, false))).ToArray();
        }

        internal Run StyleFor(DependencyObject source)
        {
            if (ReferenceEquals(source, root.Target)) return RootStyle;
            var node = CssNode.Get(source);
            var parent = node.FrameworkParent;
            return Style(node, parent is null || ReferenceEquals(parent, root) ? RootStyle : StyleFor(parent.Target), true);
        }

        internal bool Contains(DependencyObject source)
        {
            var depth = 0;
            for (var node = CssNode.Get(source); node is not null && depth++ < 4096; node = node.FrameworkParent)
                if (ReferenceEquals(node, root)) return true;
            return false;
        }

        internal FrameworkElement MetricsFor(DependencyObject source)
        {
            var style = StyleFor(source);
            if (!_metrics.TryGetValue(style, out var metric)) _metrics.Add(style, metric = new MetricElement(style));
            foreach (var property in FontProperties())
                if (!property.ReadOnly) metric.SetLayerValue(property, style.GetValue(property), DependencyObject.LayerValueSource.StyleSetter);
            metric.SetValue(CssComputedLineHeightValue.Property, CssComputedLineHeightValue.Inherited(CssNode.Get(style)));
            return metric;
        }

        private Run Style(CssNode original, DependencyObject parent, bool alias)
        {
            var key = (original, parent, alias);
            if (!_styles.TryGetValue(key, out var run))
            {
                run = new Run();
                var kind = alias ? CssGeneratedContent.Kind(original) : CssPseudoElement.FirstLine;
                var projection = new Projection(original, parent, kind, alias, this);
                s_projections.Add(run, projection);
                run.SetContentParent(parent);
                run.TextContentChanged += (_, _) =>
                {
                    if (projection.Refreshing || _refreshing > 0) return;
                    _generation++;
                    foreach (var reference in _consumers.ToArray())
                        if (reference.TryGetTarget(out var consumer)) consumer.InvalidateFirstLineStyle();
                };
                _styles.Add(key, run);
            }
            var data = s_projections.GetValue(run, _ => throw new InvalidOperationException("Missing first-line style."));
            if (data.Generation == _generation) return run;
            data.Refreshing = true; _refreshing++;
            try
            {
                if (alias) CopyNative(original.Target, run, data);
                else run.SetLayerValue(FrameworkElement.LanguageProperty, parent.GetValue(FrameworkElement.LanguageProperty), DependencyObject.LayerValueSource.StyleSetter);
                run.Text = original.Target is Run text ? text.Text : original.Target is TextBlock block ? block.Text : "";
                CssEngine.EvaluateElement(run);
                data.Generation = _generation;
            }
            finally { _refreshing--; data.Refreshing = false; }
            return run;
        }

        private static void CopyNative(DependencyObject source, Run run, Projection data)
        {
            var next = new HashSet<DependencyProperty>();
            foreach (var property in FontProperties().Append(FrameworkElement.FlowDirectionProperty).Append(TextElement.BackgroundProperty))
            {
                if (property.ReadOnly) continue;
                if (source.HasLocalOrAnimatedValue(property) || source.HasCssAnimatedValue(property))
                { run.SetValue(property, source.GetValue(property)); next.Add(property); }
                else if (source.HasValueAboveInherited(property) && CssNode.Get(source).CssRuntimeState?.Applied?.ContainsKey(property) != true)
                { run.SetLayerValue(property, source.GetValue(property), DependencyObject.LayerValueSource.StyleSetter); next.Add(property); }
            }
            foreach (var property in data.Native.Where(property => !next.Contains(property)))
            { run.ClearValue(property); run.ClearLayerValue(property, DependencyObject.LayerValueSource.StyleSetter); }
            data.Native.Clear(); data.Native.UnionWith(next);
            run.SetLayerValue(FrameworkElement.FlowDirectionProperty, source.GetValue(FrameworkElement.FlowDirectionProperty), DependencyObject.LayerValueSource.StyleSetter);
            run.SetLayerValue(FrameworkElement.LanguageProperty, source.GetValue(FrameworkElement.LanguageProperty), DependencyObject.LayerValueSource.StyleSetter);
        }
    }

    private static IEnumerable<DependencyProperty> FontProperties() => CssPropertyRegistry.GetInheritedStorageProperties()
        .Concat(new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty,
            TextElement.FontStyleProperty, TextElement.FontStretchProperty, TextElement.ForegroundProperty }).Distinct();
    private sealed class MetricElement(Run source) : FrameworkElement, ICssStyledBox
    { public DependencyObject StyleSource => source; }
}
