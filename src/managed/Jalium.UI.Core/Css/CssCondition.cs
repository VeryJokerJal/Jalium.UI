namespace Jalium.UI.Styling;

internal sealed record CssCondition(string Kind, string Query, CssCondition? Left = null, CssCondition? Right = null,CssNamespaceContext? Namespaces = null)
{
    private readonly CssSupportsQuery? _supports = Kind == "supports" ? CssSupportsQuery.Parse(Query, allowDeclaration: true,namespaces:Namespaces) : null;
    private readonly CssMediaQuery? _media = Kind == "media" ? CssMediaQuery.Parse(Query) : null;
    internal CssContainerQuery? ContainerQuery { get; init; }

    public bool Evaluate(CssNode element) => Kind switch
    {
        "and" => Left!.Evaluate(element) && Right!.Evaluate(element),
        "supports" => _supports?.Evaluate() == true,
        "media" => _media?.Evaluate(element) == true,
        "container" => (ContainerQuery ?? CssContainerQuery.Parse(Query))?.Evaluate(element) == true,
        _ => false,
    };

    internal bool Supports() => Kind == "supports" && _supports?.Evaluate() == true;
    internal static CssCondition? And(CssCondition? left, CssCondition? right)
        => left is null ? right : right is null ? left : new("and", "", left, right);
}

public static partial class Css
{
    /// <summary>Tests the implemented grammar of a registered CSS property without applying it.</summary>
    public static bool Supports(string property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        property = property.Trim();
        if (property.StartsWith("--", StringComparison.Ordinal))
        {
            var reader = new CssTokenReader(property);
            return reader.TryReadIdent(out var name) && name.Length > 2 && reader.AtEnd && CssDeclarationValue.IsValid(CssParser.StripComments(value).ToString());
        }
        property = property.ToLowerInvariant();
        var descriptor = CssPropertyRegistry.LookupForCompile(property, out _);
        if (descriptor is null || descriptor.IsFallback || descriptor.Kind == CssPropertyKind.Unsupported) return false;
        return CssEngine.CompileDeclarations([new CssDeclaration { PropertyName = property, RawValue = value }]).Length > 0;
    }
}
