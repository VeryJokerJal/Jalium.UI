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
    /// Per-type cache for <see cref="ResolveThemeStyle"/>. Style-stack re-evaluation runs
    /// for every element on tree attach and resource changes, so the resolver must be cheap;
    /// entries are invalidated whenever the generic theme dictionary instance is replaced.
    /// Published snapshots are never mutated, so the very hot read path is lock-free. Misses and
    /// invalidation copy/publish under the write gate.
    /// </summary>
    private static readonly object _themeStyleCacheWriteGate = new();
    private static Dictionary<Type, Style?> _themeStyleCache = new();

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

    static ThemeManager()
    {
        // Give Core a way to resolve theme default styles (the bottom layer of every
        // element's style stack) without referencing the theme dictionaries directly.
        // Registered in the static constructor so it is in place before any element
        // evaluates its style stack; while no theme is loaded it just returns null.
        FrameworkElement.ThemeStyleResolver = ResolveThemeStyle;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    /// <summary>
    /// Resolves the theme default style for an exact element type from the generic
    /// theme dictionary (the base-type walk happens in FrameworkElement).
    /// </summary>
    private static Style? ResolveThemeStyle(Type type)
    {
        var dictionary = _genericThemeDictionary;
        if (dictionary == null)
            return null;

        var snapshot = Volatile.Read(ref _themeStyleCache);
        if (snapshot.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var style = dictionary.TryGetValue(type, out var value) ? value as Style : null;

        lock (_themeStyleCacheWriteGate)
        {
            snapshot = Volatile.Read(ref _themeStyleCache);
            if (snapshot.TryGetValue(type, out cached))
            {
                return cached;
            }

            var next = new Dictionary<Type, Style?>(snapshot)
            {
                [type] = style
            };
            Volatile.Write(ref _themeStyleCache, next);
        }

        return style;
    }

    /// <summary>
    /// Drops all cached theme-style resolutions. Must be called whenever the generic
    /// theme dictionary instance changes (theme swap, refresh, test reset).
    /// </summary>
    private static void InvalidateThemeStyleCache()
    {
        lock (_themeStyleCacheWriteGate)
        {
            Volatile.Write(ref _themeStyleCache, new Dictionary<Type, Style?>());
        }
    }

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
    public static string CurrentDisplayFontFamily { get; private set; } = FrameworkElement.DefaultFontFamilyName;

    /// <summary>
    /// Gets the current body font family.
    /// </summary>
    public static string CurrentBodyFontFamily { get; private set; } = FrameworkElement.DefaultFontFamilyName;

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
        ResourceDictionary.CurrentThemeKey = GetEffectiveThemeKey();

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
        InvalidateThemeStyleCache();
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
        // Adding the managed dictionaries above already raises the normal resource change
        // notification for any live root. Only detached/unshown bindings need completion.
        ForceThemeRefresh(notifyLiveRoots: false);
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
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unsupported theme variant.");
        }

        var themeChanged = CurrentTheme != theme;
        CurrentTheme = theme;

        var effectiveThemeKey = GetEffectiveThemeKey();
        var themeKeyChanged = !Equals(ResourceDictionary.CurrentThemeKey, effectiveThemeKey);
        if (!themeChanged && !themeKeyChanged)
        {
            return;
        }

        // High contrast is an accessibility override. Remember the requested light/dark
        // variant underneath it, but keep the active dictionary on HighContrast until the
        // operating-system preference is released.
        ResourceDictionary.CurrentThemeKey = effectiveThemeKey;

        // Generic.jalxaml is structurally invariant across variants: its control styles and
        // templates use ThemeResource for every palette-dependent value. Rebuilding that large
        // dictionary used to allocate a second complete template graph, trigger a whole-window
        // implicit-style walk, tear down live templates, and then refresh the same dynamic
        // resources again. Keep the immutable structure and switch only the ThemeDictionaries
        // lookup key. The accent dictionary also contains both variants, so this is now a
        // single allocation-free resource sweep.
        ForceThemeRefresh(notifyLiveRoots: true);
        if (_application != null)
        {
            CompositionTarget.RequestImmediateFrame();
        }
    }

    /// <summary>
    /// Applies a runtime accent color and regenerates derived accent tokens.
    /// </summary>
    /// <remarks>
    /// 这条路径是「拖动取色器实时改主题」的每帧成本所在，因此按两个原则实现：
    /// <list type="number">
    ///   <item>颜色没变就直接返回。指针在同一像素上抖动、或调用方按帧重发当前值时，
    ///         整条重算链路完全不该启动。</item>
    ///   <item>就地更新既有的 accent 字典，而不是构造新字典去替换合并列表里的槽位。
    ///         替换槽位是**结构性**变化，通知里带不出键集合，上层只能按「所有键都变了」
    ///         处理——即整棵可视树重新求值隐式样式（本机 861 元素的窗口上 2.4 ms）。
    ///         就地更新发出的是精确键集合，动态资源订阅按键定向刷新，隐式样式一步不动。</item>
    /// </list>
    /// </remarks>
    public static void ApplyAccent(Color accent)
    {
        if (_accentDictionary != null && CurrentAccentColor == accent)
            return;

        CurrentAccentColor = accent;

        if (_application == null)
            return;

        if (_accentDictionary is { } existing)
        {
            UpdateAccentDictionaryInPlace(existing, accent);

            // 就地更新发出的是精确键集合，Application 据此做定向刷新；那一遍已经扫过全局
            // 注册表（含 detached / 未显示的树），不需要再补一次 unloaded 扫描。
            CompleteTargetedThemeRefresh();
        }
        else
        {
            // 首次挂载 accent 字典是结构性变化，通知退化为「全部键」，只覆盖活根，
            // 因此仍需补扫 detached / 未显示的注册。
            ReplaceManagedDictionary(ref _accentDictionary, BuildAccentDictionary(accent));
            ForceThemeRefresh(notifyLiveRoots: false);
        }
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
        CurrentDisplayFontFamily = NormalizeFontFamily(display, FrameworkElement.DefaultFontFamilyName);
        CurrentBodyFontFamily = NormalizeFontFamily(body, FrameworkElement.DefaultFontFamilyName);
        CurrentMonospaceFontFamily = NormalizeFontFamily(mono, "Cascadia Code");
        CurrentBodyFontSize = double.IsFinite(bodyFontSize) && bodyFontSize > 0
            ? bodyFontSize
            : FrameworkElement.DefaultFontSize;

        if (_application == null)
            return;

        ReplaceManagedDictionary(
            ref _typographyDictionary,
            BuildTypographyDictionary(CurrentDisplayFontFamily, CurrentBodyFontFamily, CurrentMonospaceFontFamily, CurrentBodyFontSize));

        // The dictionary replacement already notified every live root.
        ForceThemeRefresh(notifyLiveRoots: false);
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
            // Accent and typography replace managed dictionaries. Fold those mutations into one
            // implicit-style broadcast so every live root is re-evaluated exactly once. ApplyTheme
            // only changes the active ThemeDictionary key; _suppressRefresh folds its lightweight
            // palette walk into the same coalesced dictionary notification.
            using var notifications = _application?.Resources.DeferNotifications();

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

        // 这里刻意不再补一次全树广播。
        //
        // ApplyTheme 改的是 CurrentThemeKey，它不产生任何字典条目变更，却会改变**每一个**
        // ThemeResource 的解析结果——所以这一批操作确实需要一次全量刷新。而它已经有了：
        // ApplyTypography 走的是「整本替换合并字典」，那是结构性变化，延迟作用域结束时
        // 发出的是「所有键都变了」，Application 据此走全树广播并刷新每一个订阅。
        // 再加一次就是把每个活根重新求值两遍。
        // （回归测试：ThemeRuntimeSwitchTests.ApplyBrandTheme_ShouldReevaluateEachLiveRoot_ExactlyOnce
        //   钉住次数；ThemeRuntimeSwitchTests 里的变体绑定用例钉住主题键变化确实传播到了活树。）
        ForceThemeRefresh(notifyLiveRoots: false);
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
    /// Monotonic counter bumped once per applied theme/accent/typography change
    /// (see <see cref="ForceThemeRefresh"/>). A <see cref="Window"/> snapshots it when
    /// its content is first styled and compares it in <see cref="Window.Show"/>, so a
    /// theme swap that landed while the window was unshown — hence unreachable by the
    /// <see cref="Application"/> resource broadcast — is healed the moment it is shown.
    /// </summary>
    internal static int CurrentThemeVersion => _themeVersion;

    /// <summary>
    /// Resets the theme system, allowing re-initialization.
    /// Primarily for testing purposes.
    /// </summary>
    internal static void Reset()
    {
        DynamicResourceBindingOperations.ResetRegistryForTesting();
        DetachManagedDictionaries(_application);

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
        InvalidateThemeStyleCache();

        CurrentTheme = ThemeVariant.Dark;
        CurrentAccentColor = DefaultPrimaryAccentColor;
        CurrentDisplayFontFamily = FrameworkElement.DefaultFontFamilyName;
        CurrentBodyFontFamily = FrameworkElement.DefaultFontFamilyName;
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

    /// <summary>
    /// 收尾一次「已经通过精确键集合完成定向刷新」的调色板更新。
    /// </summary>
    /// <remarks>
    /// <see cref="ForceThemeRefresh"/> 的 <c>RefreshUnloaded</c> 是为「全树广播只覆盖活根、
    /// detached / 未显示的树够不着」这个前提准备的补扫。定向刷新走的是全局注册表，
    /// loaded 与否一视同仁，补扫因此变成对同一批订阅的第二次遍历——在本机 861 元素的
    /// 窗口上，紧跟一次缓存失效之后的这遍冷扫描要 1.7 ms，比更新字典本身还贵。
    /// </remarks>
    private static void CompleteTargetedThemeRefresh()
    {
        if (_application == null || _suppressRefresh)
            return;

        // 定向刷新已经把新值送到了每一个受影响的订阅，但公开的 Application.ResourcesChanged
        // 仍要照常触发——宿主可能挂着自己的手工刷新逻辑，不该因为框架内部换了刷新策略而失灵。
        _application.RaiseResourcesChangedWithoutBroadcast();
        _themeVersion++;
    }

    private static void ForceThemeRefresh(bool notifyLiveRoots)
    {
        if (_application == null || _suppressRefresh)
            return;

        if (notifyLiveRoots)
        {
            // CurrentThemeKey can change without mutating a dictionary. Publish one lightweight
            // palette notification: dynamic bindings and manual resource caches update, and
            // retained drawings are re-recorded, while styles/templates/layout remain intact.
            _application.NotifyThemeResourcesChanged();
        }
        else
        {
            // A preceding dictionary mutation already performed the live-root broadcast.
            ResourceLookup.InvalidateResourceCache();
        }

        // Detached and not-yet-shown trees are outside every live-root walk. Refresh only those
        // registrations instead of sweeping the loaded tree a second time.
        DynamicResourceBindingOperations.RefreshUnloaded();

        _themeVersion++;
    }

    private static ResourceDictionary BuildAccentDictionary(Color accent)
    {
        var dictionary = new ResourceDictionary();
        PopulateAccentDictionary(dictionary, accent);
        return dictionary;
    }

    /// <summary>
    /// 就地把既有 accent 字典刷新到新的强调色，并只发布实际变化的键。
    /// </summary>
    /// <remarks>
    /// <para>画刷条目走「复用实例、只改颜色」：字典里的值引用不变，所以这些键**不进**通知集合。
    /// 重绘由 <c>Brush</c> 自身的 render-owner 机制驱动——只有真正把这支画刷绑到某个属性上的
    /// 元素被标脏，比让每个动态资源订阅重新求值精确得多，也不产生一次性画刷垃圾。
    /// 只有画刷被冻结、缺失或类型不符时才退回替换实例，那时才需要发键通知。</para>
    /// <para>颜色（值类型）条目无法就地更新，必须换值并发键通知。</para>
    /// <para>变体字典（<see cref="ResourceDictionary.ThemeDictionaries"/> 的值）不是合并字典，
    /// 它的条目变更不会自行冒泡，所以在延迟通知的作用域内把变体键补进宿主的待发集合，
    /// 由宿主在作用域结束时一次性发出完整键集合。</para>
    /// </remarks>
    private static void UpdateAccentDictionaryInPlace(ResourceDictionary dictionary, Color accent)
    {
        var variantKeys = new HashSet<object>();
        using var notifications = dictionary.DeferNotifications();
        PopulateAccentDictionary(dictionary, accent, variantKeys);
        if (variantKeys.Count > 0)
        {
            dictionary.NotifyKeysChanged(variantKeys);
        }
    }

    /// <summary>
    /// 构建与就地更新共用的唯一填充点。对空字典调用等价于构建，对既有字典调用等价于刷新。
    /// </summary>
    /// <param name="variantChangedKeys">
    /// 非 null 时收集变体字典里实际变化的键，供调用方补发通知（构建路径传 null：整本字典
    /// 是新的，宿主会按结构性变化处理）。
    /// </param>
    private static void PopulateAccentDictionary(
        ResourceDictionary dictionary,
        Color accent,
        ICollection<object>? variantChangedKeys = null)
    {
        var hover = Blend(accent, Color.White, 0.18);
        var pressed = Blend(accent, Color.Black, 0.24);

        var selection = Color.FromArgb(0x99, accent.R, accent.G, accent.B);
        var weakSelection = Color.FromArgb(0x4D, accent.R, accent.G, accent.B);

        // 顶层字典的写入会自行累积键，不需要外部收集。
        SetColorEntry(dictionary, "SystemAccentColor", accent, null);
        SetColorEntry(dictionary, "SystemAccentColorLight1", Blend(accent, Color.White, 0.18), null);
        SetColorEntry(dictionary, "SystemAccentColorLight2", Blend(accent, Color.White, 0.34), null);
        SetColorEntry(dictionary, "SystemAccentColorLight3", Blend(accent, Color.White, 0.52), null);
        SetColorEntry(dictionary, "SystemAccentColorDark1", Blend(accent, Color.Black, 0.12), null);
        SetColorEntry(dictionary, "SystemAccentColorDark2", Blend(accent, Color.Black, 0.24), null);
        SetColorEntry(dictionary, "SystemAccentColorDark3", Blend(accent, Color.Black, 0.36), null);
        SetColorEntry(dictionary, "AccentFillColorSelectedTextBackground", accent, null);
        SetGradientBrushEntry(dictionary, "AccentBrush", accent, null);
        SetGradientBrushEntry(dictionary, "AccentBrushHover", hover, null);
        SetGradientBrushEntry(dictionary, "AccentBrushPressed", pressed, null);
        SetSolidBrushEntry(dictionary, "AccentFillColorSelectedTextBackgroundBrush", accent, null);
        SetSolidBrushEntry(dictionary, "SelectionBackground", selection, null);
        SetSolidBrushEntry(dictionary, "SelectionBackgroundWeak", weakSelection, null);
        SetSolidBrushEntry(dictionary, "AppBarButtonForeground", accent, null);
        SetSolidBrushEntry(dictionary, "ProgressRingForeground", accent, null);
        SetSolidBrushEntry(dictionary, "BrandPrimaryAccentBrush", accent, null);
        SetSolidBrushEntry(dictionary, "BrandSecondaryAccentBrush", DefaultSecondaryAccentColor, null);
        SetColorEntry(dictionary, "BrandPrimaryAccentColor", accent, null);
        SetColorEntry(dictionary, "BrandSecondaryAccentColor", DefaultSecondaryAccentColor, null);

        PopulateAccentVariant(
            GetOrCreateVariant(dictionary, ThemeVariant.Dark.ToString()), accent, ThemeVariant.Dark, variantChangedKeys);
        PopulateAccentVariant(
            GetOrCreateVariant(dictionary, ThemeVariant.Light.ToString()), accent, ThemeVariant.Light, variantChangedKeys);

        if (!dictionary.ThemeDictionaries.ContainsKey("HighContrast"))
        {
            // 高对比度变体全部是固定值，与强调色无关，只在缺失时构建一次。
            dictionary.ThemeDictionaries["HighContrast"] = BuildHighContrastVariant();
        }
    }

    /// <summary>
    /// 写入一个颜色条目。值类型无法就地更新，只在值真的变了时写入并登记键。
    /// </summary>
    private static void SetColorEntry(
        ResourceDictionary dictionary, object key, Color color, ICollection<object>? changedKeys)
    {
        if (dictionary.TryGetOwnValue(key, out var existing) && existing is Color current && current == color)
        {
            return;
        }

        dictionary[key] = color;
        changedKeys?.Add(key);
    }

    /// <summary>
    /// 写入一个纯色画刷条目，优先就地改色以保住实例身份。
    /// </summary>
    private static void SetSolidBrushEntry(
        ResourceDictionary dictionary, object key, Color color, ICollection<object>? changedKeys)
    {
        if (dictionary.TryGetOwnValue(key, out var existing) &&
            existing is SolidColorBrush { IsFrozen: false } brush)
        {
            // 实例身份不变 ⇒ 不进通知集合；重绘由 render owner 机制负责。
            brush.Color = color;
            return;
        }

        dictionary[key] = new SolidColorBrush(color);
        changedKeys?.Add(key);
    }

    /// <summary>
    /// 写入一个强调色渐变画刷条目，优先就地改两个停靠点的颜色以保住实例身份。
    /// </summary>
    private static void SetGradientBrushEntry(
        ResourceDictionary dictionary, object key, Color color, ICollection<object>? changedKeys)
    {
        var start = Blend(color, Color.Black, 0.06);
        var end = Blend(color, Color.White, 0.06);

        if (dictionary.TryGetOwnValue(key, out var existing) &&
            existing is LinearGradientBrush { IsFrozen: false } brush)
        {
            var stops = brush.GradientStops;
            if (stops.Count == 2 && !stops[0].IsFrozen && !stops[1].IsFrozen)
            {
                // 两个停靠点是一次逻辑变更：合并通知，否则画刷持有者会被标脏两遍。
                using var batch = stops.DeferStopChangeNotifications();
                stops[0].Color = start;
                stops[1].Color = end;
                return;
            }
        }

        dictionary[key] = AccentGradient(color);
        changedKeys?.Add(key);
    }

    private static ResourceDictionary GetOrCreateVariant(ResourceDictionary dictionary, string key)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new ResourceDictionary();
        dictionary.ThemeDictionaries[key] = created;
        return created;
    }

    /// <summary>
    /// Every AccentBrush flavor ships as a top-to-bottom gradient so the
    /// default palette yields the #207245 -> #1C8043 look (and custom
    /// accents automatically get a subtle two-stop gradient in the same
    /// style). The start stop is the accent darkened ~5% and the end stop
    /// is the accent lightened ~5% so the midpoint matches <paramref name="color"/>.
    /// </summary>
    private static LinearGradientBrush AccentGradient(Color color)
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

    private static void PopulateAccentVariant(
        ResourceDictionary variant,
        Color accent,
        ThemeVariant theme,
        ICollection<object>? changedKeys)
    {
        using var notifications = variant.DeferNotifications();

        var darkTheme = theme == ThemeVariant.Dark;
        var light2 = Blend(accent, Color.White, 0.34);
        var light3 = Blend(accent, Color.White, 0.52);
        var dark1 = Blend(accent, Color.Black, 0.12);
        var dark2 = Blend(accent, Color.Black, 0.24);
        var dark3 = Blend(accent, Color.Black, 0.36);

        var disabledBlendTarget = darkTheme
            ? Color.FromRgb(0x66, 0x66, 0x66)
            : Color.FromRgb(0xB8, 0xB8, 0xB8);
        var disabled = Blend(accent, disabledBlendTarget, 0.58);
        var accentFillDefault = darkTheme ? light2 : dark1;
        var accentFillSecondary = Color.FromArgb(
            0xE6,
            accentFillDefault.R,
            accentFillDefault.G,
            accentFillDefault.B);
        var accentFillTertiary = Color.FromArgb(
            0xCC,
            accentFillDefault.R,
            accentFillDefault.G,
            accentFillDefault.B);
        var accentTextPrimary = darkTheme ? light3 : dark2;
        var accentTextSecondary = darkTheme ? light3 : dark3;
        var accentTextTertiary = darkTheme ? light2 : dark1;
        var systemFillAttention = darkTheme ? light2 : accent;

        SetColorEntry(variant, "AccentTextFillColorPrimary", accentTextPrimary, changedKeys);
        SetColorEntry(variant, "AccentTextFillColorSecondary", accentTextSecondary, changedKeys);
        SetColorEntry(variant, "AccentTextFillColorTertiary", accentTextTertiary, changedKeys);
        SetColorEntry(variant, "AccentTextFillColorDisabled", disabled, changedKeys);
        SetColorEntry(variant, "AccentFillColorDefault", accentFillDefault, changedKeys);
        SetColorEntry(variant, "AccentFillColorSecondary", accentFillSecondary, changedKeys);
        SetColorEntry(variant, "AccentFillColorTertiary", accentFillTertiary, changedKeys);
        SetColorEntry(variant, "AccentFillColorDisabled", disabled, changedKeys);
        SetColorEntry(variant, "SystemFillColorAttention", systemFillAttention, changedKeys);
        SetGradientBrushEntry(variant, "AccentBrushDisabled", disabled, changedKeys);
        SetSolidBrushEntry(variant, "AccentTextFillColorPrimaryBrush", accentTextPrimary, changedKeys);
        SetSolidBrushEntry(variant, "AccentTextFillColorSecondaryBrush", accentTextSecondary, changedKeys);
        SetSolidBrushEntry(variant, "AccentTextFillColorTertiaryBrush", accentTextTertiary, changedKeys);
        SetSolidBrushEntry(variant, "AccentTextFillColorDisabledBrush", disabled, changedKeys);
        SetSolidBrushEntry(variant, "AccentFillColorDefaultBrush", accentFillDefault, changedKeys);
        SetSolidBrushEntry(variant, "AccentFillColorSecondaryBrush", accentFillSecondary, changedKeys);
        SetSolidBrushEntry(variant, "AccentFillColorTertiaryBrush", accentFillTertiary, changedKeys);
        SetSolidBrushEntry(variant, "AccentFillColorDisabledBrush", disabled, changedKeys);
        SetSolidBrushEntry(variant, "SystemFillColorAttentionBrush", systemFillAttention, changedKeys);
        SetSolidBrushEntry(variant, "AppBarButtonForegroundDisabled", disabled, changedKeys);
    }

    private static ResourceDictionary BuildHighContrastVariant()
    {
        var accentColor = Color.FromRgb(0xFF, 0xFF, 0x00);
        var pressedColor = Color.White;
        var disabledColor = Color.FromRgb(0x7F, 0x7F, 0x7F);
        var onAccentColor = Color.Black;

        return new ResourceDictionary
        {
            ["SystemAccentColor"] = accentColor,
            ["SystemAccentColorLight1"] = accentColor,
            ["SystemAccentColorLight2"] = accentColor,
            ["SystemAccentColorLight3"] = accentColor,
            ["SystemAccentColorDark1"] = accentColor,
            ["SystemAccentColorDark2"] = accentColor,
            ["SystemAccentColorDark3"] = accentColor,
            ["AccentTextFillColorPrimary"] = accentColor,
            ["AccentTextFillColorSecondary"] = accentColor,
            ["AccentTextFillColorTertiary"] = accentColor,
            ["AccentTextFillColorDisabled"] = disabledColor,
            ["AccentFillColorDefault"] = accentColor,
            ["AccentFillColorSecondary"] = accentColor,
            ["AccentFillColorTertiary"] = accentColor,
            ["AccentFillColorDisabled"] = disabledColor,
            ["AccentFillColorSelectedTextBackground"] = accentColor,
            ["SystemFillColorAttention"] = accentColor,
            ["AccentBrush"] = new SolidColorBrush(accentColor),
            ["AccentBrushHover"] = new SolidColorBrush(accentColor),
            ["AccentBrushPressed"] = new SolidColorBrush(pressedColor),
            ["AccentBrushDisabled"] = new SolidColorBrush(disabledColor),
            ["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(accentColor),
            ["AccentTextFillColorSecondaryBrush"] = new SolidColorBrush(accentColor),
            ["AccentTextFillColorTertiaryBrush"] = new SolidColorBrush(accentColor),
            ["AccentTextFillColorDisabledBrush"] = new SolidColorBrush(disabledColor),
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(accentColor),
            ["AccentFillColorSecondaryBrush"] = new SolidColorBrush(accentColor),
            ["AccentFillColorTertiaryBrush"] = new SolidColorBrush(accentColor),
            ["AccentFillColorDisabledBrush"] = new SolidColorBrush(disabledColor),
            ["AccentFillColorSelectedTextBackgroundBrush"] = new SolidColorBrush(accentColor),
            ["SystemFillColorAttentionBrush"] = new SolidColorBrush(accentColor),
            ["SelectionBackground"] = new SolidColorBrush(accentColor),
            ["SelectionBackgroundWeak"] = new SolidColorBrush(accentColor),
            ["AppBarButtonForeground"] = new SolidColorBrush(accentColor),
            ["AppBarButtonForegroundDisabled"] = new SolidColorBrush(disabledColor),
            ["ProgressRingForeground"] = new SolidColorBrush(accentColor),
            ["BrandPrimaryAccentBrush"] = new SolidColorBrush(accentColor),
            ["BrandPrimaryAccentColor"] = accentColor,
            ["TextOnAccent"] = new SolidColorBrush(onAccentColor),
        };
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
    [UnconditionalSuppressMessage("Trimming", "IL2035:UnresolvedAssembly",
        Justification = "Jalium.UI.Desktop is an optional Windows integration package and is intentionally absent from Linux-only deployments.")]
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

    private static string GetEffectiveThemeKey()
        => SystemParameters.HighContrast ? "HighContrast" : CurrentTheme.ToString();

    private static void OnSystemParametersChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) &&
            !string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            return;
        }

        var effectiveThemeKey = GetEffectiveThemeKey();
        if (Equals(ResourceDictionary.CurrentThemeKey, effectiveThemeKey))
        {
            return;
        }

        ResourceDictionary.CurrentThemeKey = effectiveThemeKey;
        ForceThemeRefresh(notifyLiveRoots: true);

        if (_application != null)
        {
            CompositionTarget.RequestImmediateFrame();
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
