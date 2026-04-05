using System.Xml;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Parses JALXAML files to extract information needed for code generation.
/// </summary>
public static class JalxamlParser
{
    private const string LegacyXamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string JaliumMarkupNamespace = "https://schemas.jalium.dev/jalxaml/markup";
    private const string JaliumNamespace = "http://schemas.jalium.com/jalxaml";
    private const string JaliumLegacyNamespace = "http://schemas.jalium.ui/2024";
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    // Mapping from XML element names to C# type names
    private static readonly Dictionary<string, string> TypeMappings = new()
    {
        // Controls
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

        // Layout
        { "StackPanel", "Jalium.UI.Controls.StackPanel" },
        { "Grid", "Jalium.UI.Controls.Grid" },
        { "Canvas", "Jalium.UI.Controls.Canvas" },
        { "Border", "Jalium.UI.Controls.Border" },
        { "DockPanel", "Jalium.UI.Controls.DockPanel" },
        { "WrapPanel", "Jalium.UI.Controls.WrapPanel" },
        { "UniformGrid", "Jalium.UI.Controls.Primitives.UniformGrid" },

        // Other
        { "ContentControl", "Jalium.UI.Controls.ContentControl" },
        { "ItemsControl", "Jalium.UI.Controls.ItemsControl" },
        { "UserControl", "Jalium.UI.Controls.UserControl" }
    };

