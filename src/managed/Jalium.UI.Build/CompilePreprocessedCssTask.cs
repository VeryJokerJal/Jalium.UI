using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jalium.UI.Tooling;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Jalium.UI.Build;

/// <summary>Compiles SCSS/Less entries into CSS resources consumed by CompileCssTask.</summary>
public sealed class CompilePreprocessedCssTask : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] StyleSheets { get; set; } = [];
    public ITaskItem[] ExistingCss { get; set; } = [];
    [Required] public string ProjectDirectory { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";
    [Output] public ITaskItem[] GeneratedCss { get; set; } = [];
    [Output] public ITaskItem[] WrittenFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var project = Path.GetFullPath(ProjectDirectory);
            var outputRoot = Path.GetFullPath(OutputDirectory, project);
            var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in ExistingCss) resources.Add(ResourcePath(existing, project));
            var generated = new List<ITaskItem>();
            var written = new List<ITaskItem>();
            foreach (var source in StyleSheets.OrderBy(static x => x.ItemSpec, StringComparer.Ordinal))
            {
                var sourcePath = Path.GetFullPath(source.ItemSpec, project);
                var extension = Path.GetExtension(sourcePath);
                var language = extension.Equals(".scss", StringComparison.OrdinalIgnoreCase)
                    ? CssToolLanguage.Scss : CssToolLanguage.Less;
                var resource = Path.ChangeExtension(ResourcePath(source, project), ".css").Replace('\\', '/');
                if (!resources.Add(resource))
                {
                    Log.LogError("JALCSS004: Duplicate CSS resource '{0}' from '{1}'.", resource, sourcePath);
                    continue;
                }
                var outputPath = Path.GetFullPath(resource.Replace('/', Path.DirectorySeparatorChar), outputRoot);
                if (!outputPath.StartsWith(outputRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"CSS resource path escapes output directory: {resource}");
                var cachePath = outputPath + ".inputs.json";
                DependencyCache? old = null;
                if (File.Exists(cachePath))
                    try { old = JsonSerializer.Deserialize<DependencyCache>(File.ReadAllText(cachePath)); }
                    catch (JsonException) { }
                var currentFingerprint = old is null ? null : Fingerprint(old.Dependencies);
                if (File.Exists(outputPath) && old is not null && currentFingerprint is not null
                    && old.Fingerprint == currentFingerprint)
                {
                    generated.Add(new TaskItem(outputPath, new Dictionary<string, string> { ["ResourcePath"] = resource }));
                    continue;
                }
                var result = CssTooling.CompileAsync(language, sourcePath, File.ReadAllText(sourcePath)).GetAwaiter().GetResult();
                foreach (var diagnostic in result.Diagnostics)
                    Log.LogError(null, "JALCSS004", null, diagnostic.FilePath ?? sourcePath,
                        diagnostic.Line, diagnostic.Column, diagnostic.Line, diagnostic.Column,
                        diagnostic.Message);
                if (!result.Success) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                var dependencies = result.Imports.Append(sourcePath).Where(File.Exists)
                    .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal).ToArray();
                var cache = new DependencyCache(dependencies, Fingerprint(dependencies)
                    ?? throw new FileNotFoundException("CSS tooling or imported file disappeared during preprocessing."));
                WriteIfChanged(outputPath, result.Text!);
                WriteIfChanged(cachePath, JsonSerializer.Serialize(cache));
                generated.Add(new TaskItem(outputPath, new Dictionary<string, string> { ["ResourcePath"] = resource }));
                written.Add(new TaskItem(outputPath));
                written.Add(new TaskItem(cachePath));
            }
            GeneratedCss = generated.ToArray();
            WrittenFiles = written.ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or InvalidOperationException or PlatformNotSupportedException)
        {
            Log.LogError("JALCSS004: CSS preprocessing failed: {0}", exception.Message);
            return false;
        }
    }

    private static string ResourcePath(ITaskItem item, string project)
    {
        var resource = item.GetMetadata("ResourcePath");
        if (string.IsNullOrWhiteSpace(resource)) resource = item.GetMetadata("Link");
        if (string.IsNullOrWhiteSpace(resource)) resource = Path.GetRelativePath(project, Path.GetFullPath(item.ItemSpec, project));
        resource = resource.Replace('\\', '/');
        if (Path.IsPathRooted(resource) || resource.Split('/').Any(static segment => segment is "" or "." or ".."))
            throw new ArgumentException($"Invalid CSS resource path '{resource}'.");
        return resource;
    }

    private static string? Fingerprint(IEnumerable<string> dependencies)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var worker = Path.Combine(Path.GetDirectoryName(typeof(CssTooling).Assembly.Location)!,
            "tools", "css-tooling", "index.cjs");
        var node = Path.Combine(Path.GetDirectoryName(worker)!, "node", CssTooling.HostRid,
            OperatingSystem.IsWindows() ? "node.exe" : "node");
        foreach (var path in dependencies.Append(worker).Append(node).Append(typeof(CssTooling).Assembly.Location))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private sealed record DependencyCache(string[] Dependencies, string Fingerprint);
}
