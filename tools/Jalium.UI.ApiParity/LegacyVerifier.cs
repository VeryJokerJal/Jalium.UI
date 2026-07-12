using Microsoft.CodeAnalysis;

namespace Jalium.UI.ApiParity;

internal sealed class LegacyVerifier
{
    private readonly ApiSymbolIndex _wpf;
    private readonly ApiSymbolIndex _jalium;
    private readonly NamespaceMapper _mapper;
    private readonly HashSet<string> _wpfAssemblyNames;

    public LegacyVerifier(
        ApiSymbolIndex wpf,
        ApiSymbolIndex jalium,
        NamespaceMapper mapper,
        IEnumerable<IAssemblySymbol> wpfAssemblies,
        IEnumerable<string>? mappedContractAssemblyNames = null)
    {
        _wpf = wpf;
        _jalium = jalium;
        _mapper = mapper;
        _wpfAssemblyNames = CreateMappedAssemblyNames(wpfAssemblies, mappedContractAssemblyNames);
    }

    internal static HashSet<string> CreateMappedAssemblyNames(
        IEnumerable<IAssemblySymbol> wpfAssemblies,
        IEnumerable<string>? mappedContractAssemblyNames = null)
    {
        HashSet<string> names = wpfAssemblies
            .Select(static assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (mappedContractAssemblyNames is not null)
        {
            // Public WPF contracts routinely reference types owned by other assemblies in
            // Microsoft.WindowsDesktop.App.Ref (UIAutomationProvider/Types,
            // System.Windows.Input.Manipulations, ReachFramework, Ribbon, and others).
            // Map every WindowsDesktop owner assembly, while deliberately excluding the
            // .NETCore reference set: for example System.Windows.Input.ICommand is owned by
            // System.ObjectModel and must retain its BCL identity.
            names.UnionWith(mappedContractAssemblyNames);
        }
        return names;
    }

    public IReadOnlyList<LegacyValidationResult> Verify(string csvDirectory)
    {
        string typePath = Path.Combine(csvDirectory, "missing_types.csv");
        string constructorPath = Path.Combine(csvDirectory, "missing_ctors.csv");
        var results = new List<LegacyValidationResult>();
        results.AddRange(LegacyCsvReader.Read(typePath).Select(row => VerifyType(row, typePath)));
        results.AddRange(LegacyCsvReader.Read(constructorPath).Select(row => VerifyConstructor(row, constructorPath)));
        var memberVerifier = new LegacyMemberVerifier(
            _wpf,
            _jalium,
            _mapper,
            _wpfAssemblyNames);
        results.AddRange(memberVerifier.Verify(csvDirectory));
        var facetVerifier = new LegacyFacetVerifier(
            _wpf,
            _jalium,
            _mapper,
            _wpfAssemblyNames);
        results.AddRange(facetVerifier.Verify(csvDirectory));
        return results;
    }

    private LegacyValidationResult VerifyType(CsvDataRow row, string sourcePath)
    {
        string namespaceName = row.Get("namespace");
        string legacyTypeName = row.Get("type");
        string metadataName = MetadataNames.FromLegacyTypeName(legacyTypeName);
        string wpfFullName = MetadataNames.Join(namespaceName, metadataName);
        string expectedJaliumType = _mapper.MapType(namespaceName, metadataName);
        IReadOnlyList<INamedTypeSymbol> allWpfCandidates = _wpf.FindTypes(wpfFullName);
        IReadOnlyList<INamedTypeSymbol> wpfCandidates = allWpfCandidates
            .Where(ApiVisibility.IsExternallyVisible)
            .ToArray();

        if (wpfCandidates.Count != 1)
        {
            if (wpfCandidates.Count == 0 && allWpfCandidates.Count == 1)
            {
                INamedTypeSymbol internalType = allWpfCandidates[0];
                string internalApiId = StableIds.ApiId(internalType);
                return CreateResult(
                    row,
                    sourcePath,
                    "missing-type",
                    internalApiId,
                    internalType.ContainingAssembly.Name,
                    namespaceName,
                    legacyTypeName,
                    expectedJaliumType,
                    row.Get("kind") + " " + wpfFullName,
                    "legacy-out-of-scope",
                    DescribeType(internalType),
                    "The legacy row refers to an internal/private WPF type, outside the public/protected API scope.");
            }

            string unresolvedApiId = StableIds.UnresolvedApiId("missing-type", namespaceName, legacyTypeName);
            return CreateResult(
                row,
                sourcePath,
                "missing-type",
                unresolvedApiId,
                string.Empty,
                namespaceName,
                legacyTypeName,
                expectedJaliumType,
                row.Get("kind") + " " + wpfFullName,
                "legacy-input-invalid",
                DescribeTypes(wpfCandidates),
                $"Expected exactly one WPF type, found {wpfCandidates.Count}.");
        }

        INamedTypeSymbol wpfType = wpfCandidates[0];
        string apiId = StableIds.ApiId(wpfType);
        string legacyKind = row.Get("kind");
        string actualWpfKind = TypeKindName(wpfType);
        if (!legacyKind.Equals(actualWpfKind, StringComparison.OrdinalIgnoreCase))
        {
            return CreateResult(
                row,
                sourcePath,
                "missing-type",
                apiId,
                wpfType.ContainingAssembly.Name,
                namespaceName,
                legacyTypeName,
                expectedJaliumType,
                actualWpfKind + " " + wpfFullName,
                "legacy-input-mismatch",
                actualWpfKind,
                $"CSV kind is '{legacyKind}', WPF metadata kind is '{actualWpfKind}'.");
        }

        IReadOnlyList<INamedTypeSymbol> exactCandidates = _jalium.FindTypes(expectedJaliumType);
        INamedTypeSymbol[] exactVisible = exactCandidates
            .Where(ApiVisibility.IsExternallyVisible)
            .ToArray();
        string status;
        string actual;
        string diagnostic;
        if (exactVisible.Length > 1)
        {
            status = "ambiguous-jalium-api";
            actual = DescribeTypes(exactVisible);
            diagnostic = "Multiple externally visible Jalium types have the expected metadata name.";
        }
        else if (exactVisible.Length == 1)
        {
            INamedTypeSymbol target = exactVisible[0];
            string targetKind = TypeKindName(target);
            status = targetKind.Equals(actualWpfKind, StringComparison.Ordinal)
                ? "present"
                : "kind-mismatch";
            actual = DescribeType(target);
            diagnostic = status == "present"
                ? "The mapped Jalium type is externally visible with the expected kind."
                : $"Expected kind '{actualWpfKind}', found '{targetKind}'.";
        }
        else if (exactCandidates.Count > 0)
        {
            status = "accessibility-mismatch";
            actual = DescribeTypes(exactCandidates);
            diagnostic = "The mapped Jalium type exists but is not public/protected API.";
        }
        else
        {
            INamedTypeSymbol[] moved = _jalium.FindTypesBySimpleName(metadataName)
                .Where(ApiVisibility.IsExternallyVisible)
                .ToArray();
            status = moved.Length > 0 ? "namespace-mismatch" : "missing";
            actual = DescribeTypes(moved);
            diagnostic = moved.Length > 0
                ? "A same-name Jalium type exists outside the mapped namespace."
                : "No externally visible same-name Jalium type was found.";
        }

        return CreateResult(
            row,
            sourcePath,
            "missing-type",
            apiId,
            wpfType.ContainingAssembly.Name,
            namespaceName,
            legacyTypeName,
            expectedJaliumType,
            actualWpfKind + " " + wpfFullName,
            status,
            actual,
            diagnostic);
    }

    private LegacyValidationResult VerifyConstructor(CsvDataRow row, string sourcePath)
    {
        string namespaceName = row.Get("namespace");
        string legacyTypeName = row.Get("type");
        string metadataName = MetadataNames.FromLegacyTypeName(legacyTypeName);
        string wpfFullName = MetadataNames.Join(namespaceName, metadataName);
        string expectedJaliumType = _mapper.MapType(namespaceName, metadataName);
        string legacySignature = LegacySignatureFormatter.Normalize(row.Get("wpf_ctor_sig"));
        INamedTypeSymbol[] wpfTypes = (namespaceName == "?"
                ? _wpf.FindTypesBySimpleName(metadataName)
                : _wpf.FindTypes(wpfFullName))
            .Where(ApiVisibility.IsExternallyVisible)
            .ToArray();

        if (wpfTypes.Length != 1)
        {
            string unresolvedApiId = StableIds.UnresolvedApiId(
                "missing-constructor",
                namespaceName,
                legacyTypeName,
                legacySignature);
            return CreateResult(
                row,
                sourcePath,
                "missing-constructor",
                unresolvedApiId,
                string.Empty,
                namespaceName,
                legacyTypeName,
                expectedJaliumType,
                legacySignature,
                "unresolved-wpf-api",
                DescribeTypes(wpfTypes),
                $"Expected exactly one WPF declaring type, found {wpfTypes.Length}.");
        }

        INamedTypeSymbol wpfType = wpfTypes[0];
        namespaceName = wpfType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : wpfType.ContainingNamespace.ToDisplayString();
        expectedJaliumType = _mapper.MapType(namespaceName, metadataName);
        IMethodSymbol[] constructors = wpfType.InstanceConstructors
            .Where(ApiVisibility.IsApiMember)
            .Where(constructor => LegacySignatureFormatter.MatchesConstructor(constructor, legacySignature))
            .ToArray();
        if (constructors.Length != 1)
        {
            string unresolvedApiId = StableIds.UnresolvedApiId(
                "missing-constructor",
                namespaceName,
                legacyTypeName,
                legacySignature);
            string available = string.Join(
                " || ",
                wpfType.InstanceConstructors
                    .Where(ApiVisibility.IsApiMember)
                    .Select(LegacySignatureFormatter.Constructor));
            return CreateResult(
                row,
                sourcePath,
                "missing-constructor",
                unresolvedApiId,
                wpfType.ContainingAssembly.Name,
                namespaceName,
                legacyTypeName,
                expectedJaliumType,
                legacySignature,
                "unresolved-wpf-api",
                available,
                $"Expected exactly one WPF constructor signature match, found {constructors.Length}.");
        }

        IMethodSymbol wpfConstructor = constructors[0];
        string apiId = StableIds.ApiId(wpfConstructor);
        INamedTypeSymbol[] targetTypes = _jalium.FindTypes(expectedJaliumType)
            .Where(ApiVisibility.IsExternallyVisible)
            .ToArray();
        if (targetTypes.Length != 1)
        {
            INamedTypeSymbol[] moved = _jalium.FindTypesBySimpleName(metadataName)
                .Where(ApiVisibility.IsExternallyVisible)
                .ToArray();
            string status = targetTypes.Length > 1
                ? "ambiguous-jalium-api"
                : moved.Length > 0
                    ? "namespace-mismatch"
                    : "declaring-type-missing";
            string actual = targetTypes.Length > 1 ? DescribeTypes(targetTypes) : DescribeTypes(moved);
            return CreateResult(
                row,
                sourcePath,
                "missing-constructor",
                apiId,
                wpfConstructor.ContainingAssembly.Name,
                namespaceName,
                legacyTypeName,
                expectedJaliumType,
                legacySignature,
                status,
                actual,
                "The expected Jalium declaring type could not be resolved uniquely.");
        }

        INamedTypeSymbol targetType = targetTypes[0];
        var noMappedAssemblies = new HashSet<string>(StringComparer.Ordinal);
        ConstructorContract expected = ConstructorContract.Create(
            wpfConstructor,
            _mapper,
            _wpfAssemblyNames);
        var targetContracts = targetType.InstanceConstructors
            .Select(constructor => new
            {
                Symbol = constructor,
                Contract = ConstructorContract.Create(constructor, _mapper, noMappedAssemblies),
            })
            .ToArray();
        var sourceMatches = targetContracts
            .Where(candidate => candidate.Contract.SourceKey == expected.SourceKey)
            .ToArray();
        var exact = sourceMatches.FirstOrDefault(candidate =>
            candidate.Contract.Accessibility == expected.Accessibility);

        string resultStatus;
        string resultActual;
        string resultDiagnostic;
        if (exact is not null)
        {
            resultStatus = "present";
            resultActual = LegacySignatureFormatter.Constructor(exact.Symbol);
            resultDiagnostic = "The mapped Jalium constructor matches parameter and accessibility contracts.";
        }
        else if (sourceMatches.Length > 0)
        {
            resultStatus = "accessibility-mismatch";
            resultActual = string.Join(" || ", sourceMatches.Select(candidate =>
                LegacySignatureFormatter.Constructor(candidate.Symbol)));
            resultDiagnostic =
                $"Expected accessibility '{ApiVisibility.Display(expected.Accessibility)}'.";
        }
        else
        {
            var identityMatches = targetContracts
                .Where(candidate => candidate.Contract.IdentityKey == expected.IdentityKey)
                .ToArray();
            resultStatus = identityMatches.Length > 0 ? "contract-mismatch" : "missing";
            resultActual = string.Join(" || ", identityMatches.Select(candidate =>
                LegacySignatureFormatter.Constructor(candidate.Symbol)));
            resultDiagnostic = identityMatches.Length > 0
                ? "A CLR-equivalent overload exists, but parameter names/modifiers/defaults differ."
                : "No constructor with the mapped WPF parameter identity was found.";
        }

        return CreateResult(
            row,
            sourcePath,
            "missing-constructor",
            apiId,
            wpfConstructor.ContainingAssembly.Name,
            namespaceName,
            legacyTypeName,
            expectedJaliumType,
            legacySignature,
            resultStatus,
            resultActual,
            resultDiagnostic);
    }

    internal static LegacyValidationResult CreateResult(
        CsvDataRow row,
        string sourcePath,
        string category,
        string apiId,
        string wpfAssembly,
        string wpfNamespace,
        string wpfType,
        string expectedJaliumType,
        string expectedSignature,
        string status,
        string actual,
        string diagnostic,
        string? gapFacet = null)
    {
        int tier = 0;
        if (row.Values.TryGetValue("tier", out string? tierText))
        {
            int.TryParse(tierText, out tier);
        }
        return new LegacyValidationResult(
            StableIds.GapId(apiId, gapFacet ?? category),
            apiId,
            category,
            tier,
            wpfAssembly,
            wpfNamespace,
            wpfType,
            expectedJaliumType,
            expectedSignature,
            status,
            actual,
            Path.GetFileName(sourcePath),
            row.SourceRow,
            diagnostic);
    }

    internal static string DescribeTypes(IEnumerable<INamedTypeSymbol> types)
        => string.Join(" || ", types.Select(DescribeType));

    internal static string DescribeType(INamedTypeSymbol type)
        => $"{type.ContainingAssembly.Name}:{MetadataNames.FullName(type)} ({TypeKindName(type)}, "
            + $"{ApiVisibility.Display(type.DeclaredAccessibility)})";

    internal static string TypeKindName(INamedTypeSymbol type)
    {
        return type.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => type.TypeKind.ToString().ToLowerInvariant(),
        };
    }
}
