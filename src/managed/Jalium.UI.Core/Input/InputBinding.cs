using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Represents a binding between an InputGesture and a command.
/// </summary>
public partial class InputBinding : Freezable, ICommandSource
{
    private InputGesture? _gesture;

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(InputBinding),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(InputBinding),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CommandTargetProperty =
        DependencyProperty.Register(
            nameof(CommandTarget),
            typeof(IInputElement),
            typeof(InputBinding),
            new PropertyMetadata(null));

    /// <summary>
    /// Initializes a new instance of the InputBinding class.
    /// </summary>
    protected InputBinding()
    {
    }

    /// <summary>
    /// Initializes a new instance of the InputBinding class with the specified command and gesture.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="gesture">The input gesture that invokes the command.</param>
    public InputBinding(ICommand command, InputGesture gesture)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        _gesture = gesture ?? throw new ArgumentNullException(nameof(gesture));
    }

    /// <summary>
    /// Gets or sets the command associated with this binding.
    /// </summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty) as ICommand;
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the input gesture associated with this binding.
    /// </summary>
    public virtual InputGesture? Gesture
    {
        get
        {
            ReadPreamble();
            return _gesture;
        }
        set
        {
            WritePreamble();
            if (ReferenceEquals(_gesture, value))
                return;
            _gesture = value;
            WritePostscript();
        }
    }

    /// <summary>
    /// Gets or sets the command parameter.
    /// </summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the target element for the command.
    /// </summary>
    public IInputElement? CommandTarget
    {
        get => GetValue(CommandTargetProperty) as IInputElement;
        set => SetValue(CommandTargetProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new InputBinding();

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        Gesture = CloneGesture(((InputBinding)sourceFreezable).Gesture);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        Gesture = CloneGesture(((InputBinding)sourceFreezable).Gesture);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        Gesture = CloneGesture(((InputBinding)sourceFreezable).Gesture);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        Gesture = CloneGesture(((InputBinding)sourceFreezable).Gesture);
    }

    private static InputGesture? CloneGesture(InputGesture? gesture) => gesture switch
    {
        KeyGesture keyGesture => new KeyGesture(
            keyGesture.Key,
            keyGesture.Modifiers,
            keyGesture.DisplayString),
        MouseGesture mouseGesture => new MouseGesture(
            mouseGesture.MouseAction,
            mouseGesture.Modifiers),
        _ => gesture,
    };
}

/// <summary>
/// Represents a binding between a KeyGesture and a command.
/// </summary>
public partial class KeyBinding : InputBinding
{
    private bool _synchronizingGesture;

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.Register(
            nameof(Key),
            typeof(Key),
            typeof(KeyBinding),
            new PropertyMetadata(Key.None, OnKeyOrModifiersChanged));

    public static readonly DependencyProperty ModifiersProperty =
        DependencyProperty.Register(
            nameof(Modifiers),
            typeof(ModifierKeys),
            typeof(KeyBinding),
            new PropertyMetadata(ModifierKeys.None, OnKeyOrModifiersChanged));

    /// <summary>
    /// Initializes a new instance of the KeyBinding class.
    /// </summary>
    public KeyBinding()
    {
        Gesture = new KeyGesture(Key.None, ModifierKeys.None);
    }

    /// <summary>
    /// Initializes a new instance of the KeyBinding class with the specified command and gesture.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="gesture">The key gesture that invokes the command.</param>
    public KeyBinding(ICommand command, KeyGesture gesture)
        : base(command, gesture)
    {
        SynchronizePropertiesFromGesture(gesture);
    }

    /// <summary>
    /// Initializes a new instance of the KeyBinding class with the specified command, key, and modifiers.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="key">The key that invokes the command (as key code).</param>
    /// <param name="modifiers">The modifier keys that must be pressed (as flags).</param>
    public KeyBinding(ICommand command, int key, int modifiers)
        : this(command, new KeyGesture(key, modifiers))
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified command, key, and modifiers.
    /// </summary>
    public KeyBinding(ICommand command, Key key, ModifierKeys modifiers)
        : this(command, new KeyGesture(key, modifiers))
    {
    }

    /// <summary>
    /// Gets or sets the key associated with this binding.
    /// </summary>
    public Key Key
    {
        get => (Key)(GetValue(KeyProperty) ?? Key.None);
        set => SetValue(KeyProperty, value);
    }

