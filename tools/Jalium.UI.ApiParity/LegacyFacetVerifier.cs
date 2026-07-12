using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal sealed class LegacyFacetVerifier
{
    private readonly ApiSymbolIndex _wpf;
    private readonly ApiSymbolIndex _jalium;
    private readonly NamespaceMapper _mapper;
    private readonly ISet<string> _wpfAssemblyNames;
    private readonly ISet<string> _noMappedAssemblies = new HashSet<string>(StringComparer.Ordinal);

    public LegacyFacetVerifier(
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
        string inconsistenciesPath = Path.Combine(csvDirectory, "inconsistencies.csv");
        results.AddRange(LegacyCsvReader.Read(inconsistenciesPath)
            .Select(row => VerifyInconsistency(row, inconsistenciesPath)));
        string namespacePath = Path.Combine(csvDirectory, "ns_mismatch.csv");
        results.AddRange(LegacyCsvReader.Read(namespacePath)
            .Select(row => VerifyNamespace(row, namespacePath)));
        return results;
    }

    private LegacyValidationResult VerifyInconsistency(CsvDataRow row, string sourcePath)
    {
        string differenceKind = row.Get("kind");
        string category = "inconsistency-" + differenceKind;
        string legacyNamespace = row.Get("namespace");
        string legacyType = row.Get("type");
        string api = row.Get("api");
        string expectedSignature = row.Get("wpf_sig");
        TypeResolution typeResolution = ResolveWpfType(legacyNamespace, legacyType);
        string gapFacet = $"{category}:{legacyNamespace}.{legacyType}:{api}:"
            + LegacyMemberFormatter.NormalizeMember(expectedSignature);
        if (typeResolution.Type is null)
        {
            string unresolvedApi = StableIds.UnresolvedApiId(
                category,
                legacyNamespace,
                legacyType,
                api,
                expectedSignature);
            return LegacyVerifier.CreateResult(
                row,
                sourcePath,
                category,
                unresolvedApi,
                string.Empty,
                typeResolution.Namespace,
                legacyType,
                typeResolution.ExpectedTarget,
                expectedSignature,
                typeResolution.Status,
                typeResolution.Actual,
                "The WPF declaring type could not be resolved uniquely; the facet was not inferred.",
                gapFacet);
        }

        ExpectedSymbolResolution expectedResolution = ResolveExpectedSymbol(
            typeResolution.Type,
            legacyType,
            api,
            expectedSignature,
            differenceKind);
        if (expectedResolution.Symbol is null)
        {
            string unresolvedApi = StableIds.UnresolvedApiId(
                category,
                typeResolution.Namespace,
                legacyType,
                api,
                expectedSignature);
            return LegacyVerifier.CreateResult(
                row,
                sourcePath,
                category,
                unresolvedApi,
                typeResolution.Type.ContainingAssembly.Name,
                typeResolution.Namespace,
                legacyType,
                typeResolution.ExpectedTarget,
                expectedSignature,
                expectedResolution.Status,
                expectedResolution.Actual,
                expectedResolution.Diagnostic,
                gapFacet);
        }

        FacetEvaluation? invalidLegacyClaim = ValidateLegacyMetadataClaim(
            differenceKind,
            expectedResolution.Symbol,
            expectedSignature);
        if (invalidLegacyClaim is not null)
        {
            return LegacyVerifier.CreateResult(
                row,
                sourcePath,
                category,
                StableIds.ApiId(expectedResolution.Symbol),
                expectedResolution.Symbol.ContainingAssembly.Name,
                typeResolution.Namespace,
                legacyType,
                typeResolution.ExpectedTarget,
                expectedSignature,
                invalidLegacyClaim.Status,
                DisplaySymbol(expectedResolution.Symbol),
                invalidLegacyClaim.Diagnostic,
                gapFacet);
        }

        TargetTypeResolution targetType = ResolveTargetType(typeResolution.Type);
        if (targetType.Type is null)
        {
            return LegacyVerifier.CreateResult(
                row,
                sourcePath,
                category,
                StableIds.ApiId(expectedResolution.Symbol),
                expectedResolution.Symbol.ContainingAssembly.Name,
                typeResolution.Namespace,
                legacyType,
                typeResolution.ExpectedTarget,
                expectedSignature,
                targetType.Status,
                targetType.Actual,
                "The mapped Jalium declaring type could not be resolved uniquely; the facet was not inferred.",
                gapFacet);
        }

        TargetSymbolResolution targetResolution = ResolveTargetSymbol(
            expectedResolution.Symbol,
            targetType.Type,
            differenceKind);
        if (targetResolution.Symbol is null)
        {
            return LegacyVerifier.CreateResult(
                row,
                sourcePath,
                category,
                StableIds.ApiId(expectedResolution.Symbol),
                expectedResolution.Symbol.ContainingAssembly.Name,
                typeResolution.Namespace,
                legacyType,
                typeResolution.ExpectedTarget,
                expectedSignature,
                targetResolution.Status,
                targetResolution.Actual,
                targetResolution.Diagnostic,
                gapFacet);
        }

        FacetEvaluation evaluation = EvaluateFacet(
            differenceKind,
            expectedResolution.Symbol,
            targetResolution.Symbol,
            expectedSignature);
        return LegacyVerifier.CreateResult(
            row,
            sourcePath,
            category,
            StableIds.ApiId(expectedResolution.Symbol),
            expectedResolution.Symbol.ContainingAssembly.Name,
            typeResolution.Namespace,
            legacyType,
            typeResolution.ExpectedTarget,
            expectedSignature,
            evaluation.Status,
            DisplaySymbol(targetResolution.Symbol),
            evaluation.Diagnostic,
            gapFacet);
    }

    private LegacyValidationResult VerifyNamespace(CsvDataRow row, string sourcePath)
    {
        const string category = "namespace";
        string legacyNamespace = row.Get("wpf_namespace");
        string legacyType = row.Get("type");
        TypeResolution resolution = ResolveWpfType(legacyNamespace, legacyType);
        string gapFacet = $"namespace:{legacyNamespace}.{legacyType}";
        if (resolution.Type is null)
        {
            string unresolvedApi = StableIds.UnresolvedApiId(category, legacyNamespace, legacyType);
            return LegacyVerifier.CreateResult(
                row,
                sourcePath,
                category,
                unresolvedApi,
                string.Empty,
                resolution.Namespace,
                legacyType,
                resolution.ExpectedTarget,
                resolution.ExpectedTarget,
                resolution.Status,
                resolution.Actual,
                "The WPF type could not be resolved uniquely; namespace parity was not inferred.",
                gapFacet);
        }

        IReadOnlyList<INamedTypeSymbol> allExact = _jalium.FindTypes(resolution.ExpectedTarget);
        INamedTypeSymbol[] exact = allExact.Where(ApiVisibility.IsExternallyVisible).ToArray();
        string status;
        string actual;
        string diagnostic;
        if (exact.Length == 1)
        {
            status = "resolved";
            actual = LegacyVerifier.DescribeType(exact[0]);
            diagnostic = "The WPF type now exists at the exact mapped Jalium namespace and metadata name.";
        }
        else if (exact.Length > 1)
        {
            status = "ambiguous-jalium-api";
            actual = LegacyVerifier.DescribeTypes(exact);
            diagnostic = "Multiple externally visible Jalium types have the mapped metadata name.";
        }
        else if (allExact.Count > 0)
        {
            status = "accessibility-mismatch";
            actual = LegacyVerifier.DescribeTypes(allExact);
            diagnostic = "The mapped Jalium type exists but is not externally visible.";
        }
        else
        {
            INamedTypeSymbol[] moved = _jalium.FindTypesBySimpleName(resolution.Type.MetadataName)
                .Where(ApiVisibility.IsExternallyVisible)
                .ToArray();
            status = moved.Length > 0 ? "still-mismatched" : "missing";
            actual = LegacyVerifier.DescribeTypes(moved);
            diagnostic = moved.Length > 0
                ? "Same-name Jalium type candidates still exist outside the mapped namespace."
                : "No externally visible same-name Jalium type exists.";
        }

        return LegacyVerifier.CreateResult(
            row,
            sourcePath,
            category,
            StableIds.ApiId(resolution.Type),
            resolution.Type.ContainingAssembly.Name,
            resolution.Namespace,
            legacyType,
            resolution.ExpectedTarget,
            resolution.ExpectedTarget,
            status,
            actual,
            diagnostic,
            gapFacet);
    }

    private FacetEvaluation EvaluateFacet(
        string kind,
        ISymbol expected,
        ISymbol actual,
        string legacyWpfSignature)
    {
        if (kind == "kind-mismatch"
            && expected is INamedTypeSymbol expectedType
            && actual is INamedTypeSymbol actualType
            && legacyWpfSignature.Contains("concrete, instantiable", StringComparison.OrdinalIgnoreCase))
        {
            bool expectedInstantiable = IsPubliclyInstantiable(expectedType);
            string typeShapeKeys = DescribeTypeShapeKeys(expectedType, actualType);
            if (!expectedInstantiable)
            {
                return new FacetEvaluation(
                    "legacy-input-invalid",
                    $"The legacy row claims a concrete, publicly instantiable type, but authoritative WPF metadata does not. {typeShapeKeys}");
            }

            return IsPubliclyInstantiable(actualType)
                ? new FacetEvaluation(
                    "resolved",
                    $"The Jalium type is concrete and has the public parameterless constructor claimed by the legacy row. {typeShapeKeys}")
                : new FacetEvaluation(
                    "still-mismatched",
                    $"The Jalium type is not concrete and publicly instantiable as required by WPF metadata. {typeShapeKeys}");
        }

        if (kind == "kind-mismatch"
            && expected is INamedTypeSymbol expectedNamedType
            && actual is INamedTypeSymbol actualNamedType)
        {
            return CompareTypeShapes(expectedNamedType, actualNamedType);
        }

        return kind switch
        {
            "accessibility" => CompareKeys(
                AccessibilityKey(expected),
                AccessibilityKey(actual),
                "accessibility and accessor visibility"),
            "static" => CompareOptionalValues(
                StaticValue(expected),
                StaticValue(actual),
                "static/instance declaration"),
            "return-type" => CompareOptionalKeys(
                ReturnTypeKey(expected, mapWpf: true),
                ReturnTypeKey(actual, mapWpf: false),
                "return/property/event/field type"),
            "param-count" => CompareOptionalValues(
                ParameterCount(expected),
                ParameterCount(actual),
                "parameter count"),
            "param-type" => CompareOptionalKeys(
                ParameterTypesKey(expected, mapWpf: true),
                ParameterTypesKey(actual, mapWpf: false),
                "ordered parameter types and ref kinds"),
            "param-order" => CompareOptionalKeys(
                ParameterOrderKey(expected, mapWpf: true),
                ParameterOrderKey(actual, mapWpf: false),
                "ordered parameter types and names"),
            "default-value" => CompareDefaultValue(expected, actual, legacyWpfSignature),
            "generic-constraint" => CompareGenericConstraints(expected, actual),
            "kind-mismatch" => CompareKeys(
                FullShapeKey(expected, mapWpf: true),
                FullShapeKey(actual, mapWpf: false),
                "complete member shape"),
            _ => new FacetEvaluation(
                "unsupported-facet",
                $"Facet '{kind}' is not implemented; no pass was inferred."),
        };
    }

    private static FacetEvaluation? ValidateLegacyMetadataClaim(
        string differenceKind,
        ISymbol authoritativeWpfSymbol,
        string legacyWpfSignature)
    {
        if (differenceKind == "accessibility"
            && authoritativeWpfSymbol is IPropertySymbol
            && legacyWpfSignature.Contains("{", StringComparison.Ordinal)
            && !MatchesLegacySignature(authoritativeWpfSymbol, legacyWpfSignature))
        {
            return new FacetEvaluation(
                "legacy-input-invalid",
                "The legacy property/accessor declaration contradicts authoritative WPF metadata; the Jalium contract was not relaxed to match the stale row.");
        }

        if (differenceKind == "kind-mismatch"
            && authoritativeWpfSymbol is IMethodSymbol
            && legacyWpfSignature.Contains("real comparison", StringComparison.OrdinalIgnoreCase))
        {
            return new FacetEvaluation(
                "legacy-input-invalid",
                "The legacy row describes runtime method behavior as a metadata kind mismatch; authoritative metadata cannot encode that claim.");
        }

        return null;
    }

    private static bool IsPubliclyInstantiable(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0
            && constructor.DeclaredAccessibility == Accessibility.Public);

    private FacetEvaluation CompareDefaultValue(
        ISymbol expected,
        ISymbol actual,
        string legacyWpfSignature)
    {
        if (expected is IFieldSymbol expectedField
            && actual is IFieldSymbol actualField
            && expectedField.HasConstantValue)
        {
            bool equal = actualField.HasConstantValue
                && ContractKey.Constant(expectedField.ConstantValue)
                    == ContractKey.Constant(actualField.ConstantValue);
            return equal
                ? new FacetEvaluation("resolved", "The field/enum constant value matches WPF exactly.")
                : new FacetEvaluation(
                    "still-mismatched",
                    $"Expected constant '{expectedField.ConstantValue}', found '{actualField.ConstantValue}'.");
        }

        IParameterSymbol[]? expectedParameters = Parameters(expected);
        IParameterSymbol[]? actualParameters = Parameters(actual);
        if (expectedParameters is not null
            && actualParameters is not null
            && expectedParameters.Length == actualParameters.Length
            && (expectedParameters.Any(static parameter => parameter.HasExplicitDefaultValue)
                || actualParameters.Any(static parameter => parameter.HasExplicitDefaultValue)))
        {
            string expectedDefaults = string.Join(
                ";",
                expectedParameters.Select(static parameter => parameter.HasExplicitDefaultValue
                    ? ContractKey.Constant(parameter.ExplicitDefaultValue)
                    : "-"));
            string actualDefaults = string.Join(
                ";",
                actualParameters.Select(static parameter => parameter.HasExplicitDefaultValue
                    ? ContractKey.Constant(parameter.ExplicitDefaultValue)
                    : "-"));
            return CompareKeys(expectedDefaults, actualDefaults, "parameter default values");
        }

        return new FacetEvaluation(
            "unsupported-facet",
            legacyWpfSignature.Contains("//", StringComparison.Ordinal)
                ? "The row describes constructor/runtime semantics in a comment; metadata cannot prove it."
                : "No metadata constant or optional-parameter default is available; no pass was inferred.");
    }

    private FacetEvaluation CompareGenericConstraints(ISymbol expected, ISymbol actual)
    {
        ITypeParameterSymbol[] expectedParameters = TypeParameters(expected);
        ITypeParameterSymbol[] actualParameters = TypeParameters(actual);
        if (expectedParameters.Length == 0 || actualParameters.Length == 0)
        {
            return new FacetEvaluation(
                "unsupported-facet",
                "The row is labeled generic-constraint but one side has no generic parameters; no pass was inferred.");
        }

        string expectedKey = string.Join(
            ";",
            expectedParameters.Select(parameter => ContractKey.TypeParameterConstraint(
                parameter,
                _mapper,
                _wpfAssemblyNames)));
        string actualKey = string.Join(
            ";",
            actualParameters.Select(parameter => ContractKey.TypeParameterConstraint(
                parameter,
                _mapper,
                _noMappedAssemblies)));
        return CompareKeys(expectedKey, actualKey, "generic constraints");
    }

    private static FacetEvaluation CompareKeys(string expected, string actual, string description)
        => expected == actual
            ? new FacetEvaluation("resolved", $"The {description} matches WPF metadata.")
            : new FacetEvaluation("still-mismatched", $"The {description} still differs from WPF metadata.");

    private FacetEvaluation CompareTypeShapes(INamedTypeSymbol expectedType, INamedTypeSymbol actualType)
    {
        TypeShape expected = CreateTypeShape(expectedType, _wpfAssemblyNames);
        TypeShape actual = CreateTypeShape(actualType, _noMappedAssemblies);
        var differences = new List<string>();

        AddDifference(differences, "kind", expected.Kind, actual.Kind);
        AddDifference(differences, "arity", expected.Arity, actual.Arity);
        AddDifference(differences, "static", expected.IsStatic, actual.IsStatic);
        AddDifference(differences, "abstract", expected.IsAbstract, actual.IsAbstract);
        AddDifference(differences, "sealed", expected.IsSealed, actual.IsSealed);
        AddDifference(differences, "base", expected.BaseType, actual.BaseType);
        if (!expected.Constraints.SequenceEqual(actual.Constraints, StringComparer.Ordinal))
        {
            differences.Add(
                $"constraints expected=[{string.Join(",", expected.Constraints)}] actual=[{string.Join(",", actual.Constraints)}]");
        }

        string[] missingRequiredInterfaces = expected.Interfaces
            .Except(actual.Interfaces, StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (missingRequiredInterfaces.Length > 0)
        {
            differences.Add($"missing required interfaces=[{string.Join(",", missingRequiredInterfaces)}]");
        }

        string typeShapeKeys = DescribeTypeShapeKeys(expected, actual);
        return differences.Count == 0
            ? new FacetEvaluation(
                "resolved",
                $"The required WPF type shape is satisfied; additional Jalium interfaces are allowed. {typeShapeKeys}")
            : new FacetEvaluation(
                "still-mismatched",
                $"The required WPF type shape still differs: {string.Join("; ", differences)}. {typeShapeKeys}");
    }

    private string DescribeTypeShapeKeys(INamedTypeSymbol expectedType, INamedTypeSymbol actualType) =>
        DescribeTypeShapeKeys(
            CreateTypeShape(expectedType, _wpfAssemblyNames),
            CreateTypeShape(actualType, _noMappedAssemblies));

    private static string DescribeTypeShapeKeys(TypeShape expected, TypeShape actual) =>
        $"Expected TypeShapeKey: {expected.Key}. Actual TypeShapeKey: {actual.Key}.";

    private static void AddDifference<T>(List<string> differences, string name, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add($"{name} expected={expected} actual={actual}");
        }
    }

    private static FacetEvaluation CompareOptionalKeys(
        string? expected,
        string? actual,
        string description)
    {
        if (expected is null || actual is null)
        {
            return new FacetEvaluation(
                "unsupported-facet",
                $"The {description} is not defined for one of the resolved symbol kinds; no pass was inferred.");
        }

        return CompareKeys(expected, actual, description);
    }

    private static FacetEvaluation CompareOptionalValues<T>(T? expected, T? actual, string description)
        where T : struct, IEquatable<T>
    {
        if (expected is null || actual is null)
        {
            return new FacetEvaluation(
                "unsupported-facet",
                $"The {description} is not defined for one of the resolved symbol kinds; no pass was inferred.");
        }

        return expected.Value.Equals(actual.Value)
            ? new FacetEvaluation("resolved", $"The {description} matches WPF metadata.")
            : new FacetEvaluation("still-mismatched", $"The {description} still differs from WPF metadata.");
    }

    private ExpectedSymbolResolution ResolveExpectedSymbol(
        INamedTypeSymbol wpfType,
        string legacyType,
        string api,
        string wpfSignature,
        string differenceKind)
    {
        if (IsTypeLevel(legacyType, api, differenceKind))
        {
            return new ExpectedSymbolResolution(wpfType, "resolved", string.Empty, string.Empty);
        }

        if (IsConstructor(legacyType, api))
        {
            IMethodSymbol[] constructors = wpfType.InstanceConstructors
                .Where(ApiVisibility.IsContractMember)
                .ToArray();
            IMethodSymbol[] matches = constructors
                .Where(constructor => LegacySignatureFormatter.MatchesConstructor(constructor, wpfSignature))
                .ToArray();
            if (matches.Length == 0)
            {
                int parameterCount = ParseParameterCount(wpfSignature);
                matches = constructors.Where(constructor => constructor.Parameters.Length == parameterCount).ToArray();
            }

            return UniqueExpected(matches, constructors.Select(LegacySignatureFormatter.Constructor));
        }

        if (api.Contains(" / ", StringComparison.Ordinal))
        {
            return new ExpectedSymbolResolution(
                null,
                "unsupported-legacy-signature",
                api,
                "The row combines multiple APIs; no single declaration ID can be proven.");
        }

        string memberName = ExtractMemberName(api);
        ISymbol[] candidates = EnumerateMembersInHierarchy(wpfType)
            .Where(ApiVisibility.IsContractMember)
            .Where(member => member.Name.Equals(memberName, StringComparison.Ordinal)
                || member.MetadataName.Equals(MethodMetadataName(memberName), StringComparison.Ordinal))
            .ToArray();
        ISymbol[] displayMatches = candidates.Where(member => MatchesLegacySignature(member, wpfSignature)).ToArray();
        if (displayMatches.Length == 1)
        {
            return new ExpectedSymbolResolution(displayMatches[0], "resolved", string.Empty, string.Empty);
        }

        // Some legacy rows contain the declaring type in the API column instead of the
        // actual member name. The signature is still authoritative, so use it as a
        // fallback only when the label found no candidates at all. A non-unique match
        // remains explicitly ambiguous rather than being guessed.
        if (candidates.Length == 0)
        {
            ISymbol[] signatureMatches = EnumerateMembersInHierarchy(wpfType)
                .Where(ApiVisibility.IsContractMember)
                .Where(member => MatchesLegacySignature(member, wpfSignature))
                .ToArray();
            if (signatureMatches.Length == 1)
            {
                return new ExpectedSymbolResolution(
                    signatureMatches[0],
                    "resolved",
                    string.Empty,
                    string.Empty);
            }

            if (signatureMatches.Length > 1)
            {
                return new ExpectedSymbolResolution(
                    null,
                    "ambiguous-wpf-api",
                    JoinLimited(signatureMatches.Select(DisplaySymbol)),
                    "The legacy signature matched multiple WPF declarations; no pass was inferred.");
            }
        }

        if (candidates.Length == 1)
        {
            return new ExpectedSymbolResolution(candidates[0], "resolved", string.Empty, string.Empty);
        }

        int count = ParseParameterCount(wpfSignature);
        ISymbol[] countMatches = candidates
            .Where(symbol => ParameterCount(symbol) == count)
            .ToArray();
        if (countMatches.Length == 1)
        {
            return new ExpectedSymbolResolution(countMatches[0], "resolved", string.Empty, string.Empty);
        }

        return new ExpectedSymbolResolution(
            null,
            candidates.Length > 1 ? "ambiguous-wpf-api" : "unsupported-legacy-signature",
            JoinLimited(candidates.Select(DisplaySymbol)),
            $"Could not resolve API label '{api}' to exactly one WPF symbol; no pass was inferred.");
    }

    private TargetSymbolResolution ResolveTargetSymbol(
        ISymbol expected,
        INamedTypeSymbol targetType,
        string differenceKind)
    {
        if (expected is INamedTypeSymbol)
        {
            return new TargetSymbolResolution(targetType, "resolved", string.Empty, string.Empty);
        }

        ISymbol[] candidates;
        if (expected is IMethodSymbol expectedMethod && expectedMethod.MethodKind == MethodKind.Constructor)
        {
            candidates = targetType.InstanceConstructors.Cast<ISymbol>().ToArray();
        }
        else
        {
            candidates = EnumerateMembersInHierarchy(targetType)
                .Where(member => member.MetadataName.Equals(expected.MetadataName, StringComparison.Ordinal))
                .Where(member => SameSymbolCategory(expected, member))
                .ToArray();
            if (candidates.Length == 0 && differenceKind == "kind-mismatch")
            {
                candidates = EnumerateMembersInHierarchy(targetType)
                    .Where(member => member.Name.Equals(expected.Name, StringComparison.Ordinal))
                    .ToArray();
            }
        }

        if (candidates.Length == 0)
        {
            return new TargetSymbolResolution(
                null,
                "member-missing",
                string.Empty,
                "No same-name/same-kind Jalium member was found; the facet cannot be resolved.");
        }

        if (candidates.Length == 1)
        {
            return new TargetSymbolResolution(candidates[0], "resolved", string.Empty, string.Empty);
        }

        var scored = candidates
            .Select(candidate => new
            {
                Symbol = candidate,
                Score = CandidateScore(expected, candidate),
            })
            .OrderByDescending(static candidate => candidate.Score)
            .ToArray();
        if (scored.Length > 1 && scored[0].Score == scored[1].Score)
        {
            return new TargetSymbolResolution(
                null,
                "ambiguous-jalium-api",
                JoinLimited(scored.Select(candidate => DisplaySymbol(candidate.Symbol))),
                "Multiple Jalium candidates have the same structural score; no pass was inferred.");
        }

        return new TargetSymbolResolution(scored[0].Symbol, "resolved", string.Empty, string.Empty);
    }

    private int CandidateScore(ISymbol expected, ISymbol candidate)
    {
        int score = SameSymbolCategory(expected, candidate) ? 100 : 0;
        if (expected.IsStatic == candidate.IsStatic)
        {
            score += 5;
        }

        if (expected is IMethodSymbol expectedMethod && candidate is IMethodSymbol candidateMethod)
        {
            if (expectedMethod.MethodKind == candidateMethod.MethodKind)
            {
                score += 40;
            }

            if (expectedMethod.Arity == candidateMethod.Arity)
            {
                score += 30;
            }

            if (expectedMethod.Parameters.Length == candidateMethod.Parameters.Length)
            {
                score += 20;
            }

            int common = Math.Min(expectedMethod.Parameters.Length, candidateMethod.Parameters.Length);
            for (int index = 0; index < common; index++)
            {
                string expectedType = ContractKey.ParameterIdentity(
                    expectedMethod.Parameters[index],
                    _mapper,
                    _wpfAssemblyNames);
                string actualType = ContractKey.ParameterIdentity(
                    candidateMethod.Parameters[index],
                    _mapper,
                    _noMappedAssemblies);
                if (expectedType == actualType)
                {
                    score += 8;
                }

                if (expectedMethod.Parameters[index].Name == candidateMethod.Parameters[index].Name)
                {
                    score += 2;
                }
            }
        }

        return score;
    }

    private TypeResolution ResolveWpfType(string namespaceName, string legacyType)
    {
        string metadataName = MetadataNames.FromLegacyTypeName(legacyType);
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
            return new TypeResolution(
                type,
                authoritativeNamespace,
                metadataName,
                _mapper.MapType(authoritativeNamespace, metadataName),
                "resolved",
                string.Empty);
        }

        return new TypeResolution(
            null,
            namespaceName,
            metadataName,
            _mapper.MapType(namespaceName, metadataName),
            visible.Length > 1
                ? "ambiguous-wpf-api"
                : all.Count == 1 ? "legacy-out-of-scope" : "legacy-input-invalid",
            LegacyVerifier.DescribeTypes(visible.Length > 0 ? visible : all));
    }

    private TargetTypeResolution ResolveTargetType(INamedTypeSymbol wpfType)
    {
        string namespaceName = wpfType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : wpfType.ContainingNamespace.ToDisplayString();
        string expected = _mapper.MapType(namespaceName, MetadataNames.NestedMetadataName(wpfType));
        IReadOnlyList<INamedTypeSymbol> allExact = _jalium.FindTypes(expected);
        INamedTypeSymbol[] exact = allExact.Where(ApiVisibility.IsExternallyVisible).ToArray();
        if (exact.Length == 1)
        {
            return new TargetTypeResolution(exact[0], "resolved", string.Empty);
        }

        if (exact.Length > 1)
        {
            return new TargetTypeResolution(null, "ambiguous-jalium-api", LegacyVerifier.DescribeTypes(exact));
        }

        if (allExact.Count > 0)
        {
            return new TargetTypeResolution(null, "accessibility-mismatch", LegacyVerifier.DescribeTypes(allExact));
        }

        INamedTypeSymbol[] moved = _jalium.FindTypesBySimpleName(wpfType.MetadataName)
            .Where(ApiVisibility.IsExternallyVisible)
            .ToArray();
        return new TargetTypeResolution(
            null,
            moved.Length > 0 ? "namespace-mismatch" : "declaring-type-missing",
            LegacyVerifier.DescribeTypes(moved));
    }

    private string AccessibilityKey(ISymbol symbol)
        => symbol switch
        {
            IPropertySymbol property => string.Join(
                "|",
                property.DeclaredAccessibility,
                ContractKey.Accessor(property.GetMethod),
                ContractKey.Accessor(property.SetMethod)),
            IEventSymbol eventSymbol => string.Join(
                "|",
                eventSymbol.DeclaredAccessibility,
                ContractKey.Accessor(eventSymbol.AddMethod),
                ContractKey.Accessor(eventSymbol.RemoveMethod)),
            _ => symbol.DeclaredAccessibility.ToString(),
        };

    private static bool? StaticValue(ISymbol symbol)
        => symbol switch
        {
            INamedTypeSymbol type => type.IsStatic,
            IMethodSymbol method => method.IsStatic,
            IPropertySymbol property => property.IsStatic,
            IEventSymbol eventSymbol => eventSymbol.IsStatic,
            IFieldSymbol field => field.IsStatic,
            _ => null,
        };

    private string? ReturnTypeKey(ISymbol symbol, bool mapWpf)
    {
        ISet<string> assemblies = mapWpf ? _wpfAssemblyNames : _noMappedAssemblies;
        return symbol switch
        {
            IMethodSymbol method => method.RefKind + "|" + TypeContractKey.Create(method.ReturnType, _mapper, assemblies),
            IPropertySymbol property => property.RefKind + "|" + TypeContractKey.Create(property.Type, _mapper, assemblies),
            IEventSymbol eventSymbol => TypeContractKey.Create(eventSymbol.Type, _mapper, assemblies),
            IFieldSymbol field => TypeContractKey.Create(field.Type, _mapper, assemblies),
            _ => null,
        };
    }

    private static int? ParameterCount(ISymbol symbol)
        => Parameters(symbol)?.Length;

    private string? ParameterTypesKey(ISymbol symbol, bool mapWpf)
    {
        IParameterSymbol[]? parameters = Parameters(symbol);
        if (parameters is null)
        {
            return null;
        }

        ISet<string> assemblies = mapWpf ? _wpfAssemblyNames : _noMappedAssemblies;
        return string.Join(
            ";",
            parameters.Select(parameter => ContractKey.ParameterIdentity(parameter, _mapper, assemblies)));
    }

    private string? ParameterOrderKey(ISymbol symbol, bool mapWpf)
    {
        IParameterSymbol[]? parameters = Parameters(symbol);
        if (parameters is null)
        {
            return null;
        }

        ISet<string> assemblies = mapWpf ? _wpfAssemblyNames : _noMappedAssemblies;
        return string.Join(
            ";",
            parameters.Select(parameter =>
                ContractKey.ParameterIdentity(parameter, _mapper, assemblies) + "|" + parameter.Name));
    }

    private string FullShapeKey(ISymbol symbol, bool mapWpf)
    {
        ISet<string> assemblies = mapWpf ? _wpfAssemblyNames : _noMappedAssemblies;
        return symbol switch
        {
            INamedTypeSymbol type => TypeShapeKey(type, assemblies),
            IMethodSymbol method => "method:" + MethodContract.Create(method, _mapper, assemblies).FullKey,
            IPropertySymbol property => "property:" + PropertyContract.Create(property, _mapper, assemblies).FullKey,
            IEventSymbol eventSymbol => "event:" + EventContract.Create(eventSymbol, _mapper, assemblies).FullKey,
            IFieldSymbol field => "field:" + FieldContract.Create(field, _mapper, assemblies).FullKey,
            _ => symbol.Kind + ":unsupported",
        };
    }

    private TypeShape CreateTypeShape(INamedTypeSymbol type, ISet<string> mappedAssemblies)
    {
        string baseType = type.BaseType is null
            ? "-"
            : TypeContractKey.Create(type.BaseType, _mapper, mappedAssemblies);
        string[] interfaces = type.AllInterfaces
                .Where(ApiVisibility.IsExternallyVisible)
                .Select(interfaceType => TypeContractKey.Create(interfaceType, _mapper, mappedAssemblies))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        string[] constraints = type.TypeParameters
            .Select(parameter => ContractKey.TypeParameterConstraint(
                parameter,
                _mapper,
                mappedAssemblies))
            .ToArray();
        return new TypeShape(
            type.TypeKind,
            type.Arity,
            type.IsStatic,
            type.IsAbstract,
            type.IsSealed,
            baseType,
            interfaces,
            constraints);
    }

    private string TypeShapeKey(INamedTypeSymbol type, ISet<string> mappedAssemblies) =>
        CreateTypeShape(type, mappedAssemblies).Key;

    internal string TypeShapeKeyForTest(INamedTypeSymbol type, bool mapWpf) =>
        TypeShapeKey(type, mapWpf ? _wpfAssemblyNames : _noMappedAssemblies);

    internal string EvaluateFacetStatusForTest(
        string kind,
        ISymbol expected,
        ISymbol actual,
        string legacyWpfSignature) =>
        EvaluateFacet(kind, expected, actual, legacyWpfSignature).Status;

    internal string EvaluateFacetDiagnosticForTest(
        string kind,
        ISymbol expected,
        ISymbol actual,
        string legacyWpfSignature) =>
        EvaluateFacet(kind, expected, actual, legacyWpfSignature).Diagnostic;

    internal static string? ValidateLegacyMetadataClaimStatusForTest(
        string differenceKind,
        ISymbol authoritativeWpfSymbol,
        string legacyWpfSignature) =>
        ValidateLegacyMetadataClaim(differenceKind, authoritativeWpfSymbol, legacyWpfSignature)?.Status;

    private static IParameterSymbol[]? Parameters(ISymbol symbol)
        => symbol switch
        {
            IMethodSymbol method => method.Parameters.ToArray(),
            IPropertySymbol property => property.Parameters.ToArray(),
            _ => null,
        };

    private static ITypeParameterSymbol[] TypeParameters(ISymbol symbol)
        => symbol switch
        {
            IMethodSymbol method => method.TypeParameters.ToArray(),
            INamedTypeSymbol type => type.TypeParameters.ToArray(),
            _ => [],
        };

    private sealed record TypeShape(
        TypeKind Kind,
        int Arity,
        bool IsStatic,
        bool IsAbstract,
        bool IsSealed,
        string BaseType,
        string[] Interfaces,
        string[] Constraints)
    {
        internal string Key =>
            $"kind={Kind};arity={Arity};static={IsStatic};abstract={IsAbstract};sealed={IsSealed};" +
            $"base={BaseType};interfaces=[{string.Join(",", Interfaces)}];constraints=[{string.Join(";", Constraints)}]";
    }

    private static bool IsTypeLevel(string legacyType, string api, string kind)
    {
        string baseName = legacyType.Split('<', 2)[0];
        bool typeFacet = kind is "kind-mismatch" or "static" or "generic-constraint";
        return typeFacet && (api.Equals(baseName, StringComparison.Ordinal)
                || api.StartsWith(baseName + " (", StringComparison.Ordinal)
                || api.StartsWith(baseName + " :", StringComparison.Ordinal))
            || api.Contains("type declaration", StringComparison.OrdinalIgnoreCase)
            || api.Contains("declaration", StringComparison.OrdinalIgnoreCase)
            || api.Contains("base class", StringComparison.OrdinalIgnoreCase)
            || api.Contains("class modifier", StringComparison.OrdinalIgnoreCase)
            || kind == "generic-constraint" && api.Contains('<');
    }

    private static bool IsConstructor(string legacyType, string api)
    {
        string baseName = legacyType.Split('<', 2)[0];
        return api.Equals("(constructor)", StringComparison.OrdinalIgnoreCase)
            || api.Equals(".ctor", StringComparison.Ordinal)
            || api.StartsWith(baseName + "(", StringComparison.Ordinal);
    }

    private static string ExtractMemberName(string api)
    {
        string value = api.Trim();
        int annotation = value.IndexOf(" (", StringComparison.Ordinal);
        if (annotation >= 0)
        {
            value = value[..annotation];
        }

        int parameters = value.IndexOf('(');
        if (parameters >= 0)
        {
            value = value[..parameters];
        }

        int dot = value.LastIndexOf('.');
        if (dot >= 0)
        {
            value = value[(dot + 1)..];
        }

        return value.Trim();
    }

    private static string MethodMetadataName(string value)
        => value.StartsWith("operator ", StringComparison.Ordinal)
            ? value[9..].Trim() switch
            {
                "==" => "op_Equality",
                "!=" => "op_Inequality",
                "<=" => "op_LessThanOrEqual",
                ">=" => "op_GreaterThanOrEqual",
                "<" => "op_LessThan",
                ">" => "op_GreaterThan",
                "+" => "op_Addition",
                "-" => "op_Subtraction",
                "*" => "op_Multiply",
                "/" => "op_Division",
                string token => "operator " + token,
            }
            : value;

    private static bool MatchesLegacySignature(ISymbol symbol, string signature)
        => symbol switch
        {
            IMethodSymbol method when method.MethodKind == MethodKind.Constructor
                => LegacySignatureFormatter.MatchesConstructor(method, signature),
            IMethodSymbol method => LegacyMemberFormatter.Matches(method, signature),
            IPropertySymbol property => LegacyMemberFormatter.Matches(property, signature),
            IEventSymbol eventSymbol => LegacyMemberFormatter.Matches(eventSymbol, signature),
            IFieldSymbol field => LegacyMemberFormatter.Matches(field, signature),
            _ => false,
        };

    private static ExpectedSymbolResolution UniqueExpected<T>(
        IReadOnlyList<T> matches,
        IEnumerable<string> candidates)
        where T : class, ISymbol
        => matches.Count == 1
            ? new ExpectedSymbolResolution(matches[0], "resolved", string.Empty, string.Empty)
            : new ExpectedSymbolResolution(
                null,
                matches.Count > 1 ? "ambiguous-wpf-api" : "unsupported-legacy-signature",
                JoinLimited(candidates),
                $"The legacy signature resolved to {matches.Count} WPF declarations; no pass was inferred.");

    private static bool SameSymbolCategory(ISymbol left, ISymbol right)
        => left switch
        {
            IMethodSymbol => right is IMethodSymbol,
            IPropertySymbol => right is IPropertySymbol,
            IEventSymbol => right is IEventSymbol,
            IFieldSymbol => right is IFieldSymbol,
            INamedTypeSymbol => right is INamedTypeSymbol,
            _ => left.Kind == right.Kind,
        };

    private static IEnumerable<ISymbol> EnumerateMembersInHierarchy(INamedTypeSymbol type)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (seen.Add(current))
            {
                foreach (ISymbol member in current.GetMembers())
                {
                    yield return member;
                }
            }
        }

        foreach (INamedTypeSymbol interfaceType in type.AllInterfaces)
        {
            if (seen.Add(interfaceType))
            {
                foreach (ISymbol member in interfaceType.GetMembers())
                {
                    yield return member;
                }
            }
        }
    }

    private static int ParseParameterCount(string signature)
    {
        int open = signature.IndexOf('(');
        if (open < 0)
        {
            return -1;
        }

        int depth = 0;
        int angle = 0;
        int brackets = 0;
        int count = 0;
        bool hasContent = false;
        for (int index = open + 1; index < signature.Length; index++)
        {
            char ch = signature[index];
            switch (ch)
            {
                case '<': angle++; break;
                case '>': angle--; break;
                case '[': brackets++; break;
                case ']': brackets--; break;
                case '(':
                    depth++;
                    break;
                case ')' when depth == 0:
                    return hasContent ? count + 1 : 0;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0 && angle == 0 && brackets == 0:
                    count++;
                    break;
                default:
                    if (!char.IsWhiteSpace(ch))
                    {
                        hasContent = true;
                    }
                    break;
            }
        }

        return -1;
    }

    private static string DisplaySymbol(ISymbol symbol)
        => symbol switch
        {
            INamedTypeSymbol type => LegacyVerifier.DescribeType(type),
            IMethodSymbol method when method.MethodKind == MethodKind.Constructor
                => LegacySignatureFormatter.Constructor(method),
            IMethodSymbol method => LegacyMemberFormatter.Method(method),
            IPropertySymbol property => LegacyMemberFormatter.Property(property),
            IEventSymbol eventSymbol => LegacyMemberFormatter.Event(eventSymbol),
            IFieldSymbol field => LegacyMemberFormatter.Field(field),
            _ => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
        };

    private static string JoinLimited(IEnumerable<string> values)
        => string.Join(" || ", values.Where(static value => value.Length > 0).Take(8));

    private sealed record TypeResolution(
        INamedTypeSymbol? Type,
        string Namespace,
        string MetadataName,
        string ExpectedTarget,
        string Status,
        string Actual);

    private sealed record ExpectedSymbolResolution(
        ISymbol? Symbol,
        string Status,
        string Actual,
        string Diagnostic);

    private sealed record TargetTypeResolution(
        INamedTypeSymbol? Type,
        string Status,
        string Actual);

    private sealed record TargetSymbolResolution(
        ISymbol? Symbol,
        string Status,
        string Actual,
        string Diagnostic);

    private sealed record FacetEvaluation(string Status, string Diagnostic);
}
