using System.Reflection;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Jalium.UI.Build;

/// <summary>
/// Preprocesses JALXAML files for Razor syntax and emits expression metadata registration code.
/// Current implementation preserves source markup and generates metadata for @(...) expressions.
/// </summary>
public sealed class TransformJalxamlRazorTask : Microsoft.Build.Utilities.Task
{
    private static readonly HashSet<string> ReservedTokens = new(StringComparer.Ordinal)
    {
        "abstract", "and", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double",
        "dynamic", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float",
        "for", "foreach", "global", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "not", "null", "object", "operator", "or", "out", "override",
        "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "var", "virtual", "void", "volatile", "when", "while", "with",
        "nameof", "Write", "WriteLiteral"
    };

    private static readonly Regex VariableDeclarationRegex = new(
        @"(?:(?<=^)|(?<=[;({]))\s*(?:var|[_a-zA-Z]\w*(?:\s*<[^;{}()=]+>)?(?:\[\])?)\s+(?<name>[_a-zA-Z]\w*)\s*=",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ForeachDeclarationRegex = new(
        @"\bforeach\s*\(\s*(?:var|[_a-zA-Z]\w*(?:\s*<[^()]+>)?(?:\[\])?)\s+(?<name>[_a-zA-Z]\w*)\s+in\b",
        RegexOptions.Compiled);

    private static readonly Regex CatchDeclarationRegex = new(
        @"\bcatch\s*\(\s*[_a-zA-Z]\w*(?:\s*<[^()]+>)?(?:\[\])?\s+(?<name>[_a-zA-Z]\w*)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex LocalFunctionDeclarationRegex = new(
        @"(?:(?<=^)|(?<=[;{}]))\s*(?:[_a-zA-Z]\w*(?:\s*<[^()]+>)?(?:\[\])?)\s+(?<name>[_a-zA-Z]\w*)\s*\((?<params>[^)]*)\)\s*(?:=>|\{)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ParameterNameRegex = new(
        @"(?:^|,)\s*(?:(?:this|params|ref|out|in)\s+)*(?:[_a-zA-Z]\w*(?:\s*<[^()]+>)?(?:\[\])?\s+)+(?<name>[_a-zA-Z]\w*)\s*(?:=[^,]+)?\s*(?=,|$)",
        RegexOptions.Compiled);

    private static readonly Regex UsingDirectiveRegex = new(
        @"@using\s+(?![\s(])([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;",
        RegexOptions.Compiled);

    [Required]
    public ITaskItem[]? SourceFiles { get; set; }

    [Required]
    public string? OutputDirectory { get; set; }

    public string? ProjectDirectory { get; set; }

    /// <summary>
    /// Reference assembly paths for scanning namespace types from @using directives.
    /// </summary>
    public ITaskItem[]? ReferencePaths { get; set; }

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
        var templateRows = new List<TemplateInfo>();
        var usingNamespaces = new HashSet<string>(StringComparer.Ordinal);
        var outputRoot = Path.GetFullPath(OutputDirectory);
        var seenSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceItem in SourceFiles)
        {
            var sourcePath = sourceItem.GetMetadata("FullPath");
            if (string.IsNullOrWhiteSpace(sourcePath))
                sourcePath = sourceItem.ItemSpec;

            sourcePath = Path.GetFullPath(sourcePath);

            if (!seenSourcePaths.Add(sourcePath))
                continue;

            if (sourcePath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
            {
                Log.LogMessage(MessageImportance.Low, "Skipping generated JALXAML input inside Razor output directory: {0}", sourcePath);
                continue;
            }

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

            // Extract @using Namespace; directives
            foreach (Match m in UsingDirectiveRegex.Matches(content))
                usingNamespaces.Add(m.Groups[1].Value);

            // Expand @{ } Razor code blocks at build time.
            // Protect @section and @RenderSection directives — these are handled at runtime.
            const string escapedSectionPlaceholder = "\x01__ESCSECTION__\x01";
            const string escapedRenderSectionPlaceholder = "\x01__ESCRENDERSECTION__\x01";
            const string sectionPlaceholder = "\x01__SECTION__\x01";
            const string renderSectionPlaceholder = "\x01__RENDERSECTION__\x01";
            try
            {
                var protected_ = content
                    .Replace("@@section ", escapedSectionPlaceholder)
                    .Replace("@@RenderSection(", escapedRenderSectionPlaceholder)
                    .Replace("@section ", sectionPlaceholder)
                    .Replace("@RenderSection(", renderSectionPlaceholder);
                var expanded = RazorCodeBlockExpander.Expand(protected_);
                if (expanded != null)
                {
                    Log.LogMessage(MessageImportance.Normal, "Expanded Razor code blocks in: {0}", sourcePath);
                    content = expanded
                        .Replace(sectionPlaceholder, "@section ")
                        .Replace(renderSectionPlaceholder, "@RenderSection(")
                        .Replace(escapedSectionPlaceholder, "@@section ")
                        .Replace(escapedRenderSectionPlaceholder, "@@RenderSection(");
                }
                else
                {
                    content = content; // no expansion needed, keep original with @section/@RenderSection intact
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to expand @{{ }} code blocks in '{0}': {1}", sourcePath, ex.Message);
                continue;
            }

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

            foreach (var template in ExtractTemplatesWithCodeBlocks(content))
            {
                templateRows.Add(template);
            }
        }

        // Resolve types from @using namespaces by scanning referenced assemblies
        var namespaceTypes = ResolveNamespaceTypes(usingNamespaces);

        var generated = new List<ITaskItem>();
        if (metadataRows.Count > 0 || templateRows.Count > 0 || namespaceTypes.Count > 0)
        {
            var generatedPath = Path.Combine(OutputDirectory!, "Jalxaml.RazorMetadata.g.cs");
            File.WriteAllText(generatedPath, GenerateRegistryCode(metadataRows, templateRows, namespaceTypes), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

            if (content[i] != '@' || i + 1 >= content.Length)
            {
                i++;
                continue;
            }

            // Match @( for expressions and @if( for conditional expressions.
            int exprStart;
            if (content[i + 1] == '(')
            {
                exprStart = i + 2;
            }
            else if (i + 3 < content.Length && content[i + 1] == 'i' && content[i + 2] == 'f' && content[i + 3] == '(')
            {
                exprStart = i + 4;
            }
            else
            {
                i++;
                continue;
            }
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

        // Also scan for "else if(expr)" patterns (no @ prefix).
        i = 0;
        while (i < content.Length)
        {
            // Look for "else" keyword followed by optional whitespace then "if("
            if (i + 7 < content.Length &&
                content[i] == 'e' && content[i + 1] == 'l' && content[i + 2] == 's' && content[i + 3] == 'e')
            {
                var j = i + 4;
                while (j < content.Length && char.IsWhiteSpace(content[j]))
                    j++;

                if (j + 2 < content.Length && content[j] == 'i' && content[j + 1] == 'f')
                {
                    j += 2;
                    while (j < content.Length && char.IsWhiteSpace(content[j]))
                        j++;

                    if (j < content.Length && content[j] == '(')
                    {
                        j++; // skip '('
                        var exprStart = j;
                        var depth = 1;
                        var inStr = false;
                        var esc = false;
                        var qt = '\0';

                        while (j < content.Length)
                        {
                            var c = content[j];
                            if (esc) { esc = false; j++; continue; }
                            if (inStr)
                            {
                                if (c == '\\') esc = true;
                                else if (c == qt) { inStr = false; qt = '\0'; }
                                j++;
                                continue;
                            }
                            if (c == '"' || c == '\'') { inStr = true; qt = c; j++; continue; }
                            if (c == '(') { depth++; }
                            else if (c == ')')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    var expr = content.Substring(exprStart, j - exprStart).Trim();
                                    if (!string.IsNullOrWhiteSpace(expr))
                                        expressions.Add(expr);
                                    i = j + 1;
                                    goto nextElseIfScan;
                                }
                            }
                            j++;
                        }
                        break; // unclosed
                    }
                }
            }

            i++;
            continue;

        nextElseIfScan:
            ;
        }

        return expressions;
    }

    private static string[] ExtractDependencies(string expression, IReadOnlySet<string>? ignoredIdentifiers = null)
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
            AddDependency(dependencies, path, isInvocation, ignoredIdentifiers);

            i = end;
        }

