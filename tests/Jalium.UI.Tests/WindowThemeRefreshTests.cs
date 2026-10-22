#pragma warning disable WPF0001
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WindowThemeRefreshTests : IDisposable
{
    private readonly object? _theme = ResourceDictionary.CurrentThemeKey;
    private readonly Func<object, object?>? _application = ResourceLookup.ApplicationResourceLookup;
    private readonly Func<object, (object? Value, ResourceDictionary? Dictionary)>? _source = ResourceLookup.ApplicationResourceLookupWithSource;
    private readonly Func<object, object?>? _system = ResourceLookup.SystemResourceLookup;
    private readonly List<Window> _windows = [];

    public WindowThemeRefreshTests() => ResourceDictionary.CurrentThemeKey = "Dark";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RuntimeWindowThemeSelectsTheVariantImplicitStyleAndTemplate(bool initiallyDark)
    {
        var palette = new ResourceDictionary();
        var light = Styled("light"); var dark = Styled("dark");
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = light };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = dark };
        var probe = new Probe(); var window = Create(palette, probe, initiallyDark ? ThemeMode.Dark : ThemeMode.Light);
        for (int index = 0; index < 8; ++index)
        {
            bool isDark = (index % 2 == 0) == initiallyDark;
            window.ThemeMode = isDark ? ThemeMode.Dark : ThemeMode.Light;
            probe.ApplyTemplate();
            Assert.Equal(isDark ? "dark" : "light", probe.Tag);
            Assert.Same((isDark ? dark : light).Setters.OfType<Setter>().Single(x => x.Property == Control.TemplateProperty).Value, probe.Template);
            Assert.Equal(probe.Tag, ((Border)probe.FindPart("PART_Theme")!).Tag);
        }
        Assert.Equal("Dark", ResourceDictionary.CurrentThemeKey);
    }

    [Fact]
    public void ImplicitStyleCanAppearAndDisappearWhenTheWindowThemeChanges()
    {
        var palette = new ResourceDictionary(); palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new();
        var probe = new Probe(); var window = Create(palette, probe, ThemeMode.Dark);
        Assert.Null(probe.Tag); window.ThemeMode = ThemeMode.Light; Assert.Equal("light", probe.Tag);
        window.ThemeMode = ThemeMode.Dark; Assert.Null(probe.Tag);
    }

    [Fact]
    public void AnUnchangedStyleDoesNotRecreateItsTemplateOrEditableChild()
    {
        int constructions = 0; TextBox? editor = null;
        var template = new ControlTemplate(typeof(Probe));
        template.SetVisualTree(() => { constructions++; return editor = new TextBox { Name = "PART_Editor", Text = "Miii中文🙂" }; });
        var shared = new Style(typeof(Probe)); shared.Setters.Add(new Setter(Control.TemplateProperty, template));
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = shared };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = shared };
        var probe = new Probe(); var window = Create(palette, probe, ThemeMode.Light);
        probe.ApplyTemplate(); Assert.NotNull(editor); var original = editor!; original.Select(4, 4);
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.Dark })
        {
            window.ThemeMode = mode; probe.ApplyTemplate();
            Assert.Same(original, editor); Assert.Equal("Miii中文🙂", original.Text);
            Assert.Equal(4, original.SelectionStart); Assert.Equal(4, original.SelectionLength); Assert.Equal("中文🙂", original.SelectedText);
        }
        Assert.Equal(1, constructions);
    }

    [Fact]
    public void ExplicitStyleAndLocalPropertyValuesRemainAuthoritative()
    {
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = Styled("dark") };
        var authored = Styled("authored"); var probe = new Probe { Style = authored, Tag = "local" };
        var window = Create(palette, probe, ThemeMode.Light); var template = probe.Template;
        window.ThemeMode = ThemeMode.Dark;
        Assert.Same(authored, probe.Style); Assert.Same(template, probe.Template); Assert.Equal("local", probe.Tag);
    }

    [Theory]
    [InlineData("Light")] [InlineData("Dark")] [InlineData("HighContrast")]
    public void GlobalThemeNotificationsSelectImplicitStylesForApplicationScopedWindows(string key)
    {
        var palette = new ResourceDictionary();
        foreach (string variant in new[] { "Light", "Dark", "HighContrast" })
            palette.ThemeDictionaries[variant] = new() { [typeof(Probe)] = Styled(variant) };
        var probe = new Probe(); var window = Create(palette, probe, ThemeMode.None);
        ResourceDictionary.CurrentThemeKey = key; window.NotifyThemeResourcesChangedFromRoot();
        Assert.Equal(key, probe.Tag);
    }

    [Fact]
    public void AReentrantThemeChangeKeepsTheNewestStyle()
    {
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = Styled("dark") };
        var probe = new Probe(); var window = Create(palette, probe, ThemeMode.Dark); bool changed = false;
        window.ResourcesChanged += (_, _) => { if (!changed) { changed = true; window.ThemeMode = ThemeMode.Dark; } };
        window.ThemeMode = ThemeMode.Light;
        Assert.True(changed); Assert.Equal(ThemeMode.Dark, window.ThemeMode); Assert.Equal("dark", probe.Tag);
    }

    [Fact]
    public void AResourceCallbackCanMoveTheChildToAnIndependentWindow()
    {
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = Styled("dark") };
        var probe = new Probe(); var old = Create(palette, probe, ThemeMode.Light);
        var destination = Create(palette, new Border(), ThemeMode.Light); bool moved = false;
        old.ResourcesChanged += (_, _) => { if (!moved) { moved = true; old.Content = null; destination.Content = probe; } };
        old.ThemeMode = ThemeMode.Dark;
        Assert.Same(probe, destination.Content); Assert.Equal("light", probe.Tag);
    }

    [Fact]
    public void ApplicationImplicitStylesUseEachWindowScopeWithoutLocalDictionaries()
    {
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = Styled("dark") };
        ResourceLookup.ApplicationResourceLookup = key => palette.TryGetValue(key, out var value) ? value : null;
        ResourceLookup.ApplicationResourceLookupWithSource = key => palette.TryGetValue(key, out var value) ? (value, palette) : (null, null);
        ResourceLookup.InvalidateResourceCache();
        var first = new Probe(); var second = new Probe();
        var light = new Window { ThemeMode = ThemeMode.Light, Content = first };
        var dark = new Window { ThemeMode = ThemeMode.Dark, Content = second }; _windows.Add(light); _windows.Add(dark);
        Assert.Equal("light", first.Tag); Assert.Equal("dark", second.Tag);
        light.ThemeMode = ThemeMode.Dark; dark.ThemeMode = ThemeMode.Light;
        Assert.Equal("dark", first.Tag); Assert.Equal("light", second.Tag);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SetterReentryCannotRestoreTheRetiredThemeTemplate(bool moveToAnotherWindow)
    {
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = Styled("dark") };
        var probe = new Probe(); var window = Create(palette, probe, ThemeMode.Dark);
        var destination = Create(palette, new Border(), ThemeMode.Dark); bool changed = false;
        probe.Changed = args =>
        {
            if (changed || args.Property != FrameworkElement.TagProperty || !Equals(args.NewValue, "light")) return;
            changed = true;
            if (moveToAnotherWindow) { window.Content = null; destination.Content = probe; }
            else window.ThemeMode = ThemeMode.Dark;
        };
        window.ThemeMode = ThemeMode.Light; probe.ApplyTemplate();
        Assert.True(changed); Assert.Equal("dark", probe.Tag);
        Assert.Equal("dark", ((Border)probe.FindPart("PART_Theme")!).Tag);
    }

    [Fact]
    public void MultipleExplicitStyleChangesInsideASetterKeepTheLastRequest()
    {
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = Styled("dark") };
        var probe = new Probe(); var window = Create(palette, probe, ThemeMode.Dark);
        var latest = Styled("latest"); bool changed = false;
        probe.Changed = args =>
        {
            if (changed || args.Property != FrameworkElement.TagProperty || !Equals(args.NewValue, "light")) return;
            changed = true; probe.Style = Styled("intermediate"); probe.Style = latest;
        };
        window.ThemeMode = ThemeMode.Light; probe.ApplyTemplate();
        Assert.True(changed); Assert.Same(latest, probe.Style); Assert.Equal("latest", probe.Tag);
        Assert.Equal("latest", ((Border)probe.FindPart("PART_Theme")!).Tag);
        probe.Style = null; probe.ApplyTemplate();
        Assert.Equal("light", probe.Tag); Assert.Equal("light", ((Border)probe.FindPart("PART_Theme")!).Tag);
    }

    [Fact]
    public void AThrowingSetterDoesNotStrandTheStyleUpdateGate()
    {
        var palette = new ResourceDictionary();
        palette.ThemeDictionaries["Light"] = new() { [typeof(Probe)] = Styled("light") };
        palette.ThemeDictionaries["Dark"] = new() { [typeof(Probe)] = Styled("dark") };
        var probe = new Probe(); var window = Create(palette, probe, ThemeMode.Dark); int throws = 0;
        probe.Changed = args =>
        {
            if (args.Property == FrameworkElement.TagProperty && Equals(args.NewValue, "light") && throws++ == 0)
                throw new InvalidOperationException("theme setter fixture");
        };
        window.ThemeMode = ThemeMode.Light;
        Assert.Equal(1, throws);
        window.ThemeMode = ThemeMode.Dark; probe.ApplyTemplate();
        Assert.Equal("dark", probe.Tag); Assert.Equal("dark", ((Border)probe.FindPart("PART_Theme")!).Tag);
        probe.Changed = null; window.ThemeMode = ThemeMode.Light; probe.ApplyTemplate(); Assert.Equal("light", probe.Tag);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void ExplicitNullApplicationResourcesShadowSystemValues(bool noElement, bool alias)
    {
        var key = SystemColors.WindowBrushKey; object lookup = alias ? "window-alias" : key;
        var dictionary = new ResourceDictionary { [key] = null };
        if (alias) dictionary[lookup] = new Alias(key);
        int systemCalls = 0, legacyCalls = 0;
        ResourceLookup.ApplicationResourceLookup = _ => { legacyCalls++; return "legacy"; };
        ResourceLookup.ApplicationResourceLookupWithSource = k => dictionary.TryGetValue(k, out var value) ? (value, dictionary) : (null, null);
        ResourceLookup.SystemResourceLookup = _ => { systemCalls++; return "system"; };
        ResourceLookup.InvalidateResourceCache();
        var child = noElement ? null : new FrameworkElement();
        systemCalls = legacyCalls = 0;
        for (int index = 0; index < 3; ++index) Assert.Null(ResourceLookup.FindResource(child, lookup));
        Assert.Null(ResourceLookup.FindResource(child, lookup, out var source)); Assert.Same(dictionary, source);
        Assert.Equal(0, systemCalls); Assert.Equal(0, legacyCalls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingApplicationEntriesRetainTheLegacyAndSystemFallback(bool withSource)
    {
        ResourceLookup.ApplicationResourceLookup = _ => null;
        ResourceLookup.ApplicationResourceLookupWithSource = withSource ? _ => (null, null) : null;
        ResourceLookup.SystemResourceLookup = _ => "system"; ResourceLookup.InvalidateResourceCache();
        Assert.Equal("system", ResourceLookup.FindResource(new FrameworkElement(), "missing"));
    }

    [Fact]
    public void ApplicationNullCanReplaceACachedSystemValueAndBeRemovedAgain()
    {
        object key = SystemColors.WindowBrushKey; var dictionary = new ResourceDictionary();
        ResourceLookup.ApplicationResourceLookup = k => dictionary.TryGetValue(k, out var value) ? value : null;
        ResourceLookup.ApplicationResourceLookupWithSource = k => dictionary.TryGetValue(k, out var value) ? (value, dictionary) : (null, null);
        ResourceLookup.SystemResourceLookup = _ => "system"; ResourceLookup.InvalidateResourceCache(); var child = new FrameworkElement();
        Assert.Equal("system", child.TryFindResource(key));
        dictionary[key] = null; ResourceLookup.InvalidateResourceCache(); Assert.Null(child.TryFindResource(key));
        dictionary.Remove(key); ResourceLookup.InvalidateResourceCache(); Assert.Equal("system", child.TryFindResource(key));
    }

    [Fact]
    public void DynamicNullAliasAndCycleDoNotLeaveAStaleResourceChain()
    {
        var dictionary = new ResourceDictionary { ["a"] = new Alias("b"), ["b"] = new Alias("a"), ["null"] = null };
        ResourceLookup.ApplicationResourceLookup = k => dictionary.TryGetValue(k, out var value) ? value : null;
        ResourceLookup.ApplicationResourceLookupWithSource = k => dictionary.TryGetValue(k, out var value) ? (value, dictionary) : (null, null);
        ResourceLookup.SystemResourceLookup = _ => "system"; ResourceLookup.InvalidateResourceCache(); var child = new FrameworkElement();
        Assert.Null(child.TryFindResource("a")); Assert.Null(child.TryFindResource("null"));
        Assert.Equal("system", child.TryFindResource("missing"));
    }

    private Window Create(ResourceDictionary palette, FrameworkElement child, ThemeMode mode)
    {
        var window = new Window { ThemeMode = mode }; _windows.Add(window);
        window.Resources.MergedDictionaries.Add(palette); window.Content = child; return window;
    }
    private static Style Styled(string marker)
    {
        var template = new ControlTemplate(typeof(Probe)); template.SetVisualTree(() => new Border { Name = "PART_Theme", Tag = marker });
        var style = new Style(typeof(Probe)); style.Setters.Add(new Setter(FrameworkElement.TagProperty, marker));
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    private sealed class Probe : Control
    {
        internal Action<DependencyPropertyChangedEventArgs>? Changed;
        internal DependencyObject? FindPart(string name) => GetTemplateChild(name);
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
        { base.OnPropertyChanged(args); Changed?.Invoke(args); }
    }
    private sealed record Alias(object ResourceKey) : IDynamicResourceReference;
    [Theory]
    [InlineData("own")] [InlineData("merged")] [InlineData("theme")]
    public void ActualApplicationLookupPreservesAuthoredNullAndItsDictionary(string layer)
    {
        var key = SystemColors.WindowBrushKey;
        var dictionary = new ResourceDictionary { [key] = null };
        var resources = layer == "own" ? dictionary : new ResourceDictionary();
        if (layer == "merged") resources.MergedDictionaries.Add(dictionary);
        if (layer == "theme") resources.ThemeDictionaries["Dark"] = dictionary;
        var application = ApplicationWithResources(resources);
        var source = ApplicationSourceLookup(application);
        Assert.Null(application.TryFindResource(key));
        var supplied = source(key); Assert.Null(supplied.Value); Assert.Same(dictionary, supplied.Dictionary);
        ResourceLookup.ApplicationResourceLookup = application.TryFindResource;
        ResourceLookup.ApplicationResourceLookupWithSource = source;
        Assert.Null(new Border().TryFindResource(key));
        Assert.Null(ResourceLookup.FindResource(null, key));
        Assert.Null(ResourceLookup.FindResource(new Border(), key, out var actual)); Assert.Same(dictionary, actual);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualApplicationLookupKeepsMissingAndUnsetSystemFallback(bool unset)
    {
        var key = SystemParameters.ClientAreaAnimationKey;
        var resources = new ResourceDictionary(); if (unset) resources[key] = DependencyProperty.UnsetValue;
        var application = ApplicationWithResources(resources);
        Assert.Equal(SystemParameters.ClientAreaAnimation, application.TryFindResource(key));
        var supplied = ApplicationSourceLookup(application)(key);
        Assert.Equal(SystemParameters.ClientAreaAnimation, supplied.Value); Assert.Null(supplied.Dictionary);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PublicCallbackOverridesAndRestoresItsPairedSourceWithoutCachedValues(bool cached)
    {
        var key = new object(); var dictionary = new ResourceDictionary { [key] = "original" };
        Func<object, object?> original = k => Equals(k, key) ? "original" : null;
        Func<object, (object? Value, ResourceDictionary? Dictionary)> source = k =>
            Equals(k, key) ? ("original", dictionary) : (null, null);
        ResourceLookup.ApplicationResourceLookup = original;
        ResourceLookup.ApplicationResourceLookupWithSource = source;
        var child = new Border();
        if (cached) Assert.Equal("original", child.TryFindResource(key));
        ResourceLookup.ApplicationResourceLookup = k => Equals(k, key) ? "replacement" : null;
        Assert.Equal("replacement", child.TryFindResource(key));
        Assert.Equal("replacement", ResourceLookup.FindResource(null, key));
        ResourceLookup.ApplicationResourceLookup = original;
        Assert.Same(source, ResourceLookup.ApplicationResourceLookupWithSource);
        Assert.Equal("original", child.TryFindResource(key));
        Assert.Equal("original", ResourceLookup.FindResource(child, key, out var supplied)); Assert.Same(dictionary, supplied);
    }

    private static Application ApplicationWithResources(ResourceDictionary resources)
    {
        var application = (Application)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Application));
        typeof(Application).GetField("_resources", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(application, resources);
        return application;
    }

    private static Func<object, (object? Value, ResourceDictionary? Dictionary)> ApplicationSourceLookup(Application application)
        => typeof(Application).GetMethod("TryFindApplicationOrSystemResourceWithSource",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate<Func<object, (object? Value, ResourceDictionary? Dictionary)>>(application);

    public void Dispose()
    {
        foreach (var window in _windows) window.Close();
        ResourceLookup.ApplicationResourceLookup = _application; ResourceLookup.ApplicationResourceLookupWithSource = _source;
        ResourceLookup.SystemResourceLookup = _system; ResourceDictionary.CurrentThemeKey = _theme;
        ResourceLookup.InvalidateResourceCache();
    }
}
