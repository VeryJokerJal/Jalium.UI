namespace Jalium.UI.Input;

/// <summary>
/// Manages all the input systems in the application.
/// </summary>
public sealed class InputManager
{
    private static readonly Lazy<InputManager> _current = new(() => new InputManager());

    private InputManager() { }

    /// <summary>
    /// Gets the InputManager associated with the current thread.
    /// </summary>
    public static InputManager Current => _current.Value;

    /// <summary>
    /// Occurs after input is processed.
    /// </summary>
    public event EventHandler<NotifyInputEventArgs>? PostNotifyInput;

    /// <summary>
    /// Occurs before input is processed.
    /// </summary>
    public event EventHandler<NotifyInputEventArgs>? PreNotifyInput;

    /// <summary>
    /// Occurs before input is processed, allowing modification.
    /// </summary>
    public event EventHandler<PreProcessInputEventArgs>? PreProcessInput;

    /// <summary>
    /// Occurs after input is processed.
    /// </summary>
    public event EventHandler<ProcessInputEventArgs>? PostProcessInput;

    /// <summary>
    /// Processes the specified input synchronously.
    /// Returns false if the input was canceled during pre-processing.
    /// </summary>
    public bool ProcessInput(InputEventArgs input)
    {
        MostRecentInputTimestamp = Environment.TickCount;

        // Stage 1: Notify listeners that input is about to be processed
        PreNotifyInput?.Invoke(this, new NotifyInputEventArgs(input));

        // Stage 2: Pre-process — subscribers can inspect and cancel the input
        var preProcessArgs = new PreProcessInputEventArgs(input);
        PreProcessInput?.Invoke(this, preProcessArgs);

        if (preProcessArgs.Canceled)
        {
            return false;
        }

        // Stage 3: Post-process — subscribers can react to the processed input
        PostProcessInput?.Invoke(this, new ProcessInputEventArgs(input));

        // Stage 4: Final notification after all processing is complete
        PostNotifyInput?.Invoke(this, new NotifyInputEventArgs(input));

        return true;
    }

