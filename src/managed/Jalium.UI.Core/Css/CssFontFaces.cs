using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Text;
using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

internal static class CssFontFaces
{
    internal const string BlockPrefix = "\u0001css-font:";
    internal static bool IsActive;
    private static readonly ConditionalWeakTable<CssNode, Document> s_documents = new();
    private static readonly List<WeakReference<Document>> s_known = [];
    private static readonly object s_gate = new();
    private static readonly ConditionalWeakTable<Dispatcher, ConcurrentQueue<Action>> s_notifications = new();
    private static readonly ConditionalWeakTable<CssNode, Dictionary<RequestKey, FontRequest>> s_requests = new();
    private static readonly ConcurrentDictionary<long, WeakReference<FontRequest>> s_requestIds = new();
    private static long s_nextRequest;
    private readonly record struct RequestKey(string Families, int Weight, int Style, double Width);
    private sealed record FontRequest(long Id, CssNode Node, string[] Families, int Weight, int Style, double Width);

    internal static string MaterializeSource(string source, string? text)
    {
        if (!CssFontRenderingPlan.TryDecode(source, out var plan) || plan.RequestId == 0 ||
            !s_requestIds.TryGetValue(plan.RequestId, out var weak) || !weak.TryGetTarget(out var request) ||
            !request.Node.Dispatcher.CheckAccess()) return source;
        return For(request.Node).Resolve(request.Node, request.Families, request.Weight, request.Style, text, request.Width);
    }

    private static long RequestId(CssNode node, IReadOnlyList<string> families, int weight, int style, double width)
    {
        var requests = s_requests.GetValue(node, static _ => new());
        var key = new RequestKey(JoinFamilies(families), weight, style, width);
        if (requests.TryGetValue(key, out var request)) return request.Id;
        long id = Interlocked.Increment(ref s_nextRequest);
        request = new(id, node, families.ToArray(), weight, style, width); requests[key] = request;
        s_requestIds[id] = new(request);
        if ((id & 127) == 0)
            foreach (var entry in s_requestIds) if (!entry.Value.TryGetTarget(out _)) s_requestIds.TryRemove(entry.Key, out _);
        return id;
    }

