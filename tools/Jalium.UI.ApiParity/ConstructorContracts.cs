using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal sealed record ConstructorContract(
    string IdentityKey,
    string SourceKey,
    Accessibility Accessibility)
{
    public string FullKey => $"{Accessibility}|{SourceKey}";

    public static ConstructorContract Create(
        IMethodSymbol constructor,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string identity = string.Join(
            ";",
            constructor.Parameters.Select(parameter => ParameterIdentity(
                parameter,
                mapper,
                mappedAssemblyNames)));
        string source = string.Join(
            ";",
            constructor.Parameters.Select(parameter => ParameterSourceContract(
                parameter,
                mapper,
                mappedAssemblyNames)));
        return new ConstructorContract(identity, source, constructor.DeclaredAccessibility);
    }

    private static string ParameterIdentity(
        IParameterSymbol parameter,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        return $"{parameter.RefKind}|{TypeContractKey.Create(parameter.Type, mapper, mappedAssemblyNames)}";
    }

    private static string ParameterSourceContract(
        IParameterSymbol parameter,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string defaultValue = parameter.HasExplicitDefaultValue
            ? ConstantKey(parameter.ExplicitDefaultValue)
            : "-";
        return string.Join(
            "|",
            parameter.RefKind,
            parameter.ScopedKind,
            TypeContractKey.Create(parameter.Type, mapper, mappedAssemblyNames),
            parameter.Name,
            parameter.IsParams,
            parameter.IsOptional,
            defaultValue);
    }

    private static string ConstantKey(object? value)
    {
        return value switch
        {
            null => "null",
            double number => "double:" + BitConverter.DoubleToInt64Bits(number).ToString("X16", CultureInfo.InvariantCulture),
            float number => "float:" + BitConverter.SingleToInt32Bits(number).ToString("X8", CultureInfo.InvariantCulture),
            char character => "char:" + ((int)character).ToString(CultureInfo.InvariantCulture),
            string text => "string:" + text,
            IFormattable formattable => value.GetType().FullName + ":" + formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.GetType().FullName + ":" + value,
        };
    }
}

internal static class TypeContractKey
{
    public static string Create(
        ITypeSymbol type,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        return type switch
        {
            IArrayTypeSymbol array =>
                $"{Create(array.ElementType, mapper, mappedAssemblyNames)}[{new string(',', array.Rank - 1)}]",
            IPointerTypeSymbol pointer =>
                Create(pointer.PointedAtType, mapper, mappedAssemblyNames) + "*",
            ITypeParameterSymbol parameter =>
                (parameter.TypeParameterKind == TypeParameterKind.Method ? "!!" : "!") + parameter.Ordinal,
            IFunctionPointerTypeSymbol functionPointer =>
                "fnptr:" + functionPointer.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            INamedTypeSymbol named => NamedTypeKey(named, mapper, mappedAssemblyNames),
            IDynamicTypeSymbol => "System.Object",
            _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        };
    }

    private static string NamedTypeKey(
        INamedTypeSymbol type,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string prefix;
        if (type.ContainingType is not null)
        {
            prefix = NamedTypeKey(type.ContainingType, mapper, mappedAssemblyNames) + "+" + type.MetadataName;
        }
        else
        {
            string namespaceName = type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : type.ContainingNamespace.ToDisplayString();
            if (mappedAssemblyNames.Contains(type.ContainingAssembly.Name))
            {
                prefix = mapper.MapType(namespaceName, type.MetadataName);
            }
            else
            {
                prefix = MetadataNames.Join(namespaceName, type.MetadataName);
            }
        }

        if (type.TypeArguments.Length == 0)
        {
            return prefix;
        }

        string arguments = string.Join(
            ",",
            type.TypeArguments.Select(argument => Create(argument, mapper, mappedAssemblyNames)));
        return $"{prefix}<{arguments}>";
    }
}

internal static partial class LegacySignatureFormatter
{
    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string Constructor(IMethodSymbol constructor)
    {
        bool requiresUnsafe = constructor.Parameters.Any(static parameter => ContainsPointer(parameter.Type));
        string parameters = string.Join(
            ", ",
            constructor.Parameters.Select(FormatParameter));
        string unsafeModifier = requiresUnsafe ? " unsafe" : string.Empty;
        return Normalize(
            $"{ApiVisibility.Display(constructor.DeclaredAccessibility)}{unsafeModifier} "
            + $"{constructor.ContainingType.Name}({parameters})");
    }

    public static bool MatchesConstructor(IMethodSymbol constructor, string legacySignature)
    {
        string expected = Normalize(legacySignature);
        string formatted = Constructor(constructor);
        if (formatted.Equals(expected, StringComparison.Ordinal))
        {
            return true;
        }

        string declaringNamespace = constructor.ContainingType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : constructor.ContainingType.ContainingNamespace.ToDisplayString();
        if (declaringNamespace.Length == 0)
        {
            return false;
        }

        string sameNamespaceShortForm = Normalize(
            formatted.Replace(declaringNamespace + ".", string.Empty, StringComparison.Ordinal));
        return sameNamespaceShortForm.Equals(expected, StringComparison.Ordinal);
    }

    public static string Normalize(string signature)
    {
        string normalized = WhitespaceRegex().Replace(signature, " ").Trim();
        if (normalized.EndsWith(" { }", StringComparison.Ordinal))
        {
            normalized = normalized[..^4].TrimEnd();
        }

        normalized = normalized.Replace("nint", "System.IntPtr", StringComparison.Ordinal);
        normalized = normalized.Replace("nuint", "System.UIntPtr", StringComparison.Ordinal);
        return normalized;
    }

    private static string FormatParameter(IParameterSymbol parameter)
    {
        string paramsModifier = parameter.IsParams ? "params " : string.Empty;
        string refModifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => string.Empty,
        };
        return paramsModifier
            + refModifier
            + parameter.Type.ToDisplayString(TypeDisplayFormat)
            + " "
            + parameter.Name;
    }

    private static bool ContainsPointer(ITypeSymbol type)
    {
        return type switch
        {
            IPointerTypeSymbol => true,
            IArrayTypeSymbol array => ContainsPointer(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsPointer),
            _ => false,
        };
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
