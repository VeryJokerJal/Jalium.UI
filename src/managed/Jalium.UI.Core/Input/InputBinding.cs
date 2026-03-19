using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Represents a binding between an InputGesture and a command.
/// </summary>
public class InputBinding
{
    private ICommand? _command;
    private InputGesture? _gesture;
    private object? _commandParameter;
    private IInputElement? _commandTarget;

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
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _gesture = gesture ?? throw new ArgumentNullException(nameof(gesture));
    }

    /// <summary>
    /// Gets or sets the command associated with this binding.
    /// </summary>
    public ICommand? Command
    {
        get => _command;
        set => _command = value;
    }

    /// <summary>
    /// Gets or sets the input gesture associated with this binding.
    /// </summary>
    public virtual InputGesture? Gesture
    {
        get => _gesture;
        set => _gesture = value;
    }

    /// <summary>
    /// Gets or sets the command parameter.
    /// </summary>
    public object? CommandParameter
    {
        get => _commandParameter;
        set => _commandParameter = value;
    }

    /// <summary>
    /// Gets or sets the target element for the command.
    /// </summary>
    public IInputElement? CommandTarget
    {
        get => _commandTarget;
        set => _commandTarget = value;
    }
}

/// <summary>
/// Represents a binding between a KeyGesture and a command.
/// </summary>
public sealed class KeyBinding : InputBinding
{
    /// <summary>
    /// Initializes a new instance of the KeyBinding class.
    /// </summary>
    public KeyBinding()
    {
    }

    /// <summary>
    /// Initializes a new instance of the KeyBinding class with the specified command and gesture.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="gesture">The key gesture that invokes the command.</param>
    public KeyBinding(ICommand command, KeyGesture gesture)
        : base(command, gesture)
    {
    }

    /// <summary>
    /// Initializes a new instance of the KeyBinding class with the specified command, key, and modifiers.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="key">The key that invokes the command (as key code).</param>
    /// <param name="modifiers">The modifier keys that must be pressed (as flags).</param>
    public KeyBinding(ICommand command, int key, int modifiers)
        : base(command, new KeyGesture(key, modifiers))
    {
    }

    /// <summary>
    /// Gets or sets the key associated with this binding (as key code).
    /// </summary>
    public int Key
    {
        get => (Gesture as KeyGesture)?.Key ?? 0;
        set
        {
            if (Gesture is KeyGesture existing)
            {
                Gesture = new KeyGesture(value, existing.Modifiers);
            }
            else
            {
                Gesture = new KeyGesture(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the modifier keys associated with this binding (as flags).
    /// </summary>
    public int Modifiers
    {
        get => (Gesture as KeyGesture)?.Modifiers ?? 0;
        set
        {
            if (Gesture is KeyGesture existing)
            {
                Gesture = new KeyGesture(existing.Key, value);
            }
        }
    }
}

/// <summary>
/// Represents a binding between a MouseGesture and a command.
/// </summary>
public sealed class MouseBinding : InputBinding
{
    /// <summary>
    /// Initializes a new instance of the MouseBinding class.
    /// </summary>
    public MouseBinding()
    {
    }

    /// <summary>
    /// Initializes a new instance of the MouseBinding class with the specified command and gesture.
    /// </summary>
    /// <param name="command">The command to bind.</param>
    /// <param name="gesture">The mouse gesture that invokes the command.</param>
    public MouseBinding(ICommand command, MouseGesture gesture)
        : base(command, gesture)
    {
    }

    /// <summary>
    /// Gets or sets the mouse action associated with this binding.
    /// </summary>
    public MouseAction MouseAction
    {
        get => (Gesture as MouseGesture)?.MouseAction ?? MouseAction.None;
        set
        {
            if (Gesture is MouseGesture existing)
            {
                existing.MouseAction = value;
            }
            else
            {
                Gesture = new MouseGesture(value);
            }
        }
    }
}
