using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Jalium.UI.Build;

/// <summary>
/// MSBuild 任务：编译 JALXAML 文件到 .uic 二进制包
/// 通过调用 jalxamlc 编译器工具实现
/// </summary>
public sealed class CompileJalxamlTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// 输入的 JALXAML 源文件
    /// </summary>
    [Required]
    public ITaskItem[]? SourceFiles { get; set; }

    /// <summary>
    /// 输出目录
    /// </summary>
    [Required]
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// 项目根目录
    /// </summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>
    /// 是否启用优化
    /// </summary>
    public bool EnableOptimization { get; set; } = true;

    /// <summary>
    /// 是否生成调试信息
    /// </summary>
    public bool GenerateDebugInfo { get; set; } = false;

    /// <summary>
    /// 中间输出目录
    /// </summary>
    public string? IntermediateOutputPath { get; set; }

    /// <summary>
    /// jalxamlc 编译器路径（如果为空则使用 dotnet tool）
    /// </summary>
    public string? CompilerPath { get; set; }

    /// <summary>
    /// 输出的编译结果文件
    /// </summary>
    [Output]
    public ITaskItem[]? CompiledFiles { get; set; }

    public override bool Execute()
    {
        if (SourceFiles == null || SourceFiles.Length == 0)
        {
            Log.LogMessage(MessageImportance.Normal, "没有找到 JALXAML 文件需要编译");
            CompiledFiles = Array.Empty<ITaskItem>();
            return true;
        }

        if (string.IsNullOrEmpty(OutputDirectory))
        {
            Log.LogError("OutputDirectory 未指定");
            return false;
        }

        // 确保输出目录存在
        Directory.CreateDirectory(OutputDirectory);

        var compiledItems = new List<ITaskItem>();
        var hasErrors = false;

        foreach (var sourceFile in SourceFiles)
        {
            var sourcePath = sourceFile.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(sourcePath))
            {
                sourcePath = sourceFile.ItemSpec;
            }

            if (!File.Exists(sourcePath))
            {
                Log.LogError("源文件不存在: {0}", sourcePath);
                hasErrors = true;
                continue;
            }

            try
            {
                var outputFile = CompileFile(sourcePath);
                if (outputFile != null)
                {
                    var item = new TaskItem(outputFile);
                    item.SetMetadata("SourceFile", sourcePath);
                    compiledItems.Add(item);
                    Log.LogMessage(MessageImportance.Normal, "已编译: {0} -> {1}", sourcePath, outputFile);
                }
            }
            catch (Exception ex)
            {
                Log.LogError("编译失败 [{0}]: {1}", sourcePath, ex.Message);
                hasErrors = true;
            }
        }

        CompiledFiles = compiledItems.ToArray();
        return !hasErrors;
    }

    private string? CompileFile(string sourcePath)
    {
        // 计算输出文件路径
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var outputPath = Path.Combine(OutputDirectory!, fileName + ".uic");

        Log.LogMessage(MessageImportance.Low, "开始编译: {0}", sourcePath);

        // 构建编译器参数
        var args = new StringBuilder();

        // 优化选项
        if (!EnableOptimization)
        {
            args.Append("-O0 ");
        }

        // 调试信息
        if (GenerateDebugInfo)
        {
            args.Append("-g ");
        }

        // 输出路径
        args.Append($"-o \"{outputPath}\" ");

        // 输入文件
        args.Append($"\"{sourcePath}\"");

        // 执行编译器
        var exitCode = RunCompiler(args.ToString(), out var output, out var error);

        if (exitCode != 0)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Log.LogError(error);
            }
            if (!string.IsNullOrEmpty(output))
            {
                Log.LogMessage(MessageImportance.High, output);
            }
            return null;
        }

        if (!string.IsNullOrEmpty(output))
        {
            Log.LogMessage(MessageImportance.Normal, output);
        }

        // 生成 Base64 编码的文本文件（用于 Source Generator 读取）
        // Source Generator 不能使用 File IO，所以我们需要把二进制数据转为文本
        var base64Path = outputPath + ".base64";
        try
        {
            if (File.Exists(outputPath))
            {
                var binaryData = File.ReadAllBytes(outputPath);
                var base64Content = Convert.ToBase64String(binaryData);
                File.WriteAllText(base64Path, base64Content);
                Log.LogMessage(MessageImportance.Low, "已生成 Base64: {0}", base64Path);
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning("无法生成 Base64 文件 [{0}]: {1}", base64Path, ex.Message);
        }

        return outputPath;
    }

    private int RunCompiler(string arguments, out string output, out string error)
    {
        string fileName;
        string args;

        if (!string.IsNullOrEmpty(CompilerPath) && File.Exists(CompilerPath))
        {
            // 使用指定的编译器路径
            fileName = CompilerPath;
            args = arguments;
        }
        else
        {
            // 使用 dotnet tool 执行编译器
            fileName = "dotnet";
            args = $"jalxamlc {arguments}";
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = ProjectDirectory ?? Directory.GetCurrentDirectory()
        };

        Log.LogMessage(MessageImportance.Low, "执行: {0} {1}", fileName, args);

        using var process = Process.Start(psi);
        if (process == null)
        {
            output = "";
            error = "无法启动编译器进程";
            return -1;
        }

        output = process.StandardOutput.ReadToEnd();
        error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode;
    }
}

