using System.Text;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Jalium.UI.Build;

/// <summary>
/// Preprocesses JALXAML files for Razor syntax and emits expression metadata registration code.
/// Current implementation preserves source markup and generates metadata for @(...) expressions.
/// </summary>
public sealed class TransformJalxamlRazorTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[]? SourceFiles { get; set; }

    [Required]
    public string? OutputDirectory { get; set; }

    public string? ProjectDirectory { get; set; }

    [Output]
    public ITaskItem[]? TransformedFiles { get; set; }

    [Output]
    public ITaskItem[]? GeneratedCodeFiles { get; set; }

    public override bool Execute()
    {
        if (SourceFiles == null || SourceFiles.Length == 0)
        {
            TransformedFiles = Array.Empty<ITaskItem>();
            GeneratedCodeFiles = Array.Empty<ITaskItem>();
            return true;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            Log.LogError("OutputDirectory is required.");
            return false;
        }

        Directory.CreateDirectory(OutputDirectory);

        var transformed = new List<ITaskItem>();
        var metadataRows = new List<(string Id, string Expression, string[] Dependencies)>();

        foreach (var sourceItem in SourceFiles)
        {
            var sourcePath = sourceItem.GetMetadata("FullPath");
            if (string.IsNullOrWhiteSpace(sourcePath))
                sourcePath = sourceItem.ItemSpec;

            if (!File.Exists(sourcePath))
            {
                Log.LogError("JALXAML source file does not exist: {0}", sourcePath);
                continue;
            }

            var relativePath = ComputeRelativePath(sourcePath, sourceItem);
            var outputPath = Path.Combine(OutputDirectory!, relativePath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            var content = File.ReadAllText(sourcePath);
            File.WriteAllText(outputPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var relativeLogicalPath = BuildRelativeLogicalPath(sourceItem, sourcePath);
            var transformedItem = new TaskItem(outputPath);
            transformedItem.SetMetadata("SourceFile", sourcePath);
            transformedItem.SetMetadata("RazorRelativeLogicalPath", relativeLogicalPath);
            transformed.Add(transformedItem);

            foreach (var expression in ExtractExpressions(content))
            {
                var deps = ExtractDependencies(expression);
                var id = BuildExpressionId(sourcePath, expression);
                metadataRows.Add((id, expression, deps));
            }
        }

        var generated = new List<ITaskItem>();
        if (metadataRows.Count > 0)
        {
            var generatedPath = Path.Combine(OutputDirectory!, "Jalxaml.RazorMetadata.g.cs");
            File.WriteAllText(generatedPath, GenerateRegistryCode(metadataRows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            generated.Add(new TaskItem(generatedPath));
        }

        TransformedFiles = transformed.ToArray();
        GeneratedCodeFiles = generated.ToArray();
        return !Log.HasLoggedErrors;
    }

    private string ComputeRelativePath(string sourcePath, ITaskItem sourceItem)
    {
        var recursiveDir = sourceItem.GetMetadata("RecursiveDir") ?? string.Empty;
        var fileName = Path.GetFileName(sourcePath);
        if (!string.IsNullOrWhiteSpace(recursiveDir))
            return Path.Combine(recursiveDir, fileName);

        if (!string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            var fullProjectDir = Path.GetFullPath(ProjectDirectory);
            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (fullSourcePath.StartsWith(fullProjectDir, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(fullProjectDir, fullSourcePath);
            }
        }

        return fileName;
    }

    private static string BuildRelativeLogicalPath(ITaskItem sourceItem, string sourcePath)
    {
        var recursiveDir = sourceItem.GetMetadata("RecursiveDir") ?? string.Empty;
        var filename = Path.GetFileNameWithoutExtension(sourcePath);

        var relative = (recursiveDir + filename)
            .Replace('\\', '.')
            .Replace('/', '.')
            .Trim('.');

        return string.IsNullOrWhiteSpace(relative) ? filename : relative;
    }

    private static IEnumerable<string> ExtractExpressions(string content)
    {
        var expressions = new List<string>();
        var i = 0;
        while (i < content.Length)
        {
            if (content[i] == '\\' && i + 1 < content.Length && content[i + 1] == '@')
            {
                i += 2;
                continue;
            }

            if (content[i] == '@' && i + 1 < content.Length && content[i + 1] == '@')
            {
                i += 2;
                continue;
            }

            if (content[i] != '@' || i + 1 >= content.Length || content[i + 1] != '(')
            {
                i++;
                continue;
            }

            var exprStart = i + 2;
            var pos = exprStart;
            var depth = 1;
            var inString = false;
            var escaped = false;
            var quote = '\0';

            while (pos < content.Length)
            {
                var c = content[pos];
                if (escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (inString)
                {
                    if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == quote)
                    {
                        inString = false;
                        quote = '\0';
                    }

                    pos++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    pos++;
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var expr = content.Substring(exprStart, pos - exprStart).Trim();
                        if (!string.IsNullOrWhiteSpace(expr))
                            expressions.Add(expr);
                        i = pos + 1;
                        goto nextScan;
                    }
                }

                pos++;
            }

            // Unclosed expression, stop scanning and leave runtime to report.
            break;

        nextScan:
            ;
        }

        return expressions;
    }

    private static string[] ExtractDependencies(string expression)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var span = expression.AsSpan();
        var i = 0;
        var inString = false;
        var escaped = false;
        var quote = '\0';

        while (i < span.Length)
        {
            var c = span[i];
            if (escaped)
            {
                escaped = false;
                i++;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == quote)
                {
                    inString = false;
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inString = true;
                quote = c;
                i++;
                continue;
            }

            if (!IsIdentifierStart(c))
            {
                i++;
                continue;
            }

            if (i > 0 && (span[i - 1] == '.' || span[i - 1] == ':'))
            {
                i++;
                while (i < span.Length && IsIdentifierPart(span[i]))
                {
                    i++;
                }

                continue;
            }

            var start = i;
            i++;
            while (i < span.Length && IsIdentifierPart(span[i]))
            {
                i++;
            }

            var token = span[start..i].ToString();
            if (IsReservedToken(token))
                continue;

            var end = i;
            while (end < span.Length && span[end] == '.')
            {
                var nextStart = end + 1;
                if (nextStart >= span.Length || !IsIdentifierStart(span[nextStart]))
                    break;

                end = nextStart + 1;
                while (end < span.Length && IsIdentifierPart(span[end]))
                {
                    end++;
                }
            }

            var path = span[start..end].ToString();
            var after = end;
            while (after < span.Length && char.IsWhiteSpace(span[after]))
            {
                after++;
            }

            var isInvocation = after < span.Length && span[after] == '(';
            AddDependency(dependencies, path, isInvocation);

            i = end;
        }

        return dependencies.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
    }

    private static void AddDependency(HashSet<string> dependencies, string path, bool isInvocation)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (path.Contains("::", StringComparison.Ordinal))
            return;

        if (isInvocation)
        {
            var lastDot = path.LastIndexOf('.');
            if (lastDot > 0)
            {
                path = path[..lastDot];
            }
            else
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            dependencies.Add(path);
        }
    }

    private static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);

    private static bool IsReservedToken(string token) =>
        token is "true" or "false" or "null" or "new" or "global" or "this" or "base";

    private static string BuildExpressionId(string sourcePath, string expression)
    {
        var key = $"{sourcePath}|{expression}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return "rzx_" + hash.Substring(0, 16).ToLowerInvariant();
    }

    private static string GenerateRegistryCode(IEnumerable<(string Id, string Expression, string[] Dependencies)> rows)
    {
        var uniqueRows = rows
            .GroupBy(static r => r.Expression, StringComparer.Ordinal)
            .Select(static g => g.First())
            .OrderBy(static r => r.Expression, StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using Jalium.UI.Markup;");
        sb.AppendLine();
        sb.AppendLine("internal static class JalxamlRazorMetadataRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void Register()");
        sb.AppendLine("    {");
        foreach (var row in uniqueRows)
        {
            var depsLiteral = row.Dependencies.Length == 0
                ? "System.Array.Empty<string>()"
                : $"new string[] {{ {string.Join(", ", row.Dependencies.Select(ToStringLiteral))} }}";
            sb.Append("        RazorExpressionRegistry.RegisterMetadata(")
                .Append(ToStringLiteral(row.Id)).Append(", ")
                .Append(ToStringLiteral(row.Expression)).Append(", ")
                .Append(depsLiteral)
                .AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string ToStringLiteral(string value)
    {
        if (value == null)
            return "string.Empty";

        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            + "\"";
    }
}
