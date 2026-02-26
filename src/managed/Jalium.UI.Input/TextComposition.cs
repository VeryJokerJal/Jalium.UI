namespace Jalium.UI.Input;

/// <summary>
/// Represents a text input composition, used for IME and keyboard text input processing.
/// </summary>
public class TextComposition : DispatcherObject
{
    private IInputElement? _source;
    private string _resultText;
    private string _text = string.Empty;
    private string _systemText = string.Empty;
    private string _controlText = string.Empty;
    private string _compositionText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextComposition"/> class.
    /// </summary>
    /// <param name="inputManager">The input manager associated with this composition.</param>
    /// <param name="source">The source input element.</param>
    /// <param name="resultText">The initial result text.</param>
    public TextComposition(InputManager inputManager, IInputElement? source, string resultText)
    {
        _source = source;
        _resultText = resultText ?? string.Empty;
        AutoComplete = TextCompositionAutoComplete.On;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextComposition"/> class with auto-complete mode.
    /// </summary>
    /// <param name="inputManager">The input manager associated with this composition.</param>
    /// <param name="source">The source input element.</param>
    /// <param name="resultText">The initial result text.</param>
    /// <param name="autoComplete">The auto-complete mode.</param>
    public TextComposition(InputManager inputManager, IInputElement? source, string resultText, TextCompositionAutoComplete autoComplete)
    {
        _source = source;
        _resultText = resultText ?? string.Empty;
        AutoComplete = autoComplete;
    }

    /// <summary>
    /// Gets the source element of this composition.
    /// </summary>
    public IInputElement? Source => _source;

    /// <summary>
    /// Gets or sets the composed text.
    /// </summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the system text for this composition.
    /// </summary>
    public string SystemText
    {
        get => _systemText;
        set => _systemText = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the control text for this composition.
    /// </summary>
    public string ControlText
    {
        get => _controlText;
        set => _controlText = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the composition text (IME intermediate text).
    /// </summary>
    public string CompositionText
    {
        get => _compositionText;
        set => _compositionText = value ?? string.Empty;
    }

    /// <summary>
    /// Gets the auto-complete mode for this composition.
    /// </summary>
    public TextCompositionAutoComplete AutoComplete { get; set; }

    /// <summary>
    /// Gets the current composition stage.
    /// </summary>
    public TextCompositionStage Stage { get; internal set; }

    /// <summary>
    /// Completes this composition.
    /// </summary>
    public void Complete()
    {
        TextCompositionManager.CompleteComposition(this);
    }

    /// <summary>
    /// Sets the main text value.
    /// </summary>
    internal void SetText(string text)
    {
        _text = text ?? string.Empty;
    }

    /// <summary>
    /// Clears all text properties.
    /// </summary>
    internal void ClearTexts()
    {
        _text = string.Empty;
        _systemText = string.Empty;
        _controlText = string.Empty;
    }

    /// <summary>
    /// Makes this composition a system text composition.
    /// </summary>
    internal void MakeSystem()
    {
        _systemText = _text;
        _text = string.Empty;
    }

    /// <summary>
    /// Makes this composition a control text composition.
    /// </summary>
    internal void MakeControl()
    {
        _controlText = _text;
        _text = string.Empty;
    }
}

/// <summary>
/// Specifies auto-completion behavior for text composition.
/// </summary>
public enum TextCompositionAutoComplete
{
    /// <summary>
    /// Auto-completion is off; the composition must be explicitly completed.
    /// </summary>
    Off,

    /// <summary>
    /// Auto-completion is on; the composition will be completed automatically.
    /// </summary>
    On
}

/// <summary>
/// Specifies the stage of a text composition.
/// </summary>
public enum TextCompositionStage
{
    /// <summary>
    /// The composition has not started.
    /// </summary>
    None,

    /// <summary>
    /// The composition has started.
    /// </summary>
    Started,

    /// <summary>
    /// The composition is done.
    /// </summary>
    Done
}

/// <summary>
/// Manages text compositions and provides text input routed events.
/// </summary>
public static class TextCompositionManager
{
    #region Routed Events

    /// <summary>
    /// Identifies the PreviewTextInputStart routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTextInputStartEvent =
        EventManager.RegisterRoutedEvent("PreviewTextInputStart", RoutingStrategy.Tunnel,
            typeof(TextCompositionEventHandler), typeof(TextCompositionManager));

    /// <summary>
    /// Identifies the TextInputStart routed event.
    /// </summary>
    public static readonly RoutedEvent TextInputStartEvent =
        EventManager.RegisterRoutedEvent("TextInputStart", RoutingStrategy.Bubble,
            typeof(TextCompositionEventHandler), typeof(TextCompositionManager));

    /// <summary>
    /// Identifies the PreviewTextInputUpdate routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTextInputUpdateEvent =
        EventManager.RegisterRoutedEvent("PreviewTextInputUpdate", RoutingStrategy.Tunnel,
            typeof(TextCompositionEventHandler), typeof(TextCompositionManager));

    /// <summary>
    /// Identifies the TextInputUpdate routed event.
    /// </summary>
    public static readonly RoutedEvent TextInputUpdateEvent =
        EventManager.RegisterRoutedEvent("TextInputUpdate", RoutingStrategy.Bubble,
            typeof(TextCompositionEventHandler), typeof(TextCompositionManager));

    /// <summary>
    /// Identifies the PreviewTextInput routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTextInputEvent =
        EventManager.RegisterRoutedEvent("PreviewTextInput", RoutingStrategy.Tunnel,
            typeof(TextCompositionEventHandler), typeof(TextCompositionManager));

    /// <summary>
    /// Identifies the TextInput routed event.
    /// </summary>
    public static readonly RoutedEvent TextInputEvent =
        EventManager.RegisterRoutedEvent("TextInput", RoutingStrategy.Bubble,
            typeof(TextCompositionEventHandler), typeof(TextCompositionManager));

    #endregion

    #region Attached Event Helpers

    /// <summary>
    /// Adds a handler for the PreviewTextInput attached event.
    /// </summary>
    public static void AddPreviewTextInputHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(PreviewTextInputEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the PreviewTextInput attached event.
    /// </summary>
    public static void RemovePreviewTextInputHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(PreviewTextInputEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the TextInput attached event.
    /// </summary>
    public static void AddTextInputHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(TextInputEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the TextInput attached event.
    /// </summary>
    public static void RemoveTextInputHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(TextInputEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the PreviewTextInputStart attached event.
    /// </summary>
    public static void AddPreviewTextInputStartHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(PreviewTextInputStartEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the PreviewTextInputStart attached event.
    /// </summary>
    public static void RemovePreviewTextInputStartHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(PreviewTextInputStartEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the TextInputStart attached event.
    /// </summary>
    public static void AddTextInputStartHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(TextInputStartEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the TextInputStart attached event.
    /// </summary>
    public static void RemoveTextInputStartHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(TextInputStartEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the PreviewTextInputUpdate attached event.
    /// </summary>
    public static void AddPreviewTextInputUpdateHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(PreviewTextInputUpdateEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the PreviewTextInputUpdate attached event.
    /// </summary>
    public static void RemovePreviewTextInputUpdateHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(PreviewTextInputUpdateEvent, handler);
    }

    /// <summary>
    /// Adds a handler for the TextInputUpdate attached event.
    /// </summary>
    public static void AddTextInputUpdateHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(TextInputUpdateEvent, handler);
    }

    /// <summary>
    /// Removes a handler for the TextInputUpdate attached event.
    /// </summary>
    public static void RemoveTextInputUpdateHandler(DependencyObject element, TextCompositionEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(TextInputUpdateEvent, handler);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts the specified text composition.
    /// </summary>
    /// <param name="composition">The text composition to start.</param>
    public static void StartComposition(TextComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (composition.Stage != TextCompositionStage.None)
            return;

        composition.Stage = TextCompositionStage.Started;

        if (composition.Source is UIElement source)
        {
            var args = new TextCompositionEventArgs(PreviewTextInputStartEvent, composition.Text, Environment.TickCount);
            source.RaiseEvent(args);

            if (!args.Handled)
            {
                var bubbleArgs = new TextCompositionEventArgs(TextInputStartEvent, composition.Text, Environment.TickCount);
                source.RaiseEvent(bubbleArgs);
            }

            // Auto-complete: immediately complete the composition
            if (composition.AutoComplete == TextCompositionAutoComplete.On)
            {
                CompleteComposition(composition);
            }
        }
    }

    /// <summary>
    /// Updates the specified text composition.
    /// </summary>
    /// <param name="composition">The text composition to update.</param>
    public static void UpdateComposition(TextComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (composition.Stage == TextCompositionStage.None || composition.Stage == TextCompositionStage.Done)
            return;

        if (composition.Source is UIElement source)
        {
            var args = new TextCompositionEventArgs(PreviewTextInputUpdateEvent, composition.Text, Environment.TickCount);
            source.RaiseEvent(args);

            if (!args.Handled)
            {
                var bubbleArgs = new TextCompositionEventArgs(TextInputUpdateEvent, composition.Text, Environment.TickCount);
                source.RaiseEvent(bubbleArgs);
            }
        }
    }

    /// <summary>
    /// Completes the specified text composition.
    /// </summary>
    /// <param name="composition">The text composition to complete.</param>
    public static void CompleteComposition(TextComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (composition.Stage == TextCompositionStage.Done)
            return;

        composition.Stage = TextCompositionStage.Done;

        if (composition.Source is UIElement source)
        {
            var args = new TextCompositionEventArgs(PreviewTextInputEvent, composition.Text, Environment.TickCount);
            source.RaiseEvent(args);

            if (!args.Handled)
            {
                var bubbleArgs = new TextCompositionEventArgs(TextInputEvent, composition.Text, Environment.TickCount);
                source.RaiseEvent(bubbleArgs);
            }
        }
    }

    #endregion
}
