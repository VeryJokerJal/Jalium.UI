namespace Jalium.UI.BuildSupport;

internal sealed record CssCompilationInput(string Path, string ResourcePath);
internal sealed record CssCompilationManifest(string AssemblyName, string OutputPath,
    CssCompilationInput[] StyleSheets, string[] XamlFiles);
