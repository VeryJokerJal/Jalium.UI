using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using System.Reflection;

namespace Jalium.UI.Markup;

/// <summary>
/// Base class for XAML markup extensions.
/// </summary>
public abstract class MarkupExtension
{
    /// <summary>
    /// Returns the value to use for the target property. Implementations may use reflection
    /// on user-supplied types (see <see cref="StaticExtension"/> and <see cref="ArrayExtension"/>);
    /// marked accordingly so callers / overrides can declare AOT contracts.
    /// </summary>
    /// <param name="serviceProvider">A service provider that can provide services for the markup extension.</param>
    /// <returns>The value to use for the property where the extension is applied.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Some extensions (e.g. x:Static) reflect on the resolved Type to read fields/properties.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Some extensions (e.g. x:Array) construct arrays of a runtime-supplied element Type.")]
    public abstract object? ProvideValue(IServiceProvider serviceProvider);
}

/// <summary>
/// Provides services for markup extensions during XAML parsing.
/// </summary>
public interface IProvideValueTarget
{
    /// <summary>
    /// Gets the target object of the markup extension.
    /// </summary>
    object? TargetObject { get; }

    /// <summary>
    /// Gets the target property of the markup extension.
    /// </summary>
    object? TargetProperty { get; }
}

/// <summary>
/// Implementation of IProvideValueTarget for XAML parsing.
/// </summary>
internal sealed class ProvideValueTarget : IProvideValueTarget
{
    public object? TargetObject { get; set; }
    public object? TargetProperty { get; set; }
}

/// <summary>
/// Service provider for markup extensions.
/// </summary>
internal sealed class MarkupExtensionServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public void AddService(Type serviceType, object service)
    {
        _services[serviceType] = service;
    }

    public object? GetService(Type serviceType)
    {
        return _services.GetValueOrDefault(serviceType);
    }
}

/// <summary>
/// Provides access to the ambient resource dictionaries during XAML parsing.
/// </summary>
public interface IAmbientResourceProvider
{
    /// <summary>
    /// Tries to find a resource by key in the ambient resource dictionaries.
    /// </summary>
    bool TryGetResource(object key, out object? value);
}

/// <summary>
/// Implementation of IAmbientResourceProvider that searches through a stack of resource dictionaries.
/// </summary>
internal sealed class AmbientResourceProvider : IAmbientResourceProvider
{
    private readonly List<ResourceDictionary> _resourceDictionaries = new();

    public void AddResourceDictionary(ResourceDictionary dictionary)
    {
        _resourceDictionaries.Add(dictionary);
    }

