using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Exposes <see cref="JsonTreeViewer"/> types to UI Automation.
/// </summary>
public sealed class JsonTreeViewerAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonTreeViewerAutomationPeer"/> class.
    /// </summary>
    public JsonTreeViewerAutomationPeer(JsonTreeViewer owner) : base(owner) { }

    private JsonTreeViewer JsonTreeViewerOwner => (JsonTreeViewer)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Tree;

    /// <inheritdoc />
    protected override string GetClassNameCore() => nameof(JsonTreeViewer);

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        var root = JsonTreeViewerOwner.RootNode;
        if (root != null)
        {
            return $"JSON Tree ({root.ChildCount} top-level items)";
        }

        return base.GetNameCore();
    }

    /// <inheritdoc />
    protected override string GetLocalizedControlTypeCore()
        => "JSON tree viewer";

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    /// <summary>
    /// Gets the JSON text currently loaded in the viewer.
    /// </summary>
    public string Value => JsonTreeViewerOwner.JsonText ?? string.Empty;

    /// <summary>
    /// Gets whether the JSON text value is read-only (inverse of IsEditable).
    /// </summary>
    public bool IsReadOnly => !JsonTreeViewerOwner.IsEditable;

    /// <summary>
    /// Sets the JSON text of the viewer.
    /// </summary>
    /// <param name="value">The JSON text to load.</param>
    public void SetValue(string value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled JsonTreeViewer.");

        JsonTreeViewerOwner.JsonText = value;
    }
}
