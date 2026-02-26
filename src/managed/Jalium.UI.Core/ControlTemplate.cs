using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Specifies the visual structure and behavior of a Control.
/// </summary>
public class ControlTemplate
{
    private Func<FrameworkElement>? _visualTree;
    private bool _isSealed;

    /// <summary>
    /// Gets or sets the type for which this template is intended.
    /// </summary>
    public Type? TargetType { get; set; }

    /// <summary>
    /// Gets the collection of triggers.
    /// </summary>
    public IList<Trigger> Triggers { get; } = new List<Trigger>();

    /// <summary>
    /// Gets a value that indicates whether the template is read-only.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <summary>
    /// Gets or sets the raw XAML content for this template.
    /// This is set by the XAML parser and used by LoadContent() to create the visual tree.
    /// </summary>
    internal string? VisualTreeXaml { get; set; }

    /// <summary>
    /// Gets or sets the assembly context for parsing the XAML content.
    /// </summary>
    internal Assembly? SourceAssembly { get; set; }

    /// <summary>
    /// Gets or sets a callback used by LoadContent to parse XAML.
    /// This allows the Core assembly to remain independent of the Xaml assembly.
    /// </summary>
    public static Func<string, Assembly?, FrameworkElement?>? XamlParser { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlTemplate"/> class.
    /// </summary>
    public ControlTemplate()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlTemplate"/> class with the specified target type.
    /// </summary>
    /// <param name="targetType">The type for which this template is intended.</param>
    public ControlTemplate(Type targetType)
    {
        TargetType = targetType;
    }

    /// <summary>
    /// Sets the visual tree factory for this template.
    /// </summary>
    /// <param name="visualTreeFactory">A function that creates the visual tree.</param>
    public void SetVisualTree(Func<FrameworkElement> visualTreeFactory)
    {
        if (_isSealed)
            throw new InvalidOperationException("Cannot modify a sealed ControlTemplate.");

        _visualTree = visualTreeFactory;
    }

    /// <summary>
    /// Seals the template so that it can no longer be modified.
    /// </summary>
    public void Seal()
    {
        _isSealed = true;
    }

    /// <summary>
    /// Loads the content of the template.
    /// </summary>
    /// <returns>The root element of the visual tree, or null if no visual tree is defined.</returns>
    public FrameworkElement? LoadContent()
    {
        // If we have a factory function, use it
        if (_visualTree != null)
        {
            return _visualTree.Invoke();
        }

        // If we have stored XAML content, parse it
        if (!string.IsNullOrEmpty(VisualTreeXaml) && XamlParser != null)
        {
            return XamlParser(VisualTreeXaml, SourceAssembly);
        }

        return null;
    }

    /// <summary>
    /// Finds an element by name in the template.
    /// </summary>
    /// <param name="name">The name of the element to find.</param>
    /// <param name="templatedParent">The control that the template is applied to.</param>
    /// <returns>The found element, or null if not found.</returns>
    public object? FindName(string name, FrameworkElement templatedParent)
    {
        // Search in the templated parent's visual children for the named element
        // The template content is added as visual children of the templated parent
        return SearchTemplateVisualTreeForName(templatedParent, name);
    }

    /// <summary>
    /// Recursively searches the visual tree for an element with the specified name.
    /// </summary>
    private static FrameworkElement? SearchTemplateVisualTreeForName(Visual? visual, string name)
    {
        if (visual == null) return null;

        // Check children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);

            // Check if this child has the name we're looking for
            if (child is FrameworkElement fe)
            {
                if (fe.Name == name)
                {
                    return fe;
                }

                // Also check if the element has registered named elements (TemplateRoot)
                if (fe.FindName(name) is FrameworkElement found)
                {
                    return found;
                }
            }

            // Recursively search this child's subtree
            var result = SearchTemplateVisualTreeForName(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

/// <summary>
/// Represents the visual template for an item in an ItemsControl.
/// </summary>
public sealed class ItemContainerTemplate : ControlTemplate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerTemplate"/> class.
    /// </summary>
    public ItemContainerTemplate()
    {
    }
}

/// <summary>
/// Used to apply templates to elements.
/// </summary>
public interface ITemplatedControl
{
    /// <summary>
    /// Gets or sets the template that defines the visual appearance.
    /// </summary>
    ControlTemplate? Template { get; set; }

    /// <summary>
    /// Applies the current template.
    /// </summary>
    void ApplyTemplate();
}

/// <summary>
/// Represents an element that has a template-defined visual subtree.
/// </summary>
public sealed class TemplateRoot : FrameworkElement
{
    private readonly Dictionary<string, FrameworkElement> _namedElements = new();

    /// <summary>
    /// Registers a named element in this template root.
    /// </summary>
    public new void RegisterName(string name, FrameworkElement element)
    {
        _namedElements[name] = element;
    }

    /// <summary>
    /// Finds an element by name.
    /// </summary>
    public new FrameworkElement? FindName(string name)
    {
        return _namedElements.GetValueOrDefault(name);
    }
}

/// <summary>
/// Represents a placeholder for the content in a ControlTemplate.
/// </summary>
public sealed class ContentPresenterPlaceholder : FrameworkElement
{
    /// <summary>
    /// Gets or sets the name of the content property to present.
    /// </summary>
    public string? ContentSource { get; set; } = "Content";

    /// <summary>
    /// Gets or sets the name of the content template property.
    /// </summary>
    public string? ContentTemplateSource { get; set; } = "ContentTemplate";
}
