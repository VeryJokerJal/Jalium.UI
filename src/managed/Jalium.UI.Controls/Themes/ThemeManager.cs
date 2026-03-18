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

    private static bool _initialized;
    private static int _themeVersion;
    private static Application? _application;
    private static ResourceDictionary? _genericThemeDictionary;
    private static ResourceDictionary? _accentDictionary;
    private static ResourceDictionary? _typographyDictionary;
    private static bool _suppressRefresh;

    /// <summary>
    /// Default brand primary accent (purple).
    /// </summary>
    public static readonly Color DefaultPrimaryAccentColor = Color.FromRgb(0x7C, 0x4D, 0xFF);

    /// <summary>
    /// Default brand secondary accent (orange).
    /// </summary>
    public static readonly Color DefaultSecondaryAccentColor = Color.FromRgb(0xFF, 0x8A, 0x00);

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
    /// Initializes the default theme for the application.
    /// Call this method once at application startup.
    /// </summary>
    /// <param name="app">The application instance.</param>
    public static void Initialize(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _application = app;
        SyncThemeFromCurrentThemeKey();
        ResourceDictionary.CurrentThemeKey = CurrentTheme.ToString();

        if (_initialized)
        {
            ForceThemeRefresh();
            return;
        }

        EnsureXamlLoaderRegistered();

        // Loading Jalium.UI.Xaml may re-enter ThemeManager.Initialize via ThemeLoader.Initialize().
        // If that already completed initialization, stop here to avoid duplicate dictionary insertion.
        if (_initialized)
        {
            ForceThemeRefresh();
            return;
        }

        if (XamlLoader == null)
        {
            // XamlLoader not registered yet - Jalium.UI.Xaml module initializer hasn't run.
            // This will be retried when the Xaml assembly is first accessed.
            return;
        }

        _genericThemeDictionary = LoadGenericTheme();
        if (_genericThemeDictionary != null)
        {
            app.Resources.MergedDictionaries.Add(_genericThemeDictionary);
        }

        _accentDictionary = BuildAccentDictionary(CurrentAccentColor);
        _typographyDictionary = BuildTypographyDictionary(CurrentDisplayFontFamily, CurrentBodyFontFamily, CurrentMonospaceFontFamily);

        app.Resources.MergedDictionaries.Add(_accentDictionary);
        app.Resources.MergedDictionaries.Add(_typographyDictionary);

        _initialized = true;
        ForceThemeRefresh();
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
        CurrentDisplayFontFamily = NormalizeFontFamily(display, "Segoe UI");
        CurrentBodyFontFamily = NormalizeFontFamily(body, "Segoe UI");
        CurrentMonospaceFontFamily = NormalizeFontFamily(mono, "Cascadia Code");

        if (_application == null)
            return;

        ReplaceManagedDictionary(
            ref _typographyDictionary,
            BuildTypographyDictionary(CurrentDisplayFontFamily, CurrentBodyFontFamily, CurrentMonospaceFontFamily));

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
    /// </summary>
    private static ResourceDictionary? LoadGenericTheme()
    {
        if (XamlLoader == null)
        {
            // XamlLoader not registered - Jalium.UI.Xaml assembly not initialized.
            return null;
        }

        using var stream = GetGenericThemeStream();
        if (stream == null)
        {
            return null;
        }

        return XamlLoader(stream, "Themes/Generic.jalxaml", ControlsAssembly);
    }

    /// <summary>
    /// Gets the embedded resource stream for the Generic theme.
    /// </summary>
    /// <returns>The stream containing the Generic.jalxaml content, or null if not found.</returns>
    public static Stream? GetGenericThemeStream()
    {
        var assembly = typeof(ThemeManager).Assembly;
        var stream = assembly.GetManifestResourceStream(GenericThemeResourceName);

        if (stream == null)
        {
            // Fallback: try to find the resource with different naming
            var resourceNames = assembly.GetManifestResourceNames();
            var genericResource = resourceNames.FirstOrDefault(n => n.EndsWith("Generic.jalxaml", StringComparison.OrdinalIgnoreCase));

            if (genericResource != null)
            {
                stream = assembly.GetManifestResourceStream(genericResource);
            }

        }

        return stream;
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
            ["AccentBrush"] = new SolidColorBrush(accent),
            ["AccentBrushHover"] = new SolidColorBrush(hover),
            ["AccentBrushPressed"] = new SolidColorBrush(pressed),
            ["AccentBrushDisabled"] = new SolidColorBrush(disabled),
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

    private static ResourceDictionary BuildTypographyDictionary(string display, string body, string mono)
    {
        return new ResourceDictionary
        {
            ["DisplayFontFamily"] = display,
            ["BodyFontFamily"] = body,
            ["MonoFontFamily"] = mono
        };
    }

    private static string NormalizeFontFamily(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

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
