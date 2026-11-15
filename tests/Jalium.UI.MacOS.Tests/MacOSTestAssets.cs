namespace Jalium.UI.Tests;

internal static class MacOSTestAssets
{
    internal static string PathTo(string relativePath) =>
        Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT") is { Length: > 0 } root
            ? Path.Combine(root, "tests/Jalium.UI.MacOS.Tests/Assets", relativePath)
            : Path.Combine(AppContext.BaseDirectory, "Assets", relativePath);
}
