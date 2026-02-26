using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI;

/// <summary>
/// Describes a collection of Binding objects attached to a single binding target property.
/// </summary>
[ContentProperty("Bindings")]
public class MultiBinding : BindingBase
{
    private readonly Collection<BindingBase> _bindings = new();

    /// <summary>
    /// Gets the collection of Binding objects within this MultiBinding instance.
    /// </summary>
    public Collection<BindingBase> Bindings => _bindings;

    /// <summary>
    /// Gets or sets the converter to use to convert the source values to or from the target value.
    /// </summary>
    public IMultiValueConverter? Converter { get; set; }

    /// <summary>
    /// Gets or sets an optional parameter to pass to the converter.
    /// </summary>
    public object? ConverterParameter { get; set; }

    /// <summary>
    /// Gets or sets the CultureInfo object that applies to the converter.
    /// </summary>
    public CultureInfo? ConverterCulture { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates the direction of the data flow in this binding.
    /// </summary>
    public BindingMode Mode { get; set; } = BindingMode.Default;

    /// <summary>
    /// Gets or sets a value that determines the timing of binding source updates.
    /// </summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; } = UpdateSourceTrigger.Default;

    /// <summary>
    /// Gets or sets a value that indicates whether to raise the SourceUpdated event.
    /// </summary>
    public bool NotifyOnSourceUpdated { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether to raise the TargetUpdated event.
    /// </summary>
    public bool NotifyOnTargetUpdated { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiBinding"/> class.
    /// </summary>
    public MultiBinding()
    {
    }

    /// <inheritdoc />
    internal override BindingExpressionBase CreateBindingExpression(DependencyObject target, DependencyProperty targetProperty)
    {
        return new MultiBindingExpression(this, target, targetProperty);
    }
}

/// <summary>
/// Contains instance information about a single instance of a MultiBinding.
/// </summary>
public sealed class MultiBindingExpression : BindingExpressionBase
{
    private readonly MultiBinding _multiBinding;
    private readonly List<BindingExpressionBase> _bindingExpressions = new();
    private bool _isUpdating;

    /// <summary>
    /// Gets the MultiBinding object from which this MultiBindingExpression is created.
    /// </summary>
    public MultiBinding ParentMultiBinding => _multiBinding;

    /// <summary>
    /// Gets the collection of BindingExpression objects in this instance of MultiBindingExpression.
    /// </summary>
    public ReadOnlyCollection<BindingExpressionBase> BindingExpressions => _bindingExpressions.AsReadOnly();

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiBindingExpression"/> class.
    /// </summary>
    internal MultiBindingExpression(MultiBinding multiBinding, DependencyObject target, DependencyProperty targetProperty)
        : base(target, targetProperty)
    {
        _multiBinding = multiBinding;
    }

    /// <inheritdoc />
    internal override void Activate()
    {
        if (IsActive)
            return;

        IsActive = true;
        Status = BindingStatus.Active;

        // Create and activate child binding expressions
        foreach (var binding in _multiBinding.Bindings)
        {
            // Create a shadow property for each child binding
            var shadowProperty = CreateShadowProperty(_bindingExpressions.Count);
            var childExpression = binding.CreateBindingExpression(Target, shadowProperty);
            _bindingExpressions.Add(childExpression);
            childExpression.Activate();
        }

        // Subscribe once (not per child binding) to property changes
        Target.PropertyChangedInternal += OnChildBindingChanged;

        // Initial update
        UpdateTarget();
    }

    /// <inheritdoc />
    internal override void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;
        Status = BindingStatus.Inactive;

        // Deactivate and remove child binding expressions
        Target.PropertyChangedInternal -= OnChildBindingChanged;

        foreach (var childExpression in _bindingExpressions)
        {
            childExpression.Deactivate();
        }

        _bindingExpressions.Clear();
    }

    /// <inheritdoc />
    public override void UpdateSource()
    {
        if (!IsActive || _isUpdating)
            return;

        var mode = GetEffectiveMode();
        if (mode != BindingMode.TwoWay && mode != BindingMode.OneWayToSource)
            return;

        if (_multiBinding.Converter == null)
            return;

        try
        {
            _isUpdating = true;

            var targetValue = Target.GetValue(TargetProperty);

            // Get target types for each child binding
            var targetTypes = new Type[_bindingExpressions.Count];
            for (int i = 0; i < _bindingExpressions.Count; i++)
            {
                if (_bindingExpressions[i] is BindingExpression be && be.ParentBinding.Path != null)
                {
                    targetTypes[i] = typeof(object); // Would need to resolve actual type
                }
                else
                {
                    targetTypes[i] = typeof(object);
                }
            }

            // Convert back to source values
            var sourceValues = _multiBinding.Converter.ConvertBack(
                targetValue,
                targetTypes,
                _multiBinding.ConverterParameter,
                _multiBinding.ConverterCulture ?? CultureInfo.CurrentCulture);

            if (sourceValues == null) return;

            // Update each child binding's source
            for (int i = 0; i < _bindingExpressions.Count && i < sourceValues.Length; i++)
            {
                if (_bindingExpressions[i] is BindingExpression be)
                {
                    // Set value on shadow property, which will trigger source update
                    var shadowProperty = CreateShadowProperty(i);
                    Target.SetValue(shadowProperty, sourceValues[i]);
                    be.UpdateSource();
                }
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <inheritdoc />
    public override void UpdateTarget()
    {
        if (!IsActive || _isUpdating)
            return;

        try
        {
            _isUpdating = true;

            // Collect values from all child bindings
            var values = new object?[_bindingExpressions.Count];
            for (int i = 0; i < _bindingExpressions.Count; i++)
            {
                var shadowProperty = CreateShadowProperty(i);
                values[i] = Target.GetValue(shadowProperty);
            }

            // Convert values
            object? targetValue;
            if (_multiBinding.Converter != null)
            {
                targetValue = _multiBinding.Converter.Convert(
                    values,
                    TargetProperty.PropertyType,
                    _multiBinding.ConverterParameter,
                    _multiBinding.ConverterCulture ?? CultureInfo.CurrentCulture);
            }
            else
            {
                // Without a converter, just use the first value
                targetValue = values.Length > 0 ? values[0] : null;
            }

            // Apply StringFormat if specified
            if (targetValue != null && !string.IsNullOrEmpty(_multiBinding.StringFormat))
            {
                try
                {
                    targetValue = string.Format(_multiBinding.StringFormat, targetValue);
                }
                catch (FormatException)
                {
                    // Invalid StringFormat — use unconverted value rather than crashing
                }
            }

            // Handle FallbackValue and TargetNullValue
            targetValue ??= _multiBinding.TargetNullValue ?? _multiBinding.FallbackValue;

            Target.SetValue(TargetProperty, targetValue);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void OnChildBindingChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        // Check if this is one of our shadow properties
        for (int i = 0; i < _bindingExpressions.Count; i++)
        {
            var shadowProperty = CreateShadowProperty(i);
            if (dp == shadowProperty)
            {
                UpdateTarget();
                return;
            }
        }
    }

    private BindingMode GetEffectiveMode()
    {
        if (_multiBinding.Mode != BindingMode.Default)
            return _multiBinding.Mode;

        return BindingMode.OneWay;
    }

    // Shadow properties are per-instance to avoid collisions between multiple MultiBindings
    private static int s_nextInstanceId;
    private readonly int _instanceId = Interlocked.Increment(ref s_nextInstanceId);
    private static readonly Dictionary<long, DependencyProperty> _shadowProperties = new();
    private static readonly object _shadowPropertyLock = new();

    private DependencyProperty CreateShadowProperty(int index)
    {
        long key = ((long)_instanceId << 32) | (uint)index;
        lock (_shadowPropertyLock)
        {
            if (!_shadowProperties.TryGetValue(key, out var property))
            {
                property = DependencyProperty.RegisterAttached(
                    $"_MultiBindingShadow_{_instanceId}_{index}",
                    typeof(object),
                    typeof(MultiBindingExpression),
                    new PropertyMetadata(null));
                _shadowProperties[key] = property;
            }
            return property;
        }
    }
}
