namespace Jalium.UI.Automation;

/// <summary>
/// Provides a base class that exposes an automation element to UI Automation.
/// </summary>
public abstract class AutomationPeer
{
    private readonly UIElement _owner;
    private AutomationPeer? _eventsSource;

    /// <summary>
    /// Initializes a new instance of the AutomationPeer class.
    /// </summary>
    /// <param name="owner">The UI element that is associated with this peer.</param>
    protected AutomationPeer(UIElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>
    /// Gets the UI element that is associated with this peer.
    /// </summary>
    protected UIElement Owner => _owner;

    /// <summary>
    /// Gets or sets the AutomationPeer that is the source of automation events for this peer.
    /// </summary>
    public AutomationPeer? EventsSource
    {
        get => _eventsSource;
        set => _eventsSource = value;
    }

    #region Core Methods

    /// <summary>
    /// Gets the control type for the element that is associated with this peer.
    /// </summary>
    /// <returns>The control type.</returns>
    public AutomationControlType GetAutomationControlType()
    {
        return GetAutomationControlTypeCore();
    }

    /// <summary>
    /// Gets the class name of the element that is associated with this peer.
    /// </summary>
    /// <returns>The class name.</returns>
    public string GetClassName()
    {
        return GetClassNameCore();
    }

    /// <summary>
    /// Gets the name of the element that is associated with this peer.
    /// </summary>
    /// <returns>The name of the element.</returns>
    public string GetName()
    {
        // First check AutomationProperties.Name
        var name = AutomationProperties.GetName(_owner);
        if (!string.IsNullOrEmpty(name))
            return name;

        // Then call the core implementation
        return GetNameCore();
    }

    /// <summary>
    /// Gets the automation ID for the element that is associated with this peer.
    /// </summary>
    /// <returns>The automation ID.</returns>
    public string GetAutomationId()
    {
        var id = AutomationProperties.GetAutomationId(_owner);
        if (!string.IsNullOrEmpty(id))
            return id;

        return GetAutomationIdCore();
    }

    /// <summary>
    /// Gets help text for the element that is associated with this peer.
    /// </summary>
    /// <returns>The help text.</returns>
    public string GetHelpText()
    {
        var helpText = AutomationProperties.GetHelpText(_owner);
        if (!string.IsNullOrEmpty(helpText))
            return helpText;

        return GetHelpTextCore();
    }

    /// <summary>
    /// Gets a human-readable localized string that represents the control type.
    /// </summary>
    /// <returns>The localized control type name.</returns>
    public string GetLocalizedControlType()
    {
        return GetLocalizedControlTypeCore();
    }

    /// <summary>
    /// Gets the bounding rectangle of the element.
    /// </summary>
    /// <returns>The bounding rectangle.</returns>
    public Rect GetBoundingRectangle()
    {
        return GetBoundingRectangleCore();
    }

    /// <summary>
    /// Gets a value indicating whether the element is enabled.
    /// </summary>
    /// <returns>True if enabled; otherwise, false.</returns>
    public bool IsEnabled()
    {
        return IsEnabledCore();
    }

    /// <summary>
    /// Gets a value indicating whether the element is a keyboard focusable element.
    /// </summary>
    /// <returns>True if focusable; otherwise, false.</returns>
    public bool IsKeyboardFocusable()
    {
        return IsKeyboardFocusableCore();
    }

    /// <summary>
    /// Gets a value indicating whether the element has keyboard focus.
    /// </summary>
    /// <returns>True if the element has keyboard focus; otherwise, false.</returns>
    public bool HasKeyboardFocus()
    {
        return HasKeyboardFocusCore();
    }

    /// <summary>
    /// Sets keyboard focus to the element.
    /// </summary>
    public void SetFocus()
    {
        SetFocusCore();
    }

    /// <summary>
    /// Gets a value indicating whether the element is visible.
    /// </summary>
    /// <returns>True if visible; otherwise, false.</returns>
    public bool IsOffscreen()
    {
        return IsOffscreenCore();
    }

    /// <summary>
    /// Gets a value indicating whether the element is a content element.
    /// </summary>
    /// <returns>True if content element; otherwise, false.</returns>
    public bool IsContentElement()
    {
        return IsContentElementCore();
    }

    /// <summary>
    /// Gets a value indicating whether the element is a control element.
    /// </summary>
    /// <returns>True if control element; otherwise, false.</returns>
    public bool IsControlElement()
    {
        return IsControlElementCore();
    }

    #endregion

    #region Pattern Support

    /// <summary>
    /// Gets the control pattern that is associated with the specified pattern interface.
    /// </summary>
    /// <param name="patternInterface">The pattern interface.</param>
    /// <returns>The pattern provider, or null if the pattern is not supported.</returns>
    public object? GetPattern(PatternInterface patternInterface)
    {
        return GetPatternCore(patternInterface);
    }

    #endregion

    #region Navigation

    /// <summary>
    /// Gets the parent peer.
    /// </summary>
    /// <returns>The parent automation peer, or null.</returns>
    public AutomationPeer? GetParent()
    {
        return GetParentCore();
    }

    /// <summary>
    /// Gets the child peers.
    /// </summary>
    /// <returns>A list of child automation peers.</returns>
    public List<AutomationPeer> GetChildren()
    {
        return GetChildrenCore();
    }

    #endregion

    #region Events

    /// <summary>
    /// Raises an automation event.
    /// </summary>
    /// <param name="eventId">The event to raise.</param>
    public void RaiseAutomationEvent(AutomationEvents eventId)
    {
        // In a full implementation, this would notify the UI Automation system
        // For now, we just track that it was raised
        OnAutomationEvent(eventId);
    }

    /// <summary>
    /// Raises a property changed event.
    /// </summary>
    /// <param name="property">The property that changed.</param>
    /// <param name="oldValue">The old value.</param>
    /// <param name="newValue">The new value.</param>
    public void RaisePropertyChangedEvent(AutomationProperty property, object? oldValue, object? newValue)
    {
        // In a full implementation, this would notify the UI Automation system
        OnPropertyChanged(property, oldValue, newValue);
    }

    /// <summary>
    /// Called when an automation event is raised.
    /// </summary>
    /// <param name="eventId">The event ID.</param>
    protected virtual void OnAutomationEvent(AutomationEvents eventId)
    {
    }

    /// <summary>
    /// Called when a property changes.
    /// </summary>
    /// <param name="property">The property that changed.</param>
    /// <param name="oldValue">The old value.</param>
    /// <param name="newValue">The new value.</param>
    protected virtual void OnPropertyChanged(AutomationProperty property, object? oldValue, object? newValue)
    {
    }

    #endregion

    #region Core Implementation Methods

    /// <summary>
    /// When overridden in a derived class, returns the control type for the element.
    /// </summary>
    protected abstract AutomationControlType GetAutomationControlTypeCore();

    /// <summary>
    /// When overridden in a derived class, returns the class name.
    /// </summary>
    protected abstract string GetClassNameCore();

    /// <summary>
    /// When overridden in a derived class, returns the name of the element.
    /// </summary>
    protected virtual string GetNameCore() => string.Empty;

    /// <summary>
    /// When overridden in a derived class, returns the automation ID.
    /// </summary>
    protected virtual string GetAutomationIdCore() => Owner.GetHashCode().ToString();

    /// <summary>
    /// When overridden in a derived class, returns the help text.
    /// </summary>
    protected virtual string GetHelpTextCore() => string.Empty;

    /// <summary>
    /// When overridden in a derived class, returns the localized control type.
    /// </summary>
    protected virtual string GetLocalizedControlTypeCore()
    {
        return GetAutomationControlTypeCore().ToString();
    }

    /// <summary>
    /// When overridden in a derived class, returns the bounding rectangle.
    /// </summary>
    protected virtual Rect GetBoundingRectangleCore()
    {
        // Try to get the element's position in screen coordinates
        var renderSize = Owner.RenderSize;
        return new Rect(0, 0, renderSize.Width, renderSize.Height);
    }

    /// <summary>
    /// When overridden in a derived class, returns whether the element is enabled.
    /// </summary>
    protected virtual bool IsEnabledCore()
    {
        return Owner.IsEnabled;
    }

    /// <summary>
    /// When overridden in a derived class, returns whether the element is keyboard focusable.
    /// </summary>
    protected virtual bool IsKeyboardFocusableCore()
    {
        return Owner.Focusable && Owner.IsEnabled && Owner.Visibility == Visibility.Visible;
    }

    /// <summary>
    /// When overridden in a derived class, returns whether the element has keyboard focus.
    /// </summary>
    protected virtual bool HasKeyboardFocusCore()
    {
        return Owner.IsKeyboardFocused;
    }

    /// <summary>
    /// When overridden in a derived class, sets focus to the element.
    /// </summary>
    protected virtual void SetFocusCore()
    {
        Owner.Focus();
    }

    /// <summary>
    /// When overridden in a derived class, returns whether the element is off screen.
    /// </summary>
    protected virtual bool IsOffscreenCore()
    {
        return Owner.Visibility != Visibility.Visible;
    }

    /// <summary>
    /// When overridden in a derived class, returns whether the element is a content element.
    /// </summary>
    protected virtual bool IsContentElementCore() => true;

    /// <summary>
    /// When overridden in a derived class, returns whether the element is a control element.
    /// </summary>
    protected virtual bool IsControlElementCore() => true;

    /// <summary>
    /// When overridden in a derived class, returns the pattern provider for the specified interface.
    /// </summary>
    protected virtual object? GetPatternCore(PatternInterface patternInterface) => null;

    /// <summary>
    /// When overridden in a derived class, returns the parent peer.
    /// </summary>
    protected virtual AutomationPeer? GetParentCore()
    {
        var parent = Owner.VisualParent as UIElement;
        return parent?.GetAutomationPeer();
    }

    /// <summary>
    /// When overridden in a derived class, returns the child peers.
    /// </summary>
    protected virtual List<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();

        var childCount = Owner.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            if (Owner.GetVisualChild(i) is UIElement child)
            {
                var peer = child.GetAutomationPeer();
                if (peer != null)
                {
                    children.Add(peer);
                }
            }
        }

