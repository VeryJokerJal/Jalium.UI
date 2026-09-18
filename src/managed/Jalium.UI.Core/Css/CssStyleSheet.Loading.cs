using System.Net.Http;

namespace Jalium.UI.Styling;

/// <summary>Provides stylesheet bytes and the final URI used to resolve nested resources.</summary>
public interface ICssResourceResolver
{
    ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default);
}

/// <summary>The loader owns and disposes Content after reading it.</summary>
public sealed record CssResource(Uri Uri, Stream Content);

internal sealed record CssImport(string Reference, int Line, int Offset = 0, string? LayerName = null,
    bool AnonymousLayer = false, CssCondition? Supports = null, CssCondition? Media = null)
{
    internal CssCondition? Condition => CssCondition.And(Supports,Media);
    public static bool TryParse(ReadOnlySpan<char> text, int line, out CssImport? import,CssNamespaceContext? namespaces = null)
    {
        import = null;
        text = text.Trim();
        if (text.IsEmpty || text[^1] != ';') return false;
        var reader = new CssTokenReader(text[..^1]);
        string reference;
        if (reader.TryReadString(out var quoted)) reference = quoted;
        else if (reader.TryReadFunction(out var name, out var args) && name.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            if (args.TryReadString(out quoted)) { if (!args.AtEnd) return false; reference = quoted; }
            else
            {
                var textValue = args.Remaining.Trim();
                var decoded = new System.Text.StringBuilder();
                for(var i=0;i<textValue.Length;)
                {
                    if (textValue[i]=='\\')
                    { if (!CssSyntax.ReadEscape(textValue,ref i,out var escape)) return false; decoded.Append(escape); continue; }
                    var c=textValue[i++];
                    if (char.IsWhiteSpace(c) || c<0x20 || c is '(' or ')' or '\'' or '"') return false;
                    decoded.Append(c);
                }
                reference=decoded.ToString();
            }
        }
        else return false;
        string? layer=null; var anonymous=false;
        var probe=reader;
        if (probe.TryReadIdent(out var keyword) && keyword.Equals("layer",StringComparison.OrdinalIgnoreCase))
        { layer=CssLayerOrder.AnonymousName(); anonymous=true; reader=probe; }
        else
        {
            probe=reader;
            if (probe.TryReadFunction(out var function,out var arguments) && function.Equals("layer",StringComparison.OrdinalIgnoreCase))
            {
                if (arguments.AtEnd) {layer=CssLayerOrder.AnonymousName(); anonymous=true;}
                else if (CssLayerOrder.NormalizeName(arguments.Remaining.ToString()) is { } parsed) layer=parsed;
                else return false;
                reader=probe;
            }
        }
        CssCondition? supports=null;
        probe=reader;
        if (probe.TryReadFunction(out var supportName,out var supportArguments) && supportName.Equals("supports",StringComparison.OrdinalIgnoreCase))
        {
            var query=supportArguments.Remaining.ToString();
            if (CssSupportsQuery.Parse(query,allowDeclaration:true,namespaces:namespaces) is null) return false;
            supports=new("supports",query,Namespaces:namespaces); reader=probe;
        }
        var media=reader.AtEnd ? null : new CssCondition("media",reader.Remaining.ToString());
        import = new(reference, line, LayerName:layer,AnonymousLayer:anonymous,Supports:supports,Media:media);
        return true;
    }
}

public sealed partial class CssStyleSheet
{
    private sealed record LoadedCssResource(CssStyleSheet Sheet, Uri Uri);
    private static readonly HttpClient s_http = new();
    private static readonly ICssResourceResolver s_defaultResolver = new DefaultResourceResolver();

    public static Task<CssStyleSheet> LoadAsync(Uri uri, CancellationToken cancellationToken = default)
        => LoadAsync(uri, s_defaultResolver, cancellationToken);

