namespace Jalium.UI.Interactivity;

/// <summary>
/// Represents an object that can invoke Actions conditionally.
/// </summary>
public abstract class TriggerBase : DependencyObject, IAttachedObject
{
    private DependencyObject? _associatedObject;
    private readonly TriggerActionCollection _actions;

    /// <summary>
    /// Initializes a new instance of the TriggerBase class.
    /// </summary>
    protected TriggerBase()
    {
        _actions = new TriggerActionCollection();
    }

    /// <summary>
    /// Gets the object to which this trigger is attached.
    /// </summary>
    public DependencyObject? AssociatedObject => _associatedObject;

    /// <summary>
    /// Gets the collection of actions associated with this trigger.
    /// </summary>
    public TriggerActionCollection Actions => _actions;

    /// <summary>
    /// Gets the type constraint for the associated object.
    /// </summary>
    protected virtual Type AssociatedObjectTypeConstraint => typeof(DependencyObject);

    /// <summary>
    /// Attaches to the specified object.
    /// </summary>
    /// <param name="dependencyObject">The DependencyObject to attach to.</param>
    public void Attach(DependencyObject dependencyObject)
    {
        if (dependencyObject == _associatedObject)
            return;

        if (_associatedObject != null)
            throw new InvalidOperationException("Cannot host trigger multiple times.");

        if (dependencyObject != null && !AssociatedObjectTypeConstraint.IsAssignableFrom(dependencyObject.GetType()))
            throw new InvalidOperationException($"Object is not of type {AssociatedObjectTypeConstraint.Name}.");

        _associatedObject = dependencyObject;
        _actions.Attach(dependencyObject!);
        OnAttached();
    }

    /// <summary>
    /// Detaches this instance from its associated object.
    /// </summary>
    public void Detach()
    {
        OnDetaching();
        _actions.Detach();
        _associatedObject = null;
    }

    /// <summary>
    /// Invokes all actions associated with this trigger.
    /// </summary>
    /// <param name="parameter">The parameter to pass to the actions.</param>
    protected void InvokeActions(object? parameter)
    {
        foreach (var action in _actions)
        {
            action.CallInvoke(parameter);
        }
    }

    /// <summary>
    /// Called after the trigger is attached to an AssociatedObject.
    /// </summary>
    protected virtual void OnAttached()
    {
    }

    /// <summary>
    /// Called when the trigger is being detached from its AssociatedObject, but before it has actually occurred.
    /// </summary>
    protected virtual void OnDetaching()
    {
    }
}

/// <summary>
/// Represents an object that can invoke Actions conditionally, with a type constraint on the associated object.
/// </summary>
/// <typeparam name="T">The type of object this trigger can be attached to.</typeparam>
public abstract class TriggerBase<T> : TriggerBase where T : DependencyObject
{
    /// <summary>
    /// Gets the object to which this trigger is attached.
    /// </summary>
    public new T? AssociatedObject => (T?)base.AssociatedObject;

    /// <summary>
    /// Gets the type constraint for the associated object.
    /// </summary>
    protected override Type AssociatedObjectTypeConstraint => typeof(T);
}
