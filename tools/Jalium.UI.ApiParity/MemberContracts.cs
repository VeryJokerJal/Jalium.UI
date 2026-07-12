using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal sealed record MethodContract(
    string IdentityKey,
    string FullKey,
    Accessibility Accessibility,
    bool IsStatic,
    string ReturnTypeKey,
    int ParameterCount)
{
    public static MethodContract Create(
        IMethodSymbol method,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string parameterIdentity = string.Join(
            ";",
            method.Parameters.Select(parameter => ContractKey.ParameterIdentity(
                parameter,
                mapper,
                mappedAssemblyNames)));
        string parameterSource = string.Join(
            ";",
            method.Parameters.Select(parameter => ContractKey.ParameterSource(
                parameter,
                mapper,
                mappedAssemblyNames)));
        string returnType = method.RefKind + "|"
            + TypeContractKey.Create(method.ReturnType, mapper, mappedAssemblyNames);
        string identity = $"{ContractKey.MemberIdentityName(method, mapper, mappedAssemblyNames)}|{method.Arity}|{parameterIdentity}";
        string constraints = string.Join(
            ";",
            method.TypeParameters.Select(parameter => ContractKey.TypeParameterConstraint(
                parameter,
                mapper,
                mappedAssemblyNames)));
        string full = string.Join(
            "#",
            identity,
            parameterSource,
            returnType,
            method.DeclaredAccessibility,
            method.IsStatic,
            method.IsAbstract,
            method.IsVirtual,
            method.IsOverride,
            method.IsSealed,
            constraints);
        return new MethodContract(
            identity,
            full,
            method.DeclaredAccessibility,
            method.IsStatic,
            returnType,
            method.Parameters.Length);
    }
}

internal sealed record PropertyContract(
    string IdentityKey,
    string FullKey,
    Accessibility Accessibility,
    bool IsStatic,
    string PropertyTypeKey)
{
    public static PropertyContract Create(
        IPropertySymbol property,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string parameterIdentity = string.Join(
            ";",
            property.Parameters.Select(parameter => ContractKey.ParameterIdentity(
                parameter,
                mapper,
                mappedAssemblyNames)));
        string parameterSource = string.Join(
            ";",
            property.Parameters.Select(parameter => ContractKey.ParameterSource(
                parameter,
                mapper,
                mappedAssemblyNames)));
        string typeKey = property.RefKind + "|"
            + TypeContractKey.Create(property.Type, mapper, mappedAssemblyNames);
        string accessors = ContractKey.Accessor(property.GetMethod)
            + "/"
            + ContractKey.Accessor(property.SetMethod);
        string identity = $"{ContractKey.MemberIdentityName(property, mapper, mappedAssemblyNames)}|{parameterIdentity}";
        string full = string.Join(
            "#",
            identity,
            parameterSource,
            typeKey,
            property.DeclaredAccessibility,
            property.IsStatic,
            property.IsAbstract,
            property.IsVirtual,
            property.IsOverride,
            property.IsSealed,
            accessors);
        return new PropertyContract(identity, full, property.DeclaredAccessibility, property.IsStatic, typeKey);
    }
}

internal sealed record EventContract(
    string IdentityKey,
    string FullKey,
    Accessibility Accessibility,
    bool IsStatic,
    string EventTypeKey)
{
    public static EventContract Create(
        IEventSymbol eventSymbol,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string typeKey = TypeContractKey.Create(eventSymbol.Type, mapper, mappedAssemblyNames);
        string identity = ContractKey.MemberIdentityName(eventSymbol, mapper, mappedAssemblyNames);
        string full = string.Join(
            "#",
            identity,
            typeKey,
            eventSymbol.DeclaredAccessibility,
            eventSymbol.IsStatic,
            eventSymbol.IsAbstract,
            eventSymbol.IsVirtual,
            eventSymbol.IsOverride,
            eventSymbol.IsSealed,
            ContractKey.Accessor(eventSymbol.AddMethod),
            ContractKey.Accessor(eventSymbol.RemoveMethod));
        return new EventContract(identity, full, eventSymbol.DeclaredAccessibility, eventSymbol.IsStatic, typeKey);
    }
}

internal sealed record FieldContract(
    string IdentityKey,
    string FullKey,
    Accessibility Accessibility,
    bool IsStatic,
    string FieldTypeKey,
    object? ConstantValue)
{
    public static FieldContract Create(
        IFieldSymbol field,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string typeKey = TypeContractKey.Create(field.Type, mapper, mappedAssemblyNames);
        string identity = field.MetadataName;
        string full = string.Join(
            "#",
            identity,
            typeKey,
            field.DeclaredAccessibility,
            field.IsStatic,
            field.IsConst,
            field.IsReadOnly,
            field.HasConstantValue,
            field.HasConstantValue ? ContractKey.Constant(field.ConstantValue) : "-");
        return new FieldContract(
            identity,
            full,
            field.DeclaredAccessibility,
            field.IsStatic,
            typeKey,
            field.ConstantValue);
    }
}

