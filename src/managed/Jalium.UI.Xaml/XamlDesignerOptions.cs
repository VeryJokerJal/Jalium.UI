namespace Jalium.UI.Markup;

/// <summary>Project settings used only when building a designer preview tree.</summary>
public sealed class XamlDesignerOptions
{
    public bool EnableCss { get; init; } = true;
    public Uri? CssResourceBaseUri { get; init; }
    /// <summary>Resolves project types within this preview without registering them globally.</summary>
    public Func<string, string, Type?>? TypeResolver { get; init; }
}
