using Jalium.UI.Controls;

namespace Jalium.UI.Automation.Peers;

/// <summary>Exposes an app bar command through the standard button invoke pattern.</summary>
public class AppBarButtonAutomationPeer(AppBarButton owner) : ButtonAutomationPeer(owner)
{
    protected override string GetClassNameCore() => nameof(AppBarButton);

    protected override string GetNameCore()
        => string.IsNullOrWhiteSpace(owner.Label) ? base.GetNameCore() : owner.Label;
}

/// <summary>Exposes a named app bar command through the standard toggle pattern.</summary>
public class AppBarToggleButtonAutomationPeer(AppBarToggleButton owner) : ToggleButtonAutomationPeer(owner)
{
    protected override string GetClassNameCore() => nameof(AppBarToggleButton);

    protected override string GetNameCore()
        => string.IsNullOrWhiteSpace(owner.Label) ? base.GetNameCore() : owner.Label;
}
