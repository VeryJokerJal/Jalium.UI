using System.Reflection;

namespace Jalium.UI.Interactivity;

/// <summary>
/// A trigger that listens for a specified event on its source and fires when that event is fired.
/// </summary>
public sealed class BehaviorEventTrigger : BehaviorTriggerBase<FrameworkElement>
{
    private string _eventName = "Loaded";
    private Delegate? _eventHandler;
    private EventInfo? _eventInfo;

    /// <summary>
    /// Identifies the EventName dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty EventNameProperty =
        DependencyProperty.Register(nameof(EventName), typeof(string), typeof(BehaviorEventTrigger),
            new PropertyMetadata("Loaded", OnEventNameChanged));

    /// <summary>
    /// Identifies the SourceObject dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceObjectProperty =
        DependencyProperty.Register(nameof(SourceObject), typeof(object), typeof(BehaviorEventTrigger),
            new PropertyMetadata(null, OnSourceObjectChanged));

    /// <summary>
    /// Identifies the SourceName dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceNameProperty =
        DependencyProperty.Register(nameof(SourceName), typeof(string), typeof(BehaviorEventTrigger),
            new PropertyMetadata(null, OnSourceNameChanged));

    /// <summary>
    /// Gets or sets the name of the event to listen for.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public string EventName
    {
        get => (string)(GetValue(EventNameProperty) ?? "Loaded");
        set => SetValue(EventNameProperty, value);
    }

