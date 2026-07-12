namespace Jalium.UI.Markup;

/// <summary>Defines registration and lookup within a XAML name scope.</summary>
public interface INameScope
{
    object? FindName(string name);
    void RegisterName(string name, object scopedElement);
    void UnregisterName(string name);
}
