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

        // 执行编译器（如目标文件被短暂锁定则重试）
        var exitCode = RunCompiler(args.ToString(), out var output, out var error);
        for (var attempt = 0;
             attempt < 10 && exitCode != 0 && IsTransientFileLockError(error, output, outputPath);
             attempt++)
        {
            System.Threading.Thread.Sleep(150);
            exitCode = RunCompiler(args.ToString(), out output, out error);
        }

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
                // 子进程 jalxamlc.exe 退出后，Windows 可能仍短暂持有文件句柄
                // （杀毒软件/Search 索引/NTFS 缓存），这里做短时间重试避免 IOException
                var binaryData = ReadAllBytesWithRetry(outputPath);
                var base64Content = Convert.ToBase64String(binaryData);
                WriteAllTextWithRetry(base64Path, base64Content);
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

    private static bool IsTransientFileLockError(string? error, string? output, string targetPath)
    {
        var combined = (error ?? string.Empty) + "\n" + (output ?? string.Empty);
        if (combined.Length == 0) return false;
        // 英文 + 常见本地化：文件被另一进程占用
        if (combined.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (combined.IndexOf("另一个进程正在使用", StringComparison.Ordinal) >= 0) return true;
        if (combined.IndexOf("being used by another", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        // 兜底：错误文本包含了目标 .uic 路径且提到 access/锁定/sharing violation
        var fileName = Path.GetFileName(targetPath);
        if (combined.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0 &&
            (combined.IndexOf("sharing violation", StringComparison.OrdinalIgnoreCase) >= 0 ||
             combined.IndexOf("cannot access", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }
        return false;
    }

    private static byte[] ReadAllBytesWithRetry(string path, int maxAttempts = 10, int delayMs = 100)
    {
        IOException? last = null;
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[fs.Length];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = fs.Read(buffer, read, buffer.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                return buffer;
            }
            catch (IOException ex)
            {
                last = ex;
                System.Threading.Thread.Sleep(delayMs);
            }
        }
        throw last ?? new IOException($"无法读取文件: {path}");
    }

    private static void WriteAllTextWithRetry(string path, string content, int maxAttempts = 10, int delayMs = 100)
    {
        IOException? last = null;
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                File.WriteAllText(path, content);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                System.Threading.Thread.Sleep(delayMs);
            }
        }
        throw last ?? new IOException($"无法写入文件: {path}");
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
        var seenSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenGeneratedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasErrors = false;

        foreach (var sourceFile in SourceFiles)
        {
            var sourcePath = sourceFile.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(sourcePath))
            {
                sourcePath = sourceFile.ItemSpec;
            }

            sourcePath = Path.GetFullPath(sourcePath);

            if (!seenSourcePaths.Add(sourcePath))
            {
                continue;
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
                    var outputFullPath = Path.GetFullPath(outputFile);
                    if (!seenGeneratedPaths.Add(outputFullPath))
                    {
                        continue;
                    }

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
            var aliasFullPath = Path.GetFullPath(aliasFile);
            if (seenGeneratedPaths.Add(aliasFullPath))
            {
                var aliasItem = new TaskItem(aliasFile);
                aliasItem.SetMetadata("SourceFile", string.Empty);
                generatedItems.Add(aliasItem);
            }
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

        var content = File.ReadAllText(sourcePath);
        var className = TryExtractClassName(sourcePath);
        if (string.IsNullOrWhiteSpace(className))
        {
            Log.LogMessage(MessageImportance.Low, "跳过无 x:Class 的 JALXAML 文件: {0}", sourcePath);
            return null;
        }

        // 解析命名元素
        var namedElements = ParseNamedElements(content);

        // 计算资源名
        var resourceName = $"{className}.jalxaml";

        // 拆分命名空间和类名
        var lastDot = className!.LastIndexOf('.');
        var namespaceName = lastDot > 0 ? className.Substring(0, lastDot) : null;
        var simpleClassName = lastDot > 0 ? className.Substring(lastDot + 1) : className;

        // 生成代码
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0649 // Field is never assigned - wired up by XAML loader at runtime");
        sb.AppendLine();
        sb.AppendLine("using Jalium.UI.Controls;");
        sb.AppendLine("using Jalium.UI.Controls.Primitives;");
        sb.AppendLine("using Jalium.UI.Markup;");
        sb.AppendLine();

        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        sb.AppendLine($"partial class {simpleClassName}");
        sb.AppendLine("{");

        // AOT 保根:StartupUri 等按 x:Class 字符串查类型的路径,在 AOT trim 之后会被 Assembly.GetType
        // 判空。ModuleInitializer 在模块 cctor 里一次性把 typeof(T) 写入 XamlTypeRegistry —
        // typeof 引用让 linker 留住类型,注册表让 ThemeLoader 不依赖 Assembly.GetType。
        // 方法名按类名后缀区分,避免同一 module 内重名冲突。
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine($"    internal static void __JalxamlRegisterStartupType_{simpleClassName}()");
        sb.AppendLine("    {");
        sb.AppendLine($"        global::Jalium.UI.Markup.XamlTypeRegistry.RegisterStartupType(\"{className}\", typeof({simpleClassName}));");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var element in namedElements)
        {
            sb.AppendLine($"    private {element.TypeName}? {element.Name};");
        }

        if (namedElements.Count > 0)
            sb.AppendLine();

        var uicResourceName = $"{className}.uic";

        sb.AppendLine("    private void InitializeComponent()");
        sb.AppendLine("    {");

        if (namedElements.Count > 0)
        {
            sb.AppendLine($"        var _namedElements = new System.Collections.Generic.Dictionary<string, object>();");
            sb.AppendLine();

            // .uic 编译路径 (GPU 优化)
            sb.AppendLine($"        var _uicStream = GetType().Assembly.GetManifestResourceStream(\"{uicResourceName}\");");
            sb.AppendLine($"        if (_uicStream != null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _uicStream.Dispose();");
            sb.AppendLine($"            JalxamlLoader.LoadFromCompiledResource(this, \"{uicResourceName}\", _namedElements);");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine($"            XamlReader.LoadComponent(this, \"{resourceName}\", _namedElements);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // AOT-safe: 从字典连接命名元素，不使用反射
            foreach (var element in namedElements)
            {
                sb.AppendLine($"        if (_namedElements.TryGetValue(\"{element.Name}\", out var _{element.Name}_val))");
                sb.AppendLine($"            {element.Name} = _{element.Name}_val as {element.TypeName};");
            }
        }
        else
        {
            // 无命名元素的简化路径
            sb.AppendLine($"        var _uicStream = GetType().Assembly.GetManifestResourceStream(\"{uicResourceName}\");");
            sb.AppendLine($"        if (_uicStream != null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _uicStream.Dispose();");
            sb.AppendLine($"            JalxamlLoader.LoadFromCompiledResource(this, \"{uicResourceName}\");");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine($"            XamlReader.LoadComponent(this, \"{resourceName}\");");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        WriteFileIfChanged(outputPath, sb.ToString());
        return outputPath;
    }

    private static List<(string Name, string TypeName)> ParseNamedElements(string content)
    {
        var elements = new List<(string Name, string TypeName)>();

        try
        {
            var settings = new System.Xml.XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true
            };

            using var stringReader = new StringReader(content);
            using var reader = System.Xml.XmlReader.Create(stringReader, settings);

            ParseElementsRecursive(reader, elements);
        }
        catch
        {
            // 解析失败时返回空列表，InitializeComponent 仍会使用简单加载路径
        }

        return elements;
    }

    private static void ParseElementsRecursive(System.Xml.XmlReader reader, List<(string Name, string TypeName)> elements)
    {
        while (reader.Read())
        {
            if (reader.NodeType != System.Xml.XmlNodeType.Element)
                continue;

            // 根元素理论上不会是属性元素,但出于健壮性仍递归其内容。
            if (reader.LocalName.Contains('.'))
            {
                if (!reader.IsEmptyElement)
                    ParseChildElements(reader, elements, reader.Depth);
                return;
            }

            var typeName = MapTypeName(reader.LocalName, reader.NamespaceURI);
            var nameAttr = reader.GetAttribute("Name", "http://schemas.microsoft.com/winfx/2006/xaml")
                        ?? reader.GetAttribute("Name", "https://schemas.jalium.dev/jalxaml/markup")
                        ?? FindPrefixedNameAttribute(reader);

            if (!string.IsNullOrEmpty(nameAttr))
                elements.Add((nameAttr!, typeName));

            if (!reader.IsEmptyElement)
                ParseChildElements(reader, elements, reader.Depth);

            return; // 根元素处理完毕
        }
    }

    private static void ParseChildElements(System.Xml.XmlReader reader, List<(string Name, string TypeName)> elements, int parentDepth)
    {
        while (reader.Read())
        {
            if (reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.Depth == parentDepth)
                break;

            if (reader.NodeType != System.Xml.XmlNodeType.Element)
                continue;

            // 属性元素本身不是控件 (e.g., NavigationView.PaneFooter),但其
            // 内容可能包含 x:Name 命名的控件 (e.g. <TextBlock x:Name="..."/>),
            // 必须继续递归扫描,否则 code-behind 字段会漏生成。
            if (reader.LocalName.Contains('.'))
            {
                if (!reader.IsEmptyElement)
                    ParseChildElements(reader, elements, reader.Depth);
                continue;
            }

            var typeName = MapTypeName(reader.LocalName, reader.NamespaceURI);
            var nameAttr = reader.GetAttribute("Name", "http://schemas.microsoft.com/winfx/2006/xaml")
                        ?? reader.GetAttribute("Name", "https://schemas.jalium.dev/jalxaml/markup")
                        ?? FindPrefixedNameAttribute(reader);

            if (!string.IsNullOrEmpty(nameAttr))
                elements.Add((nameAttr!, typeName));

            if (!reader.IsEmptyElement)
                ParseChildElements(reader, elements, reader.Depth);
        }
    }

    private static string? FindPrefixedNameAttribute(System.Xml.XmlReader reader)
    {
        if (!reader.HasAttributes) return null;
        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (string.Equals(reader.LocalName, "Name", StringComparison.Ordinal) &&
                string.Equals(reader.Prefix, "x", StringComparison.Ordinal))
            {
                var value = reader.Value;
                reader.MoveToElement();
                return value;
            }
        }
        reader.MoveToElement();
        return null;
    }

    private static readonly Dictionary<string, string> KnownTypeMappings = new(StringComparer.Ordinal)
    {
        { "Application", "Jalium.UI.Controls.Application" },
        { "Page", "Jalium.UI.Controls.Page" },
        { "Window", "Jalium.UI.Controls.Window" },
        { "Button", "Jalium.UI.Controls.Button" },
        { "TextBlock", "Jalium.UI.Controls.TextBlock" },
        { "TextBox", "Jalium.UI.Controls.TextBox" },
        { "PasswordBox", "Jalium.UI.Controls.PasswordBox" },
        { "CheckBox", "Jalium.UI.Controls.CheckBox" },
        { "RadioButton", "Jalium.UI.Controls.RadioButton" },
        { "ListBox", "Jalium.UI.Controls.ListBox" },
        { "ComboBox", "Jalium.UI.Controls.ComboBox" },
        { "ScrollViewer", "Jalium.UI.Controls.ScrollViewer" },
        { "NavigationView", "Jalium.UI.Controls.NavigationView" },
        { "DataGrid", "Jalium.UI.Controls.DataGrid" },
        { "WebView", "Jalium.UI.Controls.WebView" },
        { "Frame", "Jalium.UI.Controls.Frame" },
        { "Popup", "Jalium.UI.Controls.Primitives.Popup" },
        { "RepeatButton", "Jalium.UI.Controls.Primitives.RepeatButton" },
        { "Thumb", "Jalium.UI.Controls.Primitives.Thumb" },
        { "StackPanel", "Jalium.UI.Controls.StackPanel" },
        { "Grid", "Jalium.UI.Controls.Grid" },
        { "Canvas", "Jalium.UI.Controls.Canvas" },
        { "Border", "Jalium.UI.Controls.Border" },
        { "DockPanel", "Jalium.UI.Controls.DockPanel" },
        { "WrapPanel", "Jalium.UI.Controls.WrapPanel" },
        { "ContentControl", "Jalium.UI.Controls.ContentControl" },
        { "ItemsControl", "Jalium.UI.Controls.ItemsControl" },
        { "UserControl", "Jalium.UI.Controls.UserControl" },
        { "Rectangle", "Jalium.UI.Shapes.Rectangle" },
        { "Ellipse", "Jalium.UI.Shapes.Ellipse" },
        { "Path", "Jalium.UI.Shapes.Path" },
        { "Line", "Jalium.UI.Shapes.Line" },
        { "Polygon", "Jalium.UI.Shapes.Polygon" },
        { "Polyline", "Jalium.UI.Shapes.Polyline" }
    };

    private static string MapTypeName(string elementName, string namespaceUri)
    {
        if (KnownTypeMappings.TryGetValue(elementName, out var mapped))
            return mapped;

        if (namespaceUri.StartsWith("clr-namespace:", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = namespaceUri.Substring("clr-namespace:".Length);
            var ns = remainder.Split(';').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(ns))
                return $"{ns}.{elementName}";
        }

        return "Jalium.UI.FrameworkElement";
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
        var sdkPaths = new List<string>();

        // Try registry first for custom SDK install paths
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
                if (key?.GetValue("KitsRoot10") is string kitsRoot)
                {
                    var binPath = Path.Combine(kitsRoot, "bin");
                    if (Directory.Exists(binPath))
                        sdkPaths.Add(binPath);
                }
            }
            catch
            {
                // Registry access may fail in restricted environments
            }
        }

        // Fallback to well-known installation paths
        sdkPaths.Add(@"C:\Program Files (x86)\Windows Kits\10\bin");
        sdkPaths.Add(@"C:\Program Files\Windows Kits\10\bin");

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
