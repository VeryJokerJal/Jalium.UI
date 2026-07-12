using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Markup;

namespace Jalium.UI.Data;

/// <summary>
/// Describes a collection of Binding objects that is attached to a single binding target property,
/// which receives its value from the first binding in the collection that produces a value successfully.
/// </summary>
[ContentProperty("Bindings")]
public class PriorityBinding : BindingBase, IAddChild
{
    private readonly Collection<BindingBase> _bindings = new();

    /// <summary>
    /// Gets the collection of Binding objects within this PriorityBinding instance.
    /// </summary>
    public Collection<BindingBase> Bindings => _bindings;

    /// <summary>
    /// Initializes a new instance of the <see cref="PriorityBinding"/> class.
    /// </summary>
    public PriorityBinding()
    {
    }

    /// <summary>
    /// Returns whether the <see cref="Bindings"/> collection contains values that should be serialized.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeBindings() => _bindings.Count > 0;

    void IAddChild.AddChild(object value)
    {
        if (value is not BindingBase binding)
        {
            throw new ArgumentException("PriorityBinding children must derive from BindingBase.", nameof(value));
        }

        _bindings.Add(binding);
    }

    void IAddChild.AddText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("PriorityBinding does not accept text children.");
        }
    }

    /// <inheritdoc />
    internal override BindingExpressionBase CreateBindingExpression(DependencyObject target, DependencyProperty targetProperty)
    {
        return new PriorityBindingExpression(this, target, targetProperty);
    }
}

/// <summary>
/// Contains instance information about a single instance of a PriorityBinding.
/// </summary>
public sealed class PriorityBindingExpression : BindingExpressionBase
{
    private readonly PriorityBinding _priorityBinding;
    private readonly List<BindingExpressionBase> _bindingExpressions = new();
    private readonly List<DependencyProperty> _shadowProperties = new();
    private int _activeBindingIndex = -1;
    private bool _isUpdating;

    /// <summary>
    /// Gets the PriorityBinding object from which this PriorityBindingExpression is created.
    /// </summary>
    public PriorityBinding ParentPriorityBinding => _priorityBinding;

    /// <summary>
    /// Gets the collection of BindingExpression objects in this instance.
    /// </summary>
    public ReadOnlyCollection<BindingExpressionBase> BindingExpressions => _bindingExpressions.AsReadOnly();

    /// <summary>
    /// Gets the active BindingExpression, which is the one providing the value.
    /// </summary>
    public BindingExpressionBase? ActiveBindingExpression =>
        _activeBindingIndex >= 0 && _activeBindingIndex < _bindingExpressions.Count
            ? _bindingExpressions[_activeBindingIndex]
            : null;

    /// <inheritdoc />
    public override bool HasValidationError => ActiveBindingExpression?.HasValidationError ?? false;

    /// <summary>
    /// Initializes a new instance of the <see cref="PriorityBindingExpression"/> class.
    /// </summary>
    internal PriorityBindingExpression(PriorityBinding priorityBinding, DependencyObject target, DependencyProperty targetProperty)
        : base(priorityBinding, target, targetProperty)
    {
        _priorityBinding = priorityBinding;
    }

    /// <inheritdoc />
    internal override void Activate()
    {
        if (IsActive)
            return;

        IsActive = true;
        Status = BindingStatus.Active;
        AttachToBindingGroup();

        // Create shadow properties and binding expressions for each binding
        for (int i = 0; i < _priorityBinding.Bindings.Count; i++)
        {
            var binding = _priorityBinding.Bindings[i];
            var shadowProperty = CreateShadowProperty(i);
            _shadowProperties.Add(shadowProperty);

            var childExpression = binding.CreateBindingExpression(Target, shadowProperty);
            _bindingExpressions.Add(childExpression);
        }

        // Subscribe to property changes
        Target.PropertyChangedInternal += OnShadowPropertyChanged;

        // Activate all bindings (they'll update their shadow properties)
        foreach (var childExpression in _bindingExpressions)
        {
            childExpression.Activate();
        }

        // Evaluate which binding should be active
        EvaluateBindings();
    }

