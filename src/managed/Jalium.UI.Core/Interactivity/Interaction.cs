namespace Jalium.UI.Interactivity;

/// <summary>
/// Provides static methods that help attach behaviors and triggers to elements.
/// </summary>
public static class Interaction
{
    /// <summary>
    /// Identifies the Behaviors attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty BehaviorsProperty =
        DependencyProperty.RegisterAttached(
            "ShadowBehaviors",
            typeof(BehaviorCollection),
            typeof(Interaction),
            new PropertyMetadata(null, OnBehaviorsChanged));

    /// <summary>
    /// Identifies the Triggers attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TriggersProperty =
        DependencyProperty.RegisterAttached(
            "ShadowTriggers",
            typeof(TriggerCollection),
            typeof(Interaction),
            new PropertyMetadata(null, OnTriggersChanged));

    /// <summary>
    /// Gets the collection of behaviors associated with the specified object.
    /// </summary>
    /// <param name="obj">The object from which to retrieve the behaviors.</param>
    /// <returns>A BehaviorCollection containing the behaviors associated with the specified object.</returns>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static BehaviorCollection GetBehaviors(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var behaviors = (BehaviorCollection?)obj.GetValue(BehaviorsProperty);
        if (behaviors == null)
        {
            behaviors = new BehaviorCollection();
            obj.SetValue(BehaviorsProperty, behaviors);
        }

        return behaviors;
    }

    /// <summary>
    /// Gets the collection of triggers associated with the specified object.
    /// </summary>
    /// <param name="obj">The object from which to retrieve the triggers.</param>
    /// <returns>A TriggerCollection containing the triggers associated with the specified object.</returns>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static TriggerCollection GetTriggers(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var triggers = (TriggerCollection?)obj.GetValue(TriggersProperty);
        if (triggers == null)
        {
            triggers = new TriggerCollection();
            obj.SetValue(TriggersProperty, triggers);
        }

        return triggers;
    }

    private static void OnBehaviorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BehaviorCollection oldBehaviors)
        {
            oldBehaviors.Detach();
        }

        if (e.NewValue is BehaviorCollection newBehaviors)
        {
            newBehaviors.Attach(d);
        }
    }

    private static void OnTriggersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TriggerCollection oldTriggers)
        {
            oldTriggers.Detach();
        }

        if (e.NewValue is TriggerCollection newTriggers)
        {
            newTriggers.Attach(d);
        }
    }
}
