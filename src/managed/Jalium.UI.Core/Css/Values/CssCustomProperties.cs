using System.Text;

namespace Jalium.UI.Styling;

/// <summary>Custom-property dependency analysis and token-preserving var() substitution.</summary>
internal static class CssCustomProperties
{
    private const int MaxDepth = 128;
    private const int MaxExpandedLength = 1_048_576;

    public static bool ContainsVariable(string text)
        => Functions(text).Any(f => f.Name.Equals("var", StringComparison.OrdinalIgnoreCase));

    internal static bool ContainsSubstitution(string text)
        => Functions(text).Any(f => f.Name.Equals("var", StringComparison.OrdinalIgnoreCase) ||
                                    f.Name.Equals("env", StringComparison.OrdinalIgnoreCase) ||
                                    f.Name.Equals("attr", StringComparison.OrdinalIgnoreCase) ||
                                    f.Name.Equals("if", StringComparison.OrdinalIgnoreCase) ||
                                    f.Name.Equals("random-item", StringComparison.OrdinalIgnoreCase) ||
                                    f.Name.Equals("random", StringComparison.OrdinalIgnoreCase) &&
                                    !CssRandomItem.HasFixedKey(f.Arguments));

    private static bool ContainsNumericRandom(string text)
        => Functions(text).Any(f => f.Name.Equals("random", StringComparison.OrdinalIgnoreCase));

    internal static bool HasValidNumericRandomFunctions(string text)
        => text.IndexOf("random", StringComparison.OrdinalIgnoreCase) < 0 ||
            Functions(text).Any(f => IsArbitrarySubstitution(f.Name)) ||
            Functions(text).Where(f => f.Name.Equals("random", StringComparison.OrdinalIgnoreCase))
                .All(f => CssRandomItem.HasValidNumberArguments(f.Arguments));

    internal static bool ContainsUrlFunction(string text)
        => Functions(text).Any(f => f.Name.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                                    f.Name.Equals("src", StringComparison.OrdinalIgnoreCase));

    internal static bool ContainsAttributeFunction(string text)
        => Functions(text).Any(f => f.Name.Equals("attr", StringComparison.OrdinalIgnoreCase));

    internal static bool HasValidAttributeFunctions(string text)
    {
        foreach (var function in Functions(text))
        {
            if (!function.Name.Equals("attr", StringComparison.OrdinalIgnoreCase)) continue;
            var arguments = new CssTokenReader(function.Arguments);
            if (!arguments.TryReadUntilTopLevelComma(out var first) || first.Trim().IsEmpty)
                return false;
        }
        return true;
    }

    internal static bool HasValidEnvironmentFunctions(string text)
        => Functions(text).Where(f => f.Name.Equals("env", StringComparison.OrdinalIgnoreCase))
            .All(f => CssEnvironmentVariables.IsValidFunction(f.Arguments));

    internal static bool HasValidIfArguments(ReadOnlySpan<char> arguments)
        => ContainsTopLevelEarlySpread(arguments.ToString()) || TryParseIfBranches(arguments, out _);

    internal static bool HasValidRandomItemArguments(ReadOnlySpan<char> arguments)
        => ContainsTopLevelEarlySpread(arguments.ToString())
            ? CssRandomItem.HasValidSpreadArguments(arguments)
            : CssRandomItem.TryParseArguments(arguments.ToString(), out _, out _);

    private static bool ContainsTopLevelEarlySpread(string arguments)
    {
        for (var i = 0; i < arguments.Length;)
        {
            if (arguments[i] is '\'' or '"') { i = CssTokenReader.SkipString(arguments, i); continue; }
            if (arguments[i] == '.' && i + 3 < arguments.Length &&
                arguments.AsSpan(i, 3).SequenceEqual("...") &&
                TryFunction(arguments, i + 3, out var spread) && IsArbitrarySubstitution(spread.Name)) return true;
            if (TryFunction(arguments, i, out var function)) i = function.End;
            else i = SkipNonFunctionToken(arguments, i);
        }
        return false;
    }

