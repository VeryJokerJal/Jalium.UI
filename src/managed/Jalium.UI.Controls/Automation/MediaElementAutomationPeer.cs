using Jalium.UI.Automation;
using Jalium.UI.Controls;

namespace Jalium.UI.Automation.Peers;

/// <summary>
/// Exposes a <see cref="MediaElement"/> to UI Automation.
/// </summary>
public class MediaElementAutomationPeer : FrameworkElementAutomationPeer
{
    /// <summary>
    /// Initializes a peer for the supplied media element.
    /// </summary>
    public MediaElementAutomationPeer(MediaElement owner)
        : base(owner)
    {
    }

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.Custom;

    /// <inheritdoc />
    protected override string GetClassNameCore() => nameof(MediaElement);
}
