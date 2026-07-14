using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI;

/// <summary>
/// Provides the shared loading, resource, name-scope, and sealing behavior for
/// framework templates.
/// </summary>
public abstract class FrameworkTemplate : DispatcherObject, Jalium.UI.Markup.INameScope, IQueryAmbient
{
    private readonly NameScope _nameScope = new();
    private Func<FrameworkElement>? _visualTreeFactory;
    private ResourceDictionary? _resources;
    private TemplateContent? _template;
    private bool _templateAssigned;
    private bool _isSealed;
    private string? _visualTreeXaml;
    private Assembly? _sourceAssembly;
    private IReadOnlyList<ResourceDictionary>? _ambientResourceDictionaries;

    /// <summary>Initializes a new framework template.</summary>
    protected FrameworkTemplate()
    {
    }

    /// <summary>Gets whether the template has content that can be instantiated.</summary>
    public bool HasContent
    {
        get
        {
            VerifyAccess();
            return _visualTreeFactory is not null
                || _template is not null
                || !string.IsNullOrEmpty(_visualTreeXaml);
        }
    }

    /// <summary>Gets whether the template has been made immutable.</summary>
    public bool IsSealed
    {
        get
        {
            VerifyAccess();
            return _isSealed;
        }
    }

    /// <summary>Gets or sets resources available while this template is instantiated.</summary>
    [Ambient]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ResourceDictionary Resources
    {
        get
        {
            VerifyAccess();
            return _resources ??= new ResourceDictionary();
        }
        set
        {
            VerifyAccess();
            CheckSealed();
            _resources = value;
        }
    }

    /// <summary>Gets or sets deferred content owned by this template.</summary>
    [Ambient]
    [DefaultValue(null)]
    public TemplateContent? Template
    {
        get => _template;
        set
        {
            VerifyAccess();
            CheckSealed();
            ArgumentNullException.ThrowIfNull(value);

            if (_templateAssigned)
            {
                throw new InvalidOperationException("Template content can only be assigned once.");
            }

            value.AttachTo(this);
            _template = value;
            _templateAssigned = true;
        }
    }

    /// <summary>
    /// Gets or sets the raw XAML retained by the XAML loader for deferred template parsing.
    /// </summary>
    internal string? VisualTreeXaml
    {
        get => _visualTreeXaml;
        set
        {
            CheckSealed();
            _visualTreeXaml = value;
        }
    }

    /// <summary>Gets or sets the assembly used to resolve deferred XAML types.</summary>
    internal Assembly? SourceAssembly
    {
        get => _sourceAssembly;
        set
        {
            CheckSealed();
            _sourceAssembly = value;
        }
    }

    /// <summary>Gets or sets the resource chain captured when the template was declared.</summary>
    internal IReadOnlyList<ResourceDictionary>? AmbientResourceDictionaries
    {
        get => _ambientResourceDictionaries;
        set
        {
            CheckSealed();
            _ambientResourceDictionaries = value;
        }
    }

    /// <summary>Assigns the compatibility factory used by Jalium templates.</summary>
    protected void SetVisualTreeFactory(Func<FrameworkElement> visualTreeFactory)
    {
        VerifyAccess();
        CheckSealed();
        ArgumentNullException.ThrowIfNull(visualTreeFactory);
        _visualTreeFactory = visualTreeFactory;
    }

    /// <summary>Returns the parser supplied by the concrete template type.</summary>
    protected virtual Func<string, Assembly?, FrameworkElement?>? DeferredXamlParser => null;

    /// <summary>
    /// Creates a new instance of the template content. Factory, deferred-content, and
    /// captured-XAML paths all flow through this method so derived templates receive the
    /// same post-load lifecycle.
    /// </summary>
    public DependencyObject? LoadContent()
    {
        // Instantiation only reads the template definition and creates a fresh object
        // graph. Jalium caches theme dictionaries process-wide, so even an as-yet
        // unsealed template can be observed from another UI dispatcher before its first
        // use. Authoring/mutation APIs remain thread-affine; loading is intentionally
        // dispatcher-neutral.

        DependencyObject? content;
        if (_visualTreeFactory is not null)
        {
            content = _visualTreeFactory.Invoke();
        }
        else if (_template is not null)
        {
            content = _template.LoadContent();
        }
        else
        {
            content = LoadDeferredXamlContent();
        }

        OnContentLoaded(content);
        return content;
    }

    /// <summary>Called after one template instance has been created.</summary>
    protected virtual void OnContentLoaded(DependencyObject? content)
    {
    }

