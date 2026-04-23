using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Source generator that generates InitializeComponent methods for JALXAML files.
/// Generates code that loads from embedded JALXAML resources at runtime.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class JalxamlSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get all .jalxaml files
        var jalxamlFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".jalxaml", StringComparison.OrdinalIgnoreCase));

        // Get compilation info for assembly name + config options for RootNamespace / ProjectDir
        var compilationAndFiles = context.CompilationProvider
            .Combine(jalxamlFiles.Collect())
            .Combine(context.AnalyzerConfigOptionsProvider);

        // Generate source for each JALXAML file
        context.RegisterSourceOutput(compilationAndFiles, (spc, source) =>
        {
            var ((compilation, jalxamlFileList), configOptions) = source;
            var assemblyName = compilation.AssemblyName ?? "Unknown";

            // 用 RootNamespace(MSBuild 默认 = AssemblyName)而不是 x:Class 算资源名,
            // 跟 <EmbeddedResource> 默认 manifest naming 规则对齐。
            var rootNamespace = assemblyName;
            if (configOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var configuredRoot)
                && !string.IsNullOrWhiteSpace(configuredRoot))
            {
                rootNamespace = configuredRoot;
            }

            string? projectDir = null;
            if (configOptions.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projectDirValue)
                && !string.IsNullOrWhiteSpace(projectDirValue))
            {
                projectDir = projectDirValue;
            }
            else if (configOptions.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDirAlt)
                && !string.IsNullOrWhiteSpace(projectDirAlt))
            {
                projectDir = projectDirAlt;
            }

            foreach (var file in jalxamlFileList)
            {
                var resourceName = ComputeManifestResourceName(file, configOptions, rootNamespace, projectDir);
                GenerateForJalxaml(spc, file, assemblyName, resourceName);
            }
        });
    }

    private static string ComputeManifestResourceName(
        AdditionalText file,
        AnalyzerConfigOptionsProvider configOptions,
        string rootNamespace,
        string? projectDir)
    {
        // 优先级 1:EmbeddedResource 的 <LogicalName>/<ManifestResourceName> 可以被透传到
        // AnalyzerConfigOptions 里(通过 CompilerVisibleItemMetadata)。如果 Jalium.UI.Build
        // 暴露了,这里直接取最终 logical name,最准确。
        var fileOptions = configOptions.GetOptions(file);
        if (fileOptions.TryGetValue("build_metadata.AdditionalFiles.ManifestResourceName", out var manifestName)
            && !string.IsNullOrWhiteSpace(manifestName))
        {
            return manifestName;
        }
        if (fileOptions.TryGetValue("build_metadata.AdditionalFiles.LogicalName", out var logicalName)
            && !string.IsNullOrWhiteSpace(logicalName))
        {
            return logicalName;
        }

        // 优先级 2:按 MSBuild 默认规则 {RootNamespace}.{relative dir as dots}.{filename},
        // 这是 <EmbeddedResource Include="Views/Page.jalxaml" /> 生成的 manifest name。
        string? relativePath = null;
        if (fileOptions.TryGetValue("build_metadata.AdditionalFiles.TargetPath", out var targetPath)
            && !string.IsNullOrWhiteSpace(targetPath))
        {
            relativePath = targetPath;
        }
        else if (!string.IsNullOrEmpty(projectDir)
            && file.Path.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = file.Path.Substring(projectDir!.Length).TrimStart('/', '\\');
        }
        else
        {
            // 优先级 3:只拿文件名(fallback)。适用于拿不到 projectDir 的情况。
            relativePath = Path.GetFileName(file.Path);
        }

        // Razor 前处理会把每个 jalxaml 复制到 obj/<cfg>/<tfm>/Jalxaml/Razor/<originalRelativeDir>/<filename>.jalxaml
        // 并将其作为 AdditionalFile 暴露给 SG,因此 file.Path 指向中间副本。直接用相对路径计算
        // 会得到 "obj.Debug.net10.0.Jalxaml.Razor.<originalRelativeDir>.<filename>.jalxaml",与
        // Jalium.UI.Build.targets 里 EmbedJalxamlSources 目标基于 %(RelativeDir)%(Filename) 生成
        // 的 manifest 名不符,运行时就会找不到资源。这里识别并剥离中间目录前缀,回退到原始
        // 相对路径 ( <originalRelativeDir>/<filename>.jalxaml )。
        relativePath = StripRazorIntermediatePrefix(relativePath);

        var dotted = relativePath.Replace('/', '.').Replace('\\', '.');
        return $"{rootNamespace}.{dotted}";
    }

    private static string StripRazorIntermediatePrefix(string relativePath)
    {
        // 匹配 "obj/<anything>/Jalxaml/Razor/" 或反斜杠变体,大小写不敏感。截取其后的部分。
        ReadOnlySpan<char> span = relativePath.AsSpan();
        var normalized = new System.Text.StringBuilder(relativePath.Length);
        foreach (var c in span)
        {
            normalized.Append(c == '\\' ? '/' : c);
        }
        var normalizedString = normalized.ToString();

        const string marker = "/Jalxaml/Razor/";
        var idx = normalizedString.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return relativePath;
        }

        // 只有在 marker 前面出现 "obj/" 段时才认作 Razor 中间输出,避免误伤 user 故意放在
        // 真实 Jalxaml/Razor 目录下的文件。
        var beforeMarker = normalizedString.Substring(0, idx);
        if (beforeMarker.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) < 0
            && !beforeMarker.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
        {
            return relativePath;
        }

        return normalizedString.Substring(idx + marker.Length);
    }

    private void GenerateForJalxaml(SourceProductionContext context, AdditionalText file, string assemblyName, string resourceName)
    {
        var content = file.GetText(context.CancellationToken)?.ToString();
        if (string.IsNullOrEmpty(content))
            return;

        try
        {
            var parseResult = JalxamlParser.Parse(content!, file.Path);
            if (parseResult?.ClassName == null || string.IsNullOrEmpty(parseResult.ClassName))
                return;

            var className = parseResult.ClassName;
            var generatedCode = GenerateCode(parseResult, assemblyName, resourceName);
            var fileName = $"{className.Replace(".", "_")}.g.cs";

            context.AddSource(fileName, SourceText.From(generatedCode, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            // Report diagnostic for parse errors
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "JALXAML001",
                    "JALXAML Parse Error",
                    "Failed to parse JALXAML file '{0}': {1}",
                    "Jalium.UI.Xaml",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true),
                Location.None,
                file.Path,
                ex.Message);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private string GenerateCode(JalxamlParseResult result, string assemblyName, string resourceName)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0649 // Field is never assigned - wired up by XAML loader at runtime");
        sb.AppendLine();
        sb.AppendLine("using Jalium.UI.Controls;");
        sb.AppendLine("using Jalium.UI.Controls.Primitives;");
        sb.AppendLine("using Jalium.UI.Markup;");
        sb.AppendLine();

        // Extract namespace and class name
        var lastDot = result.ClassName!.LastIndexOf('.');
        var namespaceName = lastDot > 0 ? result.ClassName.Substring(0, lastDot) : null;
        var className = lastDot > 0 ? result.ClassName.Substring(lastDot + 1) : result.ClassName;

        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        sb.AppendLine($"partial class {className}");
        sb.AppendLine("{");

        // Generate fields for named elements
        foreach (var element in result.NamedElements)
        {
            sb.AppendLine($"    private {element.TypeName}? {element.Name};");
        }

        if (result.NamedElements.Count > 0)
        {
            sb.AppendLine();
        }

        // Generate InitializeComponent method.
        // resourceName 来自调用方,按 MSBuild 默认 <EmbeddedResource> manifest naming 规则
        // (基于文件物理路径)计算,而不是用 x:Class —— 这两者可以不一致,例如
        // `x:Class="...App"` 写在 `Application.jalxaml` 文件里。
        sb.AppendLine("    private void InitializeComponent()");
        sb.AppendLine("    {");

        if (result.NamedElements.Count > 0)
        {
            // AOT-safe: Use the overload that returns named elements as a dictionary
            // instead of wiring via reflection (which fails when private field metadata is trimmed)
            sb.AppendLine($"        var _namedElements = new System.Collections.Generic.Dictionary<string, object>();");
            sb.AppendLine($"        XamlReader.LoadComponent(this, \"{resourceName}\", _namedElements);");

            // Generate explicit wiring for each named element (no reflection needed)
            foreach (var element in result.NamedElements)
            {
                sb.AppendLine($"        if (_namedElements.TryGetValue(\"{element.Name}\", out var _{element.Name}_val))");
                sb.AppendLine($"            {element.Name} = _{element.Name}_val as {element.TypeName};");
            }
        }
        else
        {
            // No named elements - use the simple overload
            sb.AppendLine($"        XamlReader.LoadComponent(this, \"{resourceName}\");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}

/// <summary>
/// Represents a parsed JALXAML file.
/// </summary>
public sealed class JalxamlParseResult
{
    public string? ClassName { get; set; }
    public string? RootElementType { get; set; }
    public List<NamedElement> NamedElements { get; } = new();
}

/// <summary>
/// Represents a named element (x:Name) in JALXAML.
/// </summary>
public sealed class NamedElement
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}
