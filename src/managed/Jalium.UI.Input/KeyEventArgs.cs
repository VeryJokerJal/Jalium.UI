// KeyEventArgs and KeyEventHandler moved to Jalium.UI.Core/Input/KeyEventArgs.cs
// TextCompositionEventArgs and TextCompositionEventHandler remain here.

using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Provides data for text input events.
/// </summary>
public sealed class TextCompositionEventArgs : InputEventArgs
{
    /// <summary>
    /// Gets the text that was input.
    /// </summary>
    public string Text { get; }

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
    /// Initializes a new instance of the <see cref="TextCompositionEventArgs"/> class.
    /// </summary>
    public TextCompositionEventArgs(RoutedEvent routedEvent, string text, int timestamp)
        : base(routedEvent, timestamp)
    {
        Text = text;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextCompositionEventArgs"/> class
    /// with a <see cref="Input.TextComposition"/> object.
    /// </summary>
    public TextCompositionEventArgs(RoutedEvent routedEvent, TextComposition composition, int timestamp)
        : base(routedEvent, timestamp)
    {
        TextComposition = composition;
        Text = !string.IsNullOrEmpty(composition.Text) ? composition.Text
            : !string.IsNullOrEmpty(composition.SystemText) ? composition.SystemText
            : composition.ControlText;
    }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
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
