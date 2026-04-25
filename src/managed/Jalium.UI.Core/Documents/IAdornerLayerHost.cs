namespace Jalium.UI.Documents;

/// <summary>
/// Implemented by elements that expose an <see cref="AdornerLayer"/> for their visual subtree.
/// <see cref="AdornerLayer.GetAdornerLayer(Visual)"/> walks up the visual tree looking for the
/// nearest host. Windows and AdornerDecorators implement this interface so that adorners
/// attached to descendants can be located without requiring an explicit AdornerDecorator
/// wrapper in the user's visual tree.
/// </summary>
public interface IAdornerLayerHost
{
    /// <summary>
    /// Gets the adorner layer associated with this host, or null if none is available.
    /// </summary>
    AdornerLayer? AdornerLayer { get; }
}