    public bool TryGetResource(object key, out object? value)
    {
        // Search in reverse order (most recent first)
        for (int i = _resourceDictionaries.Count - 1; i >= 0; i--)
        {
            if (_resourceDictionaries[i].TryGetValue(key, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }
}

/// <summary>
/// XAML markup extension for data binding.
/// </summary>
public sealed class BindingExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the path to the binding source property.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the binding source.
    /// </summary>
    public object? Source { get; set; }

    /// <summary>
    /// Gets or sets the name of the element to use as the binding source.
    /// </summary>
    public string? ElementName { get; set; }

    /// <summary>
    /// Gets or sets the binding mode.
    /// </summary>
    public BindingMode Mode { get; set; } = BindingMode.Default;

    /// <summary>
    /// Gets or sets the converter.
    /// </summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>
    /// Resource key for a converter that should be resolved lazily against the binding
    /// target's resource lookup chain. Set by <see cref="SetBindingParameter"/> when
    /// <c>Converter="{StaticResource X}"</c> appears inside a template body where the
    /// resource isn't available at parse time. <see cref="ProvideValue"/> propagates this
    /// to the produced <see cref="Binding.PendingConverterKey"/> for deferred resolution.
    /// </summary>
    internal string? PendingConverterKey { get; set; }

    /// <summary>
    /// Gets or sets the converter parameter.
    /// </summary>
    public object? ConverterParameter { get; set; }

    /// <summary>
    /// Gets or sets the update source trigger.
    /// </summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; } = UpdateSourceTrigger.Default;

    /// <summary>
    /// Gets or sets the fallback value.
    /// </summary>
    public object? FallbackValue { get; set; }

    /// <summary>
    /// Gets or sets the target null value.
    /// </summary>
    public object? TargetNullValue { get; set; }

    /// <summary>
    /// Gets or sets the string format.
    /// </summary>
    public string? StringFormat { get; set; }

    /// <summary>
    /// Gets or sets the relative source for the binding.
    /// </summary>
    public RelativeSource? RelativeSource { get; set; }

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
    /// Initializes a new instance of the <see cref="BindingExtension"/> class.
    /// </summary>
    public BindingExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingExtension"/> class with the specified path.
    /// </summary>
    /// <param name="path">The binding path.</param>
    public BindingExtension(string path)
    {
        Path = path;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Path = string.IsNullOrEmpty(Path) ? null : new PropertyPath(Path),
            Source = Source,
            ElementName = ElementName,
            Mode = Mode,
            Converter = Converter,
            PendingConverterKey = PendingConverterKey,
            ConverterParameter = ConverterParameter,
            UpdateSourceTrigger = UpdateSourceTrigger,
            FallbackValue = FallbackValue,
            TargetNullValue = TargetNullValue,
            StringFormat = StringFormat,
            RelativeSource = RelativeSource,
            ValidatesOnDataErrors = ValidatesOnDataErrors,
            ValidatesOnNotifyDataErrors = ValidatesOnNotifyDataErrors,
            NotifyOnValidationError = NotifyOnValidationError
        };

        // If we have target information, set up the binding now
        var provideValueTarget = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (provideValueTarget?.TargetObject is DependencyObject targetObject &&
            provideValueTarget?.TargetProperty is DependencyProperty targetProperty)
        {
            return targetObject.SetBinding(targetProperty, binding);
        }

        // Return the binding itself if we can't set it up now
        return binding;
    }
}

/// <summary>
/// XAML markup extension for static resources.
/// </summary>
public sealed class StaticResourceExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public object? ResourceKey { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticResourceExtension"/> class.
    /// </summary>
    public StaticResourceExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticResourceExtension"/> class with the specified key.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    public StaticResourceExtension(object resourceKey)
    {
        ResourceKey = resourceKey;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (ResourceKey == null)
            return null;

        var ambientProvider = serviceProvider?.GetService(typeof(IAmbientResourceProvider)) as IAmbientResourceProvider;
        var provideValueTarget = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var targetElement = provideValueTarget?.TargetObject as FrameworkElement;

        // First, check ambient resources (resources being parsed in current XAML context)
        if (ambientProvider != null && ambientProvider.TryGetResource(ResourceKey, out var ambientValue))
        {
            var resolvedAmbientValue = ResolveDeferredResourceReference(
                ambientValue,
                ambientProvider,
                targetElement,
                new HashSet<object>());
            if (resolvedAmbientValue != null)
                return resolvedAmbientValue;
        }

        // Search for the resource in the visual tree
        if (targetElement != null)
        {
            var result = ResourceLookup.FindResource(targetElement, ResourceKey);
            if (result != null)
                return result;
        }

        // Fall back to application resources
        if (Jalium.UI.Application.Current?.Resources != null &&
            Jalium.UI.Application.Current.Resources.TryGetValue(ResourceKey, out var appValue))
        {
            var resolvedAppValue = ResolveDeferredResourceReference(
                appValue,
                ambientProvider,
                targetElement,
                new HashSet<object>());
            if (resolvedAppValue != null)
                return resolvedAppValue;
        }

        // Resource not found — log and return null. The streaming parser used to throw in
        // DEBUG so missing resources surfaced loudly during development, but the SG-emitted
        // codegen path now invokes the markup-extension runtime inside template
        // <c>SetVisualTree</c> lambdas. At that moment the lambda's element has not been
        // attached to its templated parent yet, so an enclosing
        // <c>{Binding ..., Converter={StaticResource X}}</c> hits this resolver with
        // <c>targetElement: null</c> and the resource lookup fails by design — the value
        // would have been resolved later, in the runtime parser's case, via
        // <c>ResolveMarkupExtensionValueIfNeeded</c> on the deferred reference. Throwing
        // here breaks the whole template rather than letting the binding fall through.
        // Match the Release behaviour (silent null) so DEBUG runs do not differ from
        // Release in failure mode for the SG path.
        var msg = $"[StaticResource FAIL] Cannot find resource named '{ResourceKey}' (type: {ResourceKey?.GetType().Name}). " +
                  $"targetElement: {(targetElement != null ? targetElement.GetType().Name : "null")}, " +
                  $"ambientProvider: {(ambientProvider != null ? "available" : "null")}, " +
                  $"appResources: {(Jalium.UI.Application.Current?.Resources != null ? $"{Jalium.UI.Application.Current.Resources.Count} entries" : "null")}";
        System.Diagnostics.Debug.WriteLine(msg);
        return null;
    }