/// <summary>
/// MSBuild 任务：生成 JALXAML 代码隐藏文件
/// </summary>
public sealed class GenerateJalxamlCodeBehindTask : Microsoft.Build.Utilities.Task
{
    private const string ViewAliasFileName = "Jalxaml.ViewAliases.g.cs";
    private static readonly HashSet<string> ConflictingControlTypeNames = new(StringComparer.Ordinal)
    {
        "DataGrid",
        "WebView"
    };

    private static readonly System.Text.RegularExpressions.Regex ClassAttributeRegex = new(
        @"x:Class\s*=\s*['""](?<class>[^'""]+)['""]",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [Required]
    public ITaskItem[]? SourceFiles { get; set; }

    [Required]
    public string? OutputDirectory { get; set; }

    public string? RootNamespace { get; set; }

    public string? CompilerPath { get; set; }

    public string? ProjectDirectory { get; set; }

    [Output]
    public ITaskItem[]? GeneratedFiles { get; set; }

    public override bool Execute()
    {
        if (SourceFiles == null || SourceFiles.Length == 0)
        {
            GeneratedFiles = Array.Empty<ITaskItem>();
            return true;
        }

        Directory.CreateDirectory(OutputDirectory!);

        var generatedItems = new List<ITaskItem>();
        var discoveredClassNames = new List<string>();
        var hasErrors = false;

        foreach (var sourceFile in SourceFiles)
        {
            var sourcePath = sourceFile.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(sourcePath))
            {
                sourcePath = sourceFile.ItemSpec;
            }

            try
            {
                var className = TryExtractClassName(sourcePath);
                if (!string.IsNullOrWhiteSpace(className))
                {
                    discoveredClassNames.Add(className);
                }

                var outputFile = GenerateCodeBehind(sourcePath);
                if (outputFile != null)
                {
                    var item = new TaskItem(outputFile);
                    item.SetMetadata("SourceFile", sourcePath);
                    generatedItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                Log.LogError("代码生成失败 [{0}]: {1}", sourcePath, ex.Message);
                hasErrors = true;
            }
        }

        try
        {
            var aliasFile = GenerateViewAliasFile(discoveredClassNames);
            var aliasItem = new TaskItem(aliasFile);
            aliasItem.SetMetadata("SourceFile", string.Empty);
            generatedItems.Add(aliasItem);
        }
        catch (Exception ex)
        {
            Log.LogError("视图别名文件生成失败: {0}", ex.Message);
            hasErrors = true;
        }

        GeneratedFiles = generatedItems.ToArray();
        return !hasErrors;
    }

    private static string? TryExtractClassName(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        var source = File.ReadAllText(sourcePath);
        var match = ClassAttributeRegex.Match(source);
        if (!match.Success)
        {
            return null;
        }

        var className = match.Groups["class"].Value.Trim();
        return string.IsNullOrWhiteSpace(className) ? null : className;
    }

    private string GenerateViewAliasFile(IEnumerable<string> discoveredClassNames)
    {
        var aliasFilePath = Path.Combine(OutputDirectory!, ViewAliasFileName);

        var aliasCandidates = discoveredClassNames
            .Select(static fullName => fullName.Trim())
            .Where(static fullName => !string.IsNullOrWhiteSpace(fullName))
            .Select(static fullName => new
            {
                FullName = fullName,
                SimpleName = GetSimpleTypeName(fullName)
            })
            .Where(entry => !string.IsNullOrEmpty(entry.SimpleName) && ConflictingControlTypeNames.Contains(entry.SimpleName))
            .ToList();

        var resolvedAliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in aliasCandidates.GroupBy(static entry => entry.SimpleName, StringComparer.Ordinal))
        {
            var distinctFullNames = group
                .Select(static entry => entry.FullName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (distinctFullNames.Length == 1)
            {
                resolvedAliases[group.Key] = distinctFullNames[0];
            }
            else if (distinctFullNames.Length > 1)
            {
                Log.LogWarning("视图类型别名 '{0}' 存在多个候选: {1}。已跳过自动别名生成。", group.Key, string.Join(", ", distinctFullNames));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file is generated by Jalium.UI.Build.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        foreach (var alias in resolvedAliases.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"global using {alias.Key} = {alias.Value};");
        }

        if (resolvedAliases.Count == 0)
        {
            sb.AppendLine("// No control-name view aliases were generated.");
        }

        WriteFileIfChanged(aliasFilePath, sb.ToString());
        return aliasFilePath;
    }

    private static void WriteFileIfChanged(string path, string content)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string GetSimpleTypeName(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return string.Empty;
        }

        var lastDotIndex = fullTypeName.LastIndexOf('.');
        if (lastDotIndex < 0 || lastDotIndex >= fullTypeName.Length - 1)
        {
            return fullTypeName;
        }

        return fullTypeName[(lastDotIndex + 1)..];
    }

    private string? GenerateCodeBehind(string sourcePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var outputPath = Path.Combine(OutputDirectory!, fileName + ".g.cs");

        // 构建编译器参数
        var args = new StringBuilder();
        args.Append("-c "); // 生成代码

        if (!string.IsNullOrEmpty(RootNamespace))
        {
            args.Append($"-n \"{RootNamespace}\" ");
        }

        // 传递项目目录，用于计算资源名的相对路径
        if (!string.IsNullOrEmpty(ProjectDirectory))
        {
            var projDir = ProjectDirectory.TrimEnd('\\', '/');
            args.Append($"-p \"{projDir}\" ");
        }

        // 移除尾部反斜杠以避免引号转义问题
        var outputDir = OutputDirectory!.TrimEnd('\\', '/');
        args.Append($"-d \"{outputDir}\" ");
        args.Append($"\"{sourcePath}\"");

        // 执行编译器
        var exitCode = RunCompiler(args.ToString(), out var output, out var error);

        if (exitCode != 0)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Log.LogError(error);
            }
            return null;
        }