    /// <summary>
    /// Gets the most recent time stamp of the input events.
    /// </summary>
    public int MostRecentInputTimestamp { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the InputManager is currently in a menu mode.
    /// </summary>
    public bool IsInMenuMode { get; set; }

    /// <summary>
    /// Occurs when entering menu mode.
    /// </summary>
    public event EventHandler? EnterMenuMode;

    /// <summary>
    /// Occurs when leaving menu mode.
    /// </summary>
    public event EventHandler? LeaveMenuMode;

    /// <summary>
    /// Pushes an entry onto the menu mode stack.
    /// </summary>
    public void PushMenuMode(object menuSite)
    {
        IsInMenuMode = true;
        EnterMenuMode?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Pops an entry from the menu mode stack.
    /// </summary>
    public void PopMenuMode(object menuSite)
    {
        IsInMenuMode = false;
        LeaveMenuMode?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Provides data for the NotifyInput event.
/// </summary>
public class NotifyInputEventArgs : EventArgs
{
    public NotifyInputEventArgs(InputEventArgs inputEventArgs)
    {
        StagingItem = new StagingAreaInputItem(inputEventArgs);
    }

    /// <summary>Gets the staging area input item associated with the input event.</summary>
    public StagingAreaInputItem StagingItem { get; }
}

/// <summary>
/// Provides data for the PreProcessInput event.
/// </summary>
public sealed class PreProcessInputEventArgs : ProcessInputEventArgs
{
    public PreProcessInputEventArgs(InputEventArgs inputEventArgs) : base(inputEventArgs) { }

    /// <summary>
    /// Cancels the processing of the input event.
    /// </summary>
    public bool Canceled { get; private set; }

    /// <summary>
    /// Cancels the processing of the input event.
    /// </summary>
    public void Cancel()
    {
        Canceled = true;
    }
}

/// <summary>
/// Provides data for the ProcessInput event.
/// </summary>
public class ProcessInputEventArgs : NotifyInputEventArgs
{
    public ProcessInputEventArgs(InputEventArgs inputEventArgs) : base(inputEventArgs) { }
}

/// <summary>
/// Represents a staging area input item.
/// </summary>
public sealed class StagingAreaInputItem
{
    private Dictionary<object, object>? _data;

    public StagingAreaInputItem(InputEventArgs input)
    {
        Input = input;
    }

    /// <summary>Gets the input event args associated with this staging item.</summary>
    public InputEventArgs Input { get; }

    /// <summary>
    /// Gets data that was set on this staging item.
    /// </summary>
    public object? GetData(object key)
    {
        if (_data != null && _data.TryGetValue(key, out var value))
            return value;
        return null;
    }

    /// <summary>
    /// Sets data on this staging item.
    /// </summary>
    public void SetData(object key, object value)
    {
        _data ??= new Dictionary<object, object>();
        _data[key] = value;
    }
}

/// <summary>
/// Provides data for manipulation events.
/// </summary>
public sealed class ManipulationStartingEventArgs : InputEventArgs
{
    /// <summary>Gets or sets the manipulation mode.</summary>
    public ManipulationModes Mode { get; set; } = ManipulationModes.All;

    /// <summary>Gets or sets the manipulation container.</summary>
    public UIElement? ManipulationContainer { get; set; }

    /// <summary>Gets or sets the pivot.</summary>
    public ManipulationPivot? Pivot { get; set; }

    /// <summary>Gets or sets a value indicating whether the manipulation should be canceled.</summary>
    public bool Cancel { get; set; }

    /// <summary>Gets or sets a value indicating whether manipulation is single-touch only.</summary>
    public bool IsSingleTouchEnabled { get; set; } = true;

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ManipulationStartingEventHandler typedHandler)
        {
            typedHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Provides data for manipulation boundary feedback events.
/// </summary>
public sealed class ManipulationBoundaryFeedbackEventArgs : InputEventArgs
{
    /// <summary>Gets the boundary feedback.</summary>
    public ManipulationDelta? BoundaryFeedback { get; init; }

    /// <summary>Gets the manipulation container.</summary>
    public UIElement? ManipulationContainer { get; init; }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ManipulationBoundaryFeedbackEventHandler typedHandler)
        {
            typedHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Represents a manipulation delta (translation, scale, rotation).
/// </summary>
public sealed class ManipulationDelta
{
    /// <summary>Gets the translation component.</summary>
    public Vector Translation { get; init; }

    /// <summary>Gets the rotation component in degrees.</summary>
    public double Rotation { get; init; }

    /// <summary>Gets the scale component.</summary>
    public Vector Scale { get; init; } = new Vector(1.0, 1.0);

    /// <summary>Gets the expansion component.</summary>
    public Vector Expansion { get; init; }
}

/// <summary>
/// Specifies the types of manipulations that are enabled.
/// </summary>
[Flags]
public enum ManipulationModes
{
    /// <summary>No manipulation is enabled.</summary>
    None = 0,

    /// <summary>Translation along the X axis.</summary>
    TranslateX = 1,

    /// <summary>Translation along the Y axis.</summary>
    TranslateY = 2,

    /// <summary>Translation along both axes.</summary>
    Translate = TranslateX | TranslateY,

    /// <summary>Rotation.</summary>
    Rotate = 4,

    /// <summary>Scaling.</summary>
    Scale = 8,

    /// <summary>All manipulations.</summary>
    All = Translate | Rotate | Scale
}

/// <summary>
/// Represents the pivot for a manipulation.
/// </summary>
public sealed class ManipulationPivot
{
    /// <summary>
    /// Initializes a new instance of the ManipulationPivot class.
    /// </summary>
    public ManipulationPivot() { }

    /// <summary>
    /// Initializes a new instance with the specified center and radius.
    /// </summary>
    public ManipulationPivot(Point center, double radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>Gets or sets the center of the pivot.</summary>
    public Point Center { get; set; }

    /// <summary>Gets or sets the radius of the pivot.</summary>
    public double Radius { get; set; }
}

/// <summary>
/// Specifies the focus restore mode.
/// </summary>
public enum RestoreFocusMode
{
    /// <summary>Focus is automatically restored.</summary>
    Auto,

    /// <summary>Focus is not restored.</summary>
    None
}
