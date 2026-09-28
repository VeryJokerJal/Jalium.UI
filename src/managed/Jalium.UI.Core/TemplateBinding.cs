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
    internal override void OnTemplateParentChanged() => Deactivate();

    private readonly TemplateBinding _binding;
    private FrameworkElement? _templatedParent;

    public TemplateBindingExpression(TemplateBinding binding, DependencyObject target, DependencyProperty targetProperty)
        : base(binding, target, targetProperty)
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
        AttachToBindingGroup();

        // Subscribe to property changes on the templated parent
        _templatedParent.PropertyChangedInternal += OnTemplatedParentPropertyChanged;
        if (Styling.CssBorderRadiusProperties.IsTemplateRadiusBinding(_binding.Property, TargetProperty))
            _templatedParent.CssCornerRadiusPresentationChanged += TransferCssRadius;
        if (IsCssBorderBinding())
            _templatedParent.CssBorderPresentationChanged += TransferCssBorder;

        // Initial value transfer
        TransferValue();
    }

    internal override void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;
        DetachFromBindingGroup();

        if (_templatedParent != null)
        {
            _templatedParent.PropertyChangedInternal -= OnTemplatedParentPropertyChanged;
            _templatedParent.CssCornerRadiusPresentationChanged -= TransferCssRadius;
            _templatedParent.CssBorderPresentationChanged -= TransferCssBorder;
            _templatedParent = null;
        }

        Target.ClearLayerValue(TargetProperty, DependencyObject.LayerValueSource.ParentTemplate);
        if (Styling.CssBorderRadiusProperties.IsTemplateRadiusBinding(_binding.Property, TargetProperty))
            Styling.CssBorderRadiusProperties.ClearTemplate(Target);
        if (Styling.CssBorderPaintProperties.IsTemplateBrushBinding(_binding.Property, TargetProperty))
            Styling.CssBorderPaintProperties.ClearTemplate(Target);
        if (Styling.CssBorderStyleProperties.IsTemplateThicknessBinding(_binding.Property, TargetProperty))
            Styling.CssBorderStyleProperties.ClearTemplate(Target);
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
        else if (Styling.CssBorderRadiusProperties.IsTemplatePresentationInput(dp)) TransferCssRadius();
    }

    private void TransferCssRadius()
    {
        if (IsActive && _templatedParent is not null && Styling.CssBorderRadiusProperties.IsTemplateRadiusBinding(_binding.Property, TargetProperty))
            Styling.CssBorderRadiusProperties.TransferTemplate(_templatedParent, Target);
    }

    private bool IsCssBorderBinding() =>
        Styling.CssBorderPaintProperties.IsTemplateBrushBinding(_binding.Property, TargetProperty) ||
        Styling.CssBorderStyleProperties.IsTemplateThicknessBinding(_binding.Property, TargetProperty);

    private void TransferCssBorder()
    {
        if (!IsActive || _templatedParent is null) return;
        if (Styling.CssBorderPaintProperties.IsTemplateBrushBinding(_binding.Property, TargetProperty))
            Styling.CssBorderPaintProperties.TransferTemplate(_templatedParent, Target);
        if (Styling.CssBorderStyleProperties.IsTemplateThicknessBinding(_binding.Property, TargetProperty))
            Styling.CssBorderStyleProperties.TransferTemplate(_templatedParent, Target);
    }

    private void TransferValue()
    {
        if (_templatedParent == null || _binding.Property == null)
            return;

        var value = _templatedParent.GetValue(_binding.Property);

        // WPF parity (StyleHelper.GetChildValueHelper, TemplateBinding case): a value the target
        // property cannot legally hold — null for a non-nullable value type, or an instance of an
        // incompatible type — must NOT be pinned into the ParentTemplate precedence layer. Doing so
        // would shadow the target's own default and crash on unbox at layout (e.g. (Thickness)null).
        // A string source is first offered to the XAML type-converter pipeline (see
        // TemplateBindingValueCoercion); anything that still cannot inhabit the target degrades to
        // DependencyProperty.UnsetValue semantics: clear this layer's contribution and let the
        // property fall through to its default / lower-precedence value.
        if (!TemplateBindingValueCoercion.TryCoerce(value, TargetProperty, out var effectiveValue))
        {
            System.Diagnostics.Trace.WriteLine(
                $"[Jalium.UI] TemplateBinding skip: 模板父级 {_templatedParent.GetType().Name}.{_binding.Property.Name} 的值 " +
                $"'{value ?? "<null>"}' 与目标 DP {TargetProperty.OwnerType.Name}.{TargetProperty.Name} " +
                $"({TargetProperty.PropertyType.Name}) 不兼容，回退默认值。");
            Target.ClearLayerValue(TargetProperty, DependencyObject.LayerValueSource.ParentTemplate);
            return;
        }

        Target.SetLayerValue(TargetProperty, effectiveValue, DependencyObject.LayerValueSource.ParentTemplate);
        TransferCssRadius();
        TransferCssBorder();
    }

    private static FrameworkElement? FindTemplatedParent(DependencyObject target)
    {
        if (target is FrameworkElement fe)
        {
            return fe.TemplatedParent as FrameworkElement;
        }
        return null;
    }
}

