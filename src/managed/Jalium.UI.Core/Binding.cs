using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Data;

/// <summary>
/// Specifies the direction of data flow in a binding.
/// </summary>
public enum BindingMode
{
    /// <summary>
    /// Binding mode is automatically chosen based on the target property.
    /// </summary>
    Default,

    /// <summary>
    /// Updates the target when the source changes.
    /// </summary>
    OneWay,

    /// <summary>
    /// Updates both target and source when either changes.
    /// </summary>
    TwoWay,

    /// <summary>
    /// Updates the target only once when the binding is created.
    /// </summary>
    OneTime,

    /// <summary>
    /// Updates the source when the target changes.
    /// </summary>
    OneWayToSource
}

/// <summary>
/// Specifies when the binding source is updated.
/// </summary>
public enum UpdateSourceTrigger
{
    /// <summary>
    /// Default update trigger for the property.
    /// </summary>
    Default,

    /// <summary>
    /// Updates the source whenever the target property value changes.
    /// </summary>
    PropertyChanged,

    /// <summary>
    /// Updates the source when the target element loses focus.
    /// </summary>
    LostFocus,

    /// <summary>
    /// Updates the source only when you call UpdateSource explicitly.
    /// </summary>
    Explicit
}

/// <summary>
/// Provides an abstract base class for value conversion.
/// </summary>
public interface IValueConverter
{
    /// <summary>
    /// Converts a value from the source to the target type.
    /// </summary>
    /// <param name="value">The value produced by the binding source.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>A converted value.</returns>
    object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture);

    /// <summary>
    /// Converts a value from the target back to the source type.
    /// </summary>
    /// <param name="value">The value that is produced by the binding target.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>A converted value.</returns>
    object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture);
}

/// <summary>
/// Provides a way to apply custom logic to a multi-binding.
/// </summary>
public interface IMultiValueConverter
{
    /// <summary>
    /// Converts source values to a value for the binding target.
    /// </summary>
    /// <param name="values">The array of values that the source bindings in the MultiBinding produces.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>A converted value.</returns>
    object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture);

    /// <summary>
    /// Converts a binding target value to the source binding values.
    /// </summary>
    /// <param name="value">The value that the binding target produces.</param>
    /// <param name="targetTypes">The array of types to convert to.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>An array of values that have been converted from the target value back to the source values.</returns>
    object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture);
}

/// <summary>
/// Abstract base class for binding classes.
/// </summary>
public abstract class BindingBase
{
    /// <summary>
    /// Gets or sets the value to use when the binding cannot return a value.
    /// </summary>
    public object? FallbackValue { get; set; }

    /// <summary>
    /// Gets or sets the value to use when the source value is null.
    /// </summary>
    public object? TargetNullValue { get; set; }

    /// <summary>
    /// Gets or sets a string that specifies how to format the binding if it displays the bound value as a string.
    /// </summary>
    public string? StringFormat { get; set; }

    /// <summary>
    /// Gets or sets the delay (in milliseconds) before updating the source.
    /// </summary>
    public int Delay { get; set; }

    /// <summary>
    /// Creates a new binding expression for this binding.
    /// </summary>
    internal abstract BindingExpressionBase CreateBindingExpression(DependencyObject target, DependencyProperty targetProperty);
}

/// <summary>
/// Describes the binding between a binding target and a binding source.
/// </summary>
public class Binding : BindingBase
{
    /// <summary>
    /// Used as a returned value to indicate that the binding engine should not perform any action.
    /// </summary>
    public static readonly object DoNothing = new DoNothingMarker();

    /// <summary>
    /// Used as a returned value to indicate that the binding engine should use the FallbackValue or default value.
    /// </summary>
    public static readonly object UnsetValue = DependencyProperty.UnsetValue;

    private sealed class DoNothingMarker
    {
        public override string ToString() => "{Binding.DoNothing}";
    }

    /// <summary>
    /// Occurs when a value is transferred from the binding source to the binding target.
    /// </summary>
    public static readonly RoutedEvent TargetUpdatedEvent =
        new RoutedEvent("TargetUpdated", RoutingStrategy.Bubble, typeof(EventHandler<DataTransferEventArgs>), typeof(Binding));

    /// <summary>
    /// Occurs when a value is transferred from the binding target to the binding source.
    /// </summary>
    public static readonly RoutedEvent SourceUpdatedEvent =
        new RoutedEvent("SourceUpdated", RoutingStrategy.Bubble, typeof(EventHandler<DataTransferEventArgs>), typeof(Binding));

    /// <summary>
    /// Gets or sets the path to the binding source property.
    /// </summary>
    public PropertyPath? Path { get; set; }

    /// <summary>
    /// Gets or sets the source object to use for the binding.
    /// </summary>
    public object? Source { get; set; }

    /// <summary>
    /// Gets or sets the binding source by specifying its location relative to the position of the binding target.
    /// </summary>
    public RelativeSource? RelativeSource { get; set; }

    /// <summary>
    /// Gets or sets the name of the element to use as the binding source.
    /// </summary>
    public string? ElementName { get; set; }

    /// <summary>
    /// Gets or sets the binding mode.
    /// </summary>
    public BindingMode Mode { get; set; } = BindingMode.Default;

    /// <summary>
    /// Gets or sets the trigger that determines when the source is updated.
    /// </summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; } = UpdateSourceTrigger.Default;

    /// <summary>
    /// Gets or sets the converter to use.
    /// </summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>
    /// Resource key for an <see cref="IValueConverter"/> that should be resolved lazily
    /// against the binding target's resource lookup chain. Used when the binding declares
    /// <c>Converter="{StaticResource X}"</c> inside a template (DataTemplate /
    /// ControlTemplate) where the resource cannot be resolved at parse time — the element
    /// hasn't been attached to its templated parent yet, so neither the ambient parser
    /// stack nor the visual tree contains the resource. <see cref="BindingExpression.Activate"/>
    /// resolves this through <c>ResourceLookup.FindResource</c> before the first evaluation,
    /// at which point the target FE is already in the live visual tree.
    /// </summary>
    internal string? PendingConverterKey { get; set; }

