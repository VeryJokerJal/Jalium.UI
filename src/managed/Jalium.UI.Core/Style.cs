using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI;

/// <summary>
/// Contains property setters that can be shared between instances of a type.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Setters")]
public class Style : DispatcherObject, Jalium.UI.Markup.INameScope, IQueryAmbient, Jalium.UI.Markup.IAddChild
{
    private readonly SetterBaseCollection _setters = new();
    private IList<EventSetter>? _eventSetters;
    private readonly TriggerCollection _triggers = new();
    private readonly NameScope _nameScope = new();
    private ResourceDictionary? _resources;
    private bool _isSealed;

    /// <summary>
    /// Gets or sets the type for which this style is intended.
    /// </summary>
    [Ambient]
    public Type? TargetType { get; set; }

    /// <summary>
    /// Gets or sets a style that is the basis of the current style.
    /// </summary>
    [Ambient]
    public Style? BasedOn { get; set; }

    /// <summary>
    /// Gets the collection of property setters.
    /// </summary>
    public SetterBaseCollection Setters => _setters;

    /// <summary>
    /// Gets the collection of event setters.
    /// </summary>
    public IList<EventSetter> EventSetters
        => _eventSetters ??= new SetterBaseCollectionView<EventSetter>(_setters);

    /// <summary>
    /// Gets the collection of triggers.
    /// </summary>
    public TriggerCollection Triggers => _triggers;

    /// <summary>
    /// Gets a value that indicates whether the style is read-only.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <summary>
    /// Style 局部资源字典；未实例化时返回 <see langword="null"/>（不触发懒构造）。
    ///
    /// <para>比 <see cref="FrameworkElement.ResourcesOrNull"/> 更要紧：Style 实例
    /// 是**跨元素共享**的（theme style 尤其如此），经由 <see cref="Resources"/>
    /// getter 懒构造出的空字典会挂在共享对象上，一次污染影响所有用该 style 的元素。
    /// 框架内部只读探测一律走本属性。</para>
    /// </summary>
    internal ResourceDictionary? ResourcesOrNull => _resources;

    /// <summary>
    /// Gets or sets resources scoped to this style.
    /// </summary>
    [Ambient]
    public ResourceDictionary Resources
    {
        get => _resources ??= new ResourceDictionary();
        set
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("A sealed Style cannot be changed.");
            }

            _resources = value;
            ResourceLookup.InvalidateResourceCache();
        }
    }

    bool IQueryAmbient.IsAmbientPropertyAvailable(string propertyName)
    {
        // Match WPF: Resources and BasedOn participate only when their backing values
        // already exist; all other ambient-property probes remain available.
        return propertyName switch
        {
            nameof(Resources) => _resources != null,
            nameof(BasedOn) => BasedOn != null,
            _ => true
        };
    }

    public void RegisterName(string name, object scopedElement)
        => _nameScope.RegisterName(name, scopedElement);

    public void UnregisterName(string name) => _nameScope.UnregisterName(name);

    object? Jalium.UI.Markup.INameScope.FindName(string name) => _nameScope.FindName(name);

    void Jalium.UI.Markup.INameScope.RegisterName(string name, object scopedElement) =>
        _nameScope.RegisterName(name, scopedElement);

    void Jalium.UI.Markup.INameScope.UnregisterName(string name) =>
        _nameScope.UnregisterName(name);

    void Jalium.UI.Markup.IAddChild.AddChild(object value)
    {
        if (value is not SetterBase setter)
        {
            throw new ArgumentException("Style content must derive from SetterBase.", nameof(value));
        }

        Setters.Add(setter);
    }

    void Jalium.UI.Markup.IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is not valid Style content.", nameof(text));
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class.
    /// </summary>
    public Style()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class with the specified target type.
    /// </summary>
    /// <param name="targetType">The type for which this style is intended.</param>
    public Style(Type targetType)
    {
        TargetType = targetType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> class with the specified target type and base style.
    /// </summary>
    /// <param name="targetType">The type for which this style is intended.</param>
    /// <param name="basedOn">A style that is the basis of the current style.</param>
    public Style(Type targetType, Style? basedOn)
    {
        TargetType = targetType;
        BasedOn = basedOn;
    }

    /// <summary>
    /// Seals the style so that it can no longer be modified.
    /// </summary>
    public void Seal()
    {
        if (_isSealed)
        {
            return;
        }

        _setters.Seal();
        foreach (var trigger in _triggers)
        {
            trigger.Seal();
        }

        _isSealed = true;
    }

    /// <summary>
    /// Applies this style to the specified framework element.
    /// </summary>
    /// <param name="element">The element to apply the style to.</param>
    internal void Apply(FrameworkElement element)
    {
        Apply(element, null);
    }

    private void Apply(FrameworkElement element, HashSet<Style>? visited)
    {
        // WPF seals a Style on first application. Besides locking the declaration this is
        // the point at which unresolved Trigger/Condition properties are rejected.
        Seal();

        // Apply base style first (with cycle detection)
        if (BasedOn != null)
        {
            visited ??= new HashSet<Style>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(BasedOn))
                return; // Cycle detected, stop recursion
            BasedOn.Apply(element, visited);
        }

        // Apply property and event setters in declaration order.
        foreach (var setter in _setters)
        {
            switch (setter)
            {
                case Setter propertySetter:
                    propertySetter.Apply(element);
                    break;
                case EventSetter eventSetter:
                    eventSetter.Apply(element);
                    break;
            }
        }

        // Apply triggers
        foreach (var trigger in _triggers)
        {
            trigger.ParentStyle = this;
            trigger.RegisterStyleAttachment(element);
            try
            {
                trigger.Attach(element);
            }
            catch
            {
                trigger.UnregisterStyleAttachment(element);
                throw;
            }
        }
    }

    /// <summary>
    /// Removes this style from the specified framework element.
    /// </summary>
    /// <param name="element">The element to remove the style from.</param>
    internal void Remove(FrameworkElement element)
    {
        Remove(element, null);
    }

    private void Remove(FrameworkElement element, HashSet<Style>? visited)
    {
        // Remove triggers
        foreach (var trigger in _triggers)
        {
            try
            {
                trigger.Detach(element);
            }
            finally
            {
                trigger.UnregisterStyleAttachment(element);
            }
        }

        // Remove property and event setters in reverse declaration order.
        for (int i = _setters.Count - 1; i >= 0; i--)
        {
            switch (_setters[i])
            {
                case Setter propertySetter:
                    propertySetter.Remove(element);
                    break;
                case EventSetter eventSetter:
                    eventSetter.Remove(element);
                    break;
            }
        }

        // Remove base style (with cycle detection)
        if (BasedOn != null)
        {
            visited ??= new HashSet<Style>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(BasedOn))
                return; // Cycle detected, stop recursion
            BasedOn.Remove(element, visited);
        }
    }

    /// <summary>
    /// 上层（典型为 Jalium.UI.Xaml 的 TypeConverterRegistry）注入的字符串值转换器。
    /// Setter / TriggerBase 内部 <c>ConvertValueIfNeeded</c> 的 hardcoded fast-path 命中
    /// 不到目标类型时会回落到这里，从而把 jalxaml 里写出的字符串值（例如
    /// <c>Cursor="Hand"</c>、自定义 Brush 名等）正确转成目标 DP 类型，而不是把原始字符串
    /// 塞进 layer 让渲染层强转崩溃。
    /// </summary>
    internal static Func<string, Type, object?>? StringValueConverter { get; set; }
}

/// <summary>
/// Represents a setter that sets a property value.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Value")]
[XamlSetMarkupExtension(nameof(ReceiveMarkupExtension))]
[XamlSetTypeConverter(nameof(ReceiveTypeConverter))]
public class Setter : SetterBase, ISupportInitialize
{
    private DependencyProperty? _property;
    private object? _value;
    private string? _targetName;
    private string? _propertyName;
    private bool _isInitializing;
    private object? _unresolvedProperty;
    private TypeConverter? _unresolvedPropertyConverter;
    private ITypeDescriptorContext? _unresolvedPropertyContext;
    private System.Globalization.CultureInfo? _unresolvedPropertyCulture;
    private bool _hasUnresolvedProperty;
    private object? _unresolvedValue;
    private TypeConverter? _unresolvedValueConverter;
    private ITypeDescriptorContext? _unresolvedValueContext;
    private System.Globalization.CultureInfo? _unresolvedValueCulture;
    private bool _hasUnresolvedValue;

    /// <summary>
    /// Gets or sets the property to set.
    /// </summary>
    public DependencyProperty? Property
    {
        get => _property;
        set
        {
            CheckSealed();
            if (value?.ReadOnly == true)
            {
                throw new ArgumentException("A Setter cannot target a read-only DependencyProperty.", nameof(value));
            }

            _property = value;
        }
    }

    /// <summary>
    /// Gets or sets the value to set.
    /// </summary>
    public object? Value
    {
        get => _value;
        set
        {
            CheckSealed();
            if (ReferenceEquals(value, DependencyProperty.UnsetValue))
            {
                throw new ArgumentException("DependencyProperty.UnsetValue is not a valid Setter value.", nameof(value));
            }

            _value = value;
        }
    }

    /// <summary>
    /// Gets or sets the name of the element to apply the setter to.
    /// </summary>
    public string? TargetName
    {
        get => _targetName;
        set
        {
            CheckSealed();
            _targetName = value;
        }
    }

