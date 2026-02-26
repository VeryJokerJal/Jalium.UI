using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that a user can select and clear.
/// </summary>
public class CheckBox : ToggleButton
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CheckBox"/> class.
    /// </summary>
    public CheckBox()
    {
        // CheckBox uses ControlTemplate for visual appearance (inherited from ButtonBase)
    }

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return new CheckBoxAutomationPeer(this);
    }
}
