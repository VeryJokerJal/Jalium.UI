using System.Collections.Concurrent;

namespace Jalium.UI.Styling;

internal enum CssPropertyKind : byte
{
    Longhand,
    Shorthand,
    /// <summary>A real CSS property the framework has no capability for. Blocks the kebab fallback.</summary>
    Unsupported,
}

internal delegate CssCompiledValue? CssParseValueDelegate(ref CssTokenReader reader, CssCompileContext context);

internal delegate bool CssExpandDelegate(ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output);

internal sealed class CssPropertyDescriptor
{
    public required string Name { get; init; }
    public required CssPropertyKind Kind { get; init; }
    public CssParseValueDelegate? Parse { get; init; }
    public CssExpandDelegate? Expand { get; init; }
    public string? UnsupportedReason { get; init; }
    public string? TransitionTargetDpName { get; init; }
}

/// <summary>
/// The CSS property table. Explicit entries use native CSS keywords verbatim; names not found
/// there fall back to kebab-case → PascalCase dependency-property resolution. All entries are
/// registered explicitly (AOT/trim safe — no reflection scanning).
/// </summary>
internal static class CssPropertyRegistry
{
    private static readonly ConcurrentDictionary<string, CssPropertyDescriptor> s_table =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, CssPropertyDescriptor?> s_fallbackCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Application-registered entries and aliases (CssMappings): consulted before the
    // built-in table, so later registrations win and may override Unsupported placeholders.
    private static readonly ConcurrentDictionary<string, CssPropertyDescriptor> s_userTable =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string> s_aliases =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bumped on every public registration; compiled-declaration caches key on it.</summary>
    internal static int Version;

    private const int MaxAliasDepth = 8;

    private static readonly List<Action> s_extensions = new();
    private static bool s_initialized;
    private static readonly object s_initLock = new();

    public static void Register(CssPropertyDescriptor descriptor)
        => s_table[descriptor.Name] = descriptor;

    /// <summary>
    /// Registers a deferred table-extension hook (e.g. the Controls-level entries). Hooks run on
    /// first lookup, keeping module initializers free of cross-type work.
    /// </summary>
    public static void RegisterExtension(Action registrar)
    {
        lock (s_initLock)
        {
            s_extensions.Add(registrar);
            if (s_initialized)
            {
                registrar();
            }
        }
    }

    public static CssPropertyDescriptor? Lookup(string cssName)
        => LookupForCompile(cssName, out _);

    /// <summary>
    /// Compile-time lookup with the full precedence chain: user registrations &gt; aliases
    /// (followed transitively) &gt; built-ins &gt; kebab fallback. canonicalName is the
    /// alias-resolved name — cascade merging keys on it so an alias competes with its
    /// target as the same longhand.
    /// </summary>
    internal static CssPropertyDescriptor? LookupForCompile(string cssName, out string canonicalName)
    {
        EnsureInitialized();
        canonicalName = cssName;
        for (var depth = 0; depth <= MaxAliasDepth; depth++)
        {
            if (s_userTable.TryGetValue(canonicalName, out var user))
            {
                return user;
            }

            if (s_aliases.TryGetValue(canonicalName, out var target))
            {
                canonicalName = target;
                continue;
            }

            if (s_table.TryGetValue(canonicalName, out var descriptor))
            {
                return descriptor;
            }

            return s_fallbackCache.GetOrAdd(canonicalName, static name => CreateFallback(name));
        }

        CssDiagnostics.Report(
            cssName, CssDiagnosticReason.UnknownProperty, null,
            "alias chain exceeded the depth limit (likely a cycle); declaration skipped");
        return null;
    }

    internal static void RegisterUser(CssPropertyDescriptor descriptor)
    {
        s_aliases.TryRemove(descriptor.Name, out _);
        s_userTable[descriptor.Name] = descriptor;
        Interlocked.Increment(ref Version);
        CssEngine.OnMappingsChanged();
    }

    internal static void RegisterUserAlias(string cssName, string targetCssName)
    {
        // Cycle detection over what is registered so far (the parse-time depth limit backstops).
        var cursor = targetCssName;
        for (var depth = 0; depth < MaxAliasDepth; depth++)
        {
            if (string.Equals(cursor, cssName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Alias '{cssName}' → '{targetCssName}' would create a cycle.", nameof(targetCssName));
            }

            if (!s_aliases.TryGetValue(cursor, out var next))
            {
                break;
            }

            cursor = next;
        }

        s_userTable.TryRemove(cssName, out _);
        s_aliases[cssName] = targetCssName;
        Interlocked.Increment(ref Version);
        CssEngine.OnMappingsChanged();
    }

    private static void EnsureInitialized()
    {
        if (s_initialized)
        {
            return;
        }

        lock (s_initLock)
        {
            if (s_initialized)
            {
                return;
            }

            CssCoreProperties.RegisterAll();
            foreach (var extension in s_extensions)
            {
                extension();
            }

            s_initialized = true;
        }
    }

    private static CssPropertyDescriptor? CreateFallback(string cssName)
    {
        if (!CssKebabCase.TryToPascal(cssName, out var pascalName))
        {
            return null;
        }

        return new CssPropertyDescriptor
        {
            Name = cssName,
            Kind = CssPropertyKind.Longhand,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var raw = reader.Remaining.ToString();
                return raw.Length == 0 ? null : new CssFallbackValue(cssName, pascalName, raw);
            },
        };
    }

    internal static void ResetForTests()
    {
        lock (s_initLock)
        {
            s_table.Clear();
            s_fallbackCache.Clear();
            s_userTable.Clear();
            s_aliases.Clear();
            Interlocked.Increment(ref Version);
            s_initialized = false;
        }

        CssEngine.OnMappingsChanged();
    }
}
