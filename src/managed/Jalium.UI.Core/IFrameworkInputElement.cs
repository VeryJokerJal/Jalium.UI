namespace Jalium.UI;

/// <summary>
/// Exposes the framework-level contract shared by visual and content input elements.
/// </summary>
public interface IFrameworkInputElement : IInputElement
{
    /// <summary>
    /// Gets or sets the identifying name of the element.
    /// </summary>
    string Name { get; set; }
}
