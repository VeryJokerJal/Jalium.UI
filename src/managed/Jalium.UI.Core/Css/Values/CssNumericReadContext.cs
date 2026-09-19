namespace Jalium.UI.Styling;

// Shared by reader copies and nested function readers during one declaration's
// compilation. Dynamic scalar math is validated now and recomputed per element.
internal sealed class CssNumericReadContext(CssLengthContext? lengths = null, CssPropertyValueContext? registered = null)
{
    internal bool RequiresRuntime { get; private set; }

    internal bool TryEvaluate(CssMathExpression expression, out double value)
    {
        if (registered is not null)
        {
            var normalized = CssPropertySyntax.Normalize(expression, registered);
            value = 0;
            return normalized is not null && normalized.TryEvaluate(registered.Lengths, 100, out value);
        }
        if (lengths is { } context) return expression.TryEvaluate(context, 100, out value);
        if (!expression.RequiresElementContext) return expression.TryEvaluate(CssLengthContext.Default, 100, out value);
        RequiresRuntime = true;
        value = 1;
        return true;
    }
}