    private static string JoinFamilies(IEnumerable<string> families) => string.Join(", ", families.Select(name =>
        name.IndexOfAny([',', '\'', '"', '\\']) >= 0 ? "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : name));

    internal static void FlushNotifications(Dispatcher dispatcher)
    {
        if (s_notifications.TryGetValue(dispatcher, out var queue))
            while (queue.TryDequeue(out var action)) action();
    }

    private static void Schedule(Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess()) { action(); return; }
        s_notifications.GetValue(dispatcher, static _ => new()).Enqueue(action);
        dispatcher.BeginInvoke(DispatcherPriority.Render, () => FlushNotifications(dispatcher));
    }
    private static readonly Lazy<HashSet<string>> s_localFamilies = new(() => new(
        Jalium.UI.Controls.Helpers.FontEnumerationHelper.EnumerateSystemFontFamilies() ?? [], StringComparer.OrdinalIgnoreCase));

    internal static bool IsBlocked(string family) => family.StartsWith(BlockPrefix, StringComparison.Ordinal);
    internal static string Unblock(string family) => IsBlocked(family) ? family[BlockPrefix.Length..] : family;

    private static Document For(CssNode node)
    {
        var root = CssMatcher.Root(node);
        var document = s_documents.GetValue(root, key =>
        {
            var created = new Document(key);
            lock (s_gate)
            {
                s_known.RemoveAll(entry => !entry.TryGetTarget(out _));
                s_known.Add(new(created));
            }
            return created;
        });
        document.Refresh();
        return document;
    }

    internal static void Changed()
    {
        if (!IsActive) return;
        Document[] documents;
        lock (s_gate) documents = s_known.Select(w => w.TryGetTarget(out var d) ? d : null).OfType<Document>().ToArray();
        foreach (var document in documents) document.Invalidate();
    }

    internal static void Invalidate(CssNode node)
    {
        if (IsActive && s_documents.TryGetValue(CssMatcher.Root(node), out var document)) document.Invalidate();
    }

    internal static string RenderingFamily(DependencyObject owner, FontFamily family)
    {
        if (!IsActive || !family.IsCssFamily || owner is not (FrameworkElement or FrameworkContentElement)) return family.Source;
        var node = CssNode.Get(owner);
        var familyProperty = CssDependencyPropertyLookup.Find(node.GetType(), "FontFamily");
        if (familyProperty is not null && node.HasLocalOrAnimatedValue(familyProperty)) return family.Source;
        var weightProperty = CssDependencyPropertyLookup.Find(node.GetType(), "FontWeight");
        var styleProperty = CssDependencyPropertyLookup.Find(node.GetType(), "FontStyle");
        var textProperty = CssDependencyPropertyLookup.Find(node.GetType(), "Text");
        var weight = weightProperty is not null && node.GetValue(weightProperty) is FontWeight w ? w.ToOpenTypeWeight() : 400;
        var style = styleProperty is not null && node.GetValue(styleProperty) is FontStyle s ? s.ToOpenTypeStyle() : 0;
        var text = textProperty is null ? null : node.GetValue(textProperty) as string;
        return family.CssComputedFamily is { } computed
            ? Resolve(node, computed.RenderingNames, weight, style, text)
            : Resolve(node, family.Source, weight, style, text);
    }

    internal static string Resolve(CssNode node, string family, int weight, int style, string? text = null, double? width = null)
        => IsActive ? For(node).Resolve(node, TextMeasurement.EnumerateFontFamilyNames(family).ToArray(), weight, style, text, width) : family;

    internal static string Resolve(CssNode node, IReadOnlyList<string> families, int weight, int style, string? text = null, double? width = null)
        => IsActive ? For(node).Resolve(node, families, weight, style, text, width) : string.Join(", ", families);

    internal static Task Ready(DependencyObject root, CancellationToken token) =>
        For(CssNode.Get(root)).Ready(token);

    internal static IReadOnlyList<string> Errors(DependencyObject root) => For(CssNode.Get(root)).Errors();

    private sealed class Document(CssNode root)
    {
        private readonly WeakReference<CssNode> _root = new(root);
        private readonly Dictionary<CssFontFaceRule, Entry> _entries = new(ReferenceEqualityComparer.Instance);
        private CssFontFaceRule[] _rules = [];
        private int _version = -1;
        private Size _viewport;
        private bool _refreshing;

        internal void Invalidate()
        {
            _version = -1;
            if (!_root.TryGetTarget(out var node) || node.Dispatcher.HasShutdownStarted) return;
            if (node.Dispatcher.CheckAccess()) Refresh();
            else node.Dispatcher.BeginInvoke(DispatcherPriority.Render, Refresh);
        }

        internal void Refresh()
        {
            if (_refreshing || !_root.TryGetTarget(out var root)) return;
            var viewport = CssEngine.ViewportSize(root);
            if (_version == CssEngine.CascadeVersion && _viewport == viewport) return;
            _refreshing = true;
            try
            {
                var rules = new List<(CssFontFaceRule Rule, int[] Layer, int Order)>();
                var layers = new CssLayerOrder();
                void Add(IEnumerable<CssStyleSheet> sheets)
                {
                    foreach (var sheet in sheets)
                    {
                        foreach (var layer in sheet.Layers) if (layer.Condition is null || layer.Condition.Evaluate(root)) layers.GetKey(layer.Name);
                        foreach (var rule in sheet.FontFaces)
                            if (rule.Condition is null || rule.Condition.Evaluate(root)) rules.Add((rule, layers.GetKey(rule.LayerName), rules.Count));
                    }
                }
                if (CssEngine.ApplicationStyleSheetsProvider?.Invoke() is { } application) Add(application);
                var pending = new Stack<CssNode>(); pending.Push(root); var seen = new HashSet<CssNode>();
                while (pending.TryPop(out var node))
                {
                    if (!seen.Add(node)) continue;
                    if (node.CssRuntimeState?.ScopedStyleSheets is { } sheets) Add(sheets);
                    foreach (var child in node.EnumerateChildren().Reverse()) pending.Push(child);
                }
                rules.Sort((a, b) => { var layer = CssLayerOrder.Compare(a.Layer, b.Layer); return layer != 0 ? -layer : b.Order.CompareTo(a.Order); });
                _rules = rules.Select(r => r.Rule).ToArray();
                var retained = _rules.ToHashSet(ReferenceEqualityComparer.Instance);
                foreach (var rule in _entries.Keys.Where(rule => !retained.Contains(rule)).ToArray())
                { _entries[rule].Dispose(); _entries.Remove(rule); }
                _version = CssEngine.CascadeVersion; _viewport = viewport;
            }
            finally { _refreshing = false; }
        }

        internal string Resolve(CssNode node, string family, int weight, int style, string? text)
            => Resolve(node, TextMeasurement.EnumerateFontFamilyNames(family).ToArray(), weight, style, text);

        internal string Resolve(CssNode node, IReadOnlyList<string> families, int weight, int style, string? text, double? width = null)
        {
            if (OperatingSystem.IsMacOS() && TextMeasurement.TryGetFontCharacterCoverage(
                FrameworkElement.DefaultFontFamilyName, 400, 0, [32], out _))
                return ResolveMac(node, families, weight, style, text, width ?? CssFontStretchValue.Computed(node));
            return ResolveLegacy(node, families, weight, style, text);
        }

        private sealed record Candidate(CssFontFaceRule? Rule, Entry? Entry, string? Local, int Weight, double Width)
        {
            internal string? Family => Local ?? Entry?.RenderFamily;
            internal CssUnicodeRange[] Ranges => Rule?.Ranges ?? [new(0, 0x10ffff)];
            internal bool Contains(int scalar) => Rule?.Contains(scalar) ?? true;
        }

        private string ResolveMac(CssNode node, IReadOnlyList<string> families, int weight, int style, string? text, double width)
        {
            var candidates = new List<Candidate>(); bool hasRules = false;
            foreach (var name in families)
            {
                var matches = _rules.Where(rule => rule.Family.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0)
                {
                    if (NativeMethods.FontFamilyIsAvailable(name) != 0) candidates.Add(new(null, null, name, weight, width));
                    continue;
                }
                hasRules = true;
                // Select face descriptors before matching its composite unicode subsets.
                var best = matches.OrderBy(rule => WidthDistance(rule, width)).ThenBy(rule => StyleDistance(rule.Style, style))
                    .ThenBy(rule => WeightDistance(rule, weight)).First();
                foreach (var rule in matches.Where(rule => rule.MinimumWidth == best.MinimumWidth && rule.MaximumWidth == best.MaximumWidth &&
                    rule.Style == best.Style && rule.MinimumWeight == best.MinimumWeight && rule.MaximumWeight == best.MaximumWeight))
                {
                    if (!_entries.TryGetValue(rule, out var entry)) _entries[rule] = entry = new(rule);
                    entry.Observe(node);
                    candidates.Add(new(rule, entry, null, (int)Math.Clamp(weight, rule.MinimumWeight, rule.MaximumWeight),
                        Math.Clamp(width, rule.MinimumWidth, rule.MaximumWidth)));
                }
            }
            if (!hasRules) return FontWidthRenderingSource.Wrap(JoinFamilies(families), width);
            // Font timers start on the first attempt to use a face. Already loaded
            // fallbacks can paint while it waits, without starting more downloads.
            var clusters = new List<(int[] Original, int[] Normalized)>();
            var enumerator = StringInfo.GetTextElementEnumerator(text ?? " ");
            while (enumerator.MoveNext())
            {
                string cluster = enumerator.GetTextElement();
                int[] Required(string value) => value.EnumerateRunes().Where(rune => !IsIgnorable(rune.Value)).Select(rune => rune.Value).ToArray();
                var original = Required(cluster);
                string normalized;
                try { normalized = cluster.Normalize(NormalizationForm.FormC); }
                catch (ArgumentException) { normalized = cluster; } // Unpaired UTF-16 still uses the replacement rune.
                if (original.Length > 0) clusters.Add((original, Required(normalized)));
            }
            var scalars = clusters.SelectMany(cluster => cluster.Original.Concat(cluster.Normalized)).Distinct().Select(value => (uint)value).ToArray();
            var coverage = new Dictionary<Candidate, HashSet<int>>();
            foreach (var candidate in candidates)
                if (candidate.Family is { } family && TextMeasurement.TryGetFontCharacterCoverage(
                    FontWidthRenderingSource.Wrap(family, candidate.Width), candidate.Weight, style, scalars, out var supported))
                    coverage[candidate] = scalars.Where((_, index) => supported[index] != 0).Select(value => (int)value).ToHashSet();
            bool Demand(int[] original, int[] normalized)
            {
                foreach (var candidate in candidates)
                {
                    bool Declared(int[] values) => values.Length > 0 && values.All(candidate.Contains);
                    bool Covers(int[] values) => Declared(values) && coverage.TryGetValue(candidate, out var available) && values.All(available.Contains);
                    if (candidate.Family is not null)
                    {
                        if (Covers(original) || Covers(normalized)) return true;
                    }
                    else if (candidate.Entry is { Finished: false } entry && (Declared(original) || Declared(normalized)))
                    { entry.Start(); return true; }
                }
                return false;
            }
            foreach (var cluster in clusters)
                if (!Demand(cluster.Original, cluster.Normalized))
                    foreach (int scalar in cluster.Original) Demand([scalar], [scalar]);
            var faces = candidates.Where(candidate => candidate.Family is not null || candidate.Entry is { Started: true, Finished: false })
                .Select(candidate => new CssRenderingFace(candidate.Family, candidate.Weight, candidate.Ranges,
                    candidate.Family is null, candidate.Entry?.Blocked ?? false, candidate.Width)).ToArray();
            if (faces.Length > 4096 || faces.Sum(face => (long)face.Ranges.Length) > 65536)
                throw new InvalidOperationException("CSS font cascade exceeds the supported size");
            int metrics = Array.FindIndex(faces, face => !face.Waiting && face.Contains(32));
            bool unresolved = candidates.Any(candidate => candidate.Entry is { Finished: false });
            if (!unresolved && faces.All(face => face.Unrestricted && face.Weight == weight && face.Width == width) && metrics == 0)
                return FontWidthRenderingSource.Wrap(JoinFamilies(faces.Select(face => face.Family!)), width);
            var result = FontWidthRenderingSource.Wrap(new CssFontRenderingPlan(RequestId(node, families, weight, style, width), metrics, faces).Encode(), width);
            // Preserve the existing whole-text block contract when its complete
            // range is waiting first. Partial masks are handled by the native runs.
            return faces.FirstOrDefault() is { Waiting: true, Blocked: true, Unrestricted: true } ? BlockPrefix + result : result;
        }

        private static bool IsIgnorable(int scalar) => scalar is 0x9 or 0xa or 0xd or 0x200b or 0x200c or 0x200d or 0x2060 or 0xfeff ||
            scalar is >= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef || Rune.GetUnicodeCategory(new Rune(scalar)) == UnicodeCategory.Format;

        private static (int Order, double Distance) WidthDistance(CssFontFaceRule rule, double requested)
        {
            if (rule.MinimumWidth <= requested && rule.MaximumWidth >= requested) return (0, 0);
            bool below = rule.MaximumWidth < requested;
            return (below == (requested <= 100) ? 1 : 2,
                below ? requested - rule.MaximumWidth : rule.MinimumWidth - requested);
        }

        private static int StyleDistance(int candidate, int requested) => candidate == requested ? 0 :
            requested == 0 ? candidate == 2 ? 1 : 2 : candidate == 0 ? 2 : 1;

        private string ResolveLegacy(CssNode node, IReadOnlyList<string> families, int weight, int style, string? text)
        {
            var resolved = new List<string>(); var blocked = false;
            foreach (var raw in families)
            {
                var name = raw.Trim().Trim('\'', '"');
                var candidates = _rules.Where(rule => rule.Family.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(text) || text.EnumerateRunes().Any(rune => rule.Contains(rune.Value)))).ToArray();
                if (candidates.Length == 0)
                {
                    resolved.Add(raw.Trim());
                    if (s_localFamilies.Value.Contains(name)) break;
                    continue;
                }
                var rule = candidates.OrderBy(r => r.Style == style ? 0 : r.Style != 0 && style != 0 ? 1 : 2)
                    .ThenBy(r => WeightDistance(r, weight)).First();
                if (!_entries.TryGetValue(rule, out var entry)) _entries[rule] = entry = new(rule);
                entry.Observe(node); entry.Start();
                if (entry.RenderFamily is { } loaded) { resolved.Add(loaded); break; }
                blocked |= entry.Blocked;
            }
            var result = string.Join(", ", resolved.Where(s => s.Length > 0));
            if (result.Length == 0) result = FrameworkElement.DefaultFontFamilyName;
            return blocked ? BlockPrefix + result : result;
        }

        private static double WeightDistance(CssFontFaceRule rule, int weight)
        {
            if (weight >= rule.MinimumWeight && weight <= rule.MaximumWeight) return 0;
            if (weight is >= 400 and <= 500)
            {
                if (rule.MinimumWeight >= weight && rule.MinimumWeight <= 500) return rule.MinimumWeight - weight;
                return rule.MaximumWeight < weight ? 1000 + weight - rule.MaximumWeight : 2000 + rule.MinimumWeight - weight;
            }
            if (weight < 400) return rule.MaximumWeight < weight ? weight - rule.MaximumWeight : 1000 + rule.MinimumWeight - weight;
            return rule.MinimumWeight > weight ? rule.MinimumWeight - weight : 1000 + weight - rule.MaximumWeight;
        }

        internal Task Ready(CancellationToken token) => Task.WhenAll(_entries.Values.Select(e => e.Task)).WaitAsync(token);
        internal IReadOnlyList<string> Errors() => _entries.Values.SelectMany(e => e.Errors).ToArray();
    }