    private static object? ResolveDeferredResourceReference(
        object? value,
        IAmbientResourceProvider? ambientProvider,
        FrameworkElement? targetElement,
        HashSet<object> resourceChain)
    {
        if (value is not IDynamicResourceReference dynamicReference)
            return value;

        if (!resourceChain.Add(dynamicReference.ResourceKey))
            return null;

        if (ambientProvider != null && ambientProvider.TryGetResource(dynamicReference.ResourceKey, out var ambientValue))
        {
            var resolvedAmbientValue = ResolveDeferredResourceReference(
                ambientValue,
                ambientProvider,
                targetElement,
                resourceChain);
            if (resolvedAmbientValue != null)
                return resolvedAmbientValue;
        }

        if (targetElement != null)
        {
            var resolvedTreeValue = ResourceLookup.FindResource(targetElement, dynamicReference.ResourceKey);
            if (resolvedTreeValue != null)
                return resolvedTreeValue;
        }

        if (Jalium.UI.Application.Current?.Resources != null &&
            Jalium.UI.Application.Current.Resources.TryGetValue(dynamicReference.ResourceKey, out var appValue))
        {
            return ResolveDeferredResourceReference(
                appValue,
                ambientProvider,
                targetElement,
                resourceChain);
        }

        return null;
    }
}

/// <summary>
/// XAML markup extension for dynamic resources.
/// Unlike StaticResource, DynamicResource updates when the resource changes.
/// </summary>
public sealed class DynamicResourceExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public object? ResourceKey { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicResourceExtension"/> class.
    /// </summary>
    public DynamicResourceExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicResourceExtension"/> class with the specified key.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    public DynamicResourceExtension(object resourceKey)
    {
        ResourceKey = resourceKey;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (ResourceKey == null)
            return null;

        var provideValueTarget = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var targetObject = provideValueTarget?.TargetObject as DependencyObject;
        var targetProperty = provideValueTarget?.TargetProperty as DependencyProperty;

        if (targetObject is FrameworkElement targetElement && targetProperty != null)
        {
            // Register runtime tracking for dynamic updates and return the current effective value.
            DynamicResourceBindingOperations.SetDynamicResource(targetElement, targetProperty, ResourceKey);
            return ResourceLookup.FindResource(targetElement, ResourceKey);
        }

        if (provideValueTarget?.TargetObject is FrameworkElement element)
        {
            return ResourceLookup.FindResource(element, ResourceKey) ?? new DynamicResourceReference(ResourceKey);
        }

        // For deferred cases (e.g., Setter.Value), preserve the reference so style runtime can resolve it.
        return new DynamicResourceReference(ResourceKey);
    }
}

/// <summary>
/// Represents a reference to a dynamic resource that will be resolved at runtime.
/// </summary>
public sealed class DynamicResourceReference : IDynamicResourceReference
{
    /// <summary>
    /// Gets the resource key.
    /// </summary>
    public object ResourceKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicResourceReference"/> class.
    /// </summary>
    public DynamicResourceReference(object resourceKey)
    {
        ResourceKey = resourceKey;
    }
}

/// <summary>
/// XAML markup extension for theme-aware resources.
/// Semantically equivalent to DynamicResource but specifically indicates a resource
/// defined in <see cref="ResourceDictionary.ThemeDictionaries"/> that should
/// automatically update when the current theme changes.
/// </summary>
public sealed class ThemeResourceExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public object? ResourceKey { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeResourceExtension"/> class.
    /// </summary>
    public ThemeResourceExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeResourceExtension"/> class with the specified key.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    public ThemeResourceExtension(object resourceKey)
    {
        ResourceKey = resourceKey;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (ResourceKey == null)
            return null;

        var provideValueTarget = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var targetObject = provideValueTarget?.TargetObject as DependencyObject;
        var targetProperty = provideValueTarget?.TargetProperty as DependencyProperty;

        if (targetObject is FrameworkElement targetElement && targetProperty != null)
        {
            // Register runtime tracking so the property is refreshed on theme changes.
            // ThemeManager.ApplyTheme triggers DynamicResourceBindingOperations.RefreshAll,
            // which repropagates the resource through the subscription list.
            DynamicResourceBindingOperations.SetDynamicResource(targetElement, targetProperty, ResourceKey);
            return ResourceLookup.FindResource(targetElement, ResourceKey);
        }

        if (provideValueTarget?.TargetObject is FrameworkElement element)
        {
            return ResourceLookup.FindResource(element, ResourceKey) ?? new DynamicResourceReference(ResourceKey);
        }

        // For deferred cases (e.g., Setter.Value), preserve the reference so style runtime can resolve it.
        return new DynamicResourceReference(ResourceKey);
    }
}

/// <summary>
/// XAML markup extension for x:Null.
/// </summary>
public sealed class NullExtension : MarkupExtension
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider) => null;
}

/// <summary>
/// XAML markup extension for x:Static.
/// Retrieves static fields, properties, constants, or enum values.
/// </summary>
public sealed class StaticExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the member name (in TypeName.MemberName format).
    /// </summary>
    public string? Member { get; set; }

    /// <summary>
    /// Gets or sets the member type (alternative to specifying in Member string).
    /// </summary>
    public Type? MemberType { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticExtension"/> class.
    /// </summary>
    public StaticExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticExtension"/> class with the specified member.
    /// </summary>
    /// <param name="member">The member name in TypeName.MemberName format.</param>
    public StaticExtension(string member)
    {
        Member = member;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("StaticExtension reads public static fields/properties via reflection on the resolved Type. Owner types reachable from XAML are preserved via XamlTypeRegistry registrations with DAM annotations.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of MarkupExtension.ProvideValue annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Member))
            return null;

        // Parse TypeName.MemberName
        var lastDot = Member.LastIndexOf('.');
        if (lastDot < 0)
        {
            // Check if it's an enum value with MemberType specified
            if (MemberType != null && MemberType.IsEnum)
            {
                return Enum.Parse(MemberType, Member);
            }
            return null;
        }

        var typeName = Member.Substring(0, lastDot);
        var memberName = Member.Substring(lastDot + 1);

        // Resolve the type
        var type = MemberType ?? ResolveType(typeName);
        if (type == null)
            return null;

        // Check for enum value
        if (type.IsEnum)
        {
            return Enum.Parse(type, memberName);
        }

        // Check for static field
        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
        if (field != null)
            return field.GetValue(null);

        // Check for static property
        var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
        if (property != null)
            return property.GetValue(null);

        return null;
    }

    private static Type? ResolveType(string typeName)
    {
        // AOT-safe: Use static type registry instead of AppDomain scanning
        return XamlTypeRegistry.GetType(typeName);
    }
}

/// <summary>
/// XAML markup extension for x:Array.
/// Creates an array of the specified type.
/// </summary>
public sealed class ArrayExtension : MarkupExtension
{
    private readonly List<object?> _items = new();

    /// <summary>
    /// Gets or sets the type of the array elements.
    /// </summary>
    public Type? Type { get; set; }

    /// <summary>
    /// Gets the items collection.
    /// </summary>
    public IList<object?> Items => _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrayExtension"/> class.
    /// </summary>
    public ArrayExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrayExtension"/> class with the specified type.
    /// </summary>
    public ArrayExtension(Type type)
    {
        Type = type;
    }

    /// <summary>
    /// Adds an item to the array.
    /// </summary>
    public void AddChild(object? item)
    {
        _items.Add(item);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        var elementType = Type ?? typeof(object);
        var array = Array.CreateInstance(elementType, _items.Count);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item != null && !elementType.IsAssignableFrom(item.GetType()))
            {
                // Try to convert
                item = Convert.ChangeType(item, elementType);
            }
            array.SetValue(item, i);
        }

        return array;
    }
}

