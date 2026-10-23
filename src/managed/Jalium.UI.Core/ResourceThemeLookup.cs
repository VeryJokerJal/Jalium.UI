namespace Jalium.UI;

public partial class ResourceDictionary
{
    internal bool HasLookupResources => _innerDictionary.Count != 0 || _mergedDictionaries.Count != 0 ||
        _themeDictionaries is { Count: > 0 };

    internal void MergeResourcesFrom(ResourceDictionary source)
    {
        if (ReferenceEquals(this, source)) return;
        using var notifications = DeferNotifications();
        foreach (var merged in source.MergedDictionaries)
            MergedDictionaries.Add(merged);
        foreach (System.Collections.DictionaryEntry entry in source)
            this[entry.Key] = entry.Value;
        if (source._themeDictionaries is not { Count: > 0 }) return;
        foreach (var pair in source._themeDictionaries)
            ThemeDictionaries[pair.Key] = pair.Value;
        // ThemeDictionaries is a plain map. Publish its structural change even
        // when the wrapper contains no ordinary entries or merged dictionaries.
        OnChanged();
    }

    [ThreadStatic] private static bool t_hasLookupTheme;
    [ThreadStatic] private static object? t_lookupTheme;

    internal static object? EffectiveLookupThemeKey => t_hasLookupTheme ? t_lookupTheme : CurrentThemeKey;

    internal readonly struct ThemeLookupScope : IDisposable
    {
        private readonly bool _previousHasTheme;
        private readonly object? _previousTheme;
        internal object? ThemeKey { get; }

        internal ThemeLookupScope(object? themeKey)
        {
            _previousHasTheme = t_hasLookupTheme;
            _previousTheme = t_lookupTheme;
            ThemeKey = themeKey;
            t_hasLookupTheme = true;
            t_lookupTheme = themeKey;
        }

        public void Dispose()
        {
            t_lookupTheme = _previousTheme;
            t_hasLookupTheme = _previousHasTheme;
        }
    }

    private readonly struct ThemeLookupVisit(ResourceDictionary dictionary, object? theme) : IEquatable<ThemeLookupVisit>
    {
        private readonly ResourceDictionary _dictionary = dictionary;
        private readonly object? _theme = theme;
        public bool Equals(ThemeLookupVisit other) => ReferenceEquals(_dictionary, other._dictionary) && Equals(_theme, other._theme);
        public override bool Equals(object? obj) => obj is ThemeLookupVisit other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_dictionary), _theme);
    }
}

public static partial class ResourceLookup
{
    internal static ResourceDictionary.ThemeLookupScope EnterThemeLookup(FrameworkElement? element)
    {
        var current = element;
        for (int depth = 0; current != null && depth < 2048; depth++)
        {
            if (current.GetResourceThemeKeyOverride() is { } theme)
                return new ResourceDictionary.ThemeLookupScope(theme);
            var redirected = AncestorRedirectLookup?.Invoke(current);
            current = redirected != null && !ReferenceEquals(redirected, current) ? redirected : current.FrameworkParent;
        }
        return new ResourceDictionary.ThemeLookupScope(ResourceDictionary.CurrentThemeKey);
    }
}
