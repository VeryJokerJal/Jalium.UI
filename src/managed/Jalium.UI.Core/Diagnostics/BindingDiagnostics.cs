using System.Collections.Concurrent;
using Jalium.UI.Data;

namespace Jalium.UI.Diagnostics;

/// <summary>
/// Captures binding updates and errors. Bindings always publish through these
/// hooks; when <see cref="IsRecording"/> is false the notifications short-circuit.
/// </summary>
public static class BindingDiagnostics
{
    public enum BindingEventKind
    {
        Activated,
        UpdateTarget,
        UpdateSource,
        StatusChanged,
        Error,
    }

    public sealed class BindingEventEntry
    {
        public DateTime Timestamp { get; }
        public BindingEventKind Kind { get; }
        public string TargetTypeName { get; }
        public string TargetPropertyName { get; }
        public string SourceDescription { get; }
        public BindingStatus Status { get; }
        public string? Message { get; }
        public WeakReference<BindingExpressionBase>? ExpressionRef { get; }

        internal BindingEventEntry(
            BindingExpressionBase expression,
            BindingEventKind kind,
            string? message)
        {
            Timestamp = DateTime.Now;
            Kind = kind;
            TargetTypeName = expression.Target.GetType().Name;
            TargetPropertyName = expression.TargetProperty.Name;
            Status = expression.Status;
            Message = message;
            SourceDescription = DescribeSource(expression);
            ExpressionRef = new WeakReference<BindingExpressionBase>(expression);
        }

        private static string DescribeSource(BindingExpressionBase expression)
        {
            if (expression is BindingExpression be)
            {
                string path = be.ParentBinding?.Path?.Path ?? "";
                string sourceType = be.ResolvedSource?.GetType().Name ?? "<unresolved>";
                return string.IsNullOrEmpty(path) ? sourceType : $"{sourceType}.{path}";
            }
            return expression.GetType().Name;
        }
    }

    private const int MaxEntries = 512;
    private static int s_recording;
    private static readonly ConcurrentQueue<BindingEventEntry> s_entries = new();
    private static readonly ConcurrentDictionary<BindingKey, BindingCounters> s_counters = new();

    private readonly record struct BindingKey(WeakRefKey Target, DependencyProperty Property);

    private readonly struct WeakRefKey : IEquatable<WeakRefKey>
    {
        private readonly WeakReference<DependencyObject> _ref;
        private readonly int _hash;

        public WeakRefKey(DependencyObject target)
        {
            _ref = new WeakReference<DependencyObject>(target);
            _hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target);
        }

        public override int GetHashCode() => _hash;
        public override bool Equals(object? obj) => obj is WeakRefKey other && Equals(other);
        public bool Equals(WeakRefKey other)
        {
            if (!_ref.TryGetTarget(out var a) || !other._ref.TryGetTarget(out var b))
                return false;
            return ReferenceEquals(a, b);
        }
    }

    public sealed class BindingCounters
    {
        public int UpdateTargetCount;
        public int UpdateSourceCount;
        public int ErrorCount;
        public DateTime LastUpdate;
        public string? LastError;
    }

    public static bool IsRecording => Volatile.Read(ref s_recording) != 0;
    public static event EventHandler? StateChanged;
    public static event EventHandler<BindingFailedEventArgs>? BindingFailed;

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
        s_counters.Clear();
    }

    public static BindingCounters? GetCounters(DependencyObject target, DependencyProperty property)
    {
        return s_counters.TryGetValue(new BindingKey(new WeakRefKey(target), property), out var c) ? c : null;
    }

    private static BindingCounters GetOrCreateCounters(DependencyObject target, DependencyProperty property)
    {
        return s_counters.GetOrAdd(new BindingKey(new WeakRefKey(target), property), _ => new BindingCounters());
    }

    private static bool IsIgnored(BindingExpressionBase expression)
        => DiagnosticsScope.ShouldIgnore(expression.Target as Visual);

    internal static void NotifyActivated(BindingExpressionBase expression)
    {
        if (Volatile.Read(ref s_recording) == 0) return;
        if (IsIgnored(expression)) return;
        Push(new BindingEventEntry(expression, BindingEventKind.Activated, null));
    }

    internal static void NotifyUpdateTarget(BindingExpressionBase expression, string? message = null)
    {
        if (IsIgnored(expression)) return;
        var counters = GetOrCreateCounters(expression.Target, expression.TargetProperty);
        Interlocked.Increment(ref counters.UpdateTargetCount);
        counters.LastUpdate = DateTime.Now;
        if (Volatile.Read(ref s_recording) == 0) return;
        Push(new BindingEventEntry(expression, BindingEventKind.UpdateTarget, message));
    }

    internal static void NotifyUpdateSource(BindingExpressionBase expression, string? message = null)
    {
        if (IsIgnored(expression)) return;
        var counters = GetOrCreateCounters(expression.Target, expression.TargetProperty);
        Interlocked.Increment(ref counters.UpdateSourceCount);
        counters.LastUpdate = DateTime.Now;
        if (Volatile.Read(ref s_recording) == 0) return;
        Push(new BindingEventEntry(expression, BindingEventKind.UpdateSource, message));
    }

    internal static void NotifyStatus(BindingExpressionBase expression, string? message = null)
    {
        if (Volatile.Read(ref s_recording) == 0) return;
        if (IsIgnored(expression)) return;
        Push(new BindingEventEntry(expression, BindingEventKind.StatusChanged, message));
    }

    internal static void NotifyError(BindingExpressionBase expression, string message)
    {
        if (IsIgnored(expression)) return;
        var counters = GetOrCreateCounters(expression.Target, expression.TargetProperty);
        Interlocked.Increment(ref counters.ErrorCount);
        counters.LastError = message;
        counters.LastUpdate = DateTime.Now;
        Push(new BindingEventEntry(expression, BindingEventKind.Error, message));

        var eventArgs = new BindingFailedEventArgs(
            global::System.Diagnostics.TraceEventType.Error,
            0,
            message,
            expression,
            expression.Target,
            expression.TargetProperty);
        BindingFailed?.Invoke(null, eventArgs);

        var traceSource = global::System.Diagnostics.PresentationTraceSources.DataBindingSource;
        traceSource.TraceEvent(eventArgs.EventType, eventArgs.Code, eventArgs.Message);
        traceSource.Flush();
    }

    private static void Push(BindingEventEntry entry)
    {
        s_entries.Enqueue(entry);
        while (s_entries.Count > MaxEntries && s_entries.TryDequeue(out _)) { }
    }

    public static IReadOnlyList<BindingEventEntry> Snapshot() => s_entries.ToArray();
}