/// <summary>
/// XAML markup extension for x:Type.
/// </summary>
public sealed class TypeExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the type name.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Gets or sets the type.
    /// </summary>
    public Type? Type { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeExtension"/> class.
    /// </summary>
    public TypeExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeExtension"/> class with the specified type name.
    /// </summary>
    /// <param name="typeName">The type name.</param>
    public TypeExtension(string typeName)
    {
        TypeName = typeName;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (Type != null)
            return Type;

        if (string.IsNullOrEmpty(TypeName))
            return null;

        // AOT-safe: Use static type registry instead of Type.GetType
        return XamlTypeRegistry.GetType(TypeName);
    }
}

/// <summary>
/// XAML markup extension for TemplateBinding.
/// Binds a property in a control template to a property on the templated parent.
/// </summary>
public sealed class TemplateBindingExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the name of the property on the templated parent to bind to.
    /// </summary>
    public string? Property { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateBindingExtension"/> class.
    /// </summary>
    public TemplateBindingExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateBindingExtension"/> class with the specified property name.
    /// </summary>
    /// <param name="property">The name of the property to bind to.</param>
    public TemplateBindingExtension(string property)
    {
        Property = property;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Override of a base member that is annotated with RequiresUnreferencedCode.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Override of a base member that is annotated with RequiresDynamicCode.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Property))
            return null;

        var provideValueTarget = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var targetObject = provideValueTarget?.TargetObject;
        var targetProperty = provideValueTarget?.TargetProperty as DependencyProperty;

        if (targetObject is DependencyObject depObj && targetProperty != null)
        {
            // 普通元素属性：立即建立 binding，让 BindingExpression 把值流到目标 DP。
            var binding = new DeferredTemplateBinding(Property);
            depObj.SetBinding(targetProperty, binding);
            // 返回 null 而不是 binding：避免下游再把 binding 实例当作属性值二次写入。
            return null;
        }

        // Setter / Trigger 等上下文：targetObject 不是 DependencyObject 或目标属性不是
        // DependencyProperty（例如 setter.Value 的 PropertyInfo），无法立即建立连接。
        // 此时返回 BindingBase 实例,由 Setter.Apply / ApplyTriggerSetters 在 trigger 触发
        // 时调用 SetBinding。如果在这里返回 null,setter.Value 会被设成 null,触发器激活后
        // 直接把目标 DP 写成 null（典型现象：hover 后文本变透明、边框消失）。
        return new DeferredTemplateBinding(Property);
    }
}

/// <summary>
/// A deferred template binding that resolves the property name when the TemplatedParent is available.
/// </summary>
public sealed class DeferredTemplateBinding : BindingBase
{
    /// <summary>
    /// Gets the property name to bind to on the templated parent.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeferredTemplateBinding"/> class.
    /// </summary>
    /// <param name="propertyName">The property name to bind to.</param>
    public DeferredTemplateBinding(string propertyName)
    {
        PropertyName = propertyName;
    }

    /// <inheritdoc />
    internal override BindingExpressionBase CreateBindingExpression(DependencyObject target, DependencyProperty targetProperty)
    {
        return new DeferredTemplateBindingExpression(this, target, targetProperty);
    }
}

/// <summary>
/// Binding expression for a deferred template binding.
/// Resolves the property name against the TemplatedParent's type when activated.
/// </summary>
internal sealed class DeferredTemplateBindingExpression : BindingExpressionBase
{
    private readonly DeferredTemplateBinding _binding;
    private FrameworkElement? _templatedParent;
    private DependencyProperty? _sourceProperty;

    public DeferredTemplateBindingExpression(DeferredTemplateBinding binding, DependencyObject target, DependencyProperty targetProperty)
        : base(target, targetProperty)
    {
        _binding = binding;
    }

    internal override void Activate()
    {
        if (IsActive)
            return;

        // Find the templated parent
        _templatedParent = FindTemplatedParent(Target);
        if (_templatedParent == null)
        {
            // TemplatedParent not set yet - leave IsActive=false so we can retry later
            return;
        }

        // Resolve the property name against the templated parent's type
        _sourceProperty = ResolveDependencyProperty(_templatedParent.GetType(), _binding.PropertyName);
        if (_sourceProperty == null)
        {
            return;
        }

        // Only set IsActive=true after successfully resolving everything
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
        if (dp == _sourceProperty)
        {
            TransferValue();
        }
    }