    /// <summary>
    /// Gets or sets the source object to listen to.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? SourceObject
    {
        get => GetValue(SourceObjectProperty);
        set => SetValue(SourceObjectProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the element to listen to.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? SourceName
    {
        get => (string?)GetValue(SourceNameProperty);
        set => SetValue(SourceNameProperty, value);
    }

    /// <summary>
    /// Gets the resolved source object.
    /// </summary>
    private object? ResolvedSource
    {
        get
        {
            if (SourceObject != null)
                return SourceObject;

            if (!string.IsNullOrEmpty(SourceName) && AssociatedObject != null)
            {
                return AssociatedObject.FindName(SourceName);
            }

            return AssociatedObject;
        }
    }

    /// <summary>
    /// Called after the trigger is attached to an AssociatedObject.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "BehaviorEventTrigger is a declarative XAML interactivity behavior whose EventName/SourceObject/SourceName are configured through the XAML/binding surface; reflectively binding the named event (RegisterEvent: \"Reflectively binds an event handler to a runtime-resolved event on the source object.\") is the documented purpose of this opt-in feature. Consumers that attach a BehaviorEventTrigger must keep the event they wire up preserved — a documented prerequisite for using BehaviorEventTrigger under trimming/AOT, not a defect of this attach site.")]
    protected override void OnAttached()
    {
        base.OnAttached();
        RegisterEvent();
    }

    /// <summary>
    /// Called when the trigger is being detached from its AssociatedObject.
    /// </summary>
    protected override void OnDetaching()
    {
        UnregisterEvent();
        base.OnDetaching();
    }

    /// <summary>
    /// Called when the event occurs.
    /// </summary>
    /// <param name="eventArgs">The event arguments.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Forwards to InvokeActions which may invoke reflection-based actions.")]
    private void OnEvent(object? eventArgs)
    {
        InvokeActions(eventArgs);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflectively binds an event handler to a runtime-resolved event on the source object.")]
    private void RegisterEvent()
    {
        UnregisterEvent();

        var source = ResolvedSource;
        if (source == null || string.IsNullOrEmpty(_eventName))
            return;

        _eventInfo = source.GetType().GetEvent(_eventName);
        if (_eventInfo == null)
            return;

        var eventHandlerType = _eventInfo.EventHandlerType;
        if (eventHandlerType == null)
            return;

        // Create a delegate that matches the event handler signature
        var invokeMethod = eventHandlerType.GetMethod("Invoke");
        if (invokeMethod == null)
            return;

        var parameters = invokeMethod.GetParameters();

        // Create a handler that calls OnEvent
        if (parameters.Length == 2)
        {
            // Standard (sender, args) pattern
            _eventHandler = CreateEventHandler(eventHandlerType);
        }
        else if (parameters.Length == 0)
        {
            // No parameters
            _eventHandler = CreateParameterlessEventHandler(eventHandlerType);
        }

        if (_eventHandler != null)
        {
            _eventInfo.AddEventHandler(source, _eventHandler);
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Constructs a delegate that ultimately calls reflection-based action invocations.")]
    private Delegate CreateEventHandler(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)] Type eventHandlerType)
    {
        // For standard event handlers like RoutedEventHandler, EventHandler, etc.
        Action<object?, object?> handler = (sender, args) => OnEvent(args);

        var invokeMethod = eventHandlerType.GetMethod("Invoke");
        if (invokeMethod == null)
            return handler;

        // Create a dynamic method that wraps our handler
        return Delegate.CreateDelegate(eventHandlerType, this, GetType().GetMethod(nameof(HandleEvent), BindingFlags.NonPublic | BindingFlags.Instance)!);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Constructs a delegate that ultimately calls reflection-based action invocations.")]
    private Delegate CreateParameterlessEventHandler(Type eventHandlerType)
    {
        Action handler = () => OnEvent(null);
        return Delegate.CreateDelegate(eventHandlerType, handler.Target!, handler.Method);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Forwards to OnEvent which dispatches reflection-based actions.")]
    private void HandleEvent(object? sender, object? args)
    {
        OnEvent(args);
    }

    private void UnregisterEvent()
    {
        if (_eventHandler == null || _eventInfo == null)
            return;

        var source = ResolvedSource;
        if (source != null)
        {
            _eventInfo.RemoveEventHandler(source, _eventHandler);
        }

        _eventHandler = null;
        _eventInfo = null;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "PropertyChangedCallback for the EventName DependencyProperty (signature fixed by DependencyProperty.Register, cannot carry RequiresUnreferencedCode). BehaviorEventTrigger is a declarative XAML interactivity behavior; reflectively binding the named event (RegisterEvent: \"Reflectively binds an event handler to a runtime-resolved event on the source object.\") is the documented purpose of this opt-in feature. Consumers must keep the event they wire up preserved — a documented prerequisite for using BehaviorEventTrigger under trimming/AOT, not a defect of this callback.")]
    private static void OnEventNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BehaviorEventTrigger trigger)
        {
            trigger._eventName = (string)(e.NewValue ?? "Loaded");
            if (trigger.AssociatedObject != null)
            {
                trigger.RegisterEvent();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "PropertyChangedCallback for the SourceObject DependencyProperty (signature fixed by DependencyProperty.Register, cannot carry RequiresUnreferencedCode). BehaviorEventTrigger is a declarative XAML interactivity behavior; reflectively binding the named event (RegisterEvent: \"Reflectively binds an event handler to a runtime-resolved event on the source object.\") is the documented purpose of this opt-in feature. Consumers must keep the event they wire up preserved — a documented prerequisite for using BehaviorEventTrigger under trimming/AOT, not a defect of this callback.")]
    private static void OnSourceObjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BehaviorEventTrigger trigger && trigger.AssociatedObject != null)
        {
            trigger.RegisterEvent();
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "PropertyChangedCallback for the SourceName DependencyProperty (signature fixed by DependencyProperty.Register, cannot carry RequiresUnreferencedCode). BehaviorEventTrigger is a declarative XAML interactivity behavior; reflectively binding the named event (RegisterEvent: \"Reflectively binds an event handler to a runtime-resolved event on the source object.\") is the documented purpose of this opt-in feature. Consumers must keep the event they wire up preserved — a documented prerequisite for using BehaviorEventTrigger under trimming/AOT, not a defect of this callback.")]
    private static void OnSourceNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BehaviorEventTrigger trigger && trigger.AssociatedObject != null)
        {
            trigger.RegisterEvent();
        }
    }
}
