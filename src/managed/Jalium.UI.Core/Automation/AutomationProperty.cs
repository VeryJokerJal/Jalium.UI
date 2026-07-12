using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Automation;

/// <summary>Represents an automation property identifier.</summary>
public sealed class AutomationProperty
{
    private static int s_nextId = 1;
    private static readonly Dictionary<string, AutomationProperty> s_properties = [];

    private AutomationProperty(string name, int id)
    {
        Name = name;
        Id = id;
    }

    public string Name { get; }
    public int Id { get; }

    public static AutomationProperty Register(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (s_properties)
        {
            if (s_properties.TryGetValue(name, out AutomationProperty? existing))
            {
                return existing;
            }

            AutomationProperty property = new(name, s_nextId++);
            s_properties.Add(name, property);
            return property;
        }
    }

    public static AutomationProperty NameProperty { get; } = Register("Name");
    public static AutomationProperty AutomationIdProperty { get; } = Register("AutomationId");
    public static AutomationProperty IsEnabledProperty { get; } = Register("IsEnabled");
    public static AutomationProperty HasKeyboardFocusProperty { get; } = Register("HasKeyboardFocus");
    public static AutomationProperty BoundingRectangleProperty { get; } = Register("BoundingRectangle");
    public static AutomationProperty IsOffscreenProperty { get; } = Register("IsOffscreen");
    public static AutomationProperty ToggleStateProperty { get; } = Register("ToggleState");
    public static AutomationProperty ValueProperty { get; } = Register("Value");
    public static AutomationProperty RangeValueProperty { get; } = Register("RangeValue");
    public static AutomationProperty ExpandCollapseStateProperty { get; } = Register("ExpandCollapseState");
}

/// <summary>Forwards automation events to an operating-system accessibility bridge.</summary>
internal interface IAutomationEventSink
{
    void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId);
    void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue);
    void OnFocusChanged(AutomationPeer peer);

    void OnAsyncContentLoadedRaised(AutomationPeer peer, AsyncContentLoadedEventArgs args) =>
        OnAutomationEventRaised(peer, AutomationEvents.AsyncContentLoaded);

    void OnNotificationRaised(
        AutomationPeer peer,
        AutomationNotificationKind notificationKind,
        AutomationNotificationProcessing notificationProcessing,
        string displayString,
        string activityId) =>
        OnAutomationEventRaised(peer, AutomationEvents.Notification);
}

internal static class EventSinkRegistry
{
    internal static IAutomationEventSink? Sink { get; set; }
}

/// <summary>Describes the progress of asynchronously loaded content.</summary>
public sealed class AsyncContentLoadedEventArgs : EventArgs
{
    public AsyncContentLoadedEventArgs(AsyncContentLoadedState asyncContentState, double percentComplete)
    {
        AsyncContentLoadedState = asyncContentState;
        PercentComplete = percentComplete;
    }

    public AsyncContentLoadedState AsyncContentLoadedState { get; }
    public double PercentComplete { get; }
}

public enum AsyncContentLoadedState
{
    Beginning = 0,
    Progress = 1,
    Completed = 2,
}

public enum AutomationNotificationKind
{
    ItemAdded = 0,
    ItemRemoved = 1,
    ActionCompleted = 2,
    ActionAborted = 3,
    Other = 4,
}

public enum AutomationNotificationProcessing
{
    ImportantAll = 0,
    ImportantMostRecent = 1,
    All = 2,
    MostRecent = 3,
    CurrentThenMostRecent = 4,
}
