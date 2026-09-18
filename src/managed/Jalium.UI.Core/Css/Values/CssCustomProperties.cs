using System.Text;

namespace Jalium.UI.Styling;

/// <summary>Custom-property dependency analysis and token-preserving var() substitution.</summary>
internal static class CssCustomProperties
{
    private const int MaxDepth = 128;
    private const int MaxExpandedLength = 1_048_576;

    public static bool ContainsVariable(string text)
        => Functions(text).Any(f => f.Name.Equals("var", StringComparison.OrdinalIgnoreCase));

    internal static IEnumerable<string> References(string text)
    {
        foreach (var function in Functions(text))
            if (function.Name.Equals("var", StringComparison.OrdinalIgnoreCase) && TryArguments(function.Arguments, out var name, out _)) yield return name;
    }

    internal static HashSet<string> CyclicNames(IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>(); var active = new HashSet<string>(StringComparer.Ordinal);
        var next = 0;
        void Visit(string name, int depth)
        {
            index[name] = low[name] = next++;
            if (depth >= MaxDepth) { cyclic.Add(name); return; }
            stack.Push(name); active.Add(name);
            foreach (var dependency in edges[name])
            {
                if (!edges.ContainsKey(dependency)) continue;
                if (!index.ContainsKey(dependency)) { Visit(dependency, depth + 1); low[name] = Math.Min(low[name], low[dependency]); }
                else if (active.Contains(dependency)) low[name] = Math.Min(low[name], index[dependency]);
            }
            if (low[name] != index[name]) return;
            var component = new List<string>();
            string member;
            do { member = stack.Pop(); active.Remove(member); component.Add(member); } while (member != name);
            if (component.Count > 1 || edges[name].Contains(name)) cyclic.UnionWith(component);
        }
        foreach (var name in edges.Keys) if (!index.ContainsKey(name)) Visit(name, 0);
        return cyclic;
    }

    public static IReadOnlyDictionary<string, string> Compute(
        IReadOnlyDictionary<string, string>? inherited, IReadOnlyDictionary<string, string> declarations)
    {
        var result = inherited is null ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(inherited, StringComparer.Ordinal);
        var local = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in declarations)
        {
            var keyword = CssPropertyMetadata.WideKeyword(value) ?? value.Trim();
            if (keyword.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
                keyword.Equals("unset", StringComparison.OrdinalIgnoreCase)) continue;
            result.Remove(name);
            if (!keyword.Equals("initial", StringComparison.OrdinalIgnoreCase)) local[name] = value;
        }