        return dependencies.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
    }

    private static void AddDependency(HashSet<string> dependencies, string path, bool isInvocation, IReadOnlySet<string>? ignoredIdentifiers)
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

        var root = GetRootIdentifier(path);
        if (ignoredIdentifiers != null && !string.IsNullOrWhiteSpace(root) && ignoredIdentifiers.Contains(root))
            return;

        if (!string.IsNullOrWhiteSpace(path))
        {
            dependencies.Add(path);
        }
    }

    private static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);

    private static bool IsReservedToken(string token) => ReservedTokens.Contains(token);

    private static (string[] Dependencies, string[] DeclaredIdentifiers) AnalyzeCodeBlock(string code)
    {
        var declaredIdentifiers = ExtractDeclaredIdentifiers(code);
        var dependencies = ExtractDependencies(code, declaredIdentifiers);
        return (dependencies, declaredIdentifiers.OrderBy(static x => x, StringComparer.Ordinal).ToArray());
    }

    private static HashSet<string> ExtractDeclaredIdentifiers(string code)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var sanitized = SanitizeCodeForAnalysis(code);

        AddDeclaredIdentifiers(declared, VariableDeclarationRegex.Matches(sanitized), "name");
        AddDeclaredIdentifiers(declared, ForeachDeclarationRegex.Matches(sanitized), "name");
        AddDeclaredIdentifiers(declared, CatchDeclarationRegex.Matches(sanitized), "name");

        foreach (Match match in LocalFunctionDeclarationRegex.Matches(sanitized))
        {
            AddDeclaredIdentifier(declared, match.Groups["name"].Value);

            var parameters = match.Groups["params"].Value;
            foreach (Match parameterMatch in ParameterNameRegex.Matches(parameters))
            {
                AddDeclaredIdentifier(declared, parameterMatch.Groups["name"].Value);
            }
        }

        return declared;
    }

    private static void AddDeclaredIdentifiers(HashSet<string> declared, MatchCollection matches, string groupName)
    {
        foreach (Match match in matches)
        {
            AddDeclaredIdentifier(declared, match.Groups[groupName].Value);
        }
    }

    private static void AddDeclaredIdentifier(HashSet<string> declared, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsReservedToken(value))
        {
            declared.Add(value);
        }
    }

    private static string SanitizeCodeForAnalysis(string code)
    {
        var chars = code.ToCharArray();
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escaped = false;
        var verbatimString = false;

        for (var i = 0; i < chars.Length; i++)
        {
            var current = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                }
                else
                {
                    chars[i] = ' ';
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i++;
                    inBlockComment = false;
                }
                else if (current != '\r' && current != '\n')
                {
                    chars[i] = ' ';
                }

                continue;
            }

            if (inString)
            {
                if (!verbatimString && escaped)
                {
                    chars[i] = ' ';
                    escaped = false;
                    continue;
                }

                if (!verbatimString && current == '\\')
                {
                    chars[i] = ' ';
                    escaped = true;
                    continue;
                }

                if (verbatimString && current == '"' && next == '"')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i++;
                    continue;
                }

                chars[i] = ' ';
                if (current == '"')
                {
                    inString = false;
                    verbatimString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (escaped)
                {
                    chars[i] = ' ';
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    chars[i] = ' ';
                    escaped = true;
                    continue;
                }

                chars[i] = ' ';
                if (current == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i++;
                inBlockComment = true;
                continue;
            }

            if (current == '@' && next == '"')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i++;
                inString = true;
                verbatimString = true;
                continue;
            }

            if (current == '"')
            {
                chars[i] = ' ';
                inString = true;
                verbatimString = false;
                continue;
            }

            if (current == '\'')
            {
                chars[i] = ' ';
                inChar = true;
            }
        }

        return new string(chars);
    }

    private static string BuildExpressionId(string sourcePath, string expression)
    {
        var key = $"{sourcePath}|{expression}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return "rzx_" + hash.Substring(0, 16).ToLowerInvariant();
    }

    private static string GenerateRegistryCode(
        IEnumerable<(string Id, string Expression, string[] Dependencies)> rows,
        IEnumerable<TemplateInfo> templates,
        IReadOnlyList<(string SimpleName, string FullName)> namespaceTypes = null!)
    {
        var uniqueRows = rows
            .GroupBy(static r => r.Expression, StringComparer.Ordinal)
            .Select(static g => g.First())
            .OrderBy(static r => r.Expression, StringComparer.Ordinal)
            .ToArray();

        var uniqueTemplates = templates
            .GroupBy(static t => t.Key, StringComparer.Ordinal)
            .Select(static g => g.First())
            .OrderBy(static t => t.Key, StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Diagnostics.CodeAnalysis;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using Jalium.UI.Markup;");
        sb.AppendLine();
        sb.AppendLine("internal static class JalxamlRazorMetadataRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JalxamlRazorMetadataRegistry))]");
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

            // Generate pre-compiled evaluator delegate (AOT-safe, no Roslyn needed at runtime).
            var evaluatorBody = TryGenerateEvaluatorBody(row.Expression, row.Dependencies);
            if (evaluatorBody != null)
            {
                sb.Append("        RazorExpressionRegistry.RegisterEvaluator(")
                    .Append(ToStringLiteral(row.Expression)).Append(", ")
                    .Append(evaluatorBody)
                    .AppendLine(");");
            }
        }

        foreach (var template in uniqueTemplates)
        {
            var body = GenerateTemplateEvaluatorBody(template);
            sb.Append("        RazorExpressionRegistry.RegisterTemplateEvaluator(")
                .Append(ToStringLiteral(template.Key)).Append(", ")
                .Append(body)
                .AppendLine(");");
        }

        // Register types discovered from @using directives
        if (namespaceTypes is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("        // Types from @using directives");
            foreach (var (simpleName, fullName) in namespaceTypes)
            {
                sb.Append("        RazorExpressionRegistry.RegisterNamespaceType(")
                    .Append(ToStringLiteral(simpleName)).Append(", typeof(global::")
                    .Append(fullName)
                    .AppendLine("));");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a lambda body for the expression that can be compiled statically.
    /// The lambda signature is: <c>Func&lt;Func&lt;string, object?&gt;, object?&gt;</c>
    /// where the parameter resolves property paths to values at runtime.
    /// </summary>
    private static string? TryGenerateEvaluatorBody(string expression, string[] dependencies)
    {
        // Extract root identifiers from dependencies.
        var roots = dependencies
            .Select(static d =>
            {
                var dot = d.IndexOf('.');
                var bracket = d.IndexOf('[');
                if (dot < 0) return bracket < 0 ? d : d[..bracket];
                if (bracket < 0) return d[..dot];
                return d[..Math.Min(dot, bracket)];
            })
            .Where(static r => !string.IsNullOrWhiteSpace(r) && IsValidCSharpIdentifier(r))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (roots.Length == 0 && dependencies.Length == 0)
            return null;

        var sb = new StringBuilder();
        sb.Append("__resolve => { ");

        // Declare each root as dynamic resolved from the resolver.
        foreach (var root in roots)
        {
            sb.Append("dynamic ").Append(root).Append(" = __resolve(")
                .Append(ToStringLiteral(root)).Append("); ");
        }

        sb.Append("return (object?)(").Append(expression).Append("); }");
        return sb.ToString();
    }

    private static bool IsValidCSharpIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!(value[0] == '_' || char.IsLetter(value[0])))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        // Exclude C# keywords that could conflict.
        return value is not ("true" or "false" or "null" or "new" or "this" or "base"
            or "var" or "dynamic" or "string" or "int" or "bool" or "double" or "float"
            or "object" or "void" or "class" or "struct" or "enum" or "return" or "if"
            or "else" or "for" or "foreach" or "while" or "do" or "switch" or "case"
            or "break" or "continue" or "try" or "catch" or "finally" or "throw"
            or "using" or "namespace" or "typeof" or "sizeof" or "default" or "is"
            or "as" or "in" or "out" or "ref" or "readonly" or "static" or "const"
            or "async" or "await" or "delegate" or "event" or "abstract" or "virtual"
            or "override" or "sealed" or "private" or "protected" or "public" or "internal");
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

    // --- Template extraction for @{ ... } code blocks ---

    private enum SegmentKind { Literal, Path, Expression, Code }

    private sealed class TemplateSegment
    {
        public SegmentKind Kind { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    private sealed class TemplateInfo
    {
        public string Key { get; init; } = string.Empty;
        public string[] RootIdentifiers { get; init; } = Array.Empty<string>();
        public TemplateSegment[] Segments { get; init; } = Array.Empty<TemplateSegment>();
    }

    /// <summary>
    /// Finds XML attribute values that contain @{ code blocks and parses them as templates.
    /// </summary>
    private static IEnumerable<TemplateInfo> ExtractTemplatesWithCodeBlocks(string content)
    {
        var results = new List<TemplateInfo>();
        // Find attribute values: ="..." or ='...' that contain @{
        var i = 0;
        while (i < content.Length)
        {
            if (content[i] == '=' && i + 1 < content.Length)
            {
                var quoteStart = i + 1;
                // Skip whitespace between = and quote
                while (quoteStart < content.Length && char.IsWhiteSpace(content[quoteStart]))
                    quoteStart++;

                if (quoteStart < content.Length && (content[quoteStart] == '"' || content[quoteStart] == '\''))
                {
                    var quoteChar = content[quoteStart];
                    var valueStart = quoteStart + 1;
                    var valueEnd = content.IndexOf(quoteChar, valueStart);
                    if (valueEnd > valueStart)
                    {
                        var attrValue = content[valueStart..valueEnd];
                        if (attrValue.Contains("@{", StringComparison.Ordinal))
                        {
                            var template = TryParseTemplate(attrValue);
                            if (template != null)
                                results.Add(template);
                        }

                        i = valueEnd + 1;
                        continue;
                    }
                }
            }

            // Also check text content between > and < that contains @{
            if (content[i] == '>')
            {
                var textStart = i + 1;
                var textEnd = content.IndexOf('<', textStart);
                if (textEnd > textStart)
                {
                    var textContent = content[textStart..textEnd];
                    if (textContent.Contains("@{", StringComparison.Ordinal) && textContent.Contains('@', StringComparison.Ordinal))
                    {
                        var template = TryParseTemplate(textContent);
                        if (template != null)
                            results.Add(template);
                    }

                    i = textEnd;
                    continue;
                }
            }

            i++;
        }

        return results;
    }

    /// <summary>
    /// Parses a template string into segments, matching the runtime's RazorTemplateParser.Parse logic.
    /// Returns null if no code blocks are found.
    /// </summary>
    private static TemplateInfo? TryParseTemplate(string value)
    {
        var segments = new List<TemplateSegment>();
        var literal = new StringBuilder();
        var i = 0;
        var hasCodeBlocks = false;

        while (i < value.Length)
        {
            var current = value[i];

            if (current == '\\' && i + 1 < value.Length && value[i + 1] == '@')
            {
                literal.Append('@');
                i += 2;
                continue;
            }

            if (current == '@')
            {
                if (i + 1 < value.Length && value[i + 1] == '@')
                {
                    literal.Append('@');
                    i += 2;
                    continue;
                }

                FlushLiteral(segments, literal);

                if (i + 1 < value.Length && value[i + 1] == '(')
                {
                    var expr = ParseDelimited(value, ref i, i + 2, '(', ')');
                    if (expr != null)
                    {
                        segments.Add(new TemplateSegment { Kind = SegmentKind.Expression, Text = expr.Trim() });
                        continue;
                    }
                }

                if (i + 1 < value.Length && value[i + 1] == '{')
                {
                    var code = ParseDelimited(value, ref i, i + 2, '{', '}');
                    if (code != null)
                    {
                        hasCodeBlocks = true;
                        segments.Add(new TemplateSegment { Kind = SegmentKind.Code, Text = code.Trim() });
                        continue;
                    }
                }

                // Path: @identifier.property
                var path = ParsePath(value, ref i);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    segments.Add(new TemplateSegment { Kind = SegmentKind.Path, Text = path });
                    continue;
                }

                literal.Append('@');
                i++;
                continue;
            }

            literal.Append(current);
            i++;
        }

        FlushLiteral(segments, literal);

        if (!hasCodeBlocks)
            return null;

        // Compute root identifiers and template key matching the runtime.
        var roots = ComputeTemplateRootIdentifiers(segments);
        var key = BuildTemplateKey(segments, roots);

        return new TemplateInfo
        {
            Key = key,
            RootIdentifiers = roots,
            Segments = segments.ToArray()
        };
    }

    private static void FlushLiteral(List<TemplateSegment> segments, StringBuilder literal)
    {
        if (literal.Length == 0)
            return;

        segments.Add(new TemplateSegment { Kind = SegmentKind.Literal, Text = literal.ToString() });
        literal.Clear();
    }

    private static string ParsePath(string input, ref int i)
    {
        var start = i + 1;
        if (start >= input.Length || !IsPathStart(input[start]))
            return string.Empty;

        var pos = start + 1;
        while (pos < input.Length && IsPathPart(input[pos]))
            pos++;

        i = pos;
        return input[start..pos];
    }

    private static bool IsPathStart(char c) =>
        c == '_' || c == '$' || char.IsLetter(c);

    private static bool IsPathPart(char c) =>
        char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '[' || c == ']' || c == '$';

    /// <summary>
    /// Parses balanced delimited C# content (parens or braces), matching the runtime parser.
    /// </summary>
    private static string? ParseDelimited(string input, ref int i, int start, char openChar, char closeChar)
    {
        var pos = start;
        var depth = 1;
        var inString = false;
        var inChar = false;
        var escaped = false;
        var verbatimString = false;
        var stringQuote = '\0';

        while (pos < input.Length)
        {
            var current = input[pos];
            var next = pos + 1 < input.Length ? input[pos + 1] : '\0';

            if (inString)
            {
                if (!verbatimString && escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (!verbatimString && current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == stringQuote)
                {
                    if (verbatimString && next == stringQuote)
                    {
                        pos += 2;
                        continue;
                    }

                    inString = false;
                    verbatimString = false;
                    stringQuote = '\0';
                }

                pos++;
                continue;
            }

            if (inChar)
            {
                if (escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == '\'')
                    inChar = false;

                pos++;
                continue;
            }

            if (current == '/' && next == '/')
            {
                // Skip to end of line
                pos += 2;
                while (pos < input.Length && input[pos] != '\r' && input[pos] != '\n')
                    pos++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                pos += 2;
                while (pos + 1 < input.Length && !(input[pos] == '*' && input[pos + 1] == '/'))
                    pos++;
                if (pos + 1 < input.Length) pos += 2;
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                pos++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                verbatimString =
                    (pos > 0 && input[pos - 1] == '@')
                    || (pos > 1 && ((input[pos - 1] == '$' && input[pos - 2] == '@') || (input[pos - 1] == '@' && input[pos - 2] == '$')));
                stringQuote = current;
                pos++;
                continue;
            }

            if (current == openChar)
            {
                depth++;
                pos++;
                continue;
            }

            if (current == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    var result = input[start..pos];
                    i = pos + 1;
                    return result;
                }

                pos++;
                continue;
            }

            pos++;
        }

        // Unclosed
        return null;
    }

    /// <summary>
    /// Computes root identifiers for all template segments, matching the runtime's RazorTemplate constructor logic.
    /// </summary>
    private static string[] ComputeTemplateRootIdentifiers(List<TemplateSegment> segments)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var knownLocals = new HashSet<string>(StringComparer.Ordinal);

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Path:
                    AddRootFromPath(segment.Text, knownLocals, roots);
                    break;

                case SegmentKind.Expression:
                    foreach (var dep in ExtractDependencies(segment.Text))
                        AddRootFromPath(dep, knownLocals, roots);
                    break;

                case SegmentKind.Code:
                    var analysis = AnalyzeCodeBlock(segment.Text);
                    foreach (var dep in analysis.Dependencies)
                    {
                        AddRootFromPath(dep, knownLocals, roots);
                    }

                    foreach (var declaredIdentifier in analysis.DeclaredIdentifiers)
                    {
                        knownLocals.Add(declaredIdentifier);
                    }

                    break;
            }
        }

        return roots.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
    }

    private static void AddRootFromPath(string path, HashSet<string> knownLocals, HashSet<string> roots)
    {
        var root = GetRootIdentifier(path);
        if (!string.IsNullOrWhiteSpace(root) && !knownLocals.Contains(root))
            roots.Add(root);
    }

    private static string GetRootIdentifier(string path)
    {
        var dotIndex = path.IndexOf('.');
        var bracketIndex = path.IndexOf('[');
        if (dotIndex < 0)
            return bracketIndex < 0 ? path : path[..bracketIndex];
        if (bracketIndex < 0)
            return path[..dotIndex];
        return path[..Math.Min(dotIndex, bracketIndex)];
    }

    /// <summary>
    /// Builds a template cache key matching the runtime's BuildCacheKey method.
    /// Format: root1|root2|::0:literal_text||1:path_text||2:expression_text||3:code_text||
    /// </summary>
    private static string BuildTemplateKey(List<TemplateSegment> segments, string[] roots)
    {
        var sb = new StringBuilder();
        foreach (var root in roots)
            sb.Append(root).Append('|');

        sb.Append("::");

        foreach (var segment in segments)
            sb.Append((int)segment.Kind).Append(':').Append(segment.Text).Append("||");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a pre-compiled template evaluator lambda body.
    /// Signature: Func&lt;Func&lt;string, object?&gt;, object?[]&gt;
    /// </summary>
    private static string GenerateTemplateEvaluatorBody(TemplateInfo template)
    {
        var sb = new StringBuilder();
        sb.Append("__resolve => { ");

        // Declare root variables resolved from the resolver.
        foreach (var root in template.RootIdentifiers)
        {
            if (!IsValidCSharpIdentifier(root))
                continue;

            sb.Append("dynamic ").Append(root).Append(" = __resolve(")
                .Append(ToStringLiteral(root)).Append("); ");
        }

        sb.Append("var __parts = new System.Collections.Generic.List<object?>(); ");
        sb.Append("void Write(object? value) => __parts.Add(value); ");
        sb.Append("void WriteLiteral(string value) => __parts.Add(value); ");

        foreach (var segment in template.Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Literal:
                    sb.Append("WriteLiteral(").Append(ToStringLiteral(segment.Text)).Append("); ");
                    break;

                case SegmentKind.Path:
                    sb.Append("Write((object?)(").Append(segment.Text).Append(")); ");
                    break;

                case SegmentKind.Expression:
                    sb.Append("Write((object?)(").Append(segment.Text).Append(")); ");
                    break;

                case SegmentKind.Code:
                    sb.Append(segment.Text).Append(' ');
                    break;
            }
        }

        sb.Append("return __parts.ToArray(); }");
        return sb.ToString();
    }

    // ── @using namespace type resolution ──

    private IReadOnlyList<(string SimpleName, string FullName)> ResolveNamespaceTypes(IReadOnlySet<string> namespaces)
    {
        if (namespaces.Count == 0 || ReferencePaths == null || ReferencePaths.Length == 0)
            return Array.Empty<(string, string)>();

        var result = new List<(string SimpleName, string FullName)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Collect assembly paths to scan
        var assemblyPaths = new List<string>();
        foreach (var item in ReferencePaths)
        {
            var path = item.GetMetadata("FullPath");
            if (string.IsNullOrWhiteSpace(path))
                path = item.ItemSpec;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                assemblyPaths.Add(path);
        }

        if (assemblyPaths.Count == 0)
            return Array.Empty<(string, string)>();

        try
        {
            // Use MetadataLoadContext for safe, reflection-only scanning
            var resolver = new PathAssemblyResolver(assemblyPaths);
            using var mlc = new MetadataLoadContext(resolver);

            foreach (var assemblyPath in assemblyPaths)
            {
                try
                {
                    var assembly = mlc.LoadFromAssemblyPath(assemblyPath);

                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (type.Namespace != null && namespaces.Contains(type.Namespace))
                        {
                            var simpleName = type.Name;
                            // Skip generic types with backtick (e.g. List`1)
                            if (simpleName.Contains('`'))
                                continue;
                            // Skip compiler-generated types
                            if (simpleName.StartsWith('<'))
                                continue;
                            // First-seen wins (mimics C# using resolution)
                            if (seen.Add(simpleName))
                            {
                                result.Add((simpleName, type.FullName!));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.LogMessage(MessageImportance.Low,
                        "Could not scan assembly '{0}' for @using types: {1}", assemblyPath, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.Low,
                "Could not initialize MetadataLoadContext for @using type resolution: {0}", ex.Message);
        }

        if (result.Count > 0)
        {
            Log.LogMessage(MessageImportance.Normal,
                "Resolved {0} types from @using directives: {1}",
                result.Count,
                string.Join(", ", namespaces));
        }

        return result;
    }
}
