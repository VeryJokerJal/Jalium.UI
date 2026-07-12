using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal enum ApiKind
{
    Type,
    Constructor,
    Property,
    Method,
    Event,
    Field,
    EnumValue,
}

internal sealed record ApiSymbolEntry(
    ApiKind Kind,
    string ApiId,
    ISymbol Symbol);

internal sealed class ApiSymbolIndex
{
    private readonly Dictionary<string, List<INamedTypeSymbol>> _typesByFullMetadataName;
    private readonly Dictionary<string, List<INamedTypeSymbol>> _typesBySimpleMetadataName;

    private ApiSymbolIndex(
        IReadOnlyList<ApiSymbolEntry> entries,
        Dictionary<string, List<INamedTypeSymbol>> typesByFullMetadataName,
        Dictionary<string, List<INamedTypeSymbol>> typesBySimpleMetadataName)
    {
        Entries = entries;
        _typesByFullMetadataName = typesByFullMetadataName;
        _typesBySimpleMetadataName = typesBySimpleMetadataName;
    }

    public IReadOnlyList<ApiSymbolEntry> Entries { get; }

    public static ApiSymbolIndex Build(IEnumerable<IAssemblySymbol> assemblies)
    {
        var entries = new List<ApiSymbolEntry>();
        var byFullName = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        var bySimpleName = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);

        foreach (IAssemblySymbol assembly in assemblies)
        {
            foreach (INamedTypeSymbol type in EnumerateTypes(assembly.GlobalNamespace))
            {
                Add(byFullName, MetadataNames.FullName(type), type);
                Add(bySimpleName, type.MetadataName, type);

                if (!ApiVisibility.IsExternallyVisible(type))
                {
                    continue;
                }

                entries.Add(new ApiSymbolEntry(ApiKind.Type, StableIds.ApiId(type), type));
                foreach (ISymbol member in type.GetMembers())
                {
                    if (member is INamedTypeSymbol || !ApiVisibility.IsContractMember(member))
                    {
                        continue;
                    }

                    ApiKind? kind = GetApiKind(member);
                    if (kind is not null)
                    {
                        entries.Add(new ApiSymbolEntry(kind.Value, StableIds.ApiId(member), member));
                    }
                }
            }
        }

        return new ApiSymbolIndex(entries, byFullName, bySimpleName);
    }

    public IReadOnlyList<INamedTypeSymbol> FindTypes(string fullMetadataName)
        => _typesByFullMetadataName.TryGetValue(fullMetadataName, out List<INamedTypeSymbol>? types)
            ? types
            : [];

    public IReadOnlyList<INamedTypeSymbol> FindTypesBySimpleName(string metadataName)
        => _typesBySimpleMetadataName.TryGetValue(metadataName, out List<INamedTypeSymbol>? types)
            ? types
            : [];

    private static void Add(
        IDictionary<string, List<INamedTypeSymbol>> index,
        string key,
        INamedTypeSymbol value)
    {
        if (!index.TryGetValue(key, out List<INamedTypeSymbol>? values))
        {
            values = [];
            index.Add(key, values);
        }

        values.Add(value);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (INamespaceOrTypeSymbol member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (INamedTypeSymbol nested in EnumerateTypes(childNamespace))
                {
                    yield return nested;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (INamedTypeSymbol nested in EnumerateNestedTypes(type))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol containingType)
    {
        foreach (INamedTypeSymbol nested in containingType.GetTypeMembers())
        {
            yield return nested;
            foreach (INamedTypeSymbol descendant in EnumerateNestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static ApiKind? GetApiKind(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } => ApiKind.Constructor,
            IMethodSymbol { MethodKind: MethodKind.StaticConstructor } => null,
            IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise } => null,
            IMethodSymbol => ApiKind.Method,
            IPropertySymbol => ApiKind.Property,
            IEventSymbol => ApiKind.Event,
            IFieldSymbol field when field.ContainingType.TypeKind == TypeKind.Enum && field.HasConstantValue
                => ApiKind.EnumValue,
            IFieldSymbol => ApiKind.Field,
            _ => null,
        };
    }
}

internal static class ApiVisibility
{
    public static bool IsExternallyVisible(INamedTypeSymbol type)
    {
        bool ownVisibility = IsPublicOrProtected(type.DeclaredAccessibility);
        if (!ownVisibility)
        {
            return false;
        }

        return type.ContainingType is null || IsExternallyVisible(type.ContainingType);
    }

    public static bool IsApiMember(ISymbol symbol)
        => IsPublicOrProtected(symbol.DeclaredAccessibility)
            && symbol.ContainingType is not null
            && IsExternallyVisible(symbol.ContainingType);

    public static bool IsContractMember(ISymbol symbol)
        => IsApiMember(symbol)
            || symbol.ContainingType is not null
                && IsExternallyVisible(symbol.ContainingType)
                && symbol switch
                {
                    IMethodSymbol method => method.ExplicitInterfaceImplementations.Length > 0,
                    IPropertySymbol property => property.ExplicitInterfaceImplementations.Length > 0,
                    IEventSymbol eventSymbol => eventSymbol.ExplicitInterfaceImplementations.Length > 0,
                    _ => false,
                };

    public static bool IsPublicOrProtected(Accessibility accessibility)
        => accessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;

    public static string Display(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            _ => accessibility.ToString(),
        };
    }
}
