namespace Jalium.UI.Markup;

/// <summary>Project-specific settings for a designer tree, without changing runtime XAML loading.</summary>
public sealed class XamlDesignerOptions
{
    /// <summary>Whether the edited project's framework implements CSS attached properties.</summary>
    public bool EnableCss { get; init; } = true;

    /// <summary>Root of the edited project's CSS resources; relative stylesheet paths use this URI.</summary>
    public Uri? CssResourceBaseUri { get; init; }
}
