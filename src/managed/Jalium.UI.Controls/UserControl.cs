namespace Jalium.UI.Controls;

/// <summary>
/// Provides a simple way to create a control that can contain other controls.
/// </summary>
public class UserControl : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.UserControlAutomationPeer(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserControl"/> class.
    /// </summary>
    public UserControl()
    {
        // UserControls typically should not be directly focusable
        Focusable = false;

        // Content should stretch by default
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }
}
