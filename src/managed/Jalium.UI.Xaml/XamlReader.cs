using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
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
        DynamicallyAccessedMemberTypes.PublicFields;

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
        return LoadInternal(xmlReader, null);
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

            using var xmlReader = XmlReader.Create(textReader, settings);
            LoadInternal(xmlReader, component);
        }
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
        return stream;
    }

    private static object LoadInternal(XmlReader reader, object? existingInstance)
    {
        var context = new XamlParserContext();

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var result = ParseElement(reader, context, existingInstance);
                    // Wire up named elements to fields in the component
                    if (existingInstance != null)
                    {
                        WireUpNamedElements(existingInstance, context.NamedElements);
                    }
                    return result;
            }
        }

        throw new XamlParseException("No root element found in XAML.");
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

        // Special handling for ControlTemplate - need to capture visual tree as factory
        if (elementName == "ControlTemplate" && existingInstance == null)
        {
            return ParseControlTemplate(reader, context);
        }

        // Special handling for DataTemplate
        if (elementName == "DataTemplate" && existingInstance == null)
        {
            return ParseDataTemplate(reader, context);
        }

        object instance;

        if (existingInstance != null)
        {
            // Use existing instance for root element (code-behind support)
            instance = existingInstance;
        }
        else
        {
            // Resolve the type and create new instance (AOT-safe: types are pre-registered)
            var type = context.ResolveType(namespaceUri, elementName);
            if (type == null)
            {
                throw new XamlParseException($"Cannot resolve type '{elementName}' in namespace '{namespaceUri}'.");
            }

            instance = Activator.CreateInstance(type)
                ?? throw new XamlParseException($"Failed to create instance of type '{type.FullName}'.");
        }

        // Push to parent stack for context tracking
        context.PushParent(instance);

        try
        {
            // Parse attributes
            if (reader.HasAttributes)
            {
                ParseAttributes(reader, instance, context);
            }

            // Parse child content
            if (!reader.IsEmptyElement)
            {
                ParseContent(reader, instance, context);
            }

            // Post-process Setter to convert Value based on Property type
            if (instance is Setter setter)
            {
                PostProcessSetter(setter, context);
            }
            // Post-process PropertyTrigger to convert Value based on Property type
            else if (instance is PropertyTrigger propertyTrigger)
            {
                PostProcessPropertyTrigger(propertyTrigger, context);
            }
            // Post-process DataTrigger to convert Value based on the binding's result type
            else if (instance is DataTrigger dataTrigger)
            {
                PostProcessDataTrigger(dataTrigger, context);
            }

            return instance;
        }
        finally
        {
            context.PopParent();
        }
    }

    private static void ParseAttributes(XmlReader reader, object instance, XamlParserContext context)
    {
        var type = instance.GetType();

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
                HandleXDirective(instance, attrName, attrValue, context);
                continue;
            }

            // Check for attached property (e.g., Grid.Row)
            if (attrName.Contains('.'))
            {
                SetAttachedProperty(instance, attrName, attrValue, context);
            }
            else
            {
                // Regular property
                SetProperty(instance, attrName, attrValue, context);
            }
        }

        reader.MoveToElement();
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
                var convertedValue = TypeConverterRegistry.ConvertValue(value, targetType);
                setMethod.Invoke(null, [instance, convertedValue]);
                return;
            }
        }

        // Try DependencyProperty directly
        var dpField = ownerType.GetField($"{propertyName}Property", BindingFlags.Public | BindingFlags.Static);
        if (dpField != null && instance is DependencyObject depObj)
        {
            var dp = dpField.GetValue(null) as DependencyProperty;
            if (dp != null)
            {
                var convertedValue = TypeConverterRegistry.ConvertValue(value, dp.PropertyType);
                depObj.SetValue(dp, convertedValue);
                return;
            }
        }

        throw new XamlParseException($"Cannot find attached property setter for: {propertyPath}");
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    private static Type? FindTypeByName(string typeName)
    {
        // AOT-friendly: Use static type registry
        return XamlTypeRegistry.GetType(typeName);
    }

    private static void ParseContent(XmlReader reader, object instance, XamlParserContext context)
    {
        int depth = reader.Depth;

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
                        context.ClearCurrentResourceKey(); // Clear any previous key
                        var child = ParseElement(reader, context);
                        var resourceKey = context.GetCurrentResourceKey();
                        AddChild(instance, child, resourceKey);
                        context.ClearCurrentResourceKey(); // Clear after use
                    }
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    // Content property
                    SetContentProperty(instance, reader.Value, context);
                    break;
            }
        }
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
                var childValue = ParseElement(reader, context);

                if (isCollection && propertyValue != null)
                {
                    // Add to collection
                    AddToCollection(propertyValue, childValue);
                }
                else
                {
                    // Set property value
                    property.SetValue(instance, childValue);
                }
            }
        }
    }

    /// <summary>
    /// Parses a ControlTemplate element, capturing its visual tree as a factory function.
    /// </summary>
    private static ControlTemplate ParseControlTemplate(XmlReader reader, XamlParserContext context)
    {
        var template = new ControlTemplate();
        Type? targetType = null;

        // Parse attributes
        if (reader.HasAttributes)
        {
            for (int i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                var attrName = reader.LocalName;
                var attrValue = reader.Value;
                var prefix = reader.Prefix;

                // Skip xmlns declarations
                if (prefix == "xmlns" || reader.Name == "xmlns")
                    continue;

                if (attrName == "TargetType")
                {
                    targetType = TypeConverterRegistry.ConvertValue(attrValue, typeof(Type)) as Type;
                    template.TargetType = targetType;
                }
            }
            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
            return template;

        // Parse content
        int depth = reader.Depth;
        string? visualTreeXml = null;
        var capturedTargetType = targetType; // Capture for closure

        while (reader.Read())
        {
        ProcessElement:
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element)
            {
                var localName = reader.LocalName;

                if (localName == "ControlTemplate.Triggers")
                {
                    // Parse triggers collection
                    ParseTriggersCollection(reader, template.Triggers, context, capturedTargetType);
                }
                else if (!localName.Contains('.') && visualTreeXml == null)
                {
                    // This is the visual tree root - capture the XML (only capture once)
                    visualTreeXml = reader.ReadOuterXml();
                    // After ReadOuterXml(), reader is positioned at the next sibling element
                    // We need to process this position without calling Read() again
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                        break;
                    if (reader.NodeType == XmlNodeType.Element)
                        goto ProcessElement;
                }
            }
        }

        // Set the visual tree factory if we captured XML
        if (!string.IsNullOrEmpty(visualTreeXml))
        {
            var capturedXml = visualTreeXml;
            template.SetVisualTree(() => CreateVisualTreeFromXml(capturedXml, context));
        }

        return template;
    }

    /// <summary>
    /// Parses a DataTemplate element, capturing its content as a factory function.
    /// </summary>
    private static DataTemplate ParseDataTemplate(XmlReader reader, XamlParserContext context)
    {
        var template = new DataTemplate();

        // Parse attributes
        if (reader.HasAttributes)
        {
            for (int i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                var attrName = reader.LocalName;
                var attrValue = reader.Value;
                var prefix = reader.Prefix;

                // Skip xmlns declarations
                if (prefix == "xmlns" || reader.Name == "xmlns")
                    continue;

                if (attrName == "DataType")
                {
                    template.DataType = TypeConverterRegistry.ConvertValue(attrValue, typeof(Type)) as Type;
                }
            }
            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
            return template;

        // Parse content - find the visual tree root
        int depth = reader.Depth;
        string? visualTreeXml = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && !reader.LocalName.Contains('.'))
            {
                // This is the visual tree root - capture the XML
                visualTreeXml = reader.ReadOuterXml();
                break; // DataTemplate only has one root
            }
        }

        // Set the visual tree factory if we captured XML
        if (!string.IsNullOrEmpty(visualTreeXml))
        {
            var capturedXml = visualTreeXml;
            template.SetVisualTree(() => CreateVisualTreeFromXml(capturedXml, context));
        }

        return template;
    }

    /// <summary>
    /// Creates a visual tree from captured XML string.
    /// </summary>
    private static FrameworkElement CreateVisualTreeFromXml(string xml, XamlParserContext parentContext)
    {
        // Wrap the XML in a dummy root element with namespace declarations
        // This is necessary because ReadOuterXml() doesn't include inherited xmlns declarations
        var wrappedXml = $@"<_TemplateRoot
            xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
            xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">{xml}</_TemplateRoot>";

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            ConformanceLevel = ConformanceLevel.Document
        };

        using var stringReader = new StringReader(wrappedXml);
        using var xmlReader = XmlReader.Create(stringReader, settings);

        // Move to the wrapper element
        while (xmlReader.Read() && xmlReader.NodeType != XmlNodeType.Element) { }

        if (xmlReader.NodeType != XmlNodeType.Element || xmlReader.LocalName != "_TemplateRoot")
            throw new XamlParseException("Template visual tree is empty.");

        // Move to the actual template root element
        if (!xmlReader.Read())
            throw new XamlParseException("Template visual tree is empty.");

        while (xmlReader.NodeType != XmlNodeType.Element)
        {
            if (!xmlReader.Read())
                throw new XamlParseException("Template visual tree is empty.");
        }

        // Create a new context that inherits from parent
        var context = new XamlParserContext();
        context.IsParsingTemplate = true;

        // Parse the element
        var result = ParseElement(xmlReader, context);

        if (result is not FrameworkElement fe)
            throw new XamlParseException($"Template visual tree root must be a FrameworkElement, got {result?.GetType().Name ?? "null"}.");

        return fe;
    }

    /// <summary>
    /// Parses a Triggers collection within a ControlTemplate.
    /// </summary>
    private static void ParseTriggersCollection(XmlReader reader, IList<Trigger> triggers, XamlParserContext context, Type? targetType)
    {
        int depth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element)
            {
                var triggerName = reader.LocalName;

                // Use the standard element parsing for triggers
                context.PushTemplateTargetType(targetType);
                try
                {
                    var trigger = ParseElement(reader, context);
                    if (trigger is Trigger t)
                    {
                        triggers.Add(t);
                    }
                }
                finally
                {
                    context.PopTemplateTargetType();
                }
            }
        }
    }

    private static bool IsCollectionType(Type type)
    {
        if (type.IsArray) return true;
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(IList<>) ||
                genericDef == typeof(ICollection<>) ||
                genericDef == typeof(List<>))
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

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static void SetProperty(object instance, string propertyName, object? value, XamlParserContext context)
    {
        var type = instance.GetType();
        var property = type.GetProperty(propertyName);

        if (property == null)
        {
            // Property not found - might be a design-time only property, ignore
            return;
        }

        if (!property.CanWrite)
        {
            return;
        }

        if (value is string stringValue)
        {
            // Special handling for DependencyProperty type (for Setter.Property, PropertyTrigger.Property, etc.)
            if (property.PropertyType == typeof(DependencyProperty))
            {
                // Find the target type - first check template target type (for ControlTemplate.Triggers),
                // then fall back to parent Style's TargetType
                var targetType = context.GetTemplateTargetType();
                if (targetType == null)
                {
                    var parentStyle = context.FindParent<Style>();
                    targetType = parentStyle?.TargetType;
                }

                if (targetType != null)
                {
                    var dp = XamlParserContext.ResolveDependencyProperty(stringValue, targetType);
                    if (dp != null)
                    {
                        property.SetValue(instance, dp);

                        // Store the property name for Setters that have TargetName
                        // (need runtime resolution against actual target element type)
                        if (instance is Setter setter)
                        {
                            setter.PropertyName = stringValue;
                        }

                        return;
                    }
                }
                // If we can't resolve, skip setting the property
                return;
            }

            // Check for markup extension (e.g., {Binding ...}, {TemplateBinding ...})
            if (MarkupExtensionParser.TryParse(stringValue, instance, property, context, out var extensionResult))
            {
                // Binding is already set by the extension, no need to set the property
                if (extensionResult is BindingExpressionBase)
                    return;

                // Handle DeferredTemplateBinding - store for later resolution
                if (extensionResult is DeferredTemplateBinding deferredBinding && instance is FrameworkElement fe)
                {
                    fe.AddDeferredTemplateBinding(propertyName, deferredBinding);
                    return;
                }

                value = extensionResult;
            }
            else if (property.PropertyType != typeof(string) && property.PropertyType != typeof(object))
            {
                // Type conversion (skip if target is object - strings are valid objects)
                value = TypeConverterRegistry.ConvertValue(stringValue, property.PropertyType);
            }
        }

        if (value != null)
        {
            property.SetValue(instance, value);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static void SetContentProperty(object instance, string content, XamlParserContext context)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        // Check for ContentPropertyAttribute
        var type = instance.GetType();
        var contentAttr = type.GetCustomAttribute<ContentPropertyAttribute>();

        if (contentAttr != null)
        {
            var property = type.GetProperty(contentAttr.Name);
            if (property != null)
            {
                var value = TypeConverterRegistry.ConvertValue(content, property.PropertyType);
                property.SetValue(instance, value ?? content);
                return;
            }
        }

        // Default content handling
        if (instance is ContentControl cc)
        {
            cc.Content = content;
        }
        else if (instance is TextBlock tb)
        {
            tb.Text = content;
        }
    }

    private static void PostProcessSetter(Setter setter, XamlParserContext context)
    {
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

    private static void PostProcessPropertyTrigger(PropertyTrigger trigger, XamlParserContext context)
    {
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

    [UnconditionalSuppressMessage("AOT", "IL2070:Target method argument",
        Justification = "Types are registered in XamlTypeRegistry with DynamicallyAccessedMembers")]
    private static void AddChild(object parent, object child, string? resourceKey = null)
    {
        if (parent is ResourceDictionary resourceDict)
        {
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
                        property.SetValue(parent, child);
                        return;
                    }
                }
            }
        }
    }
}

