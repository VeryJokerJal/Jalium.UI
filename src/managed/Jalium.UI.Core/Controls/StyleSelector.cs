namespace Jalium.UI.Controls;

/// <summary>
/// Provides a way to apply styles based on custom logic.
/// </summary>
public class StyleSelector
{
    /// <summary>
    /// When overridden in a derived class, returns a style based on custom logic.
    /// </summary>
    public virtual Style? SelectStyle(object item, DependencyObject container)
    {
        return null;
    }
}
