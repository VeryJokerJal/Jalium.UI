using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Resolves a JALXAML element's <c>(localName, namespaceUri)</c> pair to a fully-qualified
/// CLR type symbol from the current <see cref="Compilation"/>. Used by the source generator
/// to emit <c>typeof(T)</c> references for AOT type pinning — without these the trimmer
/// would remove constructors that <see cref="System.Activator.CreateInstance(System.Type)"/>
/// needs at runtime, surfacing as <see cref="System.MissingMethodException"/> when the
/// JALXAML reader instantiates the element.
/// </summary>
/// <remarks>
/// <para>
/// The resolver mirrors the runtime <c>XmlnsDefinitionRegistry</c> + <c>XmlnsCompatibleWith</c>
/// behaviour: it scans every referenced assembly (and the source assembly itself) for
/// <c>XmlnsDefinitionAttribute</c> / <c>XmlnsCompatibleWithAttribute</c> declarations and
/// builds <c>xmlNs → List(clrNamespace, assemblySimpleName)</c> + <c>xmlNs → xmlNs</c>
/// redirect maps. Lookups follow redirects then enumerate every candidate
/// <c>(clrNamespace, assembly)</c> pair, using <see cref="Compilation.GetTypeByMetadataName"/>
/// so unambiguous metadata wins over name-only guesses.
/// </para>
/// <para>
/// Element references that the resolver cannot pin (third-party types in assemblies that
/// don't declare <c>XmlnsDefinition</c>, dynamically-loaded plugins, parse fallbacks) are
/// returned as <c>null</c>; the generator silently skips them. Those types still resolve
/// at runtime via the reflection fallback path in <c>XamlReader.ResolveType</c>; AOT users
/// must declare <c>XmlnsDefinition</c> on their assemblies (or pin types manually) for the
/// trimmer to keep them.
/// </para>
/// </remarks>
internal sealed class XmlnsTypeResolver
{
    private const string XmlnsDefinitionAttributeName = "Jalium.UI.Markup.XmlnsDefinitionAttribute";
    private const string XmlnsCompatibleWithAttributeName = "Jalium.UI.Markup.XmlnsCompatibleWithAttribute";
    private const string ClrNamespacePrefix = "clr-namespace:";
    private const string XamlLanguageNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly HashSet<string> LegacyManagedFacadeAssemblyNames = new(StringComparer.Ordinal)
    {
        "Jalium.UI.Core",
        "Jalium.UI.Media",
        "Jalium.UI.Input",
        "Jalium.UI.Interop",
        "Jalium.UI.Controls",
    };

    /// <summary>
    /// XAML language built-in element types. <c>&lt;x:String&gt;</c> resolves to
    /// <see cref="System.String"/>, <c>&lt;x:Int32&gt;</c> to <see cref="System.Int32"/>, etc.
    /// These are XAML-spec primitives, not assembly-declared mappings, so the resolver
    /// short-circuits them before the regular <c>_mappings</c> lookup.
    /// </summary>
    private static readonly Dictionary<string, string> XamlLanguageBuiltInTypes = new(StringComparer.Ordinal)
    {
        { "Object",  "System.Object" },
        { "String",  "System.String" },
        { "Boolean", "System.Boolean" },
        { "Char",    "System.Char" },
        { "Byte",    "System.Byte" },
        { "Int16",   "System.Int16" },
        { "Int32",   "System.Int32" },
        { "Int64",   "System.Int64" },
        { "Single",  "System.Single" },
        { "Double",  "System.Double" },
        { "Decimal", "System.Decimal" },
        { "TimeSpan","System.TimeSpan" },
        { "Uri",     "System.Uri" },
    };

    private readonly Compilation _compilation;

    /// <summary>
    /// XML namespace URI → list of (CLR namespace, optional preferred assembly simple name).
    /// Multiple <c>XmlnsDefinition</c> declarations on the same XML namespace stack here in
    /// declaration order — the resolver tries every candidate against the compilation.
    /// </summary>
    private readonly Dictionary<string, List<XmlnsMapping>> _mappings;

    /// <summary>
    /// Compatibility redirect chain: <c>oldXmlNs → newXmlNs</c>. Resolved transitively in
    /// <see cref="ResolveToGlobalQualifiedName"/> so legacy URIs (e.g. the 2024 Jalium URI)
    /// fold into the canonical Presentation URI before mapping lookup.
    /// </summary>
    private readonly Dictionary<string, string> _compatRedirects;