        return children;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Invalidates the peer, causing it to be rebuilt.
    /// </summary>
    public void InvalidatePeer()
    {
        // In a full implementation, this would notify the UI Automation system
        // to rebuild the automation tree
    }

    /// <summary>
    /// Returns a peer for the specified element if one exists; otherwise creates one.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The automation peer, or null.</returns>
    public static AutomationPeer? FromElement(UIElement? element)
    {
        return element?.GetAutomationPeer();
    }

    #endregion
}

/// <summary>
/// Represents an automation property identifier.
/// </summary>
public sealed class AutomationProperty
{
    private readonly string _name;
    private readonly int _id;

    private AutomationProperty(string name, int id)
    {
        _name = name;
        _id = id;
    }

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the property ID.
    /// </summary>
    public int Id => _id;

    private static int _nextId = 1;
    private static readonly Dictionary<string, AutomationProperty> _properties = new();

    /// <summary>
    /// Registers an automation property.
    /// </summary>
    public static AutomationProperty Register(string name)
    {
        if (_properties.TryGetValue(name, out var existing))
            return existing;

        var property = new AutomationProperty(name, _nextId++);
        _properties[name] = property;
        return property;
    }

    /// <summary>
    /// The Name property.
    /// </summary>
    public static AutomationProperty NameProperty { get; } = Register("Name");

