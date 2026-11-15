namespace Jalium.UI.Controls;
/// <summary>Represents a themed separator between menu flyout commands.</summary>
[TemplatePart(Name = "PART_Line", Type = typeof(Border))]
public class MenuFlyoutSeparator : Control
{
    static MenuFlyoutSeparator()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuFlyoutSeparator), new PropertyMetadata(typeof(MenuFlyoutSeparator)));
        FocusableProperty.OverrideMetadata(typeof(MenuFlyoutSeparator), new PropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(MenuFlyoutSeparator), new PropertyMetadata(false));
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Separator);
}