    private void TransferValue()
    {
        if (_templatedParent == null || _sourceProperty == null)
            return;

        var value = _templatedParent.GetValue(_sourceProperty);

        // WPF parity (StyleHelper.GetChildValueHelper, TemplateBinding case): a value the target
        // property cannot legally hold — null for a non-nullable value type, or an instance of an
        // incompatible type — must NOT be pinned into the ParentTemplate precedence layer. Doing so
        // would shadow the target's own default and crash on unbox at layout (e.g. (Thickness)null).
        // The source property here is resolved by NAME against the templated parent's type, so a
        // type mismatch (or a null reference-typed source bound onto a value-type target) is entirely
        // possible. TemplateBinding performs no implicit conversion, so an invalid value degrades to
        // DependencyProperty.UnsetValue semantics: clear this layer's contribution and let the
        // property fall through to its default / lower-precedence value.
        if (!TargetProperty.IsValidType(value))
        {
            System.Diagnostics.Trace.WriteLine(
                $"[Jalium.UI] TemplateBinding skip: 模板父级 {_templatedParent.GetType().Name}.{_binding.PropertyName} 的值 " +
                $"'{value ?? "<null>"}' 与目标 DP {TargetProperty.OwnerType.Name}.{TargetProperty.Name} " +
                $"({TargetProperty.PropertyType.Name}) 不兼容，回退默认值。");
            Target.ClearLayerValue(TargetProperty, DependencyObject.LayerValueSource.ParentTemplate);
            return;
        }

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

    private static DependencyProperty? ResolveDependencyProperty(Type type, string propertyName)
    {
        // AOT-safe DependencyProperty lookup via the registry (no reflection).
        return DependencyProperty.FromName(type, propertyName);
    }
}

/// <summary>
/// Parser for markup extension syntax (e.g., "{Binding Path=Name}").
/// </summary>
internal static class MarkupExtensionParser
{
    /// <summary>
    /// Tries to parse a markup extension from an attribute value.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Some markup extensions (e.g. x:Static) reflect on the resolved Type to read fields/properties.")]
    public static bool TryParse(string value, object targetObject, PropertyInfo? targetProperty, out object? result)
    {
        return TryParse(value, targetObject, targetProperty, null, out result);
    }

    /// <summary>
    /// Tries to parse a markup extension from an attribute value with ambient resource support.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Some markup extensions (e.g. x:Static) reflect on the resolved Type to read fields/properties.")]
    public static bool TryParse(string value, object targetObject, PropertyInfo? targetProperty, IAmbientResourceProvider? ambientProvider, out object? result)
    {
        result = null;

        if (string.IsNullOrEmpty(value))
            return false;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            return false;

        // Extract the extension content
        var content = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (string.IsNullOrEmpty(content))
            return false;

        // Find the extension name
        var spaceIndex = content.IndexOf(' ');
        var extensionName = spaceIndex >= 0 ? content.Substring(0, spaceIndex) : content;
        var parameters = spaceIndex >= 0 ? content.Substring(spaceIndex + 1).Trim() : string.Empty;

        // Create the markup extension
        var extension = CreateMarkupExtension(extensionName, parameters, ambientProvider);
        if (extension == null)
            return false;

        result = ProvideExtensionValue(extension, targetObject, targetProperty?.Name, ambientProvider);
        return true;
    }

    /// <summary>
    /// Builds the markup-extension service provider, resolves the target
    /// <see cref="DependencyProperty"/> from <paramref name="targetPropertyName"/>, and
    /// invokes <see cref="MarkupExtension.ProvideValue"/> — the exact tail
    /// <see cref="TryParse(string, object, PropertyInfo?, IAmbientResourceProvider?, out object?)"/>
    /// uses. Factored out so the SG-lowered compiled-binding path
    /// (<see cref="BuildBindingExtension"/>) applies a binding through byte-identical
    /// runtime machinery — no behavioural divergence from a runtime-parsed
    /// <c>{Binding ...}</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("MarkupExtension.ProvideValue may reflect on resolved types.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "This is the runtime XAML markup-extension parser; invoking it is the documented consumer responsibility under AOT. The base MarkupExtension.ProvideValue carries RequiresDynamicCode only because 'some extensions (e.g. x:Array) construct arrays of a runtime-supplied element Type' — that single extension is the dynamic-code case. The built-in extensions reachable through TryParse here (Binding/StaticResource/DynamicResource/x:Static etc.) construct no runtime-typed arrays, so no dynamic code is emitted at this call site.")]
    internal static object? ProvideExtensionValue(
        MarkupExtension extension,
        object targetObject,
        string? targetPropertyName,
        IAmbientResourceProvider? ambientProvider)
    {
        var serviceProvider = new MarkupExtensionServiceProvider();

        if (ambientProvider != null)
        {
            serviceProvider.AddService(typeof(IAmbientResourceProvider), ambientProvider);
        }

        DependencyProperty? dp = null;
        if (targetObject is DependencyObject && !string.IsNullOrEmpty(targetPropertyName))
        {
            dp = FindDependencyProperty(targetObject.GetType(), targetPropertyName!);
        }

        var provideValueTarget = new ProvideValueTarget
        {
            TargetObject = targetObject,
            TargetProperty = dp
        };
        serviceProvider.AddService(typeof(IProvideValueTarget), provideValueTarget);

        return extension.ProvideValue(serviceProvider);
    }

