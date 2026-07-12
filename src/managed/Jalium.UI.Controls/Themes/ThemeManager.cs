using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Themes;

/// <summary>
/// Theme variants supported by Jalium brand theme.
/// </summary>
public enum ThemeVariant
{
    Dark,
    Light
}

/// <summary>
/// Brand theme options used for one-shot runtime theme application.
/// </summary>
public sealed class BrandThemeOptions
{
    public ThemeVariant Theme { get; init; } = ThemeVariant.Dark;

    public Color AccentColor { get; init; } = ThemeManager.DefaultPrimaryAccentColor;

    public string? DisplayFontFamily { get; init; }

    public string? BodyFontFamily { get; init; }

    public string? MonoFontFamily { get; init; }
}

/// <summary>
/// Manages theme initialization and runtime brand theme application.
/// </summary>
public static class ThemeManager
{
    private const string ThemeRefreshVersionKey = "__ThemeManager.Version";
    private const string XamlAssemblyName = "Jalium.UI.Xaml";
    private const string ThemeLoaderTypeName = "Jalium.UI.Markup.ThemeLoader";
    private const string DesktopAssemblyName = "Jalium.UI.Desktop";
    private const string DesktopBootstrapTypeName = "Jalium.UI.Desktop.DesktopBootstrap";

    private static bool _initialized;
    private static int _themeVersion;
    private static Application? _application;
    private static ResourceDictionary? _genericThemeDictionary;
    private static ResourceDictionary? _accentDictionary;
    private static ResourceDictionary? _typographyDictionary;
    private static bool _suppressRefresh;

    /// <summary>
    /// Default brand primary accent (forest emerald, midpoint of the
    /// #207245 -> #1C8043 gradient used for AccentBrush).
    /// </summary>
    public static readonly Color DefaultPrimaryAccentColor = Color.FromRgb(0x1E, 0x79, 0x3F);

    /// <summary>
    /// Default brand secondary accent (deep teal-green).
    /// </summary>
    public static readonly Color DefaultSecondaryAccentColor = Color.FromRgb(0x14, 0x5A, 0x33);

    /// <summary>
    /// The resource name of the Generic theme file.
    /// </summary>
    public const string GenericThemeResourceName = "Jalium.UI.Controls.Themes.Generic.jalxaml";

    /// <summary>
    /// Delegate for loading XAML content from a stream.
    /// Set by the Jalium.UI.Xaml assembly via ModuleInitializer to avoid circular dependency.
    /// </summary>
    public static Func<Stream, string, Assembly, ResourceDictionary?>? XamlLoader { get; set; }

    /// <summary>
    /// Optional platform-specific resolver for the system accent color.
    /// Set by a platform integration package (e.g. <c>Jalium.UI.Desktop</c>'s
    /// <c>ModuleInitializer</c> on Windows reads
    /// <c>HKCU\SOFTWARE\Microsoft\Windows\DWM\AccentColor</c>).
    /// When non-null and returning a value, its result seeds
    /// <see cref="CurrentAccentColor"/> during <see cref="Initialize"/>
    /// instead of <see cref="DefaultPrimaryAccentColor"/>, so every
    /// <c>{ThemeResource SystemAccentColor}</c> binding (and every brush
    /// derived from it) picks up the OS accent automatically.
    /// On platforms that don't ship a resolver the framework default brand
    /// emerald is preserved.
    /// </summary>
    public static Func<Color?>? SystemAccentResolver { get; set; }

    /// <summary>
    /// Gets the currently active theme variant.
    /// </summary>
    public static ThemeVariant CurrentTheme { get; private set; } = ThemeVariant.Dark;

    /// <summary>
    /// Gets the current primary accent color.
    /// </summary>
    public static Color CurrentAccentColor { get; private set; } = DefaultPrimaryAccentColor;

    /// <summary>
    /// Gets the current display font family.
    /// </summary>
    public static string CurrentDisplayFontFamily { get; private set; } = "Segoe UI";

    /// <summary>
    /// Gets the current body font family.
    /// </summary>
    public static string CurrentBodyFontFamily { get; private set; } = "Segoe UI";

    /// <summary>
    /// Gets the current monospace font family.
    /// </summary>
    public static string CurrentMonospaceFontFamily { get; private set; } = "Cascadia Code";

