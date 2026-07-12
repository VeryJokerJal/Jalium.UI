// KeyEventArgs and KeyEventHandler moved to Jalium.UI.Core/Input/KeyEventArgs.cs
// TextCompositionEventArgs and TextCompositionEventHandler live with the core routed-input contracts.

using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Provides data for text input events.
/// </summary>
public sealed class TextCompositionEventArgs : InputEventArgs
{
    private readonly string? _text;

    /// <summary>
    /// Gets the text that was input.
    /// </summary>
    public string Text => TextComposition?.Text ?? _text ?? string.Empty;

    /// <summary>
    /// Gets the system text (Alt+key combinations).
    /// </summary>
    public string SystemText => TextComposition?.SystemText ?? string.Empty;

    /// <summary>
    /// Gets the control text (Ctrl+key combinations).
    /// </summary>
    public string ControlText => TextComposition?.ControlText ?? string.Empty;

    /// <summary>
    /// Gets the <see cref="TextComposition"/> object associated with this event, if any.
    /// </summary>
    public TextComposition? TextComposition { get; }

    /// <summary>
    /// Initializes text-input event data with its originating device and live
    /// composition object.
    /// </summary>
    public TextCompositionEventArgs(InputDevice inputDevice, TextComposition composition)
        : base(inputDevice, Environment.TickCount)
    {
        ArgumentNullException.ThrowIfNull(composition);
        TextComposition = composition;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextCompositionEventArgs"/> class.
    /// </summary>
    public TextCompositionEventArgs(RoutedEvent routedEvent, string text, int timestamp)
        : base(routedEvent, timestamp)
    {
        _text = text;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextCompositionEventArgs"/> class
    /// with a <see cref="Input.TextComposition"/> object.
    /// </summary>
    public TextCompositionEventArgs(RoutedEvent routedEvent, TextComposition composition, int timestamp)
        : base(routedEvent, timestamp)
    {
        TextComposition = composition;
    }

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is TextCompositionEventHandler textHandler)
        {
            textHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Delegate for handling text composition events.
/// </summary>
public delegate void TextCompositionEventHandler(object sender, TextCompositionEventArgs e);