    /// <summary>
    /// Gets or sets the unresolved property name for deferred resolution.
    /// When a Setter has a TargetName pointing to a different element type than the Style's TargetType,
    /// the DependencyProperty cannot be resolved at parse time. The property name is stored here
    /// and resolved at runtime against the actual target element type.
    /// </summary>
    public string? PropertyName
    {
        get => _propertyName;
        set
        {
            CheckSealed();
            _propertyName = value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Setter"/> class.
    /// </summary>
    public Setter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Setter"/> class with the specified property and value.
    /// </summary>
    /// <param name="property">The property to set.</param>
    /// <param name="value">The value to set.</param>
    public Setter(DependencyProperty property, object? value)
    {
        Property = property;
        Value = value;
    }

    /// <summary>
    /// Initializes a setter for a named template element.
    /// </summary>
    public Setter(DependencyProperty property, object? value, string? targetName)
        : this(property, value)
    {
        TargetName = targetName;
    }

    void ISupportInitialize.BeginInit()
    {
        CheckSealed();
        if (_isInitializing)
        {
            throw new InvalidOperationException("Setter initialization has already begun.");
        }

        _isInitializing = true;
    }

    void ISupportInitialize.EndInit()
    {
        CheckSealed();
        if (!_isInitializing)
        {
            throw new InvalidOperationException("Setter initialization has not begun.");
        }

        if (_hasUnresolvedProperty)
        {
            object? resolvedProperty = ConvertDeferredValue(
                _unresolvedPropertyConverter,
                _unresolvedPropertyContext,
                _unresolvedPropertyCulture,
                _unresolvedProperty);

            if (resolvedProperty is DependencyProperty dependencyProperty)
            {
                Property = dependencyProperty;
            }
            else if (resolvedProperty is string propertyName)
            {
                PropertyName = propertyName;
            }
            else
            {
                throw new InvalidOperationException("Setter.Property type conversion did not produce a DependencyProperty or property name.");
            }

            ClearDeferredProperty();
        }

        if (_hasUnresolvedValue)
        {
            Value = ConvertDeferredValue(
                _unresolvedValueConverter,
                _unresolvedValueContext,
                _unresolvedValueCulture,
                _unresolvedValue);
            ClearDeferredValue();
        }

        if (_property != null && _value is string)
        {
            var converted = ConvertValueIfNeeded(_value, _property.PropertyType);
            if (_property.IsValidType(converted))
            {
                _value = converted;
            }
        }

        _isInitializing = false;
    }

    /// <summary>Receives resource and binding markup extensions used as Setter.Value.</summary>
    public static void ReceiveMarkupExtension(object targetObject, XamlSetMarkupExtensionEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(targetObject);
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (targetObject is not Setter setter || eventArgs.Member.Name != nameof(Value))
        {
            return;
        }

        setter.CheckSealed();
        MarkupExtension extension = eventArgs.MarkupExtension;
        if (extension is BindingBase)
        {
            setter.Value = extension;
            eventArgs.Handled = true;
            return;
        }

        if (extension is IStyleResourceMarkupExtension)
        {
            setter.Value = ProvideStyleResourceValue(extension, eventArgs.ServiceProvider);
            eventArgs.Handled = true;
        }
    }

    /// <summary>Defers Setter.Property and Setter.Value conversion until EndInit.</summary>
    public static void ReceiveTypeConverter(object targetObject, XamlSetTypeConverterEventArgs eventArgs)
    {
        if (targetObject is not Setter setter)
        {
            throw new ArgumentNullException(nameof(targetObject));
        }

        ArgumentNullException.ThrowIfNull(eventArgs);
        setter.CheckSealed();

        if (eventArgs.Member.Name == nameof(Property))
        {
            setter._unresolvedProperty = eventArgs.Value;
            setter._unresolvedPropertyConverter = eventArgs.TypeConverter;
            setter._unresolvedPropertyContext = eventArgs.ServiceProvider;
            setter._unresolvedPropertyCulture = eventArgs.CultureInfo;
            setter._hasUnresolvedProperty = true;
            eventArgs.Handled = true;
        }
        else if (eventArgs.Member.Name == nameof(Value))
        {
            setter._unresolvedValue = eventArgs.Value;
            setter._unresolvedValueConverter = eventArgs.TypeConverter;
            setter._unresolvedValueContext = eventArgs.ServiceProvider;
            setter._unresolvedValueCulture = eventArgs.CultureInfo;
            setter._hasUnresolvedValue = true;
            eventArgs.Handled = true;
        }
    }

    private static object? ConvertDeferredValue(
        TypeConverter? converter,
        ITypeDescriptorContext? context,
        System.Globalization.CultureInfo? culture,
        object? value)
        => converter is null ? value : converter.ConvertFrom(context, culture, value!);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only framework resource markup extensions, which do not reflect over user types, reach this helper.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Framework resource markup extensions do not emit runtime code.")]
    private static object? ProvideStyleResourceValue(MarkupExtension extension, IServiceProvider serviceProvider)
        => extension.ProvideValue(serviceProvider);

    private void ClearDeferredProperty()
    {
        _unresolvedProperty = null;
        _unresolvedPropertyConverter = null;
        _unresolvedPropertyContext = null;
        _unresolvedPropertyCulture = null;
        _hasUnresolvedProperty = false;
    }

    private void ClearDeferredValue()
    {
        _unresolvedValue = null;
        _unresolvedValueConverter = null;
        _unresolvedValueContext = null;
        _unresolvedValueCulture = null;
        _hasUnresolvedValue = false;
    }

    internal override void Seal()
    {
        if (IsSealed)
        {
            return;
        }

        if (_isInitializing)
        {
            throw new InvalidOperationException("A Setter cannot be sealed while it is being initialized.");
        }

        if (_property == null && string.IsNullOrWhiteSpace(_propertyName))
        {
            throw new InvalidOperationException("Setter.Property must be specified before the Setter is sealed.");
        }

        base.Seal();
    }

    /// <summary>
    /// Applies this setter to the specified element.
    /// </summary>
    internal void Apply(FrameworkElement element, DependencyObject.LayerValueSource source = DependencyObject.LayerValueSource.StyleSetter)
    {
        var target = GetTarget(element);
        if (target == null)
            return;

        // Resolve the property - may need deferred resolution for TargetName setters
        var resolvedProperty = Property;
        if (resolvedProperty == null && PropertyName != null)
        {
            resolvedProperty = ResolveDependencyPropertyByName(PropertyName, target.GetType());
        }
        if (resolvedProperty == null)
        {
            // A setter whose property never resolves is a markup bug, and swallowing it here
            // is how it stays invisible: the style applies "successfully", the element just
            // silently misses one property. Real case: `Property="TextOptions.TextHintingMode"`
            // parsed fine, resolved to null, and did nothing — several rounds of debugging went
            // into the wrong place because the setter looked like it was in effect.
            //
            // Report once per (property token, target type) so a setter on a long list keeps the
            // log to one line instead of one per element, then keep going: dropping one setter is
            // survivable, and throwing here would run during layout for every element.
            ReportUnresolvedSetter(PropertyName, target.GetType());
            return;
        }

        // Resolve the actual property on the target type
        // This handles the case where the property was resolved against the Style's TargetType
        // but the setter targets a different element type via TargetName
        var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
        if (actualProperty == null)
            return;

        // 注意：这里**不**因为"目标已有 local 值"就跳过。style 值必须照常落进 StyleSetter 层，
        // 由层优先级去裁决谁可见（local 天然赢）。早先的短路让 style 值根本不存在于层里，于是：
        //   • 标记里 <Button Background="Red" Style="{StaticResource X}"/> —— local 值总是先于
        //     style 应用（style 在入树时才应用），style 的 Background setter 被整条丢弃；
        //   • 之后 ClearValue(BackgroundProperty) 回落到 DP 默认值而不是 style 值；
        //     SetCurrentValue / 动画结束后的回落同理。
        // All styles share the same setter layer. A more specific setter must retire
        // an earlier binding before its next source update can overwrite this value.
        if (s_activeBindingSetters.TryGetValue(target, out var activeSetters) &&
            activeSetters.TryGetValue((actualProperty, source), out var previousSetter) &&
            !ReferenceEquals(previousSetter, this))
        {
            previousSetter.RemoveBindingValue(target);
        }

        if (Value is IDynamicResourceReference dynamicReference)
        {
            DynamicResourceBindingOperations.SetDynamicResource(
                element, target,
                actualProperty,
                dynamicReference.ResourceKey,
                source);
            return;
        }

        // 走到这里说明本 setter 要写"非 dynamic-resource"的值。下层样式（典型为主题默认
        // 样式，它与本样式共用 StyleSetter 层）可能已在同一 DP 上建立 {ThemeResource}
        // 订阅——不清掉的话，下一次资源刷新会把主题值重新灌进层里，覆盖我们即将写入的值。
        // 订阅若来自 local SetDynamicResource 则层不匹配、不受影响（且 local 值存在时
        // 上面的 HasLocalValue 早已短路）。
        DynamicResourceBindingOperations.ClearDynamicResource(
            target, actualProperty, source);

        // Setter.Value 可以是一个 BindingBase（典型场景：jalxaml 里写
        // <Setter Property="Foo" Value="{TemplateBinding Bar}" /> 或 RelativeSource Binding）。
        //
        // 不能直接 target.SetBinding(actualProperty, binding)：BindingExpression 写目标走的是
        // Target.SetValue → **local 层**。那样一条 style setter 会拿到 local 优先级，压过所有
        // trigger —— 结果就是"给控件加了个 {Binding} 的 Foreground setter，hover/pressed 全部
        // 失效"；而且 Style 被换掉时 Setter.Remove 只清 StyleSetter 层，绑定会留在元素上。
        //
        // 改用与 TriggerBase 相同的 shadow-property 方案：绑定跑在本 setter 私有的 attached DP
        // 上，把它的当前值镜像进 StyleSetter 层，优先级与生命周期都归位。
        if (Value is BindingBase binding)
        {
            ApplyBindingValue(target, actualProperty, binding, source);
            return;
        }

        // Convert value to the correct type if needed
        var valueToSet = ConvertValueIfNeeded(Value, actualProperty.PropertyType);

        // Defense (WPF StyleHelper parity): if the post-conversion value cannot legally inhabit the
        // target DP — an unconvertible string / type-mismatched object into a value-type property, or a
        // null into a non-nullable value type — do NOT pin it into the StyleSetter layer; the getter
        // would throw InvalidCastException / NullReferenceException at layout. Degrade to default by
        // clearing this layer's contribution, mirroring the TemplateBinding transfer guards.
        if (!actualProperty.IsValidType(valueToSet))
        {
            target.ClearLayerValue(
                actualProperty,
                source,
                allowAutoTransition: false);
            return;
        }

        // allowAutoTransition: false —— style setter 是**基线**值，不是动态状态切换。
        // UIElement.ScheduleAutomaticTransitionArm 的注释把这条写成了不变量（"Style.Setter
        // (baseline) 走 allowAutoTransition=false"），但此处一直在用默认 true 的重载：主题切换 /
        // 隐式样式重算 / Style 换绑时，每个带 Transition 的属性都会从旧值动画过去，还会打断
        // 正在进行的 trigger 过渡。只有 Trigger.Setter 才参与自动过渡。
        target.SetLayerValue(
            actualProperty,
            valueToSet,
            source,
            allowAutoTransition: false);

        // 宿主元素的失效由唯一调用链 FrameworkElement.UpdateStyleStack 末尾的 InvalidateVisual
        // 统一负责，这里不重复；只有 TargetName 打到别的元素时才需要单独安排一次。
        if (!ReferenceEquals(target, element))
        {
            EnsureTargetInvalidation(target, actualProperty);
        }
    }

    /// <summary>
    /// 本 setter 私有的 shadow attached DP（懒注册）。绑定挂在它上面求值，真正的目标属性只收到
    /// 镜像进 StyleSetter 层的结果值。一个 Setter 实例一个 shadow DP，跨元素靠 attached 值天然隔离。
    /// </summary>
    private DependencyProperty? _bindingShadowProperty;
    private readonly object _bindingShadowGate = new();
    private static int s_styleSetterBindingPropertyCounter;

    private sealed class BindingSetterState
    {
        public required DependencyProperty TargetProperty { get; init; }
        public required DependencyObject.LayerValueSource Source { get; init; }
        public required DependencyProperty ShadowProperty { get; init; }
        public required Action<DependencyProperty, object?, object?> Handler { get; init; }
    }

    private readonly ConditionalWeakTable<DependencyObject, BindingSetterState> _bindingStates = new();
    private static readonly ConditionalWeakTable<DependencyObject,
        Dictionary<(DependencyProperty Property, DependencyObject.LayerValueSource Source), Setter>>
        s_activeBindingSetters = new();

    private DependencyProperty GetBindingShadowProperty()
    {
        if (_bindingShadowProperty != null)
            return _bindingShadowProperty;

        lock (_bindingShadowGate)
        {
            return _bindingShadowProperty ??= DependencyProperty.RegisterAttached(
                $"_StyleSetterBindingValue{Interlocked.Increment(ref s_styleSetterBindingPropertyCounter)}",
                typeof(object),
                typeof(Setter),
                new PropertyMetadata(null));
        }
    }

    private void ApplyBindingValue(
        DependencyObject target,
        DependencyProperty actualProperty,
        BindingBase binding, DependencyObject.LayerValueSource source)
    {
        if (_bindingStates.TryGetValue(target, out var existing))
        {
            PublishBindingValue(target, existing);
            return;
        }

        var shadowProperty = GetBindingShadowProperty();

        BindingSetterState? state = null;
        Action<DependencyProperty, object?, object?> handler = (changedProperty, _, _) =>
        {
            if (!ReferenceEquals(changedProperty, shadowProperty) || state is null)
                return;

            PublishBindingValue(target, state);
        };

        state = new BindingSetterState
        {
            TargetProperty = actualProperty,
            Source = source,
            ShadowProperty = shadowProperty,
            Handler = handler
        };

        _bindingStates.Add(target, state);
        s_activeBindingSetters.GetOrCreateValue(target)[(actualProperty, source)] = this;
        target.PropertyChangedInternal += handler;
        target.SetBinding(shadowProperty, binding);
        PublishBindingValue(target, state);
    }

    private static void PublishBindingValue(DependencyObject target, BindingSetterState state)
    {
        var value = target.GetValue(state.ShadowProperty);
        if (ReferenceEquals(value, DependencyProperty.UnsetValue) ||
            !state.TargetProperty.IsValidType(value))
        {
            target.ClearLayerValue(
                state.TargetProperty,
                state.Source,
                allowAutoTransition: false);
        }
        else
        {
            target.SetLayerValue(
                state.TargetProperty,
                value,
                state.Source,
                allowAutoTransition: false);
        }

        EnsureTargetInvalidation(target, state.TargetProperty);
    }

    private void RemoveBindingValue(DependencyObject target)
    {
        if (!_bindingStates.TryGetValue(target, out var state))
            return;

        _bindingStates.Remove(target);
        if (s_activeBindingSetters.TryGetValue(target, out var activeSetters) &&
            activeSetters.TryGetValue((state.TargetProperty, state.Source), out var activeSetter) &&
            ReferenceEquals(activeSetter, this))
        {
            activeSetters.Remove((state.TargetProperty, state.Source));
        }
        target.PropertyChangedInternal -= state.Handler;
        target.ClearBinding(state.ShadowProperty);
        target.ClearValue(state.ShadowProperty);
    }

    /// <summary>
    /// Converts a value to the target type if needed.
    /// </summary>
    private static object? ConvertValueIfNeeded(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // Handle nullable types - get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var actualType = underlyingType ?? targetType;

        // String to target type conversion
        if (value is string stringValue)
        {
            // Numeric / bool / enum string conversions are best-effort: on an
            // unparseable value we fall through to `return value` (below), so the
            // caller's IsValidType guard (Setter.Apply / ApplyTriggerSetters) skips
            // the setter instead of this throwing and tearing down the whole style
            // application. (Real bug this caught: a default StatusBar-family trigger
            // setting a double property to "Auto" — WPF's spelling of double.NaN —
            // used to throw FormatException out of double.Parse and crash startup.)
            if (actualType == typeof(double))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return double.NaN;
                return double.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble)
                    ? parsedDouble
                    : value;
            }
            if (actualType == typeof(float))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return float.NaN;
                return float.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedFloat)
                    ? parsedFloat
                    : value;
            }
            if (actualType == typeof(int))
                return int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedInt)
                    ? parsedInt
                    : value;
            if (actualType == typeof(bool))
                return bool.TryParse(stringValue, out var parsedBool) ? parsedBool : value;
            if (actualType.IsEnum)
                return Enum.TryParse(actualType, stringValue, ignoreCase: true, out var parsedEnum) ? parsedEnum : value;
            if (actualType == typeof(CornerRadius))
                return ParseCornerRadius(stringValue);
            if (actualType == typeof(Thickness))
                return ParseThickness(stringValue);
            if (actualType == typeof(Cursor))
                return ParseCursor(stringValue);

            // 没命中 hardcoded 列表 — 走 Style 提供的字符串值转换器钩子。
            // 详细说明见 TriggerBase.ConvertValueIfNeeded 的同名 fallback。
            var converter = Style.StringValueConverter;
            if (converter != null)
            {
                try
                {
                    var converted = converter(stringValue, actualType);
                    if (converted != null && actualType.IsInstanceOfType(converted))
                        return converted;
                }
                catch
                {
                    // 转换失败不抛 — 让上层防御逻辑处理。
                }
            }
        }

        return value;
    }

    private static Cursor? ParseCursor(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        if (Enum.TryParse<CursorType>(trimmed, ignoreCase: true, out var cursorType))
            return new Cursor(cursorType);

        return null;
    }

    /// <summary>
    /// Parses a CornerRadius from a string value.
    /// </summary>
    private static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new CornerRadius(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new CornerRadius(0)
        };
    }

    /// <summary>
    /// Parses a Thickness from a string value.
    /// </summary>
    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            2 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new Thickness(0)
        };
    }

    /// <summary>
    /// Removes this setter from the specified element.
    /// </summary>
    internal void Remove(FrameworkElement element, DependencyObject.LayerValueSource source = DependencyObject.LayerValueSource.StyleSetter)
    {
        var target = GetTarget(element);
        if (target == null) return;

        // Resolve the property - may need deferred resolution for TargetName setters
        var resolvedProperty = Property;
        if (resolvedProperty == null && PropertyName != null)
        {
            resolvedProperty = ResolveDependencyPropertyByName(PropertyName, target.GetType());
        }
        if (resolvedProperty == null) return;

        // Resolve the actual property on the target type
        var actualProperty = ResolvePropertyForTarget(resolvedProperty, target);
        if (actualProperty == null) return;

        // 拆掉 shadow 绑定，否则 Style 换掉之后绑定还活着，源一变就把值重新灌回层里。
        RemoveBindingValue(target);

        DynamicResourceBindingOperations.ClearDynamicResource(
            target,
            actualProperty,
            source);
        target.ClearLayerValue(
            actualProperty,
            source,
            allowAutoTransition: false);

        if (!ReferenceEquals(target, element))
        {
            EnsureTargetInvalidation(target, actualProperty);
        }
    }

    /// <summary>
    /// Resolves the actual DependencyProperty for the target element type.
    /// This handles the case where the property was resolved against the Style's TargetType
    /// but the setter targets a different element type via TargetName.
    /// </summary>
    private static DependencyProperty? ResolvePropertyForTarget(DependencyProperty originalProperty, DependencyObject target)
    {
        var targetType = target.GetType();

        // If the property is already from this type or an ancestor, use it directly
        if (originalProperty.IsAttached || originalProperty.OwnerType.IsAssignableFrom(targetType))
        {
            return originalProperty;
        }

        // Resolve via the AOT-safe DependencyProperty registry (no reflection).
        return DependencyProperty.FromName(targetType, originalProperty.Name) ?? originalProperty;
    }

    /// <summary>
    /// Resolves a DependencyProperty by name on the target type.
    /// Used for deferred resolution when the property couldn't be resolved at parse time.
    /// </summary>
    internal static DependencyProperty? ResolveDependencyPropertyByName(string propertyName, Type targetType)
    {
        // AOT-safe lookup via the DependencyProperty registry.
        return DependencyProperty.FromName(targetType, propertyName);
    }

    // Deduplicates the unresolved-setter diagnostic. Keyed by (property token, target type) so a
    // style applied to hundreds of elements reports once, not once per element.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, Type), byte>
        s_reportedUnresolvedSetters = new();

    private static void ReportUnresolvedSetter(string? propertyName, Type targetType)
    {
        if (string.IsNullOrEmpty(propertyName))
            return;
        if (!s_reportedUnresolvedSetters.TryAdd((propertyName, targetType), 0))
            return;

        var hint = propertyName.Contains('.')
            ? " (attached property — is its owner type reachable from the JALXAML namespace? " +
              "framework owners need an [assembly: XmlnsDefinition] entry for their CLR namespace)"
            : string.Empty;

        var message =
            $"[Jalium.UI] Setter.Property '{propertyName}' did not resolve on '{targetType.Name}'; " +
            $"the setter was skipped{hint}.";

        System.Diagnostics.Debug.WriteLine(message);
        Console.Error.WriteLine(message);
    }

    internal (FrameworkElement Target, DependencyProperty Property)? ResolveTargetProperty(FrameworkElement element)
    {
        if (GetTarget(element) is not FrameworkElement target) return null;
        var property = Property ?? (PropertyName == null ? null : ResolveDependencyPropertyByName(PropertyName, target.GetType()));
        if (property == null) return null;
        var actual = ResolvePropertyForTarget(property, target);
        return actual == null ? null : (target, actual);
    }

    private DependencyObject? GetTarget(FrameworkElement element)
    {
        if (string.IsNullOrEmpty(TargetName))
            return element;

        // Look up named element in the template scope
        // First, try using the element's FindName method
        if (element.FindName(TargetName) is DependencyObject found)
        {
            return found;
        }

        // If that fails, search the visual tree starting from the element
        int visitedNodes = 0;
        var result = SearchVisualTreeForName(element, TargetName, ref visitedNodes);
        return result;
    }

    private static void EnsureTargetInvalidation(DependencyObject target, DependencyProperty property)
    {
        // Definitions and other named dependency objects publish their own
        // metadata notifications; only visual targets need the paint fallback.
        if (target is FrameworkElement element) TriggerBase.EnsureSetterInvalidation(element, property);
    }

    /// <summary>Recursively searches the visual tree for an element with the specified name.</summary>
    private static FrameworkElement? SearchVisualTreeForName(Visual? visual, string name, ref int visitedNodes)
    {
        if (visual == null) return null;
        visitedNodes++;

        // Check if this element has the name we're looking for
        if (visual is FrameworkElement fe && fe.Name == name)
        {
            return fe;
        }

        // Search children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            var result = SearchVisualTreeForName(child, name, ref visitedNodes);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

/// <summary>
/// Base class for triggers that conditionally apply property values.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Setters")]
public abstract class TriggerBase : DependencyObject
{
    private bool _isSealed;
    private TriggerActionCollection? _enterActions;
    private TriggerActionCollection? _exitActions;

    /// <summary>
    /// Gets the collection of setters to apply when the trigger is active.
    /// </summary>
    public SetterBaseCollection Setters { get; } = new();

    /// <summary>Gets the actions invoked when this trigger becomes active.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public TriggerActionCollection EnterActions
        => _enterActions ??= CreateActionCollection();

    /// <summary>Gets the actions invoked when this trigger becomes inactive.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public TriggerActionCollection ExitActions
        => _exitActions ??= CreateActionCollection();

    internal bool HasEnterActions => _enterActions is { Count: > 0 };

    internal bool HasExitActions => _exitActions is { Count: > 0 };

    private protected override bool IsSealedCore => _isSealed;

    /// <summary>
    /// Tracks which properties this trigger has set for each element.
    /// Used to properly decrement the active count in shared storage.
    /// </summary>
    private readonly ConditionalWeakTable<FrameworkElement, HashSet<DependencyProperty>> _activeSetters = new();

    private sealed class ResolvedTriggerSetter
    {
        public required Setter Setter { get; init; }
        public required FrameworkElement Target { get; init; }
        public required DependencyProperty Property { get; init; }
    }

    /// <summary>
    /// Setter TargetName and dependency-property resolution are invariant for one trigger
    /// attachment. Cache them for the lifetime of the attachment so pointer-state transitions
    /// do not repeatedly walk the visual tree or query the dependency-property registry.
    /// </summary>
    private readonly ConditionalWeakTable<FrameworkElement, ResolvedTriggerSetter[]> _resolvedSetters = new();

    private sealed class StyleTriggerAttachmentSet
    {
        public List<TriggerBase> Triggers { get; } = [];
    }

    /// <summary>
    /// All style triggers that are actually applied to an element, in application order.
    /// This includes theme styles, BasedOn chains, implicit styles, and explicit styles.
    /// A single StyleTrigger value layer is shared by those sources, so precedence must use
    /// this complete order instead of looking only at ParentStyle.Triggers.
    /// </summary>
    private static readonly ConditionalWeakTable<FrameworkElement, StyleTriggerAttachmentSet>
        s_styleTriggerAttachments = new();

    private sealed class TriggerBindingState
    {
        public required FrameworkElement Target { get; init; }
        public required DependencyProperty TargetProperty { get; init; }
        public required DependencyProperty ShadowProperty { get; init; }
        public required Action<DependencyProperty, object?, object?> Handler { get; init; }
    }

    /// <summary>
    /// A Binding setter cannot be installed directly on its target property: doing so would
    /// replace the template's baseline binding and leave the trigger value behind when
    /// overlapping triggers deactivate out of order. Bindings therefore evaluate on a private
    /// shadow property and copy their current value into the trigger layer only while they win.
    /// </summary>
    private readonly ConditionalWeakTable<FrameworkElement, Dictionary<Setter, TriggerBindingState>>
        _bindingSetterStates = new();

    private readonly object _bindingShadowPropertyGate = new();
    private readonly Dictionary<Setter, DependencyProperty> _bindingShadowProperties = new();
    private static int s_triggerBindingPropertyCounter;

    /// <summary>
    /// Gets or sets the parent style that contains this trigger.
    /// </summary>
    internal Style? ParentStyle { get; set; }

    /// <summary>
    /// 本 trigger 所属模板的 trigger 集合（声明顺序仲裁用），trigger 声明在
    /// ControlTemplate / DataTemplate 里时非空。
    ///
    /// <para><b>只设一次，永不清空。</b>一个 ControlTemplate（尤其主题模板）是被成百个控件
    /// **共享**的单例，模板里的 TriggerBase 实例同样共享。早先的实现在控件 teardown 时把它
    /// 置回 null，于是任何一个控件换模板/卸载，都会让仍然活着的同类控件上的同一个 trigger
    /// 变成"无宿主模板"：写值的层从 trigger 层掉回 StyleTrigger 层（写/清不同层 → 值被永久
    /// 钉死），并且 <see cref="GetSiblingTriggers"/> 拿不到兄弟集合 → 声明顺序仲裁彻底失效
    /// → 退化成 last-write-wins（hover/pressed 残留）。声明归属是 trigger 的不变量，
    /// 与"当前挂在哪个元素上"无关，因此这里按不变量处理。</para>
    /// </summary>
    internal IList<TriggerBase>? ParentTemplateTriggers { get; private set; }

    /// <summary>
    /// 宿主元素是否就是"被模板化的控件本身"。ControlTemplate 把 trigger 挂在控件上
    /// （true）；DataTemplate 把 trigger 挂在模板内容的根元素上（false，整棵内容树都是
    /// 模板生成物）。这一位决定无 TargetName 的 setter 落哪个 trigger 层，见
    /// <see cref="GetTriggerLayerSource"/>。
    /// </summary>
    private bool _hostIsTemplatedParent;

    /// <summary>
    /// 声明本 trigger 归属某个模板。幂等；重复调用同一集合不做任何事。
    /// </summary>
    internal void DeclareTemplateOwner(IList<TriggerBase> owner, bool hostIsTemplatedParent)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ParentTemplateTriggers = owner;
        _hostIsTemplatedParent = hostIsTemplatedParent;
    }

    /// <summary>
    /// 解析本 trigger 的某条 setter 应当写入的值层。
    ///
    /// <para>Style 上的 trigger → <c>StyleTrigger</c>。</para>
    /// <para>模板上的 trigger 分两种（WPF 也是这么分的）：带 TargetName 打到模板内具名部件的，
    /// 代表宿主控件对自己模板部件的支配，落 <c>ParentTemplateTrigger</c>（仅次于 local）；
    /// 不带 TargetName、打在被模板化控件自身上的，落 <c>TemplateTrigger</c>——它必须**低于**
    /// 该控件自己 Style 上的 trigger，否则用户写的 <c>&lt;Style.Triggers&gt;</c> 永远盖不过
    /// 主题模板内建的 hover/pressed 反馈。</para>
    /// </summary>
    private DependencyObject.LayerValueSource GetTriggerLayerSource(
        FrameworkElement element,
        FrameworkElement target)
    {
        if (ParentTemplateTriggers == null)
            return DependencyObject.LayerValueSource.StyleTrigger;

        return _hostIsTemplatedParent && ReferenceEquals(target, element)
            ? DependencyObject.LayerValueSource.TemplateTrigger
            : DependencyObject.LayerValueSource.ParentTemplateTrigger;
    }

    internal void RegisterStyleAttachment(FrameworkElement element)
    {
        var attachments = s_styleTriggerAttachments.GetOrCreateValue(element);
        attachments.Triggers.Add(this);
    }

    internal void UnregisterStyleAttachment(FrameworkElement element)
    {
        if (!s_styleTriggerAttachments.TryGetValue(element, out var attachments))
            return;

        attachments.Triggers.Remove(this);
        if (attachments.Triggers.Count == 0)
        {
            s_styleTriggerAttachments.Remove(element);
        }
    }

    /// <summary>
    /// Attaches this trigger to the specified element.
    /// </summary>
    internal abstract void Attach(FrameworkElement element);

    /// <summary>
    /// Detaches this trigger from the specified element.
    /// </summary>
    internal abstract void Detach(FrameworkElement element);

    /// <summary>
    /// Gets whether this trigger is currently active for the specified element.
    /// </summary>
    internal abstract bool IsActiveForElement(FrameworkElement element);

    /// <summary>
    /// Seals the setters owned by this trigger.
    /// </summary>
    internal virtual void Seal()
    {
        if (_isSealed)
        {
            return;
        }

        Setters.Seal();
        _enterActions?.Seal(this);
        _exitActions?.Seal(this);
        _isSealed = true;
    }

    /// <summary>Throws when a sealed trigger is modified.</summary>
    protected void CheckTriggerSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A sealed TriggerBase cannot be changed.");
        }
    }

    /// <summary>Applies setters and then invokes enter actions for a state transition.</summary>
    protected void EnterTrigger(FrameworkElement element)
    {
        ApplyTriggerSetters(element);
        InvokeActions(_enterActions, element);
    }

    /// <summary>Removes setters and then invokes exit actions for a state transition.</summary>
    protected void ExitTrigger(FrameworkElement element)
    {
        RemoveTriggerSetters(element);
        InvokeActions(_exitActions, element);
    }

    private TriggerActionCollection CreateActionCollection()
    {
        var actions = new TriggerActionCollection(this);
        if (_isSealed)
        {
            actions.Seal(this);
        }

        return actions;
    }

    private static void InvokeActions(TriggerActionCollection? actions, FrameworkElement element)
    {
        if (actions is null)
        {
            return;
        }

        foreach (TriggerAction action in actions)
        {
            action.Invoke(element);
        }
    }

    /// <summary>
    /// Applies the trigger's setters. WPF semantics: trigger order in the declaring
    /// collection (Style.Triggers / ControlTemplate.Triggers) determines precedence —
    /// later triggers win over earlier ones for the same target property. We honor that
    /// here by SKIPPING setters whose (target, dp) is *already owned by a later sibling
    /// trigger that is also active*. Otherwise an earlier-defined trigger activating
    /// AFTER a later one (e.g. user hovers a RadioButton that's already IsChecked=true)
    /// would clobber the later trigger's value just because the layer dictionary uses
    /// last-write-wins — which is exactly the "hover residue on a checked button" bug.
    /// </summary>
    protected void ApplyTriggerSetters(FrameworkElement element)
    {
        var siblings = GetSiblingTriggers(element);
        var myIndex = siblings?.IndexOf(this) ?? -1;

        foreach (var resolvedSetter in GetResolvedSetters(element))
        {
            var setter = resolvedSetter.Setter;
            var target = resolvedSetter.Target;
            var actualProperty = resolvedSetter.Property;
            var layerSource = GetTriggerLayerSource(element, target);

            // Track that this trigger conceptually owns this property — even if we end up
            // skipping the write below, we still need to remember the ownership so that
            // RemoveTriggerSetters can later re-apply ours when the later sibling deactivates.
            TrackActiveSetter(target, actualProperty);

            // Keep trigger bindings separate from the target property's baseline binding.
            // The shadow binding remains live even while a later trigger wins, so taking over
            // later uses the current source value instead of a stale snapshot.
            if (setter.Value is BindingBase binding)
            {
                var bindingState = EnsureBindingSetterState(
                    element,
                    setter,
                    target,
                    actualProperty,
                    binding);

                if (HasLaterActiveOwner(
                        element,
                        target,
                        actualProperty,
                        siblings,
                        myIndex))
                {
                    EnsureSetterInvalidation(target, actualProperty);
                    continue;
                }

                DynamicResourceBindingOperations.ClearDynamicResource(
                    target,
                    actualProperty,
                    layerSource);
                ApplyBindingSetterValue(bindingState, layerSource);
                continue;
            }

            // Honor WPF precedence: defer to any active later sibling.
            if (HasLaterActiveOwner(
                    element,
                    target,
                    actualProperty,
                    siblings,
                    myIndex))
            {
                EnsureSetterInvalidation(target, actualProperty);
                continue;
            }

            if (setter.Value is IDynamicResourceReference dynamicReference)
            {
                DynamicResourceBindingOperations.SetDynamicResource(target, actualProperty, dynamicReference.ResourceKey, layerSource);
                EnsureSetterInvalidation(target, actualProperty);
                continue;
            }

            // Trigger 走动态状态切换路径，必须参与自动过渡 — 这是 hover/pressed
            // 反馈动画的入口；Style.Setter (baseline) 走 allowAutoTransition=false。
            var valueToSet = ConvertValueIfNeeded(setter.Value, actualProperty.PropertyType);

            // 防御（WPF StyleHelper 对齐）：转换后若值仍无法合法存入目标 DP —— 类型不匹配的对象、
            // 没命中转换器的字符串，或 null 写入非 nullable 值类型 —— 跳过本 setter，而不是把脏值塞进
            // layer（否则渲染时从该 DP 强转目标类型会抛 InvalidCastException / NullReferenceException）。
            // 这取代了之前“转换前判断 + 排除 string”的守卫——后者漏掉了无法转换的字符串这一情形。
            if (!actualProperty.IsValidType(valueToSet))
            {
                continue;
            }

            DynamicResourceBindingOperations.ClearDynamicResource(
                target,
                actualProperty,
                layerSource);
            target.SetLayerValue(actualProperty, valueToSet, layerSource, allowAutoTransition: true);
            EnsureSetterInvalidation(target, actualProperty);
        }
    }

    /// <summary>
    /// Returns whether a later trigger in the effective style/template order currently
    /// owns the specified target property.
    /// </summary>
    private static bool HasLaterActiveOwner(
        FrameworkElement element,
        FrameworkElement target,
        DependencyProperty property,
        IList<TriggerBase>? siblings,
        int myIndex)
    {
        if (siblings == null || myIndex < 0)
            return false;

        for (int i = myIndex + 1; i < siblings.Count; i++)
        {
            var later = siblings[i];
            if (!later.IsActiveForElement(element))
                continue;

            foreach (var resolvedSetter in later.GetResolvedSetters(element))
            {
                if (ReferenceEquals(resolvedSetter.Target, target) &&
                    ReferenceEquals(resolvedSetter.Property, property))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private TriggerBindingState EnsureBindingSetterState(
        FrameworkElement element,
        Setter setter,
        FrameworkElement target,
        DependencyProperty targetProperty,
        BindingBase binding)
    {
        var states = _bindingSetterStates.GetOrCreateValue(element);
        if (states.TryGetValue(setter, out var existing))
        {
            return existing;
        }

        var shadowProperty = GetBindingShadowProperty(setter);

        TriggerBindingState? state = null;
        Action<DependencyProperty, object?, object?> handler = (changedProperty, _, _) =>
        {
            if (changedProperty != shadowProperty || state is null)
                return;

            ApplyBindingSetterValueIfWinning(element, state);
        };

        state = new TriggerBindingState
        {
            Target = target,
            TargetProperty = targetProperty,
            ShadowProperty = shadowProperty,
            Handler = handler
        };

        states.Add(setter, state);
        target.PropertyChangedInternal += handler;
        target.SetBinding(shadowProperty, binding);
        return state;
    }

    private DependencyProperty GetBindingShadowProperty(Setter setter)
    {
        lock (_bindingShadowPropertyGate)
        {
            if (_bindingShadowProperties.TryGetValue(setter, out var property))
                return property;

            property = DependencyProperty.RegisterAttached(
                $"_TriggerBindingValue{Interlocked.Increment(ref s_triggerBindingPropertyCounter)}",
                typeof(object),
                typeof(TriggerBase),
                new PropertyMetadata(null));
            _bindingShadowProperties.Add(setter, property);
            return property;
        }
    }

    private void ApplyBindingSetterValueIfWinning(
        FrameworkElement element,
        TriggerBindingState state)
    {
        if (!IsActiveForElement(element))
            return;

        var siblings = GetSiblingTriggers(element);
        var myIndex = siblings?.IndexOf(this) ?? -1;
        if (HasLaterActiveOwner(
                element,
                state.Target,
                state.TargetProperty,
                siblings,
                myIndex))
            return;

        ApplyBindingSetterValue(state, GetTriggerLayerSource(element, state.Target));
    }

    private static void ApplyBindingSetterValue(
        TriggerBindingState state,
        DependencyObject.LayerValueSource layerSource)
    {
        var value = state.Target.GetValue(state.ShadowProperty);
        if (ReferenceEquals(value, DependencyProperty.UnsetValue) ||
            !state.TargetProperty.IsValidType(value))
        {
            state.Target.ClearLayerValue(
                state.TargetProperty,
                layerSource,
                allowAutoTransition: true);
        }
        else
        {
            state.Target.SetLayerValue(
                state.TargetProperty,
                value,
                layerSource,
                allowAutoTransition: true);
        }

        EnsureSetterInvalidation(state.Target, state.TargetProperty);
    }

    private void RemoveBindingSetterState(FrameworkElement element, Setter setter)
    {
        if (!_bindingSetterStates.TryGetValue(element, out var states) ||
            !states.Remove(setter, out var state))
        {
            return;
        }

        state.Target.PropertyChangedInternal -= state.Handler;
        state.Target.ClearBinding(state.ShadowProperty);
        state.Target.ClearValue(state.ShadowProperty);

        if (states.Count == 0)
        {
            _bindingSetterStates.Remove(element);
        }
    }

    private void ClearBindingSetterStates(FrameworkElement element)
    {
        if (!_bindingSetterStates.TryGetValue(element, out var states))
            return;

        foreach (var state in states.Values)
        {
            state.Target.PropertyChangedInternal -= state.Handler;
            state.Target.ClearBinding(state.ShadowProperty);
            state.Target.ClearValue(state.ShadowProperty);
        }

        states.Clear();
        _bindingSetterStates.Remove(element);
    }

    /// <summary>
    /// 当 setter 改的 DP 没有 metadata-level invalidation callback 时（例如纯数据 DP），
    /// 显式给 target 安排一次 visual 失效，否则改完没人通知 window 重绘。
    /// 已注册 callback 的 DP（OpacityProperty / RenderTransformProperty / Border.Background
    /// 等）由 SetLayerValue → MutateValue → OnPropertyChanged → metadata callback
    /// 链路自然 invalidate，**不**走这条兜底——避免对每个 setter 都 paranoid
    /// 整体 InvalidateVisual。compositionOnly DP 路由到 InvalidateComposition。
    /// </summary>
    internal static void EnsureSetterInvalidation(FrameworkElement target, DependencyProperty dp)
    {
        var metadata = dp.GetMetadata(target.GetType());
        if (metadata.PropertyChangedCallback != null)
            return; // metadata callback 已经处理了 invalidation

        if (metadata is FrameworkPropertyMetadata fpm && fpm.AffectsCompositionOnly)
        {
            target.InvalidateComposition();
        }
        else
        {
            target.InvalidateVisual();
        }
    }

    /// <summary>
    /// Resolves the actual DependencyProperty for the target element type.
    /// This handles the case where the property was resolved against the Style's TargetType
    /// but the setter targets a different element type via TargetName.
    /// </summary>
    private static DependencyProperty? ResolvePropertyForTarget(DependencyProperty originalProperty, FrameworkElement target)
    {
        var targetType = target.GetType();

        // If the property is already from this type or an ancestor, use it directly
        if (originalProperty.IsAttached || originalProperty.OwnerType.IsAssignableFrom(targetType))
        {
            return originalProperty;
        }

        // Resolve via the AOT-safe DependencyProperty registry (no reflection).
        return DependencyProperty.FromName(targetType, originalProperty.Name) ?? originalProperty;
    }

    /// <summary>
    /// Converts a value to the target type if needed.
    /// </summary>
    protected static object? ConvertValueIfNeeded(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // Handle nullable types - get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var actualType = underlyingType ?? targetType;

        // String to target type conversion
        if (value is string stringValue)
        {
            // Numeric / bool / enum string conversions are best-effort: on an
            // unparseable value we fall through to `return value` (below), so the
            // caller's IsValidType guard (Setter.Apply / ApplyTriggerSetters) skips
            // the setter instead of this throwing and tearing down the whole style
            // application. (Real bug this caught: a default StatusBar-family trigger
            // setting a double property to "Auto" — WPF's spelling of double.NaN —
            // used to throw FormatException out of double.Parse and crash startup.)
            if (actualType == typeof(double))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return double.NaN;
                return double.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble)
                    ? parsedDouble
                    : value;
            }
            if (actualType == typeof(float))
            {
                if (stringValue.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                    stringValue.Equals("NaN", StringComparison.OrdinalIgnoreCase))
                    return float.NaN;
                return float.TryParse(stringValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedFloat)
                    ? parsedFloat
                    : value;
            }
            if (actualType == typeof(int))
                return int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedInt)
                    ? parsedInt
                    : value;
            if (actualType == typeof(bool))
                return bool.TryParse(stringValue, out var parsedBool) ? parsedBool : value;
            if (actualType.IsEnum)
                return Enum.TryParse(actualType, stringValue, ignoreCase: true, out var parsedEnum) ? parsedEnum : value;
            if (actualType == typeof(CornerRadius))
                return ParseCornerRadius(stringValue);
            if (actualType == typeof(Thickness))
                return ParseThickness(stringValue);
            if (actualType == typeof(Cursor))
                return ParseCursor(stringValue);

            // 没命中 hardcoded 列表 — 走 Style 提供的字符串值转换器钩子（典型为 Xaml 的
            // TypeConverterRegistry 在 ModuleInitializer 里注入）。这避免了把 Cursor /
            // Brush / GridLength 等框架内置类型在 Core 层重复实现一遍。
            var converter = Style.StringValueConverter;
            if (converter != null)
            {
                try
                {
                    var converted = converter(stringValue, actualType);
                    if (converted != null && actualType.IsInstanceOfType(converted))
                        return converted;
                }
                catch
                {
                    // 转换失败不抛 — 留给后续防御逻辑（如 ApplyTriggerSetters 的类型不匹配跳过）
                    // 处理；否则一个错配的 Setter 会拖垮整个 Style 加载。
                }
            }
        }

        return value;
    }

    /// <summary>
    /// 把字符串解析成 <see cref="Cursor"/>。Core 自己处理这条路径是因为 Cursor 类型也在
    /// Core 程序集里，但完整的 CursorConverter 在 Jalium.UI.Input — Style.cs 不能反向引用。
    /// </summary>
    private static Cursor? ParseCursor(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        if (Enum.TryParse<CursorType>(trimmed, ignoreCase: true, out var cursorType))
            return new Cursor(cursorType);

        return null;
    }

    /// <summary>
    /// Parses a CornerRadius from a string value.
    /// </summary>
    private static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new CornerRadius(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new CornerRadius(0)
        };
    }

    /// <summary>
    /// Parses a Thickness from a string value.
    /// </summary>
    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return parts.Length switch
        {
            1 => new Thickness(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
            2 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
            4 => new Thickness(
                double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)),
            _ => new Thickness(0)
        };
    }

    /// <summary>
    /// Removes the trigger's setters when this trigger deactivates. Mirrors WPF precedence
    /// (later sibling wins) by recomputing what the layer value should be for each owned
    /// (target, dp) key:
    ///   – If any other sibling is still active and owns the key, write the LATEST
    ///     (declaration-order) sibling's value to the layer in one mutation. This avoids
    ///     a Clear→Set sequence that would fire two transitions back-to-back and flash
    ///     baseline (the visible source of "hover residue" on a still-checked button).
    ///   – If no sibling takes over, ClearLayerValue so the property falls back to baseline.
    /// Skips the layer write entirely when this trigger had been suppressed by a later
    /// sibling during ApplyTriggerSetters (layer was never ours — leaving it alone is
    /// correct, and any redundant SetLayerValue here would needlessly restart transitions).
    /// </summary>
    protected void RemoveTriggerSetters(FrameworkElement element)
    {
        var siblings = GetSiblingTriggers(element);
        var myIndex = siblings?.IndexOf(this) ?? -1;

        // Per owned key: either the layer is already owned by a later trigger, an earlier
        // active trigger takes over, or the trigger layer falls back to its baseline.
        foreach (var resolvedSetter in GetResolvedSetters(element))
        {
            var setter = resolvedSetter.Setter;
            var target = resolvedSetter.Target;
            var actualProperty = resolvedSetter.Property;
            var layerSource = GetTriggerLayerSource(element, target);
            if (!IsSetterActive(target, actualProperty))
                continue;

            FindWinningSiblingSetter(
                element,
                target,
                actualProperty,
                siblings,
                myIndex,
                out var takeoverTrigger,
                out var takeoverSetter,
                out var suppressedByLater);

            UntrackActiveSetter(target, actualProperty);

            // A trigger Binding runs on its own shadow property and must always be detached,
            // including when a later sibling currently owns the visible target property.
            if (setter.Value is BindingBase)
            {
                RemoveBindingSetterState(element, setter);
            }

            if (suppressedByLater)
            {
                // Layer belongs to a later sibling that's still active — leave it untouched.
                continue;
            }

            DynamicResourceBindingOperations.ClearDynamicResource(
                target,
                actualProperty,
                layerSource);

            if (takeoverTrigger != null && takeoverSetter != null)
            {
                takeoverTrigger.ApplyWinningSetter(
                    element,
                    takeoverSetter.Setter,
                    target,
                    actualProperty,
                    layerSource);
            }
            else
            {
                target.ClearLayerValue(actualProperty, layerSource, allowAutoTransition: true);
            }

            EnsureSetterInvalidation(target, actualProperty);
        }
    }

    private void FindWinningSiblingSetter(
        FrameworkElement element,
        FrameworkElement target,
        DependencyProperty property,
        IList<TriggerBase>? siblings,
        int myIndex,
        out TriggerBase? winningTrigger,
        out ResolvedTriggerSetter? winningSetter,
        out bool suppressedByLater)
    {
        winningTrigger = null;
        winningSetter = null;
        suppressedByLater = false;

        if (siblings == null)
            return;

        for (var index = 0; index < siblings.Count; index++)
        {
            var sibling = siblings[index];
            if (ReferenceEquals(sibling, this) || !sibling.IsActiveForElement(element))
                continue;

            foreach (var candidate in sibling.GetResolvedSetters(element))
            {
                if (!ReferenceEquals(candidate.Target, target) ||
                    !ReferenceEquals(candidate.Property, property))
                {
                    continue;
                }

                winningTrigger = sibling;
                winningSetter = candidate;
                if (myIndex >= 0 && index > myIndex)
                {
                    suppressedByLater = true;
                }
            }
        }
    }

    private void ApplyWinningSetter(
        FrameworkElement element,
        Setter setter,
        FrameworkElement target,
        DependencyProperty actualProperty,
        DependencyObject.LayerValueSource layerSource)
    {
        if (setter.Value is BindingBase binding)
        {
            DynamicResourceBindingOperations.ClearDynamicResource(
                target,
                actualProperty,
                layerSource);
            var bindingState = EnsureBindingSetterState(
                element,
                setter,
                target,
                actualProperty,
                binding);
            ApplyBindingSetterValue(bindingState, layerSource);
            return;
        }

        if (setter.Value is IDynamicResourceReference dynamicReference)
        {
            DynamicResourceBindingOperations.SetDynamicResource(
                target,
                actualProperty,
                dynamicReference.ResourceKey,
                layerSource);
            return;
        }

        var valueToSet = ConvertValueIfNeeded(setter.Value, actualProperty.PropertyType);
        if (actualProperty.IsValidType(valueToSet))
        {
            DynamicResourceBindingOperations.ClearDynamicResource(
                target,
                actualProperty,
                layerSource);
            target.SetLayerValue(
                actualProperty,
                valueToSet,
                layerSource,
                allowAutoTransition: true);
        }
        else
        {
            target.ClearLayerValue(
                actualProperty,
                layerSource,
                allowAutoTransition: true);
        }
    }

    private IList<TriggerBase>? GetSiblingTriggers(FrameworkElement element)
    {
        if (ParentTemplateTriggers != null)
            return ParentTemplateTriggers;

        if (ParentStyle != null &&
            s_styleTriggerAttachments.TryGetValue(element, out var attachments))
        {
            return attachments.Triggers;
        }

        return ParentStyle?.Triggers as IList<TriggerBase>;
    }

    private ResolvedTriggerSetter[] GetResolvedSetters(FrameworkElement element)
        => _resolvedSetters.GetValue(element, ResolveSetters);

    private ResolvedTriggerSetter[] ResolveSetters(FrameworkElement element)
    {
        List<ResolvedTriggerSetter>? resolved = null;

        foreach (var setterBase in Setters)
        {
            if (setterBase is not Setter setter)
                continue;

            var target = GetSetterTarget(element, setter.TargetName);
            if (target == null)
                continue;

            var property = setter.Property;
            if (property == null && setter.PropertyName != null)
            {
                property = Setter.ResolveDependencyPropertyByName(
                    setter.PropertyName,
                    target.GetType());
            }

            if (property == null)
                continue;

            property = ResolvePropertyForTarget(property, target);
            if (property == null)
                continue;

            resolved ??= [];

            // Multiple setters for the same target property follow declaration-order
            // semantics. Keep only the last declaration so activation/deactivation cannot
            // track and clear one property more than once.
            var replaced = false;
            for (var index = 0; index < resolved.Count; index++)
            {
                var existing = resolved[index];
                if (ReferenceEquals(existing.Target, target) &&
                    ReferenceEquals(existing.Property, property))
                {
                    resolved[index] = new ResolvedTriggerSetter
                    {
                        Setter = setter,
                        Target = target,
                        Property = property
                    };
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                resolved.Add(new ResolvedTriggerSetter
                {
                    Setter = setter,
                    Target = target,
                    Property = property
                });
            }
        }

        return resolved?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets the target element for a setter, handling TargetName lookup.
    /// Also reused by <see cref="Trigger"/> / <see cref="MultiTrigger"/> for SourceName
    /// (same logic: FindName 优先，失败则从 templated parent 起做一次 visual-tree 搜索）。
    /// </summary>
    protected static FrameworkElement? GetSetterTarget(FrameworkElement element, string? targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return element;

        // Look up named element in the template scope
        if (element.FindName(targetName) is FrameworkElement found)
            return found;

        // If that fails, search the visual tree starting from the element
        int visitedNodes = 0;
        var result = SearchVisualTreeForName(element, targetName, ref visitedNodes);
        return result;
    }

    /// <summary>
    /// Recursively searches the visual tree for an element with the specified name.
    /// </summary>
    private static FrameworkElement? SearchVisualTreeForName(Visual? visual, string name, ref int visitedNodes)
    {
        if (visual == null) return null;
        visitedNodes++;

        // Check if this element has the name we're looking for
        if (visual is FrameworkElement fe && fe.Name == name)
            return fe;

        // Search children
        var childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            var result = SearchVisualTreeForName(child, name, ref visitedNodes);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Clears all stored pre-trigger values for an element.
    /// </summary>
    protected void ClearPreTriggerValues(FrameworkElement element)
    {
        ClearBindingSetterStates(element);
        _activeSetters.Remove(element);
        _resolvedSetters.Remove(element);
    }

    private void TrackActiveSetter(FrameworkElement target, DependencyProperty property)
        => _activeSetters.GetOrCreateValue(target).Add(property);

    private bool IsSetterActive(FrameworkElement target, DependencyProperty property)
        => _activeSetters.TryGetValue(target, out var properties) && properties.Contains(property);

    private void UntrackActiveSetter(FrameworkElement target, DependencyProperty property)
    {
        if (!_activeSetters.TryGetValue(target, out var properties))
            return;

        properties.Remove(property);
        if (properties.Count == 0)
            _activeSetters.Remove(target);
    }
}

/// <summary>
/// Represents a trigger that applies property values when a property value equals a specified value.
/// </summary>
[XamlSetTypeConverter(nameof(ReceiveTypeConverter))]
public sealed class Trigger : TriggerBase, ISupportInitialize
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        /// <summary>
        /// 实际被订阅的"源"元素：SourceName=null 时就是 templated parent；
        /// SourceName 指向命名模板部件时是该部件。Detach 时按此还原订阅。
        /// </summary>
        public FrameworkElement? SourceElement;
    }

    private readonly ConditionalWeakTable<FrameworkElement, ElementState> _elementStates = new();
    private DependencyProperty? _property;
    private object? _value;
    private string? _sourceName;
    private bool _isInitializing;
    private object? _unresolvedProperty;
    private TypeConverter? _unresolvedPropertyConverter;
    private ITypeDescriptorContext? _unresolvedPropertyContext;
    private System.Globalization.CultureInfo? _unresolvedPropertyCulture;
    private bool _hasUnresolvedProperty;
    private object? _unresolvedValue;
    private TypeConverter? _unresolvedValueConverter;
    private ITypeDescriptorContext? _unresolvedValueContext;
    private System.Globalization.CultureInfo? _unresolvedValueCulture;
    private bool _hasUnresolvedValue;

    /// <summary>
    /// Gets or sets the property that activates the trigger.
    /// </summary>
    public DependencyProperty? Property
    {
        get => _property;
        set
        {
            CheckTriggerSealed();
            _property = value;
        }
    }

    /// <summary>
    /// Retains an unresolved XAML property token until the enclosing Style is available for
    /// validation. Runtime trigger evaluation continues to use <see cref="Property"/> only.
    /// </summary>
    internal string? UnresolvedPropertyName { get; set; }

    /// <summary>
    /// Gets or sets the value that activates the trigger.
    /// </summary>
    public object? Value
    {
        get => _value;
        set
        {
            CheckTriggerSealed();
            if (value is MarkupExtension)
            {
                throw new ArgumentException("A Trigger.Value cannot be a markup extension.", nameof(value));
            }

            _value = value;
        }
    }

    /// <summary>
    /// 可选——监听具名模板部件而非 templated parent 本身的 <see cref="Property"/>。
    /// 典型用法 <c>&lt;Trigger SourceName="PART_Btn" Property="IsMouseOver"&gt;</c>：
    /// 按钮自身 hover 触发 trigger，Setter 可改 sibling 命名部件的属性，从而把按钮 hover
    /// 反馈传染到模板里其它视觉（例如图标 Fill 切换）。
    /// 解析时机：trigger Attach 时通过 <see cref="TriggerBase.GetSetterTarget"/> 在 templated
    /// parent 的 NameScope 里查找；ControlTemplate.Triggers 在模板应用之后才 Attach，命名部件
    /// 此刻已存在。SourceName 解不到（空模板 / 拼写错误）时 trigger 静默不挂——不抛、不崩。
    /// </summary>
    public string? SourceName
    {
        get => _sourceName;
        set
        {
            CheckTriggerSealed();
            _sourceName = value;
        }
    }

    void ISupportInitialize.BeginInit()
    {
        CheckTriggerSealed();
        if (_isInitializing)
        {
            throw new InvalidOperationException("Trigger initialization has already begun.");
        }

        _isInitializing = true;
    }

    void ISupportInitialize.EndInit()
    {
        CheckTriggerSealed();
        if (!_isInitializing)
        {
            throw new InvalidOperationException("Trigger initialization has not begun.");
        }

        if (_hasUnresolvedProperty)
        {
            object? resolved = ConvertDeferredValue(
                _unresolvedPropertyConverter,
                _unresolvedPropertyContext,
                _unresolvedPropertyCulture,
                _unresolvedProperty);
            Property = resolved as DependencyProperty
                ?? throw new InvalidOperationException("Trigger.Property type conversion did not produce a DependencyProperty.");
            ClearDeferredProperty();
        }

        if (_hasUnresolvedValue)
        {
            object? resolved = ConvertDeferredValue(
                _unresolvedValueConverter,
                _unresolvedValueContext,
                _unresolvedValueCulture,
                _unresolvedValue);
            Value = Property is null ? resolved : ConvertValueIfNeeded(resolved, Property.PropertyType);
            ClearDeferredValue();
        }

        _isInitializing = false;
    }

    /// <summary>Defers Trigger.Property and Trigger.Value conversion until EndInit.</summary>
    public static void ReceiveTypeConverter(object targetObject, XamlSetTypeConverterEventArgs eventArgs)
    {
        if (targetObject is not Trigger trigger)
        {
            throw new ArgumentNullException(nameof(targetObject));
        }

        ArgumentNullException.ThrowIfNull(eventArgs);
        trigger.CheckTriggerSealed();

        if (eventArgs.Member.Name == nameof(Property))
        {
            trigger._unresolvedProperty = eventArgs.Value;
            trigger._unresolvedPropertyConverter = eventArgs.TypeConverter;
            trigger._unresolvedPropertyContext = eventArgs.ServiceProvider;
            trigger._unresolvedPropertyCulture = eventArgs.CultureInfo;
            trigger._hasUnresolvedProperty = true;
            eventArgs.Handled = true;
        }
        else if (eventArgs.Member.Name == nameof(Value))
        {
            trigger._unresolvedValue = eventArgs.Value;
            trigger._unresolvedValueConverter = eventArgs.TypeConverter;
            trigger._unresolvedValueContext = eventArgs.ServiceProvider;
            trigger._unresolvedValueCulture = eventArgs.CultureInfo;
            trigger._hasUnresolvedValue = true;
            eventArgs.Handled = true;
        }
    }

    internal override void Seal()
    {
        if (IsSealed)
        {
            return;
        }

        if (_isInitializing)
        {
            throw new InvalidOperationException("A Trigger cannot be sealed while it is being initialized.");
        }

        if (_property is null)
        {
            throw new InvalidOperationException("Property can not be null on Trigger.");
        }

        if (_property is not null && !_property.IsValidValue(_value))
        {
            throw new InvalidOperationException($"'{_value}' is not a valid value for dependency property '{_property.Name}'.");
        }

        base.Seal();
    }

    private static object? ConvertDeferredValue(
        TypeConverter? converter,
        ITypeDescriptorContext? context,
        System.Globalization.CultureInfo? culture,
        object? value)
        => converter is null ? value : converter.ConvertFrom(context, culture, value!);

    private void ClearDeferredProperty()
    {
        _unresolvedProperty = null;
        _unresolvedPropertyConverter = null;
        _unresolvedPropertyContext = null;
        _unresolvedPropertyCulture = null;
        _hasUnresolvedProperty = false;
    }

    private void ClearDeferredValue()
    {
        _unresolvedValue = null;
        _unresolvedValueConverter = null;
        _unresolvedValueContext = null;
        _unresolvedValueCulture = null;
        _hasUnresolvedValue = false;
    }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        if (Property == null)
            return;

        // 幂等：先拆干净上一次的挂载。仅仅 _elementStates.Remove 是不够的——旧的 handler
        // 还订阅在 source 上（重复求值 + 泄漏），旧的激活值也还钉在层里。
        Detach(element);

        // Create per-element state
        var state = new ElementState();
        _elementStates.Add(element, state);

        // 解析 SourceName：null/空 → 监听 templated parent 自己；否则去命名部件。
        // 解析失败（命名部件未找到）→ 整条 trigger 不挂，避免无声错位。
        var source = GetSetterTarget(element, SourceName);
        if (source == null)
            return;
        state.SourceElement = source;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            if (dp == Property)
            {
                EvaluateTriggerForElement(element, newValue);
            }
        };

        // Subscribe to property changes on the resolved source
        source.PropertyChangedInternal += state.Handler;

        // Check initial state
        var currentValue = source.GetValue(Property);
        EvaluateTriggerForElement(element, currentValue);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes on the source we subscribed to
            if (state.Handler != null && state.SourceElement != null)
            {
                state.SourceElement.PropertyChangedInternal -= state.Handler;
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element, object? currentValue)
    {
        if (Property == null) return;
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // Convert Value to the property's type for proper comparison
        var triggerValue = ConvertValueIfNeeded(Value, Property.PropertyType);
        var shouldBeActive = Equals(currentValue, triggerValue);

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                EnterTrigger(element);
            }
            else
            {
                ExitTrigger(element);
            }
        }
    }
}