    private sealed class Entry(CssFontFaceRule rule) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly List<WeakReference<CssNode>> _users = [];
        private readonly object _gate = new();
        private readonly List<string> _errors = [];
        private Task? _task;
        private CssNativeFontResource? _font;
        private string? _family;
        private long _start;
        private bool _finished, _disposed;
        internal Task Task => _task ?? System.Threading.Tasks.Task.CompletedTask;
        private int BlockPeriod => rule.Display is "swap" ? 0 : rule.Display is "optional" or "fallback" ? 100 : 3000;
        private long SwapDeadline => rule.Display == "optional" ? 100 : rule.Display == "fallback" ? 3100 : long.MaxValue;
        internal bool Started { get { lock (_gate) return _task is not null; } }
        internal bool Finished { get { lock (_gate) return _finished || _disposed; } }
        internal bool Blocked { get { lock (_gate) return _task is not null && !_finished && !_disposed && Environment.TickCount64 - _start < BlockPeriod; } }
        internal string? RenderFamily { get { lock (_gate) return _family; } }
        internal string[] Errors { get { lock (_gate) return _errors.ToArray(); } }

        internal void Observe(CssNode node)
        {
            lock (_gate)
            {
                _users.RemoveAll(user => !user.TryGetTarget(out _));
                if (!_users.Any(user => user.TryGetTarget(out var existing) && ReferenceEquals(existing, node))) _users.Add(new(node));
            }
        }

