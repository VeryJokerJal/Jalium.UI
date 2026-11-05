using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

internal static partial class CssGeneratedContent
{
    // Nonvisual documents can have several simultaneous consumers. Each view
    // owns its marker visual; counter identity and selectors belong to Origin.
    private sealed record ProjectedHostBinding(WeakReference<FrameworkElement> Host, Action? OnChanged);
    private static readonly ConditionalWeakTable<CssNode, List<WeakReference<CssGeneratedText>>> ProjectedMarkers = new();
    private static readonly ConditionalWeakTable<CssNode, StrongBox<CssPseudoElement>> ProjectedTargets = new();
    private static readonly ConditionalWeakTable<CssNode, List<ProjectedHostBinding>> ProjectedHosts = new();

    internal static bool HasProjectedTarget(DependencyObject source)
    {
        if (CssReplacedContent.IsActive(source)) return true;
        var origin = CssNode.Get(source);
        if (source is Documents.Inline && CssListProperties.IsItem(origin)) return true;
        return ProjectedTargets.TryGetValue(origin, out var targets) &&
            (targets.Value & (CssPseudoElement.Before | CssPseudoElement.After | CssPseudoElement.BeforeMarker | CssPseudoElement.AfterMarker |
                (source is Documents.Inline ? CssPseudoElement.Marker : CssPseudoElement.None))) != 0;
    }

    internal static void RecordProjectedTargets(CssNode origin, CssPseudoElement targets)
    {
        if (origin.Target is not FrameworkContentElement contentOrigin) return;
        var relevant = CssPseudoElement.Before | CssPseudoElement.After | CssPseudoElement.BeforeMarker | CssPseudoElement.AfterMarker |
            (contentOrigin is Documents.Inline ? CssPseudoElement.Marker : CssPseudoElement.None);
        targets &= relevant;
        var box = ProjectedTargets.GetOrCreateValue(origin);
        var previous = box.Value;
        box.Value = targets;
        var wasReplaced = CssReplacedContent.IsActive(contentOrigin);
        CssReplacedContent.Refresh(contentOrigin);
        var isReplaced = CssReplacedContent.IsActive(contentOrigin);
        var listItem = contentOrigin is Documents.Inline && CssListProperties.IsItem(origin);
        if (previous == targets && wasReplaced == isReplaced && targets == CssPseudoElement.None && !isReplaced && !listItem)
            return;
        var hasLiveHost = false;
        if (ProjectedHosts.TryGetValue(origin, out var hosts))
        {
            foreach (var binding in hosts.ToArray())
            {
                if (binding.Host.TryGetTarget(out var host))
                {
                    hasLiveHost = true;
                    RefreshProjectedHost(origin, host);
                    if (previous != targets || wasReplaced != isReplaced)
                        binding.OnChanged?.Invoke();
                }
                else hosts.Remove(binding);
            }
        }
        if (!hasLiveHost && (targets != CssPseudoElement.None || previous != CssPseudoElement.None || wasReplaced != isReplaced || listItem))
        {
            if (contentOrigin is Documents.TextElement textElement) textElement.NotifyTextContentChanged();
            else if (contentOrigin is Documents.FlowDocument flowDocument) flowDocument.NotifyPaginationStyleChanged();
        }
    }

    internal static void BindProjectedHost(DependencyObject source, FrameworkElement host, Action? onChanged = null)
    {
        if (source is not FrameworkContentElement) return;
        var origin = CssNode.Get(source);
        var list = ProjectedHosts.GetOrCreateValue(origin);
        var index = list.FindIndex(b => b.Host.TryGetTarget(out var existing) && ReferenceEquals(existing, host));
        if (index >= 0)
        {
            if (onChanged is not null) list[index] = new(list[index].Host, onChanged);
        }
        else
        {
            list.Add(new(new WeakReference<FrameworkElement>(host), onChanged));
        }
        RefreshProjectedHost(origin, host);
        CssFirstLetter.ContentChanged();
        CssFirstLine.ContentChanged();
    }

    internal static void UnbindProjectedHost(DependencyObject source, FrameworkElement host)
    {
        if (source is not FrameworkContentElement) return;
        var origin = CssNode.Get(source);
        if (ProjectedHosts.TryGetValue(origin, out var list) &&
            list.RemoveAll(b => !b.Host.TryGetTarget(out var target) || ReferenceEquals(target, host)) > 0)
        {
            CssFirstLetter.ContentChanged();
            CssFirstLine.ContentChanged();
        }
    }

    internal static void RefreshProjectedHost(CssNode origin, FrameworkElement host)
    {
        if (CssReplacedContent.TryGetContentValue(origin.Target, out var replacedValue))
            CssReplacedContent.Set(host, replacedValue);
        else
            CssReplacedContent.Set(host, CssGeneratedContentValue.Normal);
        var targets = ProjectedTargets.TryGetValue(origin, out var box) ? box.Value : CssPseudoElement.None;
        UpdateHostSlots(origin, host, targets, allowMarker: origin.Target is Documents.Inline);
    }