    /// <summary>
    /// Gets the current body font size used by controls.
    /// Initialized from the system message font size.
    /// </summary>
    public static double CurrentBodyFontSize { get; private set; } = FrameworkElement.DefaultFontSize;

    /// <summary>
    /// Initializes the default theme for the application.
    /// Call this method once at application startup.
    /// </summary>
    /// <param name="app">The application instance.</param>
    /// <remarks>
    /// Reflective lookups inside the helper methods (<see cref="EnsureXamlLoaderRegistered"/>,
    /// <see cref="EnsurePlatformIntegrationLoaded"/>, <see cref="TryRegisterTypeResolver"/>) are
    /// covered by <see cref="DynamicDependencyAttribute"/> which preserves the targeted types
    /// for the trimmer.
    /// </remarks>
    [SuppressMessage("Trimming", "IL2026:Public bootstrap cannot itself declare RequiresUnreferencedCode (consumers' Application subclass ctors would need it). Reflective sub-routines are protected by DynamicDependency.", Justification = ".NET AOT bootstrap pattern.")]
    public static void Initialize(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var previousApplication = _application;
        _application = app;
        SyncThemeFromCurrentThemeKey();
        ResourceDictionary.CurrentThemeKey = CurrentTheme.ToString();

        if (_initialized)
        {
            // No theme state changed. Repeated initialization of the current app is a
            // no-op; when a host starts a replacement Application in the same process,
            // move the already-built dictionaries instead of sweeping every live
            // DynamicResource binding.
            if (!ReferenceEquals(previousApplication, app))
            {
                DetachManagedDictionaries(previousApplication);
            }

            EnsureManagedDictionariesAttached(app);
            return;
        }

        EnsureXamlLoaderRegistered();

        // Loading Jalium.UI.Xaml may re-enter ThemeManager.Initialize via ThemeLoader.Initialize().
        // If that already completed initialization, stop here to avoid duplicate dictionary insertion.
        if (_initialized)
        {
            EnsureManagedDictionariesAttached(app);
            return;
        }

        if (XamlLoader == null)
        {
            // XamlLoader not registered yet - Jalium.UI.Xaml module initializer hasn't run.
            // This will be retried when the Xaml assembly is first accessed.
            return;
        }

        // Give a platform integration package (e.g. Jalium.UI.Desktop on
        // Windows) a chance to register a SystemAccentResolver before we
        // build the accent dictionary.
        EnsurePlatformIntegrationLoaded();

        var platformAccent = TryGetPlatformAccent();
        if (platformAccent.HasValue)
        {
            CurrentAccentColor = platformAccent.Value;
        }

        _genericThemeDictionary = LoadGenericTheme();
        if (_genericThemeDictionary != null)
        {
            Jalium.UI.Diagnostics.ResourceDictionaryDiagnostics.RegisterGenericResourceDictionary(
                _genericThemeDictionary);
            app.Resources.MergedDictionaries.Add(_genericThemeDictionary);
        }

        _accentDictionary = BuildAccentDictionary(CurrentAccentColor);

        _typographyDictionary = BuildTypographyDictionary(CurrentDisplayFontFamily, CurrentBodyFontFamily, CurrentMonospaceFontFamily, CurrentBodyFontSize);

        app.Resources.MergedDictionaries.Add(_accentDictionary);
        app.Resources.MergedDictionaries.Add(_typographyDictionary);

        _initialized = true;
        ForceThemeRefresh();
    }

