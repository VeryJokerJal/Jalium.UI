using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Data;
using Jalium.UI.Markup;

namespace Jalium.UI;

/// <summary>Provides the WPF-compatible markup extension for a template-parent property.</summary>
[TypeConverter(typeof(TemplateBindingExtensionConverter))]
public class TemplateBindingExtension : MarkupExtension
{
    /// <summary>Initializes an empty template binding extension.</summary>
    public TemplateBindingExtension()
    {
    }

    /// <summary>Initializes a template binding for the specified dependency property.</summary>
    public TemplateBindingExtension(DependencyProperty property)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
    }

    /// <summary>Gets or sets the dependency property on the templated parent.</summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>Gets or sets an optional value converter.</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>Gets or sets the value supplied to <see cref="Converter"/>.</summary>
    public object? ConverterParameter { get; set; }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Template binding markup can resolve runtime dependency-property metadata.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Template binding markup can activate runtime binding infrastructure.")]
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Property is null)
        {
            throw new InvalidOperationException("TemplateBindingExtension.Property must be set before providing a value.");
        }

        var expression = new TemplateBindingExpression(this);
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
            target.TargetObject is DependencyObject dependencyObject &&
            target.TargetProperty is DependencyProperty targetProperty)
        {
            // A TemplateBinding is the optimized form of a one-way binding to the
            // templated parent. Install the equivalent ordinary binding here so the
            // public FrameworkElement.GetBindingExpression API can expose it as a
            // BindingExpression while preserving the same source and conversion rules.
            dependencyObject.SetBinding(targetProperty, new Binding(Property.Name)
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.OneWay,
                Converter = Converter,
                ConverterParameter = ConverterParameter,
            });
        }

        return expression;
    }
}

/// <summary>Represents a deferred template binding expression.</summary>
[TypeConverter(typeof(TemplateBindingExpressionConverter))]
public class TemplateBindingExpression : Expression
{
    internal TemplateBindingExpression(TemplateBindingExtension templateBindingExtension)
    {
        TemplateBindingExtension = templateBindingExtension ??
            throw new ArgumentNullException(nameof(templateBindingExtension));
    }

    /// <summary>Gets the extension that created this expression.</summary>
    public TemplateBindingExtension TemplateBindingExtension { get; }
}

/// <summary>Converts template-binding expressions to serializable descriptors.</summary>
public class TemplateBindingExpressionConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(InstanceDescriptor) && value is TemplateBindingExpression expression)
        {
            return new TemplateBindingExtensionConverter().ConvertTo(
                context,
                culture,
                expression.TemplateBindingExtension,
                destinationType);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>Converts template-binding extensions to serializable descriptors.</summary>
public class TemplateBindingExtensionConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "The public DependencyProperty constructor is a fixed compatibility contract on TemplateBindingExtension.")]
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(InstanceDescriptor) &&
            value is TemplateBindingExtension extension &&
            extension.Property is not null)
        {
            var constructor = typeof(TemplateBindingExtension).GetConstructor([typeof(DependencyProperty)])!;
            return new InstanceDescriptor(constructor, new object?[] { extension.Property });
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
