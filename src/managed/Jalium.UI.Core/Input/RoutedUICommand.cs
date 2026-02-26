namespace Jalium.UI.Input;

/// <summary>
/// Defines an ICommand that is routed through the element tree and contains a text property.
/// </summary>
public sealed class RoutedUICommand : RoutedCommand
{
    private string _text;

    /// <summary>
    /// Initializes a new instance of the RoutedUICommand class.
    /// </summary>
    public RoutedUICommand()
    {
        _text = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the RoutedUICommand class with the specified text, name, and owner type.
    /// </summary>
    /// <param name="text">The descriptive text for the command.</param>
    /// <param name="name">The name of the command.</param>
    /// <param name="ownerType">The type that registers the command.</param>
    public RoutedUICommand(string text, string name, Type ownerType)
        : base(name, ownerType)
    {
        _text = text ?? string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the RoutedUICommand class with the specified text, name, owner type, and input gestures.
    /// </summary>
    /// <param name="text">The descriptive text for the command.</param>
    /// <param name="name">The name of the command.</param>
    /// <param name="ownerType">The type that registers the command.</param>
    /// <param name="inputGestures">The input gestures that invoke the command.</param>
    public RoutedUICommand(string text, string name, Type ownerType, InputGestureCollection inputGestures)
        : base(name, ownerType, inputGestures)
    {
        _text = text ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the text that describes this command.
    /// </summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }
}