    /// <summary>
    /// Reconstructs a <see cref="BindingExtension"/> from the SG-pre-split
    /// <c>{Binding ...}</c> parameter list. This is <see cref="CreateBindingExtension"/>
    /// minus the <see cref="SplitParameters"/> string work (which the SourceGenerator did
    /// at compile time): the positional segment becomes <see cref="BindingExtension.Path"/>
    /// and every <c>name=value</c> pair flows through the <b>verbatim</b>
    /// <see cref="SetBindingParameter"/> — so Converter / Source / RelativeSource /
    /// ConverterParameter / FallbackValue / TargetNullValue and any nested markup
    /// extension resolve exactly as a runtime-parsed binding (incl. the deferred
    /// <c>Converter={StaticResource X}</c> → <see cref="BindingExtension.PendingConverterKey"/>
    /// path).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Forwards to SetBindingParameter which may invoke nested markup extensions that reflect on user types.")]
    internal static BindingExtension BuildBindingExtension(
        string? positionalPath,
        IReadOnlyList<string> names,
        IReadOnlyList<string> values,
        IAmbientResourceProvider? ambientProvider)
    {
        var extension = new BindingExtension();
        if (!string.IsNullOrEmpty(positionalPath))
            extension.Path = positionalPath!.Trim();

        var count = names.Count < values.Count ? names.Count : values.Count;
        for (var i = 0; i < count; i++)
        {
            SetBindingParameter(extension, names[i].Trim(), values[i].Trim(), ambientProvider);
        }

        return extension;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Some markup extensions (e.g. x:Static) reflect on the resolved Type to read fields/properties.")]
    private static MarkupExtension? CreateMarkupExtension(string name, string parameters, IAmbientResourceProvider? ambientProvider = null)
    {
        // Handle special x: namespace extensions
        if (name.StartsWith("x:", StringComparison.Ordinal))
        {
            name = name.Substring(2);
        }

        return name.ToLowerInvariant() switch
        {
            "binding" => CreateBindingExtension(parameters, ambientProvider),
            "staticresource" => CreateStaticResourceExtension(parameters),
            "dynamicresource" => CreateDynamicResourceExtension(parameters),
            "themeresource" => CreateThemeResourceExtension(parameters),
            "templatebinding" => CreateTemplateBindingExtension(parameters),
            "null" => new NullExtension(),
            "type" => CreateTypeExtension(parameters),
            "static" => CreateStaticExtension(parameters),
            "array" => CreateArrayExtension(parameters),
            _ => null
        };
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Forwards to SetBindingParameter which may invoke nested markup extensions that reflect on user types.")]
    private static BindingExtension CreateBindingExtension(string parameters, IAmbientResourceProvider? ambientProvider = null)
    {
        var extension = new BindingExtension();

        if (string.IsNullOrWhiteSpace(parameters))
            return extension;

        // Parse parameters
        var parts = SplitParameters(parameters);
        foreach (var part in parts)
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex < 0)
            {
                // Positional parameter (first one is Path)
                extension.Path = part.Trim();
            }
            else
            {
                var paramName = part.Substring(0, equalsIndex).Trim();
                var paramValue = part.Substring(equalsIndex + 1).Trim();

                SetBindingParameter(extension, paramName, paramValue, ambientProvider);
            }
        }

        return extension;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Forwards to ResolveNestedValue which can call markup extensions that reflect on user types.")]
    private static void SetBindingParameter(BindingExtension extension, string name, string value, IAmbientResourceProvider? ambientProvider = null)
    {
        switch (name.ToLowerInvariant())
        {
            case "path":
                extension.Path = value;
                break;
            case "mode":
                if (Enum.TryParse<BindingMode>(value, true, out var mode))
                    extension.Mode = mode;
                break;
            case "updatesourcetrigger":
                if (Enum.TryParse<UpdateSourceTrigger>(value, true, out var trigger))
                    extension.UpdateSourceTrigger = trigger;
                break;
            case "elementname":
                extension.ElementName = value;
                break;
            case "fallbackvalue":
                extension.FallbackValue = ResolveNestedValue(value, ambientProvider) ?? value;
                break;
            case "targetnullvalue":
                extension.TargetNullValue = ResolveNestedValue(value, ambientProvider) ?? value;
                break;
            case "stringformat":
                var fmt = value.Trim('\'', '"');
                // Strip leading {} XAML escape sequence so "{}{0:F0}" becomes "{0:F0}"
                if (fmt.StartsWith("{}", StringComparison.Ordinal))
                    fmt = fmt.Substring(2);
                extension.StringFormat = fmt;
                break;
            case "converter":
                {
                    var resolved = ResolveNestedValue(value, ambientProvider);
                    if (resolved is IValueConverter converter)
                    {
                        extension.Converter = converter;
                    }
                    else
                    {
                        // Resolution failed at parse time. If the spec is the common
                        // "{StaticResource X}" form, record X for deferred lookup —
                        // BindingExpression.Activate will resolve it through the target
                        // FE's resource chain once the visual tree is in place. This is
                        // the standard recovery path when SG-emitted template lambdas
                        // run SetProperty(string) before the element has been attached.
                        if (TryExtractStaticResourceKey(value, out var key))
                        {
                            extension.PendingConverterKey = key;
                        }
                        else
                        {
                            // Spec is a non-StaticResource expression we couldn't resolve
                            // (e.g. {x:Static}, {Binding}). Leave Converter null so the
                            // binding exhibits the same diagnostics it would in WPF.
                            extension.Converter = null;
                        }
                    }
                }
                break;
            case "converterparameter":
                extension.ConverterParameter = ResolveNestedValue(value, ambientProvider) ?? value;
                break;
            case "source":
                extension.Source = ResolveNestedValue(value, ambientProvider) ?? value;
                break;
            case "relativesource":
                extension.RelativeSource = ParseRelativeSource(value);
                break;
            case "validatesondataerrors":
                if (bool.TryParse(value, out var validatesOnDataErrors))
                    extension.ValidatesOnDataErrors = validatesOnDataErrors;
                break;
            case "validatesonnotifydataerrors":
                if (bool.TryParse(value, out var validatesOnNotifyDataErrors))
                    extension.ValidatesOnNotifyDataErrors = validatesOnNotifyDataErrors;
                break;
            case "notifyonvalidationerror":
                if (bool.TryParse(value, out var notifyOnValidationError))
                    extension.NotifyOnValidationError = notifyOnValidationError;
                break;
        }
    }

    /// <summary>
    /// Try to extract the resource key from a <c>{StaticResource X}</c> spec string.
    /// Used to record a deferred Converter lookup when parse-time resolution fails —
    /// the key is later fed to <see cref="ResourceLookup.FindResource"/> against the
    /// binding target's FE in <c>BindingExpression.Activate</c>.
    /// </summary>
    /// <remarks>
    /// Only the simple positional form is recognised (no nested extensions, no commas).
    /// More elaborate specs (<c>{StaticResource Key, ...}</c>) fall through to the
    /// caller's null-Converter path; covering them would require teaching the deferred
    /// resolver about every markup-extension shape.
    /// </remarks>
    private static bool TryExtractStaticResourceKey(string spec, out string? key)
    {
        key = null;
        if (string.IsNullOrEmpty(spec))
            return false;

        var trimmed = spec.Trim();
        if (trimmed.Length < 4 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            return false;

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        const string Prefix = "StaticResource";
        if (!inner.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        if (inner.Length <= Prefix.Length)
            return false;
        if (!char.IsWhiteSpace(inner[Prefix.Length]))
            return false;

        var rest = inner.Substring(Prefix.Length + 1).Trim();
        // Reject anything with commas / nested braces — multi-arg specs aren't deferrable
        // through the simple key channel.
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] == ',' || rest[i] == '{' || rest[i] == '}' || char.IsWhiteSpace(rest[i]))
                return false;
        }

        if (rest.Length == 0)
            return false;
        key = rest;
        return true;
    }

