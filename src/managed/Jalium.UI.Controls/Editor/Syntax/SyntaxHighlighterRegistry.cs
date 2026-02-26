using Jalium.UI.Controls;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Registry for language-to-highlighter factories so host layers can plug in advanced implementations.
/// </summary>
public static class SyntaxHighlighterRegistry
{
    private static readonly object s_syncRoot = new();
    private static readonly Dictionary<string, Func<EditControl, ISyntaxHighlighter?>> s_factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a factory for a language identifier.
    /// </summary>
    public static void Register(string language, Func<EditControl, ISyntaxHighlighter?> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(factory);

        string key = NormalizeLanguage(language);
        lock (s_syncRoot)
        {
            s_factories[key] = factory;
        }
    }

    /// <summary>
    /// Removes a factory for a language identifier.
    /// </summary>
    public static bool Unregister(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return false;

        string key = NormalizeLanguage(language);
        lock (s_syncRoot)
        {
            return s_factories.Remove(key);
        }
    }

    /// <summary>
    /// Tries to create a registered highlighter for the specified language.
    /// </summary>
    public static bool TryCreate(EditControl owner, string language, out ISyntaxHighlighter? highlighter)
    {
        ArgumentNullException.ThrowIfNull(owner);
        highlighter = null;

        if (string.IsNullOrWhiteSpace(language))
            return false;

        Func<EditControl, ISyntaxHighlighter?>? factory;
        string key = NormalizeLanguage(language);
        lock (s_syncRoot)
        {
            if (!s_factories.TryGetValue(key, out factory))
                return false;
        }

        highlighter = factory(owner);
        return highlighter != null;
    }

    private static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant();

    internal static void ClearForTesting()
    {
        lock (s_syncRoot)
        {
            s_factories.Clear();
        }
    }
}
