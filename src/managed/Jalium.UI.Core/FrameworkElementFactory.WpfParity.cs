using Jalium.UI.Data;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI;

/// <summary>
/// Describes a legacy programmatic template node. New code should prefer XAML or
/// <see cref="FrameworkTemplate"/>, but the factory remains supported for WPF compatibility.
/// </summary>
[Obsolete("FrameworkElementFactory is deprecated. Use FrameworkTemplate or XAML instead.")]
public class FrameworkElementFactory
{
    private readonly Dictionary<DependencyProperty, object?> _values = new();
    private readonly Dictionary<DependencyProperty, BindingBase> _bindings = new();
    private readonly Dictionary<DependencyProperty, object> _resourceReferences = new();
    private readonly List<(RoutedEvent RoutedEvent, Delegate Handler, bool HandledEventsToo)> _handlers = new();
    private FrameworkElementFactory? _lastChild;
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private Type? _type;
    private string? _text;
    private string? _name;

    /// <summary>Initializes an empty factory node.</summary>
    public FrameworkElementFactory()
    {
    }

    /// <summary>Initializes a factory node for the specified dependency-object type.</summary>
    public FrameworkElementFactory(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
    {
        Type = type;
    }

    /// <summary>Initializes a factory text node.</summary>
    public FrameworkElementFactory(string text)
    {
        Text = text;
    }

    /// <summary>Initializes a named factory node for the specified type.</summary>
    public FrameworkElementFactory(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type,
        string name)
    {
        Type = type;
        Name = name;
    }

    /// <summary>Gets or sets the dependency-object type created by this node.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? Type
    {
        get => _type;
        set
        {
            ThrowIfSealed();
            if (value is not null && !typeof(DependencyObject).IsAssignableFrom(value))
            {
                throw new ArgumentException("The factory type must derive from DependencyObject.", nameof(value));
            }

            _type = value;
            if (value is not null)
            {
                _text = null;
            }
        }
    }

    /// <summary>Gets or sets the text represented by a text node.</summary>
    public string? Text
    {
        get => _text;
        set
        {
            ThrowIfSealed();
            _text = value;
            if (value is not null)
            {
                _type = null;
            }
        }
    }

    /// <summary>Gets or sets the template name assigned to this node.</summary>
    public string? Name
    {
        get => _name;
        set
        {
            ThrowIfSealed();
            _name = value;
        }
    }

    /// <summary>Gets whether this node can no longer be changed.</summary>
    public bool IsSealed { get; private set; }

    /// <summary>Gets the parent factory node.</summary>
    public FrameworkElementFactory? Parent { get; private set; }

    /// <summary>Gets the first child factory node.</summary>
    public FrameworkElementFactory? FirstChild { get; private set; }

    /// <summary>Gets the next sibling factory node.</summary>
    public FrameworkElementFactory? NextSibling { get; private set; }

    /// <summary>Assigns a binding to a dependency property on created objects.</summary>
    public void SetBinding(DependencyProperty dp, BindingBase binding)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(binding);
        ThrowIfSealed();
        _bindings[dp] = binding;
        _values.Remove(dp);
        _resourceReferences.Remove(dp);
    }

    /// <summary>Adds a routed-event handler to created objects.</summary>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler) =>
        AddHandler(routedEvent, handler, handledEventsToo: false);

    /// <summary>Adds a routed-event handler to created objects.</summary>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfSealed();
        _handlers.Add((routedEvent, handler, handledEventsToo));
    }

    /// <summary>Removes a routed-event handler from this node.</summary>
    public void RemoveHandler(RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfSealed();
        _handlers.RemoveAll(item => item.RoutedEvent == routedEvent && item.Handler == handler);
    }

    /// <summary>Appends a child node.</summary>
    public void AppendChild(FrameworkElementFactory child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ThrowIfSealed();
        if (child.Parent is not null || child.NextSibling is not null)
        {
            throw new InvalidOperationException("The factory node already belongs to a tree.");
        }

        child.Parent = this;
        if (FirstChild is null)
        {
            FirstChild = child;
        }
        else
        {
            _lastChild!.NextSibling = child;
        }

        _lastChild = child;
    }

    /// <summary>Assigns a local value to a dependency property on created objects.</summary>
    public void SetValue(DependencyProperty dp, object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ThrowIfSealed();
        if (!dp.IsValidType(value))
        {
            throw new ArgumentException("The value is not valid for the dependency property.", nameof(value));
        }

        _values[dp] = value;
        _bindings.Remove(dp);
        _resourceReferences.Remove(dp);
    }

    /// <summary>Assigns a dynamic resource reference to a dependency property.</summary>
    public void SetResourceReference(DependencyProperty dp, object name)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(name);
        ThrowIfSealed();
        _resourceReferences[dp] = name;
        _bindings.Remove(dp);
        _values.Remove(dp);
    }

    internal void Seal()
    {
        if (IsSealed)
        {
            return;
        }

        IsSealed = true;
        for (FrameworkElementFactory? child = FirstChild; child != null; child = child.NextSibling)
        {
            child.Seal();
        }
    }

    private void ThrowIfSealed()
    {
        if (IsSealed)
        {
            throw new InvalidOperationException("The FrameworkElementFactory is sealed and cannot be changed.");
        }
    }
}