    /// <summary>
    /// The AutomationId property.
    /// </summary>
    public static AutomationProperty AutomationIdProperty { get; } = Register("AutomationId");

    /// <summary>
    /// The IsEnabled property.
    /// </summary>
    public static AutomationProperty IsEnabledProperty { get; } = Register("IsEnabled");

    /// <summary>
    /// The HasKeyboardFocus property.
    /// </summary>
    public static AutomationProperty HasKeyboardFocusProperty { get; } = Register("HasKeyboardFocus");

    /// <summary>
    /// The BoundingRectangle property.
    /// </summary>
    public static AutomationProperty BoundingRectangleProperty { get; } = Register("BoundingRectangle");

    /// <summary>
    /// The IsOffscreen property.
    /// </summary>
    public static AutomationProperty IsOffscreenProperty { get; } = Register("IsOffscreen");

    /// <summary>
    /// The ToggleState property.
    /// </summary>
    public static AutomationProperty ToggleStateProperty { get; } = Register("ToggleState");

    /// <summary>
    /// The Value property.
    /// </summary>
    public static AutomationProperty ValueProperty { get; } = Register("Value");

    /// <summary>
    /// The RangeValue property.
    /// </summary>
    public static AutomationProperty RangeValueProperty { get; } = Register("RangeValue");

