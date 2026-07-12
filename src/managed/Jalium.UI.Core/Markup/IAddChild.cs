namespace Jalium.UI.Markup;

/// <summary>
/// Receives object and text children that are declared directly inside an element in markup.
/// </summary>
public interface IAddChild
{
    /// <summary>
    /// Adds an object child.
    /// </summary>
    void AddChild(object value);

    /// <summary>
    /// Adds literal text content.
    /// </summary>
    void AddText(string text);
}
