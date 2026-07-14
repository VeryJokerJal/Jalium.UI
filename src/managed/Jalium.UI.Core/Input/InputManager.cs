using Jalium.UI.Threading;

namespace Jalium.UI.Input;

/// <summary>
/// Manages the input systems associated with the application.
/// </summary>
public sealed class InputManager : DispatcherObject
{
    private static readonly Lazy<InputManager> _current = new(() => new InputManager());
    private readonly System.Collections.ArrayList _inputProviders = [];
    private readonly Stack<StagingAreaInputItem> _stagingArea = [];
    private readonly Stack<PresentationSource> _menuSites = [];
    private KeyboardDevice? _primaryKeyboardDevice;
    private MouseDevice? _primaryMouseDevice;
    private InputDevice? _mostRecentInputDevice;

    private InputManager() { }

    /// <summary>
    /// Gets the InputManager associated with the current thread.
    /// </summary>
    public static InputManager Current => _current.Value;

    #region InputBlocked Attached Property

    /// <summary>
    /// Marks a UIElement subtree as "input-transparent" — the
    /// <see cref="Jalium.UI.Controls.Input.WindowInputDispatcher"/> redirects any mouse / pointer / touch
    /// input that hit-tests into this subtree back up to the blocked element itself, so descendant
    /// controls (Buttons, TextBoxes, Sliders, …) never receive routed input events.
    ///
    /// Designed for designer / preview hosts that need to render real controls without their
    /// runtime behaviors (clicks, drags, hover states). Set <c>InputManager.SetInputBlocked(host, true)</c>
    /// on the host element; descendants stay visually and structurally intact but become input-inert.
    ///
    /// Implementation notes:
    ///   - <see cref="FindInputBlockedAncestor"/> walks the visual parent chain looking for any
    ///     element with this property set to true.
    ///   - Dispatcher consults this helper after hit-testing and rewrites the input target to the
    ///     blocked element itself; routed events fire on the host, not on the descendant.
    ///   - <see cref="IsHitTestVisible"/> remains true on descendants so the host can still
    ///     hit-test them geometrically for its own purposes (e.g. designer-selection lookup).
    /// </summary>
    public static readonly DependencyProperty InputBlockedProperty =
        DependencyProperty.RegisterAttached(
            "InputBlocked",
            typeof(bool),
            typeof(InputManager),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets the value of the <see cref="InputBlockedProperty"/> attached property.
    /// </summary>
    public static bool GetInputBlocked(UIElement element)
    {
        if (element == null) return false;
        return (bool)(element.GetValue(InputBlockedProperty) ?? false);
    }

    /// <summary>
    /// Sets the value of the <see cref="InputBlockedProperty"/> attached property.
    /// </summary>
    public static void SetInputBlocked(UIElement element, bool value)
    {
        if (element == null) return;
        element.SetValue(InputBlockedProperty, value);
    }

    /// <summary>
    /// Walks the visual parent chain (starting from <paramref name="element"/> inclusive) and
    /// returns the first element with <see cref="InputBlockedProperty"/> set to true, or
    /// <see langword="null"/> if no ancestor is input-blocked.
    /// </summary>
    public static UIElement? FindInputBlockedAncestor(UIElement? element)
    {
        var cur = element;
        while (cur != null)
        {
            if (GetInputBlocked(cur)) return cur;
            cur = cur.VisualParent as UIElement;
        }
        return null;
    }

    #endregion

    /// <summary>
    /// Occurs after input is processed.
    /// </summary>
    public event NotifyInputEventHandler? PostNotifyInput;

    /// <summary>
    /// Occurs before input is processed.
    /// </summary>
    public event NotifyInputEventHandler? PreNotifyInput;

    /// <summary>
    /// Occurs before input is processed, allowing modification.
    /// </summary>
    public event PreProcessInputEventHandler? PreProcessInput;

    /// <summary>
    /// Occurs after input is processed.
    /// </summary>
    public event ProcessInputEventHandler? PostProcessInput;

    /// <summary>Occurs when input-related hit testing must be refreshed.</summary>
    public event EventHandler? HitTestInvalidatedAsync;

    /// <summary>Gets the registered platform input providers.</summary>
    public System.Collections.ICollection InputProviders => _inputProviders;

    /// <summary>Gets the device that produced the most recently staged input.</summary>
    public InputDevice MostRecentInputDevice => _mostRecentInputDevice ?? PrimaryKeyboardDevice;

    /// <summary>Gets the primary keyboard device.</summary>
    public KeyboardDevice PrimaryKeyboardDevice =>
        _primaryKeyboardDevice ??= new NullKeyboardDevice(this);

    /// <summary>Gets the primary mouse device.</summary>
    public MouseDevice PrimaryMouseDevice =>
        _primaryMouseDevice ??= new NullMouseDevice(this);

    /// <summary>
    /// Processes the specified input synchronously.
    /// Returns false if the input was canceled during pre-processing.
    /// </summary>
    public bool ProcessInput(InputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        MostRecentInputTimestamp = input.Timestamp;
        if (input.Device is not null)
            _mostRecentInputDevice = input.Device;

        var stagingItem = PushInput(input, promote: null);

        // Stage 1: Notify listeners that input is about to be processed
        PreNotifyInput?.Invoke(this, new NotifyInputEventArgs(this, stagingItem));

        // Stage 2: Pre-process — subscribers can inspect and cancel the input
        var preProcessArgs = new PreProcessInputEventArgs(this, stagingItem);
        PreProcessInput?.Invoke(this, preProcessArgs);

        if (preProcessArgs.Canceled)
        {
            RemoveStagingItem(stagingItem);
            return false;
        }

        // Stage 3: Post-process — subscribers can react to the processed input
        PostProcessInput?.Invoke(this, new ProcessInputEventArgs(this, stagingItem));

        // Stage 4: Final notification after all processing is complete
        PostNotifyInput?.Invoke(this, new NotifyInputEventArgs(this, stagingItem));
        RemoveStagingItem(stagingItem);

        return true;
    }

    internal StagingAreaInputItem? PeekInput() =>
        _stagingArea.TryPeek(out var item) ? item : null;

    internal StagingAreaInputItem? PopInput() =>
        _stagingArea.TryPop(out var item) ? item : null;

    internal StagingAreaInputItem PushInput(InputEventArgs input, StagingAreaInputItem? promote)
    {
        ArgumentNullException.ThrowIfNull(input);
        var item = new StagingAreaInputItem(input, promote);
        _stagingArea.Push(item);
        return item;
    }

    internal StagingAreaInputItem PushInput(StagingAreaInputItem input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _stagingArea.Push(input);
        return input;
    }

    private void RemoveStagingItem(StagingAreaInputItem item)
    {
        if (_stagingArea.TryPeek(out var current) && ReferenceEquals(current, item))
        {
            _stagingArea.Pop();
            return;
        }

        if (!_stagingArea.Contains(item))
            return;

        var promoted = new Stack<StagingAreaInputItem>();
        while (_stagingArea.TryPop(out current) && !ReferenceEquals(current, item))
            promoted.Push(current);
        while (promoted.TryPop(out current))
            _stagingArea.Push(current);
    }

    internal void RegisterPrimaryKeyboardDevice(KeyboardDevice device) =>
        _primaryKeyboardDevice = device ?? throw new ArgumentNullException(nameof(device));

    internal void RegisterPrimaryMouseDevice(MouseDevice device) =>
        _primaryMouseDevice = device ?? throw new ArgumentNullException(nameof(device));

    internal void RegisterInputProvider(object provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_inputProviders.Contains(provider))
            _inputProviders.Add(provider);
    }

