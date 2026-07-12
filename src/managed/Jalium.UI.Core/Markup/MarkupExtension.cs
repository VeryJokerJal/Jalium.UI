namespace Jalium.UI.Markup;

/// <summary>
/// Base class for objects that provide a value to the XAML object writer.
/// </summary>
public abstract class MarkupExtension
{
    /// <summary>
    /// Returns the value supplied by this extension.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Some extensions (for example x:Static) reflect on user supplied types.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Some extensions (for example x:Array) construct arrays of runtime supplied types.")]
    public abstract object? ProvideValue(IServiceProvider serviceProvider);
}

/// <summary>Describes the object and member targeted by a markup extension.</summary>
public interface IProvideValueTarget
{
    /// <summary>Gets the target object.</summary>
    object? TargetObject { get; }

    /// <summary>Gets the target dependency property or CLR member.</summary>
    object? TargetProperty { get; }
}

/// <summary>
/// Marks a type or property whose value is available to nested XAML objects as ambient context.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AmbientAttribute : Attribute
{
}

/// <summary>
/// Lets a XAML object report whether one of its ambient properties currently has a value.
/// </summary>
public interface IQueryAmbient
{
    /// <summary>Returns whether the named property is available for ambient lookup.</summary>
    bool IsAmbientPropertyAvailable(string propertyName);
}

/// <summary>
/// Declares the value type returned by a markup extension.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class MarkupExtensionReturnTypeAttribute : Attribute
{
    /// <summary>Initializes an attribute without a declared return type.</summary>
    public MarkupExtensionReturnTypeAttribute()
    {
    }

    /// <summary>Initializes an attribute with the declared return type.</summary>
    public MarkupExtensionReturnTypeAttribute(Type returnType)
    {
        ReturnType = returnType;
    }

    /// <summary>
    /// Initializes an attribute with the declared return type and the legacy expression type.
    /// </summary>
    [Obsolete("ExpressionType is retained only for compatibility.")]
    public MarkupExtensionReturnTypeAttribute(Type returnType, Type expressionType)
    {
        ReturnType = returnType;
        ExpressionType = expressionType;
    }

    /// <summary>Gets the type returned by the extension.</summary>
    public Type? ReturnType { get; }

    /// <summary>Gets the legacy expression type.</summary>
    [Obsolete("ExpressionType is retained only for compatibility.")]
    public Type? ExpressionType { get; }
}
