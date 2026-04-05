using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Exposes <see cref="DiffViewer"/> to UI Automation.
/// </summary>
public sealed class DiffViewerAutomationPeer : FrameworkElementAutomationPeer
{
    public DiffViewerAutomationPeer(DiffViewer owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(DiffViewer);
}