    internal void UnregisterInputProvider(object provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _inputProviders.Remove(provider);
    }

    internal void InvalidateHitTest() =>
        HitTestInvalidatedAsync?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Gets the most recent time stamp of the input events.
    /// </summary>
    public int MostRecentInputTimestamp { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the InputManager is currently in a menu mode.
    /// </summary>
    public bool IsInMenuMode => _menuSites.Count != 0;

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
    public void PushMenuMode(PresentationSource menuSite)
    {
        ArgumentNullException.ThrowIfNull(menuSite);
        bool wasInMenuMode = IsInMenuMode;
        _menuSites.Push(menuSite);
        if (!wasInMenuMode)
            EnterMenuMode?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Pops an entry from the menu mode stack.
    /// </summary>
    public void PopMenuMode(PresentationSource menuSite)
    {
        ArgumentNullException.ThrowIfNull(menuSite);
        if (_menuSites.Count == 0 || !ReferenceEquals(_menuSites.Peek(), menuSite))
            throw new InvalidOperationException("The presentation source is not the active menu site.");
        _menuSites.Pop();
        if (!IsInMenuMode)
            LeaveMenuMode?.Invoke(this, EventArgs.Empty);
    }

    private sealed class NullKeyboardDevice(InputManager inputManager) : KeyboardDevice(inputManager)
    {
        public override IInputElement? Target => FocusedElement;
        public override PresentationSource? ActiveSource => null;
        protected override KeyStates GetKeyStatesFromSystem(Key key) => KeyStates.None;
    }

    private sealed class NullMouseDevice(InputManager inputManager) : MouseDevice(inputManager)
    {
        public override IInputElement? Target => Captured ?? DirectlyOver;
        public override PresentationSource? ActiveSource => null;
        protected override MouseButtonState GetButtonState(MouseButton mouseButton) => MouseButtonState.Released;
        protected override Point GetPositionCore(IInputElement? relativeTo) => default;
    }
}

/// <summary>Represents a handler for routed input events.</summary>
public delegate void InputEventHandler(object sender, InputEventArgs e);

/// <summary>Represents a handler for input notification events.</summary>
public delegate void NotifyInputEventHandler(object sender, NotifyInputEventArgs e);

/// <summary>Represents a handler that may inspect or cancel staged input.</summary>
public delegate void PreProcessInputEventHandler(object sender, PreProcessInputEventArgs e);

/// <summary>Represents a handler that processes staged input.</summary>
public delegate void ProcessInputEventHandler(object sender, ProcessInputEventArgs e);

/// <summary>
/// Provides data for the NotifyInput event.
/// </summary>
public class NotifyInputEventArgs : EventArgs
{
    public NotifyInputEventArgs(InputEventArgs inputEventArgs)
        : this(InputManager.Current, new StagingAreaInputItem(inputEventArgs))
    {
    }

    internal NotifyInputEventArgs(InputManager inputManager, StagingAreaInputItem stagingItem)
    {
        InputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
        StagingItem = stagingItem ?? throw new ArgumentNullException(nameof(stagingItem));
    }

    /// <summary>Gets the input manager that owns the staging area.</summary>
    public InputManager InputManager { get; }

    /// <summary>Gets the staging area input item associated with the input event.</summary>
    public StagingAreaInputItem StagingItem { get; }
}

/// <summary>
/// Provides data for the PreProcessInput event.
/// </summary>
public sealed class PreProcessInputEventArgs : ProcessInputEventArgs
{
    public PreProcessInputEventArgs(InputEventArgs inputEventArgs) : base(inputEventArgs) { }

    internal PreProcessInputEventArgs(InputManager inputManager, StagingAreaInputItem stagingItem)
        : base(inputManager, stagingItem)
    {
    }

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

    internal ProcessInputEventArgs(InputManager inputManager, StagingAreaInputItem stagingItem)
        : base(inputManager, stagingItem)
    {
    }

    /// <summary>Gets the next staged input item without removing it.</summary>
    public StagingAreaInputItem? PeekInput() => InputManager.PeekInput();

    /// <summary>Removes and returns the next staged input item.</summary>
    public StagingAreaInputItem? PopInput() => InputManager.PopInput();

    /// <summary>Stages an input event, inheriting data from a promoted item.</summary>
    public StagingAreaInputItem PushInput(InputEventArgs input, StagingAreaInputItem? promote) =>
        InputManager.PushInput(input, promote);

    /// <summary>Stages an existing input item.</summary>
    public StagingAreaInputItem PushInput(StagingAreaInputItem input) =>
        InputManager.PushInput(input);
}

/// <summary>
/// Represents a staging area input item.
/// </summary>
public sealed class StagingAreaInputItem
{
    private Dictionary<object, object>? _data;

    public StagingAreaInputItem(InputEventArgs input)
        : this(input, promote: null)
    {
    }

    internal StagingAreaInputItem(InputEventArgs input, StagingAreaInputItem? promote)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        if (promote?._data is not null)
            _data = new Dictionary<object, object>(promote._data);
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
    private bool _cancelRequested;
    private ManipulationModes _mode = ManipulationModes.All;

    internal ManipulationStartingEventArgs()
    {
    }

    /// <summary>Gets or sets the manipulation mode.</summary>
    public ManipulationModes Mode
    {
        get => _mode;
        set
        {
            if ((value & ~ManipulationModes.All) != 0)
                throw new ArgumentException("The manipulation mode contains an unsupported value.", nameof(value));
            _mode = value;
        }
    }

    /// <summary>Gets or sets the manipulation container.</summary>
    public IInputElement? ManipulationContainer { get; set; }

    /// <summary>Gets or sets the pivot.</summary>
    public ManipulationPivot? Pivot { get; set; }

    /// <summary>Cancels the manipulation before it starts.</summary>
    public bool Cancel()
    {
        _cancelRequested = true;
        return true;
    }

    internal bool CancelRequested => _cancelRequested;

    /// <summary>Gets or sets a value indicating whether manipulation is single-touch only.</summary>
    public bool IsSingleTouchEnabled { get; set; } = true;

    /// <summary>Gets the input contacts participating in the manipulation.</summary>
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    private readonly List<Manipulations.ManipulationParameters2D> _parameters = [];

    /// <summary>Adds a parameter object to the active manipulation processor.</summary>
    [System.ComponentModel.Browsable(false)]
    public void SetManipulationParameter(
        Jalium.UI.Input.Manipulations.ManipulationParameters2D parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _parameters.Add(parameter);
        if (Source is UIElement element)
            Manipulation.SetManipulationParameter(element, parameter);
    }

    internal IReadOnlyList<Manipulations.ManipulationParameters2D> Parameters => _parameters;

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<ManipulationStartingEventArgs> eventHandler)
        {
            eventHandler(target, this);
        }
        else if (handler is ManipulationStartingEventHandler typedHandler)
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
    internal ManipulationBoundaryFeedbackEventArgs()
    {
    }

    /// <summary>Gets the boundary feedback.</summary>
    public ManipulationDelta BoundaryFeedback { get; internal init; } = null!;

    /// <summary>Gets the manipulation container.</summary>
    public IInputElement ManipulationContainer { get; internal init; } = null!;

    /// <summary>Gets the input contacts participating in the manipulation.</summary>
    public IEnumerable<IManipulator> Manipulators =>
        Source is UIElement element ? Manipulation.GetManipulators(element) : [];

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<ManipulationBoundaryFeedbackEventArgs> eventHandler)
        {
            eventHandler(target, this);
        }
        else if (handler is ManipulationBoundaryFeedbackEventHandler typedHandler)
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
public class ManipulationDelta
{
    internal ManipulationDelta()
    {
    }

    public ManipulationDelta(Vector translation, double rotation, Vector scale, Vector expansion)
    {
        Translation = translation;
        Rotation = rotation;
        Scale = scale;
        Expansion = expansion;
    }

    /// <summary>Gets the translation component.</summary>
    public Vector Translation { get; internal init; }

    /// <summary>Gets the rotation component in degrees.</summary>
    public double Rotation { get; internal init; }

    /// <summary>Gets the scale component.</summary>
    public Vector Scale { get; internal init; } = new Vector(1.0, 1.0);

    /// <summary>Gets the expansion component.</summary>
    public Vector Expansion { get; internal init; }
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
public class ManipulationPivot
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