    internal static IEnumerable<CssGeneratedText> Befores(CssNode origin)
    {
        if (origin.Target is FrameworkElement owner && Get(owner)?.Before is { } native) yield return native;
        if (!ProjectedHosts.TryGetValue(origin, out var list)) yield break;
        foreach (var binding in list.ToArray())
            if (binding.Host.TryGetTarget(out var host))
            {
                if (Get(host)?.Before is { } before) yield return before;
            }
            else list.Remove(binding);
    }

    internal static IEnumerable<CssGeneratedText> Afters(CssNode origin)
    {
        if (origin.Target is FrameworkElement owner && Get(owner)?.After is { } native) yield return native;
        if (!ProjectedHosts.TryGetValue(origin, out var list)) yield break;
        foreach (var binding in list.ToArray())
            if (binding.Host.TryGetTarget(out var host))
            {
                if (Get(host)?.After is { } after) yield return after;
            }
            else list.Remove(binding);
    }

    internal static IDisposable RegisterProjectedMarker(CssNode origin, CssGeneratedText marker)
    {
        var list = ProjectedMarkers.GetOrCreateValue(origin);
        var reference = new WeakReference<CssGeneratedText>(marker); list.Add(reference);
        RefreshProjectedMarker(origin, marker); CssCounters.Invalidate(origin);
        var node = CssNode.Get(marker);
        void Changed(DependencyProperty property, object? before, object? after)
        {
            if (Equals(before, after)) return;
            if (property.Name is "Text" or "FontFamily" or "FontSize" or "FontWeight" or "FontStyle" or "FontStretch" or
                "Foreground" or "LineHeight" or "LineStackingStrategy" or "FlowDirection" or "TextDecorations" or "TextEffects" ||
                property.OwnerType.Name.StartsWith("Css", StringComparison.Ordinal)) marker.ProjectionInvalidated?.Invoke();
        }
        node.PropertyChangedInternal += Changed;
        return new ProjectedMarkerRegistration(origin, list, reference, () => node.PropertyChangedInternal -= Changed);
    }

    private static void RefreshProjectedMarkers(CssNode origin)
    {
        if (!ProjectedMarkers.TryGetValue(origin, out var list)) return;
        foreach (var reference in list.ToArray())
            if (reference.TryGetTarget(out var marker)) RefreshProjectedMarker(origin, marker);
            else list.Remove(reference);
    }

    private static void RefreshProjectedMarker(CssNode origin, CssGeneratedText marker)
    {
        marker.Select(CssListProperties.IsItem(origin)); marker.CoerceValue(ContentProperty);
        CssEngine.EvaluateElement(marker);
    }

    internal static IEnumerable<CssGeneratedText> Markers(CssNode origin)
    {
        if (origin.Target is FrameworkElement owner && Get(owner)?.Marker is { } native) yield return native;
        if (ProjectedHosts.TryGetValue(origin, out var hosts))
            foreach (var binding in hosts.ToArray())
                if (binding.Host.TryGetTarget(out var host) && Get(host)?.Marker is { } hostMarker) yield return hostMarker;
        if (!ProjectedMarkers.TryGetValue(origin, out var list)) yield break;
        foreach (var reference in list.ToArray())
            if (reference.TryGetTarget(out var marker)) yield return marker;
            else list.Remove(reference);
    }

    internal static IDisposable AttachProjectedMarker(FrameworkElement host, CssGeneratedText marker)
    {
        var slots = Elements.GetValue(host, static _ => new());
        if (slots.Marker is not null) throw new InvalidOperationException("The host already has a marker.");
        s_active = true; slots.Marker = marker; host.HasCssGeneratedChildren = true;
        host.AttachCssGeneratedChild(marker);
        return new ProjectedMarkerAttachment(host, marker);
    }

    private sealed class ProjectedMarkerRegistration(CssNode origin, List<WeakReference<CssGeneratedText>> list,
        WeakReference<CssGeneratedText> reference, Action unsubscribe) : IDisposable
    {
        public void Dispose() { unsubscribe(); if (list.Remove(reference)) CssCounters.Invalidate(origin); }
    }
    private sealed class ProjectedMarkerAttachment(FrameworkElement host, CssGeneratedText marker) : IDisposable
    {
        public void Dispose()
        {
            if (Get(host) is not { } slots || !ReferenceEquals(slots.Marker, marker)) return;
            slots.Marker = null; host.DetachCssGeneratedChild(marker);
            host.HasCssGeneratedChildren = slots.Before is not null || slots.After is not null;
        }
    }
}