    /// <summary>
    /// Gets or sets the modifier keys associated with this binding.
    /// </summary>
    public ModifierKeys Modifiers
    {
        get => (ModifierKeys)(GetValue(ModifiersProperty) ?? ModifierKeys.None);
        set => SetValue(ModifiersProperty, value);
    }

    public override InputGesture? Gesture
    {
        get => base.Gesture;
        set
        {
            if (value is not null and not KeyGesture)
                throw new ArgumentException("A KeyBinding requires a KeyGesture.", nameof(value));
            base.Gesture = value;
            if (!_synchronizingGesture && value is KeyGesture keyGesture)
                SynchronizePropertiesFromGesture(keyGesture);
        }
    }

    protected override Freezable CreateInstanceCore() => new KeyBinding();

    private static void OnKeyOrModifiersChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var binding = (KeyBinding)dependencyObject;
        if (binding._synchronizingGesture)
            return;
        binding._synchronizingGesture = true;
        try
        {
            binding.Gesture = new KeyGesture(binding.Key, binding.Modifiers);
        }
        finally
        {
            binding._synchronizingGesture = false;
        }
    }

    private void SynchronizePropertiesFromGesture(KeyGesture gesture)
    {
        _synchronizingGesture = true;
        try
        {
            SetValue(KeyProperty, gesture.Key);
            SetValue(ModifiersProperty, gesture.Modifiers);
        }
        finally
        {
            _synchronizingGesture = false;
        }
    }
}

/// <summary>
/// Represents a binding between a MouseGesture and a command.
/// </summary>
public sealed class MouseBinding : InputBinding
{
    private bool _synchronizingGesture;

    public static readonly DependencyProperty MouseActionProperty =
        DependencyProperty.Register(
            nameof(MouseAction),
            typeof(MouseAction),
            typeof(MouseBinding),
            new PropertyMetadata(MouseAction.None, OnMouseActionChanged));

    /// <summary>
    /// Initializes a new instance of the MouseBinding class.
    /// </summary>
    public MouseBinding()
    {
        Gesture = new MouseGesture(MouseAction.None);
    }

    /// <summary>
    /// Initializes a new instance of the MouseBinding class with the specified command and gesture.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="gesture">The mouse gesture that invokes the command.</param>
    public MouseBinding(ICommand command, MouseGesture gesture)
        : base(command, gesture)
    {
        SynchronizePropertyFromGesture(gesture);
    }

    /// <summary>
    /// Gets or sets the mouse action associated with this binding.
    /// </summary>
    public MouseAction MouseAction
    {
        get => (MouseAction)(GetValue(MouseActionProperty) ?? MouseAction.None);
        set => SetValue(MouseActionProperty, value);
    }

    public override InputGesture? Gesture
    {
        get => base.Gesture;
        set
        {
            if (value is not null and not MouseGesture)
                throw new ArgumentException("A MouseBinding requires a MouseGesture.", nameof(value));
            base.Gesture = value;
            if (!_synchronizingGesture && value is MouseGesture mouseGesture)
                SynchronizePropertyFromGesture(mouseGesture);
        }
    }

    protected override Freezable CreateInstanceCore() => new MouseBinding();

    protected override void CloneCore(Freezable sourceFreezable) =>
        base.CloneCore(sourceFreezable);

    protected override void CloneCurrentValueCore(Freezable sourceFreezable) =>
        base.CloneCurrentValueCore(sourceFreezable);

    protected override void GetAsFrozenCore(Freezable sourceFreezable) =>
        base.GetAsFrozenCore(sourceFreezable);

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) =>
        base.GetCurrentValueAsFrozenCore(sourceFreezable);

    private static void OnMouseActionChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var binding = (MouseBinding)dependencyObject;
        if (binding._synchronizingGesture)
            return;

        binding._synchronizingGesture = true;
        try
        {
            ModifierKeys modifiers = (binding.Gesture as MouseGesture)?.Modifiers ?? ModifierKeys.None;
            binding.Gesture = new MouseGesture(binding.MouseAction, modifiers);
        }
        finally
        {
            binding._synchronizingGesture = false;
        }
    }

    private void SynchronizePropertyFromGesture(MouseGesture gesture)
    {
        _synchronizingGesture = true;
        try
        {
            SetValue(MouseActionProperty, gesture.MouseAction);
        }
        finally
        {
            _synchronizingGesture = false;
        }
    }
}