/// <summary>
/// Shared value gate for both TemplateBinding transfer paths — the strongly-typed
/// <see cref="TemplateBindingExpression"/> (code templates / <c>SetTemplateBinding(DP, DP)</c>) and the
/// name-resolved <c>DeferredTemplateBindingExpression</c> (<c>{TemplateBinding}</c> in jalxaml).
/// </summary>
/// <remarks>
/// <para>
/// The source property is picked by NAME/DP on the templated parent, so its CLR type need not match the
/// target DP's type. Markup routinely models an icon path as <c>string</c> and feeds it to a
/// <c>Geometry</c>-typed target (<c>Data="{TemplateBinding IconGeometry}"</c>) — exactly the same string
/// form the parser accepts when the value is written literally (<c>Data="M0,0 L10,10"</c>), which routes
/// through <c>XamlBuilder.SetProperty</c> → <c>TypeConverterRegistry</c>.
/// </para>
/// <para>
/// Before this gate existed, the TemplateBinding path skipped conversion entirely: a type mismatch was
/// dropped with only a <see cref="System.Diagnostics.Trace"/> line, so the target silently kept its
/// default (a Path with no geometry renders nothing — no exception, no visible diagnostic). Offering a
/// string source to the same converter registry the parser uses closes that asymmetry. Conversion is
/// attempted ONLY on the branch that used to be discarded, so every binding that already transferred is
/// bit-for-bit unaffected; a value that still cannot inhabit the target keeps the previous
/// clear-to-default behaviour.
/// </para>
/// </remarks>
internal static class TemplateBindingValueCoercion
{
    /// <summary>
    /// Produces a value legal for <paramref name="targetProperty"/>, converting a string source through
    /// the registered XAML type converters when the raw value cannot inhabit the target.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> with <paramref name="coerced"/> set to a value the target accepts;
    /// <see langword="false"/> when the value must be dropped (caller clears the ParentTemplate layer).
    /// </returns>
    internal static bool TryCoerce(object? value, DependencyProperty targetProperty, out object? coerced)
    {
        if (targetProperty.IsValidType(value))
        {
            coerced = value;
            return true;
        }

        // Only a string is convertible here. Any other mismatch is a genuine authoring error and keeps
        // the historical fail-closed behaviour rather than guessing at a conversion.
        if (value is string text)
        {
            var converter = Jalium.UI.Style.StringValueConverter;
            if (converter != null)
            {
                var underlyingType = Nullable.GetUnderlyingType(targetProperty.PropertyType)
                    ?? targetProperty.PropertyType;
                try
                {
                    // Converters signal failure two ways: null (GeometryTypeConverter) or a throw
                    // (ThicknessConverter's FormatException). Both must degrade to "drop", never escape
                    // into the caller — one malformed template value cannot be allowed to tear down the
                    // whole template application.
                    var converted = converter(text, underlyingType);
                    if (converted != null && targetProperty.IsValidType(converted))
                    {
                        coerced = converted;
                        return true;
                    }
                }
                catch
                {
                    // fall through to the drop path
                }
            }
        }

        coerced = null;
        return false;
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