        // All members of a strongly connected component are invalid, including branches
        // reached through a previously visited node and references in unused fallbacks.
        var cyclic = CyclicNames(local.ToDictionary(pair => pair.Key, pair => References(pair.Value).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal));

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        string? Resolve(string name, int depth)
        {
            if (cyclic.Contains(name) || depth >= MaxDepth) return null;
            if (!local.TryGetValue(name, out var value)) return result.GetValueOrDefault(name);
            if (!resolved.Add(name)) return result.GetValueOrDefault(name);
            if (Substitute(value, dependency => Resolve(dependency, depth + 1), out var expanded, depth))
            {
                result[name] = expanded;
                return expanded;
            }
            return null;
        }
        foreach (var name in local.Keys) Resolve(name, 0);
        return result;
    }

    public static bool TrySubstitute(string text, IReadOnlyDictionary<string, string>? properties, out string value)
        => Substitute(text, name => properties is not null && properties.TryGetValue(name, out var v) ? v : null,
            out value, 0);

    internal static bool TrySubstitute(string text, CssNode node, out string value)
    {
        var properties = node.CssRuntimeState?.CustomProperties;
        var registrations = node.CssRuntimeState?.RegisteredProperties;
        return Substitute(text, name => properties is not null && properties.TryGetValue(name, out var result) ? result : null,
            out value, validateFallback: (name, fallback) => registrations is null || !registrations.TryGetValue(name, out var registration) ||
                registration.Syntax.TryCompute(fallback, new(CssLengthContext.Default), out _),
            validateFallbackFor: name => registrations?.ContainsKey(name) == true);
    }

    internal static bool Substitute(string text, Func<string, string?> resolve, out string value, int depth = 0,
        Func<string, string, bool>? validateFallback = null, Func<string, bool>? validateFallbackFor = null)
    {
        value = string.Empty;
        if (depth >= MaxDepth || text.Length > MaxExpandedLength) return false;
        var output = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (text[i] is '\'' or '"')
            {
                var end = CssTokenReader.SkipString(text, i);
                output.Append(text.AsSpan(i, end - i));
                i = end;
                continue;
            }
            if (TryFunction(text, i, out var function))
            {
                if (function.Name.Equals("var", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryArguments(function.Arguments, out var name, out var fallback)) return false;
                    var replacement = resolve(name);
                    if (fallback is not null && validateFallback is not null && validateFallbackFor?.Invoke(name) != false)
                    {
                        if (!Substitute(fallback, resolve, out fallback, depth + 1, validateFallback, validateFallbackFor) || !validateFallback(name, fallback)) return false;
                    }
                    if (replacement is null && (fallback is null ||
                        !Substitute(fallback, resolve, out replacement, depth + 1, validateFallback, validateFallbackFor))) return false;
                    // Preserve token boundaries: var(--number)px must not become a dimension.
                    output.Append("/**/").Append(replacement).Append("/**/");
                }
                else
                {
                    if (!Substitute(function.Arguments, resolve, out var arguments, depth + 1, validateFallback, validateFallbackFor)) return false;
                    output.Append(function.Name).Append('(').Append(arguments).Append(')');
                }
                i = function.End;
            }
            else output.Append(text[i++]);
            if (output.Length > MaxExpandedLength) return false;
        }
        value = output.ToString();
        return true;
    }

    private readonly record struct Function(string Name, string Arguments, int End);

    private static IEnumerable<Function> Functions(string text, int depth = 0)
    {
        if (depth >= MaxDepth) yield break;
        for (var i = 0; i < text.Length;)
        {
            if (text[i] is '\'' or '"') { i = CssTokenReader.SkipString(text, i); continue; }
            if (TryFunction(text, i, out var function))
            {
                yield return function;
                foreach (var nested in Functions(function.Arguments, depth + 1)) yield return nested;
                i = function.End;
            }
            else i++;
        }
    }

    private static bool TryFunction(string text, int start, out Function function)
    {
        function = default;
        if (!CssSyntax.IsNameStart(text, start)) return false;
        var reader = new CssTokenReader(text.AsSpan(start));
        if (!reader.TryReadFunction(out var name, out var args)) return false;
        var consumed = reader.Position;
        function = new Function(name.ToString(), args.Remaining.ToString(), start + consumed);
        return true;
    }

    private static bool TryArguments(string arguments, out string name, out string? fallback)
    {
        var reader = new CssTokenReader(arguments);
        name = string.Empty;
        fallback = null;
        if (!reader.TryReadIdent(out var identifier) || !identifier.StartsWith("--", StringComparison.Ordinal) ||
            identifier.Length <= 2) return false;
        name = identifier.ToString();
        if (reader.AtEnd) return true;
        if (!reader.TryReadComma()) return false;
        fallback = reader.Remaining.ToString();
        return true;
    }
}

internal sealed class CssCustomPropertyValue(string rawValue, Uri? baseUri = null) : CssCompiledValue
{
    public string RawValue { get; } = rawValue;
    public Uri? BaseUri { get; } = baseUri;
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink) => true;
}

/// <summary>Resolves a pending substitution after the cascade, independently for each longhand.</summary>
internal sealed class CssPendingSubstitution(string property, string rawValue, string longhand, CssCompileContext compileContext) : CssCompiledValue
{
    internal string RawValue => rawValue;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CssCompiledDeclaration[]> _cache = new(StringComparer.Ordinal);
    private readonly CssWideValue _invalid = new(longhand, "unset");
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (CssCustomProperties.TrySubstitute(rawValue, context.Element, out var value))
        {
            if (!_cache.TryGetValue(value, out var compiled))
            {
                compiled = CssEngine.CompileDeclarations(CssParser.ParseInlineDeclarations(property + ":" + value, null), compileContext);
                if (_cache.Count >= 128) _cache.Clear();
                _cache[value] = compiled;
            }
            foreach (var declaration in compiled)
            {
                if (declaration.Name == longhand && declaration.Value is not CssPendingSubstitution)
                    return declaration.Value.TryApply(in context, sink);
            }
        }
        // Invalid at computed-value time: do not resurrect an earlier declaration.
        return _invalid.TryApply(in context, sink);
    }
}