    /// <summary>
    /// Invokes <see cref="SystemAccentResolver"/> defensively so a buggy
    /// platform package can never abort the framework's theme initialization.
    /// </summary>
    private static Color? TryGetPlatformAccent()
    {
        if (SystemAccentResolver == null)
            return null;

        try
        {
            return SystemAccentResolver.Invoke();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies a theme variant at runtime.
    /// </summary>
    public static void ApplyTheme(ThemeVariant theme)
    {
        CurrentTheme = theme;
        ResourceDictionary.CurrentThemeKey = theme.ToString();

        if (_application != null)
        {
            var refreshedGeneric = LoadGenericTheme();
            if (refreshedGeneric != null)
            {
                if (_genericThemeDictionary != null)
                {
                    Jalium.UI.Diagnostics.ResourceDictionaryDiagnostics.UnregisterGenericResourceDictionary(
                        _genericThemeDictionary);
                }

                Jalium.UI.Diagnostics.ResourceDictionaryDiagnostics.RegisterGenericResourceDictionary(
                    refreshedGeneric);
                ReplaceManagedDictionary(ref _genericThemeDictionary, refreshedGeneric);
            }

            // Accent derived resources depend on current theme (notably disabled variants).
            ReplaceManagedDictionary(ref _accentDictionary, BuildAccentDictionary(CurrentAccentColor));
        }

        ForceThemeRefresh();
    }

    /// <summary>
    /// Applies a runtime accent color and regenerates derived accent tokens.
    /// </summary>
    public static void ApplyAccent(Color accent)
    {
        CurrentAccentColor = accent;

        if (_application == null)
            return;

        ReplaceManagedDictionary(ref _accentDictionary, BuildAccentDictionary(accent));
        ForceThemeRefresh();
    }

    /// <summary>
    /// Applies runtime typography tokens.
    /// </summary>
    public static void ApplyTypography(string display, string body, string mono)
    {
        ApplyTypography(display, body, mono, CurrentBodyFontSize);
    }

    /// <summary>
    /// Applies runtime typography tokens including font size.
    /// </summary>
    public static void ApplyTypography(string display, string body, string mono, double bodyFontSize)
    {
        CurrentDisplayFontFamily = NormalizeFontFamily(display, "Segoe UI");
        CurrentBodyFontFamily = NormalizeFontFamily(body, "Segoe UI");
        CurrentMonospaceFontFamily = NormalizeFontFamily(mono, "Cascadia Code");
        CurrentBodyFontSize = bodyFontSize > 0 ? bodyFontSize : FrameworkElement.DefaultFontSize;

        if (_application == null)
            return;

        ReplaceManagedDictionary(
            ref _typographyDictionary,
            BuildTypographyDictionary(CurrentDisplayFontFamily, CurrentBodyFontFamily, CurrentMonospaceFontFamily, CurrentBodyFontSize));

        ForceThemeRefresh();
    }

    /// <summary>
    /// Applies brand theme in one call (theme + accent + typography).
    /// </summary>
    public static void ApplyBrandTheme(BrandThemeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _suppressRefresh = true;
        try
        {
            ApplyTheme(options.Theme);
            ApplyAccent(options.AccentColor);
            ApplyTypography(
                options.DisplayFontFamily ?? CurrentDisplayFontFamily,
                options.BodyFontFamily ?? CurrentBodyFontFamily,
                options.MonoFontFamily ?? CurrentMonospaceFontFamily);
        }
        finally
        {
            _suppressRefresh = false;
        }

        ForceThemeRefresh();
    }

    /// <summary>
    /// Loads the Generic theme using the registered XamlLoader callback.
    /// This avoids compile-time dependency on the Xaml project (AOT-safe).
    ///
    /// <para>
    /// Two paths are supported, in order of preference:
    /// <list type="number">
    /// <item>The SourceGenerator emitted a prebuilt-dictionary builder for Generic.jalxaml
    /// — the XamlLoader callback's prebuilt registry lookup hits and the manifest
    /// resource is irrelevant. This is the default after the no-embed switch.</item>
    /// <item>Legacy fallback: the .jalxaml file is still embedded as a manifest resource
    /// (for projects that opted into <c>EmbedJalxamlSources=true</c> for tooling).</item>
    /// </list>
    /// When neither is available the caller logs a diagnostic and theming silently
    /// falls back to default control rendering. We deliberately pass the canonical
    /// manifest resource name to XamlLoader so its prebuilt-dictionary lookup can match
    /// what the SG registered — registry keys are the canonical
    /// <c>{AssemblyName}.{path-with-dots}.jalxaml</c> form.
    /// </para>
    /// </summary>
    /// <summary>
    /// Loads the Generic theme dictionary, populating it with control templates and styles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We pass the slash-form path to <see cref="XamlLoader"/>. The Xaml-side
    /// <c>ThemeLoader</c> turns it into the canonical prebuilt-registry key
    /// (<c>{AssemblyName}.{dotted-path}</c>) when looking up the SG-emitted builder, but
    /// uses the slash form as the dictionary's <c>BaseUri</c>. The latter is mandatory:
    /// <c>&lt;ResourceDictionary Source="Controls/Button.jalxaml" /&gt;</c> children inside
    /// Generic.jalxaml resolve as path-relative URIs, and only the slash form preserves
    /// the <c>Themes/</c> segment when relative resolution strips the trailing filename.
    /// Passing the dotted form here makes the entire dotted name look like a single
    /// filename to the URI parser, dropping <c>Themes/</c> and breaking every nested
    /// MergedDictionary lookup.
    /// </para>
    /// </remarks>
    private static ResourceDictionary? LoadGenericTheme()
    {
        if (XamlLoader == null)
        {
            return null;
        }

        const string slashPath = "Themes/Generic.jalxaml";
        using var emptyStream = new MemoryStream();
        return XamlLoader(emptyStream, slashPath, ControlsAssembly);
    }

    /// <summary>
    /// Returns the embedded-resource stream for the Generic theme, when one exists.
    /// </summary>
    /// <remarks>
    /// Jalium.UI.Controls no longer embeds the framework theme dictionaries as manifest
    /// resources — the SourceGenerator emits compile-time builders that the runtime loads
    /// via <see cref="XamlPrebuiltDictionaryRegistry"/>. This API is retained for callers
    /// that may want to consume the (third-party) embedded variant of the framework
    /// theme; it now always returns <c>null</c> for the in-box assembly. Use
    /// <see cref="LoadGenericTheme"/> to obtain a fully-built dictionary instead.
    /// </remarks>
    public static Stream? GetGenericThemeStream()
    {
        // Kept as a public API for backward compatibility. With the SG-emitted prebuilt
        // registry replacing the manifest-resource pipeline, the in-box framework
        // dictionaries are no longer accessible as a stream — callers must go through
        // LoadGenericTheme / XamlLoader. Returning null here matches the "stream not
        // present" contract this API has always documented.
        return null;
    }

    /// <summary>
    /// Gets the Controls assembly for theme resource loading.
    /// </summary>
    public static Assembly ControlsAssembly => typeof(ThemeManager).Assembly;

    /// <summary>
    /// Gets a value indicating whether the theme has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Resets the theme system, allowing re-initialization.
    /// Primarily for testing purposes.
    /// </summary>
    internal static void Reset()
    {
        DynamicResourceBindingOperations.ResetRegistryForTesting();

        if (_genericThemeDictionary != null)
        {
            Jalium.UI.Diagnostics.ResourceDictionaryDiagnostics.UnregisterGenericResourceDictionary(
                _genericThemeDictionary);
        }

        _initialized = false;
        _application = null;
        _genericThemeDictionary = null;
        _accentDictionary = null;
        _typographyDictionary = null;
        _themeVersion = 0;
        _suppressRefresh = false;

        CurrentTheme = ThemeVariant.Dark;
        CurrentAccentColor = DefaultPrimaryAccentColor;
        CurrentDisplayFontFamily = "Segoe UI";
        CurrentBodyFontFamily = "Segoe UI";
        CurrentMonospaceFontFamily = "Cascadia Code";
        CurrentBodyFontSize = FrameworkElement.DefaultFontSize;

        ResourceDictionary.CurrentThemeKey = null;
    }

    private static void ReplaceManagedDictionary(ref ResourceDictionary? current, ResourceDictionary replacement)
    {
        if (_application == null)
        {
            current = replacement;
            return;
        }

        var dictionaries = _application.Resources.MergedDictionaries;
        var index = current == null ? -1 : dictionaries.IndexOf(current);

        if (index >= 0)
        {
            dictionaries[index] = replacement;
        }
        else
        {
            dictionaries.Add(replacement);
        }

        current = replacement;
    }

    private static void EnsureManagedDictionariesAttached(Application app)
    {
        var dictionaries = app.Resources.MergedDictionaries;
        using var notifications = app.Resources.DeferNotifications();

        AddIfMissing(_genericThemeDictionary);
        AddIfMissing(_accentDictionary);
        AddIfMissing(_typographyDictionary);

        void AddIfMissing(ResourceDictionary? dictionary)
        {
            if (dictionary != null && !dictionaries.Contains(dictionary))
            {
                dictionaries.Add(dictionary);
            }
        }
    }

    private static void DetachManagedDictionaries(Application? app)
    {
        if (app == null)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        using var notifications = app.Resources.DeferNotifications();
        RemoveIfPresent(_genericThemeDictionary);
        RemoveIfPresent(_accentDictionary);
        RemoveIfPresent(_typographyDictionary);

        void RemoveIfPresent(ResourceDictionary? dictionary)
        {
            if (dictionary != null)
            {
                dictionaries.Remove(dictionary);
            }
        }
    }

    private static void ForceThemeRefresh()
    {
        if (_application == null || _suppressRefresh)
            return;

        DynamicResourceBindingOperations.RefreshAll();

        _themeVersion++;
        _application.Resources[ThemeRefreshVersionKey] = _themeVersion;
    }

    private static ResourceDictionary BuildAccentDictionary(Color accent)
    {
        var hover = Blend(accent, Color.White, 0.18);
        var pressed = Blend(accent, Color.Black, 0.24);
        var light1 = Blend(accent, Color.White, 0.18);
        var light2 = Blend(accent, Color.White, 0.34);
        var light3 = Blend(accent, Color.White, 0.52);
        var dark1 = Blend(accent, Color.Black, 0.12);
        var dark2 = Blend(accent, Color.Black, 0.24);
        var dark3 = Blend(accent, Color.Black, 0.36);

        // Every AccentBrush flavor ships as a top-to-bottom gradient so the
        // default palette yields the #207245 -> #1C8043 look (and custom
        // accents automatically get a subtle two-stop gradient in the same
        // style). The start stop is the accent darkened ~5% and the end stop
        // is the accent lightened ~5% so the midpoint matches `accent`.
        static LinearGradientBrush Gradient(Color color)
        {
            var start = Blend(color, Color.Black, 0.06);
            var end = Blend(color, Color.White, 0.06);
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
            };
            brush.GradientStops.Add(new GradientStop(start, 0));
            brush.GradientStops.Add(new GradientStop(end, 1));
            return brush;
        }
        var disabledBlendTarget = CurrentTheme == ThemeVariant.Dark
            ? Color.FromRgb(0x66, 0x66, 0x66)
            : Color.FromRgb(0xB8, 0xB8, 0xB8);
        var disabled = Blend(accent, disabledBlendTarget, 0.58);
        var selection = Color.FromArgb(0x99, accent.R, accent.G, accent.B);
        var weakSelection = Color.FromArgb(0x4D, accent.R, accent.G, accent.B);
        var accentFillDefault = CurrentTheme == ThemeVariant.Dark ? light2 : dark1;
        var accentFillSecondary = Color.FromArgb(0xE6, accentFillDefault.R, accentFillDefault.G, accentFillDefault.B);
        var accentFillTertiary = Color.FromArgb(0xCC, accentFillDefault.R, accentFillDefault.G, accentFillDefault.B);
        var accentTextPrimary = CurrentTheme == ThemeVariant.Dark ? light3 : dark2;
        var accentTextSecondary = CurrentTheme == ThemeVariant.Dark ? light3 : dark3;
        var accentTextTertiary = CurrentTheme == ThemeVariant.Dark ? light2 : dark1;
        var systemFillAttention = CurrentTheme == ThemeVariant.Dark ? light2 : accent;

        var dictionary = new ResourceDictionary
        {
            ["SystemAccentColor"] = accent,
            ["SystemAccentColorLight1"] = light1,
            ["SystemAccentColorLight2"] = light2,
            ["SystemAccentColorLight3"] = light3,
            ["SystemAccentColorDark1"] = dark1,
            ["SystemAccentColorDark2"] = dark2,
            ["SystemAccentColorDark3"] = dark3,
            ["AccentTextFillColorPrimary"] = accentTextPrimary,
            ["AccentTextFillColorSecondary"] = accentTextSecondary,
            ["AccentTextFillColorTertiary"] = accentTextTertiary,
            ["AccentTextFillColorDisabled"] = disabled,
            ["AccentFillColorDefault"] = accentFillDefault,
            ["AccentFillColorSecondary"] = accentFillSecondary,
            ["AccentFillColorTertiary"] = accentFillTertiary,
            ["AccentFillColorDisabled"] = disabled,
            ["AccentFillColorSelectedTextBackground"] = accent,
            ["SystemFillColorAttention"] = systemFillAttention,
            ["AccentBrush"] = Gradient(accent),
            ["AccentBrushHover"] = Gradient(hover),
            ["AccentBrushPressed"] = Gradient(pressed),
            ["AccentBrushDisabled"] = Gradient(disabled),
            ["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(accentTextPrimary),
            ["AccentTextFillColorSecondaryBrush"] = new SolidColorBrush(accentTextSecondary),
            ["AccentTextFillColorTertiaryBrush"] = new SolidColorBrush(accentTextTertiary),
            ["AccentTextFillColorDisabledBrush"] = new SolidColorBrush(disabled),
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(accentFillDefault),
            ["AccentFillColorSecondaryBrush"] = new SolidColorBrush(accentFillSecondary),
            ["AccentFillColorTertiaryBrush"] = new SolidColorBrush(accentFillTertiary),
            ["AccentFillColorDisabledBrush"] = new SolidColorBrush(disabled),
            ["AccentFillColorSelectedTextBackgroundBrush"] = new SolidColorBrush(accent),
            ["SystemFillColorAttentionBrush"] = new SolidColorBrush(systemFillAttention),
            ["SelectionBackground"] = new SolidColorBrush(selection),
            ["SelectionBackgroundWeak"] = new SolidColorBrush(weakSelection),
            ["AppBarButtonForeground"] = new SolidColorBrush(accent),
            ["AppBarButtonForegroundDisabled"] = new SolidColorBrush(disabled),
            ["ProgressRingForeground"] = new SolidColorBrush(accent),
            ["BrandPrimaryAccentBrush"] = new SolidColorBrush(accent),
            ["BrandSecondaryAccentBrush"] = new SolidColorBrush(DefaultSecondaryAccentColor),
            ["BrandPrimaryAccentColor"] = accent,
            ["BrandSecondaryAccentColor"] = DefaultSecondaryAccentColor
        };

        return dictionary;
    }

    private static ResourceDictionary BuildTypographyDictionary(string display, string body, string mono, double bodyFontSize)
    {
        return new ResourceDictionary
        {
            ["DisplayFontFamily"] = new FontFamily(display),
            ["BodyFontFamily"] = new FontFamily(body),
            ["MonoFontFamily"] = new FontFamily(mono),
            ["BodyFontSize"] = bodyFontSize,
            ["CaptionFontSize"] = Math.Max(bodyFontSize - 2, 8.0),
            ["SmallFontSize"] = Math.Max(bodyFontSize - 4, 6.0)
        };
    }

    private static string NormalizeFontFamily(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    /// <summary>
    /// Best-effort load + bootstrap of an optional platform integration
    /// package (e.g. <c>Jalium.UI.Desktop</c> on Windows). Mirrors the
    /// existing <see cref="EnsureXamlLoaderRegistered"/> pattern: the
    /// package exposes a <c>public static Initialize()</c> method on a
    /// well-known type, and we reflectively invoke it after loading the
    /// assembly. This avoids depending on <c>[ModuleInitializer]</c>
    /// being eagerly fired by the runtime — explicit invocation is
    /// deterministic across all hosts (Debug/Release, AOT, hot-reload).
    /// Failure is silent — the framework simply keeps its defaults.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods,
        DesktopBootstrapTypeName, DesktopAssemblyName)]
    [RequiresUnreferencedCode("Reflectively resolves the desktop bootstrap type and its public Initialize() method via Assembly.GetType. The type is preserved by the DynamicDependency above when the assembly is present.")]
    private static void EnsurePlatformIntegrationLoaded()
    {
        if (SystemAccentResolver != null)
            return;

        Assembly? desktopAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, DesktopAssemblyName, StringComparison.Ordinal));

