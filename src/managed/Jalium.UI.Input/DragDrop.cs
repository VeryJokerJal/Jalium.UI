namespace Jalium.UI.Input;

/// <summary>
/// Specifies the effects of a drag-and-drop operation.
/// </summary>
[Flags]
public enum DragDropEffects
{
    /// <summary>
    /// No drop is allowed.
    /// </summary>
    None = 0,

    /// <summary>
    /// A copy operation.
    /// </summary>
    Copy = 1,

    /// <summary>
    /// A move operation.
    /// </summary>
    Move = 2,

    /// <summary>
    /// A link operation.
    /// </summary>
    Link = 4,

    /// <summary>
    /// A scroll operation.
    /// </summary>
    Scroll = unchecked((int)0x80000000),

    /// <summary>
    /// All operations.
    /// </summary>
    All = Copy | Move | Link | Scroll
}

/// <summary>
/// Specifies the key states during a drag-and-drop operation.
/// </summary>
[Flags]
public enum DragDropKeyStates
{
    /// <summary>
    /// No key is pressed.
    /// </summary>
    None = 0,

    /// <summary>
    /// The left mouse button is pressed.
    /// </summary>
    LeftMouseButton = 1,

    /// <summary>
    /// The right mouse button is pressed.
    /// </summary>
    RightMouseButton = 2,

    /// <summary>
    /// The shift key is pressed.
    /// </summary>
    ShiftKey = 4,

    /// <summary>
    /// The control key is pressed.
    /// </summary>
    ControlKey = 8,

    /// <summary>
    /// The middle mouse button is pressed.
    /// </summary>
    MiddleMouseButton = 16,

    /// <summary>
    /// The alt key is pressed.
    /// </summary>
    AltKey = 32
}

/// <summary>
/// Provides drag-and-drop functionality.
/// </summary>
public static class DragDrop
{
    private static UIElement? _dragSource;
    private static IDataObject? _currentData;
    private static bool _isDragging;

    #region Attached Properties