        if (!string.IsNullOrEmpty(output))
        {
            Log.LogMessage(MessageImportance.Normal, output);
        }

        return File.Exists(outputPath) ? outputPath : null;
    }

    private int RunCompiler(string arguments, out string output, out string error)
    {
        string fileName;
        string args;

        if (!string.IsNullOrEmpty(CompilerPath) && File.Exists(CompilerPath))
        {
            fileName = CompilerPath;
            args = arguments;
        }
        else
        {
            fileName = "dotnet";
            args = $"jalxamlc {arguments}";
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = ProjectDirectory ?? Directory.GetCurrentDirectory()
        };

        Log.LogMessage(MessageImportance.Low, "执行: {0} {1}", fileName, args);

        using var process = Process.Start(psi);
        if (process == null)
        {
            output = "";
            error = "无法启动编译器进程";
            return -1;
        }

        output = process.StandardOutput.ReadToEnd();
        error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode;
    }
}

/// <summary>
/// MSBuild 任务：编译 HLSL 着色器文件到 .cso (Compiled Shader Object)
/// 支持使用 fxc.exe (legacy) 或 dxc.exe (modern) 编译器
/// 自动检测 Windows SDK 中的着色器编译器
/// </summary>
public sealed class CompileHlslShadersTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// 输入的 HLSL 着色器文件
    /// 每个 item 应包含以下 metadata：
    ///   EntryPoint  - 着色器入口函数名（如 VSMain, PSMain）
    ///   ShaderModel - 目标着色器模型（如 vs_5_1, ps_5_1, cs_5_1）
    ///   ShaderType  - 着色器类型标识（如 UIRectVS, TextPS），用于输出文件名
    /// </summary>
    [Required]
    public ITaskItem[]? SourceFiles { get; set; }

    /// <summary>
    /// 输出目录（.cso 文件输出位置）
    /// </summary>
    [Required]
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// fxc.exe 或 dxc.exe 编译器路径（可选，自动检测）
    /// </summary>
    public string? ShaderCompilerPath { get; set; }

    /// <summary>
    /// 优化级别：0-3（默认 3）
    /// </summary>
    public int OptimizationLevel { get; set; } = 3;

    /// <summary>
    /// 是否生成调试信息
    /// </summary>
    public bool EnableDebugInfo { get; set; }

    /// <summary>
    /// 是否将警告视为错误
    /// </summary>
    public bool WarningsAsErrors { get; set; } = true;

    /// <summary>
    /// 输出的编译结果文件
    /// </summary>
    [Output]
    public ITaskItem[]? CompiledShaders { get; set; }

    public override bool Execute()
    {
        if (SourceFiles == null || SourceFiles.Length == 0)
        {
            Log.LogMessage(MessageImportance.Normal, "没有 HLSL 着色器需要编译");
            CompiledShaders = Array.Empty<ITaskItem>();
            return true;
        }

        if (string.IsNullOrEmpty(OutputDirectory))
        {
            Log.LogError("OutputDirectory 未指定");
            return false;
        }

        Directory.CreateDirectory(OutputDirectory);

        // 查找着色器编译器
        var compilerPath = ResolveShaderCompiler();
        if (compilerPath == null)
        {
            Log.LogWarning(
                "无法找到着色器编译器 (fxc.exe 或 dxc.exe)，跳过着色器预编译。" +
                "着色器将在运行时编译。请安装 Windows SDK 或设置 HlslShaderCompilerPath 属性。");
            CompiledShaders = Array.Empty<ITaskItem>();
            return true;
        }

        Log.LogMessage(MessageImportance.Normal, "使用着色器编译器: {0}", compilerPath);

        var compiledItems = new List<ITaskItem>();
        var hasErrors = false;
        var isDxc = Path.GetFileNameWithoutExtension(compilerPath)
            .Equals("dxc", StringComparison.OrdinalIgnoreCase);

        foreach (var sourceFile in SourceFiles)
        {
            var sourcePath = sourceFile.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(sourcePath))
                sourcePath = sourceFile.ItemSpec;

            var entryPoint = sourceFile.GetMetadata("EntryPoint");
            var shaderModel = sourceFile.GetMetadata("ShaderModel");
            var shaderType = sourceFile.GetMetadata("ShaderType");

            if (string.IsNullOrEmpty(entryPoint) || string.IsNullOrEmpty(shaderModel))
            {
                Log.LogError("HLSL 文件缺少必要 metadata (EntryPoint, ShaderModel): {0}", sourcePath);
                hasErrors = true;
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                Log.LogError("着色器源文件不存在: {0}", sourcePath);
                hasErrors = true;
                continue;
            }

            try
            {
                var outputName = !string.IsNullOrEmpty(shaderType)
                    ? shaderType
                    : Path.GetFileNameWithoutExtension(sourcePath);
                var outputPath = Path.Combine(OutputDirectory, outputName + ".cso");

                var success = CompileShader(
                    compilerPath, isDxc, sourcePath, outputPath, entryPoint, shaderModel);

                if (success && File.Exists(outputPath))
                {
                    var item = new TaskItem(outputPath);
                    item.SetMetadata("SourceFile", sourcePath);
                    item.SetMetadata("ShaderType", shaderType);
                    item.SetMetadata("EntryPoint", entryPoint);
                    item.SetMetadata("ShaderModel", shaderModel);
                    compiledItems.Add(item);
                    Log.LogMessage(MessageImportance.Normal,
                        "已编译着色器: {0} [{1}] -> {2}", sourcePath, entryPoint, outputPath);
                }
                else
                {
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                Log.LogError("着色器编译失败 [{0}]: {1}", sourcePath, ex.Message);
                hasErrors = true;
            }
        }

        CompiledShaders = compiledItems.ToArray();
        return !hasErrors;
    }

    private bool CompileShader(
        string compilerPath, bool isDxc,
        string sourcePath, string outputPath,
        string entryPoint, string shaderModel)
    {
        var args = new StringBuilder();

        if (isDxc)
        {
            args.Append($"-E {entryPoint} ");
            args.Append($"-T {shaderModel} ");
            args.Append($"-Fo \"{outputPath}\" ");
            if (EnableDebugInfo) args.Append("-Zi ");
            args.Append(OptimizationLevel switch
            {
                0 => "-Od ",
                1 => "-O1 ",
                2 => "-O2 ",
                _ => "-O3 "
            });
            if (WarningsAsErrors) args.Append("-WX ");
        }
        else
        {
            args.Append($"/E {entryPoint} ");
            args.Append($"/T {shaderModel} ");
            args.Append($"/Fo \"{outputPath}\" ");
            if (EnableDebugInfo) args.Append("/Zi ");
            args.Append(OptimizationLevel switch
            {
                0 => "/Od ",
                1 => "/O1 ",
                2 => "/O2 ",
                _ => "/O3 "
            });
            if (WarningsAsErrors) args.Append("/WX ");
        }

        args.Append($"\"{sourcePath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = compilerPath,
            Arguments = args.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Log.LogMessage(MessageImportance.Low, "执行: {0} {1}", compilerPath, args);

        using var process = Process.Start(psi);
        if (process == null)
        {
            Log.LogError("无法启动着色器编译器进程");
            return false;
        }

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrEmpty(stdErr))
                Log.LogError("着色器编译错误: {0}", stdErr);
            if (!string.IsNullOrEmpty(stdOut))
                Log.LogMessage(MessageImportance.High, stdOut);
            return false;
        }

        if (!string.IsNullOrEmpty(stdOut))
            Log.LogMessage(MessageImportance.Low, stdOut);

        return true;
    }

    private string? ResolveShaderCompiler()
    {
        if (!string.IsNullOrEmpty(ShaderCompilerPath) && File.Exists(ShaderCompilerPath))
            return ShaderCompilerPath;

        // 尝试在 PATH 中查找
        var pathDxc = FindInPath("dxc.exe");
        if (pathDxc != null) return pathDxc;

        var pathFxc = FindInPath("fxc.exe");
        if (pathFxc != null) return pathFxc;

        // 搜索 Windows SDK
        var sdkPaths = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\bin",
            @"C:\Program Files\Windows Kits\10\bin"
        };

        foreach (var sdkBase in sdkPaths)
        {
            if (!Directory.Exists(sdkBase)) continue;

            try
            {
                var versions = Directory.GetDirectories(sdkBase, "10.*")
                    .OrderByDescending(d => d)
                    .ToArray();

                foreach (var versionDir in versions)
                {
                    var fxcPath = Path.Combine(versionDir, "x64", "fxc.exe");
                    if (File.Exists(fxcPath)) return fxcPath;

                    var dxcPath = Path.Combine(versionDir, "x64", "dxc.exe");
                    if (File.Exists(dxcPath)) return dxcPath;
                }
            }
            catch (UnauthorizedAccessException) { }
        }

        return null;
    }

    private static string? FindInPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, executable);
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }
}