/// <summary>
/// Represents a condition for a MultiTrigger.
/// </summary>
[XamlSetMarkupExtension(nameof(ReceiveMarkupExtension))]
[XamlSetTypeConverter(nameof(ReceiveTypeConverter))]
public sealed class Condition : ISupportInitialize
{
    private DependencyProperty? _property;
    private BindingBase? _binding;
    private object? _value;
    private string? _sourceName;
    private bool _isSealed;
    private bool _isInitializing;
    private object? _unresolvedProperty;
    private TypeConverter? _unresolvedPropertyConverter;
    private ITypeDescriptorContext? _unresolvedPropertyContext;
    private System.Globalization.CultureInfo? _unresolvedPropertyCulture;
    private bool _hasUnresolvedProperty;
    private object? _unresolvedValue;
    private TypeConverter? _unresolvedValueConverter;
    private ITypeDescriptorContext? _unresolvedValueContext;
    private System.Globalization.CultureInfo? _unresolvedValueCulture;
    private bool _hasUnresolvedValue;

    /// <summary>
    /// Initializes an empty condition.
    /// </summary>
    public Condition()
    {
    }

    /// <summary>
    /// Initializes a property condition.
    /// </summary>
    public Condition(DependencyProperty conditionProperty, object? conditionValue)
        : this(conditionProperty, conditionValue, null)
    {
    }

