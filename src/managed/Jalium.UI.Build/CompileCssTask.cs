using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jalium.UI.BuildSupport;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Jalium.UI.Build;

/// <summary>Runs the portable CSS compiler, tracking both file contents and the complete input list.</summary>
public sealed class CompileCssTask : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] StyleSheets { get; set; } = [];
    public ITaskItem[] XamlFiles { get; set; } = [];
    [Required] public string ProjectDirectory { get; set; } = "";
    [Required] public string AssemblyName { get; set; } = "";
    [Required] public string CompilerPath { get; set; } = "";
    [Required] public string OutputFile { get; set; } = "";
    [Output] public ITaskItem[] GeneratedFiles { get; set; } = [];
    [Output] public ITaskItem[] WrittenFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var project = Path.GetFullPath(ProjectDirectory);
            var output = Path.GetFullPath(OutputFile, project);
            var compiler = Path.GetFullPath(CompilerPath, project);
            var manifest = new CssCompilationManifest(AssemblyName, output,
                StyleSheets.Select(item => Input(item, project)).Distinct().OrderBy(i => i.ResourcePath, StringComparer.Ordinal).ToArray(),
                XamlFiles.Select(item => FullPath(item, project)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            var json = JsonSerializer.Serialize(manifest);
            var manifestPath = output + ".json";
            var cachePath = output + ".inputs";
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (!File.Exists(compiler)) throw new FileNotFoundException("The portable CSS compiler is missing.", compiler);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(json));
            var inputs = manifest.StyleSheets.Select(i => i.Path).Concat(manifest.XamlFiles)
                .Concat([compiler, Path.Combine(Path.GetDirectoryName(compiler)!, "Jalium.UI.Managed.dll"),
                    Path.Combine(Path.GetDirectoryName(compiler)!, "Jalium.UI.Xaml.SourceGenerator.dll"), typeof(CompileCssTask).Assembly.Location]);
            foreach (var input in inputs)
            {
                using var stream = File.OpenRead(input);
                hash.AppendData(SHA256.HashData(stream));
            }
            var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
            if (!File.Exists(output) || !File.Exists(cachePath) || File.ReadAllText(cachePath) != fingerprint)
            {
                File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = project, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                };
                start.ArgumentList.Add("exec");
                start.ArgumentList.Add(compiler);
                start.ArgumentList.Add("--compile-css");
                start.ArgumentList.Add(manifestPath);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the CSS compiler.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                foreach (var line in (stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult()).Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)) Log.LogMessageFromText(line.TrimEnd('\r'), MessageImportance.Normal);
                if (process.ExitCode != 0 || Log.HasLoggedErrors)
                {
                    if (!Log.HasLoggedErrors) Log.LogError("CSS compiler exited with code {0}.", process.ExitCode);
                    return false;
                }
                if (!File.Exists(output)) throw new IOException("The CSS compiler did not produce its output file.");
                File.WriteAllText(cachePath, fingerprint, new UTF8Encoding(false));
            }
            GeneratedFiles = [new TaskItem(output)];
            WrittenFiles = [new TaskItem(output), new TaskItem(manifestPath), new TaskItem(cachePath)];
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.LogError("CSS compilation failed: {0}", exception.Message);
            return false;
        }
    }

    private static string FullPath(ITaskItem item, string project)
        => Path.GetFullPath(item.ItemSpec, project);

    private static CssCompilationInput Input(ITaskItem item, string project)
    {
        var path = FullPath(item, project);
        var resource = item.GetMetadata("ResourcePath");
        if (string.IsNullOrEmpty(resource)) resource = item.GetMetadata("Link");
        if (string.IsNullOrEmpty(resource)) resource = Path.GetRelativePath(project, path);
        resource = resource.Replace('\\', '/');
        if (Path.IsPathRooted(resource) || resource.Split('/').Any(segment => segment is ".." or "." or ""))
            throw new ArgumentException($"CSS resource path '{resource}' must be project-relative. Set ResourcePath for linked files.");
        return new(path, resource);
    }
}
