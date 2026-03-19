using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Shapes;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Interactivity;
using Jalium.UI.Media;

namespace Jalium.UI.Markup;

/// <summary>
/// Provides methods for parsing XAML and creating object trees.
/// </summary>
public static class XamlReader
{
    // Shared DynamicallyAccessedMemberTypes for AOT compatibility
    private const DynamicallyAccessedMemberTypes XamlMemberTypes =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |     // For attached property Set/Get methods
        DynamicallyAccessedMemberTypes.NonPublicFields;    // For code-behind named element wiring

    /// <summary>
    /// Reads XAML input and creates an object tree.
    /// </summary>
    /// <param name="xaml">The XAML string to parse.</param>
    /// <returns>The root of the created object tree.</returns>
    public static object Parse(string xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        using var reader = new StringReader(xaml);
        return Load(reader);
    }

    /// <summary>
    /// Reads XAML from a stream and creates an object tree.
    /// </summary>
    /// <param name="stream">The stream containing XAML.</param>
    /// <returns>The root of the created object tree.</returns>
    public static object Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream);
        return Load(reader);
    }

    /// <summary>
    /// Reads XAML from a stream and creates an object tree with assembly context.
    /// </summary>
    /// <param name="stream">The stream containing XAML.</param>
    /// <param name="resourceName">The embedded resource name (used for resolving relative paths).</param>
    /// <param name="sourceAssembly">The assembly containing the resource.</param>
    /// <returns>The root of the created object tree.</returns>
    public static object Load(Stream stream, string resourceName, Assembly sourceAssembly)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(sourceAssembly);

        using var textReader = new StreamReader(stream);
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true
        };

        // Create base URI from resource name (use file-like path for relative resolution)
        var baseUri = new Uri($"resource:///{sourceAssembly.GetName().Name}/{resourceName}", UriKind.Absolute);

        using var xmlReader = XmlReader.Create(textReader, settings);
        return LoadInternal(xmlReader, null, baseUri, sourceAssembly);
    }

    /// <summary>
    /// Reads XAML from a text reader and creates an object tree.
    /// </summary>
    /// <param name="reader">The text reader containing XAML.</param>
    /// <returns>The root of the created object tree.</returns>
    public static object Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true
        };

        using var xmlReader = XmlReader.Create(reader, settings);
        return LoadInternal(xmlReader, null, null, null);
    }

    /// <summary>
    /// Loads XAML content into an existing component instance (for code-behind support).
    /// This is typically called from InitializeComponent() in code-behind classes.
    /// </summary>
    /// <param name="component">The component instance to load into.</param>
    /// <param name="resourceName">The embedded resource name of the JALXAML file.</param>
    /// <param name="assembly">The assembly containing the resource. If null, uses the assembly of the component type.</param>
    public static void LoadComponent(object component, string resourceName, Assembly? assembly = null)
    {
        LoadComponentCore(component, resourceName, null, assembly);
    }

    /// <summary>
    /// Loads a component from XAML with AOT-safe named element output.
    /// Named elements are collected into the provided dictionary instead of being wired via reflection.
    /// </summary>
    /// <param name="component">The component to load XAML into.</param>
    /// <param name="resourceName">The embedded resource name.</param>
    /// <param name="namedElements">Dictionary to populate with named elements (x:Name → element instance).</param>
    /// <param name="assembly">The assembly containing the resource.</param>
    public static void LoadComponent(object component, string resourceName, Dictionary<string, object> namedElements, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(namedElements);
        LoadComponentCore(component, resourceName, namedElements, assembly);
    }

    private static void LoadComponentCore(object component, string resourceName, Dictionary<string, object>? namedElements, Assembly? assembly)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(resourceName);

        assembly ??= component.GetType().Assembly;

        var stream = GetResourceStream(resourceName, assembly);
        if (stream == null)
        {
            throw new XamlParseException($"Cannot find embedded resource '{resourceName}' in assembly '{assembly.GetName().Name}'.");
        }

        using (stream)
        using (var textReader = new StreamReader(stream))
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true
            };

            // Create base URI from resource name (use file-like path for relative resolution)
            var baseUri = new Uri($"resource:///{assembly.GetName().Name}/{resourceName}", UriKind.Absolute);

            using var xmlReader = XmlReader.Create(textReader, settings);
            LoadInternal(xmlReader, component, baseUri, assembly, namedElementsOut: namedElements);
        }

        HotReloadRuntime.RegisterComponent(component);
    }

    private static Stream? GetResourceStream(string resourceName, Assembly assembly)
    {
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null) return stream;

        // Try with assembly name prefix
        var assemblyName = assembly.GetName().Name;
        stream = assembly.GetManifestResourceStream($"{assemblyName}.{resourceName}");
        if (stream != null) return stream;

        // Try replacing path separators
        var normalizedName = resourceName.Replace('/', '.').Replace('\\', '.');
        stream = assembly.GetManifestResourceStream(normalizedName);
        if (stream != null) return stream;

        stream = assembly.GetManifestResourceStream($"{assemblyName}.{normalizedName}");
        if (stream != null) return stream;

        // Fallback 1: exact resource name lookup with case-insensitive comparison.
        var resourceNames = assembly.GetManifestResourceNames();
        var exactIgnoreCase = resourceNames.FirstOrDefault(n =>
            string.Equals(n, resourceName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n, normalizedName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n, $"{assemblyName}.{resourceName}", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n, $"{assemblyName}.{normalizedName}", StringComparison.OrdinalIgnoreCase));

        if (exactIgnoreCase != null)
        {
            stream = assembly.GetManifestResourceStream(exactIgnoreCase);
            if (stream != null) return stream;
        }

        // Fallback 2: suffix match (handles namespace/path drift, e.g. Views/Foo.jalxaml vs Foo.jalxaml).
        var fileName = GetResourceFileName(resourceName);
        var suffixes = new[]
        {
            $".{normalizedName}",
            $".{resourceName}",
            $".{fileName}"
        };

        var suffixMatch = resourceNames.FirstOrDefault(n =>
            suffixes.Any(s => n.EndsWith(s, StringComparison.OrdinalIgnoreCase)));

        if (suffixMatch != null)
        {
            stream = assembly.GetManifestResourceStream(suffixMatch);
            if (stream != null) return stream;
        }

        return null;
    }

    private static string GetResourceFileName(string resourceName)
    {
        var fileName = resourceName.Replace('\\', '/').Split('/').LastOrDefault() ?? resourceName;
        var extensionIndex = fileName.LastIndexOf('.');
        if (extensionIndex <= 0)
        {
            return fileName;
        }

        var extension = fileName.Substring(extensionIndex);
        var stem = fileName.Substring(0, extensionIndex);
        var simpleStem = stem.Split('.').LastOrDefault();
        return string.IsNullOrEmpty(simpleStem) ? fileName : $"{simpleStem}{extension}";
    }

    private static object LoadInternal(XmlReader reader, object? existingInstance, Uri? baseUri, Assembly? sourceAssembly,
        ResourceDictionary? parentResourceDictionary = null, Dictionary<string, object>? namedElementsOut = null)
    {
        var context = new XamlParserContext
        {
            BaseUri = baseUri,
            SourceAssembly = sourceAssembly,
            ParentResourceDictionary = parentResourceDictionary,
            CodeBehindInstance = existingInstance
        };

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var result = ParseElement(reader, context, existingInstance);
                    RegisterNamedElementsInScope(result, context.NamedElements);
                    if (existingInstance != null)
                    {
                        if (namedElementsOut != null)
                        {
                            // AOT-safe path: return named elements to caller for explicit wiring
                            foreach (var (name, element) in context.NamedElements)
                                namedElementsOut[name] = element;
                        }
                        else
                        {
                            // Legacy path: wire up named elements via reflection
                            WireUpNamedElements(existingInstance, context.NamedElements);
                        }
                    }
                    return result;
            }
        }

        throw new XamlParseException("No root element found in XAML.");
    }

    private static void RegisterNamedElementsInScope(object root, Dictionary<string, object> namedElements)
    {
        if (root is not DependencyObject dependencyObject || namedElements.Count == 0)
        {
            return;
        }

        var nameScope = NameScope.GetNameScope(dependencyObject);
        if (nameScope == null)
        {
            nameScope = new NameScope();
            NameScope.SetNameScope(dependencyObject, nameScope);
        }

        foreach (var (name, element) in namedElements)
        {
            var existing = nameScope.FindName(name);
            if (ReferenceEquals(existing, element))
            {
                continue;
            }

            if (existing != null)
            {
                nameScope.UnregisterName(name);
            }

            nameScope.RegisterName(name, element);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    [UnconditionalSuppressMessage("AOT", "IL2075:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static void WireUpNamedElements(object component, Dictionary<string, object> namedElements)
    {
        var type = component.GetType();
        var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var (name, element) in namedElements)
        {
            // Try to find a field with the same name
            var field = type.GetField(name, bindingFlags) ?? type.GetField($"_{name}", bindingFlags);
            if (field != null && field.FieldType.IsAssignableFrom(element.GetType()))
            {
                field.SetValue(component, element);
                continue;
            }

            // Try to find a property with the same name
            var property = type.GetProperty(name, bindingFlags);
            if (property != null && property.CanWrite && property.PropertyType.IsAssignableFrom(element.GetType()))
            {
                property.SetValue(component, element);
            }
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2067:Target parameter argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    [UnconditionalSuppressMessage("AOT", "IL2072:Target parameter argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static object ParseElement(XmlReader reader, XamlParserContext context, object? existingInstance = null)
    {
        var elementName = reader.LocalName;
        var namespaceUri = reader.NamespaceURI;
        var lineInfo = reader as IXmlLineInfo;
        var lineNumber = lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
        var linePosition = lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0;

        object instance;
        Type? resolvedType = null;

        if (existingInstance != null)
        {
            // Use existing instance for root element (code-behind support)
            instance = existingInstance;
        }
        else
        {
            // Resolve the type and create new instance (AOT-safe: types are pre-registered)
            resolvedType = context.ResolveType(namespaceUri, elementName);
            if (resolvedType == null)
            {
                throw new XamlParseException($"Cannot resolve type '{elementName}' in namespace '{namespaceUri}'.");
            }

            if (resolvedType == typeof(string))
            {
                instance = string.Empty;
            }
            else
            {
                instance = Activator.CreateInstance(resolvedType)
                    ?? throw new XamlParseException($"Failed to create instance of type '{resolvedType.FullName}'.");
            }
        }

        // Parse attributes before pushing the current element so ResourceDictionary Source
        // can replace the placeholder instance with an x:Class-derived dictionary.
        if (reader.HasAttributes)
        {
            instance = ParseAttributes(reader, instance, context);
        }

        // Push to parent stack for context tracking
        context.PushParent(instance);

        try
        {

            // Special handling for ControlTemplate - capture inner XML for deferred parsing
            if (instance is ControlTemplate controlTemplate && !reader.IsEmptyElement)
            {
                ParseControlTemplateContent(reader, controlTemplate, context);
            }
            // Special handling for DataTemplate - capture inner XML for deferred parsing
            else if (instance is DataTemplate dataTemplate && !reader.IsEmptyElement)
            {
                ParseDataTemplateContent(reader, dataTemplate, context);
            }
            // Parse child content normally
            else if (!reader.IsEmptyElement)
            {
                instance = ParseContent(reader, instance, context);
            }

            if (instance is Grid grid)
            {
                context.ResolvePendingGridReferences(grid);
            }

            // Post-process Setter to convert Value based on Property type
            if (instance is Setter setter)
            {
                PostProcessSetter(setter, context, lineNumber, linePosition);
            }
            // Post-process PropertyTrigger to convert Value based on Property type
            else if (instance is PropertyTrigger propertyTrigger)
            {
                PostProcessPropertyTrigger(propertyTrigger, context, lineNumber, linePosition);
            }
            // Post-process DataTrigger to convert Value based on the binding's result type
            else if (instance is DataTrigger dataTrigger)
            {
                PostProcessDataTrigger(dataTrigger, context);
            }
            // Post-process MultiTrigger to convert Condition values based on Property type
            else if (instance is MultiTrigger multiTrigger)
            {
                PostProcessMultiTrigger(multiTrigger, context, lineNumber, linePosition);
            }

            return instance;
        }
        finally
        {
            context.PopParent();
        }
    }

    private static object ParseAttributes(XmlReader reader, object instance, XamlParserContext context)
    {
        var currentInstance = instance;

        for (int i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);

            var attrName = reader.LocalName;
            var attrValue = reader.Value;
            var prefix = reader.Prefix;

            // Skip xmlns declarations
            if (prefix == "xmlns" || reader.Name == "xmlns")
            {
                continue;
            }

            // Handle x: directives
            if (prefix == "x")
            {
                HandleXDirective(currentInstance, attrName, attrValue, context);
                continue;
            }

            // Check for attached property (e.g., Grid.Row)
            if (attrName.Contains('.'))
            {
                SetAttachedProperty(currentInstance, attrName, attrValue, context);
            }
            else
            {
                // Regular property
                currentInstance = SetProperty(currentInstance, attrName, attrValue, context, reader);
            }
        }

        reader.MoveToElement();
        return currentInstance;
    }

    private static void HandleXDirective(object instance, string directive, string value, XamlParserContext context)
    {
        switch (directive)
        {
            case "Name":
                if (instance is FrameworkElement fe)
                {
                    fe.Name = value;
                }
                else
                {
                    var nameProperty = instance.GetType().GetProperty(nameof(FrameworkElement.Name));
                    if (nameProperty?.CanWrite == true && nameProperty.PropertyType == typeof(string))
                    {
                        nameProperty.SetValue(instance, value);
                    }
                }
                // Register the named element for code-behind wiring
                context.RegisterNamedElement(value, instance);
                break;
            case "Key":
                // Store the x:Key for use when adding to ResourceDictionary
                context.SetCurrentResourceKey(value);
                break;
            case "Class":
                // Used for code-behind, stored but not processed here
                break;
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    [UnconditionalSuppressMessage("AOT", "IL2075:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static void SetAttachedProperty(object instance, string propertyPath, string value, XamlParserContext context)
    {
        var parts = propertyPath.Split('.');
        if (parts.Length != 2)
        {
            throw new XamlParseException($"Invalid attached property syntax: {propertyPath}");
        }

        var ownerTypeName = parts[0];
        var propertyName = parts[1];

        // Resolve the owner type
        var ownerType = context.ResolveType("http://schemas.microsoft.com/winfx/2006/xaml/presentation", ownerTypeName);
        if (ownerType == null)
        {
            // Try to find in current assembly
            ownerType = FindTypeByName(ownerTypeName);
        }

        if (ownerType == null)
        {
            throw new XamlParseException($"Cannot resolve attached property owner type: {ownerTypeName}");
        }

        // Find the Set method (e.g., Grid.SetRow)
        var setMethod = ownerType.GetMethod($"Set{propertyName}", BindingFlags.Public | BindingFlags.Static);
        if (setMethod != null)
        {
            var parameters = setMethod.GetParameters();
            if (parameters.Length == 2)
            {
                var targetType = parameters[1].ParameterType;
                object? convertedValue = null;

                if (ownerType == typeof(Grid) &&
                    instance is UIElement element &&
                    targetType == typeof(int) &&
                    (propertyName == "Row" || propertyName == "Column"))
                {
                    if (!int.TryParse(value, out var index))
                    {
                        var parentGrid = context.FindParent<Grid>();
                        if (parentGrid == null)
                        {
                            throw new XamlParseException(
                                $"Named Grid.{propertyName} reference '{value}' requires the element to be inside a Grid.");
                        }

                        var resolved = propertyName == "Row"
                            ? GridDefinitionParser.TryResolveRowReference(parentGrid, value, out index)
                            : GridDefinitionParser.TryResolveColumnReference(parentGrid, value, out index);

                        if (!resolved)
                        {
                            context.AddPendingGridReference(parentGrid, element, propertyName, value);
                            return;
                        }
                    }

                    convertedValue = index;
                }
                else
                {
                    convertedValue = TypeConverterRegistry.ConvertValue(value, targetType);
                }

                setMethod.Invoke(null, [instance, convertedValue]);
                return;
            }
        }

        // Try DependencyProperty directly
        var dpField = ownerType.GetField($"{propertyName}Property", BindingFlags.Public | BindingFlags.Static);
        if (dpField != null && instance is DependencyObject depObj)
        {
            if (dpField.GetValue(null) is DependencyProperty dp)
            {
                var convertedValue = TypeConverterRegistry.ConvertValue(value, dp.PropertyType);
                depObj.SetValue(dp, convertedValue);
                return;
            }
        }

        throw new XamlParseException($"Cannot find attached property setter for: {propertyPath}");
    }

    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)]
    private static Type? FindTypeByName(string typeName)
    {
        // AOT-friendly: Use static type registry
        return XamlTypeRegistry.GetType(typeName);
    }

    private static object ParseContent(XmlReader reader, object instance, XamlParserContext context)
    {
        int depth = reader.Depth;
        var ifDirectiveStack = new Stack<string>();

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    // Check if this is a property element (e.g., Grid.RowDefinitions)
                    if (reader.LocalName.Contains('.'))
                    {
                        ParsePropertyElement(reader, instance, context);
                    }
                    else
                    {
                        // Child element
                        var explicitKey = TryGetXKey(reader);
                        context.ClearCurrentResourceKey(); // Clear any previous key
                        var child = ParseElement(reader, context);
                        var resourceKey = context.GetCurrentResourceKey() ?? explicitKey;

                        if (ifDirectiveStack.Count > 0)
                        {
                            var conditionExpression = BuildCombinedIfConditionExpression(ifDirectiveStack);
                            if (!ShouldIncludeConditionalChild(instance, child, conditionExpression, context))
                            {
                                context.ClearCurrentResourceKey();
                                break;
                            }
                        }

                        AddChild(instance, child, context, resourceKey);
                        context.ClearCurrentResourceKey(); // Clear after use
                    }
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    if (SupportsRazorIfDirectives(instance) &&
                        TryConsumeRazorIfDirectiveText(reader.Value, ifDirectiveStack))
                    {
                        break;
                    }

                    // Content property
                    instance = SetContentProperty(instance, reader.Value, context);
                    break;
            }
        }

        if (ifDirectiveStack.Count > 0)
        {
            throw new XamlParseException("Unclosed Razor @if block. Expected matching '}'.");
        }

        return instance;
    }

    private static bool ShouldIncludeConditionalChild(
        object parentInstance,
        object child,
        string conditionExpression,
        XamlParserContext context)
    {
        if (RazorBindingEngine.TryApplyIfVisibility(child, conditionExpression, context))
            return true;

        return RazorBindingEngine.EvaluateConditionOnce(parentInstance, context.CodeBehindInstance, conditionExpression);
    }

    private static string BuildCombinedIfConditionExpression(IEnumerable<string> expressions)
    {
        var parts = expressions
            .Reverse()
            .Select(static expr => $"({expr})")
            .ToArray();

        return parts.Length == 1 ? parts[0] : string.Join(" && ", parts);
    }

    private static bool SupportsRazorIfDirectives(object instance)
    {
        if (instance is Panel or ItemsControl or Border or Window)
            return true;

        var contentAttr = instance.GetType().GetCustomAttribute<ContentPropertyAttribute>();
        if (contentAttr == null)
            return false;

        var property = instance.GetType().GetProperty(contentAttr.Name);
        if (property == null)
            return false;

        if (typeof(System.Collections.IList).IsAssignableFrom(property.PropertyType))
            return true;

        return typeof(UIElement).IsAssignableFrom(property.PropertyType);
    }

    private static bool TryConsumeRazorIfDirectiveText(string rawText, Stack<string> ifDirectiveStack)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return true;

        var span = rawText.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
            {
                i++;
            }

            if (i >= span.Length)
                break;

            if (span[i] == '}')
            {
                if (ifDirectiveStack.Count == 0)
                    throw new XamlParseException("Unexpected Razor @if block terminator '}'.");

                ifDirectiveStack.Pop();
                i++;
                continue;
            }

            if (TryParseRazorIfStartDirectiveAt(span[i..], out var consumedLength, out var expression))
            {
                ifDirectiveStack.Push(expression);
                i += consumedLength;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryParseRazorIfStartDirectiveAt(ReadOnlySpan<char> text, out int consumedLength, out string expression)
    {
        consumedLength = 0;
        expression = string.Empty;

        if (text.Length < 3 || text[0] != '@' || text[1] != 'i' || text[2] != 'f')
            return false;

        var i = 3;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= text.Length || text[i] != '(')
            return false;

        i++;
        var exprStart = i;
        var depth = 1;
        var inString = false;
        var escaped = false;
        var quote = '\0';

        for (; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped)
            {
                escaped = false;
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

                continue;
            }

            if (c == '"' || c == '\'')
            {
                inString = true;
                quote = c;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    expression = text[exprStart..i].ToString().Trim();
                    i++;
                    break;
                }
            }
        }

        if (depth != 0 || string.IsNullOrWhiteSpace(expression))
            return false;

        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= text.Length || text[i] != '{')
            return false;

        consumedLength = i + 1;
        return true;
    }

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static void ParsePropertyElement(XmlReader reader, object instance, XamlParserContext context)
    {
        var parts = reader.LocalName.Split('.');
        if (parts.Length != 2)
        {
            throw new XamlParseException($"Invalid property element syntax: {reader.LocalName}");
        }

        var ownerTypeName = parts[0];
        var propertyName = parts[1];
        var depth = reader.Depth;
        var isEmpty = reader.IsEmptyElement;

        // Get the property
        var type = instance.GetType();
        var property = type.GetProperty(propertyName);

        if (property == null)
        {
            throw new XamlParseException($"Property '{propertyName}' not found on type '{type.Name}'");
        }

        // Check if this is a collection property
        var propertyValue = property.GetValue(instance);
        var isCollection = propertyValue != null && IsCollectionType(property.PropertyType);

        if (isEmpty)
        {
            return;
        }

        // Read the property content
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                var explicitKey = TryGetXKey(reader);
                context.ClearCurrentResourceKey();
                var childValue = ParseElement(reader, context);
                var resourceKey = context.GetCurrentResourceKey() ?? explicitKey;

                childValue = ResolveMarkupExtensionValueIfNeeded(childValue, instance, property, context);

                if (property.PropertyType == typeof(ResourceDictionary) && childValue is ResourceDictionary dictionaryValue)
                {
                    property.SetValue(instance, dictionaryValue);
                }
                else if (isCollection && propertyValue != null)
                {
                    if (propertyValue is System.Collections.IDictionary dictionary)
                    {
                        object? key = resourceKey;
                        if (key == null && childValue is Style style && style.TargetType != null)
                        {
                            key = style.TargetType;
                        }

                        if (key != null)
                        {
                            dictionary[key] = childValue;
                        }
                    }
                    else
                    {
                        // Add to collection
                        AddToCollection(propertyValue, childValue);
                    }
                }
                else
                {
                    // Set property value
                    if (!TryApplyDynamicResourceReference(instance, property, childValue))
                    {
                        property.SetValue(instance, childValue);
                    }
                }

                context.ClearCurrentResourceKey();
            }
        }
    }

    private static string? TryGetXKey(XmlReader reader)
    {
        const string xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        return reader.GetAttribute("Key", xamlNamespace);
    }

    private static object? ResolveMarkupExtensionValueIfNeeded(
        object? value,
        object targetObject,
        PropertyInfo? targetProperty,
        XamlParserContext context)
    {
        if (value is not MarkupExtension extension)
        {
            return value;
        }

        var serviceProvider = new MarkupExtensionServiceProvider();
        serviceProvider.AddService(typeof(IAmbientResourceProvider), context);

        DependencyProperty? dependencyProperty = null;
        if (targetObject is DependencyObject dependencyObject && targetProperty != null)
        {
            dependencyProperty = XamlParserContext.ResolveDependencyProperty(targetProperty.Name, dependencyObject.GetType());
        }

        object? provideValueTargetProperty = dependencyProperty ?? (object?)targetProperty;

        serviceProvider.AddService(typeof(IProvideValueTarget), new ProvideValueTarget
        {
            TargetObject = targetObject,
            TargetProperty = provideValueTargetProperty
        });

        return extension.ProvideValue(serviceProvider);
    }

    private static bool IsCollectionType(Type type)
    {
        if (type.IsArray) return true;
        if (typeof(System.Collections.IDictionary).IsAssignableFrom(type)) return true;
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(IList<>) ||
                genericDef == typeof(ICollection<>) ||
                genericDef == typeof(List<>) ||
                genericDef == typeof(IDictionary<,>))
            {
                return true;
            }
        }
        return typeof(System.Collections.IList).IsAssignableFrom(type);
    }

    private static void AddToCollection(object collection, object item)
    {
        if (collection is System.Collections.IList list)
        {
            list.Add(item);
        }
        else
        {
            var addMethod = collection.GetType().GetMethod("Add");
            addMethod?.Invoke(collection, [item]);
        }
    }

    /// <summary>
    /// Parses ControlTemplate content, capturing the visual tree XAML for deferred parsing.
    /// </summary>
    private static void ParseControlTemplateContent(XmlReader reader, ControlTemplate template, XamlParserContext context)
    {
        int depth = reader.Depth;
        var visualTreeXaml = new System.Text.StringBuilder();
        bool hasVisualTree = false;
        bool skipRead = false; // Flag to skip Read() after ReadOuterXml()

        while (skipRead || reader.Read())
        {
            skipRead = false;

            // Check for end of ControlTemplate
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            // Skip whitespace and other non-element content
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            // Check if this is a property element (e.g., ControlTemplate.Triggers)
            if (reader.LocalName.Contains('.'))
            {
                var parts = reader.LocalName.Split('.');
                if (parts.Length == 2 && parts[1] == "Triggers")
                {
                    // Parse triggers normally
                    ParseControlTemplateTriggers(reader, template, context);
                }
                else
                {
                    // Skip unknown property elements
                    SkipElement(reader);
                }
            }
            else if (!hasVisualTree)
            {
                // First non-property child is the visual tree root
                // Capture it as XML for deferred parsing
                visualTreeXaml.Append(reader.ReadOuterXml());
                hasVisualTree = true;

                // ReadOuterXml advances the reader past this element to the next node
                // We need to process the current node without calling Read() again
                skipRead = true;
            }
            else
            {
                // Only one visual tree root is allowed
                throw new XamlParseException("ControlTemplate can only have one visual tree root element.");
            }
        }

        // Store the captured XAML for deferred parsing
        if (hasVisualTree)
        {
            template.VisualTreeXaml = visualTreeXaml.ToString();
            template.SourceAssembly = context.SourceAssembly;

            // Register the XAML parser callback if not already set
            ControlTemplate.XamlParser ??= ParseTemplateXaml;
        }
    }

    /// <summary>
    /// Parses the Triggers property element of a ControlTemplate.
    /// </summary>
    private static void ParseControlTemplateTriggers(XmlReader reader, ControlTemplate template, XamlParserContext context)
    {
        int depth = reader.Depth;
        var isEmpty = reader.IsEmptyElement;

        if (isEmpty) return;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                var trigger = ParseElement(reader, context);
                if (trigger is Trigger t)
                {
                    template.Triggers.Add(t);
                }
            }
        }
    }

    /// <summary>
    /// Parses DataTemplate content, capturing the visual tree XAML for deferred parsing.
    /// </summary>
    private static void ParseDataTemplateContent(XmlReader reader, DataTemplate template, XamlParserContext context)
    {
        int depth = reader.Depth;
        var visualTreeXaml = new System.Text.StringBuilder();
        bool hasVisualTree = false;
        bool skipRead = false; // Flag to skip Read() after ReadOuterXml()

        while (skipRead || reader.Read())
        {
            skipRead = false;

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName.Contains('.'))
            {
                ParsePropertyElement(reader, template, context);
            }
            else if (!hasVisualTree)
            {
                // Capture the visual tree as XML
                visualTreeXaml.Append(reader.ReadOuterXml());
                hasVisualTree = true;

                // ReadOuterXml advances the reader past this element to the next node.
                // Re-process that node without calling Read() again.
                skipRead = true;
            }
            else
            {
                throw new XamlParseException("DataTemplate can only have one visual tree root element.");
            }
        }

        // Store the captured XAML for deferred parsing
        if (hasVisualTree)
        {
            template.VisualTreeXaml = visualTreeXaml.ToString();
            template.SourceAssembly = context.SourceAssembly;

            // Register the XAML parser callback if not already set
            DataTemplate.XamlParser ??= ParseTemplateXaml;
        }
    }

    /// <summary>
    /// Skips the current element and all its content.
    /// </summary>
    private static void SkipElement(XmlReader reader)
    {
        if (reader.IsEmptyElement) return;

        int depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Parses template XAML content and returns the root element.
    /// </summary>
    private static FrameworkElement? ParseTemplateXaml(string xaml, Assembly? sourceAssembly)
    {
        if (string.IsNullOrEmpty(xaml))
            return null;

        using var stringReader = new StringReader(xaml);
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true
        };

        using var xmlReader = XmlReader.Create(stringReader, settings);
        var context = new XamlParserContext
        {
            SourceAssembly = sourceAssembly
        };

        while (xmlReader.Read())
        {
            if (xmlReader.NodeType == XmlNodeType.Element)
            {
                var result = ParseElement(xmlReader, context);
                return result as FrameworkElement;
            }
        }

        return null;
    }

    /// <summary>
    /// Handles the Source property on ResourceDictionary by loading the external XAML file.
    /// </summary>
    private static ResourceDictionary HandleResourceDictionarySource(ResourceDictionary resourceDict, string sourceValue, XamlParserContext context)
    {
        var sourceUri = ResolveResourceDictionarySourceUri(sourceValue, context);

        // Store the Source URI on the ResourceDictionary
        resourceDict.Source = sourceUri;
        resourceDict.BaseUri = context.BaseUri;
        resourceDict.SourceAssembly = context.SourceAssembly;

        // Find the parent ResourceDictionary (the one that will contain this in MergedDictionaries)
        // The parent is needed so child XAML can reference resources from sibling dictionaries
        var parentDict = context.FindParentResourceDictionary(resourceDict);

        // Use the SourceLoader callback to load the external XAML
        // Wrap in try-catch so a single missing Source doesn't kill the entire ResourceDictionary.
        // This matches WPF behavior where MergedDictionary failures are non-fatal.
        try
        {
            if (ResourceDictionary.SourceLoader != null)
            {
                var loadedDict = ResourceDictionary.SourceLoader(resourceDict, sourceUri, context.SourceAssembly);
                if (loadedDict != null)
                {
                    loadedDict.Source = sourceUri;
                    loadedDict.BaseUri = sourceUri;
                    loadedDict.SourceAssembly = context.SourceAssembly;
                    return loadedDict;
                }
            }
            else
            {
                // No SourceLoader registered - try to load directly
                return LoadResourceDictionaryFromUri(resourceDict, sourceUri, context, parentDict);
            }
        }
        catch (XamlParseException)
        {
            // Ignore and continue loading remaining merged dictionaries.
        }

        return resourceDict;
    }

    private static Uri ResolveResourceDictionarySourceUri(string sourceValue, XamlParserContext context)
    {
        var normalizedSource = sourceValue.Replace('\\', '/');

        if (TryParsePackComponentUri(normalizedSource, out var packAssembly, out var packPath))
        {
            return BuildResourceUri(packAssembly, packPath);
        }

        if (Uri.TryCreate(normalizedSource, UriKind.Absolute, out var absoluteUri))
        {
            if (TryParsePackComponentUri(absoluteUri.ToString(), out packAssembly, out packPath))
            {
                return BuildResourceUri(packAssembly, packPath);
            }

            return absoluteUri;
        }

        if (normalizedSource.StartsWith("/", StringComparison.Ordinal))
        {
            var absolutePath = normalizedSource.TrimStart('/');
            if (context.SourceAssembly != null)
            {
                return BuildResourceUri(context.SourceAssembly.GetName().Name ?? string.Empty, absolutePath);
            }

            if (context.BaseUri != null &&
                TryParseResourceUri(context.BaseUri.ToString(), out var baseAssembly, out _))
            {
                return BuildResourceUri(baseAssembly, absolutePath);
            }

            return new Uri(absolutePath, UriKind.Relative);
        }

        if (context.BaseUri != null &&
            TryParseResourceUri(context.BaseUri.ToString(), out var relativeAssembly, out var basePath))
        {
            var slash = basePath.LastIndexOf('/');
            var baseDirectory = slash >= 0 ? basePath.Substring(0, slash + 1) : string.Empty;
            var combinedPath = CombineRelativeResourcePath(baseDirectory, normalizedSource);
            return BuildResourceUri(relativeAssembly, combinedPath);
        }

        if (context.BaseUri != null)
        {
            return new Uri(context.BaseUri, normalizedSource);
        }

        return new Uri(normalizedSource, UriKind.Relative);
    }

    private static Uri BuildResourceUri(string assemblyName, string resourcePath)
    {
        var normalizedAssembly = assemblyName.Trim('/');
        var normalizedPath = resourcePath.Replace('\\', '/').TrimStart('/');
        return new Uri($"resource:///{normalizedAssembly}/{normalizedPath}", UriKind.Absolute);
    }

    private static string CombineRelativeResourcePath(string baseDirectory, string relativePath)
    {
        var normalizedBase = baseDirectory.Replace('\\', '/').TrimStart('/');
        if (!string.IsNullOrEmpty(normalizedBase) && !normalizedBase.EndsWith("/", StringComparison.Ordinal))
        {
            normalizedBase += "/";
        }

        var dummyBase = new Uri($"http://relative-base/{normalizedBase}", UriKind.Absolute);
        return new Uri(dummyBase, relativePath).AbsolutePath.TrimStart('/');
    }

    private static bool TryParsePackComponentUri(string uriValue, out string assemblyName, out string componentPath)
    {
        assemblyName = string.Empty;
        componentPath = string.Empty;

        const string separator = ";component/";
        var separatorIndex = uriValue.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
        if (separatorIndex < 0)
            return false;

        var assemblyPart = uriValue.Substring(0, separatorIndex);
        var slashIndex = assemblyPart.LastIndexOf('/');
        if (slashIndex >= 0)
        {
            assemblyPart = assemblyPart[(slashIndex + 1)..];
        }

        var path = uriValue.Substring(separatorIndex + separator.Length).TrimStart('/');
        if (string.IsNullOrWhiteSpace(assemblyPart) || string.IsNullOrWhiteSpace(path))
            return false;

        assemblyName = assemblyPart;
        componentPath = path;
        return true;
    }

    private static bool TryParseResourceUri(string uriText, out string assemblyName, out string resourcePath)
    {
        const string prefix = "resource:///";
        assemblyName = string.Empty;
        resourcePath = string.Empty;

        if (!uriText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uriText.Substring(prefix.Length);
        var slash = path.IndexOf('/');
        if (slash < 0)
        {
            assemblyName = path;
            return !string.IsNullOrWhiteSpace(assemblyName);
        }

        assemblyName = path.Substring(0, slash);
        resourcePath = path.Substring(slash + 1).TrimStart('/');
        return !string.IsNullOrWhiteSpace(assemblyName);
    }

    /// <summary>
    /// Loads a ResourceDictionary from a URI using embedded resources.
    /// </summary>
    private static ResourceDictionary LoadResourceDictionaryFromUri(ResourceDictionary resourceDict, Uri sourceUri, XamlParserContext context, ResourceDictionary? parentDict = null)
    {
        var assembly = context.SourceAssembly;
        var uriString = sourceUri.ToString();
        string resourcePath;

        if (TryParsePackComponentUri(uriString, out var packAssembly, out var packPath))
        {
            resourcePath = packPath;
            var loadedAssembly = FindAssemblyByName(packAssembly);
            if (loadedAssembly != null)
            {
                assembly = loadedAssembly;
            }
        }
        else if (TryParseResourceUri(uriString, out var resourceAssembly, out var parsedPath))
        {
            resourcePath = parsedPath;
            var loadedAssembly = FindAssemblyByName(resourceAssembly);
            if (loadedAssembly != null)
            {
                assembly = loadedAssembly;
            }
        }
        else if (sourceUri.IsAbsoluteUri)
        {
            resourcePath = sourceUri.LocalPath.TrimStart('/');
        }
        else
        {
            resourcePath = uriString.TrimStart('/');
        }

        // If assembly is still null, try to get it from the ResourceDictionary or find by convention
        if (assembly == null)
        {
            // Try to get from the ResourceDictionary's stored assembly
            assembly = resourceDict.SourceAssembly;
        }

        if (assembly == null)
        {
            // AOT-safe: Use compile-time reference for Controls assembly
            assembly = typeof(Jalium.UI.Controls.Control).Assembly;
        }

        if (assembly == null)
        {
            throw new XamlParseException($"Cannot load ResourceDictionary from '{sourceUri}': no source assembly available.");
        }

        var pathCandidates = BuildResourcePathCandidates(resourcePath).ToList();
        if (pathCandidates.Count == 0)
        {
            throw new XamlParseException($"Cannot load ResourceDictionary from '{sourceUri}': resolved resource path is empty.");
        }

        var attemptedCandidates = new List<string>();
        Stream? stream = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pathCandidate in pathCandidates)
        {
            foreach (var manifestCandidate in BuildManifestLookupCandidates(pathCandidate, assembly.GetName().Name ?? string.Empty))
            {
                if (!seen.Add(manifestCandidate))
                    continue;

                attemptedCandidates.Add(manifestCandidate);
                stream = GetResourceStream(manifestCandidate, assembly);
                if (stream != null)
                {
                    break;
                }
            }

            if (stream != null)
                break;
        }

        if (stream == null)
        {
            throw new XamlParseException(
                $"Cannot find embedded ResourceDictionary for '{sourceUri}' in assembly '{assembly.GetName().Name}'. " +
                $"Candidates=[{string.Join(", ", attemptedCandidates)}].");
        }

        using (stream)
        using (var textReader = new StreamReader(stream))
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true
            };

            using var xmlReader = XmlReader.Create(textReader, settings);
            var loadedDict = (ResourceDictionary)LoadInternal(xmlReader, null, sourceUri, assembly, parentDict);
            loadedDict.Source = sourceUri;
            loadedDict.BaseUri = sourceUri;
            loadedDict.SourceAssembly = assembly;
            return loadedDict;
        }
    }

    private static IEnumerable<string> BuildResourcePathCandidates(string resourcePath)
    {
        var normalized = Uri.UnescapeDataString(resourcePath)
            .Replace('\\', '/')
            .TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (seen.Add(normalized))
            yield return normalized;

        if (normalized.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            var jalxaml = normalized.Substring(0, normalized.Length - ".xaml".Length) + ".jalxaml";
            if (seen.Add(jalxaml))
                yield return jalxaml;
        }
        else if (!normalized.EndsWith(".jalxaml", StringComparison.OrdinalIgnoreCase))
        {
            var jalxaml = $"{normalized}.jalxaml";
            if (seen.Add(jalxaml))
                yield return jalxaml;
        }
    }

    private static IEnumerable<string> BuildManifestLookupCandidates(string resourcePath, string assemblyName)
    {
        var normalizedPath = resourcePath.Replace('\\', '/').TrimStart('/');
        var dottedPath = normalizedPath.Replace('/', '.');

        yield return normalizedPath;
        yield return dottedPath;
        yield return $"{assemblyName}.{normalizedPath}";
        yield return $"{assemblyName}.{dottedPath}";
    }

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static object SetProperty(object instance, string propertyName, object? value, XamlParserContext context, XmlReader? reader = null)
    {
        var type = instance.GetType();
        var property = type.GetProperty(propertyName);

        if (property == null)
        {
            // Check if it's an event (e.g., Click="OnClick")
            if (value is string handlerName && TryWireEvent(instance, propertyName, handlerName, context))
                return instance;

            return instance;
        }

        if (!property.CanWrite)
        {
            if (TryPopulateReadOnlyCollectionProperty(instance, property, value, context))
            {
                return instance;
            }

            if (value is string handlerName && TryWireEvent(instance, propertyName, handlerName, context))
            {
                return instance;
            }

            return instance;
        }

        // Special handling for ResourceDictionary.Source
        if (instance is ResourceDictionary resourceDict && propertyName == "Source" && value is string sourceValue)
        {
            return HandleResourceDictionarySource(resourceDict, sourceValue, context);
        }

        if (value is string stringValue)
        {
            // Special handling for DependencyProperty type (for Setter.Property, PropertyTrigger.Property, Condition.Property, etc.)
            // Also handle nullable DependencyProperty? type
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType == typeof(DependencyProperty))
            {
                // Find the target type from the parent Style
                var parentStyle = context.FindParent<Style>();
                var targetType = parentStyle?.TargetType;

                if (targetType != null)
                {
                    var dp = XamlParserContext.ResolveDependencyProperty(stringValue, targetType);
                    if (dp != null)
                    {
                        property.SetValue(instance, dp);
                        return instance;
                    }
                }

                if (instance is Setter setter)
                {
                    // Defer Setter resolution to post-processing so we can handle
                    // attribute-order cases where TargetName may appear after Property.
                    setter.PropertyName = stringValue;
                    return instance;
                }

                if (instance is PropertyTrigger || instance is Condition)
                {
                    throw CreateUnresolvedDependencyPropertyException(
                        instance, propertyName, stringValue, context, reader);
                }

                throw CreateUnresolvedDependencyPropertyException(
                    instance, propertyName, stringValue, context, reader);
            }

            // Razor syntax support: @Path / @(expr) / mixed templates.
            if (RazorBindingEngine.TryApplyRazorValue(instance, property, stringValue, context, reader))
            {
                return instance;
            }

            // Check for markup extension (e.g., {Binding ...})
            if (MarkupExtensionParser.TryParse(stringValue, instance, property, context, out var extensionResult))
            {
                // Binding is already set by the extension, no need to set the property
                if (extensionResult is BindingExpressionBase)
                    return instance;

                value = extensionResult;
            }
            else if (property.PropertyType != typeof(string) && property.PropertyType != typeof(object))
            {
                // Type conversion (skip if target is object - strings are valid objects)
                value = TypeConverterRegistry.ConvertValue(stringValue, property.PropertyType);
            }
        }

        value = ResolveMarkupExtensionValueIfNeeded(value, instance, property, context);

        if (value != null)
        {
            if (TryApplyDynamicResourceReference(instance, property, value))
                return instance;

            property.SetValue(instance, value);
        }

        return instance;
    }

    private static bool TryPopulateReadOnlyCollectionProperty(
        object instance,
        PropertyInfo property,
        object? value,
        XamlParserContext context)
    {
        if (!IsCollectionType(property.PropertyType))
        {
            return false;
        }

        var targetCollection = property.GetValue(instance);
        if (targetCollection == null)
        {
            return false;
        }

        object? convertedValue = value;
        if (value is string stringValue)
        {
            convertedValue = TypeConverterRegistry.ConvertValue(stringValue, property.PropertyType);
        }

        convertedValue = ResolveMarkupExtensionValueIfNeeded(convertedValue, instance, property, context);
        if (convertedValue == null)
        {
            return false;
        }

        if (ReferenceEquals(targetCollection, convertedValue))
        {
            return true;
        }

        if (targetCollection is not System.Collections.IList targetList)
        {
            return false;
        }

        targetList.Clear();

        if (convertedValue is System.Collections.IEnumerable enumerable && convertedValue is not string)
        {
            foreach (var item in enumerable)
            {
                targetList.Add(item);
            }
        }
        else
        {
            targetList.Add(convertedValue);
        }

        return true;
    }

    private static XamlParseException CreateUnresolvedDependencyPropertyException(
        object instance,
        string targetPropertyName,
        string rawPropertyName,
        XamlParserContext context,
        XmlReader? reader)
    {
        var styleTargetType = context.FindParent<Style>()?.TargetType?.FullName ?? "<null>";
        var baseUri = context.BaseUri?.ToString() ?? "<null>";
        var lineInfoSuffix = TryGetLineInfoSuffix(reader);

        var message =
            $"Cannot resolve DependencyProperty '{rawPropertyName}' for {instance.GetType().Name}.{targetPropertyName}. " +
            $"StyleTargetType='{styleTargetType}', BaseUri='{baseUri}'{lineInfoSuffix}.";
        return new XamlParseException(message);
    }

    private static XamlParseException CreateTriggerValidationException(
        string detail,
        XamlParserContext context,
        int lineNumber,
        int linePosition)
    {
        var styleTargetType = context.FindParent<Style>()?.TargetType?.FullName ?? "<null>";
        var baseUri = context.BaseUri?.ToString() ?? "<null>";

        var location = lineNumber > 0
            ? $"Line={lineNumber}, Position={linePosition}"
            : "Line=<unknown>, Position=<unknown>";

        return new XamlParseException(
            $"{detail} StyleTargetType='{styleTargetType}', BaseUri='{baseUri}', {location}.");
    }

    private static XamlParseException CreateSetterValidationException(
        string detail,
        XamlParserContext context,
        int lineNumber,
        int linePosition)
    {
        var styleTargetType = context.FindParent<Style>()?.TargetType?.FullName ?? "<null>";
        var baseUri = context.BaseUri?.ToString() ?? "<null>";

        var location = lineNumber > 0
            ? $"Line={lineNumber}, Position={linePosition}"
            : "Line=<unknown>, Position=<unknown>";

        return new XamlParseException(
            $"{detail} StyleTargetType='{styleTargetType}', BaseUri='{baseUri}', {location}.");
    }

    private static string TryGetLineInfoSuffix(XmlReader? reader)
    {
        if (reader is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
        {
            return $", Line={lineInfo.LineNumber}, Position={lineInfo.LinePosition}";
        }

        return string.Empty;
    }

    private static bool TryWireEvent(object instance, string eventName, string handlerName, XamlParserContext context)
    {
        var codeBehind = context.CodeBehindInstance;
        if (codeBehind == null)
            return false;

        var eventInfo = instance.GetType().GetEvent(eventName);
        if (eventInfo == null)
            return false;

        var handlerType = eventInfo.EventHandlerType;
        if (handlerType == null)
            return false;

        // Find handler method on code-behind instance
        var codeBehindType = codeBehind.GetType();
        var method = codeBehindType.GetMethod(handlerName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        if (method == null)
            return false;

        var handler = Delegate.CreateDelegate(handlerType, codeBehind, method, throwOnBindFailure: false);
        if (handler == null)
            return false;

        eventInfo.AddEventHandler(instance, handler);
        return true;
    }

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static object SetContentProperty(object instance, string content, XamlParserContext context)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content))
        {
            return instance;
        }

        // Check for ContentPropertyAttribute
        var type = instance.GetType();
        var contentAttr = type.GetCustomAttribute<ContentPropertyAttribute>();

        if (contentAttr != null)
        {
            var property = type.GetProperty(contentAttr.Name);
            if (property != null)
            {
                if (RazorBindingEngine.TryApplyRazorValue(instance, property, content, context, null))
                {
                    return instance;
                }

                var value = TypeConverterRegistry.ConvertValue(content, property.PropertyType);
                property.SetValue(instance, value ?? content);
                return instance;
            }
        }

        // Default content handling
        if (instance is ContentControl cc)
        {
            if (RazorBindingEngine.TryApplyRazorValue(instance, typeof(ContentControl).GetProperty(nameof(ContentControl.Content))!, content, context, null))
            {
                return instance;
            }

            cc.Content = content;
            return instance;
        }
        else if (instance is TextBlock tb)
        {
            if (RazorBindingEngine.TryApplyRazorValue(instance, typeof(TextBlock).GetProperty(nameof(TextBlock.Text))!, content, context, null))
            {
                return instance;
            }

            tb.Text = content;
            return instance;
        }

        if (instance is string)
        {
            return content;
        }

        var converter = TypeConverterRegistry.GetConverter(instance.GetType());
        if (converter != null)
        {
            return converter.ConvertFrom(content) ?? instance;
        }

        return instance;
    }

    private static void PostProcessSetter(Setter setter, XamlParserContext context, int lineNumber, int linePosition)
    {
        var styleTargetType = context.FindParent<Style>()?.TargetType;
        if (setter.Property == null &&
            !string.IsNullOrWhiteSpace(setter.PropertyName) &&
            string.IsNullOrWhiteSpace(setter.TargetName) &&
            styleTargetType != null)
        {
            throw CreateSetterValidationException(
                $"Setter.Property '{setter.PropertyName}' cannot be resolved for the style target type when TargetName is not set.",
                context,
                lineNumber,
                linePosition);
        }

        // If Value is a string and Property is set, convert Value to the correct type
        if (setter.Property != null && setter.Value is string stringValue)
        {
            var targetType = setter.Property.PropertyType;

            // Check for markup extension first
            if (MarkupExtensionParser.TryParse(stringValue, setter, typeof(Setter).GetProperty("Value")!, context, out var extensionResult))
            {
                // For binding expressions, we store the result (could be a BindingExpressionBase or resolved value)
                if (extensionResult is not BindingExpressionBase)
                {
                    setter.Value = extensionResult;
                }
                // Note: For actual binding expressions on Setter.Value, we store the resolved value
                // from StaticResource (which returns the actual brush), not the binding itself
                return;
            }

            // Convert based on the property type
            try
            {
                var convertedValue = TypeConverterRegistry.ConvertValue(stringValue, targetType);
                if (convertedValue != null)
                {
                    setter.Value = convertedValue;
                }
            }
            catch
            {
                // If conversion fails, leave value as string - will be handled at runtime
            }
        }
    }

    private static void PostProcessPropertyTrigger(PropertyTrigger trigger, XamlParserContext context, int lineNumber, int linePosition)
    {
        if (trigger.Property == null)
        {
            throw CreateTriggerValidationException(
                "PropertyTrigger.Property is required and must resolve to a DependencyProperty.",
                context,
                lineNumber,
                linePosition);
        }

        // If Value is a string and Property is set, convert Value to the correct type
        if (trigger.Property != null && trigger.Value is string stringValue)
        {
            var targetType = trigger.Property.PropertyType;

            // Check for markup extension first
            if (MarkupExtensionParser.TryParse(stringValue, trigger, typeof(PropertyTrigger).GetProperty("Value")!, context, out var extensionResult))
            {
                if (extensionResult is not BindingExpressionBase)
                {
                    trigger.Value = extensionResult;
                }
                return;
            }

            // Convert based on the property type
            try
            {
                var convertedValue = TypeConverterRegistry.ConvertValue(stringValue, targetType);
                if (convertedValue != null)
                {
                    trigger.Value = convertedValue;
                }
            }
            catch
            {
                // If conversion fails, leave value as string - will be handled at runtime
            }
        }
    }

    private static void PostProcessDataTrigger(DataTrigger trigger, XamlParserContext context)
    {
        // DataTrigger.Value typically compares against binding results
        // For now, try basic type conversions for common types (bool, int, string, etc.)
        if (trigger.Value is string stringValue)
        {
            // Check for markup extension first
            if (MarkupExtensionParser.TryParse(stringValue, trigger, typeof(DataTrigger).GetProperty("Value")!, context, out var extensionResult))
            {
                if (extensionResult is not BindingExpressionBase)
                {
                    trigger.Value = extensionResult;
                }
                return;
            }

            // Try common conversions for DataTrigger values
            if (bool.TryParse(stringValue, out var boolValue))
            {
                trigger.Value = boolValue;
            }
            else if (int.TryParse(stringValue, out var intValue))
            {
                trigger.Value = intValue;
            }
            else if (double.TryParse(stringValue, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue))
            {
                trigger.Value = doubleValue;
            }
            // Otherwise leave as string for string comparisons
        }
    }

    private static void PostProcessMultiTrigger(MultiTrigger trigger, XamlParserContext context, int lineNumber, int linePosition)
    {
        // Post-process each condition's Value based on its Property type
        for (int i = 0; i < trigger.Conditions.Count; i++)
        {
            var condition = trigger.Conditions[i];

            if (condition.Property == null)
            {
                throw CreateTriggerValidationException(
                    $"MultiTrigger condition at index {i} is missing Property or references an unresolved DependencyProperty.",
                    context,
                    lineNumber,
                    linePosition);
            }

            if (condition.Value is not string stringValue)
                continue;

            var targetType = condition.Property.PropertyType;

            // Check for markup extension first
            if (MarkupExtensionParser.TryParse(stringValue, condition, typeof(Condition).GetProperty("Value")!, context, out var extensionResult))
            {
                if (extensionResult is not BindingExpressionBase)
                {
                    condition.Value = extensionResult;
                }
                continue;
            }

            // Convert the string value to the property's type
            try
            {
                var convertedValue = TypeConverterRegistry.ConvertValue(stringValue, targetType);
                if (convertedValue != null)
                {
                    condition.Value = convertedValue;
                }
            }
            catch
            {
                // If conversion fails, leave value as string - will be handled at runtime
            }
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static void AddChild(object parent, object child, XamlParserContext context, string? resourceKey = null)
    {
        if (parent is ResourceDictionary resourceDict)
        {
            child = ResolveMarkupExtensionValueIfNeeded(child, resourceDict, null, context)!;

            // Special handling for ResourceDictionary
            if (!string.IsNullOrEmpty(resourceKey))
            {
                // Use explicit x:Key
                resourceDict[resourceKey] = child;
            }
            else if (child is Style style && style.TargetType != null)
            {
                // Use TargetType as the implicit key for Styles without explicit x:Key
                resourceDict[style.TargetType] = style;
            }
            // else: skip resources without keys
            return;
        }
        else if (parent is Panel panel && child is UIElement element)
        {
            panel.Children.Add(element);
        }
        else if (parent is ItemsControl itemsControl && child is UIElement itemChild)
        {
            itemsControl.Items.Add(itemChild);
        }
        else if (parent is ContentControl cc)
        {
            cc.Content = child;
        }
        else if (parent is Border border && child is UIElement borderChild)
        {
            border.Child = borderChild;
        }
        else if (parent is Window window && child is UIElement windowContent)
        {
            window.Content = windowContent;
        }
        else
        {
            // Check for ContentPropertyAttribute on the parent type
            var parentType = parent.GetType();
            var contentAttr = parentType.GetCustomAttribute<ContentPropertyAttribute>();
            if (contentAttr != null)
            {
                var property = parentType.GetProperty(contentAttr.Name);
                if (property != null)
                {
                    var propertyValue = property.GetValue(parent);

                    // Check if the property is a collection (IList)
                    if (propertyValue is System.Collections.IList list)
                    {
                        list.Add(child);
                        return;
                    }

                    // Otherwise, set the property directly if writable
                    if (property.CanWrite)
                    {
                        child = ResolveMarkupExtensionValueIfNeeded(child, parent, property, context)!;
                        if (!TryApplyDynamicResourceReference(parent, property, child))
                        {
                            property.SetValue(parent, child);
                        }
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// AOT-safe assembly lookup by name. Uses compile-time known references for framework assemblies,
    /// falls back to Assembly.Load for user assemblies.
    /// </summary>
    private static Assembly? FindAssemblyByName(string assemblyName)
    {
        return assemblyName switch
        {
            "Jalium.UI.Core" => typeof(DependencyObject).Assembly,
            "Jalium.UI.Controls" => typeof(Jalium.UI.Controls.Control).Assembly,
            "Jalium.UI.Media" => typeof(Jalium.UI.Media.Brush).Assembly,
            "Jalium.UI.Xaml" => typeof(XamlReader).Assembly,
            _ => TryLoadAssembly(assemblyName)
        };
    }

    private static Assembly? TryLoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryApplyDynamicResourceReference(object targetObject, PropertyInfo property, object? value)
    {
        if (value is not IDynamicResourceReference dynamicReference)
            return false;

        if (targetObject is not FrameworkElement frameworkElement)
            return false;

        var dependencyProperty = XamlParserContext.ResolveDependencyProperty(property.Name, frameworkElement.GetType());
        if (dependencyProperty == null)
            return false;

        DynamicResourceBindingOperations.SetDynamicResource(frameworkElement, dependencyProperty, dynamicReference.ResourceKey);
        return true;
    }

}

/// <summary>
/// Context for XAML parsing operations.
/// </summary>
internal sealed class XamlParserContext : IAmbientResourceProvider
{
    private readonly Dictionary<string, string> _defaultNamespaces = new()
    {
        ["http://schemas.microsoft.com/winfx/2006/xaml/presentation"] = "Jalium.UI.Controls",
        ["http://schemas.microsoft.com/winfx/2006/xaml"] = "System",
        ["http://schemas.jalium.ui/2024"] = "Jalium.UI.Controls", // Jalium UI namespace
        ["https://schemas.jalium.dev/jalxaml"] = "Jalium.UI.Controls", // Jalium.dev namespace
        ["http://schemas.jalium.com/jalxaml"] = "Jalium.UI.Controls", // Jalium.com namespace
        [""] = "Jalium.UI.Controls" // Default namespace
    };

    private readonly Dictionary<string, Type> _typeCache = new();
    private readonly Dictionary<string, object> _namedElements = new();
    private readonly Stack<object> _parentStack = new();
    private readonly List<PendingGridReference> _pendingGridReferences = new();
    private string? _currentResourceKey;

    /// <summary>
    /// Gets or sets the base URI for resolving relative Source paths.
    /// </summary>
    public Uri? BaseUri { get; set; }

    /// <summary>
    /// Gets or sets the assembly used for loading embedded resources.
    /// </summary>
    public Assembly? SourceAssembly { get; set; }

    /// <summary>
    /// Gets or sets the parent ResourceDictionary for ambient resource lookup.
    /// This is used when loading child ResourceDictionaries (via Source) to allow
    /// them to reference resources from already-loaded sibling dictionaries.
    /// </summary>
    public ResourceDictionary? ParentResourceDictionary { get; set; }

    /// <summary>
    /// Gets or sets the code-behind instance for event handler wiring.
    /// </summary>
    public object? CodeBehindInstance { get; set; }

    /// <summary>
    /// Sets the current resource key (from x:Key attribute).
    /// </summary>
    public void SetCurrentResourceKey(string key) => _currentResourceKey = key;

    /// <summary>
    /// Gets the current resource key.
    /// </summary>
    public string? GetCurrentResourceKey() => _currentResourceKey;

    /// <summary>
    /// Clears the current resource key.
    /// </summary>
    public void ClearCurrentResourceKey() => _currentResourceKey = null;

    /// <summary>
    /// Tries to find a resource by key in the ambient resource dictionaries (parent stack and parent dictionary).
    /// Also falls back to Application resources for template parsing scenarios.
    /// </summary>
    public bool TryGetResource(object key, out object? value)
    {
        // Search through the parent stack for ResourceDictionaries
        // and FrameworkElements that have their own Resources property
        foreach (var parent in _parentStack)
        {
            if (parent is ResourceDictionary rd && rd.TryGetValue(key, out value))
            {
                return true;
            }

            if (parent is FrameworkElement fe && fe.Resources != null && fe.Resources.TryGetValue(key, out value))
            {
                return true;
            }
        }

        // Search through the parent ResourceDictionary's MergedDictionaries
        // This allows child XAML files to reference resources from sibling dictionaries
        // that were loaded earlier (e.g., Button.jalxaml can reference Colors.jalxaml resources)
        if (ParentResourceDictionary != null && ParentResourceDictionary.TryGetValue(key, out value))
        {
            return true;
        }

        // Fall back to Application resources for template parsing scenarios
        // When parsing ControlTemplate content, the parent stack is empty but
        // Application resources should still be accessible
        if (ResourceLookup.ApplicationResourceLookup != null)
        {
            value = ResourceLookup.ApplicationResourceLookup(key);
            if (value != null)
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Pushes an object onto the parent stack.
    /// </summary>
    public void PushParent(object parent) => _parentStack.Push(parent);

    /// <summary>
    /// Pops an object from the parent stack.
    /// </summary>
    public void PopParent() { if (_parentStack.Count > 0) _parentStack.Pop(); }

    /// <summary>
    /// Finds a parent of the specified type in the parent stack.
    /// </summary>
    public T? FindParent<T>() where T : class
    {
        foreach (var parent in _parentStack)
        {
            if (parent is T typed)
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Finds the parent ResourceDictionary that will contain the specified child dictionary.
    /// This skips the child itself in the parent stack.
    /// </summary>
    public ResourceDictionary? FindParentResourceDictionary(ResourceDictionary child)
    {
        bool foundChild = false;
        foreach (var parent in _parentStack)
        {
            if (parent == child)
            {
                foundChild = true;
                continue;
            }
            if (foundChild && parent is ResourceDictionary rd)
            {
                return rd;
            }
        }
        // If the child wasn't found, just return the first ResourceDictionary
        return FindParent<ResourceDictionary>();
    }

    /// <summary>
    /// Resolves a DependencyProperty by name from a target type.
    /// </summary>
    public static DependencyProperty? ResolveDependencyProperty(string propertyName, Type? targetType)
    {
        if (string.IsNullOrEmpty(propertyName) || targetType == null)
            return null;

        var fieldName = $"{propertyName}Property";

        // Search in target type and its base types explicitly
        // Using DeclaredOnly to search each type individually for reliability
        var currentType = targetType;
        while (currentType != null && currentType != typeof(object))
        {
            var dpField = currentType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (dpField != null && dpField.FieldType == typeof(DependencyProperty))
            {
                return dpField.GetValue(null) as DependencyProperty;
            }
            currentType = currentType.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Gets the dictionary of named elements (elements with x:Name).
    /// </summary>
    public Dictionary<string, object> NamedElements => _namedElements;

    /// <summary>
    /// Registers a named element for later field wiring.
    /// </summary>
    public void RegisterNamedElement(string name, object element)
    {
        _namedElements[name] = element;
    }

    public void AddPendingGridReference(Grid grid, UIElement element, string propertyName, string reference)
    {
        _pendingGridReferences.Add(new PendingGridReference(grid, element, propertyName, reference));
    }

    public void ResolvePendingGridReferences(Grid grid)
    {
        for (var i = _pendingGridReferences.Count - 1; i >= 0; i--)
        {
            var pending = _pendingGridReferences[i];
            if (!ReferenceEquals(pending.Grid, grid))
            {
                continue;
            }

            if (pending.PropertyName == "Row")
            {
                if (!GridDefinitionParser.TryResolveRowReference(grid, pending.Reference, out var rowIndex))
                {
                    throw new XamlParseException($"Cannot resolve Grid.Row reference '{pending.Reference}'.");
                }

                Grid.SetRow(pending.Element, rowIndex);
            }
            else
            {
                if (!GridDefinitionParser.TryResolveColumnReference(grid, pending.Reference, out var columnIndex))
                {
                    throw new XamlParseException($"Cannot resolve Grid.Column reference '{pending.Reference}'.");
                }

                Grid.SetColumn(pending.Element, columnIndex);
            }

            _pendingGridReferences.RemoveAt(i);
        }
    }

    public Type? ResolveType(string namespaceUri, string typeName)
    {
        var cacheKey = $"{namespaceUri}:{typeName}";
        if (_typeCache.TryGetValue(cacheKey, out var cachedType))
        {
            return cachedType;
        }

        // Try CLR namespace from the URI
        if (namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var type = ResolveClrNamespaceType(namespaceUri, typeName);
            if (type != null)
            {
                _typeCache[cacheKey] = type;
                return type;
            }
        }

        // Try default namespace mappings
        if (_defaultNamespaces.TryGetValue(namespaceUri, out var clrNamespace))
        {
            var type = ResolveTypeInNamespace(clrNamespace, typeName);
            if (type != null)
            {
                _typeCache[cacheKey] = type;
                return type;
            }
        }

        // Try multiple known namespaces
        var namespaces = new[]
        {
            "Jalium.UI.Controls",
            "Jalium.UI",
            "Jalium.UI.Media"
        };

        foreach (var ns in namespaces)
        {
            var type = ResolveTypeInNamespace(ns, typeName);
            if (type != null)
            {
                _typeCache[cacheKey] = type;
                return type;
            }
        }

        var extensionType = XamlTypeRegistry.GetType(typeName + "Extension");
        if (extensionType != null)
        {
            _typeCache[cacheKey] = extensionType;
            return extensionType;
        }

        return null;
    }

    private Type? ResolveClrNamespaceType(string namespaceUri, string typeName)
    {
        // Parse clr-namespace:MyNamespace;assembly=MyAssembly
        var ns = namespaceUri.Substring("clr-namespace:".Length);
        string? assemblyName = null;

        var semicolonIndex = ns.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            var remainder = ns.Substring(semicolonIndex + 1);
            ns = ns.Substring(0, semicolonIndex);

            if (remainder.StartsWith("assembly=", StringComparison.Ordinal))
            {
                assemblyName = remainder.Substring("assembly=".Length);
            }
        }

        return ResolveTypeInNamespace(ns, typeName, assemblyName);
    }

    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)]
    private Type? ResolveTypeInNamespace(string clrNamespace, string typeName, string? assemblyName = null)
    {
        // Try the AOT-friendly static type registry first
        var registryType = XamlTypeRegistry.GetType(typeName);
        if (registryType != null)
            return registryType;

        // Fall back to assembly-based resolution for user-defined types
        // This supports clr-namespace: references to types not in the static registry
        var fullName = $"{clrNamespace}.{typeName}";

        if (assemblyName != null)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            if (assembly != null)
            {
                var type = assembly.GetType(fullName);
                if (type != null)
                {
                    // Cache in registry for future lookups
                    XamlTypeRegistry.RegisterType(typeName, type);
                    return type;
                }
            }
        }

        // Search the source assembly first, then all loaded assemblies
        var searchAssemblies = SourceAssembly != null
            ? new[] { SourceAssembly }.Concat(AppDomain.CurrentDomain.GetAssemblies())
            : AppDomain.CurrentDomain.GetAssemblies().AsEnumerable();

        foreach (var asm in searchAssemblies)
        {
            var type = asm.GetType(fullName);
            if (type != null)
            {
                // Cache in registry for future lookups
                XamlTypeRegistry.RegisterType(typeName, type);
                return type;
            }
        }

        return null;
    }

    private readonly record struct PendingGridReference(Grid Grid, UIElement Element, string PropertyName, string Reference);
}

/// <summary>
/// Static registry of XAML types for AOT compatibility.
/// All types used in XAML must be registered here.
/// </summary>
public static class XamlTypeRegistry
{
    // AOT-safe type registry - types are preserved at compile time
    private static readonly Dictionary<string, Type> _types = InitializeTypes();

    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
        Justification = "Types are statically registered and preserved")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Types are statically registered and preserved")]
    private static Dictionary<string, Type> InitializeTypes()
    {
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);

        // Register all known XAML types
        RegisterCoreTypes(types);
        RegisterControlTypes(types);
        RegisterMediaTypes(types);
        RegisterShapeTypes(types);
        RegisterDataTypes(types);
        RegisterDocumentTypes(types);
        RegisterInteractivityTypes(types);

        return types;
    }

    private static void RegisterCoreTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI namespace (Core types)
        Register<DependencyObject>(types);
        Register<DependencyProperty>(types);
        Register<FrameworkElement>(types);
        Register<UIElement>(types);
        Register<Visual>(types);
        Register<Style>(types);
        Register<Setter>(types);
        Register<Trigger>(types);
        Register<PropertyTrigger>(types);
        Register<MultiTrigger>(types);
        Register<Condition>(types);
        Register<DataTrigger>(types);
        Register<EventTrigger>(types);
        Register<ControlTemplate>(types);
        Register<DataTemplate>(types);
        Register<ResourceDictionary>(types);
        Register<Binding>(types);
        Register<BindingBase>(types);
        Register<BindingExtension>(types);
        Register<StaticResourceExtension>(types);
        Register<DynamicResourceExtension>(types);
        Register<TemplateBindingExtension>(types);
        Register<NullExtension>(types);
        Register<TypeExtension>(types);
        Register<StaticExtension>(types);
        Register<ArrayExtension>(types);
        Register<string>(types);
        Register<Thickness>(types);
        Register<CornerRadius>(types);
        Register<GridLength>(types);
    }

    private static void RegisterControlTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Controls namespace
        Register<Application>(types);
        Register<Window>(types);
        Register<Page>(types);
        Register<Frame>(types);
        Register<Control>(types);
        Register<AccessText>(types);
        Register<ContentControl>(types);
        Register<ContentPresenter>(types);
        Register<ItemsControl>(types);
        Register<ButtonBase>(types);
        Register<Button>(types);
        Register<SplitButton>(types);
        Register<ToggleButton>(types);
        Register<TextBlock>(types);
        Register<TextBox>(types);
        Register<PasswordBox>(types);
        Register<NumberBox>(types);
        Register<RichTextBox>(types);
        Register<CheckBox>(types);
        Register<RadioButton>(types);
        Register<ComboBox>(types);
        Register<ComboBoxItem>(types);
        Register<Selector>(types);
        Register<ListBox>(types);
        Register<ListBoxItem>(types);
        Register<ListView>(types);
        Register<ListViewItem>(types);
        Register<GridView>(types);
        Register<Jalium.UI.Controls.GridViewColumn>(types);
        Register<GridViewColumnHeader>(types);
        Register<DataGrid>(types);
        Register<DataGridRow>(types);
        Register<DataGridCell>(types);
        Register<Jalium.UI.Controls.DataGridColumnHeader>(types);
        Register<Slider>(types);
        Register<ProgressBar>(types);
        Register<TabControl>(types);
        Register<TabItem>(types);
        Register<Border>(types);
        Register<Panel>(types);
        Register<StackPanel>(types);
        Register<Grid>(types);
        Register<Canvas>(types);
        Register<DockPanel>(types);
        Register<WrapPanel>(types);
        Register<ScrollViewer>(types);
        Register<Image>(types);
        Register<QRCode>(types);
        Register<ToolTip>(types);
        Register<ContentDialog>(types);
        Register<Popup>(types);
        Register<TreeView>(types);
        Register<TreeViewItem>(types);
        Register<NavigationView>(types);
        Register<NavigationViewItem>(types);
        Register<NavigationViewItemHeader>(types);
        Register<NavigationViewItemSeparator>(types);
        Register<TitleBar>(types);
        Register<TitleBarButton>(types);
        Register<RowDefinition>(types);
        Register<ColumnDefinition>(types);
        Register<ScrollBar>(types);
        Register<RepeatButton>(types);
        Register<ToggleSwitch>(types);
        Register<AutoCompleteBox>(types);
        Register<HyperlinkButton>(types);
        Register<Label>(types);
        Register<InkCanvas>(types);
        Register<MediaElement>(types);
        Register<EditControl>(types);
        Register<Markdown>(types);
        Register<Calendar>(types);
        Register<CalendarButton>(types);
        Register<CalendarDayButton>(types);
        Register<CalendarItem>(types);
        Register<DatePicker>(types);
        Register<DatePickerTextBox>(types);
        Register<TimePicker>(types);
        Register<ColorPicker>(types);
        Register<StatusBar>(types);
        Register<Jalium.UI.Controls.StatusBarItem>(types); // Fully qualified to disambiguate from Primitives.StatusBarItem
        Register<Separator>(types);
        Register<InfoBar>(types);
        Register<Thumb>(types);
        Register<Expander>(types);
        Register<GroupBox>(types);
        Register<Viewbox>(types);
        Register<GridSplitter>(types);
        Register<DockLayout>(types);
        Register<DockSplitBar>(types);
        Register<DockSplitPanel>(types);
        Register<DockTabPanel>(types);
        Register<DockItem>(types);
        Register<TransitioningContentControl>(types);

        // Icons
        Register<IconElement>(types);
        Register<SymbolIcon>(types);
        Register<FontIcon>(types);
        Register<PathIcon>(types);

        // Menus & Toolbars controls
        Register<AppBarButton>(types);
        Register<AppBarSeparator>(types);
        Register<AppBarToggleButton>(types);
        Register<CommandBar>(types);
        Register<CommandBarFlyout>(types);
        Register<MenuBar>(types);
        Register<MenuBarItem>(types);
        Register<Menu>(types);
        Register<MenuItem>(types);
        Register<ContextMenu>(types);
        Register<MenuFlyout>(types);
        Register<MenuFlyoutItem>(types);
        Register<MenuFlyoutSubItem>(types);
        Register<MenuFlyoutSeparator>(types);
        Register<ToggleMenuFlyoutItem>(types);
        Register<SwipeControl>(types);
        Register<Jalium.UI.Controls.ToolBar>(types);
        Register<ToolBarTray>(types);

        // Jalium.UI.Controls.Primitives namespace
        Register<BulletDecorator>(types);
        Register<DocumentPageView>(types);
        Register<ItemsPresenter>(types);
        Register<ResizeGrip>(types);
        Register<SelectiveScrollingGrid>(types);
        Register<TabPanel>(types);
        Register<TickBar>(types);
        Register<ToolBarOverflowPanel>(types);
        Register<ToolBarPanel>(types);
        Register<UniformGrid>(types);
        Register<VirtualizingStackPanel>(types);
    }

    private static void RegisterMediaTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Media namespace
        Register<Brush>(types);
        Register<SolidColorBrush>(types);
        Register<LinearGradientBrush>(types);
        Register<RadialGradientBrush>(types);
        Register<GradientStop>(types);
        Register<Color>(types);
        Register<ImageSource>(types);
        Register<Transform>(types);
        Register<TranslateTransform>(types);
        Register<RotateTransform>(types);
        Register<ScaleTransform>(types);
        Register<Geometry>(types);
        Register<RectangleGeometry>(types);
        Register<EllipseGeometry>(types);
        Register<PathGeometry>(types);
    }

    private static void RegisterShapeTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Controls.Shapes namespace
        Register<Shape>(types);
        Register<Ellipse>(types);
        Register<Rectangle>(types);
        Register<Jalium.UI.Controls.Shapes.Path>(types);
        Register<Line>(types);
    }

    private static void RegisterDataTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Data namespace - Converters
        Register<BooleanToVisibilityConverter>(types);
        Register<InverseBooleanConverter>(types);
        Register<NullToBooleanConverter>(types);
        Register<NullToVisibilityConverter>(types);
        Register<StringCaseConverter>(types);
        Register<EqualityConverter>(types);
        Register<MultiplyConverter>(types);
        Register<AddConverter>(types);
        Register<EnumToBooleanConverter>(types);
        Register<DateTimeFormatConverter>(types);

        // Jalium.UI.Data namespace - Collection support
        Register<CollectionViewSource>(types);
    }

    private static void RegisterDocumentTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Documents namespace
        Register<FlowDocument>(types);
        Register<Paragraph>(types);
        Register<Section>(types);
        Register<Run>(types);
        Register<Bold>(types);
        Register<Italic>(types);
        Register<Underline>(types);
        Register<Span>(types);
        Register<Hyperlink>(types);
        Register<LineBreak>(types);
        Register<InlineUIContainer>(types);
        Register<BlockUIContainer>(types);
        Register<Table>(types);
        Register<TableColumn>(types);
        Register<TableRowGroup>(types);
        Register<TableRow>(types);
        Register<TableCell>(types);
        Register<Jalium.UI.Documents.AdornerDecorator>(types);
        Register<Jalium.UI.Documents.Decorator>(types);
    }

    private static void RegisterInteractivityTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Interactivity namespace
        Register<Jalium.UI.Interactivity.EventTrigger>(types);
        Register<InvokeCommandAction>(types);
        Register<CallMethodAction>(types);
        Register<ChangePropertyAction>(types);
    }

    private static void Register<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)] T>(Dictionary<string, Type> types)
    {
        types[typeof(T).Name] = typeof(T);
    }

    /// <summary>
    /// Gets a type by its simple name.
    /// </summary>
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)]
    public static Type? GetType(string typeName)
    {
        return _types.GetValueOrDefault(typeName);
    }

    /// <summary>
    /// Registers a custom type for XAML parsing.
    /// Call this for any custom types used in XAML.
    /// </summary>
    public static void RegisterType<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)] T>()
    {
        _types[typeof(T).Name] = typeof(T);
    }

    /// <summary>
    /// Registers a custom type with a specific name.
    /// </summary>
    public static void RegisterType<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)] T>(string name)
    {
        _types[name] = typeof(T);
    }

    /// <summary>
    /// Registers a type by name for runtime-discovered types (e.g., from clr-namespace resolution).
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
        Justification = "Used for runtime clr-namespace type resolution; types are kept alive by user reference")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Used for runtime clr-namespace type resolution")]
    internal static void RegisterType(string name, Type type)
    {
        _types[name] = type;
    }

    /// <summary>
    /// Applies a compiled UI bundle to an existing component instance.
    /// This is the optimized path for pre-compiled JALXAML binary data.
    /// </summary>
    /// <param name="component">The component instance to apply the bundle to.</param>
    /// <param name="bundle">The compiled UI bundle.</param>
    public static void ApplyBundle(object component, Gpu.CompiledUIBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(bundle);

        if (component is not FrameworkElement frameworkElement)
            return;

        // Fast path: render directly from compiled DrawCommands.
        frameworkElement.SetCompiledBundle(bundle);
        var renderer = new BundleRenderer(bundle);
        frameworkElement.SetBundleRenderCallback(dc => renderer.Render(dc));

        // Build node elements once so named element wiring and fallback tree share the same instances.
        var nodeElements = BuildNodeElements(bundle);

        // Wire up named elements if the bundle/node metadata provides names.
        var componentType = component.GetType();
        foreach (var node in bundle.Nodes)
        {
            var nodeName = GetNodeName(bundle, node);
            if (string.IsNullOrWhiteSpace(nodeName))
                continue;

            if (!nodeElements.TryGetValue(node.Id, out var element))
                continue;

            var field = componentType.GetField(nodeName,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            if (field != null && field.FieldType.IsAssignableFrom(element.GetType()))
            {
                field.SetValue(component, element);
            }
        }

        // Fallback path: when no precompiled draw commands are available, materialize a basic visual tree.
        // Disable bundle callback in this mode to avoid duplicate rendering.
        if (bundle.DrawCommands.Length == 0)
        {
            var fallbackRoot = BuildFallbackVisualRoot(bundle, nodeElements);
            if (fallbackRoot != null)
            {
                frameworkElement.SetBundleRenderCallback(null);
                AttachFallbackVisual(component, fallbackRoot);
            }
        }
    }

    /// <summary>
    /// Gets the name of a node from the bundle's metadata (if available).
    /// </summary>
    private static string? GetNodeName(Gpu.CompiledUIBundle bundle, Gpu.SceneNode node)
    {
        // Future-compatible reflection probe:
        // if node types later include Name/XName metadata, wire-up starts working automatically.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var nodeType = node.GetType();

        foreach (var propertyName in new[] { "Name", "XName" })
        {
            var property = nodeType.GetProperty(propertyName, flags);
            if (property?.GetValue(node) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a FrameworkElement from a compiled scene node.
    /// </summary>
    private static FrameworkElement? CreateElementFromNode(Gpu.CompiledUIBundle bundle, Gpu.SceneNode node, Gpu.Rect absoluteBounds)
    {
        FrameworkElement? element = node switch
        {
            Gpu.RectNode rect => CreateRectElement(bundle, rect),
            Gpu.TextNode text => CreateTextElement(bundle, text),
            Gpu.ImageNode image => CreateImageElement(bundle, image),
            Gpu.PathNode path => CreatePathElement(bundle, path),
            Gpu.BackdropFilterNode backdrop => CreateBackdropElement(bundle, backdrop),
            _ => null
        };

        if (element == null)
            return null;

        ApplyNodeLayout(element, node, absoluteBounds);
        ApplyNodeTransform(bundle, node, element);
        return element;
    }

    private static Dictionary<uint, FrameworkElement> BuildNodeElements(Gpu.CompiledUIBundle bundle)
    {
        var nodeLookup = bundle.Nodes.ToDictionary(node => node.Id);
        var boundsCache = new Dictionary<uint, Gpu.Rect>(bundle.Nodes.Length);
        var elements = new Dictionary<uint, FrameworkElement>(bundle.Nodes.Length);

        foreach (var node in bundle.Nodes.OrderBy(node => node.ZIndex).ThenBy(node => node.Id))
        {
            var absoluteBounds = GetAbsoluteBounds(node.Id, nodeLookup, boundsCache);
            var element = CreateElementFromNode(bundle, node, absoluteBounds);
            if (element != null)
            {
                elements[node.Id] = element;
            }
        }

        return elements;
    }

    private static Canvas? BuildFallbackVisualRoot(Gpu.CompiledUIBundle bundle, Dictionary<uint, FrameworkElement> nodeElements)
    {
        if (nodeElements.Count == 0)
            return null;

        var root = new Canvas();
        double maxX = 0;
        double maxY = 0;

        foreach (var node in bundle.Nodes.OrderBy(node => node.ZIndex).ThenBy(node => node.Id))
        {
            if (!nodeElements.TryGetValue(node.Id, out var element))
                continue;

            root.Children.Add(element);

            if (!double.IsNaN(element.Width) && element.Width > 0)
            {
                maxX = Math.Max(maxX, Canvas.GetLeft(element) + element.Width);
            }

            if (!double.IsNaN(element.Height) && element.Height > 0)
            {
                maxY = Math.Max(maxY, Canvas.GetTop(element) + element.Height);
            }
        }

        if (maxX > 0) root.Width = maxX;
        if (maxY > 0) root.Height = maxY;
        return root;
    }

    private static void AttachFallbackVisual(object component, UIElement root)
    {
        switch (component)
        {
            case Panel panel:
                panel.Children.Clear();
                panel.Children.Add(root);
                break;

            case Border border:
                border.Child = root;
                break;

            case Window window:
                window.Content = root;
                break;

            case ContentControl contentControl:
                contentControl.Content = root;
                break;

            default:
                TryAttachFallbackChild(component, root);
                break;
        }
    }

    private static void TryAttachFallbackChild(object parent, UIElement child)
    {
        var parentType = parent.GetType();
        var contentAttr = parentType.GetCustomAttribute<ContentPropertyAttribute>();
        if (contentAttr == null)
            return;

        var property = parentType.GetProperty(contentAttr.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
            return;

        var propertyValue = property.GetValue(parent);
        if (propertyValue is System.Collections.IList list)
        {
            list.Add(child);
            return;
        }

        if (!property.CanWrite)
            return;

        if (property.PropertyType.IsAssignableFrom(child.GetType()) || property.PropertyType == typeof(object))
        {
            property.SetValue(parent, child);
        }
    }

    private static void ApplyNodeLayout(FrameworkElement element, Gpu.SceneNode node, Gpu.Rect bounds)
    {
        element.Tag = node.Id;
        element.Visibility = node.IsVisible ? Visibility.Visible : Visibility.Collapsed;

        if (bounds.Width > 0)
            element.Width = bounds.Width;
        if (bounds.Height > 0)
            element.Height = bounds.Height;

        Canvas.SetLeft(element, bounds.X);
        Canvas.SetTop(element, bounds.Y);
        Panel.SetZIndex(element, node.ZIndex);
    }

    private static void ApplyNodeTransform(Gpu.CompiledUIBundle bundle, Gpu.SceneNode node, FrameworkElement element)
    {
        if (!TryGetTransform(bundle, node.TransformIndex, out var transform))
            return;

        element.RenderTransform = transform;
    }

    private static bool TryGetTransform(Gpu.CompiledUIBundle bundle, uint transformIndex, out MatrixTransform transform)
    {
        transform = null!;
        if (transformIndex == 0)
            return false;

        var offset = (int)(transformIndex * 6);
        if (offset + 5 >= bundle.Transforms.Length)
            return false;

        transform = new MatrixTransform(new Matrix(
            bundle.Transforms[offset],
            bundle.Transforms[offset + 1],
            bundle.Transforms[offset + 2],
            bundle.Transforms[offset + 3],
            bundle.Transforms[offset + 4],
            bundle.Transforms[offset + 5]));
        return true;
    }

    private static Gpu.Rect GetAbsoluteBounds(
        uint nodeId,
        IReadOnlyDictionary<uint, Gpu.SceneNode> nodes,
        Dictionary<uint, Gpu.Rect> cache)
    {
        if (cache.TryGetValue(nodeId, out var cached))
            return cached;

        if (!nodes.TryGetValue(nodeId, out var node))
            return default;

        var local = GetLocalBounds(node);
        if (node.ParentId != 0 && nodes.ContainsKey(node.ParentId))
        {
            var parent = GetAbsoluteBounds(node.ParentId, nodes, cache);
            local = new Gpu.Rect(parent.X + local.X, parent.Y + local.Y, local.Width, local.Height);
        }

        cache[nodeId] = local;
        return local;
    }

    private static Gpu.Rect GetLocalBounds(Gpu.SceneNode node)
    {
        return node switch
        {
            Gpu.RectNode rectNode => rectNode.Bounds,
            Gpu.TextNode textNode => textNode.Bounds,
            Gpu.ImageNode imageNode => imageNode.Bounds,
            Gpu.PathNode pathNode => pathNode.Bounds,
            Gpu.BackdropFilterNode backdropNode => backdropNode.FilterRegion,
            _ => default
        };
    }

    private static FrameworkElement CreateRectElement(Gpu.CompiledUIBundle bundle, Gpu.RectNode node)
    {
        var element = new Border
        {
            CornerRadius = ToCornerRadius(node.CornerRadius),
            BorderThickness = ToThickness(node.BorderThickness)
        };

        if (TryGetMaterial(bundle, node.MaterialIndex, out var material))
        {
            element.Background = CreateBrush(material.BackgroundColor);
            element.BorderBrush = CreateBrush(material.BorderColor);
            element.Opacity = material.Opacity / 255.0;
        }

        return element;
    }

    private static FrameworkElement CreateTextElement(Gpu.CompiledUIBundle bundle, Gpu.TextNode node)
    {
        var element = new TextBlock
        {
            Text = $"#{node.TextHash:X16}"
        };

        if (TryGetMaterial(bundle, node.MaterialIndex, out var material))
        {
            element.Foreground = CreateBrush(material.ForegroundColor);
            element.Opacity = material.Opacity / 255.0;
        }

        return element;
    }

    private static FrameworkElement CreateImageElement(Gpu.CompiledUIBundle bundle, Gpu.ImageNode node)
    {
        var element = new Image();
        if (node.TextureIndex < bundle.Textures.Length)
        {
            var texture = bundle.Textures[node.TextureIndex];
            if (!string.IsNullOrWhiteSpace(texture.Path) &&
                Uri.TryCreate(texture.Path, UriKind.RelativeOrAbsolute, out var uri))
            {
                element.Source = new BitmapImage(uri);
            }
        }

        if (TryGetMaterial(bundle, node.MaterialIndex, out var material))
        {
            element.Opacity = material.Opacity / 255.0;
        }

        return element;
    }

    private static FrameworkElement CreatePathElement(Gpu.CompiledUIBundle bundle, Gpu.PathNode node)
    {
        var element = new Jalium.UI.Controls.Shapes.Path();

        if (TryGetMaterial(bundle, node.MaterialIndex, out var material))
        {
            element.Fill = CreateBrush(material.BackgroundColor);
            element.Stroke = CreateBrush(material.BorderColor);
            element.Opacity = material.Opacity / 255.0;
        }

        return element;
    }

    private static FrameworkElement CreateBackdropElement(Gpu.CompiledUIBundle bundle, Gpu.BackdropFilterNode node)
    {
        // Software fallback representation: keep bounds and opacity to preserve layout/placement.
        var element = new Border();
        if (TryGetMaterial(bundle, node.MaterialIndex, out var material))
        {
            element.Opacity = material.Opacity / 255.0;
        }

        return element;
    }

    private static bool TryGetMaterial(Gpu.CompiledUIBundle bundle, uint materialIndex, out Gpu.Material material)
    {
        if (materialIndex < bundle.Materials.Length)
        {
            material = bundle.Materials[materialIndex];
            return true;
        }

        material = default;
        return false;
    }

    private static SolidColorBrush CreateBrush(uint argb) => new(ToColor(argb));

    private static Color ToColor(uint argb)
    {
        return Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }

    private static Thickness ToThickness(Gpu.Thickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

    private static CornerRadius ToCornerRadius(Gpu.CornerRadius cornerRadius) =>
        new(cornerRadius.TopLeft, cornerRadius.TopRight, cornerRadius.BottomRight, cornerRadius.BottomLeft);

}

/// <summary>
/// Exception thrown during XAML parsing.
/// </summary>
public sealed class XamlParseException : Exception
{
    public XamlParseException(string message) : base(message) { }
    public XamlParseException(string message, Exception innerException) : base(message, innerException) { }
}