    /// <summary>
    /// Resolves a nested markup extension value (e.g., {StaticResource BoolToVis}) or returns null if not a markup extension.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Some markup extensions (e.g. x:Static) reflect on the resolved Type to read fields/properties.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "This resolves a nested binding-parameter markup extension (e.g. {StaticResource X}, {x:Static}) for the runtime XAML parser — the documented consumer responsibility under AOT. The base MarkupExtension.ProvideValue carries RequiresDynamicCode only because 'some extensions (e.g. x:Array) construct arrays of a runtime-supplied element Type'; the nested extensions valid as Binding parameters here construct no runtime-typed arrays, so no dynamic code is emitted at this call site.")]
    private static object? ResolveNestedValue(string value, IAmbientResourceProvider? ambientProvider)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            return null;

        // Parse and resolve the nested markup extension
        var serviceProvider = new MarkupExtensionServiceProvider();
        if (ambientProvider != null)
            serviceProvider.AddService(typeof(IAmbientResourceProvider), ambientProvider);

        var content = trimmed.Substring(1, trimmed.Length - 2).Trim();
        var spaceIndex = content.IndexOf(' ');
        var extensionName = spaceIndex >= 0 ? content.Substring(0, spaceIndex) : content;
        var parameters = spaceIndex >= 0 ? content.Substring(spaceIndex + 1).Trim() : string.Empty;

        var nestedExtension = CreateMarkupExtension(extensionName, parameters);
        return nestedExtension?.ProvideValue(serviceProvider);
    }

    private static RelativeSource? ParseRelativeSource(string value)
    {
        // Handle nested markup extension: {RelativeSource AncestorType=ComboBox}
        var trimmed = value.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            var content = trimmed.Substring(1, trimmed.Length - 2).Trim();

            // Check for "RelativeSource" prefix
            if (content.StartsWith("RelativeSource", StringComparison.OrdinalIgnoreCase))
            {
                var parameters = content.Substring("RelativeSource".Length).Trim();
                return CreateRelativeSourceFromParameters(parameters);
            }
        }