/// <summary>
/// MSBuild 任务：从 Jalium.UI.Gpu 程序集的 ShaderLibrary 提取 HLSL 源码到独立 .hlsl 文件
/// 在构建管线中使用，将嵌入 C# 代码的着色器提取为可被 fxc/dxc 编译的文件
/// </summary>
public sealed class ExtractHlslShadersTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Jalium.UI.Gpu 程序集路径（用于反射加载 ShaderLibrary）
    /// </summary>
    [Required]
    public string? GpuAssemblyPath { get; set; }

    /// <summary>
    /// 输出目录
    /// </summary>
    [Required]
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// 输出的 HLSL 文件（带 metadata: EntryPoint, ShaderModel, ShaderType）
    /// </summary>
    [Output]
    public ITaskItem[]? ExtractedShaders { get; set; }

    public override bool Execute()
    {
        if (string.IsNullOrEmpty(GpuAssemblyPath) || !File.Exists(GpuAssemblyPath))
        {
            Log.LogWarning("Jalium.UI.Gpu 程序集不存在: {0}，跳过着色器提取",
                GpuAssemblyPath ?? "(null)");
            ExtractedShaders = Array.Empty<ITaskItem>();
            return true;
        }

        Directory.CreateDirectory(OutputDirectory!);

        try
        {
            var assembly = System.Reflection.Assembly.LoadFrom(GpuAssemblyPath);
            var libraryType = assembly.GetType("Jalium.UI.Gpu.Shaders.ShaderLibrary");
            var shaderTypeEnum = assembly.GetType("Jalium.UI.Gpu.Shaders.ShaderType");

            if (libraryType == null || shaderTypeEnum == null)
            {
                Log.LogWarning("无法在程序集中找到 ShaderLibrary 或 ShaderType 类型");
                ExtractedShaders = Array.Empty<ITaskItem>();
                return true;
            }

            var getFullSource = libraryType.GetMethod("GetFullSource");
            var getEntryPoint = libraryType.GetMethod("GetEntryPoint");
            var getTarget = libraryType.GetMethod("GetTarget");

            if (getFullSource == null || getEntryPoint == null || getTarget == null)
            {
                Log.LogWarning("ShaderLibrary 缺少必要的方法");
                ExtractedShaders = Array.Empty<ITaskItem>();
                return true;
            }

            var extractedItems = new List<ITaskItem>();

            foreach (var shaderType in Enum.GetValues(shaderTypeEnum))
            {
                var typeName = shaderType.ToString()!;
                var source = (string)getFullSource.Invoke(null, [shaderType])!;
                var entryPoint = (string)getEntryPoint.Invoke(null, [shaderType])!;
                var target = (string)getTarget.Invoke(null, [shaderType])!;

                var hlslPath = Path.Combine(OutputDirectory!, typeName + ".hlsl");
                File.WriteAllText(hlslPath, source, Encoding.UTF8);

                var item = new TaskItem(hlslPath);
                item.SetMetadata("EntryPoint", entryPoint);
                item.SetMetadata("ShaderModel", target);
                item.SetMetadata("ShaderType", typeName);
                extractedItems.Add(item);

                Log.LogMessage(MessageImportance.Normal,
                    "已提取着色器: {0} (entry={1}, target={2})", typeName, entryPoint, target);
            }

            ExtractedShaders = extractedItems.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarning("提取着色器失败: {0}", ex.Message);
            ExtractedShaders = Array.Empty<ITaskItem>();
            return true;
        }
    }
}
