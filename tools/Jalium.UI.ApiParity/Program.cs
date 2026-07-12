using System.Text;

namespace Jalium.UI.ApiParity;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            CliOptions options = CliOptions.Parse(args);
            string repositoryRoot = options.RepositoryRoot ?? FindRepositoryRoot();
            string namespaceMapPath = options.NamespaceMapPath
                ?? Path.Combine(repositoryRoot, "tools", "Jalium.UI.ApiParity", "namespace-map.json");
            NamespaceMapper mapper = NamespaceMapper.Load(namespaceMapPath);
            ReferencePack pack = ReferencePackLocator.Locate();

            Console.WriteLine($"WPF ref pack: {pack.Version} ({pack.DesktopReferenceDirectory})");
            if (options.Command == "self-test")
            {
                SelfTests.Run(pack, mapper);
                Console.WriteLine(
                    "SELF-TEST PASS: stable/generic IDs, member and constructor contracts, "
                    + "external WindowsDesktop contract mapping, exact enum values, namespace mapping, "
                    + "and ambiguity detection.");
                return 0;
            }

            if (options.Command != "verify-legacy")
            {
                Console.Error.WriteLine($"Unknown command '{options.Command}'. Expected verify-legacy or self-test.");
                return 64;
            }

            CompilationBundle wpfBundle = CompilationFactory.CreateWpf(pack);
            CompilationBundle jaliumBundle = CompilationFactory.CreateJalium(pack, AppContext.BaseDirectory);
            ApiSymbolIndex wpfIndex = ApiSymbolIndex.Build(wpfBundle.Assemblies);
            ApiSymbolIndex jaliumIndex = ApiSymbolIndex.Build(jaliumBundle.Assemblies);
            PrintIndexSummary("WPF", wpfIndex);
            PrintIndexSummary("Jalium", jaliumIndex);

            string csvDirectory = Path.Combine(repositoryRoot, "docs", "wpf-parity-gap", "csv");
            var verifier = new LegacyVerifier(
                wpfIndex,
                jaliumIndex,
                mapper,
                wpfBundle.Assemblies,
                pack.DesktopReferenceAssemblyNames);
            IReadOnlyList<LegacyValidationResult> results = verifier.Verify(csvDirectory);
            string outputDirectory = options.OutputDirectory
                ?? Path.Combine(repositoryRoot, "tools", "Jalium.UI.ApiParity", "artifacts");
            LegacyResultWriter.Write(outputDirectory, results);
            PrintValidationSummary(results, outputDirectory);

            int unresolved = results.Count(result => result.Status is
                "unresolved-wpf-api" or "legacy-input-mismatch");
            if (unresolved > 0)
            {
                Console.Error.WriteLine($"Validation completed with {unresolved} unresolved or ambiguous rows.");
                return 2;
            }

            if (options.FailOnGap && results.Any(static result => !IsSatisfied(result.Status)))
            {
                Console.Error.WriteLine("At least one tracked legacy gap remains unresolved.");
                return 1;
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 70;
        }
    }

    private static bool IsSatisfied(string status)
        => status is "present" or "resolved";

    private static void PrintIndexSummary(string label, ApiSymbolIndex index)
    {
        string counts = string.Join(
            ", ",
            index.Entries
                .GroupBy(static entry => entry.Kind)
                .OrderBy(static group => group.Key)
                .Select(static group => $"{group.Key}={group.Count()}"));
        Console.WriteLine($"{label} external-contract API index: total={index.Entries.Count}; {counts}");
    }

    private static void PrintValidationSummary(
        IReadOnlyList<LegacyValidationResult> results,
        string outputDirectory)
    {
        Console.WriteLine($"Legacy rows evaluated: {results.Count}");
        foreach (var category in results
            .GroupBy(static result => result.Category)
            .OrderBy(static group => group.Key))
        {
            string statuses = string.Join(
                ", ",
                category.GroupBy(static result => result.Status)
                    .OrderBy(static group => group.Key)
                    .Select(static group => $"{group.Key}={group.Count()}"));
            Console.WriteLine($"  {category.Key}: {statuses}");
        }

        Console.WriteLine($"JSONL: {Path.Combine(outputDirectory, "legacy-validation.jsonl")}");
        Console.WriteLine($"CSV:   {Path.Combine(outputDirectory, "legacy-validation.csv")}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Jalium.UI.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find Jalium.UI.slnx above the current working directory. Use --repo-root.");
    }
}

internal sealed record CliOptions(
    string Command,
    string? RepositoryRoot,
    string? OutputDirectory,
    string? NamespaceMapPath,
    bool FailOnGap)
{
    public static CliOptions Parse(string[] args)
    {
        string command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : "verify-legacy";
        int index = command == "verify-legacy" && (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
            ? 0
            : 1;
        string? repositoryRoot = null;
        string? outputDirectory = null;
        string? namespaceMapPath = null;
        bool failOnGap = false;

        while (index < args.Length)
        {
            string argument = args[index++];
            switch (argument)
            {
                case "--repo-root":
                    repositoryRoot = RequireValue(args, ref index, argument);
                    break;
                case "--output":
                    outputDirectory = RequireValue(args, ref index, argument);
                    break;
                case "--namespace-map":
                    namespaceMapPath = RequireValue(args, ref index, argument);
                    break;
                case "--fail-on-gap":
                    failOnGap = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{argument}'.");
            }
        }

        return new CliOptions(command, repositoryRoot, outputDirectory, namespaceMapPath, failOnGap);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index >= args.Length)
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return Path.GetFullPath(args[index++]);
    }
}
