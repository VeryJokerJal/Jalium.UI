using System.Collections.Concurrent;
using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Markup;

/// <summary>
/// Runtime entry point for JALXAML hot reload patching.
/// </summary>
public static class HotReloadRuntime
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, List<WeakReference<FrameworkElement>>> ComponentsByClass = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<DependencyProperty>> DependencyPropertyCache = new();

    public static void RegisterComponent(object component)
    {
        if (component is not FrameworkElement element)
        {
            return;
        }

        var xClass = element.GetType().FullName;
        if (string.IsNullOrWhiteSpace(xClass))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!ComponentsByClass.TryGetValue(xClass, out var entries))
            {
                entries = [];
                ComponentsByClass[xClass] = entries;
            }

            CleanupDeadEntries(entries);

            if (entries.Any(wr => wr.TryGetTarget(out var current) && ReferenceEquals(current, element)))
            {
                return;
            }

            entries.Add(new WeakReference<FrameworkElement>(element));
        }
    }

    /// <summary>
    /// Applies a JALXAML patch to all active instances of the specified x:Class.
    /// </summary>
    public static HotReloadPatchResult ApplyPatch(string xClass, string filePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        object parsed;
        try
        {
            parsed = XamlReader.Parse(content);
        }
        catch (Exception ex)
        {
            return new HotReloadPatchResult(0, 0, 1, $"Failed to parse JALXAML patch: {ex.Message}");
        }

        if (parsed is not FrameworkElement incomingRoot)
        {
            return new HotReloadPatchResult(0, 0, 1, "JALXAML patch root is not a FrameworkElement.");
        }

        List<FrameworkElement> activeInstances;
        lock (SyncRoot)
        {
            if (!ComponentsByClass.TryGetValue(xClass, out var entries))
            {
                return new HotReloadPatchResult(0, 0, 0, "No active instances for target x:Class.");
            }

            CleanupDeadEntries(entries);
            activeInstances = entries
                .Select(static wr => wr.TryGetTarget(out var target) ? target : null)
                .Where(static target => target != null)
                .Cast<FrameworkElement>()
                .ToList();
        }

        if (activeInstances.Count == 0)
        {
            return new HotReloadPatchResult(0, 0, 0, "No active instances for target x:Class.");
        }

        var updated = 0;
        var fallback = 0;
        var failed = 0;

        foreach (var targetRoot in activeInstances)
        {
            try
            {
                var counters = new PatchCounters();
                ApplyElementPatch(targetRoot, incomingRoot, counters);
                updated += counters.UpdatedElements;
                fallback += counters.FallbackReplacements;
            }
            catch
            {
                failed++;
            }
        }

        return new HotReloadPatchResult(updated, fallback, failed, string.Empty);
    }

    private static void ApplyElementPatch(FrameworkElement target, FrameworkElement source, PatchCounters counters)
    {
        if (!AreTypesCompatible(target.GetType(), source.GetType()))
        {
            counters.FailedElements++;
            return;
        }

        CopyDependencyProperties(target, source);
        CopyClrProperties(target, source);
        counters.UpdatedElements++;

        if (target is Panel targetPanel && source is Panel sourcePanel)
        {
            PatchPanelChildren(targetPanel, sourcePanel, counters);
            return;
        }

        if (target is ContentControl targetContent && source is ContentControl sourceContent)
        {
            PatchContentControl(targetContent, sourceContent, counters);
            return;
        }

        if (target is Border targetBorder && source is Border sourceBorder)
        {
            PatchSingleChildContainer(
                targetBorder,
                sourceBorder,
                static border => border.Child,
                static (border, child) => border.Child = child,
                counters);
            return;
        }

        if (target is Viewbox targetViewbox && source is Viewbox sourceViewbox)
        {
            PatchSingleChildContainer(
                targetViewbox,
                sourceViewbox,
                static viewbox => viewbox.Child,
                static (viewbox, child) => viewbox.Child = child,
                counters);
        }
    }

    private static void PatchPanelChildren(Panel targetPanel, Panel sourcePanel, PatchCounters counters)
    {
        var existingChildren = targetPanel.Children.ToList();
        var sourceChildren = sourcePanel.Children.ToList();
        var used = new HashSet<UIElement>();
        var merged = new List<UIElement>(sourceChildren.Count);

        for (var i = 0; i < sourceChildren.Count; i++)
        {
            var sourceChild = sourceChildren[i];
            var matched = FindMatchingChild(existingChildren, used, sourceChild, i);

            if (matched == null)
            {
                merged.Add(sourceChild);
                counters.FallbackReplacements++;
                continue;
            }

            used.Add(matched);
            if (matched is FrameworkElement targetChildFe && sourceChild is FrameworkElement sourceChildFe)
            {
                ApplyElementPatch(targetChildFe, sourceChildFe, counters);
                merged.Add(matched);
            }
            else
            {
                merged.Add(sourceChild);
                counters.FallbackReplacements++;
            }
        }

        targetPanel.Children.Clear();
        foreach (var child in merged)
        {
            targetPanel.Children.Add(child);
        }
    }

    private static UIElement? FindMatchingChild(
        List<UIElement> existingChildren,
        HashSet<UIElement> used,
        UIElement sourceChild,
        int indexHint)
    {
        if (sourceChild is FrameworkElement sourceFe && !string.IsNullOrWhiteSpace(sourceFe.Name))
        {
            var named = existingChildren.FirstOrDefault(candidate =>
                !used.Contains(candidate)
                && candidate is FrameworkElement candidateFe
                && AreTypesCompatible(candidate.GetType(), sourceChild.GetType())
                && string.Equals(candidateFe.Name, sourceFe.Name, StringComparison.Ordinal));

            if (named != null)
            {
                return named;
            }
        }

        if (indexHint >= 0 && indexHint < existingChildren.Count)
        {
            var indexed = existingChildren[indexHint];
            if (!used.Contains(indexed) && AreTypesCompatible(indexed.GetType(), sourceChild.GetType()))
            {
                return indexed;
            }
        }

        return null;
    }

    private static void PatchContentControl(ContentControl target, ContentControl source, PatchCounters counters)
    {
        var sourceContent = source.Content;
        if (sourceContent is not UIElement sourceElement)
        {
            target.Content = sourceContent;
            return;
        }

        if (target.Content is FrameworkElement targetFe && sourceElement is FrameworkElement sourceFe
            && AreTypesCompatible(targetFe.GetType(), sourceFe.GetType()))
        {
            ApplyElementPatch(targetFe, sourceFe, counters);
            return;
        }

        target.Content = sourceContent;
        counters.FallbackReplacements++;
    }

    private static void PatchSingleChildContainer<TContainer>(
        TContainer target,
        TContainer source,
        Func<TContainer, UIElement?> getChild,
        Action<TContainer, UIElement?> setChild,
        PatchCounters counters)
        where TContainer : FrameworkElement
    {
        var sourceChild = getChild(source);
        if (sourceChild == null)
        {
            setChild(target, null);
            return;
        }

        var targetChild = getChild(target);
        if (targetChild is FrameworkElement targetFe && sourceChild is FrameworkElement sourceFe
            && AreTypesCompatible(targetFe.GetType(), sourceFe.GetType()))
        {
            ApplyElementPatch(targetFe, sourceFe, counters);
            return;
        }

        setChild(target, sourceChild);
        counters.FallbackReplacements++;
    }

    private static void CopyDependencyProperties(DependencyObject target, DependencyObject source)
    {
        var dps = DependencyPropertyCache.GetOrAdd(target.GetType(), static type =>
        {
            var result = new List<DependencyProperty>();
            for (var current = type; current != null; current = current.BaseType)
            {
                var fields = current.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (!typeof(DependencyProperty).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }

                    if (field.GetValue(null) is DependencyProperty dp && !result.Contains(dp))
                    {
                        result.Add(dp);
                    }
                }
            }

            return result;
        });

        foreach (var dp in dps)
        {
            if (dp == FrameworkElement.NameProperty)
            {
                continue;
            }

            var sourceValue = source.ReadLocalValue(dp);
            if (!ReferenceEquals(sourceValue, DependencyProperty.UnsetValue))
            {
                target.SetValue(dp, sourceValue);
            }
        }
    }

    private static void CopyClrProperties(object target, object source)
    {
        var targetType = target.GetType();
        var sourceType = source.GetType();
        if (!AreTypesCompatible(targetType, sourceType))
        {
            return;
        }

        var properties = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.Name is "Name" or "Parent" or "VisualParent" or "TemplatedParent")
            {
                continue;
            }

            var propertyType = property.PropertyType;
            if (typeof(DependencyObject).IsAssignableFrom(propertyType)
                || typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string))
            {
                continue;
            }

            try
            {
                var value = property.GetValue(source);
                property.SetValue(target, value);
            }
            catch
            {
                // Ignore non-copyable CLR properties.
            }
        }
    }

    private static void CleanupDeadEntries(List<WeakReference<FrameworkElement>> entries)
    {
        entries.RemoveAll(static wr => !wr.TryGetTarget(out _));
    }

    private static bool AreTypesCompatible(Type targetType, Type sourceType)
    {
        return targetType == sourceType
               || sourceType.IsAssignableFrom(targetType)
               || targetType.IsAssignableFrom(sourceType);
    }

    private sealed class PatchCounters
    {
        public int UpdatedElements;
        public int FallbackReplacements;
        public int FailedElements;
    }
}

/// <summary>
/// Result for runtime JALXAML patch apply.
/// </summary>
public sealed record HotReloadPatchResult(
    int UpdatedElements,
    int FallbackReplacements,
    int FailedElements,
    string Message);