    /// <summary>
    /// The ExpandCollapseState property.
    /// </summary>
    public static AutomationProperty ExpandCollapseStateProperty { get; } = Register("ExpandCollapseState");
}

/// <summary>
/// Provides a base class for framework element automation peers.
/// </summary>
public class FrameworkElementAutomationPeer : AutomationPeer
{
    /// <summary>
    /// Initializes a new instance of the FrameworkElementAutomationPeer class.
    /// </summary>
    /// <param name="owner">The framework element.</param>
    public FrameworkElementAutomationPeer(FrameworkElement owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the framework element owner.
    /// </summary>
    protected new FrameworkElement Owner => (FrameworkElement)base.Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Custom;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return Owner.GetType().Name;
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        // For FrameworkElement, we might look at Name property
        var name = Owner.Name;
        if (!string.IsNullOrEmpty(name))
            return name;

        return base.GetNameCore();
    }

    /// <inheritdoc />
    protected override string GetAutomationIdCore()
    {
        // Use the element's Name property if set
        var name = Owner.Name;
        if (!string.IsNullOrEmpty(name))
            return name;

        return base.GetAutomationIdCore();
    }
}

/// <summary>
/// Exposes UIElement types to UI Automation.
/// </summary>
public sealed class UIElementAutomationPeer : AutomationPeer
{
    public UIElementAutomationPeer(UIElement owner) : base(owner) { }

    /// <summary>
    /// Creates a peer for the specified UIElement.
    /// </summary>
    public static AutomationPeer? CreatePeerForElement(UIElement element)
    {
        return element.GetAutomationPeer();
    }

    /// <summary>
    /// Gets the existing peer for the specified element, or creates a new one.
    /// </summary>
    public new static AutomationPeer? FromElement(UIElement element)
    {
        return CreatePeerForElement(element);
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override string GetClassNameCore() => Owner.GetType().Name;
}

/// <summary>
/// Exposes ContentElement types to UI Automation.
/// </summary>
public sealed class ContentElementAutomationPeer : AutomationPeer
{
    private readonly DependencyObject _contentElement;

    public ContentElementAutomationPeer(UIElement hostElement, DependencyObject contentElement) : base(hostElement)
    {
        _contentElement = contentElement;
    }

    /// <summary>Gets the content element associated with this peer.</summary>
    public DependencyObject ContentElement => _contentElement;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override string GetClassNameCore() => _contentElement.GetType().Name;
}

/// <summary>
/// Represents an automation peer used as the root for hit testing.
/// </summary>
public sealed class GenericRootAutomationPeer : AutomationPeer
{
    public GenericRootAutomationPeer(UIElement owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Window;
    protected override string GetClassNameCore() => "Pane";
    protected override string GetNameCore() => "Desktop";
}