    /// <summary>
    /// Initializes a property condition that reads from a named source.
    /// </summary>
    public Condition(DependencyProperty conditionProperty, object? conditionValue, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(conditionProperty);
        if (!conditionProperty.IsValidValue(conditionValue))
        {
            throw new ArgumentException(
                $"'{conditionValue}' is not a valid value for dependency property '{conditionProperty.Name}'.",
                nameof(conditionValue));
        }

        _property = conditionProperty;
        Value = conditionValue;
        _sourceName = sourceName;
    }

    /// <summary>
    /// Initializes a data-binding condition.
    /// </summary>
    public Condition(BindingBase binding, object? conditionValue)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _binding = binding;
        Value = conditionValue;
    }

    /// <summary>
    /// Gets or sets the property to evaluate.
    /// </summary>
    public DependencyProperty? Property
    {
        get => _property;
        set
        {
            VerifyMutable();
            if (_binding is not null)
            {
                throw new InvalidOperationException("A condition cannot use both Property and Binding.");
            }

            _property = value;
        }
    }

    /// <summary>
    /// Retains an unresolved XAML property token until the enclosing MultiTrigger is
    /// post-processed. Runtime condition evaluation continues to use <see cref="Property"/> only.
    /// </summary>
    internal string? UnresolvedPropertyName { get; set; }

    /// <summary>
    /// Gets or sets the binding to evaluate.
    /// </summary>
    public BindingBase? Binding
    {
        get => _binding;
        set
        {
            VerifyMutable();
            if (_property is not null)
            {
                throw new InvalidOperationException("A condition cannot use both Property and Binding.");
            }

            _binding = value;
        }
    }

    /// <summary>
    /// Gets or sets the value that the property must equal for the condition to be true.
    /// </summary>
    public object? Value
    {
        get => _value;
        set
        {
            VerifyMutable();
            if (value is MarkupExtension)
            {
                throw new ArgumentException("A Condition.Value cannot be a markup extension.", nameof(value));
            }

            _value = value;
        }
    }

    /// <summary>
    /// 可选——按命名模板部件取 <see cref="Property"/> 值（而非 templated parent 自身）。
    /// 用于跨子部件组合条件：例如 MultiTrigger 同时检查"按钮被按下"+"父控件已选中"。
    /// 解析逻辑与 <see cref="Trigger.SourceName"/> 一致，由 <see cref="MultiTrigger.Attach"/>
    /// 在挂载时一次性缓存到 condition-source 数组。
    /// </summary>
    public string? SourceName
    {
        get => _sourceName;
        set
        {
            VerifyMutable();
            _sourceName = value;
        }
    }

    void ISupportInitialize.BeginInit()
    {
        VerifyMutable();
        if (_isInitializing)
        {
            throw new InvalidOperationException("Condition initialization has already begun.");
        }

        _isInitializing = true;
    }

    void ISupportInitialize.EndInit()
    {
        VerifyMutable();
        if (!_isInitializing)
        {
            throw new InvalidOperationException("Condition initialization has not begun.");
        }

        if (_hasUnresolvedProperty)
        {
            object? resolved = ConvertDeferredValue(
                _unresolvedPropertyConverter,
                _unresolvedPropertyContext,
                _unresolvedPropertyCulture,
                _unresolvedProperty);
            Property = resolved as DependencyProperty
                ?? throw new InvalidOperationException("Condition.Property type conversion did not produce a DependencyProperty.");
            ClearDeferredProperty();
        }

        if (_hasUnresolvedValue)
        {
            Value = ConvertDeferredValue(
                _unresolvedValueConverter,
                _unresolvedValueContext,
                _unresolvedValueCulture,
                _unresolvedValue);
            ClearDeferredValue();
        }

        _isInitializing = false;
    }

    /// <summary>Receives BindingBase markup extensions assigned to Condition.Binding.</summary>
    public static void ReceiveMarkupExtension(object targetObject, XamlSetMarkupExtensionEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(targetObject);
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (targetObject is Condition condition &&
            eventArgs.Member.Name == nameof(Binding) &&
            eventArgs.MarkupExtension is BindingBase binding)
        {
            condition.VerifyMutable();
            condition.Binding = binding;
            eventArgs.Handled = true;
        }
    }

    /// <summary>Defers Condition.Property and Condition.Value conversion until EndInit.</summary>
    public static void ReceiveTypeConverter(object targetObject, XamlSetTypeConverterEventArgs eventArgs)
    {
        if (targetObject is not Condition condition)
        {
            throw new ArgumentNullException(nameof(targetObject));
        }

        ArgumentNullException.ThrowIfNull(eventArgs);
        condition.VerifyMutable();

        if (eventArgs.Member.Name == nameof(Property))
        {
            condition._unresolvedProperty = eventArgs.Value;
            condition._unresolvedPropertyConverter = eventArgs.TypeConverter;
            condition._unresolvedPropertyContext = eventArgs.ServiceProvider;
            condition._unresolvedPropertyCulture = eventArgs.CultureInfo;
            condition._hasUnresolvedProperty = true;
            eventArgs.Handled = true;
        }
        else if (eventArgs.Member.Name == nameof(Value))
        {
            condition._unresolvedValue = eventArgs.Value;
            condition._unresolvedValueConverter = eventArgs.TypeConverter;
            condition._unresolvedValueContext = eventArgs.ServiceProvider;
            condition._unresolvedValueCulture = eventArgs.CultureInfo;
            condition._hasUnresolvedValue = true;
            eventArgs.Handled = true;
        }
    }

    internal void Seal(bool dataCondition)
    {
        if (_isSealed)
        {
            return;
        }

        if (dataCondition)
        {
            if (_binding is null)
            {
                throw new InvalidOperationException("A data-trigger condition requires a Binding.");
            }

            if (_sourceName is not null)
            {
                throw new InvalidOperationException("SourceName is not valid for a data-trigger condition.");
            }
        }
        else
        {
            if (_property is null)
            {
                throw new InvalidOperationException("A property-trigger condition requires a Property.");
            }

            if (!_property.IsValidValue(_value))
            {
                throw new InvalidOperationException(
                    $"'{_value}' is not a valid value for dependency property '{_property.Name}'.");
            }
        }

        _isSealed = true;
    }

    private void VerifyMutable()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A sealed Condition cannot be changed.");
        }
    }

    private static object? ConvertDeferredValue(
        TypeConverter? converter,
        ITypeDescriptorContext? context,
        System.Globalization.CultureInfo? culture,
        object? value)
        => converter is null ? value : converter.ConvertFrom(context, culture, value!);

    private void ClearDeferredProperty()
    {
        _unresolvedProperty = null;
        _unresolvedPropertyConverter = null;
        _unresolvedPropertyContext = null;
        _unresolvedPropertyCulture = null;
        _hasUnresolvedProperty = false;
    }

    private void ClearDeferredValue()
    {
        _unresolvedValue = null;
        _unresolvedValueConverter = null;
        _unresolvedValueContext = null;
        _unresolvedValueCulture = null;
        _hasUnresolvedValue = false;
    }
}

