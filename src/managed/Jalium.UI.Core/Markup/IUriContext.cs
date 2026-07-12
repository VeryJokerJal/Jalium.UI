namespace Jalium.UI.Markup;

/// <summary>
/// Provides the base URI used to resolve relative resource references.
/// </summary>
public interface IUriContext
{
    /// <summary>Gets or sets the current base URI.</summary>
    Uri? BaseUri { get; set; }
}