    private static bool IsArbitrarySubstitution(string name)
        => name.Equals("var", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("if", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("attr", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("env", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("random-item", StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<string> References(string text)
    {
        if (!CssEnvironmentVariables.TrySubstitute(text, null, out var expanded)) yield break;
        foreach (var function in Functions(expanded, traverseIfContents: false))
            if (function.Name.Equals("var", StringComparison.OrdinalIgnoreCase) &&
                TryArguments(function.Arguments, out var first, out _) && TryParseCustomName(first, out var name)) yield return name;
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
        IReadOnlyDictionary<string, string>? inherited, IReadOnlyDictionary<string, string> declarations,
        CssNode? element = null)
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
        var visiting = new List<string>();
        string? Resolve(string name, int depth)
        {
            if (cyclic.Contains(name) || depth >= MaxDepth) return null;
            if (!local.TryGetValue(name, out var value)) return result.GetValueOrDefault(name);
            var cycleStart = visiting.IndexOf(name);
            if (cycleStart >= 0)
            {
                foreach (var member in visiting.Skip(cycleStart)) cyclic.Add(member);
                return null;
            }
            if (!resolved.Add(name)) return result.GetValueOrDefault(name);
            visiting.Add(name);
            try
            {
                if (Substitute(value, dependency => Resolve(dependency, depth + 1), out var expanded, depth,
                        element: element, isActive: visiting.Contains, randomContext: new(name, false)) && !cyclic.Contains(name))
                {
                    var keyword = CssPropertyMetadata.WideKeyword(expanded);
                    if (keyword is "inherit" or "unset")
                    {
                        if (inherited is not null && inherited.TryGetValue(name, out var inheritedValue))
                            return result[name] = inheritedValue;
                        return null;
                    }
                    if (keyword == "initial") return null;
                    result[name] = expanded;
                    return expanded;
                }
            }
            finally { visiting.RemoveAt(visiting.Count - 1); }
            return null;
        }
        foreach (var name in local.Keys) Resolve(name, 0);
        return result;
    }

    internal static HashSet<string> ComputeTainted(IReadOnlySet<string>? inheritedTainted,
        IReadOnlyDictionary<string, string> declarations, IReadOnlyDictionary<string, string> computed,
        CssNode? element = null)
    {
        var tainted = inheritedTainted is null ? new HashSet<string>(StringComparer.Ordinal)
            : inheritedTainted.Where(computed.ContainsKey).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, raw) in declarations)
        {
            var keyword = CssPropertyMetadata.WideKeyword(raw) ?? raw.Trim();
            if (!keyword.Equals("inherit", StringComparison.OrdinalIgnoreCase) &&
                !keyword.Equals("unset", StringComparison.OrdinalIgnoreCase)) tainted.Remove(name);
        }
        for (var pass = 0; pass <= declarations.Count; pass++)
        {
            var changed = false;
            foreach (var (name, raw) in declarations)
            {
                if (tainted.Contains(name) || !computed.ContainsKey(name)) continue;
                var usedTainted = false;
                if (!Substitute(raw, dependency => computed.GetValueOrDefault(dependency), out var expanded,
                        onUsed: dependency => usedTainted |= tainted.Contains(dependency), element: element,
                        onAttributeUsed: () => usedTainted = true, randomContext: new(name, false))) continue;
                var keyword = CssPropertyMetadata.WideKeyword(expanded);
                if (keyword is "inherit" or "unset")
                {
                    if (inheritedTainted?.Contains(name) == true) changed |= tainted.Add(name);
                }
                else if (usedTainted || ContainsAttributeFunction(expanded)) changed |= tainted.Add(name);
            }
            if (!changed) break;
        }
        return tainted;
    }

    public static bool TrySubstitute(string text, IReadOnlyDictionary<string, string>? properties, out string value)
        => Substitute(text, name => properties is not null && properties.TryGetValue(name, out var v) ? v : null,
            out value, 0);

    internal static bool TrySubstitute(string text, CssNode node, out string value, string? property = null)
    {
        var properties = node.CssRuntimeState?.CustomProperties;
        var registrations = node.CssRuntimeState?.RegisteredProperties;
        var tainted = node.CssRuntimeState?.AttrTaintedProperties;
        return Substitute(text, name => properties is not null && properties.TryGetValue(name, out var result) ? result : null,
            out value, validateFallback: (name, fallback) => registrations is null || !registrations.TryGetValue(name, out var registration) ||
                registration.Syntax.TryCompute(fallback, new(CssLengthContext.Default), out _),
            validateFallbackFor: name => registrations?.ContainsKey(name) == true,
            isTainted: name => tainted?.Contains(name) == true, element: node,
            randomContext: new(property));
    }

    internal static bool Substitute(string text, Func<string, string?> resolve, out string value, int depth = 0,
        Func<string, string, bool>? validateFallback = null, Func<string, bool>? validateFallbackFor = null,
        Func<string, bool>? isTainted = null, Action<string>? onUsed = null, bool inUrl = false,
        CssNode? element = null, Action? onAttributeUsed = null, Func<string, bool>? isActive = null,
        CssRandomContext? randomContext = null, bool earlyRandom = false, bool deferNumeric = false)
    {
        value = string.Empty;
        if (depth >= MaxDepth || text.Length > MaxExpandedLength) return false;
        randomContext ??= new(null);
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
                    if (!SubstituteEarly(function.Arguments, resolve, out var earlyArguments, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                            element, onAttributeUsed, isActive, randomContext) ||
                        !TryArguments(earlyArguments, out var first, out var fallback)) return false;
                    string? name = null;
                    var nameHasAttribute = false;
                    var nameUsedTainted = false;
                    void NameUsed(string dependency)
                    {
                        onUsed?.Invoke(dependency);
                        nameUsedTainted |= isTainted?.Invoke(dependency) == true;
                    }
                    if (Substitute(first, resolve, out var expandedName, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, NameUsed, inUrl, element, onAttributeUsed, isActive, randomContext, true))
                    {
                        nameHasAttribute = ContainsAttributeFunction(expandedName);
                        if ((element is null || !nameHasAttribute || CssAttributeSubstitution.TrySubstitute(
                                expandedName, element, CssLengthContext.Default, out expandedName)) &&
                            TryParseCustomName(expandedName, out var parsedName)) name = parsedName;
                    }
                    if (nameHasAttribute && name is not null)
                    {
                        if (inUrl) return false;
                        onAttributeUsed?.Invoke();
                    }
                    if (name is not null && fallback is not null && validateFallback is not null &&
                        validateFallbackFor?.Invoke(name) != false)
                    {
                        // Registered fallback syntax is checked even when the variable exists.
                        // Its random functions must not consume indexes in the used value.
                        var validationRandomContext = new CssRandomContext(randomContext.Property,
                            randomContext.ResolveNumeric);
                        if (!Substitute(fallback, resolve, out var validatedFallback, depth + 1,
                                validateFallback, validateFallbackFor, element: element, isActive: isActive,
                                randomContext: validationRandomContext, earlyRandom: earlyRandom,
                                deferNumeric: deferNumeric) ||
                            !validateFallback(name, validatedFallback)) return false;
                    }
                    var replacement = name is null ? null : resolve(name);
                    if (replacement is null)
                    {
                        if (fallback is null || !Substitute(fallback, resolve, out replacement, depth + 1,
                                validateFallback, validateFallbackFor, isTainted, onUsed, inUrl, element, onAttributeUsed, isActive, randomContext, earlyRandom, deferNumeric)) return false;
                    }
                    else if (name is not null)
                    {
                        onUsed?.Invoke(name);
                        if ((isTainted?.Invoke(name) == true || nameHasAttribute || nameUsedTainted) &&
                            (inUrl || ContainsUrlFunction(replacement))) return false;
                    }
                    if (!deferNumeric && (randomContext.ResolveNumeric || earlyRandom) && ContainsNumericRandom(replacement) &&
                        !Substitute(replacement, resolve, out replacement, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                            element, onAttributeUsed, isActive, randomContext, earlyRandom)) return false;
                    // Preserve token boundaries: var(--number)px must not become a dimension.
                    output.Append("/**/").Append(replacement).Append("/**/");
                }
                else if (function.Name.Equals("attr", StringComparison.OrdinalIgnoreCase))
                {
                    // attr() selects its fallback after reading the attribute. Variables in an
                    // unused fallback must not invalidate the surrounding value.
                    output.Append(text.AsSpan(i, function.End - i));
                }
                else if (function.Name.Equals("env", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CssEnvironmentVariables.TryResolveFunction(function.Arguments, element, out var replacement) ||
                        !Substitute(replacement, resolve, out replacement, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                            element, onAttributeUsed, isActive, randomContext, earlyRandom, deferNumeric)) return false;
                    output.Append("/**/").Append(replacement).Append("/**/");
                }
                else if (function.Name.Equals("if", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryResolveIf(function.Arguments, resolve, out var replacement, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                            element, onAttributeUsed, isActive, randomContext, earlyRandom, deferNumeric)) return false;
                    output.Append("/**/").Append(replacement).Append("/**/");
                }
                else if (function.Name.Equals("random-item", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryResolveRandomItem(function.Arguments, resolve, out var replacement, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                            element, onAttributeUsed, isActive, randomContext, earlyRandom, deferNumeric)) return false;
                    output.Append("/**/").Append(replacement).Append("/**/");
                }
                else if (function.Name.Equals("random", StringComparison.OrdinalIgnoreCase) &&
                         !deferNumeric && (randomContext.ResolveNumeric || earlyRandom))
                {
                    var index = randomContext.ClaimIndex(earlyRandom);
                    if (!Substitute(function.Arguments, resolve, out var arguments, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                            element, onAttributeUsed, isActive, randomContext, earlyRandom) ||
                        !CssRandomItem.TryResolveNumberArguments(arguments, element,
                            randomContext.Property, index, out var resolved, earlyRandom)) return false;
                    output.Append("random(").Append(resolved).Append(')');
                }
                else
                {
                    var url = function.Name.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                              function.Name.Equals("src", StringComparison.OrdinalIgnoreCase);
                    if (!Substitute(function.Arguments, resolve, out var arguments, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl || url,
                            element, onAttributeUsed, isActive, randomContext, earlyRandom, deferNumeric)) return false;
                    output.Append(function.Name).Append('(').Append(arguments).Append(')');
                }
                i = function.End;
            }
            else
            {
                var end = SkipNonFunctionToken(text, i);
                output.Append(text.AsSpan(i, end - i));
                i = end;
            }
            if (output.Length > MaxExpandedLength) return false;
        }
        value = output.ToString();
        return true;
    }

    private static bool TryResolveIf(string arguments, Func<string, string?> resolve, out string value, int depth,
        Func<string, string, bool>? validateFallback, Func<string, bool>? validateFallbackFor,
        Func<string, bool>? isTainted, Action<string>? onUsed, bool inUrl,
        CssNode? element, Action? onAttributeUsed, Func<string, bool>? isActive,
        CssRandomContext randomContext, bool earlyRandom, bool deferNumeric)
    {
        value = string.Empty;
        var conditionTainted = false;
        void ConditionUsed(string dependency)
        {
            onUsed?.Invoke(dependency);
            conditionTainted |= isTainted?.Invoke(dependency) == true;
        }
        void ConditionAttributeUsed()
        {
            onAttributeUsed?.Invoke();
            conditionTainted = true;
        }
        if (!SubstituteEarly(arguments, resolve, out arguments, depth,
                validateFallback, validateFallbackFor, isTainted, ConditionUsed, inUrl,
                element, ConditionAttributeUsed, isActive, randomContext) ||
            !TryParseIfBranches(arguments, out var branches)) return false;
        foreach (var (condition, branchValue) in branches)
        {
            // Conditions are parsed during arbitrary substitution; the chosen value
            // is parsed afterward unless its enclosing substitution is also early.
            if (!Substitute(condition, resolve, out var expandedCondition, depth,
                    validateFallback, validateFallbackFor, isTainted, ConditionUsed, inUrl,
                    element, ConditionAttributeUsed, isActive, randomContext, true)) continue;
            if (ContainsAttributeFunction(expandedCondition)) ConditionAttributeUsed();
            var conditionReader = new CssTokenReader(expandedCondition);
            var otherwise = conditionReader.TryReadIdent(out var keyword) &&
                keyword.Equals("else", StringComparison.OrdinalIgnoreCase) && conditionReader.AtEnd;
            if (!otherwise && !CssIfCondition.Matches(expandedCondition, element, resolve, isActive,
                    ConditionUsed)) continue;
            if (!Substitute(branchValue, resolve, out value, depth,
                    validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                    element, onAttributeUsed, isActive, randomContext, earlyRandom, deferNumeric)) return false;
            return !conditionTainted || !inUrl && !ContainsUrlFunction(value);
        }
        return true;
    }

    private static bool TryResolveRandomItem(string arguments, Func<string, string?> resolve,
        out string value, int depth, Func<string, string, bool>? validateFallback,
        Func<string, bool>? validateFallbackFor, Func<string, bool>? isTainted,
        Action<string>? onUsed, bool inUrl, CssNode? element, Action? onAttributeUsed,
        Func<string, bool>? isActive, CssRandomContext randomContext, bool earlyRandom,
        bool deferNumeric)
    {
        value = string.Empty;
        var index = randomContext.ClaimIndex(earlyRandom);
        var keyTainted = false;
        void KeyUsed(string dependency)
        {
            onUsed?.Invoke(dependency);
            keyTainted |= isTainted?.Invoke(dependency) == true;
        }
        void KeyAttributeUsed()
        {
            onAttributeUsed?.Invoke();
            keyTainted = true;
        }
        if (!SubstituteEarly(arguments, resolve, out arguments, depth,
                validateFallback, validateFallbackFor, isTainted, KeyUsed, inUrl,
                element, KeyAttributeUsed, isActive, randomContext) ||
            !CssRandomItem.TryParseArguments(arguments, out var key, out var options) ||
            !Substitute(key, resolve, out key, depth,
                validateFallback, validateFallbackFor, isTainted, KeyUsed, inUrl,
                element, KeyAttributeUsed, isActive, randomContext, true)) return false;
        if (ContainsAttributeFunction(key))
        {
            if (element is null || !CssAttributeSubstitution.TrySubstitute(key, element,
                    CssLengthContext.Default, out key, inUrl: inUrl)) return false;
            KeyAttributeUsed();
        }
        if (!CssRandomItem.TrySelect(key, options.Count, element,
                randomContext.Property, index, out var selected, earlyRandom) ||
            !Substitute(options[selected], resolve, out value, depth,
                validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                element, onAttributeUsed, isActive, randomContext, earlyRandom, deferNumeric)) return false;
        return !keyTainted || !inUrl && !ContainsUrlFunction(value);
    }

    private static bool SubstituteEarly(string text, Func<string, string?> resolve, out string value, int depth,
        Func<string, string, bool>? validateFallback, Func<string, bool>? validateFallbackFor,
        Func<string, bool>? isTainted, Action<string>? onUsed, bool inUrl,
        CssNode? element, Action? onAttributeUsed, Func<string, bool>? isActive,
        CssRandomContext randomContext)
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
            }
            else if (text[i] == '.' && i + 3 < text.Length &&
                     text.AsSpan(i, 3).SequenceEqual("...") &&
                     TryFunction(text, i + 3, out var spread) && IsArbitrarySubstitution(spread.Name))
            {
                var source = text[(i + 3)..spread.End];
                if (!Substitute(source, resolve, out var replacement, depth + 1,
                        validateFallback, validateFallbackFor, isTainted, onUsed, inUrl,
                        element, onAttributeUsed, isActive, randomContext,
                        earlyRandom: true, deferNumeric: true)) return false;
                if (spread.Name.Equals("attr", StringComparison.OrdinalIgnoreCase))
                {
                    if (element is null || !CssAttributeSubstitution.TrySubstitute(replacement, element,
                            CssLengthContext.Default, out replacement, inUrl: inUrl)) return false;
                    onAttributeUsed?.Invoke();
                }
                output.Append(replacement);
                i = spread.End;
            }
            else if (TryFunction(text, i, out var function))
            {
                if (IsArbitrarySubstitution(function.Name)) output.Append(text.AsSpan(i, function.End - i));
                else
                {
                    var url = function.Name.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                              function.Name.Equals("src", StringComparison.OrdinalIgnoreCase);
                    if (!SubstituteEarly(function.Arguments, resolve, out var arguments, depth + 1,
                            validateFallback, validateFallbackFor, isTainted, onUsed, inUrl || url,
                            element, onAttributeUsed, isActive, randomContext)) return false;
                    output.Append(function.Name).Append('(').Append(arguments).Append(')');
                }
                i = function.End;
            }
            else
            {
                var end = SkipNonFunctionToken(text, i);
                output.Append(text.AsSpan(i, end - i));
                i = end;
            }
            if (output.Length > MaxExpandedLength) return false;
        }
        value = output.ToString();
        return true;
    }

    private static bool TryParseIfBranches(ReadOnlySpan<char> arguments,
        out List<(string Condition, string Value)> branches)
    {
        branches = [];
        var reader = new CssTokenReader(arguments);
        while (!reader.AtEnd)
        {
            if (!reader.TryReadUntilTopLevelDelimiter(';', out var branch) || branch.IsEmpty) return false;
            var branchReader = new CssTokenReader(branch);
            if (!branchReader.TryReadUntilTopLevelDelimiter(':', out var condition) || condition.IsEmpty ||
                !branchReader.TryReadDelimiter(':')) return false;
            var branchValue = branchReader.Remaining.ToString();
            if (!CssDeclarationValueSyntax.IsValid(condition) ||
                !CssDeclarationValueSyntax.IsValid(branchValue)) return false;
            branches.Add((condition.ToString(), branchValue));
            if (reader.AtEnd) break;
            if (!reader.TryReadDelimiter(';')) return false;
        }
        return branches.Count > 0;
    }

    private readonly record struct Function(string Name, string Arguments, int End);

    private static IEnumerable<Function> Functions(string text, int depth = 0, bool traverseIfContents = true)
    {
        if (depth >= MaxDepth) yield break;
        for (var i = 0; i < text.Length;)
        {
            if (text[i] is '\'' or '"') { i = CssTokenReader.SkipString(text, i); continue; }
            if (TryFunction(text, i, out var function))
            {
                yield return function;
                if (traverseIfContents || !function.Name.Equals("if", StringComparison.OrdinalIgnoreCase) &&
                    !function.Name.Equals("random-item", StringComparison.OrdinalIgnoreCase))
                    foreach (var nested in Functions(function.Arguments, depth + 1, traverseIfContents)) yield return nested;
                i = function.End;
            }
            else i = SkipNonFunctionToken(text, i);
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

    internal static int SkipNonFunctionToken(string text, int position)
    {
        var value = text.AsSpan();
        if (value[position] == '/' && position + 1 < value.Length && value[position + 1] == '*')
            return CssTokenReader.SkipComment(value, position);
        if (value[position] is '#' or '@')
        {
            var end = position + 1;
            if (CssSyntax.ReadIdentifier(value, ref end, out _, hash: value[position] == '#')) return end;
        }
        if (char.IsAsciiDigit(value[position]))
        {
            var end = position;
            while (end < value.Length && (char.IsAsciiDigit(value[end]) || value[end] == '.')) end++;
            if (CssSyntax.IsNameStart(value, end)) CssSyntax.ReadIdentifier(value, ref end, out _);
            return end;
        }
        if (CssSyntax.IsNameStart(value, position))
        {
            var end = position;
            if (CssSyntax.ReadIdentifier(value, ref end, out _)) return end;
        }
        return position + 1;
    }

    private static bool TryArguments(string arguments, out string first, out string? fallback)
    {
        var reader = new CssTokenReader(arguments);
        first = string.Empty;
        fallback = null;
        if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty) return false;
        first = segment.ToString();
        if (reader.AtEnd) return true;
        if (!reader.TryReadComma()) return false;
        fallback = reader.Remaining.ToString();
        return true;
    }

    private static bool TryParseCustomName(string text, out string name)
    {
        var reader = new CssTokenReader(text);
        var wrapped = reader.TryReadDelimiter('{');
        name = string.Empty;
        if (!reader.TryReadIdent(out var identifier) || !identifier.StartsWith("--", StringComparison.Ordinal) ||
            identifier.Length <= 2 || wrapped && !reader.TryReadDelimiter('}') || !reader.AtEnd) return false;
        name = identifier.ToString();
        return true;
    }
}