/// <summary>
/// Represents a condition based on a data binding for a MultiDataTrigger.
/// </summary>
public sealed class BindingCondition
{
    /// <summary>
    /// Gets or sets the binding to evaluate.
    /// </summary>
    public Binding? Binding { get; set; }

    /// <summary>
    /// Gets or sets the value that the binding must equal for the condition to be true.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Converts the legacy Jalium binding-condition shape to the WPF-compatible Condition type.
    /// </summary>
    public static implicit operator Condition(BindingCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new Condition(
            condition.Binding ?? throw new InvalidOperationException("BindingCondition requires a Binding."),
            condition.Value);
    }
}

/// <summary>
/// Represents a trigger that applies property values when multiple conditions are all true.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Setters")]
public sealed class MultiTrigger : TriggerBase, Jalium.UI.Markup.IAddChild
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        /// <summary>
        /// 每条 Condition 解析到的源元素，与 <see cref="MultiTrigger.Conditions"/> 平行索引；
        /// 元素为 null 表示该 condition 的 SourceName 解析失败（命名部件不存在）。
        /// EvaluateTriggerForElement 按此数组逐条取值，避免每次重新走 visual-tree 搜索。
        /// </summary>
        public FrameworkElement?[] ConditionSources = Array.Empty<FrameworkElement?>();
        public FrameworkElement[] SubscribedSources = Array.Empty<FrameworkElement>();
    }

    private readonly ConditionalWeakTable<FrameworkElement, ElementState> _elementStates = new();

    /// <summary>
    /// Gets the collection of conditions that must all be true for the trigger to activate.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ConditionCollection Conditions { get; } = new();

    void Jalium.UI.Markup.IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not Setter setter)
        {
            throw new ArgumentException("MultiTrigger content must be a Setter.", nameof(value));
        }

        Setters.Add(setter);
    }

    void Jalium.UI.Markup.IAddChild.AddText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is not valid MultiTrigger content.", nameof(text));
        }
    }

    /// <inheritdoc />
    internal override void Seal()
    {
        // Match WPF's validation order: seal/validate setters first, then validate
        // each condition as a dependency-property condition.
        base.Seal();
        if (Conditions.Count > 0)
        {
            Conditions.Seal(dataConditions: false);
        }
    }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // 幂等：见 Trigger.Attach 的说明。
        Detach(element);

        // Create per-element state
        var state = new ElementState();
        _elementStates.Add(element, state);

        // 解析每条 Condition 的 source（SourceName=null → templated parent 自己）。
        // 收集去重后的源元素集合，handler 订阅到每个唯一源——任何一个源的相关 DP 变化
        // 都会触发整组重评估（"all must be true" 语义）。
        state.ConditionSources = new FrameworkElement?[Conditions.Count];
        List<FrameworkElement>? uniqueSources = null;
        for (int i = 0; i < Conditions.Count; i++)
        {
            var source = GetSetterTarget(element, Conditions[i].SourceName);
            state.ConditionSources[i] = source;
            if (source == null)
                continue;

            uniqueSources ??= [];
            var alreadyAdded = false;
            foreach (var existing in uniqueSources)
            {
                if (ReferenceEquals(existing, source))
                {
                    alreadyAdded = true;
                    break;
                }
            }

            if (!alreadyAdded)
            {
                uniqueSources.Add(source);
            }
        }
        state.SubscribedSources = uniqueSources?.ToArray() ?? [];

        // 没有任何可订阅的源：所有 SourceName 都没解到——直接放弃，避免无效订阅与误激活。
        if (state.SubscribedSources.Length == 0)
            return;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            // Check if this property is one of our conditions
            bool shouldEvaluate = false;
            foreach (var condition in Conditions)
            {
                // Compare by property reference or by name as fallback
                if (dp == condition.Property ||
                    (condition.Property != null && dp.Name == condition.Property.Name))
                {
                    shouldEvaluate = true;
                    break;
                }
            }

            if (shouldEvaluate)
            {
                EvaluateTriggerForElement(element);
            }
        };

        // Subscribe handler to every distinct source so any of them firing PropertyChanged
        // wakes this MultiTrigger.
        foreach (var source in state.SubscribedSources)
        {
            source.PropertyChangedInternal += state.Handler;
        }

        // Check initial state
        EvaluateTriggerForElement(element);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe handler from every distinct source we subscribed to in Attach.
            if (state.Handler != null)
            {
                foreach (var source in state.SubscribedSources)
                {
                    source.PropertyChangedInternal -= state.Handler;
                }
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // All conditions must be true
        var shouldBeActive = true;

        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (condition.Property == null)
            {
                shouldBeActive = false;
                break;
            }

            // 用 Attach 时缓存的 source（与 Conditions 平行）取值。SourceName 解析失败的
            // condition（source==null）视为不满足——MultiTrigger 整体不激活。
            var source = i < state.ConditionSources.Length ? state.ConditionSources[i] : null;
            if (source == null)
            {
                shouldBeActive = false;
                break;
            }
            var currentValue = source.GetValue(condition.Property);
            var conditionValue = ConvertValueIfNeeded(condition.Value, condition.Property.PropertyType);

            if (!Equals(currentValue, conditionValue))
            {
                shouldBeActive = false;
                break;
            }
        }

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                EnterTrigger(element);
            }
            else
            {
                ExitTrigger(element);
            }
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when data equals a specified value.
/// </summary>
[XamlSetMarkupExtension(nameof(ReceiveMarkupExtension))]
public sealed class DataTrigger : TriggerBase
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        public BindingExpressionBase? BindingExpression;
    }

    private readonly ConditionalWeakTable<FrameworkElement, ElementState> _elementStates = new();
    private BindingBase? _binding;
    private object? _value;

    // Shadow property for receiving binding updates. ONE UNIQUE attached property per DataTrigger
    // INSTANCE (lazily registered). A single property shared across all instances made two
    // DataTriggers on the SAME element clobber each other's binding — the later Attach's
    // SetBinding overwrote the earlier trigger's binding, so the earlier one never saw its value
    // and never fired (e.g. a hover DataTrigger + a selected DataTrigger on one border: hover died).
    // Distinct instances now use distinct properties; per-element attached values keep multiple
    // elements sharing one style independent. Mirrors MultiDataTrigger's per-condition shadow props.
    private static int _propertyCounter;
    private readonly object _shadowPropertyGate = new();
    private DependencyProperty? _shadowProperty;

    private DependencyProperty ShadowProperty
    {
        get
        {
            if (_shadowProperty != null)
                return _shadowProperty;

            lock (_shadowPropertyGate)
            {
                return _shadowProperty ??= DependencyProperty.RegisterAttached(
                    $"_DataTriggerValue{Interlocked.Increment(ref _propertyCounter)}",
                    typeof(object),
                    typeof(DataTrigger),
                    new PropertyMetadata(null));
            }
        }
    }

    /// <summary>
    /// Gets or sets the binding that produces the value to compare with Value.
    /// </summary>
    public BindingBase? Binding
    {
        get => _binding;
        set
        {
            CheckTriggerSealed();
            _binding = value;
        }
    }

    /// <summary>
    /// Gets or sets the value that activates the trigger.
    /// </summary>
    public object? Value
    {
        get => _value;
        set
        {
            CheckTriggerSealed();
            if (value is MarkupExtension)
            {
                throw new ArgumentException("A DataTrigger.Value cannot be a markup extension.", nameof(value));
            }

            _value = value;
        }
    }

    /// <summary>Receives BindingBase markup extensions assigned to DataTrigger.Binding.</summary>
    public static void ReceiveMarkupExtension(object targetObject, XamlSetMarkupExtensionEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(targetObject);
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (targetObject is DataTrigger trigger &&
            eventArgs.Member.Name == nameof(Binding) &&
            eventArgs.MarkupExtension is BindingBase binding)
        {
            trigger.Binding = binding;
            eventArgs.Handled = true;
        }
        else
        {
            eventArgs.CallBase();
        }
    }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // 幂等：见 Trigger.Attach 的说明。
        Detach(element);

        // Create per-element state
        var state = new ElementState();
        _elementStates.Add(element, state);

        var shadowProp = ShadowProperty;

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            if (dp == shadowProp)
            {
                EvaluateTriggerForElement(element, newValue);
            }
        };

        // Subscribe to property changes for the shadow property
        element.PropertyChangedInternal += state.Handler;

        // Set up binding to the shadow property
        if (Binding != null)
        {
            state.BindingExpression = BindingOperations.SetBinding(element, shadowProp, Binding);
        }

        // Check initial state
        var currentValue = element.GetValue(shadowProp);
        EvaluateTriggerForElement(element, currentValue);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            // Clear binding
            if (state.BindingExpression != null)
            {
                BindingOperations.ClearBinding(element, ShadowProperty);
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element, object? currentValue)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // Coerce the (typically XAML-authored string) Value to the binding result's RUNTIME type
        // before comparing. The property-based Trigger converts Value to Property.PropertyType; a
        // DataTrigger has no static target type, so convert against currentValue's runtime type.
        // Without this a XAML Value="True" (string) never equals a bool binding result (e.g.
        // IsMouseOver) and the trigger silently never activates.
        var triggerValue = currentValue != null ? ConvertValueIfNeeded(Value, currentValue.GetType()) : Value;
        var shouldBeActive = Equals(currentValue, triggerValue);

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                EnterTrigger(element);
            }
            else
            {
                ExitTrigger(element);
            }
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when an event occurs.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(Actions))]
public class EventTrigger : TriggerBase, Jalium.UI.Markup.IAddChild
{
    private sealed class AttachmentState
    {
        public required FrameworkElement EventSource { get; init; }
        public required RoutedEvent RoutedEvent { get; init; }
        public required RoutedEventHandler Handler { get; init; }
    }

