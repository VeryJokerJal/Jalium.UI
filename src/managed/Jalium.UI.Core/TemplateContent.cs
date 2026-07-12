namespace Jalium.UI;

/// <summary>
/// Represents the deferred object graph owned by a framework template.
/// </summary>
public class TemplateContent
{
    private readonly Func<DependencyObject?>? _loader;
    private FrameworkTemplate? _ownerTemplate;

    internal TemplateContent()
    {
    }

    /// <summary>
    /// Creates deferred template content backed by a factory. The constructor is internal,
    /// matching WPF's loader-owned TemplateContent lifecycle.
    /// </summary>
    internal TemplateContent(Func<DependencyObject?> loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    internal void AttachTo(FrameworkTemplate ownerTemplate)
    {
        ArgumentNullException.ThrowIfNull(ownerTemplate);
        if (_ownerTemplate is not null && !ReferenceEquals(_ownerTemplate, ownerTemplate))
        {
            throw new InvalidOperationException("Template content already belongs to another FrameworkTemplate.");
        }

        _ownerTemplate = ownerTemplate;
    }

    internal DependencyObject? LoadContent() => _loader?.Invoke();
}