    private static readonly HashSet<string> ShapeTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Rectangle",
        "Ellipse",
        "Path",
        "Line",
        "Polygon",
        "Polyline"
    };

    public static JalxamlParseResult? Parse(string content, string filePath)
    {
        var result = new JalxamlParseResult();

        // Strip Razor directives before XML parsing — they may contain
        // characters like '<' (e.g. i <= 5) that break the XML reader.
        var stripped = StripRazorCodeBlocks(content);

        try
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true
            };

            using var stringReader = new StringReader(stripped);
            using var reader = XmlReader.Create(stringReader, settings);

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    result.RootElementType = GetTypeName(reader.LocalName, reader.NamespaceURI);

                    var classAttr = GetClassAttributeValue(reader);
                    if (!string.IsNullOrEmpty(classAttr))
                        result.ClassName = classAttr;

                    ParseElement(reader, result);
                    break;
                }
            }
        }
        catch
        {
            // Fallback: use regex to extract x:Class and x:Name from the ORIGINAL content.
            // Clear partial results from the failed XML parse to avoid duplicates.
            result.NamedElements.Clear();
            result.ClassName = null;
            result.RootElementType = null;
            ParseWithRegexFallback(content, result);
        }

        return result;
    }

    private static void ParseWithRegexFallback(string content, JalxamlParseResult result)
    {
        // Extract x:Class
        var classMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"x:Class\s*=\s*[""'](?<cls>[^""']+)[""']");
        if (classMatch.Success)
            result.ClassName = classMatch.Groups["cls"].Value;

        // Extract x:Name
        var nameMatches = System.Text.RegularExpressions.Regex.Matches(
            content, @"x:Name\s*=\s*[""'](?<name>[^""']+)[""']");
        foreach (System.Text.RegularExpressions.Match m in nameMatches)
        {
            var name = m.Groups["name"].Value;
            if (string.IsNullOrEmpty(name)) continue;

            // Try to determine the element type from the preceding tag
            var beforeMatch = content.Substring(0, m.Index);
            var lastTagStart = beforeMatch.LastIndexOf('<');
            var typeName = "Jalium.UI.FrameworkElement";
            if (lastTagStart >= 0)
            {
                var tagContent = beforeMatch.Substring(lastTagStart + 1);
                var spaceIdx = tagContent.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '/' });
                var elementName = spaceIdx >= 0 ? tagContent.Substring(0, spaceIdx) : tagContent;
                if (!string.IsNullOrEmpty(elementName) && !elementName.Contains('.'))
                    typeName = TypeMappings.TryGetValue(elementName, out var mapped) ? mapped : $"Jalium.UI.Controls.{elementName}";
            }

            result.NamedElements.Add(new NamedElement { Name = name, TypeName = typeName });
        }
    }

    private static void ParseElement(XmlReader reader, JalxamlParseResult result)
    {
        var elementName = reader.LocalName;
        var typeName = GetTypeName(elementName, reader.NamespaceURI);

        // Check for x:Name attribute (legacy/new namespace + prefix fallback)
        var nameAttr = GetNameAttributeValue(reader);
        if (!string.IsNullOrEmpty(nameAttr))
        {
            result.NamedElements.Add(new NamedElement
            {
                Name = nameAttr!,
                TypeName = typeName
            });
        }

        // If empty element, return
        if (reader.IsEmptyElement)
            return;

        // Parse child elements
        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element)
            {
                // Skip property elements (e.g., Grid.RowDefinitions)
                if (!reader.LocalName.Contains('.'))
                {
                    ParseElement(reader, result);
                }
                else
                {
                    // Skip property element content
                    SkipElement(reader);
                }
            }
        }
    }

    private static void SkipElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return;

        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;
        }
    }

    private static string GetTypeName(string elementName, string namespaceUri)
    {
        if (TypeMappings.TryGetValue(elementName, out var typeName))
            return typeName;

        if (string.Equals(namespaceUri, JaliumNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(namespaceUri, JaliumLegacyNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(namespaceUri, PresentationNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(namespaceUri))
        {
            if (ShapeTypeNames.Contains(elementName))
                return $"Jalium.UI.Shapes.{elementName}";

            return $"Jalium.UI.Controls.{elementName}";
        }

        if (namespaceUri.StartsWith("clr-namespace:", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = namespaceUri.Substring("clr-namespace:".Length);
            var namespacePart = remainder
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(namespacePart))
                return $"{namespacePart}.{elementName}";
        }

        return "Jalium.UI.FrameworkElement";
    }

    private static string? GetClassAttributeValue(XmlReader reader)
    {
        var classAttr = reader.GetAttribute("Class", LegacyXamlNamespace);
        if (!string.IsNullOrEmpty(classAttr))
            return classAttr;

        classAttr = reader.GetAttribute("Class", JaliumMarkupNamespace);
        if (!string.IsNullOrEmpty(classAttr))
            return classAttr;

        return GetPrefixedAttributeFallback(reader, "Class");
    }

    private static string? GetNameAttributeValue(XmlReader reader)
    {
        var nameAttr = reader.GetAttribute("Name", LegacyXamlNamespace);
        if (!string.IsNullOrEmpty(nameAttr))
            return nameAttr;

        nameAttr = reader.GetAttribute("Name", JaliumMarkupNamespace);
        if (!string.IsNullOrEmpty(nameAttr))
            return nameAttr;

        // Compatibility: allow unprefixed Name in markup.
        nameAttr = reader.GetAttribute("Name");
        if (!string.IsNullOrEmpty(nameAttr))
            return nameAttr;

        return GetPrefixedAttributeFallback(reader, "Name");
    }

    private static string? GetPrefixedAttributeFallback(XmlReader reader, string localName)
    {
        if (!reader.HasAttributes)
            return null;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!string.Equals(reader.LocalName, localName, StringComparison.Ordinal))
                continue;

            if (string.Equals(reader.Prefix, "x", StringComparison.Ordinal))
            {
                var value = reader.Value;
                reader.MoveToElement();
                return value;
            }
        }

        reader.MoveToElement();
        return null;
    }

    /// <summary>
    /// Strips Razor directives and code blocks from JALXAML content so the remaining
    /// text is valid XML for the source generator's metadata extraction pass.
    /// In text content (outside XML tags), any <c>@</c>-prefixed content that could
    /// contain XML-invalid characters (like <c>&lt;</c> in <c>i &lt;= 5</c>) is removed.
    /// </summary>
    private static string StripRazorCodeBlocks(string content)
    {
        var sb = new System.Text.StringBuilder(content.Length);
        var i = 0;
        var inTag = false;
        var inAttr = false;
        char attrQuote = '\0';

        while (i < content.Length)
        {
            // Inside attribute value — keep as-is
            if (inAttr)
            {
                if (content[i] == attrQuote) { inAttr = false; attrQuote = '\0'; }
                sb.Append(content[i]); i++; continue;
            }

            // Inside tag — keep as-is, detect attribute starts
            if (inTag)
            {
                if (content[i] == '"' || content[i] == '\'') { inAttr = true; attrQuote = content[i]; }
                else if (content[i] == '>') inTag = false;
                sb.Append(content[i]); i++; continue;
            }

            // Tag start
            if (content[i] == '<' && i + 1 < content.Length &&
                (char.IsLetter(content[i + 1]) || content[i + 1] == '/' || content[i + 1] == '!'))
            {
                inTag = true; sb.Append(content[i]); i++; continue;
            }

            // Text content: strip any @-prefixed Razor content that may contain
            // XML-breaking characters. Keep only plain whitespace/text.
            if (content[i] == '@' && i + 1 < content.Length && content[i + 1] != '@')
            {
                // Skip until we hit the next '<' (start of XML element) or end of content,
                // preserving newlines for line number stability.
                i++;
                while (i < content.Length && content[i] != '<')
                {
                    if (content[i] == '\n') sb.Append('\n');
                    i++;
                }
                continue;
            }

            sb.Append(content[i]);
            i++;
        }

        return sb.ToString();
    }
}
