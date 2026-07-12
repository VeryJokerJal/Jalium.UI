namespace Jalium.UI.Diagnostics;

/// <summary>
/// Exposes resource-dictionary source, ownership, and load/resolve diagnostics.
/// </summary>
public static class ResourceDictionaryDiagnostics
{
    public static IEnumerable<ResourceDictionaryInfo> ThemedResourceDictionaries =>
        ResourceDictionaryDiagnosticsStore.GetSystemDictionaries(
            ResourceDictionaryRegistrationKind.Themed);

    public static IEnumerable<ResourceDictionaryInfo> GenericResourceDictionaries =>
        ResourceDictionaryDiagnosticsStore.GetSystemDictionaries(
            ResourceDictionaryRegistrationKind.Generic);

    public static event EventHandler<ResourceDictionaryLoadedEventArgs>? ThemedResourceDictionaryLoaded
    {
        add => ResourceDictionaryDiagnosticsStore.ThemedResourceDictionaryLoaded += value;
        remove => ResourceDictionaryDiagnosticsStore.ThemedResourceDictionaryLoaded -= value;
    }

    public static event EventHandler<ResourceDictionaryUnloadedEventArgs>? ThemedResourceDictionaryUnloaded
    {
        add => ResourceDictionaryDiagnosticsStore.ThemedResourceDictionaryUnloaded += value;
        remove => ResourceDictionaryDiagnosticsStore.ThemedResourceDictionaryUnloaded -= value;
    }

    public static event EventHandler<ResourceDictionaryLoadedEventArgs>? GenericResourceDictionaryLoaded
    {
        add => ResourceDictionaryDiagnosticsStore.GenericResourceDictionaryLoaded += value;
        remove => ResourceDictionaryDiagnosticsStore.GenericResourceDictionaryLoaded -= value;
    }

    public static event EventHandler<StaticResourceResolvedEventArgs>? StaticResourceResolved
    {
        add => ResourceDictionaryDiagnosticsStore.StaticResourceResolved += value;
        remove => ResourceDictionaryDiagnosticsStore.StaticResourceResolved -= value;
    }

    public static IEnumerable<ResourceDictionary> GetResourceDictionariesForSource(Uri uri) =>
        ResourceDictionaryDiagnosticsStore.GetDictionariesForSource(uri);

    public static IEnumerable<FrameworkElement> GetFrameworkElementOwners(
        ResourceDictionary dictionary) =>
        ResourceDictionaryDiagnosticsStore
            .GetOwners(dictionary, ResourceDictionaryOwnerKind.FrameworkElement)
            .OfType<FrameworkElement>()
            .ToArray();

    public static IEnumerable<FrameworkContentElement> GetFrameworkContentElementOwners(
        ResourceDictionary dictionary) =>
        ResourceDictionaryDiagnosticsStore
            .GetOwners(dictionary, ResourceDictionaryOwnerKind.FrameworkContentElement)
            .OfType<FrameworkContentElement>()
            .ToArray();

    public static IEnumerable<Application> GetApplicationOwners(ResourceDictionary dictionary) =>
        ResourceDictionaryDiagnosticsStore
            .GetOwners(dictionary, ResourceDictionaryOwnerKind.Application)
            .OfType<Application>()
            .ToArray();

    internal static void RegisterGenericResourceDictionary(ResourceDictionary dictionary) =>
        ResourceDictionaryDiagnosticsStore.RegisterSystemDictionary(
            dictionary,
            ResourceDictionaryRegistrationKind.Generic);

    internal static void UnregisterGenericResourceDictionary(ResourceDictionary dictionary) =>
        ResourceDictionaryDiagnosticsStore.UnregisterSystemDictionary(
            dictionary,
            ResourceDictionaryRegistrationKind.Generic);

    internal static void RegisterThemedResourceDictionary(ResourceDictionary dictionary) =>
        ResourceDictionaryDiagnosticsStore.RegisterSystemDictionary(
            dictionary,
            ResourceDictionaryRegistrationKind.Themed);

    internal static void UnregisterThemedResourceDictionary(ResourceDictionary dictionary) =>
        ResourceDictionaryDiagnosticsStore.UnregisterSystemDictionary(
            dictionary,
            ResourceDictionaryRegistrationKind.Themed);
}
