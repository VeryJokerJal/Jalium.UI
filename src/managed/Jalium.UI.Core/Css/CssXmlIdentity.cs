using System.Globalization;
using System.Runtime.CompilerServices;
using Jalium.UI.Markup;

namespace Jalium.UI.Styling;

internal readonly record struct CssExpandedName(string NamespaceUri,string LocalName);
internal sealed record CssXmlAttribute(string Value,Type? OwnerType);

/// <summary>Weak markup metadata keeps native objects, bindings and namescopes intact.</summary>
internal static class CssXmlIdentity
{
    private sealed class Metadata(CssExpandedName name)
    {
        internal CssExpandedName Name=name;
        internal Dictionary<CssExpandedName,CssXmlAttribute>? Attributes;
    }
    private static readonly ConditionalWeakTable<DependencyObject,Metadata> s_nodes=new();
    private static readonly ConditionalWeakTable<Type,Metadata> s_defaults=new();

    internal static CssExpandedName Name(DependencyObject target)
        => s_nodes.TryGetValue(target,out var metadata) ? metadata.Name : Default(target.GetType());

    private static CssExpandedName Default(Type type) => s_defaults.GetValue(type,static type=>new(new(
        type.Assembly==typeof(FrameworkElement).Assembly ? JalxamlNamespaces.Presentation
            : "clr-namespace:"+(type.Namespace??string.Empty)+";assembly="+type.Assembly.GetName().Name,
        type.Name))).Name;

    internal static void Set(object instance,string namespaceUri,string localName,bool resetAttributes=false)
    {
        if(instance is not DependencyObject target || target is not (FrameworkElement or FrameworkContentElement)) return;
        var name=new CssExpandedName(namespaceUri,localName);
        var metadata=s_nodes.GetValue(target,key=>new(Default(key.GetType())));
        var cleared=resetAttributes && metadata.Attributes is {Count:>0};
        if(resetAttributes) metadata.Attributes=null;
        if(metadata.Name==name && !cleared) return;
        metadata.Name=name;
        if(CssEngine.IsActive) CssEngine.InvalidateSelectorDependents(CssNode.Get(target));
    }

    internal static void RecordAttribute(object instance,string namespaceUri,string localName,string value,Type? ownerType=null)
    {
        if(instance is not DependencyObject target || target is not (FrameworkElement or FrameworkContentElement)) return;
        var metadata=s_nodes.GetValue(target,key=>new(Default(key.GetType())));
        var name=new CssExpandedName(namespaceUri,localName);
        var attribute=new CssXmlAttribute(value,ownerType);
        var attributes=metadata.Attributes??=[];
        if(attributes.TryGetValue(name,out var previous) && previous==attribute) return;
        attributes[name]=attribute;
        if(CssEngine.IsActive) CssEngine.InvalidateSelectorDependents(CssNode.Get(target));
    }

    internal static bool HasQualifiedAttribute(DependencyObject target,string localName)
        => s_nodes.TryGetValue(target,out var metadata) && metadata.Attributes is { } attributes &&
            !attributes.ContainsKey(new(string.Empty,localName)) && attributes.Keys.Any(name=>name.LocalName==localName && name.NamespaceUri.Length>0);

    internal static IEnumerable<(CssExpandedName Name,string? Value,string? Property)> Attributes(DependencyObject target,string? namespaceUri,string localName)
    {
        if(!s_nodes.TryGetValue(target,out var metadata) || metadata.Attributes is not { } attributes) yield break;
        foreach(var pair in attributes)
        {
            if(pair.Key.LocalName!=localName || namespaceUri is not null && pair.Key.NamespaceUri!=namespaceUri) continue;
            var name=localName;
            var owner=pair.Value.OwnerType??target.GetType();
            if(localName.IndexOf('.') is var dot && dot>=0)
            {
                name=localName[(dot+1)..];
                if(pair.Value.OwnerType is null) owner=TypeResolver.ResolveTypeByName?.Invoke(localName[..dot])??owner;
            }
            var property=CssDependencyPropertyLookup.Find(owner,name);
            if(property is not null)
            {
                var value=target.HasLocalValue(property) ? Convert.ToString(target.GetValue(property),CultureInfo.InvariantCulture) : null;
                yield return(pair.Key,value,property.Name);
            }
            else yield return(pair.Key,pair.Value.Value,null);
        }
    }
}