    private readonly ConditionalWeakTable<FrameworkElement, AttachmentState> _attachments = new();
    private RoutedEvent? _routedEvent;
    private string? _sourceName;
    private TriggerActionCollection? _actions;

    public EventTrigger()
    {
    }

    public EventTrigger(RoutedEvent routedEvent)
    {
        RoutedEvent = routedEvent ?? throw new ArgumentNullException(nameof(routedEvent));
    }

    /// <summary>
    /// Gets or sets the name of the event that activates the trigger.
    /// </summary>
    public RoutedEvent? RoutedEvent
    {
        get => _routedEvent;
        set
        {
            CheckTriggerSealed();
            _routedEvent = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>
    /// Gets or sets the name of the element whose routed event activates this trigger.
    /// </summary>
    public string? SourceName
    {
        get => _sourceName;
        set
        {
            CheckTriggerSealed();
            _sourceName = value;
        }
    }

    /// <summary>
    /// Gets the collection of actions to perform when the event occurs.
    /// </summary>
    public TriggerActionCollection Actions => _actions ??= new TriggerActionCollection(this);

    /// <summary>Adds an action supplied through XAML child syntax.</summary>
    protected virtual void AddChild(object value)
    {
        if (value is not TriggerAction action)
        {
            throw new ArgumentException("EventTrigger accepts only TriggerAction children.", nameof(value));
        }

        Actions.Add(action);
    }

    /// <summary>Accepts formatting whitespace and rejects text content.</summary>
    protected virtual void AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is not valid EventTrigger content.", nameof(text));
        }
    }