    /// <summary>
    /// Identifies the AllowDrop attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty AllowDropProperty =
        DependencyProperty.RegisterAttached("AllowDrop", typeof(bool), typeof(DragDrop),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets the AllowDrop value for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetAllowDrop(DependencyObject element)
    {
        return (bool)(element.GetValue(AllowDropProperty) ?? false);
    }

    /// <summary>
    /// Sets the AllowDrop value for the specified element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetAllowDrop(DependencyObject element, bool value)
    {
        element.SetValue(AllowDropProperty, value);
    }

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the DragEnter routed event.
    /// </summary>
    public static readonly RoutedEvent DragEnterEvent =
        EventManager.RegisterRoutedEvent("DragEnter", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the DragOver routed event.
    /// </summary>
    public static readonly RoutedEvent DragOverEvent =
        EventManager.RegisterRoutedEvent("DragOver", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the DragLeave routed event.
    /// </summary>
    public static readonly RoutedEvent DragLeaveEvent =
        EventManager.RegisterRoutedEvent("DragLeave", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the Drop routed event.
    /// </summary>
    public static readonly RoutedEvent DropEvent =
        EventManager.RegisterRoutedEvent("Drop", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the PreviewDragEnter routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDragEnterEvent =
        EventManager.RegisterRoutedEvent("PreviewDragEnter", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the PreviewDragOver routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDragOverEvent =
        EventManager.RegisterRoutedEvent("PreviewDragOver", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the PreviewDragLeave routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDragLeaveEvent =
        EventManager.RegisterRoutedEvent("PreviewDragLeave", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the PreviewDrop routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDropEvent =
        EventManager.RegisterRoutedEvent("PreviewDrop", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the GiveFeedback routed event.
    /// </summary>
    public static readonly RoutedEvent GiveFeedbackEvent =
        EventManager.RegisterRoutedEvent("GiveFeedback", RoutingStrategy.Bubble,
            typeof(GiveFeedbackEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the QueryContinueDrag routed event.
    /// </summary>
    public static readonly RoutedEvent QueryContinueDragEvent =
        EventManager.RegisterRoutedEvent("QueryContinueDrag", RoutingStrategy.Bubble,
            typeof(QueryContinueDragEventHandler), typeof(DragDrop));

    #endregion

    #region Methods

    /// <summary>
    /// Initiates a drag-and-drop operation.
    /// </summary>
    /// <param name="dragSource">The drag source element.</param>
    /// <param name="data">The data object to drag.</param>
    /// <param name="allowedEffects">The allowed effects.</param>
    /// <returns>The final effect of the operation.</returns>
    public static DragDropEffects DoDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(data);

        _dragSource = dragSource as UIElement;
        _currentData = data as IDataObject ?? new DataObject(data);
        _isDragging = true;

        try
        {
            return DoDragDropInternal(_currentData, allowedEffects);
        }
        finally
        {
            _isDragging = false;
            _dragSource = null;
            _currentData = null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a drag operation is in progress.
    /// </summary>
    public static bool IsDragging => _isDragging;

    #endregion

    #region Event Handler Registration

    /// <summary>
    /// Adds a DragEnter handler.
    /// </summary>
    public static void AddDragEnterHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(DragEnterEvent, handler);
    }

    /// <summary>
    /// Removes a DragEnter handler.
    /// </summary>
    public static void RemoveDragEnterHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(DragEnterEvent, handler);
    }

    /// <summary>
    /// Adds a DragOver handler.
    /// </summary>
    public static void AddDragOverHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(DragOverEvent, handler);
    }

    /// <summary>
    /// Removes a DragOver handler.
    /// </summary>
    public static void RemoveDragOverHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(DragOverEvent, handler);
    }

    /// <summary>
    /// Adds a DragLeave handler.
    /// </summary>
    public static void AddDragLeaveHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(DragLeaveEvent, handler);
    }

    /// <summary>
    /// Removes a DragLeave handler.
    /// </summary>
    public static void RemoveDragLeaveHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(DragLeaveEvent, handler);
    }

    /// <summary>
    /// Adds a Drop handler.
    /// </summary>
    public static void AddDropHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.AddHandler(DropEvent, handler);
    }

    /// <summary>
    /// Removes a Drop handler.
    /// </summary>
    public static void RemoveDropHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
            uiElement.RemoveHandler(DropEvent, handler);
    }

    #endregion

    #region Internal Methods

    private static DragDropEffects DoDragDropInternal(IDataObject data, DragDropEffects allowedEffects)
    {
        // Platform-specific implementation would use OLE drag-drop
        return DragDropEffects.None;
    }

    #endregion
}

/// <summary>
/// Event arguments for drag events.
/// </summary>
public sealed class DragEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the data object being dragged.
    /// </summary>
    public IDataObject Data { get; }

    /// <summary>
    /// Gets the key states.
    /// </summary>
    public DragDropKeyStates KeyStates { get; }

    /// <summary>
    /// Gets the allowed effects.
    /// </summary>
    public DragDropEffects AllowedEffects { get; }

    /// <summary>
    /// Gets or sets the effects of the drop operation.
    /// </summary>
    public DragDropEffects Effects { get; set; }

    private Point _position;

    /// <summary>
    /// Initializes a new instance of the DragEventArgs class.
    /// </summary>
    public DragEventArgs(IDataObject data, DragDropKeyStates keyStates, DragDropEffects allowedEffects, Point position)
    {
        Data = data;
        KeyStates = keyStates;
        AllowedEffects = allowedEffects;
        Effects = allowedEffects;
        _position = position;
    }

    /// <summary>
    /// Gets the position relative to the specified element.
    /// </summary>
    public Point GetPosition(UIElement? relativeTo)
    {
        // Transform position if needed
        return _position;
    }
}

/// <summary>
/// Event handler for drag events.
/// </summary>
public delegate void DragEventHandler(object sender, DragEventArgs e);

/// <summary>
/// Event arguments for give feedback events.
/// </summary>
public sealed class GiveFeedbackEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the effects of the drag operation.
    /// </summary>
    public DragDropEffects Effects { get; }

    /// <summary>
    /// Gets or sets a value indicating whether to use default cursors.
    /// </summary>
    public bool UseDefaultCursors { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the GiveFeedbackEventArgs class.
    /// </summary>
    public GiveFeedbackEventArgs(DragDropEffects effects)
    {
        Effects = effects;
    }
}

/// <summary>
/// Event handler for give feedback events.
/// </summary>
public delegate void GiveFeedbackEventHandler(object sender, GiveFeedbackEventArgs e);

/// <summary>
/// Specifies the action to take with a drag operation.
/// </summary>
public enum DragAction
{
    /// <summary>
    /// Continue the drag operation.
    /// </summary>
    Continue,

    /// <summary>
    /// Drop the data.
    /// </summary>
    Drop,

    /// <summary>
    /// Cancel the drag operation.
    /// </summary>
    Cancel
}

/// <summary>
/// Event arguments for query continue drag events.
/// </summary>
public sealed class QueryContinueDragEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets a value indicating whether the escape key was pressed.
    /// </summary>
    public bool EscapePressed { get; }

    /// <summary>
    /// Gets the key states.
    /// </summary>
    public DragDropKeyStates KeyStates { get; }

    /// <summary>
    /// Gets or sets the action to take.
    /// </summary>
    public DragAction Action { get; set; } = DragAction.Continue;

    /// <summary>
    /// Initializes a new instance of the QueryContinueDragEventArgs class.
    /// </summary>
    public QueryContinueDragEventArgs(bool escapePressed, DragDropKeyStates keyStates)
    {
        EscapePressed = escapePressed;
        KeyStates = keyStates;
    }
}

/// <summary>
/// Event handler for query continue drag events.
/// </summary>
public delegate void QueryContinueDragEventHandler(object sender, QueryContinueDragEventArgs e);

/// <summary>
/// Interface for data objects used in drag-and-drop and clipboard operations.
/// </summary>
public interface IDataObject
{
    /// <summary>
    /// Gets the data for the specified format.
    /// </summary>
    object? GetData(string format);

    /// <summary>
    /// Gets the data for the specified format.
    /// </summary>
    object? GetData(Type format);

    /// <summary>
    /// Gets the data for the specified format.
    /// </summary>
    object? GetData(string format, bool autoConvert);

    /// <summary>
    /// Determines whether data in the specified format is present.
    /// </summary>
    bool GetDataPresent(string format);

    /// <summary>
    /// Determines whether data in the specified format is present.
    /// </summary>
    bool GetDataPresent(Type format);

    /// <summary>
    /// Determines whether data in the specified format is present.
    /// </summary>
    bool GetDataPresent(string format, bool autoConvert);

    /// <summary>
    /// Gets the formats available in this data object.
    /// </summary>
    string[] GetFormats();

    /// <summary>
    /// Gets the formats available in this data object.
    /// </summary>
    string[] GetFormats(bool autoConvert);

    /// <summary>
    /// Sets data in the specified format.
    /// </summary>
    void SetData(object data);

    /// <summary>
    /// Sets data in the specified format.
    /// </summary>
    void SetData(string format, object data);

    /// <summary>
    /// Sets data in the specified format.
    /// </summary>
    void SetData(Type format, object data);

    /// <summary>
    /// Sets data in the specified format.
    /// </summary>
    void SetData(string format, object data, bool autoConvert);
}

/// <summary>
/// Implements a basic data object for drag-and-drop and clipboard.
/// </summary>
public sealed class DataObject : IDataObject
{
    private readonly Dictionary<string, object> _data = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the DataObject class.
    /// </summary>
    public DataObject()
    {
    }

    /// <summary>
    /// Initializes a new instance of the DataObject class with the specified data.
    /// </summary>
    public DataObject(object data)
    {
        SetData(data);
    }

    /// <summary>
    /// Initializes a new instance of the DataObject class with the specified format and data.
    /// </summary>
    public DataObject(string format, object data)
    {
        SetData(format, data);
    }

    /// <inheritdoc />
    public object? GetData(string format)
    {
        return GetData(format, true);
    }

    /// <inheritdoc />
    public object? GetData(Type format)
    {
        return GetData(format.FullName ?? format.Name);
    }

    /// <inheritdoc />
    public object? GetData(string format, bool autoConvert)
    {
        _data.TryGetValue(format, out var data);
        return data;
    }

    /// <inheritdoc />
    public bool GetDataPresent(string format)
    {
        return GetDataPresent(format, true);
    }

    /// <inheritdoc />
    public bool GetDataPresent(Type format)
    {
        return GetDataPresent(format.FullName ?? format.Name);
    }

    /// <inheritdoc />
    public bool GetDataPresent(string format, bool autoConvert)
    {
        return _data.ContainsKey(format);
    }

    /// <inheritdoc />
    public string[] GetFormats()
    {
        return GetFormats(true);
    }

    /// <inheritdoc />
    public string[] GetFormats(bool autoConvert)
    {
        return _data.Keys.ToArray();
    }

    /// <inheritdoc />
    public void SetData(object data)
    {
        var type = data.GetType();
        SetData(type.FullName ?? type.Name, data);
    }

    /// <inheritdoc />
    public void SetData(string format, object data)
    {
        SetData(format, data, true);
    }

    /// <inheritdoc />
    public void SetData(Type format, object data)
    {
        SetData(format.FullName ?? format.Name, data);
    }

    /// <inheritdoc />
    public void SetData(string format, object data, bool autoConvert)
    {
        _data[format] = data;
    }
}

/// <summary>
/// Standard data formats.
/// </summary>
public static class DataFormats
{
    /// <summary>
    /// Text format.
    /// </summary>
    public const string Text = "Text";

    /// <summary>
    /// Unicode text format.
    /// </summary>
    public const string UnicodeText = "UnicodeText";

    /// <summary>
    /// Rich text format.
    /// </summary>
    public const string Rtf = "Rich Text Format";

    /// <summary>
    /// HTML format.
    /// </summary>
    public const string Html = "HTML Format";

    /// <summary>
    /// File drop format.
    /// </summary>
    public const string FileDrop = "FileDrop";

    /// <summary>
    /// Bitmap format.
    /// </summary>
    public const string Bitmap = "Bitmap";

    /// <summary>
    /// DIB format.
    /// </summary>
    public const string Dib = "DeviceIndependentBitmap";

    /// <summary>
    /// XAML format.
    /// </summary>
    public const string Xaml = "Xaml";

    /// <summary>
    /// XAML package format.
    /// </summary>
    public const string XamlPackage = "XamlPackage";

    /// <summary>
    /// Serializable format.
    /// </summary>
    public const string Serializable = "PersistentObject";
}
