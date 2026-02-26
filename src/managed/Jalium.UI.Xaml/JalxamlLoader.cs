using System.Reflection;
using Jalium.UI.Gpu;

namespace Jalium.UI.Markup;

/// <summary>
/// Provides methods for loading JALXAML (Jalium XAML) files.
/// </summary>
public static class JalxamlLoader
{
    /// <summary>
    /// Loads compiled JALXAML binary data into an existing component instance.
    /// This is the optimized path for Source Generator embedded binary data.
    /// </summary>
    /// <param name="component">The component instance to load into.</param>
    /// <param name="compiledData">The compiled .uic binary data.</param>
    public static void LoadFromCompiledData(object component, ReadOnlySpan<byte> compiledData)
    {
        var bundle = BundleSerializer.Load(compiledData);
        XamlTypeRegistry.ApplyBundle(component, bundle);
    }

    /// <summary>
    /// Loads compiled JALXAML from an embedded .uic resource into an existing component instance.
    /// This is used by Source Generator generated InitializeComponent() methods.
    /// </summary>
    /// <param name="component">The component instance to load into.</param>
    /// <param name="resourceName">The name of the embedded .uic resource (e.g., "AssemblyName.ClassName.uic").</param>
    public static void LoadFromCompiledResource(object component, string resourceName)
    {
        var assembly = component.GetType().Assembly;

        // Try to find the embedded resource
        var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            // List available resources for debugging
            var availableResources = assembly.GetManifestResourceNames();
            throw new JalxamlLoadException(
                $"Cannot find embedded .uic resource '{resourceName}' in assembly '{assembly.GetName().Name}'. " +
                $"Available resources: [{string.Join(", ", availableResources)}]");
        }

        using (stream)
        {
            // Read the binary data from the stream
            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer, 0, buffer.Length);

