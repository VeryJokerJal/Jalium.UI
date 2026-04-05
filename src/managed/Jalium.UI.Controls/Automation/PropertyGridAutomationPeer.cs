using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Exposes <see cref="PropertyGrid"/> to UI Automation.
/// </summary>
public sealed class PropertyGridAutomationPeer : FrameworkElementAutomationPeer
{
    public PropertyGridAutomationPeer(PropertyGrid owner) : base(owner) { }

    private PropertyGrid PropertyGridOwner => (PropertyGrid)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Table;

    protected override string GetClassNameCore() => nameof(PropertyGrid);

    protected override string GetNameCore()
    {
        var selectedObj = PropertyGridOwner.SelectedObject;
        if (selectedObj != null)
            return $"PropertyGrid - {selectedObj.GetType().Name}";
        return base.GetNameCore();
    }
}
