namespace Jalium.UI.BuildSupport;

/// <summary>Inputs for one portable JALXAML compiler process.</summary>
public sealed record JalxamlBatchCompilationManifest(
    bool Optimize,
    bool Debug,
    JalxamlBatchCompilationInput[] Files);

public sealed record JalxamlBatchCompilationInput(string SourcePath, string OutputPath);
