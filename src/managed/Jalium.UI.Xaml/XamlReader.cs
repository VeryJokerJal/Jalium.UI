using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Xml;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Shapes;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Documents;
using Jalium.UI.Interactivity;
using Jalium.UI.Media;

namespace Jalium.UI.Markup;

/// <summary>
/// Provides methods for parsing XAML and creating object trees.
/// </summary>
[RequiresUnreferencedCode("XAML loading uses XamlTypeRegistry types whose ctors / overrides may use reflection on user-supplied targets, and may invoke Razor reflection.")]
public partial class XamlReader
{
    private static readonly Jalium.UI.Xaml.XamlSchemaContext s_wpfSchemaContext = new();
    private CancellationTokenSource? _asyncCancellation;

    public XamlReader()
    {
    }

    public event System.ComponentModel.AsyncCompletedEventHandler? LoadCompleted;

    private sealed class RazorIfBlockEntry
    {
        public string EffectiveCondition { get; set; } = "";
        public List<string> ChainBranchConditions { get; } = new();
    }

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

        using var xmlReader = JalxamlParser.CreateReader(xaml);
        return LoadInternal(xmlReader, null, null, null);
    }

    public static object Parse(string xamlText, bool useRestrictiveXamlReader)
        => Parse(xamlText);

    public static object Parse(string xamlText, ParserContext parserContext)
        => Parse(xamlText, parserContext, useRestrictiveXamlReader: false);

    public static object Parse(string xamlText, ParserContext parserContext, bool useRestrictiveXamlReader)
    {
        ArgumentNullException.ThrowIfNull(xamlText);
        ArgumentNullException.ThrowIfNull(parserContext);
        using var xmlReader = JalxamlParser.CreateReader(xamlText);
        return LoadInternal(xmlReader, null, parserContext.BaseUri, null);
    }

    /// <summary>
    /// Hot-reload variant of <see cref="Parse(string)"/>: builds a standalone object tree (no existing
    /// root) but wires inline event handlers to <paramref name="codeBehindForEvents"/> — the live
    /// instance being patched — so a grafted new element's Click/KeyDown/etc. handlers work without a
    /// full restart. Used by <c>HotReloadRuntime.ApplyPatch</c>.
    /// </summary>
    internal static object ParseForHotReload(string xaml, object? codeBehindForEvents)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        using var xmlReader = JalxamlParser.CreateReader(xaml);
        return LoadInternal(xmlReader, null, null, null, codeBehindForEvents: codeBehindForEvents);
    }

    /// <summary>
    /// Reads XAML from a stream and creates an object tree.
    /// </summary>
    /// <param name="stream">The stream containing XAML.</param>
    /// <returns>The root of the created object tree.</returns>
    public static object Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var xmlReader = JalxamlParser.CreateReader(stream);
        return LoadInternal(xmlReader, null, null, null);
    }

    public static object Load(Stream stream, bool useRestrictiveXamlReader) => Load(stream);

    public static object Load(Stream stream, ParserContext parserContext)
        => Load(stream, parserContext, useRestrictiveXamlReader: false);

    public static object Load(Stream stream, ParserContext parserContext, bool useRestrictiveXamlReader)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(parserContext);
        using var xmlReader = JalxamlParser.CreateReader(stream);
        return LoadInternal(xmlReader, null, parserContext.BaseUri, null);
    }

    public static object Load(XmlReader reader)
        => Load(reader, useRestrictiveXamlReader: false);

    public static object Load(XmlReader reader, bool useRestrictiveXamlReader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return LoadInternal(reader, null, TryGetBaseUri(reader), null);
    }

    public static object Load(Jalium.UI.Xaml.XamlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader is Jalium.UI.Xaml.IXmlBackedXamlReader xmlBacked)
        {
            return Load(xmlBacked.XmlReader);
        }

        throw new NotSupportedException("This XAML reader does not expose an XML source that Jalium can materialize.");
    }

    public static Jalium.UI.Xaml.XamlSchemaContext GetWpfSchemaContext() => s_wpfSchemaContext;

    public object LoadAsync(Stream stream)
        => LoadAsync(stream, new ParserContext(), useRestrictiveXamlReader: false);

    public object LoadAsync(Stream stream, bool useRestrictiveXamlReader)
        => LoadAsync(stream, new ParserContext(), useRestrictiveXamlReader);

    public object LoadAsync(Stream stream, ParserContext parserContext)
        => LoadAsync(stream, parserContext, useRestrictiveXamlReader: false);

    public object LoadAsync(Stream stream, ParserContext parserContext, bool useRestrictiveXamlReader)
        => StartAsyncLoad(() => Load(stream, parserContext, useRestrictiveXamlReader));

    public object LoadAsync(XmlReader reader)
        => LoadAsync(reader, useRestrictiveXamlReader: false);

    public object LoadAsync(XmlReader reader, bool useRestrictiveXamlReader)
        => StartAsyncLoad(() => Load(reader, useRestrictiveXamlReader));

    public void CancelAsync()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref _asyncCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private object StartAsyncLoad(Func<object> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        CancelAsync();
        var cancellation = new CancellationTokenSource();
        _asyncCancellation = cancellation;

        object result;
        try
        {
            result = load();
        }
        catch (Exception exception)
        {
            QueueCompletion(cancellation, exception);
            throw;
        }

        QueueCompletion(cancellation, error: null);
        return result;
    }

    private void QueueCompletion(CancellationTokenSource cancellation, Exception? error)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool cancelled = cancellation.IsCancellationRequested;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _asyncCancellation, null, cancellation), cancellation))
            {
                cancellation.Dispose();
            }

            LoadCompleted?.Invoke(this,
                new System.ComponentModel.AsyncCompletedEventArgs(error, cancelled, userState: null));
        });
    }

    private static Uri? TryGetBaseUri(XmlReader reader)
        => Uri.TryCreate(reader.BaseURI, UriKind.RelativeOrAbsolute, out Uri? uri) ? uri : null;

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
        var baseUri = new Uri($"resource:///{sourceAssembly.GetName().Name}/{resourceName}", UriKind.Absolute);

        using var xmlReader = JalxamlParser.CreateReader(textReader);
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

        using var xmlReader = JalxamlParser.CreateReader(reader);
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

    /// <summary>
    /// Loads a component from a raw JALXAML string into an existing instance.
    /// Used by the SourceGenerator's runtime-fallback path for documents containing
    /// Razor directives (<c>@section</c> / <c>@RenderSection</c> / runtime <c>@if</c>)
    /// that the SG cannot lower to straight-line C#. The runtime <see cref="JalxamlReader"/>
    /// handles those directives end-to-end; this overload avoids the need for an embedded
    /// manifest resource by accepting the inlined source text directly.
    /// </summary>
    /// <param name="component">The component to load XAML into.</param>
    /// <param name="xaml">The raw JALXAML / XAML document text.</param>
    /// <param name="baseUri">Optional base URI used for relative-uri resolution (e.g. <c>resource:///{Assembly}/{Path}</c>).</param>
    /// <param name="sourceAssembly">Optional assembly to attribute the load to. Defaults to <paramref name="component"/>'s declaring assembly.</param>
    public static void LoadComponentFromString(object component, string xaml, Uri? baseUri = null, Assembly? sourceAssembly = null)
    {
        LoadComponentFromStringCore(component, xaml, baseUri, sourceAssembly, namedElementsOut: null);
    }

    /// <summary>
    /// Loads a component from a raw JALXAML string with AOT-safe named-element output.
    /// Companion overload to <see cref="LoadComponentFromString(object, string, Uri?, Assembly?)"/>;
    /// see that method for the rationale and intended caller (the SourceGenerator runtime-fallback path).
    /// </summary>
    public static void LoadComponentFromString(object component, string xaml, Dictionary<string, object> namedElements, Uri? baseUri = null, Assembly? sourceAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(namedElements);
        LoadComponentFromStringCore(component, xaml, baseUri, sourceAssembly, namedElementsOut: namedElements);
    }

    private static void LoadComponentFromStringCore(object component, string xaml, Uri? baseUri, Assembly? sourceAssembly, Dictionary<string, object>? namedElementsOut)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(xaml);

        sourceAssembly ??= component.GetType().Assembly;
        using var xmlReader = JalxamlParser.CreateReader(xaml);
        LoadInternal(xmlReader, component, baseUri, sourceAssembly, namedElementsOut: namedElementsOut);
        HotReloadRuntime.RegisterComponent(component);
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
        {
            var content = new StreamReader(stream).ReadToEnd();
            var baseUri = new Uri($"resource:///{assembly.GetName().Name}/{resourceName}", UriKind.Absolute);

            using var xmlReader = JalxamlParser.CreateReader(content);
            LoadInternal(xmlReader, component, baseUri, assembly, namedElementsOut: namedElements);
        }

        HotReloadRuntime.RegisterComponent(component);
    }

    // Per-assembly cache of manifest resource names for O(1) case-insensitive lookup.
    // Avoids repeated GetManifestResourceNames() calls during theme loading (~30+ dictionaries).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Assembly, HashSet<string>> _manifestNameCache = new();

    private static HashSet<string> GetManifestNameSet(Assembly assembly)
    {
        return _manifestNameCache.GetOrAdd(assembly, static asm =>
            new HashSet<string>(asm.GetManifestResourceNames(), StringComparer.OrdinalIgnoreCase));
    }

    private static Stream? GetResourceStream(string resourceName, Assembly assembly)
    {

        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            return stream;
        }

        // Try with assembly name prefix
        var assemblyName = assembly.GetName().Name;
        stream = assembly.GetManifestResourceStream($"{assemblyName}.{resourceName}");
        if (stream != null)
        {
            return stream;
        }

        // Try replacing path separators
        var normalizedName = resourceName.Replace('/', '.').Replace('\\', '.');
        stream = assembly.GetManifestResourceStream(normalizedName);
        if (stream != null)
        {
            return stream;
        }

        stream = assembly.GetManifestResourceStream($"{assemblyName}.{normalizedName}");
        if (stream != null)
        {
            return stream;
        }

        // Fallback: use cached manifest name set for O(1) case-insensitive lookup
        // instead of linear scanning GetManifestResourceNames() on every call.
        var nameSet = GetManifestNameSet(assembly);

        // Check known candidate names via the hash set
        string[] candidates =
        [
            resourceName,
            normalizedName,
            $"{assemblyName}.{resourceName}",
            $"{assemblyName}.{normalizedName}"
        ];

        foreach (var candidate in candidates)
        {
            if (nameSet.TryGetValue(candidate, out var actual))
            {
                stream = assembly.GetManifestResourceStream(actual);
                if (stream != null)
                {
                    return stream;
                }
            }
        }

        // Fallback 2: suffix match (handles namespace/path drift, e.g. Views/Foo.jalxaml vs Foo.jalxaml).
        var fileName = GetResourceFileName(resourceName);
        string[] suffixes =
        [
            $".{normalizedName}",
            $".{resourceName}",
            $".{fileName}"
        ];

        foreach (var name in nameSet)
        {
            foreach (var suffix in suffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    stream = assembly.GetManifestResourceStream(name);
                    if (stream != null)
                    {
                        return stream;
                    }
                    break;
                }
            }
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
        ResourceDictionary? parentResourceDictionary = null, Dictionary<string, object>? namedElementsOut = null,
        object? codeBehindForEvents = null)
    {

        var context = new XamlParserContext
        {
            BaseUri = baseUri,
            SourceAssembly = sourceAssembly,
            ParentResourceDictionary = parentResourceDictionary,
            // Hot reload parses a STANDALONE source tree (existingInstance == null) yet still needs its
            // inline event handlers (Click=…) to bind to the LIVE instance's code-behind so grafted new
            // elements stay interactive. codeBehindForEvents carries that target and affects ONLY event
            // wiring — the root is still built fresh because existingInstance remains null.
            CodeBehindInstance = codeBehindForEvents ?? existingInstance
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

    [RequiresUnreferencedCode("Wires named XAML elements onto fields/properties of the component runtime type via reflection. Code-behind types reachable from XAML are preserved via XamlTypeRegistry/DAM annotations.")]
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

        VisualDiagnostics.SetXamlSourceInfo(
            instance,
            new XamlSourceInfo(context.BaseUri, lineNumber, linePosition));

        // Push to parent stack for context tracking
        context.PushParent(instance);

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
        // Special handling for ItemsPanelTemplate - capture inner XML for deferred parsing
        else if (instance is Jalium.UI.Controls.ItemsPanelTemplate itemsPanelTemplate && !reader.IsEmptyElement)
        {
            ParseItemsPanelTemplateContent(reader, itemsPanelTemplate, context);
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
        // Post-process Trigger to convert Value based on Property type
        else if (instance is Trigger trigger)
        {
            PostProcessTrigger(trigger, context, lineNumber, linePosition);
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
        context.PopParent();

        return instance;
    }

    private static object ParseAttributes(XmlReader reader, object instance, XamlParserContext context)
    {
        var currentInstance = instance;
        // Capture before MoveToAttribute repositions the reader to attribute scope.
        var elementNamespaceUri = reader.NamespaceURI;

        for (int i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);

            var attrName = reader.LocalName;
            var attrValue = reader.Value;
            var prefix = reader.Prefix;
            var attrNamespaceUri = reader.NamespaceURI;

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
                SetAttachedProperty(currentInstance, attrName, attrValue, context, attrNamespaceUri, elementNamespaceUri);
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

    [RequiresUnreferencedCode("Sets a Name property on an instance via reflection when the instance is not a FrameworkElement.")]
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

    [RequiresUnreferencedCode("Resolves attached property setters via reflection on the owner type.")]
    private static void SetAttachedProperty(object instance, string propertyPath, string value, XamlParserContext context, string attributeNamespaceUri, string elementNamespaceUri)
    {
        var parts = propertyPath.Split('.');
        if (parts.Length != 2)
        {
            throw new XamlParseException($"Invalid attached property syntax: {propertyPath}");
        }

        var ownerTypeName = parts[0];
        var propertyName = parts[1];

        // Prefer the attribute's own namespace, set when the author used an explicit prefix
        // (e.g. `b:AnimatedBg.StartWindow` where `b` maps to a clr-namespace URI).
        Type? ownerType = null;
        if (!string.IsNullOrEmpty(attributeNamespaceUri))
        {
            ownerType = context.ResolveType(attributeNamespaceUri, ownerTypeName);
        }

        // Unprefixed attached properties (e.g. `Grid.Row`) inherit XML's no-namespace rule,
        // so look them up under the host element's xmlns instead.
        if (ownerType == null && !string.IsNullOrEmpty(elementNamespaceUri))
        {
            ownerType = context.ResolveType(elementNamespaceUri, ownerTypeName);
        }

        // Fall back to the AOT-safe simple-name registry as a last resort.
        if (ownerType == null)
        {
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

        // Try DependencyProperty directly via the AOT-safe registry.
        var dp = DependencyProperty.FromName(ownerType, propertyName);
        if (dp != null && instance is DependencyObject depObj)
        {
            var convertedValue = TypeConverterRegistry.ConvertValue(value, dp.PropertyType);
            depObj.SetValue(dp, convertedValue);
            return;
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
        var ifDirectiveStack = new Stack<RazorIfBlockEntry>();

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

    private static string BuildCombinedIfConditionExpression(IEnumerable<RazorIfBlockEntry> entries)
    {
        var parts = entries
            .Reverse()
            .Select(static entry => $"({entry.EffectiveCondition})")
            .ToArray();

        return parts.Length == 1 ? parts[0] : string.Join(" && ", parts);
    }

    [RequiresUnreferencedCode("Reads ContentPropertyAttribute and the matching property via reflection on the runtime type.")]
    private static bool SupportsRazorIfDirectives(object instance)
    {
        if (instance is Panel or ItemsControl or Border or Window)
            return true;

        string? contentPropertyName = GetContentPropertyName(instance.GetType());
        if (contentPropertyName == null)
            return false;

        var property = instance.GetType().GetProperty(contentPropertyName);
        if (property == null)
            return false;

        if (typeof(System.Collections.IList).IsAssignableFrom(property.PropertyType))
            return true;

        return typeof(UIElement).IsAssignableFrom(property.PropertyType);
    }

    private static bool TryConsumeRazorIfDirectiveText(string rawText, Stack<RazorIfBlockEntry> ifDirectiveStack)
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

                var popped = ifDirectiveStack.Pop();
                i++;

                // Check for else / else if after the closing brace
                var afterBrace = i;
                while (afterBrace < span.Length && char.IsWhiteSpace(span[afterBrace]))
                    afterBrace++;

                if (TryMatchElse(span, afterBrace, out var elseConsumed, out var elseIfExpression))
                {
                    // Build the negation of all prior branch conditions in this chain
                    var chainConditions = popped.ChainBranchConditions;

                    if (elseIfExpression != null)
                    {
                        // else if(expr): effective = !(c1) && !(c2) && ... && (expr)
                        var parts = new List<string>(chainConditions.Count + 1);
                        foreach (var cond in chainConditions)
                            parts.Add($"!({cond})");
                        parts.Add($"({elseIfExpression})");
                        var effective = string.Join(" && ", parts);

                        var newEntry = new RazorIfBlockEntry { EffectiveCondition = effective };
                        newEntry.ChainBranchConditions.AddRange(chainConditions);
                        newEntry.ChainBranchConditions.Add(elseIfExpression);
                        ifDirectiveStack.Push(newEntry);
                    }
                    else
                    {
                        // else: effective = !(c1) && !(c2) && ...
                        var parts = new List<string>(chainConditions.Count);
                        foreach (var cond in chainConditions)
                            parts.Add($"!({cond})");
                        var effective = parts.Count == 1 ? parts[0] : string.Join(" && ", parts);

                        var newEntry = new RazorIfBlockEntry { EffectiveCondition = effective };
                        newEntry.ChainBranchConditions.AddRange(chainConditions);
                        ifDirectiveStack.Push(newEntry);
                    }

                    i = afterBrace + elseConsumed;
                    continue;
                }

                // No else follows; the chain is done.
                continue;
            }

            if (TryParseRazorIfStartDirectiveAt(span[i..], out var consumedLength, out var expression))
            {
                var entry = new RazorIfBlockEntry { EffectiveCondition = expression };
                entry.ChainBranchConditions.Add(expression);
                ifDirectiveStack.Push(entry);
                i += consumedLength;
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Tries to match "else if(expr) {" or "else {" starting at the given position.
    /// Returns true if matched, with consumedLength set to characters consumed from <paramref name="startIndex"/>,
    /// and elseIfExpression set to the expression (or null for plain else).
    /// </summary>
    private static bool TryMatchElse(ReadOnlySpan<char> span, int startIndex, out int consumedLength, out string? elseIfExpression)
    {
        consumedLength = 0;
        elseIfExpression = null;

        var remaining = span[startIndex..];
        if (remaining.Length < 4)
            return false;

        if (remaining[0] != 'e' || remaining[1] != 'l' || remaining[2] != 's' || remaining[3] != 'e')
            return false;

        var i = 4;
        // Need whitespace or '(' or '{' after "else"
        if (i >= remaining.Length)
            return false;

        // Skip whitespace after "else"
        while (i < remaining.Length && char.IsWhiteSpace(remaining[i]))
            i++;

        if (i >= remaining.Length)
            return false;

        if (remaining[i] == 'i' && i + 1 < remaining.Length && remaining[i + 1] == 'f')
        {
            // else if(expr) {
            i += 2;
            while (i < remaining.Length && char.IsWhiteSpace(remaining[i]))
                i++;

            if (i >= remaining.Length || remaining[i] != '(')
                return false;

            i++; // skip '('
            var exprStart = i;
            var depth = 1;
            var inString = false;
            var escaped = false;
            var quote = '\0';

            for (; i < remaining.Length; i++)
            {
                var c = remaining[i];
                if (escaped) { escaped = false; continue; }
                if (inString)
                {
                    if (c == '\\') escaped = true;
                    else if (c == quote) { inString = false; quote = '\0'; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(') { depth++; continue; }
                if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        elseIfExpression = remaining[exprStart..i].ToString().Trim();
                        i++;
                        break;
                    }
                }
            }

            if (depth != 0 || string.IsNullOrWhiteSpace(elseIfExpression))
                return false;

            while (i < remaining.Length && char.IsWhiteSpace(remaining[i]))
                i++;

            if (i >= remaining.Length || remaining[i] != '{')
                return false;

            consumedLength = i + 1;
            return true;
        }

        if (remaining[i] == '{')
        {
            // plain else {
            consumedLength = i + 1;
            elseIfExpression = null;
            return true;
        }

        return false;
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

    [RequiresUnreferencedCode("Resolves property element setters via reflection on the runtime instance type.")]
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
        var namespaceUri = reader.NamespaceURI;

        var type = instance.GetType();

        // Distinguish between property element syntax (owner type == instance type or base)
        // and attached property element syntax (owner type is a different type exposing the attached DP).
        // Example: <Border.Child>…</Border.Child> uses Border's own Child property,
        // whereas <ContextMenuService.ContextMenu>…</ContextMenuService.ContextMenu> attaches a value to the Border.
        var ownerMatchesInstance = OwnerTypeMatchesInstance(type, ownerTypeName);
        if (!ownerMatchesInstance)
        {
            var attachedOwnerType = context.ResolveType(namespaceUri, ownerTypeName) ?? FindTypeByName(ownerTypeName);
            if (attachedOwnerType != null && attachedOwnerType != type)
            {
                ParseAttachedPropertyElement(reader, instance, attachedOwnerType, propertyName, depth, isEmpty, context);
                return;
            }
        }

        // Regular property element syntax
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
                    // WPF 语义:<Foo.Resources><ResourceDictionary>…</ResourceDictionary></Foo.Resources>
                    // 是把子 dict 的 MergedDictionaries 和 items 合并到 existing Resources,不是整块替换。
                    //
                    // 最关键的实际场景:Application ctor 里 ThemeManager.Initialize 把 Generic theme /
                    // accent / typography 三个 dict 挂到 app.Resources.MergedDictionaries;如果 user
                    // App.jalxaml 紧接着声明 <Application.Resources><ResourceDictionary>…</ResourceDictionary>
                    // </Application.Resources>,整块替换会把 Generic theme 丢掉,所有控件 Template=null,
                    // 窗口整片黑。Window.Resources / UserControl.Resources 同理。
                    //
                    // 只有 existing 为 null (property 从未 get 过、没有懒初始化) 或者 user 显式用了
                    // 同一 dict 实例时才直接 SetValue。
                    var existing = property.GetValue(instance) as ResourceDictionary;
                    if (existing == null || ReferenceEquals(existing, dictionaryValue))
                    {
                        property.SetValue(instance, dictionaryValue);
                    }
                    else
                    {
                        foreach (var merged in dictionaryValue.MergedDictionaries)
                        {
                            existing.MergedDictionaries.Add(merged);
                        }
                        foreach (System.Collections.DictionaryEntry entry in dictionaryValue)
                        {
                            existing[entry.Key] = entry.Value;
                        }
                    }
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
                        AddToCollection(propertyValue, childValue!);
                    }
                }
                else
                {
                    // Set property value
                    SetPropertyValueWithResourceFallback(instance, property, childValue, context);
                }

                context.ClearCurrentResourceKey();
            }
        }
    }

    private static bool OwnerTypeMatchesInstance(Type instanceType, string ownerTypeName)
    {
        for (var t = instanceType; t != null; t = t.BaseType)
        {
            if (string.Equals(t.Name, ownerTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void ParseAttachedPropertyElement(
        XmlReader reader,
        object instance,
        Type ownerType,
        string propertyName,
        int depth,
        bool isEmpty,
        XamlParserContext context)
    {
        if (isEmpty)
        {
            return;
        }

        // Resolve once up-front. Prefer the static Set{PropertyName} helper (canonical
        // attached-property pattern); fall back to the {PropertyName}Property DP so attached
        // properties declared without a setter helper still work.
        var setMethod = ownerType.GetMethod(
            $"Set{propertyName}",
            BindingFlags.Public | BindingFlags.Static);

        DependencyProperty? attachedDp = null;
        Type? targetType = null;
        if (setMethod != null)
        {
            var methodParameters = setMethod.GetParameters();
            if (methodParameters.Length == 2)
            {
                targetType = methodParameters[1].ParameterType;
            }
            else
            {
                setMethod = null;
            }
        }

        if (setMethod == null)
        {
            var dp = DependencyProperty.FromName(ownerType, propertyName);
            if (dp != null)
            {
                attachedDp = dp;
                targetType = dp.PropertyType;
            }
        }

        if (setMethod == null && attachedDp == null)
        {
            throw new XamlParseException(
                $"Cannot find attached property setter for: {ownerType.Name}.{propertyName}");
        }

        object? assignedValue = null;
        var hasValue = false;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var explicitKey = TryGetXKey(reader);
            context.ClearCurrentResourceKey();
            var childValue = ParseElement(reader, context);
            _ = context.GetCurrentResourceKey() ?? explicitKey;
            context.ClearCurrentResourceKey();

            childValue = ResolveMarkupExtensionValueIfNeeded(childValue, instance, targetProperty: null, context);

            if (childValue == null)
            {
                continue;
            }

            if (targetType != null &&
                !targetType.IsInstanceOfType(childValue) &&
                childValue is string stringValue)
            {
                childValue = TypeConverterRegistry.ConvertValue(stringValue, targetType);
            }

            assignedValue = childValue;
            hasValue = true;
        }

        if (!hasValue)
        {
            return;
        }

        if (setMethod != null)
        {
            setMethod.Invoke(null, [instance, assignedValue]);
            return;
        }

        if (attachedDp != null && instance is DependencyObject depObj)
        {
            depObj.SetValue(attachedDp, assignedValue);
        }
    }

    private static string? TryGetXKey(XmlReader reader)
    {
        const string xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        return reader.GetAttribute("Key", xamlNamespace);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "This is the runtime XAML markup-extension resolution tail; invoking it is the documented consumer responsibility under AOT. The base MarkupExtension.ProvideValue carries RequiresDynamicCode only because 'some extensions (e.g. x:Array) construct arrays of a runtime-supplied element Type' — that single extension is the dynamic-code case. The built-in extensions reachable here (Binding/StaticResource/DynamicResource/TemplateBinding/x:Static etc.) construct no runtime-typed arrays, so no dynamic code is emitted at this call site.")]
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

    [RequiresUnreferencedCode("Falls back to reflection to invoke Add on non-IList collections.")]
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
    /// 把当前 reader 上活动的 xmlns 声明合并到 <paramref name="fragment"/> 的 root
    /// open-tag。模板（DataTemplate / ControlTemplate / ItemsPanelTemplate）通过
    /// <c>ReadOuterXml</c> 把内容存为字符串后延迟解析，但 ReadOuterXml 只切原文，
    /// 不带外层声明。延迟解析时新 reader 找不到 prefix 映射，导致
    /// <c>controls:WelcomeTemplateCard</c> 之类的 prefix:Type 引用全部解析失败 —
    /// 比如 <c>RelativeSource={RelativeSource AncestorType=controls:WelcomeTemplateCard}</c>
    /// 拿到 null AncestorType，FindAncestor 立刻返回 null，binding 永远 Unattached。
    /// 这里把外层 xmlns 注入到 fragment 自身的 root 标签上，让 fragment 自包含。
    ///
    /// fragment 自己已经声明的 prefix 优先（更近的 scope），不被外层声明覆盖。
    /// </summary>
    private static string InjectAmbientNamespaces(string fragment, XmlReader reader)
    {
        if (string.IsNullOrEmpty(fragment))
            return fragment;
        if (reader is not Markup.JalxamlReader jalxaml)
            return fragment;

        var snapshot = jalxaml.SnapshotNamespacesInScope();
        if (snapshot.Count == 0)
            return fragment;

        // 找 root element 的 open-tag。跳过前导空白 + comments / processing instructions。
        int cursor = 0;
        while (cursor < fragment.Length)
        {
            // 跳过空白
            while (cursor < fragment.Length && char.IsWhiteSpace(fragment[cursor])) cursor++;
            if (cursor >= fragment.Length || fragment[cursor] != '<') return fragment;

            // 跳过 <!-- ... --> / <? ... ?>
            if (cursor + 3 < fragment.Length && fragment[cursor + 1] == '!' && fragment[cursor + 2] == '-' && fragment[cursor + 3] == '-')
            {
                var endComment = fragment.IndexOf("-->", cursor + 4, StringComparison.Ordinal);
                if (endComment < 0) return fragment;
                cursor = endComment + 3;
                continue;
            }
            if (cursor + 1 < fragment.Length && fragment[cursor + 1] == '?')
            {
                var endPi = fragment.IndexOf("?>", cursor + 2, StringComparison.Ordinal);
                if (endPi < 0) return fragment;
                cursor = endPi + 2;
                continue;
            }
            break;
        }

        if (cursor >= fragment.Length || fragment[cursor] != '<')
            return fragment;

        int openTagStart = cursor;
        // element name 起点：'<' 后的第一个非空白字符，跳过 '<'
        int nameStart = openTagStart + 1;
        // element name 结束：第一个空白、'/'、或 '>'
        int nameEnd = nameStart;
        while (nameEnd < fragment.Length &&
               !char.IsWhiteSpace(fragment[nameEnd]) &&
               fragment[nameEnd] != '/' &&
               fragment[nameEnd] != '>')
        {
            nameEnd++;
        }
        if (nameEnd >= fragment.Length) return fragment;

        // open-tag 结束：往后找第一个 '>'，但要跳过引号内的内容（属性值可能包含 '>')
        int openTagEnd = nameEnd;
        char inQuote = '\0';
        while (openTagEnd < fragment.Length)
        {
            var ch = fragment[openTagEnd];
            if (inQuote != '\0')
            {
                if (ch == inQuote) inQuote = '\0';
            }
            else
            {
                if (ch == '"' || ch == '\'') inQuote = ch;
                else if (ch == '>') break;
            }
            openTagEnd++;
        }
        if (openTagEnd >= fragment.Length) return fragment;

        // 扫描 open-tag 内已有的 xmlns 声明，避免在同一元素上重复声明（XML 解析器会拒绝）。
        var existing = new HashSet<string>(StringComparer.Ordinal);
        int scan = nameEnd;
        char scanQuote = '\0';
        while (scan < openTagEnd)
        {
            var ch = fragment[scan];
            if (scanQuote != '\0')
            {
                if (ch == scanQuote) scanQuote = '\0';
                scan++;
                continue;
            }
            if (ch == '"' || ch == '\'')
            {
                scanQuote = ch;
                scan++;
                continue;
            }
            // 检查 "xmlns" 或 "xmlns:" 出现位置
            if ((ch == 'x' || ch == 'X') && scan + 4 < openTagEnd &&
                string.Compare(fragment, scan, "xmlns", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                int afterXmlns = scan + 5;
                if (afterXmlns < openTagEnd && fragment[afterXmlns] == ':')
                {
                    int prefixStart = afterXmlns + 1;
                    int prefixEnd = prefixStart;
                    while (prefixEnd < openTagEnd &&
                           !char.IsWhiteSpace(fragment[prefixEnd]) &&
                           fragment[prefixEnd] != '=')
                    {
                        prefixEnd++;
                    }
                    existing.Add(fragment.Substring(prefixStart, prefixEnd - prefixStart));
                    scan = prefixEnd;
                }
                else
                {
                    existing.Add(string.Empty);
                    scan = afterXmlns;
                }
                continue;
            }
            scan++;
        }

        // 构造需要注入的 xmlns 声明。
        var sb = new System.Text.StringBuilder();
        foreach (var kvp in snapshot)
        {
            if (existing.Contains(kvp.Key)) continue;
            sb.Append(' ');
            if (string.IsNullOrEmpty(kvp.Key))
            {
                sb.Append("xmlns=\"").Append(EscapeXmlAttribute(kvp.Value)).Append('"');
            }
            else
            {
                sb.Append("xmlns:").Append(kvp.Key).Append("=\"").Append(EscapeXmlAttribute(kvp.Value)).Append('"');
            }
        }

        if (sb.Length == 0) return fragment;

        // 在 element name 之后插入注入字符串。
        return string.Concat(
            fragment.AsSpan(0, nameEnd),
            sb.ToString(),
            fragment.AsSpan(nameEnd));
    }

    private static string EscapeXmlAttribute(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(['&', '"', '<', '>']) < 0) return value;
        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
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
                var wasEmpty = reader.IsEmptyElement;
                visualTreeXaml.Append(InjectAmbientNamespaces(reader.ReadOuterXml(), reader));
                hasVisualTree = true;

                // For non-empty elements, ReadOuterXml advances the reader past the end tag
                // to the next node, so we must process that node without calling Read() again.
                // For self-closing (empty) elements, System.Xml leaves the reader positioned on
                // the same element, so the next loop iteration must call Read() to advance —
                // otherwise we'd reprocess the same element as a second visual tree root.
                skipRead = !wasEmpty;
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
                if (trigger is TriggerBase t)
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
                // Capture the visual tree as XML.
                // ReadOuterXml 对非空元素会推进 reader 到该元素之后的下一节点，
                // 对 self-closing(空) 元素则保持在该元素上 — skipRead 必须按
                // wasEmpty 区分,否则空元素根（如 <StackPanel/>）会被当作"第二个根"
                // 二次进入此分支,继而被错误地抛 "only one visual tree root" 异常。
                var wasEmpty = reader.IsEmptyElement;
                visualTreeXaml.Append(InjectAmbientNamespaces(reader.ReadOuterXml(), reader));
                hasVisualTree = true;
                skipRead = !wasEmpty;
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
            // 记下解析时刻栈中的 ResourceDictionary 链，让 LoadContent 时可解析祖先 {StaticResource}。
            template.AmbientResourceDictionaries = context.SnapshotAmbientResourceDictionaries();

            // Register the XAML parser callback if not already set
            DataTemplate.XamlParser ??= ParseTemplateXaml;
        }
    }

    private static void ParseItemsPanelTemplateContent(
        XmlReader reader,
        Jalium.UI.Controls.ItemsPanelTemplate template,
        XamlParserContext context)
    {
        int depth = reader.Depth;
        var visualTreeXaml = new System.Text.StringBuilder();
        bool hasVisualTree = false;
        bool skipRead = false;

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
                // 与 DataTemplate / ControlTemplate 同样的空元素处理：ReadOuterXml
                // 对 self-closing 元素不推进 reader,如果一律 skipRead=true 会把同一个
                // 根元素当成"第二个 visual tree root" 抛异常,典型现场是
                // <ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate>。
                var wasEmpty = reader.IsEmptyElement;
                visualTreeXaml.Append(InjectAmbientNamespaces(reader.ReadOuterXml(), reader));
                hasVisualTree = true;
                skipRead = !wasEmpty;
            }
            else
            {
                throw new XamlParseException("ItemsPanelTemplate can only have one visual tree root element.");
            }
        }

        // Store the captured XAML for deferred parsing
        if (hasVisualTree)
        {
            template.VisualTreeXaml = visualTreeXaml.ToString();
            template.SourceAssembly = context.SourceAssembly;
            template.AmbientResourceDictionaries = context.SnapshotAmbientResourceDictionaries();

            // Register the XAML parser callback if not already set
            Jalium.UI.Controls.ItemsPanelTemplate.XamlParser ??= ParseTemplateXaml;
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

        using var xmlReader = JalxamlParser.CreateReader(xaml);
        var context = new XamlParserContext
        {
            SourceAssembly = sourceAssembly
        };

        // 把模板被声明时的祖先 ResourceDictionary 链注入新解析上下文的 ambient 栈底，
        // 让模板内 {StaticResource X} 能找到外层 UserControl.Resources / Window.Resources 等
        // 声明的资源（如 BooleanToVisibilityConverter）。
        var inheritedAmbient = TemplateAmbientResourceContext.Current;
        if (inheritedAmbient != null && inheritedAmbient.Count > 0)
        {
            // 反向 push 让最外层资源在栈底，模板自身资源（首次 push 进栈的）保持在上方优先匹配。
            for (int i = inheritedAmbient.Count - 1; i >= 0; i--)
            {
                context.PushParent(inheritedAmbient[i]);
            }
        }

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

                LogMergedDictionaryFailure(sourceUri,
                    $"SourceLoader returned null for '{sourceUri}'. SourceAssembly={context.SourceAssembly?.GetName().Name ?? "<null>"}.");
            }
            else
            {
                // No SourceLoader registered - try to load directly
                return LoadResourceDictionaryFromUri(resourceDict, sourceUri, context, parentDict);
            }
        }
        catch (XamlParseException ex)
        {
            // 静默吞匹配 WPF 行为，但开发期需要诊断信息；写入 trace + 文件日志。
            LogMergedDictionaryFailure(sourceUri, ex.ToString());
        }
        catch (Exception ex)
        {
            // 兜住意料之外的异常类型，避免一处出错让整个字典链崩塌而无任何提示。
            LogMergedDictionaryFailure(sourceUri, ex.ToString());
        }

        return resourceDict;
    }

    /// <summary>
    /// 静默吞 MergedDictionary 加载异常匹配 WPF 行为（单个 Source 失败不应让整个字典爆炸），
    /// 但开发期需要看到具体原因，否则 "卡片渲染空白却没有任何提示" 这种场景无从下手。
    /// 通过 Trace.WriteLine 暴露给 Debug 输出 / DiagnosticsTraceListener。
    /// </summary>
    private static void LogMergedDictionaryFailure(Uri sourceUri, string detail)
    {
        var message = $"[Jalium.UI] MergedDictionary 加载失败: '{sourceUri}'. {detail}";
        System.Diagnostics.Trace.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
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
        {
            using var xmlReader = JalxamlParser.CreateReader(stream);
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

    [RequiresUnreferencedCode("Sets a CLR property on the runtime type of an instance via reflection. Types reachable from XAML are preserved via XamlTypeRegistry/DAM annotations.")]
    private static object SetProperty(object instance, string propertyName, object? value, XamlParserContext context, XmlReader? reader = null)
    {
        var type = instance.GetType();
        var property = type.GetProperty(propertyName);

        if (property == null)
        {
            // Check if it's an event (e.g., Click="OnClick")
            if (value is string handlerName && TryWireEvent(instance, propertyName, handlerName, context))
            {
                return instance;
            }

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
            // Special handling for DependencyProperty type (for Setter.Property, Trigger.Property, Condition.Property, etc.)
            // Also handle nullable DependencyProperty? type
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            // Type-typed properties, plus DataTemplate.DataType (which is object-typed in
            // WPF so it can also hold an XML tag name), need the live XML namespace map.
            // Resolve both bare "prefix:TypeName" and "{x:Type prefix:TypeName}" forms.
            // When a DataTemplate value does not resolve as a CLR type it deliberately
            // falls through as a string, preserving XML-data template semantics.
            bool isDataTemplateDataType =
                instance is DataTemplate && propertyName == nameof(DataTemplate.DataType);
            string? typeReference = propertyType == typeof(Type) || isDataTemplateDataType
                ? GetXamlTypeReference(stringValue)
                : null;

            if (typeReference != null)
            {
                var resolvedType = ResolveTypeFromXamlString(typeReference, context, reader);
                if (resolvedType != null)
                {
                    property.SetValue(instance, resolvedType);
                    return instance;
                }
                // Resolution failure is not immediately fatal: normal markup-extension and
                // type-converter handling still gets a chance below. For DataTemplate this
                // is also the valid path for an XML element-name string.
            }

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

                if (instance is Trigger trigger)
                {
                    // The enclosing Style's TargetType may not yet be known at the moment this
                    // child element is being parsed (for example when the Style is declared inside
                    // a ResourceDictionary whose parent chain hasn't been fully established). In
                    // that case the trigger/condition would be rejected here even though the XAML
                    // is well-formed. Leave Property unset — the trigger/condition Attach path
                    // performs an early-return when Property is null, so the trigger simply does
                    // not fire rather than aborting the whole dictionary parse.
                    trigger.UnresolvedPropertyName = stringValue;
                    return instance;
                }

                if (instance is Condition condition)
                {
                    condition.UnresolvedPropertyName = stringValue;
                    return instance;
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
            SetPropertyValueWithResourceFallback(instance, property, value, context);
        }
        else
        {
        }

        return instance;
    }

    /// <summary>
    /// 将 XAML 属性值（形如 "TypeName" 或 "prefix:TypeName"）解析成 CLR <see cref="Type"/>。
    /// 这条路径服务于 Style.TargetType 等 Type 属性，以及可保存 CLR Type 的 DataTemplate.DataType —
    /// 它们必须通过当前 XML reader 的命名空间映射把 prefix 解析成 namespace URI，
    /// 然后再走 <see cref="XamlParserContext.ResolveType"/> 的标准查找链
    /// （clr-namespace / XmlnsDefinition / XamlTypeRegistry / 默认 fallback）。
    /// 默认的 <c>TypeTypeConverter</c> 只做 simple-name 查找，遇到 prefix 永远返回 null。
    /// </summary>
    private static Type? ResolveTypeFromXamlString(string typeName, XamlParserContext context, XmlReader? reader)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var trimmed = typeName.Trim();
        var colonIndex = trimmed.IndexOf(':');

        if (colonIndex < 0)
        {
            // 无前缀：优先 AOT-safe simple-name registry，覆盖框架内置控件
            // （它们在 ModuleInitializer 里都注册了简单名）。
            var registryType = XamlTypeRegistry.GetType(trimmed);
            if (registryType != null)
                return registryType;

            // Fallback：用元素的默认命名空间走 ResolveType，避免漏掉
            // xmlns="clr-namespace:..." 这种"匿名前缀"场景。
            if (reader != null)
            {
                var defaultNs = reader.LookupNamespace(string.Empty);
                if (!string.IsNullOrEmpty(defaultNs))
                {
                    return context.ResolveType(defaultNs, trimmed);
                }
            }
            return null;
        }

        var prefix = trimmed.Substring(0, colonIndex);
        var localName = trimmed.Substring(colonIndex + 1);

        if (reader == null)
            return null;

        var namespaceUri = reader.LookupNamespace(prefix);
        if (string.IsNullOrEmpty(namespaceUri))
            return null;

        return context.ResolveType(namespaceUri, localName);
    }

    /// <summary>
    /// Returns a type reference from either a bare XAML type token or an <c>x:Type</c>
    /// markup extension. Other markup extensions return <see langword="null"/> so the
    /// normal markup-extension pipeline can process them.
    /// </summary>
    private static string? GetXamlTypeReference(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return trimmed;
        }

        if (trimmed.StartsWith("{}", StringComparison.Ordinal) || trimmed[^1] != '}')
        {
            return null;
        }

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        const string XTypeName = "x:Type";
        if (!inner.StartsWith(XTypeName, StringComparison.OrdinalIgnoreCase)
            || inner.Length <= XTypeName.Length
            || !char.IsWhiteSpace(inner[XTypeName.Length]))
        {
            return null;
        }

        var typeReference = inner.Substring(XTypeName.Length).Trim();
        return typeReference.Length > 0 ? typeReference : null;
    }

    private static string Truncate(object? value)
    {
        if (value == null) return "<null>";
        var s = value as string ?? value.ToString() ?? "<null>";
        return s.Length <= 80 ? s : s.Substring(0, 77) + "...";
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

    [RequiresUnreferencedCode("Resolves events and code-behind handler methods via reflection on runtime types.")]
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

    [RequiresUnreferencedCode("Reads ContentPropertyAttribute and the matching property via reflection on the runtime type.")]
    private static object SetContentProperty(object instance, string content, XamlParserContext context)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content))
        {
            return instance;
        }

        // TextBlock's canonical XAML content property is Inlines, but this runtime represents
        // plain/mixed text through Text so it can participate in the dependency-property binding
        // pipeline. Inline child elements still flow through AddChild and into Inlines.
        if (instance is TextBlock textBlock)
        {
            var textProperty = typeof(TextBlock).GetProperty(nameof(TextBlock.Text))!;
            if (RazorBindingEngine.TryApplyRazorValue(instance, textProperty, content, context, null))
            {
                return instance;
            }

            textBlock.Text = content;
            return instance;
        }

        // Check for ContentPropertyAttribute
        var type = instance.GetType();
        string? contentPropertyName = GetContentPropertyName(type);

        if (contentPropertyName != null)
        {
            var property = type.GetProperty(contentPropertyName);
            if (property != null)
            {
                if (RazorBindingEngine.TryApplyRazorValue(instance, property, content, context, null))
                {
                    return instance;
                }

                // 只读 ContentProperty（如 Panel.Children、ItemsControl.Items 这种 IList getter-only）
                // 必须走 IList.Add 而不是 SetValue。collection 元素类型与 content 不兼容时（如往
                // IList<UIElement> 加 string）silent ignore — 让 mixed content 中的裸文本（XAML 编辑
                // 过程中的临时字符、Razor 块等）不至于让整个解析抛 ArgumentException。
                if (!property.CanWrite)
                {
                    try
                    {
                        var existing = property.GetValue(instance);
                        if (existing is System.Collections.IList list)
                        {
                            var converted = TypeConverterRegistry.ConvertValue(content, property.PropertyType);
                            var item = converted ?? content;
                            if (IsAssignableElement(list, item))
                            {
                                list.Add(item);
                            }
                        }
                    }
                    catch
                    {
                        // collection 元素类型不兼容 / 解析中间状态 — 静默忽略
                    }
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

    /// 判断 item 是否可以放进 list（用于只读 collection ContentProperty 的 Add 前类型检查）。
    /// 对泛型 List&lt;T&gt; / Collection&lt;T&gt; 等取首个类型参数当元素类型；非泛型 IList 当 object 处理。
    private static bool IsAssignableElement(System.Collections.IList list, object item)
    {
        if (item == null) return true;
        var listType = list.GetType();
        var elementType = typeof(object);
        if (listType.IsGenericType)
        {
            var args = listType.GetGenericArguments();
            if (args.Length > 0) elementType = args[0];
        }
        return elementType.IsInstanceOfType(item);
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

    private static void PostProcessTrigger(Trigger trigger, XamlParserContext context, int lineNumber, int linePosition)
    {
        if (trigger.Property == null)
        {
            // Match WPF: an unresolved property token is retained as Property=null while
            // XAML is read. Trigger validation belongs to Style.Seal (and therefore style
            // application), not to the streaming parse phase.
            return;
        }

        // If Value is a string and Property is set, convert Value to the correct type
        if (trigger.Property != null && trigger.Value is string stringValue)
        {
            var targetType = trigger.Property.PropertyType;

            // Check for markup extension first
            if (MarkupExtensionParser.TryParse(stringValue, trigger, typeof(Trigger).GetProperty("Value")!, context, out var extensionResult))
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
                // Match WPF's Trigger behavior: parsing succeeds with Property=null and
                // MultiTrigger/Condition validation happens when the Style is sealed.
                continue;
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

    [RequiresUnreferencedCode("Resolves ContentPropertyAttribute and Add methods via reflection on parent runtime types.")]
    private static void AddChild(object parent, object child, XamlParserContext context, string? resourceKey = null)
    {
        // ResourceDictionary 走特殊路径（key/style.TargetType 等），不能用 ContentProperty。
        if (parent is ResourceDictionary resourceDict)
        {
            child = ResolveMarkupExtensionValueIfNeeded(child, resourceDict, null, context)!;
            if (!string.IsNullOrEmpty(resourceKey))
            {
                resourceDict[resourceKey] = child;
            }
            else if (TryGetImplicitResourceKey(child, out var implicitKey))
            {
                resourceDict[implicitKey] = child;
            }
            else if (child is Style style && style.TargetType != null)
            {
                resourceDict[style.TargetType] = style;
            }
            // else: skip resources without keys
            return;
        }

        // [ContentProperty] **优先**于 Panel/ItemsControl/ContentControl 这种"按类型兜底"的
        // 路径——这才是 WPF/XAML 的标准行为。原先把 `parent is Panel → Children.Add` 放在
        // ContentProperty 之前会让 Panel 子类（如 SplitDockGroup / DockTabPanel）即使
        // 显式标了 `[Jalium.UI.Markup.ContentProperty("Items")]`，子元素仍被加到 Children 而不是 Items
        // ——实际表现是"子在 visual tree 里、但不在自定义集合里"，所有依赖该集合的逻辑
        // （事件路由 / 命中查找）都会失败。
        //
        // [ContentProperty] 的 Inherited=true，没标的 Panel 子类会继承 Panel 自己的
        // `[Jalium.UI.Markup.ContentProperty("Children")]`，最终也走 list.Add(child) 等价于 Children.Add，
        // 和旧路径行为一致；不会破坏现有 Panel 子类。
        var parentType = parent.GetType();
        string? contentPropertyName = GetContentPropertyName(parentType);
        if (contentPropertyName != null)
        {
            var property = parentType.GetProperty(contentPropertyName);
            if (property != null)
            {
                var propertyValue = property.GetValue(parent);

                // 集合属性（Items / Children 等）：直接 Add
                if (propertyValue is System.Collections.IList list)
                {
                    list.Add(child);
                    return;
                }

                // 单值属性（Content / Child / Document 等）：set
                if (property.CanWrite)
                {
                    child = ResolveMarkupExtensionValueIfNeeded(child, parent, property, context)!;
                    SetPropertyValueWithResourceFallback(parent, property, child, context);
                    return;
                }
            }
        }

        // Fallback：没标 [ContentProperty] 的容器类（一般少见，极少类型连 inherited 的也没有）。
        if (parent is Panel panel && child is UIElement element)
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
    }

    private static bool TryGetImplicitResourceKey(object child, out object resourceKey)
    {
        resourceKey = null!;

        var keyAttribute = child.GetType()
            .GetCustomAttribute<DictionaryKeyPropertyAttribute>(inherit: true);
        string? keyPropertyName = keyAttribute?.Name;
        if (string.IsNullOrEmpty(keyPropertyName))
        {
            return false;
        }

        var keyProperty = child.GetType().GetProperty(
            keyPropertyName,
            BindingFlags.Instance | BindingFlags.Public);
        if (keyProperty?.CanRead != true)
        {
            return false;
        }

        object? key = keyProperty.GetValue(child);
        if (key == null)
        {
            return false;
        }

        resourceKey = key;
        return true;
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

    private static bool TryApplyDynamicResourceReference(object targetObject, PropertyInfo property, object? value, XamlParserContext? context = null)
    {
        if (value is not IDynamicResourceReference dynamicReference)
            return false;

        if (targetObject is FrameworkElement frameworkElement)
        {
            var dependencyProperty = XamlParserContext.ResolveDependencyProperty(property.Name, frameworkElement.GetType());
            if (dependencyProperty == null)
                return false;

            DynamicResourceBindingOperations.SetDynamicResource(frameworkElement, dependencyProperty, dynamicReference.ResourceKey);
            return true;
        }

        // Non-FrameworkElement DependencyObject support (e.g. GradientStop),
        // analogous to WPF's Freezable inheritance context.
        if (targetObject is DependencyObject dependencyObject && context != null)
        {
            var dependencyProperty = XamlParserContext.ResolveDependencyProperty(property.Name, dependencyObject.GetType());
            if (dependencyProperty == null)
                return false;

            var host = context.FindParent<FrameworkElement>();
            if (host != null)
            {
                DynamicResourceBindingOperations.SetDynamicResourceForNonVisual(host, dependencyObject, dependencyProperty, dynamicReference.ResourceKey);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sets a property, honouring DynamicResource bindings when the target supports them.
    /// Falls back to an immediate ambient lookup (equivalent to a StaticResource) when the
    /// target is not a DependencyObject or the property has no matching DependencyProperty,
    /// so the raw <see cref="IDynamicResourceReference"/> never reaches a typed property
    /// setter (which would throw <see cref="ArgumentException"/>).
    /// </summary>
    private static void SetPropertyValueWithResourceFallback(
        object targetObject,
        PropertyInfo property,
        object? value,
        XamlParserContext? context)
    {
        if (TryApplyDynamicResourceReference(targetObject, property, value, context))
            return;

        if (value is IDynamicResourceReference dynRef)
        {
            // Setter.Value is deliberately deferred: resolving it against the dictionary being
            // parsed would freeze the current brush/value and discard DynamicResource semantics.
            // Style.Apply installs the live subscription on each eventual target element.
            if (targetObject is Setter && property.Name == nameof(Setter.Value))
            {
                property.SetValue(targetObject, dynRef);
                return;
            }

            if (context != null && context.TryGetResource(dynRef.ResourceKey, out var resolved) && resolved != null)
            {
                value = resolved;
                // Apply the same string→target conversion the non-extension path would have done.
                if (value is string s &&
                    property.PropertyType != typeof(string) &&
                    property.PropertyType != typeof(object))
                {
                    value = TypeConverterRegistry.ConvertValue(s, property.PropertyType);
                }
            }
            else if (!property.PropertyType.IsInstanceOfType(dynRef))
            {
                // Resource is unresolvable right now (forward reference during dictionary parse)
                // and the typed property setter would reject the raw reference object. Silently
                // skip — the same observable effect the parser had before this helper existed
                // (the exception thrown by SetValue was caught by the outer ResourceDictionary
                // loader), but without tripping the "break when thrown" debugger pause.
                return;
            }
        }

        try
        {
            property.SetValue(targetObject, value);
        }
        catch (ArgumentException)
        {
            // Last-resort fallback: the value doesn't match the property type
            // (typically a DynamicResourceReference whose key isn't available yet).
            // Swallow so one misbehaving binding doesn't abort the whole dictionary load.
        }
    }

    // ============================================================
    // Internal bridge for the compile-time XAML builder (XamlBuilder).
    // The SourceGenerator emits straight-line C# that calls into XamlBuilder
    // for each element / attribute / child. XamlBuilder forwards to these
    // methods so the runtime keeps its existing type-converter, markup-extension,
    // attached-property and collection-add semantics — only the XML lexer,
    // element-name → Type lookup and named-element wiring reflection are
    // eliminated. This is the "uic" path: jalxaml structure is captured
    // statically, value semantics stay shared with the legacy LoadComponent path.
    // ============================================================

    internal static XamlParserContext CreateBuilderContext(object? component, Uri? baseUri, Assembly? sourceAssembly)
    {
        return new XamlParserContext
        {
            BaseUri = baseUri,
            SourceAssembly = sourceAssembly,
            CodeBehindInstance = component,
        };
    }

    [RequiresUnreferencedCode("XamlBuilder.SetProperty forwards to the runtime XamlReader.SetProperty path which uses reflection / type converters / markup extensions.")]
    internal static object BuilderSetProperty(object instance, string propertyName, object? value, XamlParserContext context)
    {
        // Mirror the streaming parser's `instance = SetProperty(instance, ...)` shape.
        // SetProperty's documented contract is to return the (possibly replaced) instance —
        // ResourceDictionary.Source is the canonical case where the new instance is the
        // externally-loaded dictionary and the original empty stub is discarded.
        return SetProperty(instance, propertyName, value, context, null);
    }

    /// <summary>
    /// Apply a SourceGenerator-lowered <c>{Binding ...}</c>. The SG already did the
    /// envelope + <c>SplitParameters</c> string work at compile time and hands us the
    /// positional path and the <c>name=value</c> pairs. We rebuild the
    /// <see cref="BindingExtension"/> via the verbatim <see cref="MarkupExtensionParser.BuildBindingExtension"/>
    /// (so Converter / Source / RelativeSource / nested markup resolve identically) and
    /// then run the <b>exact</b> apply/assign path the streaming parser uses for a
    /// runtime-parsed binding (mirrors <c>SetProperty</c>'s markup-extension branch):
    /// a produced <see cref="BindingExpressionBase"/> means
    /// <see cref="DependencyObject.SetBinding(DependencyProperty, BindingBase)"/> already
    /// bound it; otherwise the produced value is assigned through the same
    /// <see cref="BuilderSetProperty"/> path. Behaviour is byte-identical to
    /// <c>{Binding ...}</c> parsed at runtime — only the string parse moved to build time.
    /// </summary>
    [RequiresUnreferencedCode("Forwards to MarkupExtensionParser.BuildBindingExtension / ProvideExtensionValue which resolve nested markup extensions via reflection.")]
    internal static void BuilderApplyCompiledBinding(
        object target,
        string propertyName,
        string? positionalPath,
        string[] names,
        string[] values,
        XamlParserContext context)
    {
        var extension = MarkupExtensionParser.BuildBindingExtension(positionalPath, names, values, context);
        var result = MarkupExtensionParser.ProvideExtensionValue(extension, target, propertyName, context);

        // Mirror SetProperty's markup-extension branch exactly: SetBinding already
        // applied the binding when ProvideValue returned a BindingExpressionBase;
        // anything else is a value to assign through the normal property path.
        if (result is BindingExpressionBase)
            return;
        if (result != null)
            BuilderSetProperty(target, propertyName, result, context);
    }

    [RequiresUnreferencedCode("XamlBuilder.SetAttachedProperty resolves the static SetXxx method via reflection on the owner type.")]
    internal static void BuilderSetAttachedProperty(
        object instance,
        string ownerTypeName,
        string propertyName,
        string value,
        XamlParserContext context,
        string elementNamespaceUri)
    {
        SetAttachedProperty(
            instance,
            $"{ownerTypeName}.{propertyName}",
            value,
            context,
            // The SG emits attached references like Grid.Row that carry no explicit
            // attribute prefix — pass the element xmlns for both, which mirrors how
            // the runtime parser treats unprefixed attached attributes.
            elementNamespaceUri,
            elementNamespaceUri);
    }

    [RequiresUnreferencedCode("XamlBuilder.AddChild dispatches to the runtime AddChild path which may reflect on ContentPropertyAttribute / IList collections.")]
    internal static void BuilderAddChild(object parent, object child, XamlParserContext context, string? resourceKey = null)
    {
        AddChild(parent, child, context, resourceKey);
    }

    /// <summary>
    /// Apply a single child of a property-element (<c>&lt;Foo.Bar&gt;...&lt;/Foo.Bar&gt;</c>) to
    /// the named property on <paramref name="instance"/>. Mirrors the per-child logic from the
    /// streaming <see cref="ParsePropertyElement"/> path:
    /// <list type="bullet">
    /// <item>ResourceDictionary into ResourceDictionary → merge MergedDictionaries + entries (do NOT replace).</item>
    /// <item>IDictionary collection property → write under the explicit x:Key (or Style.TargetType key).</item>
    /// <item>Other read-only collection property → call AddToCollection.</item>
    /// <item>Otherwise → set the property value with the runtime resource-fallback semantics.</item>
    /// </list>
    /// </summary>
    [RequiresUnreferencedCode("Resolves the property setter on the runtime instance type via reflection and may invoke type converters / markup-extension resolution.")]
    internal static void BuilderApplyPropertyElementChild(
        object instance,
        string propertyName,
        object? childValue,
        XamlParserContext context,
        string? resourceKey)
    {
        var type = instance.GetType();
        var property = type.GetProperty(propertyName);
        if (property == null)
        {
            return;
        }

        var propertyValue = property.CanRead ? property.GetValue(instance) : null;
        var isCollection = propertyValue != null && IsCollectionType(property.PropertyType);

        childValue = ResolveMarkupExtensionValueIfNeeded(childValue, instance, property, context);

        if (property.PropertyType == typeof(ResourceDictionary) && childValue is ResourceDictionary dictionaryValue)
        {
            // Mirror the streaming parser's <Foo.Resources><ResourceDictionary>...</ResourceDictionary></Foo.Resources>
            // semantics: merge the child dictionary's MergedDictionaries + entries into the
            // existing Resources rather than replacing the whole instance — otherwise framework
            // theme dictionaries set up by ThemeManager.Initialize get blown away.
            var existing = property.GetValue(instance) as ResourceDictionary;
            if (existing == null || ReferenceEquals(existing, dictionaryValue))
            {
                if (property.CanWrite)
                {
                    property.SetValue(instance, dictionaryValue);
                }
            }
            else
            {
                foreach (var merged in dictionaryValue.MergedDictionaries)
                {
                    existing.MergedDictionaries.Add(merged);
                }
                foreach (KeyValuePair<object, object?> entry in dictionaryValue)
                {
                    existing[entry.Key] = entry.Value;
                }
            }
            return;
        }

        if (isCollection && propertyValue != null)
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
                AddToCollection(propertyValue, childValue!);
            }
            return;
        }

        SetPropertyValueWithResourceFallback(instance, property, childValue, context);
    }

    internal static void BuilderRegisterNamedScope(object root, XamlParserContext context)
    {
        RegisterNamedElementsInScope(root, context.NamedElements);
    }

    internal static void BuilderRegisterHotReload(object component)
    {
        HotReloadRuntime.RegisterComponent(component);
    }

    [RequiresUnreferencedCode("x:Name on a non-FrameworkElement may set Name via reflection.")]
    internal static void BuilderApplyXDirective(object instance, string directive, string value, XamlParserContext context)
    {
        // Mirror the streaming parser's x:* attribute handling so SG-emitted code can
        // forward x:Key / x:Name / x:Class through the same post-processing without
        // each call site re-implementing the rules.
        HandleXDirective(instance, directive, value, context);
    }

    /// <summary>
    /// Resolve a StaticResource immediately and assign it to <paramref name="propertyName"/>.
    /// Mirrors what <c>StaticResourceExtension.ProvideValue</c> does at runtime, but
    /// invoked directly by SG-emitted code so the markup-string parse / extension-instance
    /// allocation / IServiceProvider plumbing all disappear.
    /// </summary>
    [RequiresUnreferencedCode("Resolves the property setter on the target's runtime type via reflection.")]
    internal static void BuilderSetStaticResource(object target, string propertyName, string key, XamlParserContext context)
    {
        var fe = target as FrameworkElement;
        var value = ResourceLookup.FindResource(fe, key, out var sourceDictionary);
        if (value == null && context.TryGetResource(key, out var ambient, out sourceDictionary))
        {
            value = ambient;
        }

        if (value == null)
        {
            // Match the runtime parser's behaviour: an unresolved StaticResource is
            // silently swallowed (the value stays at the property's default).
            return;
        }

        object? targetProperty = DependencyProperty.FromName(target.GetType(), propertyName)
            ?? (object?)target.GetType().GetProperty(propertyName);
        ResourceDictionaryDiagnosticsStore.NotifyStaticResourceResolved(
            target,
            targetProperty,
            sourceDictionary,
            key);
        ApplyResolvedValue(target, propertyName, value, context);
    }

    /// <summary>
    /// Subscribe a property to a dynamic / theme resource so it tracks dictionary changes.
    /// Mirrors <c>DynamicResourceExtension.ProvideValue</c> + the
    /// <c>DynamicResourceBindingOperations.SetDynamicResource</c> registration the runtime
    /// would have done after parsing the markup string.
    /// </summary>
    [RequiresUnreferencedCode("Resolves DependencyProperty by name for the target's runtime type.")]
    internal static void BuilderSetDynamicResource(object target, string propertyName, string key, XamlParserContext context)
    {
        if (target is Setter setter && propertyName == nameof(Setter.Value))
        {
            // Setter.Value is a declaration, not a live target property. Resolving it here
            // would freeze the resource visible while the generated dictionary is built.
            // Preserve the expression so Style.Apply can install a per-target subscription.
            setter.Value = new DynamicResourceReference(key);
            return;
        }

        if (target is FrameworkElement fe)
        {
            var dp = DependencyProperty.FromName(target.GetType(), propertyName);
            if (dp != null)
            {
                DynamicResourceBindingOperations.SetDynamicResource(fe, dp, key);
                return;
            }
        }

        // Fallback for non-style declaration targets: resolve immediately when no live
        // DependencyProperty subscription can be installed.
        BuilderSetStaticResource(target, propertyName, key, context);
    }

    /// <summary>
    /// Wire <paramref name="propertyName"/> on <paramref name="target"/> to the property
    /// <paramref name="sourcePropertyName"/> on the templated parent. Mirrors what
    /// <c>TemplateBindingExtension.ProvideValue</c> does at runtime:
    /// <list type="bullet">
    ///   <item>Target is a DependencyObject + the property resolves to a DependencyProperty
    ///   → call <c>SetBinding(targetDp, new DeferredTemplateBinding(sourcePropertyName))</c>
    ///   so the binding engine wires up the templated-parent lookup at apply time.</item>
    ///   <item>Otherwise (e.g. <c>Setter.Value = "{TemplateBinding ...}"</c> in a Style)
    ///   → assign the <see cref="DeferredTemplateBinding"/> instance to the property so
    ///   <c>Setter.Apply</c> resolves it later when the trigger fires.</item>
    /// </list>
    /// </summary>
    [RequiresUnreferencedCode("Resolves DependencyProperty by name for the target's runtime type.")]
    internal static void BuilderSetTemplateBinding(object target, string propertyName, string sourcePropertyName, XamlParserContext context)
    {
        if (string.IsNullOrEmpty(sourcePropertyName))
        {
            return;
        }

        var binding = new Jalium.UI.Markup.DeferredTemplateBinding(sourcePropertyName);

        if (target is DependencyObject depObj)
        {
            var dp = DependencyProperty.FromName(target.GetType(), propertyName);
            if (dp != null)
            {
                depObj.SetBinding(dp, binding);
                return;
            }
        }

        // Non-DependencyObject target (Setter.Value, Trigger.Setters[N].Value): assign the
        // deferred binding instance directly so the runtime trigger machinery resolves it
        // at apply time. Mirrors the streaming parser's "return BindingBase from
        // ProvideValue → SetProperty walks property setter" path.
        ApplyResolvedValue(target, propertyName, binding, context);
    }

    [RequiresUnreferencedCode("ApplyResolvedValue assigns a property by name via reflection.")]
    private static void ApplyResolvedValue(object target, string propertyName, object value, XamlParserContext context)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite)
            return;

        if (property.PropertyType.IsInstanceOfType(value))
        {
            property.SetValue(target, value);
            return;
        }

        var converted = TypeConverterRegistry.ConvertValue(
            value as string ?? value.ToString() ?? string.Empty,
            property.PropertyType);
        if (converted != null)
        {
            property.SetValue(target, converted);
        }
    }

    /// <summary>
    /// Apply <paramref name="text"/> as the inner-text content of <paramref name="instance"/>.
    /// Routes through <see cref="SetContentProperty"/> so the runtime's <see cref="TypeConverter"/>
    /// + <c>ContentProperty</c> rules apply uniformly: <c>&lt;TextBlock&gt;Hello&lt;/TextBlock&gt;</c>
    /// becomes <c>tb.Text = "Hello"</c>; <c>&lt;Color&gt;#FFFFFF&lt;/Color&gt;</c> goes through
    /// <c>ColorConverter</c> and returns the parsed <see cref="Jalium.UI.Media.Color"/>. The
    /// returned reference may differ from <paramref name="instance"/> for value-type elements
    /// — the SG-emitted code reassigns its local accordingly.
    /// </summary>
    [RequiresUnreferencedCode("SetContentProperty walks ContentPropertyAttribute / TypeConverter via reflection.")]
    internal static object BuilderSetContentText(object instance, string text, XamlParserContext context)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(text);
        return SetContentProperty(instance, text, context);
    }

    [RequiresUnreferencedCode("Reads ContentPropertyAttribute metadata from the runtime type.")]
    private static string? GetContentPropertyName(Type type)
        => type.GetCustomAttribute<ContentPropertyAttribute>(inherit: true)?.Name;
}

/// <summary>
/// Context for XAML parsing operations.
/// </summary>
internal sealed class XamlParserContext : IAmbientResourceProvider
{
    // Bootstrap fallback: a handful of well-known XML namespace → CLR namespace pairs used
    // when XmlnsDefinitionRegistry has not yet observed an assembly-level declaration (for
    // example because the type lives in an assembly the scan has not reached, or because a
    // third-party library omits the attribute). Authoritative mappings come from
    // XmlnsDefinitionAttribute; this list is only consulted after the registry misses.
    private static readonly IReadOnlyList<string> _fallbackClrNamespaces = new[]
    {
        "Jalium.UI.Controls",
        "Jalium.UI.Controls.Primitives",
        "Jalium.UI.Shapes",
        "Jalium.UI",
        "Jalium.UI.Media",
        "Jalium.UI.Documents",
        "Jalium.UI.Interactivity",
        "Jalium.UI.Data",
    };

    // Process-wide type-resolution cache. Keyed by (namespaceUri, typeName, sourceAssembly):
    //   - 大多数 xmlns 命中 step 1/2/3 时与 SourceAssembly 无关 → key 退化为 (ns, name, null)
    //   - step 4 fallback CLR 命名空间扫描会优先 SourceAssembly,所以同 (ns, name) 在不同
    //     入口程序集下可能解出不同 type → 第三个分量隔离
    // **value 是 Type?** —— null 也 cache。失败查询会被反复触发(ResolveType 的若干 caller
    // 容忍 null,如 line ~2344/2360 markup-extension 字面量回退、attached property owner
    // 解析等),不缓存就让那些"失败 typeName"每次都走完整 8-namespace × 50-assembly 反射扫描
    // (每次 ~1ms)。null-cache 把"重复 failure"成本归零。
    private readonly record struct TypeCacheKey(string NamespaceUri, string TypeName, Assembly? SourceAssembly);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<TypeCacheKey, Type?> s_typeCache = new();

    // ResolveTypeInNamespace 内层 fullName cache。多个外层 (ns, name, asm) 触发的 step 4
    // fallback 扫描会反复用相同的 fullName="{clrNamespace}.{typeName}" 调 Assembly.GetType。
    // 给定 fullName,它在某 (sourceAssembly 优先序) 下找到/找不到的结果是确定的,因此
    // 用 (fullName, sourceAssembly) 作 key 缓存,把 8-namespace × 50-assembly 的反射扫描
    // 摊平到一次。null 也 cache(同样的"失败重复"问题)。
    private readonly record struct FullNameCacheKey(string FullName, Assembly? SourceAssembly);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<FullNameCacheKey, Type?> s_fullNameCache = new();

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
        => TryGetResource(key, out value, out _);

    internal bool TryGetResource(
        object key,
        out object? value,
        out ResourceDictionary? sourceDictionary)
    {
        // Search through the parent stack for ResourceDictionaries
        // and FrameworkElements that have their own Resources property
        foreach (var parent in _parentStack)
        {
            if (parent is ResourceDictionary rd &&
                rd.TryGetValue(key, out value, out sourceDictionary))
            {
                return true;
            }

            if (parent is FrameworkElement fe &&
                fe.Resources.TryGetValue(key, out value, out sourceDictionary))
            {
                return true;
            }
        }

        // Search through the parent ResourceDictionary's MergedDictionaries
        // This allows child XAML files to reference resources from sibling dictionaries
        // that were loaded earlier (e.g., Button.jalxaml can reference Colors.jalxaml resources)
        if (ParentResourceDictionary != null &&
            ParentResourceDictionary.TryGetValue(key, out value, out sourceDictionary))
        {
            return true;
        }

        // Fall back to Application resources for template parsing scenarios
        // When parsing ControlTemplate content, the parent stack is empty but
        // Application resources should still be accessible
        if (ResourceLookup.ApplicationResourceLookupWithSource != null)
        {
            var applicationResult = ResourceLookup.ApplicationResourceLookupWithSource(key);
            value = applicationResult.Value;
            sourceDictionary = applicationResult.Dictionary;
            if (value != null)
            {
                return true;
            }
        }
        else if (ResourceLookup.ApplicationResourceLookup != null)
        {
            value = ResourceLookup.ApplicationResourceLookup(key);
            if (value != null)
            {
                sourceDictionary = null;
                return true;
            }
        }

        value = null;
        sourceDictionary = null;
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
    /// 拍下当前解析栈中所有"可作为资源源"的 ResourceDictionary 引用。
    /// 用于把祖先 UserControl.Resources / Window.Resources / 显式 ResourceDictionary
    /// 透传给延迟解析的模板（DataTemplate / ItemsPanelTemplate）。
    ///
    /// 顺序保持"近 → 远"：栈顶（最近的祖先）在前，栈底（最外层）在后。
    /// 遍历 _parentStack 自带"栈顶在前"语义，符合预期。
    /// </summary>
    internal IReadOnlyList<ResourceDictionary>? SnapshotAmbientResourceDictionaries()
    {
        List<ResourceDictionary>? snapshot = null;
        foreach (var parent in _parentStack)
        {
            ResourceDictionary? rd = null;
            if (parent is ResourceDictionary direct)
            {
                rd = direct;
            }
            else if (parent is FrameworkElement fe)
            {
                // 仅当 Resources 已经被实例化（含至少一项）才纳入，避免每个元素都建空字典污染栈。
                var feResources = fe.HasResources ? fe.Resources : null;
                if (feResources != null && feResources.Count > 0)
                {
                    rd = feResources;
                }
            }

            if (rd != null)
            {
                snapshot ??= new List<ResourceDictionary>();
                if (!snapshot.Contains(rd))
                {
                    snapshot.Add(rd);
                }
            }
        }
        return snapshot;
    }

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

        var normalizedName = propertyName.Trim();
        if (normalizedName.Length > 2 && normalizedName[0] == '(' && normalizedName[^1] == ')')
        {
            normalizedName = normalizedName.Substring(1, normalizedName.Length - 2).Trim();
        }

        var separator = normalizedName.LastIndexOf('.');
        if (separator > 0 && separator < normalizedName.Length - 1)
        {
            var ownerToken = normalizedName.Substring(0, separator);
            var ownerSimpleNameSeparator = Math.Max(ownerToken.LastIndexOf('.'), ownerToken.LastIndexOf(':'));
            var ownerSimpleName = ownerSimpleNameSeparator >= 0
                ? ownerToken.Substring(ownerSimpleNameSeparator + 1)
                : ownerToken;
            var ownerType = XamlTypeRegistry.GetType(ownerToken) ?? XamlTypeRegistry.GetType(ownerSimpleName);
            return ownerType == null
                ? null
                : DependencyProperty.FromName(ownerType, normalizedName.Substring(separator + 1));
        }

        // AOT-safe lookup via the DependencyProperty registry — walks the inheritance chain.
        return DependencyProperty.FromName(targetType, normalizedName);
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
        var cacheKey = new TypeCacheKey(namespaceUri, typeName, SourceAssembly);
        if (s_typeCache.TryGetValue(cacheKey, out var cachedType))
        {
            return cachedType;
        }

        var resolved = ResolveTypeUncached(namespaceUri, typeName);
        // null 也 cache —— 见 s_typeCache 注释。
        s_typeCache.TryAdd(cacheKey, resolved);
        return resolved;
    }

    private Type? ResolveTypeUncached(string namespaceUri, string typeName)
    {
        // 1) clr-namespace:Foo;assembly=Bar — an explicit CLR namespace reference.
        if (namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var type = ResolveClrNamespaceType(namespaceUri, typeName);
            if (type != null) return type;
        }

        // 2) Look up assembly-level XmlnsDefinition mappings. The registry is populated by
        //    scanning XmlnsDefinitionAttribute on every loaded assembly, so user assemblies
        //    can opt into a shared XML namespace simply by declaring the attribute.
        var mappings = XmlnsDefinitionRegistry.GetMappings(namespaceUri);
        if (!mappings.IsDefaultOrEmpty)
        {
            foreach (var mapping in mappings)
            {
                var type = ResolveTypeInNamespace(mapping.ClrNamespace, typeName, mapping.Assembly);
                if (type != null) return type;
            }
        }

        // 3) Fall back to the AOT-friendly static type registry by simple name. This catches
        //    the bootstrap window before XmlnsDefinition scanning has seen the framework
        //    assemblies, and also handles the empty default namespace (xmlns="") case.
        var registryType = XamlTypeRegistry.GetType(typeName);
        if (registryType != null) return registryType;

        // 4) Scan the framework CLR namespaces by convention. This covers third-party assemblies
        //    that ship types in these namespaces without declaring XmlnsDefinitionAttribute.
        foreach (var clrNamespace in _fallbackClrNamespaces)
        {
            var type = ResolveTypeInNamespace(clrNamespace, typeName);
            if (type != null) return type;
        }

        // 5) Markup extension convention: "Foo" resolves to "FooExtension".
        return XamlTypeRegistry.GetType(typeName + "Extension");
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

        var assembly = assemblyName != null ? FindAssemblyBySimpleName(assemblyName) : null;
        return ResolveTypeInNamespace(ns, typeName, assembly);
    }

    private static Assembly? FindAssemblyBySimpleName(string assemblyName)
    {
        foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        return null;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "This is the user-defined-type fallback of the runtime XAML reader, reached only after the AOT-safe XamlTypeRegistry (which preserves every framework type) misses. Resolving a user type named in jalxaml via Assembly.GetType is the documented consumer responsibility under AOT — the RUC contract is already declared at the public XamlReader.Load/Parse boundary (see the class-level RequiresUnreferencedCode), and user code-behind types reachable from XAML are preserved via XamlTypeRegistry registration / the SourceGenerator's DynamicDependency emission.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2073:DynamicallyAccessedMembers",
        Justification = "The [return: DynamicallyAccessedMembers] promises preserved members on the resolved type so downstream reflective construction is safe. For the registry hit path that guarantee holds (registered types carry the DAM annotation on Register<T>/RegisterType<T>). For this Assembly.GetType fallback the resolved user type is a runtime-discovered token that cannot carry DAM statically; its preservation is the documented consumer responsibility (XamlReader.Load/Parse RUC contract + SourceGenerator DynamicDependency), so the return annotation is satisfied by the consumer-side preservation rather than a static flow.")]
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)]
    private Type? ResolveTypeInNamespace(string clrNamespace, string typeName, Assembly? preferredAssembly = null)
    {
        // Try the AOT-friendly static type registry first. It's keyed by simple name and is
        // populated with every framework type that can appear in XAML.
        var registryType = XamlTypeRegistry.GetType(typeName);
        if (registryType != null)
            return registryType;

        // Fall back to assembly-based resolution for user-defined types. The preferred assembly
        // (from XmlnsDefinition scanning or clr-namespace;assembly=) is searched first so
        // user intent is respected when the same simple name is reused.
        var fullName = $"{clrNamespace}.{typeName}";

        // 内层 fullName cache:外层 (ns, name, asm) 触发的 8-namespace 循环会反复用相同的
        // fullName 调 Assembly.GetType。这里把反射扫描的结果(命中或 null)按
        // (fullName, sourceAssembly) 缓存。null 也 cache —— failure 路径不重复 scan。
        var fullNameKey = new FullNameCacheKey(fullName, SourceAssembly);
        if (s_fullNameCache.TryGetValue(fullNameKey, out var cached))
        {
            if (cached != null)
            {
                XamlTypeRegistry.RegisterType(typeName, cached);
            }
            return cached;
        }

        // 扫描集合精简策略:
        //   1. preferredAssembly (来自 XmlnsDefinition mapping.Assembly 或 clr-namespace;assembly=)
        //      —— 用户/属性显式声明的入口,最优先。
        //   2. SourceAssembly —— 当前 jalxaml 文件所在 assembly,99% 的 user types 在这里。
        //
        // 不再扫描 AppDomain.CurrentDomain.GetAssemblies() 全集 —— 启动期 50+ assemblies
        // 中 ReactiveUI/Splat/Microsoft.Extensions.* 等极不可能含 jalxaml types,但每次
        // step 4 fallback 都扫一遍,72 个 unique miss × 8 namespace × 50 assemblies =
        // 28800 次 Assembly.GetType 反射,~70ms 浪费。framework types 全部预注册在
        // XamlTypeRegistry (step 3 命中),不会进入这里;user types 必然在 SourceAssembly
        // 或显式 xmlns prefix 指向的 preferredAssembly,精简到这两者已经覆盖了所有真实
        // 命中场景。如果跨 assembly 引用 user types,jalxaml 应该用 `clr-namespace:Foo;
        // assembly=Bar` 显式声明 —— 这是 XAML 标准做法,避免歧义。
        Type? result = null;

        if (preferredAssembly != null)
        {
            result = preferredAssembly.GetType(fullName);
        }

        if (result == null && SourceAssembly != null && !ReferenceEquals(SourceAssembly, preferredAssembly))
        {
            result = SourceAssembly.GetType(fullName);
        }

        s_fullNameCache.TryAdd(fullNameKey, result);
        if (result != null)
        {
            XamlTypeRegistry.RegisterType(typeName, result);
        }
        return result;
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

    [UnconditionalSuppressMessage("Trimming", "IL2026:Static field initializer cannot itself declare RequiresUnreferencedCode.", Justification = "This initializer registers Type tokens only; reflective construction happens in XamlReader.Load which carries the RUC contract.")]
    private static Dictionary<string, Type> InitializeTypes()
    {
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);

        // Register all known XAML types
        RegisterCoreTypes(types);
        RegisterMarkupTypes(types);
        RegisterControlTypes(types);
        RegisterMediaTypes(types);
        RegisterShapeTypes(types);
        RegisterDataTypes(types);
        RegisterDocumentTypes(types);
        RegisterInteractivityTypes(types);

        return types;
    }

    [RequiresUnreferencedCode("Registers core types whose markup-extension ProvideValue overrides are themselves annotated with RequiresUnreferencedCode.")]
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
        Register<MultiTrigger>(types);
        Register<Condition>(types);
        Register<DataTrigger>(types);
        Register<EventTrigger>(types);
        Register<ControlTemplate>(types);
        Register<DataTemplate>(types);
        Register<HierarchicalDataTemplate>(types);
        Register<Jalium.UI.Controls.DataTemplateSelector>(types);
        Register<Jalium.UI.Controls.ItemsPanelTemplate>(types);
        Register<ResourceDictionary>(types);
        Register<Binding>(types);
        Register<BindingBase>(types);
        Register<BindingExtension>(types);
        Register<global::Jalium.UI.StaticResourceExtension>(types);
        Register<global::Jalium.UI.DynamicResourceExtension>(types);
        Register<global::Jalium.UI.ThemeDictionaryExtension>(types);
        Register<global::Jalium.UI.ColorConvertedBitmapExtension>(types);
        Register<ThemeResourceExtension>(types);
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

    private static void RegisterMarkupTypes(Dictionary<string, Type> types)
    {
        Register<Markup.RazorSectionHost>(types);
    }

    [RequiresUnreferencedCode("Registers control types whose ctors / ProvideValue overrides are themselves annotated with RequiresUnreferencedCode.")]
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
        Register<Jalium.UI.Controls.ItemContainerTemplate>(types);
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
        Register<Jalium.UI.Controls.Primitives.DataGridColumnHeader>(types);
        Register<TreeDataGrid>(types);
        Register<TreeDataGridRow>(types);
        Register<Slider>(types);
        Register<RangeSlider>(types);
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
        Register<TreeSelector>(types);
        Register<TreeSelectorItem>(types);
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
        Register<Jalium.UI.Controls.Primitives.StatusBar>(types);
        Register<Jalium.UI.Controls.Primitives.StatusBarItem>(types);
        Register<Separator>(types);
        Register<InfoBar>(types);
        Register<Thumb>(types);
        Register<Expander>(types);
        Register<GroupBox>(types);
        Register<Viewbox>(types);
        Register<GridSplitter>(types);
        Register<DockLayout>(types);
        Register<Split>(types);
        Register<DockSplitPanel>(types);
        Register<DockTabPanel>(types);
        Register<DockItem>(types);
        Register<TransitioningContentControl>(types);
        Register<JsonTreeViewer>(types);
        Register<PropertyGrid>(types);
        Register<MapView>(types);
        Register<MiniMap>(types);
        Register<GeographicHeatmap>(types);
        Register<Terminal>(types);
        Register<DiffViewer>(types);
        Register<HexEditor>(types);

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
        // 抽象基类 VirtualizingPanel 也要注册：VirtualizingPanel.IsVirtualizing /
        // VirtualizationMode / ScrollUnit 等附加属性的 owner 是这个基类，jalxaml 里
        // <VirtualizingStackPanel VirtualizingPanel.IsVirtualizing="True" /> 解析 owner 时
        // 需按简单名找到它（否则 SetAttachedProperty 抛 "Cannot resolve attached property owner type"）。
        Register<VirtualizingPanel>(types);
        Register<VirtualizingStackPanel>(types);

        // Static service classes used as attached-property owners
        RegisterStaticOwner(types, typeof(ContextMenuService));
    }

    private static void RegisterStaticOwner(
        Dictionary<string, Type> types,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.PublicFields)] Type ownerType)
    {
        types[ownerType.Name] = ownerType;
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
        // Jalium.UI.Shapes namespace
        Register<Shape>(types);
        Register<Ellipse>(types);
        Register<Rectangle>(types);
        Register<Jalium.UI.Shapes.Path>(types);
        Register<Line>(types);
        Register<Polygon>(types);
        Register<Polyline>(types);
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
        Register<Jalium.UI.Controls.Decorator>(types);
    }

    private static void RegisterInteractivityTypes(Dictionary<string, Type> types)
    {
        // Jalium.UI.Interactivity namespace
        Register<BehaviorEventTrigger>(types);
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2073:DynamicallyAccessedMembers",
        Justification = "The _types dictionary is populated exclusively by the DAM-annotated Register<T>/RegisterType<T> generics (each carries the same PublicConstructors|PublicProperties|PublicFields|PublicMethods|NonPublicFields annotation on T), so every stored Type already has those members preserved. The trimmer cannot propagate DAM through the Dictionary<string,Type> value type, so it cannot statically prove the returned token satisfies the [return: DAM] promise; the promise is in fact upheld by those registration generics.")]
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)]
    public static Type? GetType(string typeName)
    {
        var result = _types.GetValueOrDefault(typeName);
        if (result == null)
        {
        }
        return result;
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
    internal static void RegisterType(string name, Type type)
    {
        _types[name] = type;
    }

    // Full-name index for x:Class types (e.g. "Jalium.UI.Gallery.Modules.Main.Views.MainWindow").
    // Keyed by the CLR full name because x:Class is always fully qualified; the simple-name
    // _types dictionary would collide on duplicate leaf names across namespaces.
    //
    // Populated by source-generator-emitted ModuleInitializer stubs so that every jalxaml
    // x:Class is both (a) referenced via typeof(T) in IL — preventing the trimmer from
    // removing the type — and (b) discoverable by full name at runtime. StartupUri and any
    // other string→Type lookup path consults this registry first, which is the only
    // AOT-reliable way to recover a user type from a string after trimming.
    private static readonly Dictionary<string, Type> _classFullNameTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Maps every plausible <c>StartupUri</c> spelling for a SG-registered code-behind type
    /// to the type itself. The runtime entry point (<see cref="ThemeLoader.LoadStartupObjectFromUri"/>)
    /// queries this dictionary first so a missing manifest resource (the default after the
    /// jalxaml-no-embed switch) does not break startup-window loading.
    /// </summary>
    /// <remarks>
    /// Each generator emits multiple key spellings for one type — slash-separated /
    /// dot-separated / RootNamespace-prefixed variants — so the lookup matches whatever
    /// shape a developer wrote in <c>Application.StartupUri="…"</c>. Comparison is case
    /// insensitive to mirror Windows file-system semantics.
    /// </remarks>
    private static readonly Dictionary<string, Type> _startupUriTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a user x:Class type by its CLR full name so StartupUri / string-based lookups
    /// can find it after AOT trimming. Emitted automatically by the JALXAML source generator
    /// (one <c>[ModuleInitializer]</c> per jalxaml file) — manual calls are rarely needed.
    /// </summary>
    /// <param name="fullName">The type's CLR full name (e.g. namespace + "." + class name).</param>
    /// <param name="type">
    /// The type itself. Must be supplied as <c>typeof(TYourClass)</c> so the call site
    /// becomes a static reference that the trimmer preserves.
    /// </param>
    public static void RegisterStartupType(
        string fullName,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicFields)]
        Type type)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(type);
        _classFullNameTypes[fullName] = type;
    }

    /// <summary>
    /// Looks up a type previously registered via <see cref="RegisterStartupType"/> by CLR full name.
    /// Returns <c>null</c> if no type with that full name was registered.
    /// </summary>
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)]
    public static Type? GetStartupType(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        if (_classFullNameTypes.TryGetValue(fullName, out var type))
        {
            return type;
        }

        return null;
    }

    /// <summary>
    /// Register a <c>StartupUri</c> spelling that maps to the SG-generated code-behind type.
    /// The generator calls this once per supported spelling (slash-separated path,
    /// dot-separated path, root-namespace prefix variants) so any reasonable
    /// <c>Application.StartupUri</c> string lands on the same type. Replaces the historical
    /// "manifest resource lookup → x:Class extraction" path now that .jalxaml files are
    /// no longer embedded by default.
    /// </summary>
    public static void RegisterStartupUri(
        string startupUri,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicFields)]
        Type type)
    {
        ArgumentNullException.ThrowIfNull(startupUri);
        ArgumentNullException.ThrowIfNull(type);
        _startupUriTypes[startupUri] = type;
    }

    /// <summary>
    /// Look up the type registered under <paramref name="startupUri"/> via
    /// <see cref="RegisterStartupUri"/>. Returns null when no matching spelling was
    /// registered — the caller then falls back to the legacy manifest-resource path.
    /// </summary>
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicFields)]
    public static Type? GetStartupTypeByUri(string startupUri)
    {
        ArgumentNullException.ThrowIfNull(startupUri);
        return _startupUriTypes.TryGetValue(startupUri, out var type) ? type : null;
    }

    /// <summary>
    /// Applies a compiled UI bundle to an existing component instance.
    /// This is the optimized path for pre-compiled JALXAML binary data.
    /// </summary>
    /// <param name="component">The component instance to apply the bundle to.</param>
    /// <param name="bundle">The compiled UI bundle.</param>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Forwards to the three-arg ApplyBundle with namedElements=null, which per its RequiresUnreferencedCode contract 'walks the component runtime type via reflection to assign named fields. Pass a non-null namedElements dictionary for AOT-safe operation.' This compatibility overload is the runtime (non-SourceGenerator) JALXAML application path whose reflective named-element wiring is the documented consumer responsibility; AOT consumers use the SourceGenerator-emitted three-arg call that supplies a non-null namedElements dictionary (see JalxamlLoader).")]
    public static void ApplyBundle(object component, Gpu.CompiledUIBundle bundle)
        => ApplyBundle(component, bundle, null);

    /// <summary>
    /// Applies a compiled UI bundle with AOT-safe named element output.
    /// Named elements are collected into the provided dictionary instead of being wired via reflection.
    /// </summary>
    [RequiresUnreferencedCode("When namedElements is null this method walks the component runtime type via reflection to assign named fields. Pass a non-null namedElements dictionary for AOT-safe operation.")]
    public static void ApplyBundle(object component, Gpu.CompiledUIBundle bundle, Dictionary<string, object>? namedElements)
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
        foreach (var node in bundle.Nodes)
        {
            var nodeName = GetNodeName(bundle, node);
            if (string.IsNullOrWhiteSpace(nodeName))
                continue;

            if (!nodeElements.TryGetValue(node.Id, out var element))
                continue;

            if (namedElements != null)
            {
                // AOT-safe: output to dictionary, let caller wire fields
                namedElements[nodeName] = element;
            }
            else
            {
                // Reflection path (non-AOT)
                var componentType = component.GetType();
                var field = componentType.GetField(nodeName,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (field != null && field.FieldType.IsAssignableFrom(element.GetType()))
                {
                    field.SetValue(component, element);
                }
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
    [RequiresUnreferencedCode("Speculative reflection probe for future SceneNode metadata Name/XName properties.")]
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "The default case calls TryAttachFallbackChild, whose RequiresUnreferencedCode contract is 'Reads ContentPropertyAttribute and the matching property via reflection on the parent runtime type.' AttachFallbackVisual is reached only from the reflective (non-AOT, namedElements==null) ApplyBundle fallback path, which already declares the RequiresUnreferencedCode contract; preserving the parent type's ContentPropertyAttribute-named property is the documented consumer responsibility on that runtime path.")]
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

    [RequiresUnreferencedCode("Reads ContentPropertyAttribute and the matching property via reflection on the parent runtime type.")]
    private static void TryAttachFallbackChild(object parent, UIElement child)
    {
        var parentType = parent.GetType();
        string? contentPropertyName = GetContentPropertyName(parentType);
        if (contentPropertyName == null)
            return;

        var property = parentType.GetProperty(contentPropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

    [RequiresUnreferencedCode("Reads ContentPropertyAttribute metadata from the runtime type.")]
    private static string? GetContentPropertyName(Type type)
        => type.GetCustomAttribute<ContentPropertyAttribute>(inherit: true)?.Name;

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
                element.Source = Jalium.UI.Media.ImageSourceLoader.FromUri(uri);
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
        var element = new Jalium.UI.Shapes.Path();

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
[Serializable]
public partial class XamlParseException : SystemException
{
    public XamlParseException()
    {
    }

    public XamlParseException(string message) : base(message) { }

    public XamlParseException(string message, Exception innerException) : base(message, innerException) { }

    public XamlParseException(string message, int lineNumber, int linePosition)
        : base(message)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    public XamlParseException(string message, int lineNumber, int linePosition, Exception innerException)
        : base(message, innerException)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

#pragma warning disable SYSLIB0051
    protected XamlParseException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ArgumentNullException.ThrowIfNull(info);
        LineNumber = info.GetInt32(nameof(LineNumber));
        LinePosition = info.GetInt32(nameof(LinePosition));
        BaseUri = (Uri?)info.GetValue(nameof(BaseUri), typeof(Uri));
        KeyContext = info.GetValue(nameof(KeyContext), typeof(object));
        NameContext = info.GetString(nameof(NameContext));
        UidContext = info.GetString(nameof(UidContext));
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        base.GetObjectData(info, context);
        info.AddValue(nameof(LineNumber), LineNumber);
        info.AddValue(nameof(LinePosition), LinePosition);
        info.AddValue(nameof(BaseUri), BaseUri, typeof(Uri));
        info.AddValue(nameof(KeyContext), KeyContext, typeof(object));
        info.AddValue(nameof(NameContext), NameContext);
        info.AddValue(nameof(UidContext), UidContext);
    }
#pragma warning restore SYSLIB0051

    public int LineNumber { get; }

    public int LinePosition { get; }

    public Uri? BaseUri { get; internal set; }

    public object? KeyContext { get; internal set; }

    public string? NameContext { get; internal set; }

    public string? UidContext { get; internal set; }
}

