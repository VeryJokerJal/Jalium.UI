using System.Text;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Tests;

public class WebViewDependencyTests
{
    private static readonly Lazy<(string Root, string[] ProjectFiles)> s_repository =
        new(CreateRepositoryIndex);

    [Fact]
    public void Repository_ShouldNotReference_MicrosoftWebWebView2_NuGetPackage()
    {
        var (root, projectFiles) = s_repository.Value;

        Assert.NotEmpty(projectFiles);

        var offenders = new List<string>();
        foreach (var file in projectFiles)
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            if (content.Contains("Microsoft.Web.WebView2", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Found forbidden Microsoft.Web.WebView2 package references: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void CurrentObjAssets_ShouldNotContain_MicrosoftWebWebView2Package()
    {
        var (root, projectFiles) = s_repository.Value;
        var projectDirectories = projectFiles
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var assetsFiles = projectDirectories
            .Select(directory => Path.Combine(directory, "obj", "project.assets.json"))
            .Where(File.Exists)
            .ToArray();

        var offenders = new List<string>();
        foreach (var file in assetsFiles)
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            if (content.Contains("microsoft.web.webview2", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Found forbidden Microsoft.Web.WebView2 assets entries: {string.Join(", ", offenders)}");
    }

    private static (string Root, string[] ProjectFiles) CreateRepositoryIndex()
    {
        string root = FindRepoRoot();
        string[] projectFiles = new[]
            {
                Path.Combine(root, "src"),
                Path.Combine(root, "tests"),
            }
            .Where(Directory.Exists)
            .SelectMany(EnumerateProjectFiles)
            .ToArray();

        return (root, projectFiles);
    }

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            foreach (string project in Directory.EnumerateFiles(directory, "*.csproj"))
            {
                yield return project;
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (IsBuildOutputDirectory(name)
                    || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static bool IsBuildOutputDirectory(string name)
    {
        return name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || name.Equals("out", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build", StringComparison.OrdinalIgnoreCase)
            || name.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CMakeFiles", StringComparison.OrdinalIgnoreCase)
            || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packages", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".cache", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".tmp-build", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("build_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("build-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("cmake-build", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("obj_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("obj-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("bin_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        // CallerFilePath keeps this reliable when JaliumBuildRoot puts the test
        // binary outside the checkout (the normal cross-OS/CI configuration).
        // The environment/current-directory fallbacks also support running an
        // assembly cross-compiled on a host whose source-path syntax differs.
        string? sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        string? configuredRoot = Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT");
        foreach (string? seed in new[]
                 {
                     configuredRoot,
                     sourceDirectory,
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory,
                 })
        {
            if (string.IsNullOrWhiteSpace(seed) || !Directory.Exists(seed))
            {
                continue;
            }

            var current = new DirectoryInfo(seed);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src", "managed", "Jalium.UI.Controls")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
