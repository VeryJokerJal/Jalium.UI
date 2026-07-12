using Jalium.UI.Automation.Provider;

namespace Jalium.UI.Automation.Peers;

/// <summary>Exposes a framework element to accessibility clients.</summary>
public abstract class AutomationPeer : DispatcherObject
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AutomationPeer, AutomationPeerRawProvider>
        s_rawProviders = new();
    private List<AutomationPeer>? _childrenCache;

    /// <summary>Initializes an automation peer.</summary>
    protected AutomationPeer()
    {
    }

    /// <summary>Gets or sets the peer that is used as the source for events raised by this peer.</summary>
    public AutomationPeer? EventsSource { get; set; }

    /// <summary>Gets the dependency object represented by this peer for framework accessibility bridges.</summary>
    internal DependencyObject? Owner => GetAutomationOwnerCore();

    /// <summary>Gets the provider for a control pattern.</summary>
    public abstract object? GetPattern(PatternInterface patternInterface);

    public AutomationControlType GetAutomationControlType() => GetAutomationControlTypeCore();
    public string GetClassName() => GetClassNameCore() ?? string.Empty;

    public string GetName()
    {
        DependencyObject? owner = GetAutomationOwnerCore();
        if (owner is not null)
        {
            string value = AutomationProperties.GetName(owner);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return GetNameCore() ?? string.Empty;
    }

    public string GetAutomationId()
    {
        DependencyObject? owner = GetAutomationOwnerCore();
        if (owner is not null)
        {
            string value = AutomationProperties.GetAutomationId(owner);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return GetAutomationIdCore() ?? string.Empty;
    }

    public string GetHelpText()
    {
        DependencyObject? owner = GetAutomationOwnerCore();
        if (owner is not null)
        {
            string value = AutomationProperties.GetHelpText(owner);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return GetHelpTextCore() ?? string.Empty;
    }

    public string GetAcceleratorKey()
    {
        DependencyObject? owner = GetAutomationOwnerCore();
        if (owner is not null)
        {
            string value = AutomationProperties.GetAcceleratorKey(owner);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return GetAcceleratorKeyCore() ?? string.Empty;
    }

    public string GetAccessKey()
    {
        DependencyObject? owner = GetAutomationOwnerCore();
        if (owner is not null)
        {
            string value = AutomationProperties.GetAccessKey(owner);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return GetAccessKeyCore() ?? string.Empty;
    }

    public string GetItemStatus()
    {
        DependencyObject? owner = GetAutomationOwnerCore();
        if (owner is not null)
        {
            string value = AutomationProperties.GetItemStatus(owner);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return GetItemStatusCore() ?? string.Empty;
    }

    public string GetItemType()
    {
        DependencyObject? owner = GetAutomationOwnerCore();
        if (owner is not null)
        {
            string value = AutomationProperties.GetItemType(owner);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return GetItemTypeCore() ?? string.Empty;
    }

    public string GetLocalizedControlType() => GetLocalizedControlTypeCore() ?? string.Empty;
    public Rect GetBoundingRectangle() => GetBoundingRectangleCore();
    public Point GetClickablePoint() => GetClickablePointCore();
    public AutomationOrientation GetOrientation() => GetOrientationCore();
    public bool IsEnabled() => IsEnabledCore();
    public bool IsKeyboardFocusable() => IsKeyboardFocusableCore();
    public bool HasKeyboardFocus() => HasKeyboardFocusCore();
    public bool IsOffscreen() => IsOffscreenCore();
    public bool IsContentElement() => IsContentElementCore();
    public bool IsControlElement() => IsControlElementCore();
    public bool IsPassword() => IsPasswordCore();
    public bool IsRequiredForForm() => IsRequiredForFormCore();
    public bool IsDialog() => IsDialogCore();
    public int GetPositionInSet() => GetPositionInSetCore();
    public int GetSizeOfSet() => GetSizeOfSetCore();
    public AutomationHeadingLevel GetHeadingLevel() => GetHeadingLevelCore();
    public AutomationLiveSetting GetLiveSetting() => GetLiveSettingCore();
    public AutomationPeer? GetLabeledBy() => GetLabeledByCore();
    public List<AutomationPeer> GetControlledPeers() => GetControlledPeersCore() ?? [];
    public AutomationPeer? GetParent() => GetParentCore();

    public List<AutomationPeer> GetChildren()
    {
        if (_childrenCache is null)
        {
            _childrenCache = GetChildrenCore() ?? [];
        }

        return _childrenCache;
    }

    public AutomationPeer? GetPeerFromPoint(Point point) => GetPeerFromPointCore(point);

    public void SetFocus() => SetFocusCore();

    public void RaiseAutomationEvent(AutomationEvents eventId)
    {
        OnAutomationEvent(eventId);
        AutomationPeer effectiveSource = EventsSource ?? this;
        Automation.IAutomationEventSink? sink = Automation.EventSinkRegistry.Sink;
        sink?.OnAutomationEventRaised(effectiveSource, eventId);
    }

    public void RaisePropertyChangedEvent(AutomationProperty property, object? oldValue, object? newValue)
    {
        ArgumentNullException.ThrowIfNull(property);
        OnPropertyChanged(property, oldValue, newValue);
        AutomationPeer effectiveSource = EventsSource ?? this;
        Automation.EventSinkRegistry.Sink?.OnPropertyChangedRaised(effectiveSource, property, oldValue, newValue);
    }

    public void RaiseAsyncContentLoadedEvent(AsyncContentLoadedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        OnAutomationEvent(AutomationEvents.AsyncContentLoaded);
        AutomationPeer effectiveSource = EventsSource ?? this;
        Automation.EventSinkRegistry.Sink?.OnAsyncContentLoadedRaised(effectiveSource, args);
    }

    public void RaiseNotificationEvent(
        AutomationNotificationKind notificationKind,
        AutomationNotificationProcessing notificationProcessing,
        string displayString,
        string activityId)
    {
        ArgumentNullException.ThrowIfNull(displayString);
        ArgumentNullException.ThrowIfNull(activityId);
        OnAutomationEvent(AutomationEvents.Notification);
        AutomationPeer effectiveSource = EventsSource ?? this;
        Automation.EventSinkRegistry.Sink?.OnNotificationRaised(
            effectiveSource,
            notificationKind,
            notificationProcessing,
            displayString,
            activityId);
    }

    public static bool ListenerExists(AutomationEvents eventId) => Automation.EventSinkRegistry.Sink is not null;

    /// <summary>Compatibility overload retained for existing Jalium call sites.</summary>
    public static bool ListenerExists() => Automation.EventSinkRegistry.Sink is not null;

    internal static Automation.IAutomationEventSink? EventSink
    {
        get => Automation.EventSinkRegistry.Sink;
        set => Automation.EventSinkRegistry.Sink = value;
    }

    public void InvalidatePeer()
    {
        ResetChildrenCache();
        Automation.EventSinkRegistry.Sink?.OnFocusChanged(EventsSource ?? this);
    }

    public void ResetChildrenCache() => _childrenCache = null;

    protected internal virtual bool IsHwndHost => false;

    protected internal IRawElementProviderSimple ProviderFromPeer(AutomationPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return s_rawProviders.GetValue(peer, static value => new AutomationPeerRawProvider(value));
    }

    protected AutomationPeer PeerFromProvider(IRawElementProviderSimple provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider is IAutomationPeerRawProvider peerProvider ? peerProvider.Peer : null!;
    }

    protected virtual DependencyObject? GetAutomationOwnerCore() => null;
    protected virtual HostedWindowWrapper? GetHostRawElementProviderCore() => null;
    protected virtual AutomationPeer? GetPeerFromPointCore(Point point) => HitTestPeer(this, point);
    protected virtual bool IsDialogCore() => false;
    protected virtual int GetPositionInSetCore() => -1;
    protected virtual int GetSizeOfSetCore() => -1;
    protected virtual AutomationHeadingLevel GetHeadingLevelCore() => AutomationHeadingLevel.None;
    protected virtual AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Off;
    protected virtual List<AutomationPeer> GetControlledPeersCore() => [];

    protected abstract AutomationControlType GetAutomationControlTypeCore();
    protected abstract string GetClassNameCore();
    protected abstract string GetNameCore();
    protected abstract string GetAutomationIdCore();
    protected abstract string GetHelpTextCore();
    protected abstract string GetAcceleratorKeyCore();
    protected abstract string GetAccessKeyCore();
    protected abstract string GetItemStatusCore();
    protected abstract string GetItemTypeCore();
    protected abstract string GetLocalizedControlTypeCore();
    protected abstract Rect GetBoundingRectangleCore();
    protected abstract Point GetClickablePointCore();
    protected abstract AutomationOrientation GetOrientationCore();
    protected abstract bool IsEnabledCore();
    protected abstract bool IsKeyboardFocusableCore();
    protected abstract bool HasKeyboardFocusCore();
    protected abstract bool IsOffscreenCore();
    protected abstract bool IsContentElementCore();
    protected abstract bool IsControlElementCore();
    protected abstract bool IsPasswordCore();
    protected abstract bool IsRequiredForFormCore();
    protected abstract AutomationPeer? GetLabeledByCore();
    protected abstract AutomationPeer? GetParentCore();
    protected abstract List<AutomationPeer> GetChildrenCore();
    protected abstract void SetFocusCore();

    protected virtual void OnAutomationEvent(AutomationEvents eventId)
    {
    }

    protected virtual void OnPropertyChanged(AutomationProperty property, object? oldValue, object? newValue)
    {
    }

    private static AutomationPeer? HitTestPeer(AutomationPeer peer, Point point)
    {
        List<AutomationPeer> children = peer.GetChildren();
        for (int index = children.Count - 1; index >= 0; index--)
        {
            AutomationPeer? hit = HitTestPeer(children[index], point);
            if (hit is not null)
            {
                return hit;
            }
        }

        Rect bounds = peer.GetBoundingRectangle();
        return !bounds.IsEmpty && bounds.Contains(point) ? peer : null;
    }
}

/// <summary>Exposes a <see cref="UIElement"/> to accessibility clients.</summary>
public class UIElementAutomationPeer : AutomationPeer
{
    public UIElementAutomationPeer(UIElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
    }

    public new UIElement Owner { get; }

    public static AutomationPeer? CreatePeerForElement(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetAutomationPeer();
    }

    public static AutomationPeer? FromElement(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetExistingAutomationPeer();
    }

    public override object? GetPattern(PatternInterface patternInterface) => GetPatternCore(patternInterface);

    protected virtual object? GetPatternCore(PatternInterface patternInterface) => null;
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
    protected override Rect GetBoundingRectangleCore() => Owner.MapLocalRectToScreen(new Rect(0, 0, Owner.RenderSize.Width, Owner.RenderSize.Height));

    protected override Point GetClickablePointCore()
    {
        Rect bounds = GetBoundingRectangleCore();
        return bounds.IsEmpty || IsOffscreenCore()
            ? new Point(double.NaN, double.NaN)
            : new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
    protected override bool IsEnabledCore() => Owner.IsEnabled;
    protected override bool IsKeyboardFocusableCore() => Owner.Focusable && Owner.IsEnabled && Owner.Visibility == Visibility.Visible;
    protected override bool HasKeyboardFocusCore() => Owner.IsKeyboardFocused;
    protected override bool IsOffscreenCore() => Owner.Visibility != Visibility.Visible;
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
        return labeledBy is null ? null : CreatePeerForElement(labeledBy);
    }

    protected override AutomationPeer? GetParentCore()
    {
        Visual? current = Owner.VisualParent;
        while (current is not null)
        {
            if (current is UIElement element && element.GetAutomationPeer() is AutomationPeer peer)
            {
                return peer;
            }

            current = current.VisualParent;
        }

        return null;
    }

    protected override List<AutomationPeer> GetChildrenCore()
    {
        List<AutomationPeer> peers = [];
        CollectChildPeers(Owner, peers);
        return peers;
    }

    protected override void SetFocusCore() => Owner.Focus();

    private static void CollectChildPeers(UIElement parent, List<AutomationPeer> result)
    {
        for (int index = 0; index < parent.VisualChildrenCount; index++)
        {
            if (parent.GetVisualChild(index) is not UIElement child)
            {
                continue;
            }

            AutomationPeer? childPeer = child.GetAutomationPeer();
            if (childPeer is not null)
            {
                result.Add(childPeer);
            }
            else
            {
                CollectChildPeers(child, result);
            }
        }
    }
}

/// <summary>Exposes a <see cref="FrameworkElement"/> to accessibility clients.</summary>
public class FrameworkElementAutomationPeer : UIElementAutomationPeer
{
    public FrameworkElementAutomationPeer(FrameworkElement owner)
        : base(owner)
    {
    }

    protected override string GetNameCore()
    {
        FrameworkElement owner = (FrameworkElement)Owner;
        return owner.Name ?? string.Empty;
    }

    protected override string GetAutomationIdCore()
    {
        FrameworkElement owner = (FrameworkElement)Owner;
        return owner.Name ?? string.Empty;
    }
}

/// <summary>A root peer used for accessibility hit testing.</summary>
public partial class GenericRootAutomationPeer : UIElementAutomationPeer
{
    public GenericRootAutomationPeer(UIElement owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Window;
    protected override string GetClassNameCore() => "Pane";
    protected override string GetNameCore() => "Desktop";
    protected override Rect GetBoundingRectangleCore() => base.GetBoundingRectangleCore();
}

/// <summary>Identifies the orientation of an automation element.</summary>
public enum AutomationOrientation
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
}

/// <summary>Wraps a native hosted window handle for an automation peer.</summary>
public sealed class HostedWindowWrapper
{
    public HostedWindowWrapper(nint handle)
    {
        Handle = handle;
    }

    public nint Handle { get; }
}
