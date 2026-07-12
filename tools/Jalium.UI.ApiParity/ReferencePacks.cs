using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Jalium.UI.ApiParity;

internal sealed record ReferencePack(
    string Version,
    string DesktopReferenceDirectory,
    string CoreReferenceDirectory,
    IReadOnlyDictionary<string, string> WpfAssemblyPaths,
    IReadOnlySet<string> DesktopReferenceAssemblyNames);

internal static class ReferencePackLocator
{
    private const string TargetFramework = "net10.0";

    private static readonly string[] WpfAssemblyNames =
    [
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "System.Xaml",
    ];

    public static ReferencePack Locate()
    {
        string dotnetRoot = FindDotnetRoot();
        string desktopPackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.WindowsDesktop.App.Ref");
        (string desktopVersion, string desktopRefDirectory) =
            FindHighestReferenceDirectory(desktopPackRoot, preferredVersion: null);

        string corePackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        (_, string coreRefDirectory) =
            FindHighestReferenceDirectory(corePackRoot, desktopVersion);

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string assemblyName in WpfAssemblyNames)
        {
            string path = Path.Combine(desktopRefDirectory, assemblyName + ".dll");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"WPF reference assembly '{assemblyName}' was not found in '{desktopRefDirectory}'.",
                    path);
            }

            paths.Add(assemblyName, path);
        }

        HashSet<string> desktopAssemblyNames = Directory
            .EnumerateFiles(desktopRefDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => !string.IsNullOrEmpty(name))
            .Select(static name => name!)
            .ToHashSet(StringComparer.Ordinal);

        return new ReferencePack(
            desktopVersion,
            desktopRefDirectory,
            coreRefDirectory,
            paths,
            desktopAssemblyNames);
    }

    private static string FindDotnetRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string standard = Path.Combine(programFiles, "dotnet");
        if (Directory.Exists(standard))
        {
            return standard;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the dotnet packs directory. Set DOTNET_ROOT explicitly.");
    }

    private static (string Version, string ReferenceDirectory) FindHighestReferenceDirectory(
        string packRoot,
        string? preferredVersion)
    {
        if (!Directory.Exists(packRoot))
        {
            throw new DirectoryNotFoundException($"Reference pack root '{packRoot}' does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            string preferred = Path.Combine(packRoot, preferredVersion, "ref", TargetFramework);
            if (Directory.Exists(preferred))
            {
                return (preferredVersion, preferred);
            }
        }

        var candidates = Directory.EnumerateDirectories(packRoot)
            .Select(static path => new
            {
                Path = path,
                Name = Path.GetFileName(path),
            })
            .Select(static candidate => new
            {
                candidate.Path,
                candidate.Name,
                Version = ParseVersion(candidate.Name),
            })
            .Where(static candidate => candidate.Version is not null)
            .Where(candidate => Directory.Exists(Path.Combine(candidate.Path, "ref", TargetFramework)))
            .OrderByDescending(static candidate => candidate.Version)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new DirectoryNotFoundException(
                $"No {TargetFramework} reference directory was found below '{packRoot}'.");
        }

        var selected = candidates[0];
        return (selected.Name, Path.Combine(selected.Path, "ref", TargetFramework));
    }

    private static Version? ParseVersion(string value)
    {
        int suffix = value.IndexOf('-');
        string numeric = suffix >= 0 ? value[..suffix] : value;
        return Version.TryParse(numeric, out Version? version) ? version : null;
    }
}

internal sealed record CompilationBundle(
    CSharpCompilation Compilation,
    IReadOnlyList<IAssemblySymbol> Assemblies);

internal static class CompilationFactory
{
    private static readonly string[] JaliumRuntimeAssemblyNames =
    [
        "Jalium.UI.Managed",
        "Jalium.UI.Gpu",
        "Jalium.UI.Xaml",
    ];

    public static CompilationBundle CreateWpf(ReferencePack pack)
    {
        var references = new List<MetadataReference>();
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // WindowsBase also has a facade in Microsoft.NETCore.App.Ref. Prefer the
        // WindowsDesktop reference set so the four WPF owner assemblies resolve
        // to their authoritative metadata rather than to a same-named facade.
        AddDirectoryReferences(pack.DesktopReferenceDirectory, references, seenFileNames);
        AddDirectoryReferences(pack.CoreReferenceDirectory, references, seenFileNames);

        var compilation = CreateEmptyCompilation("JaliumApiParity.Wpf", references);
        var assemblies = new List<IAssemblySymbol>();
        foreach ((string assemblyName, string path) in pack.WpfAssemblyPaths)
        {
            MetadataReference reference = references.Single(item =>
                string.Equals(item.Display, path, StringComparison.OrdinalIgnoreCase));
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                throw new InvalidDataException(
                    $"Roslyn could not read WPF reference assembly '{assemblyName}' at '{path}'.");
            }

            assemblies.Add(assembly);
        }

        return new CompilationBundle(compilation, assemblies);
    }

    public static CompilationBundle CreateJalium(ReferencePack pack, string assemblyDirectory)
    {
        var references = new List<MetadataReference>();
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDirectoryReferences(pack.CoreReferenceDirectory, references, seenFileNames);
        AddDirectoryReferences(assemblyDirectory, references, seenFileNames);

        var compilation = CreateEmptyCompilation("JaliumApiParity.Jalium", references);
        var assemblies = new List<IAssemblySymbol>();
        foreach (string assemblyName in JaliumRuntimeAssemblyNames)
        {
            string fileName = assemblyName + ".dll";
            MetadataReference? reference = references.FirstOrDefault(item =>
                string.Equals(Path.GetFileName(item.Display), fileName, StringComparison.OrdinalIgnoreCase));
            if (reference is null || compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                throw new FileNotFoundException(
                    $"Jalium assembly '{fileName}' was not found or could not be read in '{assemblyDirectory}'.");
            }

            assemblies.Add(assembly);
        }

        return new CompilationBundle(compilation, assemblies);
    }

    public static CSharpCompilation CreateSourceCompilation(
        string assemblyName,
        string source,
        string coreReferenceDirectory)
    {
        var references = new List<MetadataReference>();
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDirectoryReferences(coreReferenceDirectory, references, seenFileNames);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));
        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));
    }

    private static CSharpCompilation CreateEmptyCompilation(
        string assemblyName,
        IReadOnlyList<MetadataReference> references)
    {
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));
    }

    private static void AddDirectoryReferences(
        string directory,
        ICollection<MetadataReference> references,
        ISet<string> seenFileNames)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Metadata reference directory '{directory}' does not exist.");
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.dll").OrderBy(static path => path))
        {
            string fileName = Path.GetFileName(path);
            if (seenFileNames.Add(fileName))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
    }
}