            // Load using the compiled data path
            var bundle = BundleSerializer.Load(buffer);
            XamlTypeRegistry.ApplyBundle(component, bundle);
        }
    }

    /// <summary>
    /// Loads a JALXAML file from an embedded resource.
    /// </summary>
    /// <param name="resourceName">The name of the embedded resource (e.g., "Pages/HomePage.jalxaml").</param>
    /// <param name="assembly">The assembly containing the resource. If null, uses the calling assembly.</param>
    /// <returns>The root object created from the JALXAML.</returns>
    public static object LoadFromResource(string resourceName, Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();

        // Try different resource name formats
        var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            // Try with assembly name prefix
            var assemblyName = assembly.GetName().Name;
            stream = assembly.GetManifestResourceStream($"{assemblyName}.{resourceName}");
        }

        if (stream == null)
        {
            // Try replacing path separators
            var normalizedName = resourceName.Replace('/', '.').Replace('\\', '.');
            stream = assembly.GetManifestResourceStream(normalizedName);

            if (stream == null)
            {
                var assemblyName = assembly.GetName().Name;
                stream = assembly.GetManifestResourceStream($"{assemblyName}.{normalizedName}");
            }
        }

        if (stream == null)
        {
            // List available resources for debugging
            var availableResources = assembly.GetManifestResourceNames();
            throw new JalxamlLoadException(
                $"Cannot find embedded resource '{resourceName}' in assembly '{assembly.GetName().Name}'. " +
                $"Available resources: [{string.Join(", ", availableResources)}]");
        }

        using (stream)
        {
            return XamlReader.Load(stream);
        }
    }

    /// <summary>
    /// Loads a JALXAML file from the file system.
    /// </summary>
    /// <param name="filePath">The path to the JALXAML file.</param>
    /// <returns>The root object created from the JALXAML.</returns>
    public static object LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new JalxamlLoadException($"JALXAML file not found: {filePath}");
        }

        var content = File.ReadAllText(filePath);
        return XamlReader.Parse(content);
    }

    /// <summary>
    /// Loads a JALXAML string and creates an object tree.
    /// </summary>
    /// <param name="jalxaml">The JALXAML content string.</param>
    /// <returns>The root object created from the JALXAML.</returns>
    public static object Parse(string jalxaml)
    {
        return XamlReader.Parse(jalxaml);
    }

    /// <summary>
    /// Loads a JALXAML file from an embedded resource and casts to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected type of the root element.</typeparam>
    /// <param name="resourceName">The name of the embedded resource.</param>
    /// <param name="assembly">The assembly containing the resource. If null, uses the calling assembly.</param>
    /// <returns>The root object cast to type T.</returns>
    public static T LoadFromResource<T>(string resourceName, Assembly? assembly = null) where T : class
    {
        assembly ??= Assembly.GetCallingAssembly();
        var result = LoadFromResource(resourceName, assembly);

        if (result is T typedResult)
        {
            return typedResult;
        }

        throw new JalxamlLoadException(
            $"Expected root element of type '{typeof(T).Name}' but got '{result?.GetType().Name ?? "null"}'.");
    }

    /// <summary>
    /// Loads a JALXAML file from the file system and casts to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected type of the root element.</typeparam>
    /// <param name="filePath">The path to the JALXAML file.</param>
    /// <returns>The root object cast to type T.</returns>
    public static T LoadFromFile<T>(string filePath) where T : class
    {
        var result = LoadFromFile(filePath);

        if (result is T typedResult)
        {
            return typedResult;
        }

        throw new JalxamlLoadException(
            $"Expected root element of type '{typeof(T).Name}' but got '{result?.GetType().Name ?? "null"}'.");
    }

    /// <summary>
    /// Loads JALXAML content into an existing component instance (for code-behind support).
    /// This is typically called from InitializeComponent() in code-behind classes.
    /// </summary>
    /// <param name="component">The component instance to load into.</param>
    /// <param name="resourceName">The embedded resource name of the JALXAML file.</param>
    /// <param name="assembly">The assembly containing the resource. If null, uses the assembly of the component type.</param>
    public static void LoadComponent(object component, string resourceName, Assembly? assembly = null)
    {
        XamlReader.LoadComponent(component, resourceName, assembly);
    }

    /// <summary>
    /// Loads JALXAML content into an existing component instance using a Pack URI.
    /// This is the preferred method called from generated InitializeComponent() methods.
    /// </summary>
    /// <param name="component">The component instance to load into.</param>
    /// <param name="resourceUri">Pack URI in the format: /AssemblyName;component/Path/To/File.jalxaml</param>
    /// <remarks>
    /// Pack URI format: /AssemblyName;component/relative/path/file.jalxaml
    /// Example: /Jalium.UI.Gallery;component/Views/ButtonPage.jalxaml
    /// </remarks>
    public static void LoadComponent(object component, Uri resourceUri)
    {
        var (assemblyName, componentPath) = ParsePackUri(resourceUri);

        // Find the assembly by name (AOT-safe: prefer component's own assembly)
        Assembly? assembly = null;
        if (!string.IsNullOrEmpty(assemblyName))
        {
            // First check if it matches the component's own assembly
            var componentAssembly = component.GetType().Assembly;
            if (componentAssembly.GetName().Name == assemblyName)
            {
                assembly = componentAssembly;
            }
            else
            {
                // Try to load by name (works in both AOT and non-AOT)
                try
                {
                    assembly = Assembly.Load(new AssemblyName(assemblyName));
                }
                catch
                {
                    assembly = componentAssembly;
                }
            }
        }
        else
        {
            assembly = component.GetType().Assembly;
        }

        // Convert component path to embedded resource name
        // Views/ButtonPage.jalxaml -> AssemblyName.Views.ButtonPage.jalxaml
        var resourceName = componentPath.Replace('/', '.');
        var fullResourceName = $"{assembly.GetName().Name}.{resourceName}";

        XamlReader.LoadComponent(component, fullResourceName, assembly);
    }

    /// <summary>
    /// Parses a Pack URI to extract assembly name and component path.
    /// </summary>
    /// <param name="packUri">The Pack URI to parse.</param>
    /// <returns>A tuple of (assemblyName, componentPath).</returns>
    /// <exception cref="JalxamlLoadException">Thrown when the URI format is invalid.</exception>
    private static (string assemblyName, string componentPath) ParsePackUri(Uri packUri)
    {
        // Expected format: /AssemblyName;component/Path/To/File.jalxaml
        var uriString = packUri.OriginalString;

        // Remove leading slash
        if (uriString.StartsWith("/", StringComparison.Ordinal))
        {
            uriString = uriString.Substring(1);
        }

        // Find ";component/" separator
        const string componentSeparator = ";component/";
        var separatorIndex = uriString.IndexOf(componentSeparator, StringComparison.OrdinalIgnoreCase);

        if (separatorIndex < 0)
        {
            throw new JalxamlLoadException(
                $"Invalid Pack URI format: '{packUri.OriginalString}'. " +
                $"Expected format: /AssemblyName;component/Path/To/File.jalxaml");
        }

        var assemblyName = uriString.Substring(0, separatorIndex);
        var componentPath = uriString.Substring(separatorIndex + componentSeparator.Length);

        return (assemblyName, componentPath);
    }

    /// <summary>
    /// Gets the default JALXAML resource name for a component type.
    /// Convention: TypeName.jalxaml in the same namespace folder structure.
    /// </summary>
    /// <param name="componentType">The type of the component.</param>
    /// <returns>The conventional resource name.</returns>
    public static string GetDefaultResourceName(Type componentType)
    {
        // Convert namespace to path: Jalium.UI.Gallery.Views.BorderPage -> Jalium.UI.Gallery.Views.BorderPage.jalxaml
        return $"{componentType.FullName}.jalxaml";
    }
}

/// <summary>
/// Exception thrown when JALXAML loading fails.
/// </summary>
public sealed class JalxamlLoadException : Exception
{
    public JalxamlLoadException(string message) : base(message) { }
    public JalxamlLoadException(string message, Exception innerException) : base(message, innerException) { }
}
