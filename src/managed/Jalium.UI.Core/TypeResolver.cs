namespace Jalium.UI;

/// <summary>
/// Provides AOT-safe type resolution by name.
/// The resolver delegate is registered by the Jalium.UI.Xaml assembly via ModuleInitializer,
/// bridging the gap between Core (which can't reference Xaml) and the XamlTypeRegistry.
/// </summary>
internal static class TypeResolver
{
    /// <summary>
    /// Delegate for resolving types by simple name.
    /// Set by Jalium.UI.Xaml's ModuleInitializer to point to XamlTypeRegistry.GetType.
    /// </summary>
    internal static Func<string, Type?>? ResolveTypeByName { get; set; }
}