/// <summary>
/// Context for XAML parsing operations.
/// </summary>
internal class XamlParserContext : IAmbientResourceProvider
{
    private readonly Dictionary<string, string> _defaultNamespaces = new()
    {
        ["http://schemas.microsoft.com/winfx/2006/xaml/presentation"] = "Jalium.UI.Controls",
        ["http://schemas.microsoft.com/winfx/2006/xaml"] = "System",
        ["http://schemas.jalium.ui/2024"] = "Jalium.UI.Controls", // Jalium UI namespace
        [""] = "Jalium.UI.Controls" // Default namespace
    };

    private readonly Dictionary<string, Type> _typeCache = new();
    private readonly Dictionary<string, object> _namedElements = new();
    private readonly Stack<object> _parentStack = new();
    private readonly Stack<Type?> _templateTargetTypeStack = new();
    private string? _currentResourceKey;

    /// <summary>
    /// Gets or sets whether we're currently parsing a template.
    /// </summary>
    public bool IsParsingTemplate { get; set; }

    /// <summary>
    /// Pushes a template target type onto the stack (for resolving properties in template triggers).
    /// </summary>
    public void PushTemplateTargetType(Type? targetType) => _templateTargetTypeStack.Push(targetType);

    /// <summary>
    /// Pops a template target type from the stack.
    /// </summary>
    public void PopTemplateTargetType() { if (_templateTargetTypeStack.Count > 0) _templateTargetTypeStack.Pop(); }

