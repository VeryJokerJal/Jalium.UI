namespace Jalium.UI.Interactivity;

/// <summary>
/// Represents an attachable object that encapsulates a unit of functionality.
/// </summary>
public abstract class BehaviorTriggerAction : DependencyObject, IAttachedObject
{
    private DependencyObject? _associatedObject;
    private bool _isHosted;

    /// <summary>
    /// Gets the object to which this action is attached.
    /// </summary>
    public DependencyObject? AssociatedObject => _associatedObject;

    /// <summary>
    /// Gets or sets a value indicating whether this action will run when invoked.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets the type constraint for the associated object.
    /// </summary>
    protected virtual Type AssociatedObjectTypeConstraint => typeof(DependencyObject);

    /// <summary>
    /// Gets a value indicating whether this action is hosted.
    /// </summary>
    internal bool IsHosted
    {
        get => _isHosted;
        set => _isHosted = value;
    }

    /// <summary>
    /// Attaches to the specified object.
    /// </summary>
    /// <param name="dependencyObject">The DependencyObject to attach to.</param>
    public void Attach(DependencyObject dependencyObject)
    {
        if (dependencyObject == _associatedObject)
            return;

        if (_associatedObject != null)
            throw new InvalidOperationException("Cannot host action multiple times.");

        if (dependencyObject != null && !AssociatedObjectTypeConstraint.IsAssignableFrom(dependencyObject.GetType()))
            throw new InvalidOperationException($"Object is not of type {AssociatedObjectTypeConstraint.Name}.");

        _associatedObject = dependencyObject;
        OnAttached();
    }

    /// <summary>
    /// Detaches this instance from its associated object.
    /// </summary>
    public void Detach()
    {
        OnDetaching();
        _associatedObject = null;
    }

    /// <summary>
    /// Invokes the action.
    /// </summary>
    /// <param name="parameter">The parameter to the action.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Forwards to abstract Invoke whose overrides may use reflection on user-supplied targets.")]
    internal void CallInvoke(object? parameter)
    {
        if (IsEnabled)
        {
            Invoke(parameter);
        }
    }

    /// <summary>
    /// Invokes the action. Implementations may use reflection on user-supplied targets
    /// (see <see cref="CallMethodAction"/> / <see cref="InvokeCommandAction"/>); marked
    /// accordingly so callers / overrides can declare AOT contracts.
    /// </summary>
    /// <param name="parameter">The parameter to the action. If the Action does not require a parameter, the parameter may be set to a null reference.</param>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Some actions (e.g. CallMethodAction, InvokeCommandAction with a property path) reflectively invoke members on user-supplied targets.")]
    protected abstract void Invoke(object? parameter);

    /// <summary>
    /// Called after the action is attached to an AssociatedObject.
    /// </summary>
    protected virtual void OnAttached()
    {
    }

    /// <summary>
    /// Called when the action is being detached from its AssociatedObject, but before it has actually occurred.
    /// </summary>
    protected virtual void OnDetaching()
    {
    }
}

/// <summary>
/// Represents an attachable object that encapsulates a unit of functionality, with a type constraint on the associated object.
/// </summary>
/// <typeparam name="T">The type of object this action can be attached to.</typeparam>
public abstract class BehaviorTriggerAction<T> : BehaviorTriggerAction where T : DependencyObject
{
    /// <summary>
    /// Gets the object to which this action is attached.
    /// </summary>
    public new T? AssociatedObject => (T?)base.AssociatedObject;

    /// <summary>
    /// Gets the type constraint for the associated object.
    /// </summary>
    protected override Type AssociatedObjectTypeConstraint => typeof(T);
}
