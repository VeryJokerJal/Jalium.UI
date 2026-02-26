namespace Jalium.UI;

/// <summary>
/// Provides a way to choose a DataTemplate based on the data object and the data-bound element.
/// </summary>
public class DataTemplateSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataTemplateSelector"/> class.
    /// </summary>
    public DataTemplateSelector()
    {
    }

    /// <summary>
    /// When overridden in a derived class, returns a DataTemplate based on custom logic.
    /// </summary>
    /// <param name="item">The data object for which to select the template.</param>
    /// <param name="container">The data-bound object.</param>
    /// <returns>Returns a DataTemplate or null.</returns>
    public virtual DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        return null;
    }
}

/// <summary>
/// Represents a DataTemplate that supports HeaderedItemsControl, such as TreeViewItem.
/// </summary>
public sealed class HierarchicalDataTemplate : DataTemplate
{
    /// <summary>
    /// Gets or sets the binding to use to get the collection to use for the next level in the data hierarchy.
    /// </summary>
    public BindingBase? ItemsSource { get; set; }

    /// <summary>
    /// Gets or sets the DataTemplate to apply to the ItemTemplate property on a generated HeaderedItemsControl.
    /// </summary>
    public DataTemplate? ItemTemplate { get; set; }

    /// <summary>
    /// Gets or sets the DataTemplateSelector to apply to the ItemTemplateSelector property on a generated HeaderedItemsControl.
    /// </summary>
    public DataTemplateSelector? ItemTemplateSelector { get; set; }

    /// <summary>
    /// Gets or sets the style to apply to the ItemContainerStyle property.
    /// </summary>
    public Style? ItemContainerStyle { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HierarchicalDataTemplate"/> class.
    /// </summary>
    public HierarchicalDataTemplate()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HierarchicalDataTemplate"/> class for the specified type.
    /// </summary>
    /// <param name="dataType">The type for which this template is intended.</param>
    public HierarchicalDataTemplate(Type dataType) : base(dataType)
    {
    }
}

/// <summary>
/// Represents a template for items panels.
/// </summary>
public sealed class ItemsPanelTemplate
{
    private Func<FrameworkElement>? _visualTree;
    private bool _isSealed;

    /// <summary>
    /// Gets a value indicating whether this template is read-only.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <summary>
    /// Gets or sets the raw XAML content for this template.
    /// </summary>
    internal string? VisualTreeXaml { get; set; }

    /// <summary>
    /// Gets or sets the assembly context for parsing the XAML content.
    /// </summary>
    internal System.Reflection.Assembly? SourceAssembly { get; set; }

    /// <summary>
    /// Gets or sets a callback used by LoadContent to parse XAML.
    /// </summary>
    public static Func<string, System.Reflection.Assembly?, FrameworkElement?>? XamlParser { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemsPanelTemplate"/> class.
    /// </summary>
    public ItemsPanelTemplate()
    {
    }

    /// <summary>
    /// Sets the visual tree factory for this template.
    /// </summary>
    /// <param name="visualTreeFactory">A function that creates the visual tree.</param>
    public void SetVisualTree(Func<FrameworkElement> visualTreeFactory)
    {
        if (_isSealed)
            throw new InvalidOperationException("Cannot modify a sealed ItemsPanelTemplate.");

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
    /// Gets or sets the type of panel to create.
    /// When set, <see cref="CreatePanel"/> will instantiate this type directly
    /// without going through the XAML parser.
    /// </summary>
    public Type? PanelType { get; set; }

    /// <summary>
    /// Creates an instance of the panel specified by <see cref="PanelType"/>.
    /// Falls back to <see cref="LoadContent"/> when <see cref="PanelType"/> is not set.
    /// </summary>
    /// <returns>A new panel instance, or null if no panel type or visual tree is configured.</returns>
    public FrameworkElement? CreatePanel()
    {
        if (PanelType != null)
        {
            return Activator.CreateInstance(PanelType) as FrameworkElement;
        }

        // Fall back to LoadContent if no explicit PanelType is set
        return LoadContent();
    }

    /// <summary>
    /// Creates the visual tree defined by this template.
    /// </summary>
    /// <returns>The root element of the visual tree.</returns>
    public FrameworkElement? LoadContent()
    {
        if (_visualTree != null)
        {
            return _visualTree.Invoke();
        }

        if (!string.IsNullOrEmpty(VisualTreeXaml) && XamlParser != null)
        {
            return XamlParser(VisualTreeXaml, SourceAssembly);
        }

        return null;
    }
}