internal sealed class CssCustomPropertyValue(string rawValue, Uri? baseUri = null,
    CssNamespaceContext? namespaces = null) : CssCompiledValue
{
    public string RawValue { get; } = rawValue;
    public Uri? BaseUri { get; } = baseUri;
    public CssNamespaceContext? Namespaces { get; } = namespaces;
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink) => true;
}

/// <summary>Resolves a pending substitution after the cascade, independently for each longhand.</summary>
internal sealed class CssPendingSubstitution(string property, string rawValue, string longhand, CssCompileContext compileContext) : CssCompiledValue
{
    internal string RawValue => rawValue;
    internal CssNamespaceContext? Namespaces => compileContext.Namespaces;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CssCompiledDeclaration[]> _cache = new(StringComparer.Ordinal);
    private readonly CssWideValue _invalid = new(longhand, "unset");
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (TryApplyKeyframe(in context, sink)) return true;
        // In the ordinary cascade an invalid computed value becomes unset.
        return _invalid.TryApply(in context, sink);
    }

    internal bool TryApplyKeyframe(in CssApplyContext context, ICssSetterSink sink)
    {
        var lengths = property.StartsWith("font", StringComparison.Ordinal)
            ? context.Lengths.ForFontProperty()
            : property == "line-height" ? context.Lengths.ForLineHeight() : context.Lengths;
        if (CssCustomProperties.TrySubstitute(rawValue, context.Element, out var value, property) &&
            CssAttributeSubstitution.TrySubstitute(value, context.Element, lengths, out value,
                namespaces: compileContext.Namespaces))
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
                    return declaration.Value is CssNumericDeclarationValue numeric
                        ? numeric.TryApplyKeyframe(in context, sink)
                        : declaration.Value.TryApply(in context, sink);
            }
        }
        return false;
    }
}