        if (desktopAssembly == null)
        {
            try
            {
                desktopAssembly = Assembly.Load(new AssemblyName(DesktopAssemblyName));
            }
            catch
            {
                // Platform package isn't in the deployment — fine, we keep defaults.
                return;
            }
        }

        try
        {
            var bootstrapType = desktopAssembly.GetType(DesktopBootstrapTypeName, throwOnError: false);
            var initializeMethod = bootstrapType?.GetMethod(
                "Initialize",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            initializeMethod?.Invoke(null, null);
        }
        catch
        {
            // Bootstrap is best-effort. If it throws we fall back to defaults
            // rather than aborting application startup.
        }
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods,
        ThemeLoaderTypeName, XamlAssemblyName)]
    [RequiresUnreferencedCode("Reflectively resolves ThemeLoader and its Initialize/LoadResourceDictionaryFromStream/LoadStartupObjectFromUri methods via Assembly.GetType. The type is preserved by the DynamicDependency above.")]
    private static void EnsureXamlLoaderRegistered()
    {
        if (XamlLoader != null)
        {
            return;
        }

        try
        {
            var xamlAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, XamlAssemblyName, StringComparison.Ordinal))
                ?? Assembly.Load(new AssemblyName(XamlAssemblyName));

            var themeLoaderType = xamlAssembly.GetType(ThemeLoaderTypeName, throwOnError: false);
            var initializeMethod = themeLoaderType?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            initializeMethod?.Invoke(null, null);

            if (XamlLoader == null && themeLoaderType != null)
            {
                var loadMethod = themeLoaderType.GetMethod(
                    "LoadResourceDictionaryFromStream",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (loadMethod != null)
                {
                    XamlLoader = loadMethod.CreateDelegate<Func<Stream, string, Assembly, ResourceDictionary?>>();
                }

                var startupLoaderMethod = themeLoaderType.GetMethod(
                    "LoadStartupObjectFromUri",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (Application.StartupObjectLoader == null && startupLoaderMethod != null)
                {
                    Application.StartupObjectLoader =
                        startupLoaderMethod.CreateDelegate<Func<Application, Uri, object?>>();
                }
            }

            TryRegisterTypeResolver(xamlAssembly);
        }
        catch (Exception)
        {
        }
    }

    private static void SyncThemeFromCurrentThemeKey()
    {
        if (ResourceDictionary.CurrentThemeKey is not string themeKey)
            return;

        if (!Enum.TryParse<ThemeVariant>(themeKey, ignoreCase: true, out var variant))
            return;

        CurrentTheme = variant;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicProperties, typeof(global::Jalium.UI.TypeResolver))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, "Jalium.UI.Markup.XamlTypeRegistry", XamlAssemblyName)]
    [RequiresUnreferencedCode("Reflectively resolves TypeResolver and XamlTypeRegistry members via Assembly.GetType. Both types are preserved by the DynamicDependency above.")]
    private static void TryRegisterTypeResolver(Assembly xamlAssembly)
    {
        try
        {
            var typeResolverType = typeof(Application).Assembly.GetType("Jalium.UI.TypeResolver", throwOnError: false);
            var resolveTypeByNameProperty = typeResolverType?.GetProperty(
                "ResolveTypeByName",
                BindingFlags.NonPublic | BindingFlags.Static);
            var xamlTypeRegistryType = xamlAssembly.GetType("Jalium.UI.Markup.XamlTypeRegistry", throwOnError: false);
            var getTypeMethod = xamlTypeRegistryType?.GetMethod("GetType", BindingFlags.Public | BindingFlags.Static);

            if (resolveTypeByNameProperty == null || getTypeMethod == null)
                return;

            var existingResolver = resolveTypeByNameProperty.GetValue(null);
            if (existingResolver != null)
                return;

            var resolver = getTypeMethod.CreateDelegate<Func<string, Type?>>();
            resolveTypeByNameProperty.SetValue(null, resolver);
        }
        catch (Exception)
        {
        }
    }

    private static Color Blend(Color color, Color target, double factor)
    {
        factor = Math.Clamp(factor, 0.0, 1.0);

        static byte Lerp(byte from, byte to, double t)
        {
            return (byte)Math.Clamp((int)Math.Round(from + ((to - from) * t)), 0, 255);
        }

        return Color.FromArgb(
            Lerp(color.A, target.A, factor),
            Lerp(color.R, target.R, factor),
            Lerp(color.G, target.G, factor),
            Lerp(color.B, target.B, factor));
    }
}
