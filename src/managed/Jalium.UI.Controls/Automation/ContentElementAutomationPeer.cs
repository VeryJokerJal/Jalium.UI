using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Automation.Peers;

/// <summary>Exposes a nonvisual <see cref="ContentElement"/> to accessibility clients.</summary>
public class ContentElementAutomationPeer : AutomationPeer
{
    public ContentElementAutomationPeer(ContentElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
    }

    public new ContentElement Owner { get; }

    public static AutomationPeer? CreatePeerForElement(ContentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetAutomationPeer();
    }

    public static AutomationPeer? FromElement(ContentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetExistingAutomationPeer();
    }

    public override object? GetPattern(PatternInterface patternInterface) => null;

    protected override DependencyObject GetAutomationOwnerCore() => Owner;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override string GetClassNameCore() => Owner.GetType().Name;
    protected override string GetNameCore() => string.Empty;
    protected override string GetAutomationIdCore() => string.Empty;
    protected override string GetHelpTextCore() => string.Empty;
    protected override string GetAcceleratorKeyCore() => string.Empty;
    protected override string GetAccessKeyCore() => string.Empty;
    protected override string GetItemStatusCore() => string.Empty;
    protected override string GetItemTypeCore() => string.Empty;
    protected override string GetLocalizedControlTypeCore() => GetAutomationControlTypeCore().ToString();

    protected override Rect GetBoundingRectangleCore()
    {
        UIElement? host = FindVisualHost();
        return host is null
            ? Rect.Empty
            : host.MapLocalRectToScreen(new Rect(0, 0, host.RenderSize.Width, host.RenderSize.Height));
    }

    protected override Point GetClickablePointCore()
    {
        Rect bounds = GetBoundingRectangleCore();
        return bounds.IsEmpty || IsOffscreenCore()
            ? new Point(double.NaN, double.NaN)
            : new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
    protected override bool IsEnabledCore() => Owner.IsEnabled;
    protected override bool IsKeyboardFocusableCore() => Owner.Focusable && Owner.IsEnabled;
    protected override bool HasKeyboardFocusCore() => Owner.IsKeyboardFocused;
    protected override bool IsOffscreenCore() => FindVisualHost()?.Visibility != Visibility.Visible;
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsPasswordCore() => false;
    protected override bool IsRequiredForFormCore() => AutomationProperties.GetIsRequiredForForm(Owner);
    protected override bool IsDialogCore() => AutomationProperties.GetIsDialog(Owner);
    protected override int GetPositionInSetCore() => AutomationProperties.GetPositionInSet(Owner);
    protected override int GetSizeOfSetCore() => AutomationProperties.GetSizeOfSet(Owner);
    protected override AutomationHeadingLevel GetHeadingLevelCore() => AutomationProperties.GetHeadingLevel(Owner);
    protected override AutomationLiveSetting GetLiveSettingCore() => AutomationProperties.GetLiveSetting(Owner);

    protected override AutomationPeer? GetLabeledByCore()
    {
        UIElement? labeledBy = AutomationProperties.GetLabeledBy(Owner);
        return labeledBy is null ? null : UIElementAutomationPeer.CreatePeerForElement(labeledBy);
    }

    protected override AutomationPeer? GetParentCore()
    {
        DependencyObject? current = Owner.GetUIParentCore();
        while (current is not null)
        {
            switch (current)
            {
                case ContentElement content when content.GetAutomationPeer() is AutomationPeer contentPeer:
                    return contentPeer;
                case ContentElement content:
                    current = content.GetUIParentCore();
                    continue;
                case UIElement element when element.GetAutomationPeer() is AutomationPeer elementPeer:
                    return elementPeer;
                case UIElement element:
                    current = element.VisualParent;
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    protected override List<AutomationPeer> GetChildrenCore() => [];
    protected override void SetFocusCore() => Owner.Focus();

    private UIElement? FindVisualHost()
    {
        DependencyObject? current = Owner.GetUIParentCore();
        while (current is ContentElement content)
        {
            current = content.GetUIParentCore();
        }

        return current as UIElement;
    }
}