        internal void Start()
        {
            lock (_gate)
            {
                if (_task is not null || _disposed) return;
                _start = Environment.TickCount64;
                _task = System.Threading.Tasks.Task.Run(LoadAsync);
                if (BlockPeriod > 0) _ = EndBlockAsync();
            }
        }

        private async Task EndBlockAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(BlockPeriod, _cancellation.Token).ConfigureAwait(false);
                lock (_gate) if (_finished || _disposed) return;
                Notify();
            }
            catch (OperationCanceledException) { }
        }

        private async Task LoadAsync()
        {
            try
            {
                foreach (var source in rule.Sources)
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                    if (!CssFontCapabilities.SupportsSource(source)) continue;
                    try
                    {
                        string? family; CssNativeFontResource? resource = null;
                        if (source.Local)
                        {
                            if (!s_localFamilies.Value.Contains(source.Reference)) continue;
                            family = source.Reference;
                        }
                        else
                        {
                            // An invalid collection fragment cannot denote a PostScript
                            // name, so move to the next source without fetching it.
                            if (!CssFontData.TryParseFontFragment(source.Reference, out var reference, out var postScriptName)) continue;
                            var uri = rule.BaseUri is { } baseUri ? CssStyleSheet.ResolveReference(baseUri, reference) : new Uri(reference, UriKind.RelativeOrAbsolute);
                            var fetched = await (rule.Resolver ?? CssStyleSheet.DefaultResolver).ResolveAsync(uri, _cancellation.Token).ConfigureAwait(false);
                            using (fetched.Content)
                            using (var buffer = new MemoryStream())
                            {
                                var block = new byte[16384]; int count;
                                while ((count = await fetched.Content.ReadAsync(block, _cancellation.Token).ConfigureAwait(false)) > 0)
                                {
                                    if (buffer.Length + count > CssFontData.MaximumBytes) throw new InvalidDataException("font resource exceeds the supported size");
                                    buffer.Write(block, 0, count);
                                }
                                _cancellation.Token.ThrowIfCancellationRequested();
                                resource = CssNativeFontResource.Create(buffer.ToArray(), postScriptName);
                            }
                            if (resource is null) throw new InvalidDataException("font data could not be registered");
                            family = resource.Family;
                        }
                        lock (_gate)
                        {
                            if (_disposed || Environment.TickCount64 - _start > SwapDeadline) resource?.Dispose();
                            else { _font = resource; _family = family; }
                            _finished = true;
                        }
                        return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) when (error is IOException or InvalidDataException or System.Net.Http.HttpRequestException or UriFormatException or EntryPointNotFoundException or DllNotFoundException)
                    { lock (_gate) _errors.Add($"@font-face '{rule.Family}', source '{source.Reference}': {error.Message}"); }
                }
            }
            catch (OperationCanceledException) { }
            finally { lock (_gate) _finished = true; Notify(); }
        }

        private void Notify()
        {
            CssNode[] users;
            lock (_gate) users = _users.Select(w => w.TryGetTarget(out var node) ? node : null).OfType<CssNode>().ToArray();
            foreach (var group in users.GroupBy(node => node.Dispatcher))
            {
                var nodes = group.ToArray();
                void Update()
                {
                    TextMeasurement.ClearCache();
                    foreach (var node in nodes)
                    {
                        for (var current = node; current is not null; current = current.FrameworkParent)
                            if (current.Target is FrameworkElement visual) { visual.OnFontResourcesChanged(); break; }
                        CssEvaluationScheduler.InvalidateSubtree(node);
                    }
                }
                if (group.Key.HasShutdownStarted) continue;
                Schedule(group.Key, Update);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true; _family = null; _font?.Dispose(); _font = null;
                _cancellation.Cancel();
            }
            Notify();
        }
    }
}

public static partial class Css
{
    /// <summary>Waits for font resources already requested by layout or drawing.</summary>
    public static Task WaitForFontsAsync(DependencyObject root, CancellationToken cancellationToken = default)
        => CssFontFaces.Ready(root, cancellationToken);

    /// <summary>Returns load diagnostics for fonts requested in this native document.</summary>
    public static IReadOnlyList<string> GetFontLoadErrors(DependencyObject root) => CssFontFaces.Errors(root);
}
