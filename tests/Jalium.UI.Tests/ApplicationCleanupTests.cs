using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ApplicationCleanupTests
{
    private static readonly MethodInfo s_cleanupMethod =
        typeof(Application).GetMethod(
            "Cleanup",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Application.Cleanup was not found.");

    private static readonly FieldInfo s_currentField =
        typeof(Application).GetField(
            "_current",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Application._current was not found.");

    private static readonly FieldInfo s_themeApplicationField =
        typeof(ThemeManager).GetField(
            "_application",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ThemeManager._application was not found.");

    private static readonly FieldInfo s_genericThemeDictionaryField = GetThemeDictionaryField(
        "_genericThemeDictionary");
    private static readonly FieldInfo s_accentDictionaryField = GetThemeDictionaryField(
        "_accentDictionary");
    private static readonly FieldInfo s_typographyDictionaryField = GetThemeDictionaryField(
        "_typographyDictionary");

    private static readonly FieldInfo s_resourceLookupCacheField = GetStaticField(
        typeof(ResourceLookup),
        "t_resourceCache");
    private static readonly FieldInfo s_mergedLookupCacheField = GetStaticField(
        typeof(ResourceDictionary),
        "t_mergedLookupCache");
    private static readonly FieldInfo s_scopelessResourceCacheField = GetStaticField(
        typeof(FrameworkElement),
        "t_scopelessResourceCache");

    private static readonly MethodInfo s_lookupResourceForImplicitStyleMethod =
        typeof(FrameworkElement).GetMethod(
            "LookupResourceForImplicitStyle",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "FrameworkElement.LookupResourceForImplicitStyle was not found.");

    [Fact]
    public void Cleanup_ReleasesOldApplicationWindowAndThreadCaches()
    {
        ResetApplicationState();
        Application? first = null;
        Application? replacement = null;

        try
        {
            first = new Application();
            var firstWindow = new Window();
            first.MainWindow = firstWindow;

            var resourceKey = new object();
            var resourceValue = new object();
            var userDictionary = new ResourceDictionary
            {
                [resourceKey] = resourceValue,
            };
            first.Resources.MergedDictionaries.Add(userDictionary);

            var element = new Border();
            Assert.Same(resourceValue, ResourceLookup.FindResource(element, resourceKey));
            Assert.Same(
                resourceValue,
                s_lookupResourceForImplicitStyleMethod.Invoke(
                    element,
                    [resourceKey, true]));

            Assert.NotNull(s_resourceLookupCacheField.GetValue(null));
            Assert.NotNull(s_mergedLookupCacheField.GetValue(null));
            Assert.NotNull(s_scopelessResourceCacheField.GetValue(null));

            var managedDictionaries = GetManagedThemeDictionaries();
            Assert.NotEmpty(managedDictionaries);
            Assert.All(
                managedDictionaries,
                dictionary => Assert.Contains(
                    dictionary,
                    first.Resources.MergedDictionaries));

            Cleanup(first);

            Assert.Null(Application.Current);
            Assert.Null(first.MainWindow);
            Assert.Null(s_themeApplicationField.GetValue(null));
            Assert.Null(ResourceLookup.ApplicationResourceLookup);
            Assert.Null(ResourceLookup.ApplicationResourceLookupWithSource);
            Assert.Null(ResourceLookup.AncestorRedirectLookup);
            Assert.Null(s_resourceLookupCacheField.GetValue(null));
            Assert.Null(s_mergedLookupCacheField.GetValue(null));
            Assert.Null(s_scopelessResourceCacheField.GetValue(null));

            Assert.All(
                managedDictionaries,
                dictionary => Assert.DoesNotContain(
                    dictionary,
                    first.Resources.MergedDictionaries));

            replacement = new Application();

            Assert.Same(replacement, Application.Current);
            Assert.Same(replacement, s_themeApplicationField.GetValue(null));
            Assert.NotNull(ResourceLookup.ApplicationResourceLookup);
            Assert.NotNull(ResourceLookup.ApplicationResourceLookupWithSource);
            Assert.NotNull(ResourceLookup.AncestorRedirectLookup);
            Assert.All(
                managedDictionaries,
                dictionary => Assert.Contains(
                    dictionary,
                    replacement.Resources.MergedDictionaries));

            Cleanup(replacement);
            replacement = null;
            Assert.Null(Application.Current);
        }
        finally
        {
            if (replacement != null && ReferenceEquals(Application.Current, replacement))
            {
                Cleanup(replacement);
            }

            if (first != null && ReferenceEquals(Application.Current, first))
            {
                Cleanup(first);
            }

            ResetApplicationState();
        }
    }

    [Fact]
    public void DerivedApplication_StillInitializesPrivateXamlResourcesExactlyOnce()
    {
        ResetApplicationState();
        InitializedApplication? application = null;
        try
        {
            application = new InitializedApplication();
            Assert.Equal(1, application.InitializationCount);
            Assert.Equal("initialized", application.Resources["InitializerResult"]);
        }
        finally
        {
            if (application != null)
                Cleanup(application);
            ResetApplicationState();
        }
    }

    private sealed class InitializedApplication : Application
    {
        public int InitializationCount { get; private set; }

        private void InitializeComponent()
        {
            InitializationCount++;
            Resources["InitializerResult"] = "initialized";
        }
    }

    private static IReadOnlyList<ResourceDictionary> GetManagedThemeDictionaries()
    {
        return new[]
            {
                s_genericThemeDictionaryField.GetValue(null),
                s_accentDictionaryField.GetValue(null),
                s_typographyDictionaryField.GetValue(null),
            }
            .OfType<ResourceDictionary>()
            .ToArray();
    }

    private static void Cleanup(Application application)
        => s_cleanupMethod.Invoke(application, null);

    private static void ResetApplicationState()
    {
        s_currentField.SetValue(null, null);
        ThemeManager.Reset();
        ResourceLookup.ApplicationResourceLookup = null;
        ResourceLookup.ApplicationResourceLookupWithSource = null;
        ResourceLookup.AncestorRedirectLookup = null;
        ResourceLookup.ClearThreadCache();
        ResourceDictionary.ClearThreadCache();
        FrameworkElement.ClearScopelessResourceThreadCache();
    }

    private static FieldInfo GetThemeDictionaryField(string name)
        => GetStaticField(typeof(ThemeManager), name);

    private static FieldInfo GetStaticField(Type type, string name)
        => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{type.Name}.{name} was not found.");
}