internal static class ContractKey
{
    public static string MemberIdentityName(
        ISymbol symbol,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        return symbol switch
        {
            IMethodSymbol method when method.ExplicitInterfaceImplementations.Length == 1
                => ExplicitMemberName(method.ExplicitInterfaceImplementations[0], mapper, mappedAssemblyNames),
            IPropertySymbol property when property.ExplicitInterfaceImplementations.Length == 1
                => ExplicitMemberName(property.ExplicitInterfaceImplementations[0], mapper, mappedAssemblyNames),
            IEventSymbol eventSymbol when eventSymbol.ExplicitInterfaceImplementations.Length == 1
                => ExplicitMemberName(eventSymbol.ExplicitInterfaceImplementations[0], mapper, mappedAssemblyNames),
            _ => symbol.MetadataName,
        };
    }

    public static string ParameterIdentity(
        IParameterSymbol parameter,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
        => $"{parameter.RefKind}|{TypeContractKey.Create(parameter.Type, mapper, mappedAssemblyNames)}";

    public static string ParameterSource(
        IParameterSymbol parameter,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
        => string.Join(
            "|",
            ParameterIdentity(parameter, mapper, mappedAssemblyNames),
            parameter.ScopedKind,
            parameter.Name,
            parameter.IsParams,
            parameter.IsOptional,
            parameter.HasExplicitDefaultValue ? Constant(parameter.ExplicitDefaultValue) : "-");

    public static string Accessor(IMethodSymbol? accessor)
        => accessor is null
            ? "-"
            : $"{accessor.DeclaredAccessibility}|{accessor.IsInitOnly}";

    public static string TypeParameterConstraint(
        ITypeParameterSymbol parameter,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
    {
        string types = string.Join(
            ",",
            parameter.ConstraintTypes.Select(type => TypeContractKey.Create(type, mapper, mappedAssemblyNames)));
        return string.Join(
            "|",
            parameter.Ordinal,
            parameter.HasReferenceTypeConstraint,
            parameter.ReferenceTypeConstraintNullableAnnotation,
            parameter.HasValueTypeConstraint,
            parameter.HasUnmanagedTypeConstraint,
            parameter.HasNotNullConstraint,
            parameter.HasConstructorConstraint,
            types);
    }

    public static string Constant(object? value)
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

    private static string ExplicitMemberName(
        ISymbol implementedMember,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblyNames)
        => TypeContractKey.Create(implementedMember.ContainingType, mapper, mappedAssemblyNames)
            + "."
            + implementedMember.MetadataName;
}

internal static partial class LegacyMemberFormatter
{
    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static bool Matches(IMethodSymbol method, string legacySignature)
        => MatchesWithSameNamespaceShortening(Method(method), legacySignature, method.ContainingNamespace);

    public static bool Matches(IPropertySymbol property, string legacySignature)
        => MatchesWithSameNamespaceShortening(Property(property), legacySignature, property.ContainingNamespace);

    public static bool Matches(IEventSymbol eventSymbol, string legacySignature)
        => MatchesWithSameNamespaceShortening(Event(eventSymbol), legacySignature, eventSymbol.ContainingNamespace);

    public static bool Matches(IFieldSymbol field, string legacySignature)
        => MatchesWithSameNamespaceShortening(Field(field), legacySignature, field.ContainingNamespace);

    public static string Method(IMethodSymbol method)
    {
        bool explicitImplementation = method.ExplicitInterfaceImplementations.Length > 0;
        bool interfaceMember = method.ContainingType.TypeKind == TypeKind.Interface;
        string accessibility = explicitImplementation || interfaceMember
            ? string.Empty
            : ApiVisibility.Display(method.DeclaredAccessibility) + " ";
        string staticModifier = method.IsStatic ? " static" : string.Empty;
        string parameters = string.Join(", ", method.Parameters.Select(FormatParameter));
        string returnType = ReturnType(method.RefKind, method.ReturnType);

        if (method.MethodKind == MethodKind.Conversion)
        {
            string conversion = method.MetadataName == "op_Implicit" ? "implicit" : "explicit";
            return NormalizeMember(
                $"{accessibility}{staticModifier} {conversion} operator {returnType}({parameters})");
        }

        if (method.MethodKind == MethodKind.UserDefinedOperator)
        {
            string token = OperatorToken(method.MetadataName);
            return NormalizeMember(
                $"{accessibility}{staticModifier} {returnType} operator {token}({parameters})");
        }

        string genericParameters = method.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", method.TypeParameters.Select(static parameter => parameter.Name)) + ">";
        return NormalizeMember(
            $"{accessibility}{staticModifier} {returnType} {method.Name}{genericParameters}({parameters})");
    }

    public static string Property(IPropertySymbol property)
    {
        bool explicitImplementation = property.ExplicitInterfaceImplementations.Length > 0;
        bool interfaceMember = property.ContainingType.TypeKind == TypeKind.Interface;
        string accessibility = explicitImplementation || interfaceMember
            ? string.Empty
            : ApiVisibility.Display(property.DeclaredAccessibility) + " ";
        string staticModifier = property.IsStatic ? " static" : string.Empty;
        string name = property.IsIndexer
            ? "this[" + string.Join(", ", property.Parameters.Select(FormatParameter)) + "]"
            : property.Name;
        var accessors = new StringBuilder();
        accessors.Append(" { ");
        if (property.GetMethod is not null)
        {
            AppendAccessor(accessors, property.GetMethod, property.DeclaredAccessibility, "get");
        }

        if (property.SetMethod is not null)
        {
            AppendAccessor(
                accessors,
                property.SetMethod,
                property.DeclaredAccessibility,
                property.SetMethod.IsInitOnly ? "init" : "set");
        }

        accessors.Append('}');
        return NormalizeMember(
            $"{accessibility}{staticModifier} {ReturnType(property.RefKind, property.Type)} {name}{accessors}");
    }

    public static string Event(IEventSymbol eventSymbol)
    {
        string staticModifier = eventSymbol.IsStatic ? " static" : string.Empty;
        string accessibility = eventSymbol.ExplicitInterfaceImplementations.Length > 0
            || eventSymbol.ContainingType.TypeKind == TypeKind.Interface
            ? string.Empty
            : ApiVisibility.Display(eventSymbol.DeclaredAccessibility) + " ";
        return NormalizeMember(
            $"{accessibility}{staticModifier} event "
            + $"{eventSymbol.Type.ToDisplayString(TypeDisplayFormat)} {eventSymbol.Name}");
    }

    public static string Field(IFieldSymbol field)
    {
        string modifiers = field.IsConst
            ? " const"
            : (field.IsStatic ? " static" : string.Empty) + (field.IsReadOnly ? " readonly" : string.Empty);
        string value = field.HasConstantValue
            ? " = " + DisplayConstant(field.ConstantValue)
            : string.Empty;
        return NormalizeMember(
            $"{ApiVisibility.Display(field.DeclaredAccessibility)}{modifiers} "
            + $"{field.Type.ToDisplayString(TypeDisplayFormat)} {field.Name}{value}");
    }

    public static string NormalizeMember(string signature)
    {
        string normalized = StripLeadingAttributes(signature.Trim());
        int lineComment = normalized.IndexOf("//", StringComparison.Ordinal);
        if (lineComment >= 0)
        {
            normalized = normalized[..lineComment];
        }

        int explicitAnnotation = normalized.IndexOf(" (explicit;", StringComparison.Ordinal);
        if (explicitAnnotation >= 0)
        {
            normalized = normalized[..explicitAnnotation];
        }

        int propertyEnd = normalized.LastIndexOf('}');
        if (propertyEnd >= 0)
        {
            normalized = normalized[..(propertyEnd + 1)];
        }
        else
        {
            int methodEnd = normalized.LastIndexOf(')');
            if (methodEnd >= 0
                && normalized[(methodEnd + 1)..].TrimStart().StartsWith('('))
            {
                normalized = normalized[..(methodEnd + 1)];
            }
        }

        normalized = LegacySignatureFormatter.Normalize(normalized);
        normalized = IgnorableModifierRegex().Replace(normalized, string.Empty);
        normalized = OpenParenRegex().Replace(normalized, "(");
        normalized = CloseParenRegex().Replace(normalized, ")");
        normalized = OpenBracketRegex().Replace(normalized, "[");
        normalized = CloseBracketRegex().Replace(normalized, "]");
        normalized = OpenBraceRegex().Replace(normalized, "{");
        normalized = CloseBraceRegex().Replace(normalized, "}");
        normalized = CommaRegex().Replace(normalized, ",");
        normalized = SemicolonRegex().Replace(normalized, ";");
        normalized = GenericOpenRegex().Replace(normalized, "<");
        normalized = GenericCloseRegex().Replace(normalized, ">");
        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();
        if (normalized.EndsWith(';'))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        return normalized;
    }

    private static bool MatchesWithSameNamespaceShortening(
        string formatted,
        string legacySignature,
        INamespaceSymbol namespaceSymbol)
    {
        string expected = NormalizeMember(legacySignature);
        if (formatted.Equals(expected, StringComparison.Ordinal))
        {
            return true;
        }

        if (namespaceSymbol.IsGlobalNamespace)
        {
            return false;
        }

        string prefix = namespaceSymbol.ToDisplayString() + ".";
        string shortened = NormalizeMember(formatted.Replace(prefix, string.Empty, StringComparison.Ordinal));
        return shortened.Equals(expected, StringComparison.Ordinal);
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

    private static string ReturnType(RefKind refKind, ITypeSymbol type)
    {
        string modifier = refKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.RefReadOnly => "ref readonly ",
            _ => string.Empty,
        };
        return modifier + type.ToDisplayString(TypeDisplayFormat);
    }

    private static void AppendAccessor(
        StringBuilder builder,
        IMethodSymbol accessor,
        Accessibility propertyAccessibility,
        string keyword)
    {
        if (accessor.DeclaredAccessibility != propertyAccessibility)
        {
            builder.Append(ApiVisibility.Display(accessor.DeclaredAccessibility));
            builder.Append(' ');
        }

        builder.Append(keyword);
        builder.Append("; ");
    }

    private static string StripLeadingAttributes(string value)
    {
        int position = 0;
        while (position < value.Length && value[position] == '[')
        {
            int depth = 0;
            int index = position;
            for (; index < value.Length; index++)
            {
                if (value[index] == '[')
                {
                    depth++;
                }
                else if (value[index] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        index++;
                        break;
                    }
                }
            }

            if (depth != 0)
            {
                break;
            }

            position = index;
            while (position < value.Length && char.IsWhiteSpace(value[position]))
            {
                position++;
            }
        }

        return value[position..];
    }

    private static string DisplayConstant(object? value)
        => value switch
        {
            null => "null",
            string text => "\"" + text.Replace("\"", "\\\"") + "\"",
            char character => "'" + character + "'",
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? "null",
        };

    private static string OperatorToken(string metadataName)
        => metadataName switch
        {
            "op_Addition" => "+",
            "op_Subtraction" => "-",
            "op_Multiply" => "*",
            "op_Division" => "/",
            "op_Modulus" => "%",
            "op_BitwiseAnd" => "&",
            "op_BitwiseOr" => "|",
            "op_ExclusiveOr" => "^",
            "op_LeftShift" => "<<",
            "op_RightShift" => ">>",
            "op_UnsignedRightShift" => ">>>",
            "op_Equality" => "==",
            "op_Inequality" => "!=",
            "op_LessThan" => "<",
            "op_GreaterThan" => ">",
            "op_LessThanOrEqual" => "<=",
            "op_GreaterThanOrEqual" => ">=",
            "op_UnaryPlus" => "+",
            "op_UnaryNegation" => "-",
            "op_LogicalNot" => "!",
            "op_OnesComplement" => "~",
            "op_Increment" => "++",
            "op_Decrement" => "--",
            "op_True" => "true",
            "op_False" => "false",
            _ => metadataName,
        };

    [GeneratedRegex("\\b(new|virtual|override|abstract|sealed|partial|extern|unsafe|readonly)\\s+")]
    private static partial Regex IgnorableModifierRegex();

    [GeneratedRegex("\\s*\\(\\s*")]
    private static partial Regex OpenParenRegex();

    [GeneratedRegex("\\s*\\)\\s*")]
    private static partial Regex CloseParenRegex();

    [GeneratedRegex("\\s*\\[\\s*")]
    private static partial Regex OpenBracketRegex();

    [GeneratedRegex("\\s*\\]\\s*")]
    private static partial Regex CloseBracketRegex();

    [GeneratedRegex("\\s*\\{\\s*")]
    private static partial Regex OpenBraceRegex();

    [GeneratedRegex("\\s*\\}\\s*")]
    private static partial Regex CloseBraceRegex();

    [GeneratedRegex("\\s*,\\s*")]
    private static partial Regex CommaRegex();

    [GeneratedRegex("\\s*;\\s*")]
    private static partial Regex SemicolonRegex();

    [GeneratedRegex("\\s*<\\s*")]
    private static partial Regex GenericOpenRegex();

    [GeneratedRegex("\\s*>")]
    private static partial Regex GenericCloseRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
