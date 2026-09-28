namespace Jalium.UI.Styling;

/// <summary>Evaluates the conditional tests used by the CSS Values 5 if() function.</summary>
internal static class CssIfCondition
{
    internal static bool Matches(string source, CssNode? element, Func<string, string?> resolve,
        Func<string, bool>? isActive, Action<string>? onQueried = null)
    {
        var expression = CssBooleanQuery.Parse(CssParser.StripComments(source).Trim().ToString());
        return expression?.Evaluate(EvaluateFeature) == CssQueryResult.True;

        CssQueryResult EvaluateFeature(string text)
        {
            var reader = new CssTokenReader(text);
            if (!reader.TryReadFunction(out var name, out var arguments) || !reader.AtEnd)
                return CssQueryResult.Unknown;
            var body = arguments.Remaining.ToString();
            if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
                return CssContainerQuery.EvaluateStyleOnElement(body, element, resolve, isActive, onQueried);
            if (name.Equals("supports", StringComparison.OrdinalIgnoreCase))
            {
                var query = CssSupportsQuery.Parse(body, allowDeclaration: true);
                return query is null ? CssQueryResult.Unknown : query.Evaluate()
                    ? CssQueryResult.True : CssQueryResult.False;
            }
            if (name.Equals("media", StringComparison.OrdinalIgnoreCase) && element is not null)
            {
                if (string.IsNullOrWhiteSpace(body)) return CssQueryResult.Unknown;
                return CssMediaQuery.Parse(body).Evaluate(element) ||
                    CssMediaQuery.Parse("(" + body + ")").Evaluate(element)
                    ? CssQueryResult.True : CssQueryResult.False;
            }
            return CssQueryResult.Unknown;
        }
    }
}
