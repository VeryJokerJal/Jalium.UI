using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Data;

/// <summary>
/// Describes a collection of Binding objects attached to a single binding target property.
/// </summary>
[ContentProperty("Bindings")]
public class MultiBinding : BindingBase, IAddChild
{
    private readonly Collection<BindingBase> _bindings = new();
    private readonly Collection<ValidationRule> _validationRules = new();

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

    /// <summary>Gets or sets whether validation errors raise the routed validation event.</summary>
    public bool NotifyOnValidationError { get; set; }

    /// <summary>Gets or sets the callback used when a source update throws.</summary>
    public UpdateSourceExceptionFilterCallback? UpdateSourceExceptionFilter { get; set; }

    /// <summary>Gets or sets whether <see cref="IDataErrorInfo"/> participates in validation.</summary>
    public bool ValidatesOnDataErrors { get; set; }

    /// <summary>Gets or sets whether source-update exceptions participate in validation.</summary>
    public bool ValidatesOnExceptions { get; set; }

    /// <summary>Gets or sets whether <see cref="INotifyDataErrorInfo"/> participates in validation.</summary>
    public bool ValidatesOnNotifyDataErrors { get; set; } = true;

    /// <summary>Gets the validation rules applied to the combined target value.</summary>
    public Collection<ValidationRule> ValidationRules => _validationRules;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiBinding"/> class.
    /// </summary>
    public MultiBinding()
    {
    }

    /// <summary>Returns whether the bindings collection contains serializable children.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeBindings() => _bindings.Count > 0;

    /// <summary>Returns whether the validation-rules collection contains serializable children.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeValidationRules() => _validationRules.Count > 0;

    void IAddChild.AddChild(object value)
    {
        if (value is not BindingBase binding)
        {
            throw new ArgumentException("MultiBinding children must derive from BindingBase.", nameof(value));
        }

        _bindings.Add(binding);
    }

