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

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true
        };

        using var stringReader = new StringReader(content);
        using var reader = XmlReader.Create(stringReader, settings);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                // Parse root element
                result.RootElementType = GetTypeName(reader.LocalName, reader.NamespaceURI);

                // Look for x:Class attribute (legacy/new namespace + prefix fallback)
                var classAttr = GetClassAttributeValue(reader);
                if (!string.IsNullOrEmpty(classAttr))
                {
                    result.ClassName = classAttr;
                }

                // Parse the entire document for x:Name elements
                ParseElement(reader, result);
                break;
            }
        }

        return result;
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
}