    /// <inheritdoc />
    internal override void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;
        Status = BindingStatus.Inactive;
        DetachFromBindingGroup();

        Target.PropertyChangedInternal -= OnShadowPropertyChanged;

        foreach (var childExpression in _bindingExpressions)
        {
            childExpression.Deactivate();
        }

        _bindingExpressions.Clear();
        _shadowProperties.Clear();
        _activeBindingIndex = -1;
    }

    /// <inheritdoc />
    public override void UpdateSource()
    {
        // Only update through the active binding
        if (_activeBindingIndex >= 0 && _activeBindingIndex < _bindingExpressions.Count)
        {
            _bindingExpressions[_activeBindingIndex].UpdateSource();
        }
    }

    /// <inheritdoc />
    public override void UpdateTarget()
    {
        EvaluateBindings();
    }

    private void OnShadowPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        // Check if this is one of our shadow properties
        int index = _shadowProperties.IndexOf(dp);
        if (index >= 0)
        {
            // Re-evaluate which binding should be active
            EvaluateBindings();
        }
    }

    private void EvaluateBindings()
    {
        if (_isUpdating)
            return;

        try
        {
            _isUpdating = true;

            // Child bindings may be Unattached when visual ancestry was unavailable
            // during initial activation. Retry here so they can attach later.
            foreach (var expression in _bindingExpressions)
            {
                if (!expression.IsActive)
                {
                    expression.Activate();
                }
            }

            // Find the first binding that has a valid value
            int newActiveIndex = -1;
            object? activeValue = null;

            for (int i = 0; i < _bindingExpressions.Count; i++)
            {
                var shadowProperty = _shadowProperties[i];
                var value = Target.GetValue(shadowProperty);

                // WPF parity: null is a valid value; UnsetValue is not.
                if (!ReferenceEquals(value, DependencyProperty.UnsetValue) && _bindingExpressions[i].IsActive &&
                    _bindingExpressions[i].Status == BindingStatus.Active)
                {
                    newActiveIndex = i;
                    activeValue = value;
                    break;
                }
            }

            // If no binding has a valid value, use FallbackValue
            if (newActiveIndex < 0)
            {
                activeValue = _priorityBinding.FallbackValue;
            }

            // Handle TargetNullValue
            if (activeValue == null && _priorityBinding.TargetNullValue != null)
                activeValue = _priorityBinding.TargetNullValue;

            // Apply StringFormat if specified
            if (activeValue != null && !string.IsNullOrEmpty(_priorityBinding.StringFormat))
            {
                try
                {
                    activeValue = string.Format(
                        CultureInfo.CurrentCulture,
                        _priorityBinding.StringFormat,
                        activeValue);
                }
                catch (FormatException)
                {
                    // Keep unformatted value for invalid format strings.
                }
            }

            _activeBindingIndex = newActiveIndex;
            activeValue = BindingValueCoercion.Coerce(
                activeValue,
                TargetProperty.PropertyType,
                CultureInfo.CurrentCulture);
            Target.SetValue(TargetProperty, activeValue);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    // Cache for shadow properties
    private static readonly Dictionary<int, DependencyProperty> _shadowProperties_cache = new();
    private static readonly object _shadowPropertyLock = new();

    private static DependencyProperty CreateShadowProperty(int index)
    {
        lock (_shadowPropertyLock)
        {
            if (!_shadowProperties_cache.TryGetValue(index, out var property))
            {
                property = DependencyProperty.RegisterAttached(
                    $"_PriorityBindingShadow{index}",
                    typeof(object),
                    typeof(PriorityBindingExpression),
                    new PropertyMetadata(null));
                _shadowProperties_cache[index] = property;
            }
            return property;
        }
    }
}