    /// <summary>Returns whether the Actions collection should be serialized.</summary>
    public bool ShouldSerializeActions() => _actions is { Count: > 0 };

    void Jalium.UI.Markup.IAddChild.AddChild(object value) => AddChild(value);

    void Jalium.UI.Markup.IAddChild.AddText(string text) => AddText(text);

    /// <inheritdoc />
    internal override void Seal()
    {
        if (IsSealed)
        {
            return;
        }

        if (Setters.Count > 0)
        {
            throw new InvalidOperationException("An EventTrigger cannot contain property setters.");
        }

        if (HasEnterActions || HasExitActions)
        {
            throw new InvalidOperationException("EnterActions and ExitActions are not valid on an EventTrigger.");
        }

        _actions?.Seal(this);
        base.Seal();
    }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        Detach(element);

        var eventSource = string.IsNullOrEmpty(SourceName)
            ? element
            : element.FindName(SourceName) as FrameworkElement;

        var routedEvent = RoutedEvent;
        if (routedEvent == null || eventSource == null)
            return;

        RoutedEventHandler handler = (_, _) => InvokeEventActions(element);
        eventSource.AddHandler(routedEvent, handler);
        _attachments.Add(element, new AttachmentState
        {
            EventSource = eventSource,
            RoutedEvent = routedEvent,
            Handler = handler
        });
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (!_attachments.TryGetValue(element, out var state))
            return;

