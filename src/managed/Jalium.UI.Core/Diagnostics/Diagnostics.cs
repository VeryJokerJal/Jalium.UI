using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Data;

namespace System.Diagnostics
{
    /// <summary>
    /// Specifies how much presentation-framework trace information is produced for an object.
    /// </summary>
    public enum PresentationTraceLevel
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
    }

    /// <summary>
    /// Exposes the presentation trace sources and the per-object trace-level attached property.
    /// </summary>
    public static class PresentationTraceSources
    {
        private sealed class TraceLevelHolder
        {
            public int Level;
        }

        private static readonly ConditionalWeakTable<object, TraceLevelHolder> s_traceLevels = new();
        private static readonly Lazy<TraceSource> s_dependencyPropertySource = Create("System.Windows.DependencyProperty");
        private static readonly Lazy<TraceSource> s_freezableSource = Create("System.Windows.Freezable");
        private static readonly Lazy<TraceSource> s_nameScopeSource = Create("System.Windows.NameScope");
        private static readonly Lazy<TraceSource> s_routedEventSource = Create("System.Windows.RoutedEvent");
        private static readonly Lazy<TraceSource> s_animationSource = Create("System.Windows.Media.Animation");
        private static readonly Lazy<TraceSource> s_dataBindingSource = Create("System.Windows.Data");
        private static readonly Lazy<TraceSource> s_documentsSource = Create("System.Windows.Documents");
        private static readonly Lazy<TraceSource> s_resourceDictionarySource = Create("System.Windows.ResourceDictionary");
        private static readonly Lazy<TraceSource> s_markupSource = Create("System.Windows.Markup");
        private static readonly Lazy<TraceSource> s_hwndHostSource = Create("System.Windows.Interop.HwndHost");
        private static readonly Lazy<TraceSource> s_shellSource = Create("System.Windows.Shell");

        public static readonly global::Jalium.UI.DependencyProperty TraceLevelProperty =
            global::Jalium.UI.DependencyProperty.RegisterAttached(
                "TraceLevel",
                typeof(PresentationTraceLevel),
                typeof(PresentationTraceSources));

        public static TraceSource DependencyPropertySource => s_dependencyPropertySource.Value;
        public static TraceSource FreezableSource => s_freezableSource.Value;
        public static TraceSource NameScopeSource => s_nameScopeSource.Value;
        public static TraceSource RoutedEventSource => s_routedEventSource.Value;
        public static TraceSource AnimationSource => s_animationSource.Value;
        public static TraceSource DataBindingSource => s_dataBindingSource.Value;
        public static TraceSource DocumentsSource => s_documentsSource.Value;
        public static TraceSource ResourceDictionarySource => s_resourceDictionarySource.Value;
        public static TraceSource MarkupSource => s_markupSource.Value;
        public static TraceSource HwndHostSource => s_hwndHostSource.Value;
        public static TraceSource ShellSource => s_shellSource.Value;

        public static PresentationTraceLevel GetTraceLevel(object? element)
        {
            if (element is null || !s_traceLevels.TryGetValue(element, out var holder))
            {
                return PresentationTraceLevel.None;
            }

            return (PresentationTraceLevel)Volatile.Read(ref holder.Level);
        }

        public static void SetTraceLevel(object? element, PresentationTraceLevel traceLevel)
        {
            if (element is null)
            {
                return;
            }

            if (traceLevel <= PresentationTraceLevel.None)
            {
                s_traceLevels.Remove(element);
                return;
            }

            var holder = s_traceLevels.GetValue(element, static _ => new TraceLevelHolder());
            Volatile.Write(ref holder.Level, (int)traceLevel);
        }

        public static void Refresh()
        {
            Trace.Refresh();
        }

        private static Lazy<TraceSource> Create(string sourceName) =>
            new(() =>
            {
                var source = new TraceSource(sourceName);
                if (source.Switch.Level == SourceLevels.Off && Debugger.IsAttached)
                {
                    source.Switch.Level = SourceLevels.Warning;
                }

                return source;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}

namespace Jalium.UI.Diagnostics
{
    /// <summary>Describes a binding failure emitted by the binding engine.</summary>
    public class BindingFailedEventArgs : EventArgs
    {
        internal BindingFailedEventArgs(
            global::System.Diagnostics.TraceEventType eventType,
            int code,
            string? message,
            BindingExpressionBase binding,
            params object[]? parameters)
        {
            EventType = eventType;
            Code = code;
            Message = message ?? string.Empty;
            Binding = binding;
            Parameters = parameters ?? Array.Empty<object>();
        }

        public global::System.Diagnostics.TraceEventType EventType { get; }
        public int Code { get; }
        public string Message { get; }
        public BindingExpressionBase Binding { get; }
        public object[] Parameters { get; }
    }

    /// <summary>Identifies whether a visual child was attached or detached.</summary>
    public enum VisualTreeChangeType
    {
        Add = 0,
        Remove = 1,
    }

    /// <summary>Provides data for a visual-tree change notification.</summary>
    public class VisualTreeChangeEventArgs : EventArgs
    {
        public VisualTreeChangeEventArgs(
            DependencyObject parent,
            DependencyObject child,
            int childIndex,
            VisualTreeChangeType changeType)
        {
            Parent = parent;
            Child = child;
            ChildIndex = childIndex;
            ChangeType = changeType;
        }

        public DependencyObject Parent { get; }
        public DependencyObject Child { get; }
        public int ChildIndex { get; }
        public VisualTreeChangeType ChangeType { get; }
    }

    /// <summary>Associates an object created from XAML with its source location.</summary>
    [global::System.Diagnostics.DebuggerDisplay("line={LineNumber}, offset={LinePosition}, uri={SourceUri}")]
    public class XamlSourceInfo
    {
        public XamlSourceInfo(Uri? sourceUri, int lineNumber, int linePosition)
        {
            SourceUri = sourceUri;
            LineNumber = lineNumber;
            LinePosition = linePosition;
        }

        public Uri? SourceUri { get; }
        public int LineNumber { get; }
        public int LinePosition { get; }
    }

    /// <summary>Publishes visual-tree changes and XAML source information.</summary>
    public static class VisualDiagnostics
    {
        private static readonly ConditionalWeakTable<object, XamlSourceInfo> s_sourceInfo = new();
        private static readonly object s_sourceInfoLock = new();
        private static int s_visualTreeChangedEnabled;

        public static event EventHandler<VisualTreeChangeEventArgs>? VisualTreeChanged;

        public static void EnableVisualTreeChanged() =>
            Volatile.Write(ref s_visualTreeChangedEnabled, 1);

        public static void DisableVisualTreeChanged() =>
            Volatile.Write(ref s_visualTreeChangedEnabled, 0);

        public static XamlSourceInfo? GetXamlSourceInfo(object? obj)
        {
            if (obj is null)
            {
                return null;
            }

            return s_sourceInfo.TryGetValue(obj, out var sourceInfo) ? sourceInfo : null;
        }

        internal static void SetXamlSourceInfo(object? obj, XamlSourceInfo? sourceInfo)
        {
            if (obj is null)
            {
                return;
            }

            lock (s_sourceInfoLock)
            {
                s_sourceInfo.Remove(obj);
                if (sourceInfo is not null)
                {
                    s_sourceInfo.Add(obj, sourceInfo);
                }
            }
        }

        internal static void NotifyVisualChildChanged(
            DependencyObject parent,
            DependencyObject child,
            int childIndex,
            VisualTreeChangeType changeType)
        {
            if (Volatile.Read(ref s_visualTreeChangedEnabled) == 0)
            {
                return;
            }

            VisualTreeChanged?.Invoke(
                null,
                new VisualTreeChangeEventArgs(parent, child, childIndex, changeType));
        }
    }

    /// <summary>Describes a loaded resource dictionary.</summary>
    [global::System.Diagnostics.DebuggerDisplay("Assembly = {Assembly?.GetName()?.Name}, ResourceDictionary SourceUri = {SourceUri?.AbsoluteUri}")]
    public class ResourceDictionaryInfo
    {
        internal ResourceDictionaryInfo(
            Assembly? assembly,
            Assembly? resourceDictionaryAssembly,
            ResourceDictionary resourceDictionary,
            Uri? sourceUri)
        {
            Assembly = assembly;
            ResourceDictionaryAssembly = resourceDictionaryAssembly;
            ResourceDictionary = resourceDictionary;
            SourceUri = sourceUri;
        }

        public Assembly? Assembly { get; }
        public Assembly? ResourceDictionaryAssembly { get; }
        public ResourceDictionary ResourceDictionary { get; }
        public Uri? SourceUri { get; }
    }

    public class ResourceDictionaryLoadedEventArgs : EventArgs
    {
        internal ResourceDictionaryLoadedEventArgs(ResourceDictionaryInfo resourceDictionaryInfo) =>
            ResourceDictionaryInfo = resourceDictionaryInfo;

        public ResourceDictionaryInfo ResourceDictionaryInfo { get; }
    }

    public class ResourceDictionaryUnloadedEventArgs : EventArgs
    {
        internal ResourceDictionaryUnloadedEventArgs(ResourceDictionaryInfo resourceDictionaryInfo) =>
            ResourceDictionaryInfo = resourceDictionaryInfo;

        public ResourceDictionaryInfo ResourceDictionaryInfo { get; }
    }

    public class StaticResourceResolvedEventArgs : EventArgs
    {
        internal StaticResourceResolvedEventArgs(
            object? targetObject,
            object? targetProperty,
            ResourceDictionary resourceDictionary,
            object key)
        {
            TargetObject = targetObject;
            TargetProperty = targetProperty;
            ResourceDictionary = resourceDictionary;
            ResourceKey = key;
        }

        public object? TargetObject { get; }
        public object? TargetProperty { get; }
        public ResourceDictionary ResourceDictionary { get; }
        public object ResourceKey { get; }
    }

    internal enum ResourceDictionaryOwnerKind
    {
        FrameworkElement,
        FrameworkContentElement,
        Application,
    }

    internal enum ResourceDictionaryRegistrationKind
    {
        Themed,
        Generic,
    }

    /// <summary>
    /// Weak, assembly-neutral state store used by the Core resource engine and the Controls
    /// public diagnostics facade. It intentionally never pins an owner or dictionary.
    /// </summary>
    internal static class ResourceDictionaryDiagnosticsStore
    {
        private sealed record OwnerEntry(WeakReference<object> Owner, ResourceDictionaryOwnerKind Kind);

        private sealed class OwnerBucket
        {
            public object Gate { get; } = new();
            public List<OwnerEntry> Entries { get; } = new();
        }

        private sealed class ParentBucket
        {
            public object Gate { get; } = new();
            public List<WeakReference<ResourceDictionary>> Parents { get; } = new();
        }

        private static readonly object s_sourceGate = new();
        private static readonly Dictionary<Uri, List<WeakReference<ResourceDictionary>>> s_dictionariesBySource = new();
        private static readonly ConditionalWeakTable<ResourceDictionary, OwnerBucket> s_owners = new();
        private static readonly ConditionalWeakTable<ResourceDictionary, ParentBucket> s_parents = new();
        private static readonly object s_registrationGate = new();
        private static readonly List<WeakReference<ResourceDictionary>> s_themedDictionaries = new();
        private static readonly List<WeakReference<ResourceDictionary>> s_genericDictionaries = new();

        internal static event EventHandler<ResourceDictionaryLoadedEventArgs>? ThemedResourceDictionaryLoaded;
        internal static event EventHandler<ResourceDictionaryUnloadedEventArgs>? ThemedResourceDictionaryUnloaded;
        internal static event EventHandler<ResourceDictionaryLoadedEventArgs>? GenericResourceDictionaryLoaded;
        internal static event EventHandler<StaticResourceResolvedEventArgs>? StaticResourceResolved;

        internal static void RegisterSource(ResourceDictionary dictionary, Uri source)
        {
            lock (s_sourceGate)
            {
                if (!s_dictionariesBySource.TryGetValue(source, out var entries))
                {
                    entries = new List<WeakReference<ResourceDictionary>>();
                    s_dictionariesBySource.Add(source, entries);
                }

                RemoveDeadAndMatching(entries, dictionary);
                entries.Add(new WeakReference<ResourceDictionary>(dictionary));
            }
        }

        internal static void UnregisterSource(ResourceDictionary dictionary, Uri source)
        {
            lock (s_sourceGate)
            {
                if (!s_dictionariesBySource.TryGetValue(source, out var entries))
                {
                    return;
                }

                RemoveDeadAndMatching(entries, dictionary);
                if (entries.Count == 0)
                {
                    s_dictionariesBySource.Remove(source);
                }
            }
        }

        internal static IReadOnlyCollection<ResourceDictionary> GetDictionariesForSource(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            lock (s_sourceGate)
            {
                if (!s_dictionariesBySource.TryGetValue(uri, out var entries))
                {
                    return Array.Empty<ResourceDictionary>();
                }

                var live = SnapshotLive(entries);
                if (entries.Count == 0)
                {
                    s_dictionariesBySource.Remove(uri);
                }

                return new ReadOnlyCollection<ResourceDictionary>(live);
            }
        }

        internal static void RegisterOwner(
            ResourceDictionary dictionary,
            object owner,
            ResourceDictionaryOwnerKind kind)
        {
            var bucket = s_owners.GetValue(dictionary, static _ => new OwnerBucket());
            lock (bucket.Gate)
            {
                for (var index = bucket.Entries.Count - 1; index >= 0; index--)
                {
                    if (!bucket.Entries[index].Owner.TryGetTarget(out var existing))
                    {
                        bucket.Entries.RemoveAt(index);
                        continue;
                    }

                    if (ReferenceEquals(existing, owner))
                    {
                        return;
                    }
                }

                bucket.Entries.Add(new OwnerEntry(new WeakReference<object>(owner), kind));
            }
        }

        internal static void UnregisterOwner(ResourceDictionary dictionary, object owner)
        {
            if (!s_owners.TryGetValue(dictionary, out var bucket))
            {
                return;
            }

            lock (bucket.Gate)
            {
                for (var index = bucket.Entries.Count - 1; index >= 0; index--)
                {
                    if (!bucket.Entries[index].Owner.TryGetTarget(out var existing) ||
                        ReferenceEquals(existing, owner))
                    {
                        bucket.Entries.RemoveAt(index);
                    }
                }
            }
        }

        internal static IReadOnlyCollection<object> GetOwners(
            ResourceDictionary dictionary,
            ResourceDictionaryOwnerKind kind)
        {
            ArgumentNullException.ThrowIfNull(dictionary);

            var owners = new List<object>();
            var visitedDictionaries = new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
            var visitedOwners = new HashSet<object>(ReferenceEqualityComparer.Instance);
            CollectOwners(dictionary, kind, owners, visitedDictionaries, visitedOwners);
            return new ReadOnlyCollection<object>(owners);
        }

        internal static void LinkMergedDictionary(ResourceDictionary parent, ResourceDictionary child)
        {
            var bucket = s_parents.GetValue(child, static _ => new ParentBucket());
            lock (bucket.Gate)
            {
                for (var index = bucket.Parents.Count - 1; index >= 0; index--)
                {
                    if (!bucket.Parents[index].TryGetTarget(out var existing))
                    {
                        bucket.Parents.RemoveAt(index);
                        continue;
                    }

                    if (ReferenceEquals(existing, parent))
                    {
                        return;
                    }
                }

                bucket.Parents.Add(new WeakReference<ResourceDictionary>(parent));
            }
        }

        internal static void UnlinkMergedDictionary(ResourceDictionary parent, ResourceDictionary child)
        {
            if (!s_parents.TryGetValue(child, out var bucket))
            {
                return;
            }

            lock (bucket.Gate)
            {
                for (var index = bucket.Parents.Count - 1; index >= 0; index--)
                {
                    if (!bucket.Parents[index].TryGetTarget(out var existing) ||
                        ReferenceEquals(existing, parent))
                    {
                        bucket.Parents.RemoveAt(index);
                    }
                }
            }
        }

        internal static void RegisterSystemDictionary(
            ResourceDictionary dictionary,
            ResourceDictionaryRegistrationKind kind)
        {
            ArgumentNullException.ThrowIfNull(dictionary);

            lock (s_registrationGate)
            {
                var entries = kind == ResourceDictionaryRegistrationKind.Themed
                    ? s_themedDictionaries
                    : s_genericDictionaries;
                RemoveDeadAndMatching(entries, dictionary);
                entries.Add(new WeakReference<ResourceDictionary>(dictionary));
            }

            var eventArgs = new ResourceDictionaryLoadedEventArgs(CreateInfo(dictionary));
            if (kind == ResourceDictionaryRegistrationKind.Themed)
            {
                ThemedResourceDictionaryLoaded?.Invoke(null, eventArgs);
            }
            else
            {
                GenericResourceDictionaryLoaded?.Invoke(null, eventArgs);
            }
        }

        internal static void UnregisterSystemDictionary(
            ResourceDictionary dictionary,
            ResourceDictionaryRegistrationKind kind)
        {
            lock (s_registrationGate)
            {
                var entries = kind == ResourceDictionaryRegistrationKind.Themed
                    ? s_themedDictionaries
                    : s_genericDictionaries;
                RemoveDeadAndMatching(entries, dictionary);
            }

            if (kind == ResourceDictionaryRegistrationKind.Themed)
            {
                ThemedResourceDictionaryUnloaded?.Invoke(
                    null,
                    new ResourceDictionaryUnloadedEventArgs(CreateInfo(dictionary)));
            }
        }

        internal static IReadOnlyCollection<ResourceDictionaryInfo> GetSystemDictionaries(
            ResourceDictionaryRegistrationKind kind)
        {
            lock (s_registrationGate)
            {
                var entries = kind == ResourceDictionaryRegistrationKind.Themed
                    ? s_themedDictionaries
                    : s_genericDictionaries;
                var live = SnapshotLive(entries);
                return new ReadOnlyCollection<ResourceDictionaryInfo>(live.Select(CreateInfo).ToList());
            }
        }

        internal static void NotifyStaticResourceResolved(
            object? targetObject,
            object? targetProperty,
            ResourceDictionary? dictionary,
            object key)
        {
            if (dictionary is null)
            {
                return;
            }

            StaticResourceResolved?.Invoke(
                null,
                new StaticResourceResolvedEventArgs(targetObject, targetProperty, dictionary, key));
        }

        private static ResourceDictionaryInfo CreateInfo(ResourceDictionary dictionary)
        {
            var dictionaryAssembly = dictionary.SourceAssembly ?? dictionary.GetType().Assembly;
            return new ResourceDictionaryInfo(
                dictionaryAssembly,
                dictionaryAssembly,
                dictionary,
                dictionary.Source ?? dictionary.BaseUri);
        }

        private static void CollectOwners(
            ResourceDictionary dictionary,
            ResourceDictionaryOwnerKind kind,
            List<object> result,
            HashSet<ResourceDictionary> visitedDictionaries,
            HashSet<object> visitedOwners)
        {
            if (!visitedDictionaries.Add(dictionary))
            {
                return;
            }

            if (s_owners.TryGetValue(dictionary, out var ownerBucket))
            {
                lock (ownerBucket.Gate)
                {
                    for (var index = ownerBucket.Entries.Count - 1; index >= 0; index--)
                    {
                        var entry = ownerBucket.Entries[index];
                        if (!entry.Owner.TryGetTarget(out var owner))
                        {
                            ownerBucket.Entries.RemoveAt(index);
                            continue;
                        }

                        if (entry.Kind == kind && visitedOwners.Add(owner))
                        {
                            result.Add(owner);
                        }
                    }
                }
            }

            if (!s_parents.TryGetValue(dictionary, out var parentBucket))
            {
                return;
            }

            List<ResourceDictionary> parents;
            lock (parentBucket.Gate)
            {
                parents = SnapshotLive(parentBucket.Parents);
            }

            foreach (var parent in parents)
            {
                CollectOwners(parent, kind, result, visitedDictionaries, visitedOwners);
            }
        }

        private static List<ResourceDictionary> SnapshotLive(
            List<WeakReference<ResourceDictionary>> entries)
        {
            var live = new List<ResourceDictionary>(entries.Count);
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index].TryGetTarget(out var dictionary))
                {
                    live.Add(dictionary);
                }
                else
                {
                    entries.RemoveAt(index);
                }
            }

            live.Reverse();
            return live;
        }

        private static void RemoveDeadAndMatching(
            List<WeakReference<ResourceDictionary>> entries,
            ResourceDictionary dictionary)
        {
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                if (!entries[index].TryGetTarget(out var existing) ||
                    ReferenceEquals(existing, dictionary))
                {
                    entries.RemoveAt(index);
                }
            }
        }
    }
}
