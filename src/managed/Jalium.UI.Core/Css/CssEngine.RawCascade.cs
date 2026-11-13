namespace Jalium.UI.Styling;

/// <summary>A token value for a nonvisual consumer; cascade ordering stays shared with live CSS.</summary>
internal sealed class CssRawCascadeValue(string text, CssNamespaceContext? namespaces = null, string? shorthand = null) : CssCompiledValue
{
    internal string Text { get; } = text;
    internal string? Shorthand { get; } = shorthand;
    internal CssNamespaceContext? Namespaces { get; } = namespaces;
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink) => false;
}

internal static partial class CssEngine
{
    internal static Dictionary<string, string> ComputeRawCascade(CssXmlElement target,
        IReadOnlyList<CssStyleSheet> sheets, IReadOnlySet<string> properties,
        IEnumerable<CssDeclaration> presentation, IEnumerable<CssDeclaration> inline,
        Func<string, string, bool> valid, IReadOnlyDictionary<string, string>? inheritedCustom,
        IReadOnlyDictionary<string, string[]>? shorthands = null,
        Func<CssXmlElement, string, string, IReadOnlyDictionary<string, string>?>? expandShorthand = null)
    {
        var node = CssNode.Get(target);
        var state = node.CssRuntimeState ??= new();
        var groups = new Dictionary<string, List<CascadeCandidate>>(StringComparer.Ordinal);
        var ruleId = 0;
        void Add(IEnumerable<CssDeclaration> declarations, int specificity, long order, int[] layer,
            bool inlineRule = false, bool hint = false, int proximity = int.MaxValue, CssNamespaceContext? namespaces = null)
        {
            var id = ruleId++; var index = 0;
            foreach (var declaration in declarations)
            {
                var name = declaration.PropertyName;
                var value = declaration.RawValue;
                var shorthand = shorthands?.ContainsKey(name) == true;
                var custom = name.StartsWith("--", StringComparison.Ordinal);
                if (!custom && name != "all" && !properties.Contains(name) && !shorthand) continue;
                if (name == "all" && CssPropertyMetadata.WideKeyword(value) is null) continue;
                if (!custom && !CssCustomProperties.ContainsSubstitution(value) &&
                    CssPropertyMetadata.WideKeyword(value) is null && !valid(name, value)) continue;
                CssCompiledValue compiled = CssPropertyMetadata.WideKeyword(value) is { } keyword &&
                    keyword.StartsWith("revert", StringComparison.Ordinal) ? CssRevertValue.FromKeyword(keyword)
                    : custom ? new CssCustomPropertyValue(value, namespaces: namespaces) : new CssRawCascadeValue(value, namespaces, shorthand ? name : null);
                foreach (var property in name == "all" ? properties.Where(p => p is not ("direction" or "unicode-bidi")) : shorthand ? shorthands![name] : [name])
                {
                    if (!groups.TryGetValue(property, out var candidates)) groups[property] = candidates = [];
                    candidates.Add(new(new(property, compiled, declaration.Important && !hint, index), false, 0,
                        specificity, order, layer, inlineRule, id, proximity, hint));
                }
                index++;
            }
        }
        // Presentation hints precede author sheets, including every named layer.
        Add(presentation, 0, long.MinValue, [-1], hint: true);
        List<MatchedRule>? matches = null; var scope = default(ScopeInfo);
        MatchSheets(node, sheets, 0, ref matches, ref scope, CssMatcher.Root(node), snapshot: true);
        if (matches is not null)
            foreach (var match in matches)
                if (!match.Rule.StartingStyle)
                    Add(match.Rule.Declarations, match.Specificity, match.OrderKey, match.LayerKey, proximity: match.Proximity, namespaces: match.Rule.Namespaces);
        Add(inline, int.MaxValue, long.MaxValue, [], inlineRule: true);

        var registrations = new Dictionary<string, CssPropertyRegistration>(StringComparer.Ordinal);
        foreach (var sheet in sheets)
            foreach (var registration in sheet.Properties)
                if (registration.Condition is null || registration.Condition.Evaluate(node)) registrations[registration.Name] = registration;
        state.RegisteredProperties = registrations;
        CssCompiledValue? OrdinaryValue(string property)
        {
            if (!groups.TryGetValue(property, out var candidates) || SelectCandidate(candidates, node)?.Declaration.Value is not CssRawCascadeValue raw) return null;
            var source = raw.Shorthand ?? property; var text = raw.Text;
            if (property == "font-size" && CssPropertyMetadata.WideKeyword(text) is { } keyword)
            {
                source = "font-size";
                text = (keyword == "initial" ? target.InitialFontSize : target.Lengths.InheritedFontSize).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "px";
            }
            return CompileDeclarations([new CssDeclaration { PropertyName = source, RawValue = text }],
                new CssCompileContext { NumericLengths = target.Lengths, InitialFontSize = target.InitialFontSize }).FirstOrDefault(value => value.Name == property).Value;
        }
        var selectedCustom = new Dictionary<string, CssCustomPropertyValue>(StringComparer.Ordinal);
        void ComputeVariables(bool substituteReverts)
        {
            var selected = new Dictionary<string, CssCustomPropertyValue>(StringComparer.Ordinal);
            foreach (var (name, candidates) in groups)
                if (name.StartsWith("--", StringComparison.Ordinal) &&
                    SelectCandidate(candidates, node, substituteReverts, selectedCustom, registrations)?.Declaration.Value is CssCustomPropertyValue value)
                    selected[name] = value;
            selectedCustom = selected;
            if (registrations.Count > 0)
            {
                var computation = new CssRegisteredComputation(node, inheritedCustom, null, selected, registrations,
                    target.Lengths, OrdinaryValue("font-size"), OrdinaryValue("color"));
                state.CustomProperties = computation; state.CustomProperties = computation.Compute();
                state.RegisteredValues = computation.TypedValues; state.AttrTaintedProperties = computation.AttrTaintedProperties;
            }
            else state.CustomProperties = CssCustomProperties.Compute(inheritedCustom,
                selected.ToDictionary(pair => pair.Key, pair => pair.Value.RawValue, StringComparer.Ordinal), node);
        }
        ComputeVariables(false);
        for (var pass = 0; pass < 8; pass++)
        {
            var before = selectedCustom;
            ComputeVariables(true);
            if (before.Count == selectedCustom.Count && before.All(pair =>
                selectedCustom.TryGetValue(pair.Key, out var value) && ReferenceEquals(pair.Value, value))) break;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, candidates) in groups)
        {
            if (name.StartsWith("--", StringComparison.Ordinal)) continue;
            if (SelectCandidate(candidates, node, resolveVariables: true, selectedCustom)?.Declaration.Value is not CssRawCascadeValue value) continue;
            var raw = value.Text;
            if (!CssCustomProperties.TrySubstitute(raw, node, out raw, name) ||
                !CssAttributeSubstitution.TrySubstitute(raw, node, target.Lengths, out raw, namespaces: value.Namespaces)) raw = "unset";
            // Substitution inserts comment separators to preserve token boundaries.
            // Ordinary consumers parse tokens; never compare those separators as text.
            raw = CssParser.StripComments(raw).ToString().Trim();
            if (CssPropertyMetadata.WideKeyword(raw) is null && !valid(value.Shorthand ?? name, raw)) raw = "unset";
            if (value.Shorthand is { } shorthand && CssPropertyMetadata.WideKeyword(raw) is null)
                raw = expandShorthand?.Invoke(target, shorthand, raw)?.GetValueOrDefault(name) ?? "unset";
            result[name] = raw;
        }
        return result;
    }
}
