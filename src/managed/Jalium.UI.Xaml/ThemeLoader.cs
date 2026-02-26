using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Markup;

/// <summary>
/// Provides theme loading utilities for the Jalium.UI framework.
/// </summary>
public static class ThemeLoader
{
    /// <summary>
    /// Module initializer that registers the XAML loader callback with ThemeManager.
    /// This runs automatically when the Jalium.UI.Xaml assembly is loaded,
    /// eliminating the need for reflection to bridge Controls and Xaml projects (AOT-safe).
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries")]
    public static void Initialize()
    {
        // Register XamlReader.Load as the theme loader callback
        ThemeManager.XamlLoader = LoadResourceDictionaryFromStream;

        // Register AOT-safe type resolver for PropertyPath and other Core types
        TypeResolver.ResolveTypeByName = XamlTypeRegistry.GetType;

        // If Application was already created but theme not yet loaded, load now
        if (Application.Current != null && !ThemeManager.IsInitialized)
        {
            ThemeManager.Initialize(Application.Current);
        }
    }

    private static ResourceDictionary? LoadResourceDictionaryFromStream(
        Stream stream, string resourceName, Assembly sourceAssembly)
    {
        try
        {
            return XamlReader.Load(stream, resourceName, sourceAssembly) as ResourceDictionary;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeLoader] Failed to load '{resourceName}' from {sourceAssembly.GetName().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads a ResourceDictionary from a stream containing XAML content.
    /// </summary>
    /// <param name="stream">The stream containing the XAML content.</param>
    /// <returns>The loaded ResourceDictionary, or null if loading failed.</returns>
    public static ResourceDictionary? LoadResourceDictionary(Stream stream)
    {
        try
        {
            return XamlReader.Load(stream) as ResourceDictionary;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the Generic theme ResourceDictionary.
    /// </summary>
    /// <returns>The Generic theme ResourceDictionary, or null if loading failed.</returns>
    public static ResourceDictionary? LoadGenericTheme()
    {
        using var stream = ThemeManager.GetGenericThemeStream();
        if (stream == null)
            return null;

        return LoadResourceDictionary(stream);
    }
}
