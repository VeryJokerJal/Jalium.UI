extern alias sg;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using sg::Jalium.UI.Xaml.SourceGenerator;

namespace Jalium.UI.Tests;

public sealed class AotTypeRegistrySourceGeneratorTests
{
    [Fact]
    public void AssemblyMetadata_RegistersOneLazyProvider()
    {
        const string source = """
            public sealed class GeneratedCatalogViewModel
            {
                public string Title { get; set; } = "AOT";
            }
            """;

        var compilation = CSharpCompilation.Create(
            assemblyName: $"GeneratedCatalog_{Guid.NewGuid():N}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        IIncrementalGenerator generator = new JalxamlSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var runResult = driver.GetRunResult();
        var generatedSource = runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .Single(static result =>
                result.HintName == "__JaliumGeneratedAotMetadata.g.cs")
            .SourceText
            .ToString();

        Assert.Contains(
            "AotTypeRegistry.RegisterAssembly(typeof(__JaliumGeneratedAotMetadata_",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(", ProvideTypes);", generatedSource, StringComparison.Ordinal);
        Assert.Contains("private static void ProvideTypes()", generatedSource, StringComparison.Ordinal);
        Assert.Contains(
            "AotTypeRegistry.Register(typeof(global::GeneratedCatalogViewModel));",
            generatedSource,
            StringComparison.Ordinal);

        var moduleInitializerStart = generatedSource.IndexOf(
            "internal static void Register()",
            StringComparison.Ordinal);
        var providerStart = generatedSource.IndexOf(
            "private static void ProvideTypes()",
            StringComparison.Ordinal);
        var moduleInitializer = generatedSource.Substring(
            moduleInitializerStart,
            providerStart - moduleInitializerStart);

        Assert.DoesNotContain(
            "AotTypeRegistry.Register(typeof(",
            moduleInitializer,
            StringComparison.Ordinal);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException(
                "The runtime did not expose trusted platform assemblies.");

        var paths = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(AotTypeRegistry).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths.Select(static path => MetadataReference.CreateFromFile(path));
    }
}