        // Handle simple values like "Self" or "TemplatedParent"
        return CreateRelativeSourceFromParameters(value);
    }

    private static RelativeSource? CreateRelativeSourceFromParameters(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        // Check for simple mode values
        var trimmedParams = parameters.Trim();

        // Handle "Self", "TemplatedParent", "FindAncestor"
        if (Enum.TryParse<RelativeSourceMode>(trimmedParams, true, out var simpleMode))
        {
            return new RelativeSource(simpleMode);
        }

        // Parse named parameters: AncestorType=ComboBox, AncestorLevel=1
        var parts = SplitParameters(parameters);
        RelativeSourceMode mode = RelativeSourceMode.FindAncestor;
        Type? ancestorType = null;
        int ancestorLevel = 1;

        foreach (var part in parts)
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex < 0)
            {
                // Positional parameter - might be mode
                if (Enum.TryParse<RelativeSourceMode>(part.Trim(), true, out var modeValue))
                {
                    mode = modeValue;
                }
                continue;
            }

            var paramName = part.Substring(0, equalsIndex).Trim();
            var paramValue = part.Substring(equalsIndex + 1).Trim();

            switch (paramName.ToLowerInvariant())
            {
                case "mode":
                    if (Enum.TryParse<RelativeSourceMode>(paramValue, true, out var modeVal))
                        mode = modeVal;
                    break;
                case "ancestortype":
                    ancestorType = ResolveType(paramValue);
                    break;
                case "ancestorlevel":
                    if (int.TryParse(paramValue, out var level))
                        ancestorLevel = level;
                    break;
            }
        }

        // If AncestorType is specified, mode is FindAncestor
        if (ancestorType != null)
        {
            mode = RelativeSourceMode.FindAncestor;
        }

        var relativeSource = new RelativeSource(mode)
        {
            AncestorType = ancestorType,
            AncestorLevel = ancestorLevel
        };

        return relativeSource;
    }

    private static Type? ResolveType(string typeName)
    {
        // Remove quotes if present
        var cleanName = typeName.Trim().Trim('"', '\'');

        // Handle x:Type syntax
        if (cleanName.StartsWith("{x:Type", StringComparison.OrdinalIgnoreCase))
        {
            var start = cleanName.IndexOf(' ') + 1;
            var end = cleanName.IndexOf('}');
            if (start > 0 && end > start)
            {
                cleanName = cleanName.Substring(start, end - start).Trim();
            }
        }

        // AOT-safe: Use static type registry instead of AppDomain scanning / Type.GetType
        return XamlTypeRegistry.GetType(cleanName);
    }

    private static StaticResourceExtension CreateStaticResourceExtension(string parameters)
    {
        var key = ParseResourceKey(parameters);
        return new StaticResourceExtension(key);
    }

    private static DynamicResourceExtension CreateDynamicResourceExtension(string parameters)
    {
        var key = ParseResourceKey(parameters);
        return new DynamicResourceExtension(key);
    }

    private static ThemeResourceExtension CreateThemeResourceExtension(string parameters)
    {
        var key = ParseResourceKey(parameters);
        return new ThemeResourceExtension(key);
    }

    private static object ParseResourceKey(string parameters)
    {
        var trimmed = parameters.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return string.Empty;

        var parts = SplitParameters(trimmed);
        string keyText = trimmed;
        foreach (var part in parts)
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            var name = part.Substring(0, equalsIndex).Trim();
            if (!name.Equals("ResourceKey", StringComparison.OrdinalIgnoreCase))
                continue;

            keyText = part[(equalsIndex + 1)..].Trim();
            break;
        }

        keyText = keyText.Trim().Trim('"', '\'');

        // Support x:Type resource keys: {StaticResource {x:Type Button}}
        // should look up the Type key instead of the literal string.
        if (keyText.StartsWith("{x:Type", StringComparison.OrdinalIgnoreCase))
        {
            var type = ResolveType(keyText);
            if (type != null)
                return type;
        }

        return keyText;
    }

    private static StaticExtension CreateStaticExtension(string parameters)
    {
        var member = parameters.Trim();
        return new StaticExtension(member);
    }

    private static ArrayExtension CreateArrayExtension(string parameters)
    {
        // Parse Type= parameter
        var extension = new ArrayExtension();
        var parts = SplitParameters(parameters);

        foreach (var part in parts)
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex >= 0)
            {
                var paramName = part.Substring(0, equalsIndex).Trim();
                var paramValue = part.Substring(equalsIndex + 1).Trim();

                if (paramName.Equals("Type", StringComparison.OrdinalIgnoreCase))
                {
                    extension.Type = ResolveType(paramValue);
                }
            }
        }

        return extension;
    }

    private static TypeExtension CreateTypeExtension(string parameters)
    {
        var typeName = parameters.Trim();
        return new TypeExtension(typeName);
    }

    private static TemplateBindingExtension CreateTemplateBindingExtension(string parameters)
    {
        var propertyName = parameters.Trim();
        return new TemplateBindingExtension(propertyName);
    }

    private static List<string> SplitParameters(string parameters)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var braceDepth = 0;
        var inQuote = false;

        foreach (var c in parameters)
        {
            if (c == '\'' || c == '"')
            {
                inQuote = !inQuote;
                current.Append(c);
            }
            else if (!inQuote && c == '{')
            {
                braceDepth++;
                current.Append(c);
            }
            else if (!inQuote && c == '}')
            {
                braceDepth--;
                current.Append(c);
            }
            else if (!inQuote && braceDepth == 0 && c == ',')
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString().Trim());
        }

        return result;
    }

    private static DependencyProperty? FindDependencyProperty(Type type, string propertyName)
    {
        // AOT-safe lookup: walks the registered DependencyProperty registry by owner type
        // and property name. Avoids reflection on a dynamically supplied owner type.
        return DependencyProperty.FromName(type, propertyName);
    }
}
