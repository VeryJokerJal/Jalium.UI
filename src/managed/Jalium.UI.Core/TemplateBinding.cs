namespace Jalium.UI.Data;

/// <summary>
/// A binding that binds to a property on the templated parent.
/// </summary>
public sealed class TemplateBinding : BindingBase
{
    /// <summary>
    /// Gets or sets the property on the templated parent to bind to.
    /// </summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateBinding"/> class.
    /// </summary>
    public TemplateBinding()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateBinding"/> class with the specified property.
    /// </summary>
    /// <param name="property">The property to bind to.</param>
    public TemplateBinding(DependencyProperty property)
    {
        Property = property;
    }

    /// <inheritdoc />
    internal override BindingExpressionBase CreateBindingExpression(DependencyObject target, DependencyProperty targetProperty)
    {
        return new TemplateBindingExpression(this, target, targetProperty);
    }
}

/// <summary>
/// The binding expression for a TemplateBinding.
/// </summary>
internal sealed class TemplateBindingExpression : BindingExpressionBase
{
    private readonly TemplateBinding _binding;
    private FrameworkElement? _templatedParent;

    public TemplateBindingExpression(TemplateBinding binding, DependencyObject target, DependencyProperty targetProperty)
        : base(target, targetProperty)
    {
        _binding = binding;
    }

    internal override void Activate()
    {
        if (IsActive)
            return;

        if (_binding.Property == null)
            return;

        // Find the templated parent
        _templatedParent = FindTemplatedParent(Target);
        if (_templatedParent == null)
            return;

        IsActive = true;

        // Subscribe to property changes on the templated parent
        _templatedParent.PropertyChangedInternal += OnTemplatedParentPropertyChanged;

        // Initial value transfer
        TransferValue();
    }

    internal override void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;

        if (_templatedParent != null)
        {
            _templatedParent.PropertyChangedInternal -= OnTemplatedParentPropertyChanged;
            _templatedParent = null;
        }

        Target.ClearLayerValue(TargetProperty, DependencyObject.LayerValueSource.ParentTemplate);
    }

    public override void UpdateSource()
    {
        // TemplateBinding is OneWay, so UpdateSource does nothing
    }

    public override void UpdateTarget()
    {
        TransferValue();
    }

    private void OnTemplatedParentPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        if (dp == _binding.Property)
        {
            TransferValue();
        }
    }

    private void TransferValue()
    {
        if (_templatedParent == null || _binding.Property == null)
            return;

        var value = _templatedParent.GetValue(_binding.Property);
        Target.SetLayerValue(TargetProperty, value, DependencyObject.LayerValueSource.ParentTemplate);
    }

    private static FrameworkElement? FindTemplatedParent(DependencyObject target)
    {
        if (target is FrameworkElement fe)
        {
            return fe.TemplatedParent;
        }
        return null;
    }
}

/// <summary>
/// Extension methods for working with template bindings.
/// </summary>
public static class TemplateBindingExtensions
{
    /// <summary>
    /// Sets a template binding on a dependency property.
    /// </summary>
    /// <param name="element">The element to set the binding on.</param>
    /// <param name="targetProperty">The property to bind.</param>
    /// <param name="sourceProperty">The property on the templated parent to bind to.</param>
    public static void SetTemplateBinding(this FrameworkElement element, DependencyProperty targetProperty, DependencyProperty sourceProperty)
    {
        var binding = new TemplateBinding(sourceProperty);
        element.SetBinding(targetProperty, binding);
    }
}
