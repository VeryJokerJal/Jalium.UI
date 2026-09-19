using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Jalium.UI.BuildSupport;
using Jalium.UI.Styling;
using Jalium.UI.Xaml.SourceGenerator;

namespace Jalium.UI.Compiler;

internal static class CssCompiler
{
    internal static int Run(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<CssCompilationManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("Empty CSS compilation manifest.");
            var entries = new List<CssCompilationEntry>();
            var resources = new HashSet<string>(StringComparer.Ordinal);
            var texts = new HashSet<(string, bool)>();
            foreach (var input in manifest.StyleSheets.OrderBy(f => f.ResourcePath, StringComparer.Ordinal))
            {
                var uri = $"/{manifest.AssemblyName};component/{input.ResourcePath.Replace('\\', '/').TrimStart('/')}";
                if (!resources.Add(uri)) throw new InvalidDataException($"Duplicate compiled CSS resource '{uri}'. Use distinct ResourcePath metadata.");
                var sheet = CssStyleSheet.Parse(File.ReadAllText(input.Path), uri);
                Report(sheet.Diagnostics, input.Path);
                entries.Add(new(uri, sheet, true));
            }
            foreach (var path in manifest.XamlFiles.Order(StringComparer.Ordinal))
            {
                foreach (var attribute in ReadCssAttributes(path))
                {
                    var name = attribute.Name;
                    var text = attribute.Value;
                    // The XAML/Razor pipeline owns bindings and expressions. Escaped
                    // literal markup-extension syntax has the same text at runtime.
                    if (text.StartsWith("{}", StringComparison.Ordinal)) text = text[2..];
                    else if (text.StartsWith('{')) continue;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var inline = name == "Css.Style";
                    if (!texts.Add((text, inline))) continue;
                    var diagnostics = new List<CssParseDiagnostic>();
                    var sheet = inline
                        ? new CssStyleSheet([new CssRule { Selectors = [], Declarations = CssParser.ParseInlineDeclarations(text, diagnostics).ToArray() }], diagnostics.ToArray(), name)
                        : CssStyleSheet.Parse(text, name);
                    Report(sheet.Diagnostics, path, attribute.Line - 1);
                    entries.Add(new(text, sheet, false, inline));
                }
            }
            var source = CssSourceEmitter.Generate(entries);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifest.OutputPath))!);
            if (!File.Exists(manifest.OutputPath) || File.ReadAllText(manifest.OutputPath) != source)
                File.WriteAllText(manifest.OutputPath, source, new UTF8Encoding(false));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or XmlException or InvalidOperationException or NotSupportedException)
        {
            Console.Error.WriteLine($"CSS compilation: error JALCSS002: {exception.Message}");
            return 1;
        }
    }

    private sealed record CssAttribute(string Name, string Value, int Line);

    private static IEnumerable<CssAttribute> ReadCssAttributes(string path)
    {
        var source = File.ReadAllText(path);
        if (!source.Contains("Css.Style", StringComparison.Ordinal)) return [];
        try
        {
            using var input = new StringReader(source);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            return document.Descendants().Attributes()
                .Where(attribute => attribute.Name.LocalName is "Css.Style" or "Css.StyleSheet")
                .Select(attribute => new CssAttribute(attribute.Name.LocalName, attribute.Value, ((IXmlLineInfo)attribute).LineNumber))
                .ToArray();
        }
        catch (XmlException)
        {
            // Razor conditions/code can contain unescaped '<' even after the
            // build transform. Use exactly the XAML generator's lowering rules.
            // XAML diagnostics belong to that generator, not this optimization.
            var parsed = JalxamlParser.Parse(source, path);
            if (parsed?.Root is not { } root)
            {
                Console.WriteLine($"{path}(1): warning JALCSS003: Static CSS could not be extracted from this JALXAML document; its existing runtime CSS path will be used.");
                return [];
            }
            var result = new List<CssAttribute>();
            var pending = new Stack<JalxamlAstNode>();
            pending.Push(root);
            while (pending.TryPop(out var node))
            {
                foreach (var attribute in node.Attributes)
                    if (attribute.AttachedOwner == "Css" && attribute.LocalName is "Style" or "StyleSheet")
                        result.Add(new("Css." + attribute.LocalName, attribute.Value, node.LineNumber));
                var children = node.Children.Concat(node.PropertyElements.SelectMany(property => property.Children));
                foreach (var child in children.Reverse()) pending.Push(child);
            }
            return result;
        }
    }

    private static void Report(IReadOnlyList<CssParseDiagnostic> diagnostics, string path, int lineOffset = 0)
    {
        foreach (var diagnostic in diagnostics)
            if (diagnostic.Severity == CssDiagnosticSeverity.Warning)
                Console.WriteLine($"{path}({Math.Max(1, diagnostic.Line + lineOffset)}): warning JALCSS001: {diagnostic.Message}");
    }
}
