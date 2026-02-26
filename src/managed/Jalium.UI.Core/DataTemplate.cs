namespace Jalium.UI;

/// <summary>
/// Defines a template for data display.
/// </summary>
public class DataTemplate
{
    private Func<FrameworkElement>? _visualTree;
    private bool _isSealed;

    /// <summary>
    /// Gets or sets the type of data for which this template is intended.
    /// </summary>
    public Type? DataType { get; set; }

    /// <summary>
    /// Gets a value indicating whether this template is read-only.
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
    internal System.Reflection.Assembly? SourceAssembly { get; set; }

    /// <summary>
    /// Gets or sets a callback used by LoadContent to parse XAML.
    /// This allows the Controls assembly to remain independent of the Xaml assembly.
    /// </summary>
    public static Func<string, System.Reflection.Assembly?, FrameworkElement?>? XamlParser { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTemplate"/> class.
    /// </summary>
    public DataTemplate()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTemplate"/> class with the specified data type.
    /// </summary>
    /// <param name="dataType">The type of data for which this template is intended.</param>
    public DataTemplate(Type dataType)
    {
        DataType = dataType;
    }

    /// <summary>
    /// Sets the visual tree factory for this template.
    /// </summary>
    /// <param name="visualTreeFactory">A function that creates the visual tree.</param>
    public void SetVisualTree(Func<FrameworkElement> visualTreeFactory)
    {
        if (_isSealed)
            throw new InvalidOperationException("Cannot modify a sealed DataTemplate.");

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
    /// Creates the visual tree defined by this template.
    /// </summary>
    /// <returns>The root element of the visual tree.</returns>
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
    /// Finds a named element in the template content applied to the specified parent.
    /// </summary>
    /// <param name="name">The name of the element to find.</param>
    /// <param name="templatedParent">The element to which this template was applied.</param>
    /// <returns>The named element, or null if not found.</returns>
    public object? FindName(string name, FrameworkElement templatedParent)
    {
        ArgumentNullException.ThrowIfNull(templatedParent);

        if (string.IsNullOrEmpty(name))
            return null;

        // Search the visual tree of the templated parent for a named element
        return FindNameInVisualTree(templatedParent, name);
    }

    private static object? FindNameInVisualTree(Visual root, string name)
    {
        if (root is FrameworkElement fe && fe.Name == name)
            return fe;

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child != null)
            {
                var result = FindNameInVisualTree(child, name);
                if (result != null) return result;
            }
        }

        return null;
    }
}