    void IAddChild.AddText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("MultiBinding does not accept text children.");
        }
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

    // Converter / ConverterParameter 自身可变状态订阅 —— 与 BindingExpression 同语义：
    // IMultiValueConverter 上挂 INPC 或 DependencyProperty 时，其属性变化必须触发整组
    // child binding 的重算，否则会出现“源数据没变但 Converter 状态切换”而 target 不刷新的问题。
    private INotifyPropertyChanged? _converterNotify;
    private DependencyObject? _converterDepObj;
    private INotifyPropertyChanged? _converterParamNotify;
    private DependencyObject? _converterParamDepObj;

    /// <summary>
    /// Gets the MultiBinding object from which this MultiBindingExpression is created.
    /// </summary>
    public MultiBinding ParentMultiBinding => _multiBinding;

    /// <summary>
    /// Gets the collection of BindingExpression objects in this instance of MultiBindingExpression.
    /// </summary>
    public ReadOnlyCollection<BindingExpressionBase> BindingExpressions => _bindingExpressions.AsReadOnly();

    /// <inheritdoc />
    public override bool HasValidationError =>
        base.HasValidationError || _bindingExpressions.Any(static expression => expression.HasValidationError);

    /// <inheritdoc />
    public override bool HasError =>
        base.HasError || _bindingExpressions.Any(static expression => expression.HasError);

    /// <inheritdoc />
    public override ValidationError? ValidationError =>
        base.ValidationError ?? _bindingExpressions.Select(static expression => expression.ValidationError).FirstOrDefault(static error => error != null);

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiBindingExpression"/> class.
    /// </summary>
    internal MultiBindingExpression(MultiBinding multiBinding, DependencyObject target, DependencyProperty targetProperty)
        : base(multiBinding, target, targetProperty)
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
        AttachToBindingGroup();

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

        // Subscribe to converter/parameter mutations (parity with BindingExpression).
        SubscribeToConverter();

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
        DetachFromBindingGroup();

        // Deactivate and remove child binding expressions
        Target.PropertyChangedInternal -= OnChildBindingChanged;

        UnsubscribeFromConverter();

        foreach (var childExpression in _bindingExpressions)
        {
            childExpression.Deactivate();
        }

        _bindingExpressions.Clear();
    }

    private void SubscribeToConverter()
    {
        UnsubscribeFromConverter();

        var converter = _multiBinding.Converter;
        if (converter != null)
        {
            // DependencyObject 优先：避免极端实现同时挂 DP + INPC 时一次变化触发两次 UpdateTarget。
            if (converter is DependencyObject convDo)
            {
                _converterDepObj = convDo;
                convDo.PropertyChangedInternal += OnConverterDependencyPropertyChanged;
            }
            else if (converter is INotifyPropertyChanged convNpc)
            {
                _converterNotify = convNpc;
                convNpc.PropertyChanged += OnConverterPropertyChanged;
            }
        }

        var parameter = _multiBinding.ConverterParameter;
        if (parameter != null)
        {
            if (parameter is DependencyObject paramDo)
            {
                _converterParamDepObj = paramDo;
                paramDo.PropertyChangedInternal += OnConverterParameterDependencyPropertyChanged;
            }
            else if (parameter is INotifyPropertyChanged paramNpc)
            {
                _converterParamNotify = paramNpc;
                paramNpc.PropertyChanged += OnConverterParameterPropertyChanged;
            }
        }
    }

    private void UnsubscribeFromConverter()
    {
        if (_converterNotify != null)
        {
            _converterNotify.PropertyChanged -= OnConverterPropertyChanged;
            _converterNotify = null;
        }

        if (_converterDepObj != null)
        {
            _converterDepObj.PropertyChangedInternal -= OnConverterDependencyPropertyChanged;
            _converterDepObj = null;
        }

        if (_converterParamNotify != null)
        {
            _converterParamNotify.PropertyChanged -= OnConverterParameterPropertyChanged;
            _converterParamNotify = null;
        }

        if (_converterParamDepObj != null)
        {
            _converterParamDepObj.PropertyChangedInternal -= OnConverterParameterDependencyPropertyChanged;
            _converterParamDepObj = null;
        }
    }

    private void OnConverterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdating || !IsActive) return;
        UpdateTarget();
    }

    private void OnConverterDependencyPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        if (_isUpdating || !IsActive) return;
        UpdateTarget();
    }

    private void OnConverterParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdating || !IsActive) return;
        UpdateTarget();
    }

    private void OnConverterParameterDependencyPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        if (_isUpdating || !IsActive) return;
        UpdateTarget();
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
            object?[]? sourceValues;
            try
            {
                sourceValues = _multiBinding.Converter.ConvertBack(
                    targetValue,
                    targetTypes,
                    _multiBinding.ConverterParameter,
                    _multiBinding.ConverterCulture ?? CultureInfo.CurrentCulture);
            }
            catch (Exception exception)
            {
                Status = BindingStatus.UpdateSourceError;
                object? errorContent = _multiBinding.UpdateSourceExceptionFilter?.Invoke(this, exception)
                    ?? exception.Message;
                if (_multiBinding.ValidatesOnExceptions)
                {
                    ValidationError error = new(null, this, errorContent, exception);
                    AddValidationErrorState(error);
                    Validation.MarkInvalid(this, error);
                }
                return;
            }

            if (sourceValues == null) return;

            // Update each child binding's source
            for (int i = 0; i < _bindingExpressions.Count && i < sourceValues.Length; i++)
            {
                if (_bindingExpressions[i] is BindingExpression be)
                {
                    if (ReferenceEquals(sourceValues[i], DependencyProperty.UnsetValue))
                        continue;

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

            // Child bindings can remain Unattached if they were created before the
            // target entered the visual tree. Retry activation on each target update
            // so DataContext-based sources can attach once ancestors are available.
            foreach (var expression in _bindingExpressions)
            {
                if (!expression.IsActive)
                {
                    expression.Activate();
                }
                else
                {
                    // Keep child expressions synchronized before reading their
                    // shadow values. This also gives composite bindings (e.g.
                    // PriorityBinding) a chance to retry unresolved children.
                    expression.UpdateTarget();
                }
            }

            // Collect values from all child bindings
            var values = new object?[_bindingExpressions.Count];
            var hasUnset = false;
            for (int i = 0; i < _bindingExpressions.Count; i++)
            {
                var shadowProperty = CreateShadowProperty(i);
                values[i] = Target.GetValue(shadowProperty);
                if (ReferenceEquals(values[i], DependencyProperty.UnsetValue))
                    hasUnset = true;
            }

            // Convert values
            object? targetValue;
            if (_multiBinding.Converter != null)
            {
                try
                {
                    targetValue = _multiBinding.Converter.Convert(
                        values,
                        TargetProperty.PropertyType,
                        _multiBinding.ConverterParameter,
                        _multiBinding.ConverterCulture ?? CultureInfo.CurrentCulture);
                }
                catch
                {
                    targetValue = DependencyProperty.UnsetValue;
                }
            }
            else
            {
                // Without a converter, just use the first value
                targetValue = values.Length > 0 ? values[0] : null;
            }

            if (ReferenceEquals(targetValue, DependencyProperty.UnsetValue))
                targetValue = _multiBinding.FallbackValue;

            // Apply StringFormat if specified
            if (!string.IsNullOrEmpty(_multiBinding.StringFormat))
            {
                try
                {
                    var culture = _multiBinding.ConverterCulture ?? CultureInfo.CurrentCulture;
                    // When a converter is used, format the converted result;
                    // otherwise pass all child binding values so that
                    // "{0} - {1}" style formats work as in WPF.
                    if (_multiBinding.Converter != null && targetValue != null)
                    {
                        targetValue = string.Format(culture, _multiBinding.StringFormat, targetValue);
                    }
                    else if (values.Length > 0)
                    {
                        targetValue = string.Format(culture, _multiBinding.StringFormat, values!);
                    }
                }
                catch (FormatException)
                {
                    // Invalid StringFormat — use unconverted value rather than crashing
                }
            }

            if (targetValue == null && _multiBinding.TargetNullValue != null)
                targetValue = _multiBinding.TargetNullValue;

            if (targetValue == null && (hasUnset || _multiBinding.FallbackValue != null))
                targetValue = _multiBinding.FallbackValue;

            targetValue = BindingValueCoercion.Coerce(
                targetValue,
                TargetProperty.PropertyType,
                _multiBinding.ConverterCulture ?? CultureInfo.CurrentCulture);

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