    private XmlnsTypeResolver(
        Compilation compilation,
        Dictionary<string, List<XmlnsMapping>> mappings,
        Dictionary<string, string> compatRedirects)
    {
        _compilation = compilation;
        _mappings = mappings;
        _compatRedirects = compatRedirects;
    }

    public static XmlnsTypeResolver FromCompilation(Compilation compilation)
    {
        var mappings = new Dictionary<string, List<XmlnsMapping>>(StringComparer.Ordinal);
        var compatRedirects = new Dictionary<string, string>(StringComparer.Ordinal);

        // The source assembly contributes its own xmlns declarations (e.g. user assembly
        // declaring [assembly: XmlnsDefinition(...)] for its own types).
        ScanAssembly(compilation.Assembly, mappings, compatRedirects);

        // Every referenced assembly that exposes the metadata symbol gets scanned. Reference
        // assemblies that the compilation can't resolve (missing reference) are silently
        // skipped — the JALXAML compile would already fail elsewhere if they're truly needed.
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assemblySymbol)
            {
                ScanAssembly(assemblySymbol, mappings, compatRedirects);
            }
        }

        return new XmlnsTypeResolver(compilation, mappings, compatRedirects);
    }

    private static void ScanAssembly(
        IAssemblySymbol assembly,
        Dictionary<string, List<XmlnsMapping>> mappings,
        Dictionary<string, string> compatRedirects)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            var attrFullName = attribute.AttributeClass?.ToDisplayString();
            if (attrFullName == null)
                continue;

            if (string.Equals(attrFullName, XmlnsDefinitionAttributeName, StringComparison.Ordinal))
            {
                if (attribute.ConstructorArguments.Length < 2)
                    continue;

                var xmlNs = attribute.ConstructorArguments[0].Value as string;
                var clrNs = attribute.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(xmlNs) || string.IsNullOrEmpty(clrNs))
                    continue;

                string? preferredAssembly = null;
                foreach (var named in attribute.NamedArguments)
                {
                    if (string.Equals(named.Key, "AssemblyName", StringComparison.Ordinal))
                    {
                        preferredAssembly = named.Value.Value as string;
                        break;
                    }
                }

                preferredAssembly ??= assembly.Identity.Name;

                if (!mappings.TryGetValue(xmlNs!, out var list))
                {
                    list = new List<XmlnsMapping>();
                    mappings[xmlNs!] = list;
                }
                list.Add(new XmlnsMapping(clrNs!, preferredAssembly));
            }
            else if (string.Equals(attrFullName, XmlnsCompatibleWithAttributeName, StringComparison.Ordinal))
            {
                if (attribute.ConstructorArguments.Length < 2)
                    continue;

                var oldNs = attribute.ConstructorArguments[0].Value as string;
                var newNs = attribute.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(oldNs) || string.IsNullOrEmpty(newNs))
                    continue;

                // Last-writer wins is fine — XmlnsCompatibleWith is unique per (old, new) pair
                // by convention; conflicting declarations would be a bug regardless.
                compatRedirects[oldNs!] = newNs!;
            }
        }
    }

    /// <summary>
    /// Resolves a JALXAML element to its fully-qualified CLR type symbol expressed as
    /// <c>global::Foo.Bar.Baz</c>. Returns <c>null</c> when the element cannot be unambiguously
    /// resolved against the current compilation (unknown XML namespace, missing CLR type,
    /// third-party assembly without <c>XmlnsDefinition</c>, etc.).
    /// </summary>
    public string? ResolveToGlobalQualifiedName(string elementName, string namespaceUri)
    {
        if (string.IsNullOrEmpty(elementName))
            return null;

        // 1) clr-namespace:Foo;assembly=Bar — explicit CLR namespace reference.
        if (!string.IsNullOrEmpty(namespaceUri) &&
            namespaceUri.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal))
        {
            return ResolveClrNamespace(elementName, namespaceUri);
        }

        // 1b) XAML-language built-in primitive types (<x:String>, <x:Int32> etc.). These are
        //     spec-mandated aliases for System.* types; no assembly carries an XmlnsDefinition
        //     for them so the regular mapping path won't see them.
        if (string.Equals(namespaceUri, XamlLanguageNamespace, StringComparison.Ordinal) &&
            XamlLanguageBuiltInTypes.TryGetValue(elementName, out var builtIn))
        {
            return $"global::{builtIn}";
        }

        // 2) Follow XmlnsCompatibleWith redirect chain (with cycle guard) so legacy URIs
        //    (e.g. http://schemas.jalium.ui/2024) fold into the canonical Presentation URI
        //    where the actual XmlnsDefinition declarations live.
        var resolvedXmlNs = FollowRedirects(namespaceUri ?? string.Empty);

        if (!_mappings.TryGetValue(resolvedXmlNs, out var candidateMappings))
        {
            return null;
        }

        // Two-pass walk. Pass 1 honours the assembly hint when the same (clrNs, name) pair
        // resolves in multiple assemblies — without this the SG would arbitrarily pick the
        // first assembly the symbol table returns (which depends on file enumeration order).
        // Pass 2 accepts ANY hit when no preferred-assembly candidate matched — the runtime
        // type registry behaves the same way (XmlnsDefinitionRegistry.AddXmlnsDefinition
        // records the declaring assembly purely as a tie-breaker; XamlReader still honours
        // a name found in a different assembly when the declaring assembly doesn't carry
        // the type). The framework relies on this behaviour heavily: the Media types live
        // in Jalium.UI.Core but their xmlns mapping is declared on the Jalium.UI.Media
        // assembly facade.
        INamedTypeSymbol? firstCandidate = null;
        foreach (var mapping in candidateMappings)
        {
            var fullName = $"{mapping.ClrNamespace}.{elementName}";
            var typeSymbol = _compilation.GetTypeByMetadataName(fullName);
            if (typeSymbol == null)
                continue;

            firstCandidate ??= typeSymbol;

            if (string.IsNullOrEmpty(mapping.PreferredAssembly) ||
                string.Equals(typeSymbol.ContainingAssembly?.Identity.Name, mapping.PreferredAssembly, StringComparison.Ordinal))
            {
                return ToGlobalDisplayString(typeSymbol);
            }
        }

        return firstCandidate != null ? ToGlobalDisplayString(firstCandidate) : null;
    }

    private string? ResolveClrNamespace(string elementName, string namespaceUri)
    {
        var raw = namespaceUri.Substring(ClrNamespacePrefix.Length);
        var clrNs = raw;
        string? assemblyName = null;

        var semicolonIndex = raw.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            clrNs = raw.Substring(0, semicolonIndex);
            var remainder = raw.Substring(semicolonIndex + 1);
            const string AssemblyEquals = "assembly=";
            if (remainder.StartsWith(AssemblyEquals, StringComparison.Ordinal))
            {
                assemblyName = remainder.Substring(AssemblyEquals.Length).Trim();
            }
        }

        if (string.IsNullOrEmpty(clrNs))
            return null;

        var fullName = $"{clrNs}.{elementName}";
        var typeSymbol = _compilation.GetTypeByMetadataName(fullName);
        if (typeSymbol == null)
            return null;

        if (!string.IsNullOrEmpty(assemblyName) &&
            !string.Equals(typeSymbol.ContainingAssembly?.Identity.Name, assemblyName, StringComparison.Ordinal) &&
            !(string.Equals(typeSymbol.ContainingAssembly?.Identity.Name, "Jalium.UI.Managed", StringComparison.Ordinal) &&
              LegacyManagedFacadeAssemblyNames.Contains(assemblyName!)))
        {
            return null;
        }

        return ToGlobalDisplayString(typeSymbol);
    }

    private string FollowRedirects(string xmlNs)
    {
        var current = xmlNs;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current };

        while (_compatRedirects.TryGetValue(current, out var next))
        {
            if (!seen.Add(next))
                break;
            current = next;
        }

        return current;
    }

    private static string ToGlobalDisplayString(INamedTypeSymbol type)
    {
        // SymbolDisplayFormat.FullyQualifiedFormat already prefixes "global::" and uses
        // generic type names with arity-aware syntax — exactly what we need for typeof().
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private readonly struct XmlnsMapping
    {
        public XmlnsMapping(string clrNamespace, string? preferredAssembly)
        {
            ClrNamespace = clrNamespace;
            PreferredAssembly = preferredAssembly;
        }

        public string ClrNamespace { get; }
        public string? PreferredAssembly { get; }
    }
}
