using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>One registration map per native document root; no process-global DP registration or control-tree rewriting.</summary>
internal static class CssRegisteredProperties
{
    private sealed class Document
    {
        internal int Version = -1;
        internal bool Dirty = true;
        internal Size Viewport;
        internal IReadOnlyDictionary<string, CssPropertyRegistration> Properties = Empty;
    }

    private static readonly ConditionalWeakTable<CssNode, Document> s_documents = new();
    internal static readonly IReadOnlyDictionary<string, CssPropertyRegistration> Empty = new Dictionary<string, CssPropertyRegistration>(StringComparer.Ordinal);
    internal static bool IsActive;

    internal static IReadOnlyDictionary<string, CssPropertyRegistration> For(CssNode element)
    {
        if (!IsActive) return Empty;
        var root = CssMatcher.Root(element);
        var document = s_documents.GetValue(root, static _ => new());
        var viewport = CssEngine.ViewportSize(root);
        if (!document.Dirty && document.Version == CssEngine.CascadeVersion && document.Viewport == viewport) return document.Properties;
        var properties = new Dictionary<string, CssPropertyRegistration>(StringComparer.Ordinal);
        var layerOrder=new CssLayerOrder();
        var propertyLayers=new Dictionary<string,int[]>(StringComparer.Ordinal);
        void Add(IEnumerable<CssStyleSheet> sheets)
        {
            foreach (var sheet in sheets)
            {
                foreach(var layer in sheet.Layers)
                    if(layer.Condition is null || layer.Condition.Evaluate(root)) layerOrder.GetKey(layer.Name);
                foreach (var registration in sheet.Properties)
                {
                    if (registration.Condition is not null && !registration.Condition.Evaluate(root)) continue;
                    var key=layerOrder.GetKey(registration.LayerName);
                    if (propertyLayers.TryGetValue(registration.Name,out var previous) && CssLayerOrder.Compare(key,previous)<0) continue;
                    properties[registration.Name]=registration; propertyLayers[registration.Name]=key;
                }
            }
        }
        if (CssEngine.ApplicationStyleSheetsProvider?.Invoke() is { } application) Add(application);
        var pending = new Stack<CssNode>(); pending.Push(root);
        var seen = new HashSet<CssNode>();
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            if (current.CssRuntimeState?.ScopedStyleSheets is { } sheets) Add(sheets);
            foreach (var child in current.EnumerateChildren().Reverse()) pending.Push(child);
        }
        var changed = properties.Count != document.Properties.Count || properties.Any(pair =>
            !document.Properties.TryGetValue(pair.Key, out var previous) || !ReferenceEquals(previous, pair.Value));
        document.Dirty = false; document.Version = CssEngine.CascadeVersion; document.Viewport = viewport;
        if (changed)
        {
            document.Properties = properties;
            // Rules attached below the root still define document-wide property names.
            CssEvaluationScheduler.InvalidateSubtree(root);
        }
        return document.Properties;
    }

    internal static void Invalidate(CssNode element)
    {
        if (!IsActive) return;
        var root = CssMatcher.Root(element);
        s_documents.GetValue(root, static _ => new()).Dirty = true;
        CssEvaluationScheduler.InvalidateElement(root);
    }
}