    /// <summary>
    /// Gets the current template target type.
    /// </summary>
    public Type? GetTemplateTargetType() => _templateTargetTypeStack.Count > 0 ? _templateTargetTypeStack.Peek() : null;

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
    /// Tries to find a resource by key in the ambient resource dictionaries (parent stack).
    /// </summary>
    public bool TryGetResource(object key, out object? value)
    {
        // Search through the parent stack for ResourceDictionaries
        foreach (var parent in _parentStack)
        {
            if (parent is ResourceDictionary rd && rd.TryGetValue(key, out value))
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
    /// Resolves a DependencyProperty by name from a target type.
    /// </summary>
    public static DependencyProperty? ResolveDependencyProperty(string propertyName, Type? targetType)
    {
        if (string.IsNullOrEmpty(propertyName) || targetType == null)
            return null;

        // Search in target type and its base types
        var currentType = targetType;
        while (currentType != null)
        {
            var dpField = currentType.GetField($"{propertyName}Property",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (dpField != null)
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

    public Type? ResolveType(string namespaceUri, string typeName)
    {
        var cacheKey = $"{namespaceUri}:{typeName}";
        if (_typeCache.TryGetValue(cacheKey, out var cachedType))
        {
            return cachedType;
        }

        // Try CLR namespace from the URI
        if (namespaceUri.StartsWith("clr-namespace:"))
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

            if (remainder.StartsWith("assembly="))
            {
                assemblyName = remainder.Substring("assembly=".Length);
            }
        }

        return ResolveTypeInNamespace(ns, typeName, assemblyName);
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    private Type? ResolveTypeInNamespace(string clrNamespace, string typeName, string? assemblyName = null)
    {
        // AOT-friendly: Use static type registry instead of Assembly.GetType
        return XamlTypeRegistry.GetType(typeName);
    }
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
        Register<DataTrigger>(types);
        Register<EventTrigger>(types);
        Register<ResourceDictionary>(types);
        Register<Binding>(types);
        Register<BindingBase>(types);
        Register<Thickness>(types);
        Register<CornerRadius>(types);
        Register<GridLength>(types);
    }

    private static void RegisterControlTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Controls namespace
        Register<Window>(types);
        Register<Page>(types);
        Register<Frame>(types);
        Register<Control>(types);
        Register<ContentControl>(types);
        Register<ContentPresenter>(types);
        Register<ItemsControl>(types);
        Register<ButtonBase>(types);
        Register<Button>(types);
        Register<ToggleButton>(types);
        Register<TextBlock>(types);
        Register<TextBox>(types);
        Register<PasswordBox>(types);
        Register<CheckBox>(types);
        Register<RadioButton>(types);
        Register<ComboBox>(types);
        Register<Selector>(types);
        Register<ListBox>(types);
        Register<ListBoxItem>(types);
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
        Register<ToolTip>(types);
        Register<Popup>(types);
        Register<TreeView>(types);
        Register<TreeViewItem>(types);
        Register<NavigationView>(types);
        Register<NavigationViewItem>(types);
        Register<TitleBar>(types);
        Register<TitleBarButton>(types);
        Register<RowDefinition>(types);
        Register<ColumnDefinition>(types);
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

    private static void Register<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields)] T>(Dictionary<string, Type> types)
    {
        types[typeof(T).Name] = typeof(T);
    }

    /// <summary>
    /// Gets a type by its simple name.
    /// </summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
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
        DynamicallyAccessedMemberTypes.PublicFields)] T>()
    {
        _types[typeof(T).Name] = typeof(T);
    }

    /// <summary>
    /// Registers a custom type with a specific name.
    /// </summary>
    public static void RegisterType<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields)] T>(string name)
    {
        _types[name] = typeof(T);
    }
}

/// <summary>
/// Exception thrown during XAML parsing.
/// </summary>
public class XamlParseException : Exception
{
    public XamlParseException(string message) : base(message) { }
    public XamlParseException(string message, Exception innerException) : base(message, innerException) { }
}