    /// <summary>
    /// Gets or sets the parameter to pass to the converter.
    /// </summary>
    public object? ConverterParameter { get; set; }

    /// <summary>
    /// Gets or sets the culture to use in the converter.
    /// </summary>
    public System.Globalization.CultureInfo? ConverterCulture { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether to raise PropertyChanged events.
    /// </summary>
    public bool NotifyOnSourceUpdated { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether to raise PropertyChanged events.
    /// </summary>
    public bool NotifyOnTargetUpdated { get; set; }

    /// <summary>
    /// Gets the collection of validation rules to apply to the binding.
    /// </summary>
    public Collection<ValidationRule> ValidationRules { get; } = new();

    /// <summary>
    /// Gets or sets a value that indicates whether to include exceptions as validation errors.
    /// </summary>
    public bool ValidatesOnExceptions { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether to use IDataErrorInfo for validation.
    /// </summary>
    public bool ValidatesOnDataErrors { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether to use INotifyDataErrorInfo for validation.
    /// </summary>
    public bool ValidatesOnNotifyDataErrors { get; set; } = true;

    /// <summary>
    /// Gets or sets a value that indicates whether to raise validation error events.
    /// </summary>
    public bool NotifyOnValidationError { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Binding"/> class.
    /// </summary>
    public Binding()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Binding"/> class with the specified path.
    /// </summary>
    /// <param name="path">The property path string.</param>
    public Binding(string path)
    {
        Path = new PropertyPath(path);
    }

    /// <inheritdoc />
    internal override BindingExpressionBase CreateBindingExpression(DependencyObject target, DependencyProperty targetProperty)
    {
        return new BindingExpression(this, target, targetProperty);
    }
}

/// <summary>
/// Describes the location of a binding source relative to the position of the binding target.
/// </summary>
public sealed class RelativeSource
{
    /// <summary>
    /// Gets the relative source mode.
    /// </summary>
    public RelativeSourceMode Mode { get; }

    /// <summary>
    /// Gets the type of ancestor to look for.
    /// </summary>
    public Type? AncestorType { get; set; }

    /// <summary>
    /// Gets the level of ancestor to look for.
    /// </summary>
    public int AncestorLevel { get; set; } = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeSource"/> class.
    /// </summary>
    public RelativeSource(RelativeSourceMode mode)
    {
        Mode = mode;
    }

    /// <summary>
    /// Gets a static RelativeSource for Self mode.
    /// </summary>
    public static RelativeSource Self { get; } = new(RelativeSourceMode.Self);

    /// <summary>
    /// Gets a static RelativeSource for TemplatedParent mode.
    /// </summary>
    public static RelativeSource TemplatedParent { get; } = new(RelativeSourceMode.TemplatedParent);

    /// <summary>
    /// Gets a static RelativeSource for PreviousData mode.
    /// </summary>
    public static RelativeSource PreviousData { get; } = new(RelativeSourceMode.PreviousData);
}

/// <summary>
/// Specifies the relative source mode.
/// </summary>
public enum RelativeSourceMode
{
    /// <summary>
    /// Refers to the previous data item in the data-bound collection.
    /// </summary>
    PreviousData,

    /// <summary>
    /// Refers to the parent element in the control template.
    /// </summary>
    TemplatedParent,

    /// <summary>
    /// Refers to the element on which you set the binding.
    /// </summary>
    Self,

    /// <summary>
    /// Refers to an ancestor in the parent chain.
    /// </summary>
    FindAncestor
}

/// <summary>
/// Base class for binding expressions.
/// </summary>
public abstract class BindingExpressionBase
{
    /// <summary>
    /// Gets the target element.
    /// </summary>
    public DependencyObject Target { get; }

    /// <summary>
    /// Gets the target property.
    /// </summary>
    public DependencyProperty TargetProperty { get; }

    /// <summary>
    /// Gets a value indicating whether the binding is active.
    /// </summary>
    public bool IsActive { get; protected set; }

    /// <summary>
    /// Gets the binding status.
    /// </summary>
    public BindingStatus Status { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingExpressionBase"/> class.
    /// </summary>
    protected BindingExpressionBase(DependencyObject target, DependencyProperty targetProperty)
    {
        Target = target;
        TargetProperty = targetProperty;
    }

    /// <summary>
    /// Activates the binding.
    /// </summary>
    internal abstract void Activate();

    /// <summary>
    /// Deactivates the binding.
    /// </summary>
    internal abstract void Deactivate();

    /// <summary>
    /// Updates the source value.
    /// </summary>
    public abstract void UpdateSource();

    /// <summary>
    /// Updates the target value.
    /// </summary>
    public abstract void UpdateTarget();
}

/// <summary>
/// Represents the runtime instance of a binding.
/// </summary>
public sealed class BindingExpression : BindingExpressionBase
{
    private readonly Binding _binding;
    private INotifyPropertyChanged? _sourceNotify;
    private INotifyDataErrorInfo? _notifyDataErrorInfo;
    private DependencyObject? _sourceDependencyObject;
    private PropertyInfo? _sourceProperty;
    private object? _effectiveSource;
    private bool _isUpdating;
    private bool _isLostFocusUpdate;
    private List<(INotifyPropertyChanged Notify, string PropertyName)>? _intermediateSubscriptions;

    // Converter / ConverterParameter 自身的可变状态订阅。
    // 若 Converter 实现 INotifyPropertyChanged 或继承自 DependencyObject，其属性变化时
    // 必须重新执行 Convert()，否则会出现 “通知属性变了但目标没刷新” 的脏数据。
    // ConverterParameter 同理 —— 例如以 ViewModel/DependencyObject 作为参数对象。
    // 同一对象优先按 DependencyObject 订阅（避免极少数同时实现 INPC + DO 时双触发）。
    private INotifyPropertyChanged? _converterNotify;
    private DependencyObject? _converterDepObj;
    private INotifyPropertyChanged? _converterParamNotify;
    private DependencyObject? _converterParamDepObj;

    /// <summary>
    /// True once we've subscribed to <see cref="FrameworkElement.Loaded"/> for the
    /// deferred-Converter retry path; prevents duplicate handler registration when
    /// <see cref="Activate"/> runs multiple times before the element finally loads.
    /// </summary>
    private bool _deferredConverterLoadedHooked;

    // ── INotifyPropertyChanged 订阅句柄 ───────────────────────────────────────
    // 缓存为字段（构造函数初始化指向实例方法），让 WeakEventManager 注册时 delegate
    // 实例稳定，RemoveHandler 能精确找到对应订阅；同时字段强引用让 lambda 跟着
    // BindingExpression 一起活，target 被 GC 时 BindingExpression GC，handler 也 GC，
    // PropertyChangedEventManager 内部的 WeakReference 失效，source 不再钉住 target。
    // 这是 WPF 风格：source 用 weak event 持 target，避免长寿 VM 钉死短寿 UI。
    private readonly EventHandler<PropertyChangedEventArgs> _sourcePcHandler;
    private readonly EventHandler<PropertyChangedEventArgs> _converterPcHandler;
    private readonly EventHandler<PropertyChangedEventArgs> _converterParamPcHandler;
    private readonly EventHandler<PropertyChangedEventArgs> _intermediatePcHandler;

    /// <summary>
    /// Gets the parent binding.
    /// </summary>
    public Binding ParentBinding => _binding;

    /// <summary>
    /// Gets the resolved data source.
    /// </summary>
    public object? ResolvedSource { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingExpression"/> class.
    /// </summary>
    internal BindingExpression(Binding binding, DependencyObject target, DependencyProperty targetProperty)
        : base(target, targetProperty)
    {
        _binding = binding;
        // 方法组转换为 EventHandler<PropertyChangedEventArgs> —— 缓存稳定 delegate 实例
        // 给 WeakEventManager 用，否则每次 += 创建新 delegate，-= 无法精准移除。
        _sourcePcHandler = OnSourcePropertyChanged;
        _converterPcHandler = OnConverterPropertyChanged;
        _converterParamPcHandler = OnConverterParameterPropertyChanged;
        _intermediatePcHandler = OnIntermediatePropertyChanged;
    }

    /// <inheritdoc />
    internal override void Activate()
    {
        if (IsActive)
            return;

        // Lazy-resolve a deferred Converter resource reference. SG-emitted template
        // SetVisualTree lambdas execute SetProperty(string) before the target element
        // has been parented (lambda runs first, parent assignment happens after the
        // lambda returns). At that moment ResourceLookup.FindResource walks zero
        // ancestors and never reaches the UserControl that owns the Converter resource.
        // We resolve through three windows of opportunity, in order:
        //   1. Right now — works when the binding is set on an already-attached element
        //      (e.g. a non-template usage).
        //   2. Target.Loaded — fires once the visual tree wires up; FindResource then
        //      walks the live ancestor chain and finds the resource.
        //   3. Subsequent Activate calls — keep the key set on failure so reactivations
        //      (DataContextChanged, ItemsControl re-virtualisation) get another shot.
        TryResolveDeferredConverter();

        // Resolve the data source
        ResolveDataSource();

        // If the source couldn't be resolved (visual tree not ready for DataContext inheritance,
        // or FindAncestor can't find ancestor), subscribe to DataContextChanged so we can
        // activate when the visual tree is built and DataContext becomes available.
        if (ResolvedSource == null && _binding.Source == null)
        {
            Status = BindingStatus.Unattached;
            if (Target is FrameworkElement pendingFe)
            {
                // 防重 subscribe — 多次 Activate 失败时（XAML 解析时 + ReactivateBindings 时
                // visual tree 仍未就绪等场景）不应叠加多份 handler，否则 DataContext 后续
                // 就绪时 OnDataContextChanged 会被调用多次，徒增 SubscribeToSource 累积。
                pendingFe.DataContextChanged -= OnDataContextChanged;
                pendingFe.DataContextChanged += OnDataContextChanged;
            }
            BindingDiagnostics.NotifyStatus(this, "Unattached — source not resolved");
            return;
        }

        IsActive = true;
        Status = BindingStatus.Active;
        BindingDiagnostics.NotifyActivated(this);

        // Subscribe to source changes
        SubscribeToSource();

        // Initial update — but skip when we have a deferred Converter that hasn't
        // resolved yet. Running UpdateTarget without the Converter pushes the raw
        // source value through BindingValueCoercion, and a Boolean → Brush coerce
        // (typical for IsSelected → Background bindings on selection cards) throws
        // InvalidCastException as a first-chance exception even though the catch
        // swallows it. The Loaded handler re-runs UpdateTarget once the Converter
        // is bound, which yields the correct typed value on first visible paint.
        if (string.IsNullOrEmpty(_binding.PendingConverterKey))
        {
            UpdateTarget();
        }
    }

    /// <summary>
    /// Try to bind the deferred Converter through the target's resource lookup chain.
    /// Called from <see cref="Activate"/> and again from the target FE's Loaded handler.
    /// On success the Converter is set and the pending key cleared; on failure the key
    /// stays set and we hook into <see cref="FrameworkElement.Loaded"/> for one more
    /// attempt once the element joins the live visual tree.
    /// </summary>
    private void TryResolveDeferredConverter()
    {
        if (_binding.Converter != null || string.IsNullOrEmpty(_binding.PendingConverterKey))
            return;

        if (Target is not FrameworkElement targetFe)
        {
            // Non-FE targets (rare for binding, e.g. Freezable) cannot resolve resources
            // through visual ancestors. Leave the key set; if the binding ever attaches
            // to an FE later, we'll try again.
            return;
        }

        var key = _binding.PendingConverterKey!;
        var resolved = ResourceLookup.FindResource(targetFe, key);
        if (resolved is IValueConverter converter)
        {
            _binding.Converter = converter;
            _binding.PendingConverterKey = null;
            return;
        }

        // Resource not reachable yet — typically because the target was constructed
        // inside a SG SetVisualTree lambda and hasn't been parented yet. Hook Loaded
        // exactly once so the next round-trip through the visual tree wires it up.
        if (!_deferredConverterLoadedHooked)
        {
            targetFe.Loaded += OnTargetLoadedForDeferredConverter;
            _deferredConverterLoadedHooked = true;
        }
    }

    private void OnTargetLoadedForDeferredConverter(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            // One-shot — Loaded can fire multiple times for templated elements when
            // the visual tree is recreated, but we only need the converter resolved
            // once. Detach immediately to keep the handler list clean.
            fe.Loaded -= OnTargetLoadedForDeferredConverter;
        }
        _deferredConverterLoadedHooked = false;

        if (_binding.Converter != null || string.IsNullOrEmpty(_binding.PendingConverterKey))
            return;

        if (Target is FrameworkElement targetFe)
        {
            var resolved = ResourceLookup.FindResource(targetFe, _binding.PendingConverterKey!);
            if (resolved is IValueConverter converter)
            {
                _binding.Converter = converter;
                _binding.PendingConverterKey = null;

                // Re-evaluate the binding so the freshly-installed Converter is
                // applied to the current source value — without this the Border's
                // Background stays at whatever the missing-Converter coercion path
                // produced (typically the unconverted Boolean → Brush exception).
                if (IsActive)
                {
                    UpdateTarget();
                }
            }
        }
    }

    /// <inheritdoc />
    internal override void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;
        Status = BindingStatus.Inactive;

        // Unsubscribe from source changes
        UnsubscribeFromSource();
    }

    /// <inheritdoc />
    public override void UpdateSource()
    {
        if (!IsActive || _isUpdating)
            return;

        var mode = GetEffectiveMode();
        if (mode != BindingMode.TwoWay && mode != BindingMode.OneWayToSource)
            return;

        // If UpdateSourceTrigger is LostFocus or Explicit, only update when explicitly requested
        // (LostFocus updates via OnTargetLostFocus; Explicit requires manual call).
        // When called from DependencyObject.SetValue (automatic path), we must skip if
        // the trigger is not PropertyChanged/Default.
        var trigger = _binding.UpdateSourceTrigger;
        if (trigger == UpdateSourceTrigger.Explicit)
            return;
        // LostFocus: block automatic updates triggered by property changes; only allow
        // updates initiated by the LostFocus handler (which sets _isLostFocusUpdate = true).
        if (trigger == UpdateSourceTrigger.LostFocus && !_isLostFocusUpdate)
            return;

        if (ResolvedSource == null || _sourceProperty == null)
            return;

        BindingDiagnostics.NotifyUpdateSource(this);

        try
        {
            _isUpdating = true;

            var targetValue = Target.GetValue(TargetProperty);

            // Step 1: RawProposedValue validation
            if (!ValidateValue(targetValue, ValidationStep.RawProposedValue))
                return;

            // Step 2: Convert value
            object? sourceValue;
            try
            {
                sourceValue = ConvertBack(targetValue);
                sourceValue = BindingValueCoercion.Coerce(
                    sourceValue,
                    _sourceProperty.PropertyType,
                    _binding.ConverterCulture ?? CultureInfo.CurrentCulture);
            }
            catch (Exception ex)
            {
                if (_binding.ValidatesOnExceptions)
                {
                    AddValidationError(new ValidationError(null, ResolvedSource, ex.Message, ex));
                }
                return;
            }

            // Step 3: ConvertedProposedValue validation
            if (!ValidateValue(sourceValue, ValidationStep.ConvertedProposedValue))
                return;

            // Step 4: Update source
            try
            {
                _sourceProperty.SetValue(_effectiveSource ?? ResolvedSource, sourceValue);
            }
            catch (Exception ex)
            {
                if (_binding.ValidatesOnExceptions)
                {
                    AddValidationError(new ValidationError(null, ResolvedSource, ex.Message, ex));
                }
                return;
            }

            // Step 5: UpdatedValue validation
            if (!ValidateValue(sourceValue, ValidationStep.UpdatedValue))
                return;

            // Step 6: Validate IDataErrorInfo if enabled
            if (_binding.ValidatesOnDataErrors)
            {
                if (!ValidateDataErrorInfo())
                    return;
            }

            // Success - clear validation errors
            ClearValidationErrors();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Validates a value against all validation rules for the specified step.
    /// </summary>
    private bool ValidateValue(object? value, ValidationStep step)
    {
        var culture = _binding.ConverterCulture ?? CultureInfo.CurrentCulture;

        foreach (var rule in _binding.ValidationRules)
        {
            if (rule.ValidationStep != step)
                continue;

            var result = rule.Validate(value, culture);
            if (!result.IsValid)
            {
                AddValidationError(new ValidationError(rule, ResolvedSource, result.ErrorContent, null));
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates using IDataErrorInfo if the source implements it.
    /// </summary>
    private bool ValidateDataErrorInfo()
    {
        if (ResolvedSource is not IDataErrorInfo dataErrorInfo || _binding.Path == null)
            return true;

        var propertyName = _binding.Path.PathSegments.LastOrDefault() ?? _binding.Path.Path;
        var error = dataErrorInfo[propertyName];

        if (!string.IsNullOrEmpty(error))
        {
            AddValidationError(new ValidationError(error));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates using INotifyDataErrorInfo if the source implements it.
    /// </summary>
    private void ValidateNotifyDataErrorInfo()
    {
        if (_notifyDataErrorInfo == null || _binding.Path == null)
            return;

        var propertyName = _binding.Path.PathSegments.LastOrDefault() ?? _binding.Path.Path;
        var errors = _notifyDataErrorInfo.GetErrors(propertyName);

        if (errors != null)
        {
            foreach (var error in errors)
            {
                if (error != null)
                {
                    AddValidationError(new ValidationError(error.ToString() ?? "Validation error"));
                }
            }
        }
    }

    /// <summary>
    /// Adds a validation error to the target element.
    /// </summary>
    private void AddValidationError(ValidationError error)
    {
        Validation.MarkInvalid(Target, error);
        Status = BindingStatus.UpdateSourceError;

        BindingDiagnostics.NotifyError(this, error?.ErrorContent?.ToString() ?? "<null>");

        if (_binding.NotifyOnValidationError && error != null)
        {
            RaiseValidationErrorEvent(error, ValidationErrorEventAction.Added);
        }
    }

    /// <summary>
    /// Clears all validation errors from the target element.
    /// </summary>
    private void ClearValidationErrors()
    {
        if (Validation.GetHasError(Target))
        {
            var errors = Validation.GetErrors(Target);
            if (errors != null && _binding.NotifyOnValidationError)
            {
                foreach (var error in errors.ToList())
                {
                    RaiseValidationErrorEvent(error, ValidationErrorEventAction.Removed);
                }
            }

            Validation.ClearInvalid(Target);
        }
        Status = BindingStatus.Active;
    }

    /// <summary>
    /// Raises the validation error event.
    /// </summary>
    private void RaiseValidationErrorEvent(ValidationError error, ValidationErrorEventAction action)
    {
        if (Target is UIElement uiElement)
        {
            var args = new ValidationErrorEventArgs(Validation.ErrorEvent, error, action);
            uiElement.RaiseEvent(args);
        }
    }

    /// <inheritdoc />
    public override void UpdateTarget()
    {
        if (!IsActive || _isUpdating)
            return;

        BindingDiagnostics.NotifyUpdateTarget(this);

        // Late-bind a deferred Converter resource if it's still pending. Activate's
        // initial attempt happens before the SG-emitted SetVisualTree lambda parents
        // the element, so FindResource reaches no ancestors. By the time UpdateTarget
        // fires (typically driven by DataContextChanged once the ItemsControl pushes
        // DataContext into the container), the element has been added to the visual
        // tree and FindResource walks the live ancestor chain — exactly when the
        // resource becomes resolvable. Doing this on every UpdateTarget call is cheap
        // (TryResolveDeferredConverter exits in two field reads when nothing's pending)
        // and removes the dependency on FrameworkElement.Loaded firing reliably for
        // every templated element.
        TryResolveDeferredConverter();

        // If the Converter is still unresolved, bail without touching the target. A
        // bare Boolean source value can't sensibly coerce into a Brush DP, and pushing
        // the unconverted value through SetValue would either land in the DP at the
        // wrong type or no-op silently. Leaving the target on its metadata default
        // until the deferred resolution succeeds matches WPF's behaviour for a
        // binding whose Converter resource is genuinely missing.
        if (!string.IsNullOrEmpty(_binding.PendingConverterKey))
        {
            return;
        }

        try
        {
            _isUpdating = true;

            var sourceValue = GetSourceValue();
            var convertedValue = Convert(sourceValue);

            // Apply StringFormat if specified
            if (convertedValue != null && !string.IsNullOrEmpty(_binding.StringFormat))
            {
                try
                {
                    convertedValue = string.Format(
                        _binding.ConverterCulture ?? CultureInfo.CurrentCulture,
                        _binding.StringFormat,
                        convertedValue);
                }
                catch (FormatException)
                {
                    // Invalid StringFormat — use unconverted value rather than crashing
                }
            }

            var targetValue = BindingValueCoercion.Coerce(
                convertedValue,
                TargetProperty.PropertyType,
                _binding.ConverterCulture ?? CultureInfo.CurrentCulture);

            Target.SetValue(TargetProperty, targetValue);

            // Validate data errors for the target update
            if (_binding.ValidatesOnNotifyDataErrors && _notifyDataErrorInfo != null)
            {
                ValidateNotifyDataErrorInfo();
            }

            if (_binding.ValidatesOnDataErrors)
            {
                ValidateDataErrorInfo();
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void ResolveDataSource()
    {
        // Priority: explicit Source > ElementName > RelativeSource > DataContext
        if (_binding.Source != null)
        {
            ResolvedSource = _binding.Source;
        }
        else if (!string.IsNullOrEmpty(_binding.ElementName))
        {
            // Resolve element by name - walk up the visual tree looking for named element
            ResolvedSource = FindElementByName(Target, _binding.ElementName);
        }
        else if (_binding.RelativeSource != null)
        {
            ResolvedSource = ResolveRelativeSource();
        }
        else
        {
            // Use DataContext
            ResolvedSource = GetDataContext();
        }

        // Resolve property from path
        if (ResolvedSource != null && _binding.Path != null)
        {
            ResolveSourceProperty();
        }
    }

    private static object? FindElementByName(DependencyObject target, string elementName)
    {
        // First, try to find the element in the current scope
        if (target is FrameworkElement fe)
        {
            // Use FindName which walks up the tree looking in each element's named scope
            var found = fe.FindName(elementName);
            if (found != null)
            {
                return found;
            }

            // If not found in named scopes, try walking up the visual tree
            // looking for an element with matching Name property
            return FindElementInVisualTree(fe, elementName);
        }
        return null;
    }

    private static object? FindElementInVisualTree(FrameworkElement start, string elementName)
    {
        // Walk up to find the root or template root
        FrameworkElement? root = start;
        while (root.VisualParent is FrameworkElement parent)
        {
            root = parent;
        }

        // Search down the tree for the named element
        return SearchVisualTreeForName(root, elementName);
    }

    private static FrameworkElement? SearchVisualTreeForName(Visual? visual, string elementName)
    {
        if (visual == null) return null;

        // Check if this element has the name we're looking for
        if (visual is FrameworkElement fe && fe.Name == elementName)
        {
            return fe;
        }

        // Search children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            var found = SearchVisualTreeForName(child, elementName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private object? ResolveRelativeSource()
    {
        var relativeSource = _binding.RelativeSource;
        if (relativeSource == null)
            return null;

        switch (relativeSource.Mode)
        {
            case RelativeSourceMode.Self:
                return Target;

            case RelativeSourceMode.TemplatedParent:
                // Return the control that owns the template containing this element
                if (Target is FrameworkElement fe)
                {
                    return fe.TemplatedParent;
                }
                return null;

            case RelativeSourceMode.FindAncestor:
                return FindAncestor(relativeSource.AncestorType, relativeSource.AncestorLevel);

            default:
                return null;
        }
    }

    private object? FindAncestor(Type? ancestorType, int level)
    {
        if (ancestorType == null || Target is not Visual visual)
            return null;

        var current = visual.VisualParent;
        var count = 0;

        while (current != null)
        {
            if (ancestorType.IsAssignableFrom(current.GetType()))
            {
                count++;
                if (count >= level)
                    return current;
            }
            current = current.VisualParent;
        }

        return null;
    }

    private object? GetDataContext()
    {
        // Walk up the visual tree to find the nearest DataContext
        FrameworkElement? current = Target as FrameworkElement;
        while (current != null)
        {
            if (current.DataContext != null)
                return current.DataContext;
            current = current.VisualParent as FrameworkElement;
        }
        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "PropertyAccessorRegistry.TryReadProperty/TryGetPropertyInfo are the binding engine's reflection fallback for unregistered user view-model types. Per their RUC message — \"Register typed accessors via Register() to opt out of reflection\" — preserving these properties is the documented consumer responsibility for trim/AOT (the SourceGenerator emits Register<T>()/DynamicDependency for jalxaml DataType bindings; see project_trim_view_model_binding). Suppressing here keeps the RUC contract declared at the PropertyAccessorRegistry surface rather than cascading it onto every DependencyObject.SetValue caller of the binding engine.")]
    private void ResolveSourceProperty()
    {
        if (ResolvedSource == null || _binding.Path == null)
            return;

        var segments = _binding.Path.PathSegments;
        if (segments.Length == 0)
            return;

        // Navigate to the object containing the final property using the
        // AOT-safe PropertyAccessorRegistry (with reflection fallback).
        object? current = ResolvedSource;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current == null) return;
            if (!PropertyAccessorRegistry.TryReadProperty(current, segments[i], out var next))
                return;
            current = next;
        }

        if (current == null) return;

        _effectiveSource = current;
        var lastSegment = segments[segments.Length - 1];
        _sourceProperty = PropertyAccessorRegistry.TryGetPropertyInfo(current, lastSegment);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "PropertyAccessorRegistry.TryReadProperty is the binding engine's reflection fallback for unregistered user view-model types. Per its RUC message — \"Register typed accessors via Register() to opt out of reflection\" — preserving these properties is the documented consumer responsibility for trim/AOT (the SourceGenerator emits Register<T>()/DynamicDependency for jalxaml DataType bindings; see project_trim_view_model_binding). Suppressing here keeps the RUC contract declared at the PropertyAccessorRegistry surface rather than cascading it onto every binding consumer.")]
    private object? GetSourceValue()
    {
        if (ResolvedSource == null)
            return _binding.FallbackValue;

        if (_binding.Path == null || _binding.Path.PathSegments.Length == 0)
            return ResolvedSource;

        // Navigate the path
        object? current = ResolvedSource;
        foreach (var segment in _binding.Path.PathSegments)
        {
            if (current == null)
                return _binding.FallbackValue;

            if (!PropertyAccessorRegistry.TryReadProperty(current, segment, out var next))
                return _binding.FallbackValue;

            current = next;
        }

        return current ?? _binding.TargetNullValue;
    }

    private object? Convert(object? value)
    {
        if (_binding.Converter != null)
        {
            value = _binding.Converter.Convert(
                value,
                TargetProperty.PropertyType,
                _binding.ConverterParameter,
                _binding.ConverterCulture ?? System.Globalization.CultureInfo.CurrentCulture);
        }

        if (value != null && !string.IsNullOrEmpty(_binding.StringFormat))
        {
            try
            {
                value = string.Format(_binding.StringFormat, value);
            }
            catch (FormatException)
            {
                // Invalid StringFormat — return unconverted value rather than crashing
            }
        }

        return value;
    }

    private object? ConvertBack(object? value)
    {
        if (_binding.Converter != null)
        {
            value = _binding.Converter.ConvertBack(
                value,
                _sourceProperty?.PropertyType ?? typeof(object),
                _binding.ConverterParameter,
                _binding.ConverterCulture ?? System.Globalization.CultureInfo.CurrentCulture);
        }

        return value;
    }

    private void SubscribeToSource()
    {
        // 全部 subscribe 都先 unsubscribe 防重 — 调用方（OnDataContextChanged、Activate 二次激活等）
        // 可能在没 UnsubscribeFromSource 的情况下调本方法，避免 handler 累积
        // 让 PropertyChanged 触发数倍数 UpdateTarget。
        if (ResolvedSource is INotifyPropertyChanged notify)
        {
            _sourceNotify = notify;
            // WeakEventManager：source 用弱引用持 handler，target 被 GC 时 BindingExpression
            // 跟着 GC，handler 引用自动失效；先 Remove 防重复订阅累积。
            PropertyChangedEventManager.RemoveHandler(_sourceNotify, _sourcePcHandler, "");
            PropertyChangedEventManager.AddHandler(_sourceNotify, _sourcePcHandler, "");
        }

        // Also subscribe to DependencyObject PropertyChangedInternal for DependencyProperty changes
        if (ResolvedSource is DependencyObject depObj)
        {
            _sourceDependencyObject = depObj;
            _sourceDependencyObject.PropertyChangedInternal -= OnSourceDependencyPropertyChanged;
            _sourceDependencyObject.PropertyChangedInternal += OnSourceDependencyPropertyChanged;
        }

        // Also subscribe to DataContext changes
        if (Target is FrameworkElement fe)
        {
            fe.DataContextChanged -= OnDataContextChanged;
            fe.DataContextChanged += OnDataContextChanged;
        }

        // Subscribe to LostFocus for UpdateSourceTrigger.LostFocus
        if (_binding.UpdateSourceTrigger == UpdateSourceTrigger.LostFocus && Target is UIElement targetElement)
        {
            targetElement.LostFocus += OnTargetLostFocus;
        }

        // Subscribe to INotifyDataErrorInfo if enabled
        if (_binding.ValidatesOnNotifyDataErrors && ResolvedSource is INotifyDataErrorInfo ndei)
        {
            _notifyDataErrorInfo = ndei;
            _notifyDataErrorInfo.ErrorsChanged += OnErrorsChanged;

            // Check for initial errors
            ValidateNotifyDataErrorInfo();
        }

        // Subscribe to intermediate objects for nested property paths (e.g., Address.City)
        SubscribeToIntermediates();

        // Subscribe to mutations on the converter and its parameter so that target
        // re-conversion happens automatically when their notification properties change.
        SubscribeToConverter();
    }

    private void UnsubscribeFromSource()
    {
        if (_sourceNotify != null)
        {
            PropertyChangedEventManager.RemoveHandler(_sourceNotify, _sourcePcHandler, "");
            _sourceNotify = null;
        }

        if (_sourceDependencyObject != null)
        {
            _sourceDependencyObject.PropertyChangedInternal -= OnSourceDependencyPropertyChanged;
            _sourceDependencyObject = null;
        }

        if (Target is FrameworkElement fe)
        {
            fe.DataContextChanged -= OnDataContextChanged;
        }

        if (Target is UIElement targetElementUnsub)
        {
            targetElementUnsub.LostFocus -= OnTargetLostFocus;
        }

        if (_notifyDataErrorInfo != null)
        {
            _notifyDataErrorInfo.ErrorsChanged -= OnErrorsChanged;
            _notifyDataErrorInfo = null;
        }

        UnsubscribeFromIntermediates();
        UnsubscribeFromConverter();
    }

    private void SubscribeToConverter()
    {
        // 先解除任何遗留订阅（与 source/intermediate 同样的 “防重” 风格），允许多次 Activate
        // 或 DataContext 切换路径都安全调用本方法。
        UnsubscribeFromConverter();

        var converter = _binding.Converter;
        if (converter != null)
        {
            // 同一实例若同时实现 DependencyObject 与 INotifyPropertyChanged，
            // 优先走 DP 通道避免双触发；纯 INPC 实现则走 PropertyChanged。
            if (converter is DependencyObject convDo)
            {
                _converterDepObj = convDo;
                convDo.PropertyChangedInternal += OnConverterDependencyPropertyChanged;
            }
            else if (converter is INotifyPropertyChanged convNpc)
            {
                _converterNotify = convNpc;
                PropertyChangedEventManager.AddHandler(convNpc, _converterPcHandler, "");
            }
        }

        var parameter = _binding.ConverterParameter;
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
                PropertyChangedEventManager.AddHandler(paramNpc, _converterParamPcHandler, "");
            }
        }
    }

    private void UnsubscribeFromConverter()
    {
        if (_converterNotify != null)
        {
            PropertyChangedEventManager.RemoveHandler(_converterNotify, _converterPcHandler, "");
            _converterNotify = null;
        }

        if (_converterDepObj != null)
        {
            _converterDepObj.PropertyChangedInternal -= OnConverterDependencyPropertyChanged;
            _converterDepObj = null;
        }

        if (_converterParamNotify != null)
        {
            PropertyChangedEventManager.RemoveHandler(_converterParamNotify, _converterParamPcHandler, "");
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
        // Converter 任意属性变化都应触发整套 binding 重算 —— Converter 没有 “路径” 概念，
        // 任何状态字段都可能影响 Convert/ConvertBack 的输出。_isUpdating 防止 Convert
        // 内部对自身属性 setter 的副作用产生递归。
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

    private void UnsubscribeFromIntermediates()
    {
        if (_intermediateSubscriptions != null)
        {
            foreach (var (notify, _) in _intermediateSubscriptions)
            {
                PropertyChangedEventManager.RemoveHandler(notify, _intermediatePcHandler, "");
            }
            _intermediateSubscriptions = null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
        Justification = "current.GetType().GetProperty(segment) reflects the runtime type of an intermediate value-object on the binding path to walk it for INotifyPropertyChanged subscriptions. The source is object.GetType() and cannot carry DynamicallyAccessedMembers. The property being reflected is the same property the binding engine already reads via PropertyAccessorRegistry, whose RUC contract — \"Register typed accessors via Register() to opt out of reflection\" — documents that consumers must preserve their bound view-model properties for trim/AOT (the SourceGenerator emits Register<T>()/DynamicDependency for jalxaml DataType bindings; see project_trim_view_model_binding). This is the same documented consumer responsibility, surfaced here as IL2075 because the walk is a direct reflection site.")]
    private void SubscribeToIntermediates()
    {
        UnsubscribeFromIntermediates();

        if (_binding.Path == null) return;
        var segments = _binding.Path.PathSegments;
        if (segments.Length <= 1) return; // No intermediates for simple paths

        _intermediateSubscriptions = new();

        object? current = ResolvedSource;
        // Subscribe to intermediate objects (segments[0] through segments[Length-2]).
        // Segment 0's property changing on ResolvedSource is already handled by _sourceNotify,
        // but we still need to subscribe to the *value* of segment 0 (the intermediate object)
        // for changes to segment 1, and so on.
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current == null) break;
            var prop = current.GetType().GetProperty(segments[i]);
            if (prop == null) break;

            var intermediateObj = prop.GetValue(current);
            if (intermediateObj is INotifyPropertyChanged inpc)
            {
                // Subscribe to this intermediate object for property changes
                // (e.g., for path A.B.C, subscribe to A's value for "B" changes)
                PropertyChangedEventManager.AddHandler(inpc, _intermediatePcHandler, "");
                _intermediateSubscriptions.Add((inpc, segments[i + 1]));
            }
            current = intermediateObj;
        }
    }

    private void OnIntermediatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (_binding.Path == null) return;

        // Find which intermediate this sender corresponds to
        if (_intermediateSubscriptions != null)
        {
            foreach (var (notify, propertyName) in _intermediateSubscriptions)
            {
                if (ReferenceEquals(notify, sender))
                {
                    // Only react if the changed property matches the segment we care about,
                    // or if PropertyName is null/empty (meaning all properties changed)
                    if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == propertyName)
                    {
                        // Re-resolve the entire property chain since an intermediate changed
                        ResolveSourceProperty();
                        SubscribeToIntermediates();
                        UpdateTarget();
                    }
                    return;
                }
            }
        }
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (_binding.Path == null)
            return;

        var propertyName = _binding.Path.PathSegments.LastOrDefault() ?? _binding.Path.Path;

        // Check if the changed property matches our binding path
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == propertyName)
        {
            // Clear existing errors and revalidate
            if (_notifyDataErrorInfo != null && _notifyDataErrorInfo.HasErrors)
            {
                ClearValidationErrors();
                ValidateNotifyDataErrorInfo();
            }
            else
            {
                ClearValidationErrors();
            }
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (_binding.Path == null)
            return;

        // Check if the changed property is in our path
        if (string.IsNullOrEmpty(e.PropertyName) ||
            _binding.Path.PathSegments.Length > 0 && _binding.Path.PathSegments[0] == e.PropertyName)
        {
            // For nested paths (e.g., Address.City), when the top-level property changes
            // (e.g., Address), we need to re-resolve the entire property chain and
            // re-subscribe to the new intermediate objects
            if (_binding.Path.PathSegments.Length > 1)
            {
                ResolveSourceProperty();
                SubscribeToIntermediates();
            }
            UpdateTarget();
        }
    }

    private void OnSourceDependencyPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        if (_isUpdating) return;
        if (_binding.Path == null)
            return;

        // Check if the changed property matches our path
        if (_binding.Path.PathSegments.Length > 0 && _binding.Path.PathSegments[0] == dp.Name)
        {
            // For nested paths, re-resolve the property chain and re-subscribe intermediates
            if (_binding.Path.PathSegments.Length > 1)
            {
                ResolveSourceProperty();
                SubscribeToIntermediates();
            }
            UpdateTarget();
        }
    }

    private void OnTargetLostFocus(object sender, RoutedEventArgs e)
    {
        _isLostFocusUpdate = true;
        try
        {
            UpdateSource();
        }
        finally
        {
            _isLostFocusUpdate = false;
        }
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        // Re-resolve the data source when DataContext changes
        UnsubscribeFromSource();
        ResolveDataSource();

        // 关键：首次 Activate 时若 visual tree 还没建立，ResolvedSource = null →
        // Status 设为 Unattached、IsActive = false，仅 subscribe Target.DataContextChanged
        // 等 DataContext 后续就绪。
        // 当 DataContext 终于就绪触发本回调时，必须把 IsActive 升回 true，否则
        // 下方 UpdateTarget() 会因 if (!IsActive) 直接 return —— 表现为
        // "Source PropertyChanged 永远到不了 Converter / Target 永远停留在初值"。
        // 同时把 Subscribe 重新挂上（DataContextChanged 路径上 SubscribeToSource 也会再 subscribe
        // DataContextChanged，UnsubscribeFromSource 已先解除避免重复）。
        if (ResolvedSource != null || _binding.Source != null)
        {
            IsActive = true;
            Status = BindingStatus.Active;
        }
        else
        {
            // DataContext 已切回 null：把 binding 标回 Unattached，等下一次再就绪。
            IsActive = false;
            Status = BindingStatus.Unattached;
        }

        SubscribeToSource();
        UpdateTarget();
    }

    private BindingMode GetEffectiveMode()
    {
        if (_binding.Mode != BindingMode.Default)
            return _binding.Mode;

        // Default mode based on property metadata would go here
        // For now, default to OneWay
        return BindingMode.OneWay;
    }
}

/// <summary>
/// Specifies the status of a binding.
/// </summary>
public enum BindingStatus
{
    /// <summary>
    /// The binding has not been activated yet.
    /// </summary>
    Unattached,

    /// <summary>
    /// The binding is inactive.
    /// </summary>
    Inactive,

    /// <summary>
    /// The binding is active and working.
    /// </summary>
    Active,

    /// <summary>
    /// The binding is detached.
    /// </summary>
    Detached,

    /// <summary>
    /// The binding encountered an error while resolving.
    /// </summary>
    PathError,

    /// <summary>
    /// The binding cannot update because of source validation errors.
    /// </summary>
    UpdateTargetError,

    /// <summary>
    /// The binding cannot update because of target validation errors.
    /// </summary>
    UpdateSourceError
}
