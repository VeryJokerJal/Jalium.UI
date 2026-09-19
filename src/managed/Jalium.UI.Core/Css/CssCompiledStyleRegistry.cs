using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>Registers lazy CSS factories emitted by the build compiler. Registration does not apply styles.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CssCompiledStyleRegistry
{
    private sealed class Catalog
    {
        internal readonly ConcurrentDictionary<string, Func<string?, Uri?, CssStyleSheet>> Resources = new(StringComparer.Ordinal);
        internal readonly ConcurrentDictionary<(string Text, bool Inline), Func<string?, Uri?, CssStyleSheet>> Text = new();
    }

    // Factories must not prevent unloading their application/plugin assembly.
    private static readonly ConditionalWeakTable<Assembly, Catalog> s_catalogs = new();

    public static void RegisterResource(Assembly assembly, string resourceUri, Func<string?, Uri?, CssStyleSheet> factory)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceUri);
        ArgumentNullException.ThrowIfNull(factory);
        s_catalogs.GetValue(assembly, static _ => new Catalog()).Resources[Normalize(resourceUri)] = factory;
    }

    public static void RegisterText(Assembly assembly, string text, bool inline, Func<string?, Uri?, CssStyleSheet> factory)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(factory);
        s_catalogs.GetValue(assembly, static _ => new Catalog()).Text[(text, inline)] = factory;
    }

    internal static bool TryCreateResource(Uri uri, out CssStyleSheet sheet)
    {
        var key = Normalize(uri.OriginalString);
        foreach (var entry in s_catalogs)
        {
            if (!entry.Value.Resources.TryGetValue(key, out var factory)) continue;
            sheet = factory(uri.ToString(), uri);
            return true;
        }
        sheet = null!;
        return false;
    }

    internal static bool TryCreateText(string text, bool inline, string? label, out CssStyleSheet sheet)
    {
        foreach (var entry in s_catalogs)
        {
            if (!entry.Value.Text.TryGetValue((text, inline), out var factory)) continue;
            sheet = factory(label, null);
            return true;
        }
        sheet = null!;
        return false;
    }

    private static string Normalize(string uri)
    {
        const string prefix = "pack://application:,,,";
        if (uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) uri = uri[prefix.Length..];
        return uri.Replace('\\', '/');
    }
}
