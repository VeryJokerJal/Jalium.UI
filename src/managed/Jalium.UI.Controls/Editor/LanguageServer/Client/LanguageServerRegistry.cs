// Registry mapping language identifiers to language server configurations.

namespace Jalium.UI.Controls.Editor.LanguageServer.Client;

/// <summary>
/// Global registry for language server configurations.
/// Maps language identifiers to server launch configurations.
/// </summary>
public static class LanguageServerRegistry
{
    private static readonly object s_syncRoot = new();
    private static readonly Dictionary<string, LanguageServerConfig> s_configs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a language server configuration. The config's <see cref="LanguageServerConfig.Languages"/>
    /// determines which languages are served.
    /// </summary>
    public static void Register(LanguageServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Command))
            throw new ArgumentException("Config must specify a Command.", nameof(config));

        lock (s_syncRoot)
        {
            foreach (var lang in config.Languages)
            {
                if (!string.IsNullOrWhiteSpace(lang))
                    s_configs[lang.Trim().ToLowerInvariant()] = config;
            }
        }
    }

    /// <summary>
    /// Removes the server configuration for a specific language.
    /// </summary>
    public static bool Unregister(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return false;
        lock (s_syncRoot)
        {
            return s_configs.Remove(language.Trim().ToLowerInvariant());
        }
    }

    /// <summary>
    /// Attempts to get the server configuration for the given language.
    /// </summary>
    public static bool TryGetConfig(string language, out LanguageServerConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(language)) return false;
        lock (s_syncRoot)
        {
            return s_configs.TryGetValue(language.Trim().ToLowerInvariant(), out config);
        }
    }

    /// <summary>
    /// Gets all registered language identifiers.
    /// </summary>
    public static string[] GetRegisteredLanguages()
    {
        lock (s_syncRoot)
        {
            return [.. s_configs.Keys];
        }
    }

    internal static void ClearForTesting()
    {
        lock (s_syncRoot)
        {
            s_configs.Clear();
        }
    }
}