        state.EventSource.RemoveHandler(state.RoutedEvent, state.Handler);

        _attachments.Remove(element);
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        // EventTriggers don't have a persistent active state - they fire on events
        return false;
    }

    private void InvokeEventActions(FrameworkElement attachedElement)
    {
        foreach (var action in Actions)
        {
            action.Invoke(attachedElement);
        }
    }
}

/// <summary>
/// Represents a trigger that applies property values when multiple data binding conditions are all true.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Setters")]
public sealed class MultiDataTrigger : TriggerBase, Jalium.UI.Markup.IAddChild
{
    /// <summary>
    /// Tracks per-element state since a single trigger can be attached to multiple elements (shared styles).
    /// </summary>
    private class ElementState
    {
        public bool IsActive;
        public Action<DependencyProperty, object?, object?>? Handler;
        public DependencyProperty[] ShadowProperties = Array.Empty<DependencyProperty>();
    }

    private readonly ConditionalWeakTable<FrameworkElement, ElementState> _elementStates = new();
    private readonly object _shadowPropertyGate = new();
    private DependencyProperty[]? _shadowProperties;

    // Counter for generating unique shadow property names
    private static int _propertyCounter;

    /// <summary>
    /// Gets the collection of binding conditions that must all be true for the trigger to activate.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ConditionCollection Conditions { get; } = new();

    void Jalium.UI.Markup.IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not Setter setter)
        {
            throw new ArgumentException("MultiDataTrigger content must be a Setter.", nameof(value));
        }

        Setters.Add(setter);
    }

    void Jalium.UI.Markup.IAddChild.AddText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is not valid MultiDataTrigger content.", nameof(text));
        }
    }

    /// <inheritdoc />
    internal override void Seal()
    {
        // Match WPF's validation order: seal/validate setters first, then validate
        // each condition as a data-binding condition.
        base.Seal();
        if (Conditions.Count > 0)
        {
            Conditions.Seal(dataConditions: true);
        }

        _ = GetShadowProperties();
    }

    /// <inheritdoc />
    internal override void Attach(FrameworkElement element)
    {
        // 幂等：见 Trigger.Attach 的说明。
        Detach(element);

        // Create per-element state
        var state = new ElementState();
        _elementStates.Add(element, state);

        // Shadow properties describe the trigger declaration, not an attached
        // element. Reuse one property per condition across every element sharing
        // this sealed trigger; registering them per attachment permanently grew
        // the global dependency-property registry during virtualization.
        state.ShadowProperties = GetShadowProperties();

        // Create a closure that captures this specific element
        state.Handler = (dp, oldValue, newValue) =>
        {
            // Check if this property is one of our shadow properties
            if (Array.IndexOf(state.ShadowProperties, dp) >= 0)
            {
                EvaluateTriggerForElement(element);
            }
        };

        // Subscribe to property changes
        element.PropertyChangedInternal += state.Handler;

        // Set up bindings to shadow properties
        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (condition.Binding != null)
            {
                var bindingExpr = BindingOperations.SetBinding(element, state.ShadowProperties[i], condition.Binding);
                _ = bindingExpr;
            }
        }

        // Check initial state
        EvaluateTriggerForElement(element);
    }

    /// <inheritdoc />
    internal override void Detach(FrameworkElement element)
    {
        if (_elementStates.TryGetValue(element, out var state))
        {
            // Unsubscribe from property changes
            if (state.Handler != null)
            {
                element.PropertyChangedInternal -= state.Handler;
            }

            // Clear all bindings
            foreach (var shadowProp in state.ShadowProperties)
            {
                BindingOperations.ClearBinding(element, shadowProp);
            }

            if (state.IsActive)
            {
                RemoveTriggerSetters(element);
            }

            _elementStates.Remove(element);
        }

        // Clear any stored pre-trigger values
        ClearPreTriggerValues(element);
    }

    private DependencyProperty[] GetShadowProperties()
    {
        if (_shadowProperties != null)
            return _shadowProperties;

        lock (_shadowPropertyGate)
        {
            if (_shadowProperties != null)
                return _shadowProperties;

            var properties = new DependencyProperty[Conditions.Count];
            for (var index = 0; index < properties.Length; index++)
            {
                var propertyId = Interlocked.Increment(ref _propertyCounter);
                properties[index] = DependencyProperty.RegisterAttached(
                    $"_MultiDataTriggerValue{propertyId}",
                    typeof(object),
                    typeof(MultiDataTrigger),
                    new PropertyMetadata(null));
            }

            _shadowProperties = properties;
            return properties;
        }
    }

    /// <inheritdoc />
    internal override bool IsActiveForElement(FrameworkElement element)
    {
        return _elementStates.TryGetValue(element, out var state) && state.IsActive;
    }

    private void EvaluateTriggerForElement(FrameworkElement element)
    {
        if (!_elementStates.TryGetValue(element, out var state)) return;

        // All conditions must be true
        var shouldBeActive = true;

        for (int i = 0; i < Conditions.Count; i++)
        {
            var condition = Conditions[i];
            if (i >= state.ShadowProperties.Length)
            {
                shouldBeActive = false;
                break;
            }

            var currentValue = element.GetValue(state.ShadowProperties[i]);
            // Coerce condition.Value (typically a XAML string) to the binding result's runtime type
            // before comparing — same reason as DataTrigger.EvaluateTriggerForElement: a string
            // "True" must match a bool binding result, etc.
            var conditionValue = currentValue != null
                ? ConvertValueIfNeeded(condition.Value, currentValue.GetType())
                : condition.Value;
            if (!Equals(currentValue, conditionValue))
            {
                shouldBeActive = false;
                break;
            }
        }

        if (shouldBeActive != state.IsActive)
        {
            state.IsActive = shouldBeActive;

            if (state.IsActive)
            {
                EnterTrigger(element);
            }
            else
            {
                ExitTrigger(element);
            }
        }
    }
}

/// <summary>
/// Base class for actions that can be invoked by event triggers.
/// </summary>
public abstract class TriggerAction : DependencyObject
{
    private TriggerBase? _containingTrigger;
    private bool _isSealed;

    private protected override bool IsSealedCore => _isSealed;

    /// <summary>Gets the trigger that owns this action, when it has been attached.</summary>
    internal TriggerBase? ContainingTrigger => _containingTrigger;

    /// <summary>
    /// Invokes the action on the specified element.
    /// </summary>
    internal abstract void Invoke(FrameworkElement? element);

    internal void AttachOwner(TriggerBase? owner)
    {
        if (owner is null)
        {
            return;
        }

        if (_containingTrigger is not null && !ReferenceEquals(_containingTrigger, owner))
        {
            throw new InvalidOperationException("A TriggerAction can belong to only one trigger.");
        }

        _containingTrigger = owner;
    }

    internal void DetachOwner(TriggerBase? owner)
    {
        if (!_isSealed && owner is not null && ReferenceEquals(_containingTrigger, owner))
        {
            _containingTrigger = null;
        }
    }

    internal void Seal(TriggerBase containingTrigger)
    {
        ArgumentNullException.ThrowIfNull(containingTrigger);
        if (_isSealed)
        {
            if (!ReferenceEquals(_containingTrigger, containingTrigger))
            {
                throw new InvalidOperationException("A TriggerAction can belong to only one trigger.");
            }

            return;
        }

        AttachOwner(containingTrigger);
        _isSealed = true;
    }

    /// <summary>Throws when a sealed action is changed by a derived implementation.</summary>
    protected void CheckSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A sealed TriggerAction cannot be changed.");
        }
    }
}

/// <summary>
/// Represents a setter that applies an event handler in a Style.
/// </summary>
public class EventSetter : SetterBase
{
    private RoutedEvent? _event;
    private Delegate? _handler;
    private bool _handledEventsToo;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSetter"/> class.
    /// </summary>
    public EventSetter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSetter"/> class with the specified event and handler.
    /// </summary>
    /// <param name="routedEvent">The routed event that this EventSetter responds to.</param>
    /// <param name="handler">The handler to assign.</param>
    public EventSetter(RoutedEvent routedEvent, Delegate handler)
    {
        Event = routedEvent;
        Handler = handler;
    }

    /// <summary>
    /// Gets or sets the particular routed event that this EventSetter responds to.
    /// </summary>
    public RoutedEvent? Event
    {
        get => _event;
        set
        {
            CheckSealed();
            _event = value;
        }
    }

    /// <summary>
    /// Gets or sets the handler to assign in this setter.
    /// </summary>
    public Delegate? Handler
    {
        get => _handler;
        set
        {
            CheckSealed();
            _handler = value;
        }
    }

    /// <summary>
    /// Gets or sets whether the handler should be invoked even if the event is marked as handled.
    /// </summary>
    public bool HandledEventsToo
    {
        get => _handledEventsToo;
        set
        {
            CheckSealed();
            _handledEventsToo = value;
        }
    }

    internal override void Seal()
    {
        if (IsSealed)
        {
            return;
        }

        if (_event == null)
        {
            throw new InvalidOperationException("EventSetter.Event must be specified before the EventSetter is sealed.");
        }

        if (_handler == null)
        {
            throw new InvalidOperationException("EventSetter.Handler must be specified before the EventSetter is sealed.");
        }

        if (!_event.HandlerType.IsInstanceOfType(_handler))
        {
            throw new InvalidOperationException("EventSetter.Handler is not compatible with the routed event handler type.");
        }

        base.Seal();
    }

    /// <summary>
    /// Applies this event setter to the specified element.
    /// </summary>
    internal void Apply(FrameworkElement element)
    {
        if (Event == null || Handler == null)
            return;

        element.AddHandler(Event, Handler, HandledEventsToo);
    }

    /// <summary>
    /// Removes this event setter from the specified element.
    /// </summary>
    internal void Remove(FrameworkElement element)
    {
        if (Event == null || Handler == null)
            return;

        element.RemoveHandler(Event, Handler);
    }
}
