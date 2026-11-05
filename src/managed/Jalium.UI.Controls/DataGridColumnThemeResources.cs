using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Controls;

/// <summary>Loads the immutable column defaults without creating an Application or visual tree.</summary>
internal static class DataGridColumnThemeResources
{
    private static readonly object Gate = new();
    private static ResourceDictionary? _defaults;

    internal static Style GetStyle(ComponentResourceKey key) =>
        GetDictionary().TryGetValue(key, out var value) && value is Style style
            ? style
            : throw new InvalidOperationException($"The default grid-column style '{key}' is missing.");

    internal static T CreateElement<T>(string key) where T : FrameworkElement
    {
        var resources = GetDictionary();
        if (resources.TryGetValue(key, out var value) && value is DataTemplate template && template.LoadContent() is T element)
            return element;
        throw new InvalidOperationException($"The grid-column template '{key}' must create a {typeof(T).Name}.");
    }

    private static ResourceDictionary GetDictionary()
    {
        if (Volatile.Read(ref _defaults) is { } existing) return existing;
        lock (Gate)
        {
            if (_defaults is { } cached) return cached;
            var resources = ThemeManager.LoadControlThemeDictionary("Themes/Controls/DataGridColumns.jalxaml");
            foreach (var key in resources.Keys.Cast<object>().ToArray())
            {
                if (!resources.TryGetValue(key, out var value)) continue;
                if (value is Style style) style.Seal();
                else if (value is FrameworkTemplate template) template.Seal();
            }
            Volatile.Write(ref _defaults, resources);
            return resources;
        }
    }
}
