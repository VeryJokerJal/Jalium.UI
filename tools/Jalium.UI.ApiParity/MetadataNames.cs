using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal static class MetadataNames
{
    public static string Join(string namespaceName, string metadataTypeName)
        => string.IsNullOrEmpty(namespaceName)
            ? metadataTypeName
            : namespaceName + "." + metadataTypeName;

    public static string FullName(INamedTypeSymbol type)
    {
        string typeName = NestedMetadataName(type);
        string namespaceName = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        return Join(namespaceName, typeName);
    }

    public static string NestedMetadataName(INamedTypeSymbol type)
    {
        return type.ContainingType is null
            ? type.MetadataName
            : NestedMetadataName(type.ContainingType) + "+" + type.MetadataName;
    }

    public static string FromLegacyTypeName(string typeName)
    {
        string trimmed = typeName.Trim();
        int genericStart = trimmed.IndexOf('<');
        if (genericStart < 0)
        {
            return trimmed;
        }

        int genericEnd = trimmed.LastIndexOf('>');
        if (genericEnd <= genericStart)
        {
            throw new FormatException($"Malformed generic type name '{typeName}'.");
        }

        string arguments = trimmed[(genericStart + 1)..genericEnd];
        int depth = 0;
        int arity = 1;
        foreach (char ch in arguments)
        {
            switch (ch)
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arity++;
                    break;
            }
        }

        return trimmed[..genericStart] + "`" + arity;
    }
}
