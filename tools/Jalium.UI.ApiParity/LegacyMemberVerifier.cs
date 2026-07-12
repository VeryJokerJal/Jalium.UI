using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal sealed class LegacyMemberVerifier
{
    private readonly ApiSymbolIndex _wpf;
    private readonly ApiSymbolIndex _jalium;
    private readonly NamespaceMapper _mapper;
    private readonly ISet<string> _wpfAssemblyNames;
    private readonly ISet<string> _noMappedAssemblies = new HashSet<string>(StringComparer.Ordinal);

    public LegacyMemberVerifier(
        ApiSymbolIndex wpf,
        ApiSymbolIndex jalium,
        NamespaceMapper mapper,
        ISet<string> wpfAssemblyNames)
    {
        _wpf = wpf;
        _jalium = jalium;
        _mapper = mapper;
        _wpfAssemblyNames = wpfAssemblyNames;
    }

    public IReadOnlyList<LegacyValidationResult> Verify(string csvDirectory)
    {
        var results = new List<LegacyValidationResult>();
        AddRows("missing_properties.csv", VerifyProperty);
        AddRows("missing_methods.csv", VerifyMethod);
        AddRows("missing_events.csv", VerifyEvent);
        AddRows("missing_fields.csv", VerifyField);
        AddRows("missing_enum_values.csv", VerifyEnumValue);
        return results;

        void AddRows(
            string fileName,
            Func<CsvDataRow, string, LegacyValidationResult> verifier)
        {
            string path = Path.Combine(csvDirectory, fileName);
            results.AddRange(LegacyCsvReader.Read(path).Select(row => verifier(row, path)));
        }
    }

    private LegacyValidationResult VerifyProperty(CsvDataRow row, string sourcePath)
    {
        const string category = "missing-property";
        string signature = row.Get("wpf_sig");
        WpfTypeResolution typeResolution = ResolveWpfType(row.Get("namespace"), row.Get("type"));
        if (typeResolution.Type is null)
        {
            return TypeResolutionFailure(row, sourcePath, category, signature, typeResolution);
        }

        string memberName = row.Get("member");
        IPropertySymbol[] candidates = typeResolution.Type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(ApiVisibility.IsContractMember)
            .Where(property => memberName.StartsWith("this[", StringComparison.Ordinal)
                ? property.IsIndexer
                : MemberNameMatches(property.Name, memberName))
            .ToArray();
        IPropertySymbol[] matches = candidates
            .Where(property => LegacyMemberFormatter.Matches(property, signature))
            .ToArray();
        if (matches.Length == 0 && candidates.Length == 1)
        {
            matches = candidates;
        }
        if (matches.Length != 1)
        {
            return MemberResolutionFailure(
                row,
                sourcePath,
                category,
                signature,
                typeResolution,
                matches.Length,
                candidates.Select(LegacyMemberFormatter.Property));
        }

        IPropertySymbol expected = matches[0];
        TargetTypeResolution target = ResolveTargetType(typeResolution.Type);
        if (target.Type is null)
        {
            return TargetResolutionFailure(row, sourcePath, category, signature, expected, target);
        }

        PropertyContract expectedContract = PropertyContract.Create(expected, _mapper, _wpfAssemblyNames);
        var targetCandidates = target.Type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(symbol => MemberNameMatches(symbol.Name, expected.Name)
                || symbol.ExplicitInterfaceImplementations.Any(implementation =>
                    implementation.Name.Equals(expected.Name.Split('.').Last(), StringComparison.Ordinal)))
            .Select(symbol => new
            {
                Symbol = symbol,
                Contract = PropertyContract.Create(symbol, _mapper, _noMappedAssemblies),
            })
            .ToArray();
        var exact = targetCandidates.FirstOrDefault(candidate => candidate.Contract.FullKey == expectedContract.FullKey);
        string status = exact is not null
            ? "present"
            : targetCandidates.Any(candidate => candidate.Contract.IdentityKey == expectedContract.IdentityKey)
                ? "contract-mismatch"
                : "missing";
        string actual = exact is not null
            ? LegacyMemberFormatter.Property(exact.Symbol)
            : JoinLimited(targetCandidates.Select(candidate => LegacyMemberFormatter.Property(candidate.Symbol)));
        return Result(
            row,
            sourcePath,
            category,
            expected,
            typeResolution,
            signature,
            status,
            actual,
            status == "present"
                ? "The mapped Jalium property matches type, index parameters, accessors, accessibility, and static/virtual contract."
                : "No declared Jalium property has the complete mapped WPF contract.");
    }

    private LegacyValidationResult VerifyMethod(CsvDataRow row, string sourcePath)
    {
        const string category = "missing-method";
        string signature = row.Get("wpf_sig");
        WpfTypeResolution typeResolution = ResolveWpfType(row.Get("namespace"), row.Get("type"));
        if (typeResolution.Type is null)
        {
            return TypeResolutionFailure(row, sourcePath, category, signature, typeResolution);
        }

        string metadataName = MethodMetadataName(row.Get("member"));
        IMethodSymbol[] candidates = typeResolution.Type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(ApiVisibility.IsContractMember)
            .Where(method => method.MethodKind is not (
                MethodKind.Constructor
                or MethodKind.StaticConstructor
                or MethodKind.PropertyGet
                or MethodKind.PropertySet
                or MethodKind.EventAdd
                or MethodKind.EventRemove))
            .Where(method => method.MetadataName.Equals(metadataName, StringComparison.Ordinal)
                || MemberNameMatches(method.Name, row.Get("member")))
            .ToArray();
        IMethodSymbol[] matches = candidates
            .Where(method => LegacyMemberFormatter.Matches(method, signature))
            .ToArray();
        if (matches.Length == 0 && candidates.Length == 1)
        {
            matches = candidates;
        }
        else if (matches.Length == 0 && candidates.Length > 1)
        {
            int parameterCount = ParseParameterCount(signature);
            IMethodSymbol[] countMatches = candidates
                .Where(method => method.Parameters.Length == parameterCount)
                .ToArray();
            if (countMatches.Length == 1)
            {
                matches = countMatches;
            }
        }
        if (matches.Length != 1)
        {
            return MemberResolutionFailure(
                row,
                sourcePath,
                category,
                signature,
                typeResolution,
                matches.Length,
                candidates.Select(LegacyMemberFormatter.Method));
        }

        IMethodSymbol expected = matches[0];
        TargetTypeResolution target = ResolveTargetType(typeResolution.Type);
        if (target.Type is null)
        {
            return TargetResolutionFailure(row, sourcePath, category, signature, expected, target);
        }

        MethodContract expectedContract = MethodContract.Create(expected, _mapper, _wpfAssemblyNames);
        var targetCandidates = target.Type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind is not (
                MethodKind.Constructor
                or MethodKind.StaticConstructor
                or MethodKind.PropertyGet
                or MethodKind.PropertySet
                or MethodKind.EventAdd
                or MethodKind.EventRemove))
            .Where(symbol => symbol.MetadataName.Equals(expected.MetadataName, StringComparison.Ordinal)
                || symbol.Name.Split('.').Last().Equals(expected.Name.Split('.').Last(), StringComparison.Ordinal))
            .Select(symbol => new
            {
                Symbol = symbol,
                Contract = MethodContract.Create(symbol, _mapper, _noMappedAssemblies),
            })
            .ToArray();
        var exact = targetCandidates.FirstOrDefault(candidate => candidate.Contract.FullKey == expectedContract.FullKey);
        string status = exact is not null
            ? "present"
            : targetCandidates.Any(candidate => candidate.Contract.IdentityKey == expectedContract.IdentityKey)
                ? "contract-mismatch"
                : "missing";
        string actual = exact is not null
            ? LegacyMemberFormatter.Method(exact.Symbol)
            : JoinLimited(targetCandidates.Select(candidate => LegacyMemberFormatter.Method(candidate.Symbol)));
        return Result(
            row,
            sourcePath,
            category,
            expected,
            typeResolution,
            signature,
            status,
            actual,
            status == "present"
                ? "The mapped Jalium method matches overload identity, return/parameter types, modifiers, accessibility, and generic constraints."
                : "No declared Jalium method has the complete mapped WPF contract.");
    }

    private LegacyValidationResult VerifyEvent(CsvDataRow row, string sourcePath)
    {
        const string category = "missing-event";
        string signature = row.Get("wpf_sig");
        WpfTypeResolution typeResolution = ResolveWpfType(row.Get("namespace"), row.Get("type"));
        if (typeResolution.Type is null)
        {
            return TypeResolutionFailure(row, sourcePath, category, signature, typeResolution);
        }

        string memberName = row.Get("member");
        IEventSymbol[] candidates = typeResolution.Type.GetMembers()
            .OfType<IEventSymbol>()
            .Where(ApiVisibility.IsContractMember)
            .Where(symbol => MemberNameMatches(symbol.Name, memberName))
            .ToArray();
        IEventSymbol[] matches = candidates
            .Where(symbol => LegacyMemberFormatter.Matches(symbol, signature))
            .ToArray();
        if (matches.Length == 0 && candidates.Length == 1)
        {
            matches = candidates;
        }
        if (matches.Length != 1)
        {
            return MemberResolutionFailure(
                row,
                sourcePath,
                category,
                signature,
                typeResolution,
                matches.Length,
                candidates.Select(LegacyMemberFormatter.Event));
        }

        IEventSymbol expected = matches[0];
        TargetTypeResolution target = ResolveTargetType(typeResolution.Type);
        if (target.Type is null)
        {
            return TargetResolutionFailure(row, sourcePath, category, signature, expected, target);
        }

        EventContract expectedContract = EventContract.Create(expected, _mapper, _wpfAssemblyNames);
        var targetCandidates = target.Type.GetMembers()
            .OfType<IEventSymbol>()
            .Where(symbol => MemberNameMatches(symbol.Name, expected.Name))
            .Select(symbol => new
            {
                Symbol = symbol,
                Contract = EventContract.Create(symbol, _mapper, _noMappedAssemblies),
            })
            .ToArray();
        var exact = targetCandidates.FirstOrDefault(candidate => candidate.Contract.FullKey == expectedContract.FullKey);
        string status = exact is not null
            ? "present"
            : targetCandidates.Length > 0 ? "contract-mismatch" : "missing";
        string actual = exact is not null
            ? LegacyMemberFormatter.Event(exact.Symbol)
            : JoinLimited(targetCandidates.Select(candidate => LegacyMemberFormatter.Event(candidate.Symbol)));
        return Result(
            row,
            sourcePath,
            category,
            expected,
            typeResolution,
            signature,
            status,
            actual,
            status == "present"
                ? "The mapped Jalium event matches handler type, accessibility, static/virtual contract, and accessors."
                : "No declared Jalium event has the complete mapped WPF contract.");
    }

    private LegacyValidationResult VerifyField(CsvDataRow row, string sourcePath)
    {
        const string category = "missing-field";
        string signature = row.Get("wpf_sig");
        WpfTypeResolution typeResolution = ResolveWpfType(row.Get("namespace"), row.Get("type"));
        if (typeResolution.Type is null)
        {
            return TypeResolutionFailure(row, sourcePath, category, signature, typeResolution);
        }

        string memberName = row.Get("member");
        IFieldSymbol[] candidates = typeResolution.Type.GetMembers(memberName)
            .OfType<IFieldSymbol>()
            .Where(ApiVisibility.IsApiMember)
            .ToArray();
        if (candidates.Length != 1)
        {
            return MemberResolutionFailure(
                row,
                sourcePath,
                category,
                signature,
                typeResolution,
                candidates.Length,
                candidates.Select(LegacyMemberFormatter.Field));
        }

        IFieldSymbol expected = candidates[0];
        TargetTypeResolution target = ResolveTargetType(typeResolution.Type);
        if (target.Type is null)
        {
            return TargetResolutionFailure(row, sourcePath, category, signature, expected, target);
        }

        FieldContract expectedContract = FieldContract.Create(expected, _mapper, _wpfAssemblyNames);
        var targetCandidates = target.Type.GetMembers(expected.Name)
            .OfType<IFieldSymbol>()
            .Select(symbol => new
            {
                Symbol = symbol,
                Contract = FieldContract.Create(symbol, _mapper, _noMappedAssemblies),
            })
            .ToArray();
        var exact = targetCandidates.FirstOrDefault(candidate => candidate.Contract.FullKey == expectedContract.FullKey);
        string status = exact is not null
            ? "present"
            : targetCandidates.Length > 0 ? "contract-mismatch" : "missing";
        string actual = exact is not null
            ? LegacyMemberFormatter.Field(exact.Symbol)
            : JoinLimited(targetCandidates.Select(candidate => LegacyMemberFormatter.Field(candidate.Symbol)));
        return Result(
            row,
            sourcePath,
            category,
            expected,
            typeResolution,
            signature,
            status,
            actual,
            status == "present"
                ? "The mapped Jalium field matches type, accessibility, static/const/readonly, and constant contract."
                : "No declared Jalium field has the complete mapped WPF contract.");
    }

    private LegacyValidationResult VerifyEnumValue(CsvDataRow row, string sourcePath)
    {
        const string category = "missing-enum-value";
        string valueText = row.Get("value");
        string memberName = valueText.Split('=', 2)[0].Trim();
        WpfTypeResolution typeResolution = ResolveWpfType(row.Get("namespace"), row.Get("enum"));
        if (typeResolution.Type is null)
        {
            return TypeResolutionFailure(row, sourcePath, category, valueText, typeResolution);
        }

        IFieldSymbol[] candidates = typeResolution.Type.GetMembers(memberName)
            .OfType<IFieldSymbol>()
            .Where(static field => field.HasConstantValue)
            .ToArray();
        if (candidates.Length != 1)
        {
            return MemberResolutionFailure(
                row,
                sourcePath,
                category,
                valueText,
                typeResolution,
                candidates.Length,
                candidates.Select(LegacyMemberFormatter.Field));
        }

        IFieldSymbol expected = candidates[0];
        TargetTypeResolution target = ResolveTargetType(typeResolution.Type);
        if (target.Type is null)
        {
            return TargetResolutionFailure(row, sourcePath, category, valueText, expected, target);
        }

        IFieldSymbol[] targetFields = target.Type.GetMembers(memberName)
            .OfType<IFieldSymbol>()
            .ToArray();
        string status;
        string actual;
        string diagnostic;
        if (targetFields.Length == 0)
        {
            status = "missing";
            actual = string.Empty;
            diagnostic = "The enum member is absent from the mapped Jalium enum.";
        }
        else if (targetFields.Length > 1)
        {
            status = "ambiguous-jalium-api";
            actual = JoinLimited(targetFields.Select(LegacyMemberFormatter.Field));
            diagnostic = "Multiple same-name Jalium enum fields were found.";
        }
        else
        {
            IFieldSymbol field = targetFields[0];
            bool sameValue = field.HasConstantValue
                && ContractKey.Constant(field.ConstantValue) == ContractKey.Constant(expected.ConstantValue);
            status = sameValue ? "present" : "value-mismatch";
            actual = field.HasConstantValue
                ? $"{field.Name} = {field.ConstantValue}"
                : field.Name + " (not constant)";
            diagnostic = sameValue
                ? "The Jalium enum member has the exact WPF constant value."
                : $"Expected constant value '{expected.ConstantValue}'.";
        }

        return Result(
            row,
            sourcePath,
            category,
            expected,
            typeResolution,
            valueText,
            status,
            actual,
            diagnostic);
    }

    private WpfTypeResolution ResolveWpfType(string namespaceName, string legacyTypeName)
    {
        string metadataName = MetadataNames.FromLegacyTypeName(legacyTypeName);
        IReadOnlyList<INamedTypeSymbol> all = namespaceName == "?"
            ? _wpf.FindTypesBySimpleName(metadataName)
            : _wpf.FindTypes(MetadataNames.Join(namespaceName, metadataName));
        INamedTypeSymbol[] visible = all.Where(ApiVisibility.IsExternallyVisible).ToArray();
        if (visible.Length == 1)
        {
            INamedTypeSymbol type = visible[0];
            string authoritativeNamespace = type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : type.ContainingNamespace.ToDisplayString();
            return new WpfTypeResolution(
                type,
                authoritativeNamespace,
                legacyTypeName,
                metadataName,
                _mapper.MapType(authoritativeNamespace, metadataName),
                "resolved",
                string.Empty);
        }

        string status = visible.Length > 1
            ? "ambiguous-wpf-api"
            : all.Count == 1
                ? "legacy-out-of-scope"
                : "legacy-input-invalid";
        return new WpfTypeResolution(
            null,
            namespaceName,
            legacyTypeName,
            metadataName,
            _mapper.MapType(namespaceName, metadataName),
            status,
            LegacyVerifier.DescribeTypes(visible.Length > 0 ? visible : all));
    }

    private TargetTypeResolution ResolveTargetType(INamedTypeSymbol wpfType)
    {
        string namespaceName = wpfType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : wpfType.ContainingNamespace.ToDisplayString();
        string expectedFullName = _mapper.MapType(namespaceName, MetadataNames.NestedMetadataName(wpfType));
        IReadOnlyList<INamedTypeSymbol> allExact = _jalium.FindTypes(expectedFullName);
        INamedTypeSymbol[] visibleExact = allExact.Where(ApiVisibility.IsExternallyVisible).ToArray();
        if (visibleExact.Length == 1)
        {
            return new TargetTypeResolution(visibleExact[0], expectedFullName, "resolved", string.Empty);
        }

        if (visibleExact.Length > 1)
        {
            return new TargetTypeResolution(
                null,
                expectedFullName,
                "ambiguous-jalium-api",
                LegacyVerifier.DescribeTypes(visibleExact));
        }

        if (allExact.Count > 0)
        {
            return new TargetTypeResolution(
                null,
                expectedFullName,
                "accessibility-mismatch",
                LegacyVerifier.DescribeTypes(allExact));
        }

        INamedTypeSymbol[] moved = _jalium.FindTypesBySimpleName(wpfType.MetadataName)
            .Where(ApiVisibility.IsExternallyVisible)
            .ToArray();
        return new TargetTypeResolution(
            null,
            expectedFullName,
            moved.Length > 0 ? "namespace-mismatch" : "declaring-type-missing",
            LegacyVerifier.DescribeTypes(moved));
    }

    private static LegacyValidationResult TypeResolutionFailure(
        CsvDataRow row,
        string sourcePath,
        string category,
        string expectedSignature,
        WpfTypeResolution resolution)
    {
        string apiId = StableIds.UnresolvedApiId(
            category,
            resolution.WpfNamespace,
            resolution.LegacyTypeName,
            expectedSignature);
        return LegacyVerifier.CreateResult(
            row,
            sourcePath,
            category,
            apiId,
            string.Empty,
            resolution.WpfNamespace,
            resolution.LegacyTypeName,
            resolution.ExpectedJaliumType,
            expectedSignature,
            resolution.Status,
            resolution.Actual,
            "The WPF declaring type could not be resolved uniquely within the public/protected scope.");
    }

    private static LegacyValidationResult MemberResolutionFailure(
        CsvDataRow row,
        string sourcePath,
        string category,
        string expectedSignature,
        WpfTypeResolution typeResolution,
        int matchCount,
        IEnumerable<string> candidates)
    {
        string apiId = StableIds.UnresolvedApiId(
            category,
            typeResolution.WpfNamespace,
            typeResolution.LegacyTypeName,
            expectedSignature);
        return LegacyVerifier.CreateResult(
            row,
            sourcePath,
            category,
            apiId,
            typeResolution.Type?.ContainingAssembly.Name ?? string.Empty,
            typeResolution.WpfNamespace,
            typeResolution.LegacyTypeName,
            typeResolution.ExpectedJaliumType,
            expectedSignature,
            matchCount > 1 ? "ambiguous-wpf-api" : "unsupported-legacy-signature",
            JoinLimited(candidates),
            $"The legacy display signature matched {matchCount} WPF members; no pass was inferred.");
    }

    private LegacyValidationResult TargetResolutionFailure(
        CsvDataRow row,
        string sourcePath,
        string category,
        string expectedSignature,
        ISymbol expected,
        TargetTypeResolution target)
        => LegacyVerifier.CreateResult(
            row,
            sourcePath,
            category,
            StableIds.ApiId(expected),
            expected.ContainingAssembly.Name,
            expected.ContainingNamespace.ToDisplayString(),
            expected.ContainingType.Name,
            target.ExpectedFullName,
            expectedSignature,
            target.Status,
            target.Actual,
            "The mapped Jalium declaring type could not be resolved uniquely.");

    private static LegacyValidationResult Result(
        CsvDataRow row,
        string sourcePath,
        string category,
        ISymbol expected,
        WpfTypeResolution typeResolution,
        string expectedSignature,
        string status,
        string actual,
        string diagnostic)
        => LegacyVerifier.CreateResult(
            row,
            sourcePath,
            category,
            StableIds.ApiId(expected),
            expected.ContainingAssembly.Name,
            typeResolution.WpfNamespace,
            typeResolution.LegacyTypeName,
            typeResolution.ExpectedJaliumType,
            expectedSignature,
            status,
            actual,
            diagnostic);

    private static string MethodMetadataName(string legacyMemberName)
    {
        if (legacyMemberName.StartsWith("op_", StringComparison.Ordinal))
        {
            int metadataAnnotation = legacyMemberName.IndexOf(' ');
            return metadataAnnotation >= 0 ? legacyMemberName[..metadataAnnotation] : legacyMemberName;
        }

        if (legacyMemberName.StartsWith("explicit operator", StringComparison.Ordinal))
        {
            return "op_Explicit";
        }

        if (legacyMemberName.StartsWith("implicit operator", StringComparison.Ordinal))
        {
            return "op_Implicit";
        }

        if (!legacyMemberName.StartsWith("operator ", StringComparison.Ordinal))
        {
            return legacyMemberName;
        }

        string operatorToken = legacyMemberName[9..].Trim();
        bool unary = operatorToken.EndsWith(" (unary)", StringComparison.Ordinal);
        int operatorAnnotation = operatorToken.IndexOf(" (", StringComparison.Ordinal);
        if (operatorAnnotation >= 0)
        {
            operatorToken = operatorToken[..operatorAnnotation];
        }

        return (operatorToken, unary) switch
        {
            ("+", true) => "op_UnaryPlus",
            ("-", true) => "op_UnaryNegation",
            ("+", false) => "op_Addition",
            ("-", false) => "op_Subtraction",
            ("*", _) => "op_Multiply",
            ("/", _) => "op_Division",
            ("%", _) => "op_Modulus",
            ("&", _) => "op_BitwiseAnd",
            ("|", _) => "op_BitwiseOr",
            ("^", _) => "op_ExclusiveOr",
            ("<<", _) => "op_LeftShift",
            (">>", _) => "op_RightShift",
            ("==", _) => "op_Equality",
            ("!=", _) => "op_Inequality",
            ("<", _) => "op_LessThan",
            (">", _) => "op_GreaterThan",
            ("<=", _) => "op_LessThanOrEqual",
            (">=", _) => "op_GreaterThanOrEqual",
            ("true", _) => "op_True",
            ("false", _) => "op_False",
            (string unknownToken, _) => "operator " + unknownToken,
        };
    }

    private static bool MemberNameMatches(string symbolName, string legacyName)
        => symbolName.Equals(legacyName, StringComparison.Ordinal)
            || symbolName.EndsWith("." + legacyName, StringComparison.Ordinal)
            || legacyName.EndsWith("." + symbolName, StringComparison.Ordinal);

    private static int ParseParameterCount(string signature)
    {
        int open = signature.IndexOf('(');
        if (open < 0)
        {
            return -1;
        }

        int parenDepth = 0;
        int genericDepth = 0;
        int bracketDepth = 0;
        int commas = 0;
        bool content = false;
        for (int index = open + 1; index < signature.Length; index++)
        {
            switch (signature[index])
            {
                case '<': genericDepth++; break;
                case '>': genericDepth--; break;
                case '[': bracketDepth++; break;
                case ']': bracketDepth--; break;
                case '(': parenDepth++; break;
                case ')' when parenDepth == 0:
                    return content ? commas + 1 : 0;
                case ')': parenDepth--; break;
                case ',' when parenDepth == 0 && genericDepth == 0 && bracketDepth == 0:
                    commas++;
                    break;
                default:
                    if (!char.IsWhiteSpace(signature[index]))
                    {
                        content = true;
                    }
                    break;
            }
        }

        return -1;
    }

    private static string JoinLimited(IEnumerable<string> values)
    {
        string[] materialized = values.Where(static value => value.Length > 0).Take(6).ToArray();
        return string.Join(" || ", materialized);
    }

    private sealed record WpfTypeResolution(
        INamedTypeSymbol? Type,
        string WpfNamespace,
        string LegacyTypeName,
        string MetadataName,
        string ExpectedJaliumType,
        string Status,
        string Actual);

    private sealed record TargetTypeResolution(
        INamedTypeSymbol? Type,
        string ExpectedFullName,
        string Status,
        string Actual);
}