    public static async Task<CssStyleSheet> LoadAsync(Uri uri, ICssResourceResolver resolver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(resolver);
        var active = new HashSet<Uri>();
        // Cache fetched/parsed resources, not expanded imports: expansion depends on the
        // ancestor chain and must create independent anonymous layers at each occurrence.
        var cache = new Dictionary<Uri, LoadedCssResource>();

        async Task<LoadedCssResource> Fetch(Uri reference)
        {
            if (cache.TryGetValue(reference,out var cached)) return cached;
            // Explicit resolvers remain authoritative, including hot reload.
            if (ReferenceEquals(resolver, s_defaultResolver) && CssCompiledStyleRegistry.TryCreateResource(reference, out var compiled))
            {
                var generated = new LoadedCssResource(compiled, reference);
                cache[reference] = generated;
                return generated;
            }
            var resource=await resolver.ResolveAsync(reference,cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(resource.Content);
            string text;
            using(resource.Content)
            using(var reader=new StreamReader(resource.Content))
                text=await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var parsed=new LoadedCssResource(Parse(text,resource.Uri.ToString(),resource.Uri),resource.Uri);
            cache[reference]=parsed;
            if (!cache.ContainsKey(resource.Uri)) cache[resource.Uri]=parsed;
            return parsed;
        }

        async Task<CssStyleSheet> Load(Uri reference, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 64 || !active.Add(reference))
                return new CssStyleSheet([], [new CssParseDiagnostic(CssDiagnosticSeverity.Warning,
                    "cyclic or excessively nested @import was skipped", 1)], reference.ToString());
            try
            {
                var resource=await Fetch(reference).ConfigureAwait(false);
                var sheet=resource.Sheet;
                var resourceUri=resource.Uri;
                var aliasAdded=false;
                if (!resourceUri.Equals(reference))
                {
                    if (!active.Add(resourceUri)) return new CssStyleSheet([], [new(CssDiagnosticSeverity.Warning,"cyclic redirected @import was skipped",1)],resourceUri.ToString());
                    aliasAdded=true;
                }
                try
                {
                    var anonymousNames=new Dictionary<string,string>(StringComparer.Ordinal);
                    foreach(var layer in sheet.Layers.Where(layer=>layer.Anonymous)) anonymousNames[layer.Name.Split('.')[^1]]=CssLayerOrder.AnonymousName();
                    foreach(var import in sheet.Imports.Where(import=>import.AnonymousLayer)) anonymousNames[import.LayerName!]=CssLayerOrder.AnonymousName();
                    string? LocalName(string? name) => name is null ? null : string.Join('.',name.Split('.').Select(part=>anonymousNames.GetValueOrDefault(part,part)));
                    var rules = new List<CssRule>();
                    var layers = sheet.Layers.Select(layer => (Order: layer.Offset, Layer: layer with {Name=LocalName(layer.Name)!})).ToList();
                    var properties = sheet.Properties.Select(property => (Order: property.Offset, Property: property with {LayerName=LocalName(property.LayerName)})).ToList();
                    var diagnostics = sheet.Diagnostics.Where(d => !d.Message.StartsWith("@import is deferred", StringComparison.Ordinal)).ToList();
                    foreach (var import in sheet.Imports)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var wrapper=LocalName(import.LayerName);
                        var condition=import.Condition;
                        if (wrapper is not null) layers.Add((import.Offset,new(wrapper,import.Offset,condition,import.AnonymousLayer)));
                        if (import.Supports is not null && !import.Supports.Supports()) continue;
                        if (import.Reference.Length==0)
                        {
                            diagnostics.Add(new(CssDiagnosticSeverity.Warning,"empty @import URL was not loaded",import.Line));
                            continue;
                        }
                        try
                        {
                            var imported = await Load(ResolveReference(resourceUri, import.Reference), depth + 1).ConfigureAwait(false);
                            string? ImportedName(string? name) => wrapper is null ? name : name is null ? wrapper : wrapper+"."+name;
                            foreach(var rule in imported.Rules)
                                rules.Add(new CssRule {Selectors=rule.Selectors,Declarations=rule.Declarations,BaseUri=rule.BaseUri,Scope=rule.Scope,
                                    LayerName=ImportedName(rule.LayerName),Condition=CssCondition.And(condition,rule.Condition),RuleIndex=rules.Count});
                            layers.AddRange(imported.Layers.Select(layer => (import.Offset,layer with {Name=ImportedName(layer.Name)!,Condition=CssCondition.And(condition,layer.Condition)})));
                            properties.AddRange(imported.Properties.Select(property => (import.Offset,property with {LayerName=ImportedName(property.LayerName),Condition=CssCondition.And(condition,property.Condition)})));
                            diagnostics.AddRange(imported.Diagnostics);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception exception) when (exception is IOException or HttpRequestException or UriFormatException or InvalidOperationException)
                        {
                            diagnostics.Add(new CssParseDiagnostic(CssDiagnosticSeverity.Warning,
                                $"@import '{import.Reference}' could not be loaded: {exception.Message}", import.Line));
                        }
                    }
                    foreach(var rule in sheet.Rules)
                        rules.Add(new CssRule {Selectors=rule.Selectors,Declarations=rule.Declarations,BaseUri=rule.BaseUri,Scope=rule.Scope,
                            LayerName=LocalName(rule.LayerName),Condition=rule.Condition,RuleIndex=rules.Count});
                    // Clone rule order, retaining each imported sheet's resource base. Cached
                    // sheet instances may be imported more than once at different positions.
                    var flattened = rules.Select((rule, index) => new CssRule
                    {
                        Selectors = rule.Selectors, Declarations = rule.Declarations,
                        BaseUri = rule.BaseUri, Condition = rule.Condition, LayerName = rule.LayerName, Scope = rule.Scope, RuleIndex = index,
                    }).ToArray();
                    var result = new CssStyleSheet(flattened, diagnostics.ToArray(), resourceUri.ToString(),
                        layers: layers.OrderBy(entry => entry.Order).Select((entry, index) => entry.Layer with { Offset = index }).ToArray(),
                        properties: properties.OrderBy(entry => entry.Order).Select((entry, index) => entry.Property with { Offset = index }).ToArray());
                    return result;
                }
                finally {if(aliasAdded) active.Remove(resourceUri);}
            }
            finally { active.Remove(reference); }
        }
        return await Load(uri, 0).ConfigureAwait(false);
    }

    internal static Uri ResolveReference(Uri baseUri, string reference)
    {
        if (System.Uri.TryCreate(reference, UriKind.Absolute, out var absolute)) return absolute;
        if (baseUri.IsAbsoluteUri) return new Uri(baseUri, reference);
        // A synthetic origin only performs URI normalization; it is never fetched.
        var origin = new Uri("https://jalium.invalid/");
        var resolved = new Uri(new Uri(origin, baseUri), reference);
        return new Uri(resolved.PathAndQuery + resolved.Fragment, UriKind.Relative);
    }

    private sealed class DefaultResourceResolver : ICssResourceResolver
    {
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (uri.IsAbsoluteUri && uri.Scheme is "http" or "https")
            {
                using var response = await s_http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return new(response.RequestMessage?.RequestUri ?? uri, new MemoryStream(bytes, writable: false));
            }
            if (UriStreamResolver?.Invoke(uri) is { } resource) return new(uri, resource);
            if (uri.IsAbsoluteUri && uri.IsFile)
                return new(uri, new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true));
            throw new IOException($"CSS resource '{uri}' could not be resolved");
        }
    }
}
