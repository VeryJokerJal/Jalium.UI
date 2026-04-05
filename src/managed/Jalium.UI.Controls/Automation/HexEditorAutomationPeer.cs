using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Exposes <see cref="HexEditor"/> to UI Automation.
/// </summary>
public sealed class HexEditorAutomationPeer : FrameworkElementAutomationPeer
{
    public HexEditorAutomationPeer(HexEditor owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(HexEditor);

    protected override string GetLocalizedControlTypeCore() => "hex editor";
}
