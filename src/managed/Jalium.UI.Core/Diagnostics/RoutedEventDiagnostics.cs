using System.Collections.Concurrent;
using Jalium.UI.Media;

namespace Jalium.UI.Diagnostics;

/// <summary>
/// Records routed events as they propagate through the visual tree.
/// Each entry captures the full propagation path so DevTools can render the
/// event as a node graph (source → ancestors for Bubble, ancestors → source for Tunnel).
/// Zero cost when <see cref="IsRecording"/> is false.
/// </summary>
public static class RoutedEventDiagnostics
{
    public sealed class PathNode
    {
        public WeakReference<Visual> VisualRef { get; }
        public string TypeName { get; }
        public string ElementName { get; }

        internal PathNode(Visual visual)
        {
            VisualRef = new WeakReference<Visual>(visual);
            TypeName = visual.GetType().Name;
            ElementName = visual is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name)
                ? fe.Name
                : string.Empty;
        }
    }

    public sealed class RoutedEventEntry
    {
        public DateTime Timestamp { get; }
        public string EventName { get; }
        public RoutingStrategy Strategy { get; }
        public bool Handled { get; internal set; }

        public WeakReference<object>? SourceRef { get; }
        public string SourceTypeName { get; }
        public string SourceName { get; }
        public WeakReference<object>? OriginalSourceRef { get; }
        public string OriginalSourceTypeName { get; }

        /// <summary>
        /// Full propagation path, ordered from the visual closest to the OriginalSource
        /// up to the outer-most ancestor. For Bubble events this is the dispatch order;
        /// for Tunnel events the dispatch order is reversed. Direct events have a single node.
        /// </summary>
        public IReadOnlyList<PathNode> Path { get; }

        internal RoutedEventEntry(RoutedEventArgs args, IReadOnlyList<PathNode> path)
        {
            Timestamp = DateTime.Now;
            EventName = args.RoutedEvent?.Name ?? "?";
            Strategy = args.RoutedEvent?.RoutingStrategy ?? RoutingStrategy.Direct;
            Path = path;

            if (args.Source is { } src)
            {
                SourceRef = new WeakReference<object>(src);
                SourceTypeName = src.GetType().Name;
                SourceName = src is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name) ? fe.Name : "";
            }
            else
            {
                SourceTypeName = "";
                SourceName = "";
            }
            if (args.OriginalSource is { } orig)
            {
                OriginalSourceRef = new WeakReference<object>(orig);
                OriginalSourceTypeName = orig.GetType().Name;
            }
            else
            {
                OriginalSourceTypeName = "";
            }
            Handled = args.Handled;
        }
    }

    private const int MaxEntries = 512;
    private static int s_recording;
    private static readonly ConcurrentQueue<RoutedEventEntry> s_entries = new();
    private static readonly HashSet<string> s_filteredNames = new(StringComparer.Ordinal)
    {
        // These fire every mouse move and would flood the log; opt-in via SetFilter if needed.
        "PreviewMouseMove",
        "MouseMove",
    };
    private static readonly object s_filterLock = new();

    public static bool IsRecording => Volatile.Read(ref s_recording) != 0;
    public static event EventHandler? StateChanged;

    public static void StartRecording()
    {
        if (Interlocked.Exchange(ref s_recording, 1) == 0)
            StateChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void StopRecording()
    {
        if (Interlocked.Exchange(ref s_recording, 0) == 1)
            StateChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Reset()
    {
        while (s_entries.TryDequeue(out _)) { }
    }

    public static void SetFilter(IEnumerable<string> eventNames)
    {
        lock (s_filterLock)
        {
            s_filteredNames.Clear();
            foreach (var name in eventNames)
                s_filteredNames.Add(name);
        }
    }

    public static IReadOnlyCollection<string> GetFilter()
    {
        lock (s_filterLock)
        {
            return s_filteredNames.ToArray();
        }
    }

    internal static void NotifyRaised(RoutedEventArgs args)
    {
        if (Volatile.Read(ref s_recording) == 0) return;
        if (DiagnosticsScope.ShouldIgnoreEvent(args)) return;
        var name = args.RoutedEvent?.Name;
        if (name != null)
        {
            bool skip;
            lock (s_filterLock)
            {
                skip = s_filteredNames.Contains(name);
            }
            if (skip) return;
        }

        var path = CapturePath(args);
        s_entries.Enqueue(new RoutedEventEntry(args, path));
        while (s_entries.Count > MaxEntries && s_entries.TryDequeue(out _)) { }
    }

    // Hard limits on how much we store per event so that diagnostics never turn
    // into a runaway cost during dense input bursts.
    private const int MaxPathDepth = 64;

    private static IReadOnlyList<PathNode> CapturePath(RoutedEventArgs args)
    {
        var start = args.OriginalSource as Visual ?? args.Source as Visual;
        if (start == null) return Array.Empty<PathNode>();

        var strategy = args.RoutedEvent?.RoutingStrategy ?? RoutingStrategy.Direct;
        if (strategy == RoutingStrategy.Direct)
            return new PathNode[] { new PathNode(start) };

        // Bubble + Tunnel both walk the same chain — start → ancestor … → root.
        // Hard cap depth so a pathological tree cannot cause an infinite loop
        // or allocate unboundedly per event.
        var list = new List<PathNode>(16);
        int depth = 0;
        for (var cur = start; cur != null && depth < MaxPathDepth; cur = cur.VisualParent, depth++)
            list.Add(new PathNode(cur));
        return list;
    }

    public static IReadOnlyList<RoutedEventEntry> Snapshot() => s_entries.ToArray();
}
