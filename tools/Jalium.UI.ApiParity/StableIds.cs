using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal static class StableIds
{
    public static string ApiId(ISymbol symbol)
    {
        string declarationId = DocumentationCommentId.CreateDeclarationId(symbol)
            ?? FallbackDeclarationId(symbol);
        return $"{symbol.ContainingAssembly.Name}|{declarationId}";
    }

    public static string GapId(string apiId, string facet)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"gap-id-v1\0{apiId}\0{facet}"));
        return $"gap:v1:{Convert.ToHexString(bytes).ToLowerInvariant()[..24]}";
    }

    public static string UnresolvedApiId(string category, params string[] identityParts)
    {
        string payload = string.Join("\0", identityParts);
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"legacy-api-v1\0{category}\0{payload}"));
        return $"unresolved:v1:{Convert.ToHexString(bytes).ToLowerInvariant()[..24]}";
    }

    private static string FallbackDeclarationId(ISymbol symbol)
    {
        string kind = symbol switch
        {
            INamedTypeSymbol => "T",
            IMethodSymbol => "M",
            IPropertySymbol => "P",
            IEventSymbol => "E",
            IFieldSymbol => "F",
            _ => symbol.Kind.ToString(),
        };

        return $"{kind}:{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
    }
}
