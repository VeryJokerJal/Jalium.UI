using System.Reflection;
using Jalium.UI;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the visual structure and behavior of a Control.
/// </summary>
public class ControlTemplate : FrameworkTemplate
{
    private Type? _targetType;

    /// <summary>
    /// Gets or sets the type for which this template is intended.
    /// </summary>
    public Type? TargetType
    {
        get => _targetType;
        set
        {
            CheckSealed();
            _targetType = value;
        }
    }

    /// <summary>
    /// Gets the collection of triggers.
    /// </summary>
    public TriggerCollection Triggers { get; } = new();

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
        SetVisualTreeFactory(visualTreeFactory);
    }

    /// <summary>
    /// Loads the content of the template.
    /// </summary>
    /// <returns>The root element of the visual tree, or null if no visual tree is defined.</returns>
    public new FrameworkElement? LoadContent() => base.LoadContent() as FrameworkElement;

    /// <inheritdoc />
    protected override Func<string, Assembly?, FrameworkElement?>? DeferredXamlParser => XamlParser;

    /// <inheritdoc />
    protected override void ValidateTemplatedParent(FrameworkElement templatedParent)
    {
        ArgumentNullException.ThrowIfNull(templatedParent);
        if (TargetType is not null && !TargetType.IsInstanceOfType(templatedParent))
        {
            throw new ArgumentException(
                $"A template targeting '{TargetType.Name}' cannot be applied to '{templatedParent.GetType().Name}'.",
                nameof(templatedParent));
        }
    }
}