    private FrameworkElement? LoadDeferredXamlContent()
    {
        var parser = DeferredXamlParser;
        if (parser is null || string.IsNullOrEmpty(_visualTreeXaml))
        {
            return null;
        }

        using (TemplateAmbientResourceContext.Push(BuildAmbientResourceChain()))
        {
            return parser(_visualTreeXaml, _sourceAssembly);
        }
    }

    private IReadOnlyList<ResourceDictionary>? BuildAmbientResourceChain()
    {
        if (_resources is null)
        {
            return _ambientResourceDictionaries;
        }

        if (_ambientResourceDictionaries is null || _ambientResourceDictionaries.Count == 0)
        {
            return new[] { _resources };
        }

        var dictionaries = new ResourceDictionary[_ambientResourceDictionaries.Count + 1];
        dictionaries[0] = _resources;
        for (int i = 0; i < _ambientResourceDictionaries.Count; i++)
        {
            dictionaries[i + 1] = _ambientResourceDictionaries[i];
        }

        return dictionaries;
    }

    /// <summary>Finds a named object in the instance applied to a templated parent.</summary>
    public object? FindName(string name, FrameworkElement templatedParent)
    {
        VerifyAccess();
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(templatedParent);
        ValidateTemplatedParent(templatedParent);

        for (int i = 0; i < templatedParent.VisualChildrenCount; i++)
        {
            if (templatedParent.GetVisualChild(i) is Visual child
                && FindNameInVisualTree(child, name) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static object? FindNameInVisualTree(Visual root, string name)
    {
        if (root is FrameworkElement element)
        {
            if (string.Equals(element.Name, name, StringComparison.Ordinal))
            {
                return element;
            }

            if (element.FindName(name) is { } scopedElement)
            {
                return scopedElement;
            }
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child
                && FindNameInVisualTree(child, name) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Registers an object in this template's definition name scope.</summary>
    public void RegisterName(string name, object scopedElement)
    {
        VerifyAccess();
        _nameScope.RegisterName(name, scopedElement);
    }

    /// <summary>Removes an object from this template's definition name scope.</summary>
    public void UnregisterName(string name)
    {
        VerifyAccess();
        _nameScope.UnregisterName(name);
    }

    object? Jalium.UI.Markup.INameScope.FindName(string name)
    {
        VerifyAccess();
        return _nameScope.FindName(name);
    }

    bool IQueryAmbient.IsAmbientPropertyAvailable(string propertyName)
    {
        if (string.Equals(propertyName, nameof(Resources), StringComparison.Ordinal))
        {
            return _resources is not null;
        }

        if (string.Equals(propertyName, nameof(Template), StringComparison.Ordinal))
        {
            return _template is not null;
        }

        return true;
    }

    /// <summary>Seals this template and its derived mutable collections.</summary>
    public void Seal()
    {
        VerifyAccess();
        if (_isSealed)
        {
            return;
        }

        OnSeal();
        _isSealed = true;
    }

    /// <summary>Lets derived templates seal their own state before the template is locked.</summary>
    protected virtual void OnSeal()
    {
    }

    /// <summary>Throws when template state is changed after sealing.</summary>
    internal void CheckSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("Cannot modify a sealed FrameworkTemplate.");
        }
    }

    /// <summary>Returns whether the visual-tree representation should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeVisualTree()
    {
        VerifyAccess();
        return _visualTreeFactory is not null || !string.IsNullOrEmpty(_visualTreeXaml);
    }

    /// <summary>Returns whether template resources should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeResources(XamlDesignerSerializationManager? manager)
    {
        VerifyAccess();
        return manager is null || manager.XmlWriter is null;
    }

    /// <summary>Validates the element to which this template is being applied.</summary>
    protected virtual void ValidateTemplatedParent(FrameworkElement templatedParent)
    {
    }

    /// <summary>
    /// Validates a templated-parent type without introducing a Core-to-Controls assembly
    /// dependency. Derived controls are accepted by walking the CLR base-type chain.
    /// </summary>
    protected static void ValidateTemplatedParentType(
        FrameworkElement templatedParent,
        string expectedTypeFullName,
        string expectedTypeDisplayName)
    {
        ArgumentNullException.ThrowIfNull(templatedParent);

        for (Type? type = templatedParent.GetType(); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, expectedTypeFullName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new ArgumentException(
            $"A template for {expectedTypeDisplayName} cannot be applied to '{templatedParent.GetType().Name}'.",
            nameof(templatedParent));
    }
}
