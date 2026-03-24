using System.Collections.Specialized;

namespace Jalium.UI;

/// <summary>
/// Specifies the effects of a drag-and-drop operation.
/// </summary>
[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = unchecked((int)0x80000000),
    All = Copy | Move | Link | Scroll
}

/// <summary>
/// Specifies the key states during a drag-and-drop operation.
/// </summary>
[Flags]
public enum DragDropKeyStates
{
    None = 0,
    LeftMouseButton = 1,
    RightMouseButton = 2,
    ShiftKey = 4,
    ControlKey = 8,
    MiddleMouseButton = 16,
    AltKey = 32
}

/// <summary>
/// Specifies the action to take with a drag operation.
/// </summary>
public enum DragAction
{
    Continue,
    Drop,
    Cancel
}

/// <summary>
/// Provides a format-independent mechanism for transferring data.
/// </summary>
public interface IDataObject
{
    object? GetData(string format);
    object? GetData(Type format);
    object? GetData(string format, bool autoConvert);
    bool GetDataPresent(string format);
    bool GetDataPresent(Type format);
    bool GetDataPresent(string format, bool autoConvert);
    string[] GetFormats();
    string[] GetFormats(bool autoConvert);
    void SetData(object data);
    void SetData(string format, object data);
    void SetData(Type format, object data);
    void SetData(string format, object data, bool autoConvert);
}

/// <summary>
/// Implements IDataObject for data transfer in drag-and-drop and clipboard operations.
/// </summary>
public sealed class DataObject : IDataObject
{
    private readonly Dictionary<string, object> _data = new(StringComparer.OrdinalIgnoreCase);

    public DataObject() { }
    public DataObject(object data) => SetData(data);
    public DataObject(string format, object data) => SetData(format, data);

    public object? GetData(string format) => GetData(format, true);
    public object? GetData(Type format) => GetData(format.FullName ?? format.Name);
    public object? GetData(string format, bool autoConvert)
    {
        _data.TryGetValue(format, out var data);
        return data;
    }

    public bool GetDataPresent(string format) => GetDataPresent(format, true);
    public bool GetDataPresent(Type format) => GetDataPresent(format.FullName ?? format.Name);
    public bool GetDataPresent(string format, bool autoConvert) => _data.ContainsKey(format);

    public string[] GetFormats() => GetFormats(true);
    public string[] GetFormats(bool autoConvert) => _data.Keys.ToArray();

    public void SetData(object data)
    {
        var type = data.GetType();
        SetData(type.FullName ?? type.Name, data);

        if (data is string text)
        {
            SetData(DataFormats.Text, text);
            SetData(DataFormats.UnicodeText, text);
        }
        else if (data is string[] files)
        {
            SetData(DataFormats.FileDrop, files);
        }
    }

    public void SetData(string format, object data) => SetData(format, data, true);
    public void SetData(Type format, object data) => SetData(format.FullName ?? format.Name, data);
    public void SetData(string format, object data, bool autoConvert) => _data[format] = data;

    public bool ContainsFileDropList() => GetDataPresent(DataFormats.FileDrop);

    public StringCollection GetFileDropList()
    {
        var files = GetData(DataFormats.FileDrop) as string[];
        var collection = new StringCollection();
        if (files != null)
            collection.AddRange(files);
        return collection;
    }

    public void SetFileDropList(StringCollection fileDropList)
    {
        var files = new string[fileDropList.Count];
        fileDropList.CopyTo(files, 0);
        SetData(DataFormats.FileDrop, files);
    }

    public bool ContainsText() =>
        GetDataPresent(DataFormats.Text) || GetDataPresent(DataFormats.UnicodeText);

    public string GetText() =>
        GetData(DataFormats.UnicodeText) as string ?? GetData(DataFormats.Text) as string ?? string.Empty;

    public void SetText(string textData)
    {
        SetData(DataFormats.Text, textData);
        SetData(DataFormats.UnicodeText, textData);
    }
}

/// <summary>
/// Provides standard data format names.
/// </summary>
public static class DataFormats
{
    public const string Text = "Text";
    public const string UnicodeText = "UnicodeText";
    public const string Rtf = "Rich Text Format";
    public const string Html = "HTML Format";
    public const string FileDrop = "FileDrop";
    public const string Bitmap = "Bitmap";
    public const string Dib = "DeviceIndependentBitmap";
    public const string Xaml = "Xaml";
    public const string XamlPackage = "XamlPackage";
    public const string Serializable = "PersistentObject";
    public const string StringFormat = "System.String";
    public const string Locale = "Locale";
    public const string OemText = "OEMText";
    public const string CommaSeparatedValue = "CSV";
}

/// <summary>
/// Provides data for drag-and-drop events.
/// </summary>
public class DragEventArgs : RoutedEventArgs
{
    public IDataObject Data { get; }
    public DragDropKeyStates KeyStates { get; }
    public DragDropEffects AllowedEffects { get; }
    public DragDropEffects Effects { get; set; }

    private readonly Point _position;

    public DragEventArgs(RoutedEvent routedEvent, IDataObject data, DragDropKeyStates keyStates, DragDropEffects allowedEffects, Point position)
        : base(routedEvent)
    {
        Data = data;
        KeyStates = keyStates;
        AllowedEffects = allowedEffects;
        Effects = allowedEffects;
        _position = position;
    }

    public Point GetPosition(IInputElement? relativeTo) => _position;

    /// <inheritdoc />
    internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is DragEventHandler dragHandler)
            dragHandler(target, this);
        else
            base.InvokeEventHandler(handler, target);
    }
}

/// <summary>
/// Delegate for drag events.
/// </summary>
public delegate void DragEventHandler(object sender, DragEventArgs e);

/// <summary>
/// Provides data for the GiveFeedback event.
/// </summary>
public class GiveFeedbackEventArgs : RoutedEventArgs
{
    public DragDropEffects Effects { get; }
    public bool UseDefaultCursors { get; set; } = true;

    public GiveFeedbackEventArgs(RoutedEvent routedEvent, DragDropEffects effects)
        : base(routedEvent)
    {
        Effects = effects;
    }

    /// <inheritdoc />
    internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is GiveFeedbackEventHandler feedbackHandler)
            feedbackHandler(target, this);
        else
            base.InvokeEventHandler(handler, target);
    }
}

/// <summary>
/// Delegate for give feedback events.
/// </summary>
public delegate void GiveFeedbackEventHandler(object sender, GiveFeedbackEventArgs e);

/// <summary>
/// Provides data for the QueryContinueDrag event.
/// </summary>
public class QueryContinueDragEventArgs : RoutedEventArgs
{
    public bool EscapePressed { get; }
    public DragDropKeyStates KeyStates { get; }
    public DragAction Action { get; set; } = DragAction.Continue;

    public QueryContinueDragEventArgs(RoutedEvent routedEvent, DragDropKeyStates keyStates, bool escapePressed)
        : base(routedEvent)
    {
        EscapePressed = escapePressed;
        KeyStates = keyStates;
        Action = escapePressed ? DragAction.Cancel : DragAction.Continue;
    }

    /// <inheritdoc />
    internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is QueryContinueDragEventHandler queryHandler)
            queryHandler(target, this);
        else
            base.InvokeEventHandler(handler, target);
    }
}

/// <summary>
/// Delegate for query continue drag events.
/// </summary>
public delegate void QueryContinueDragEventHandler(object sender, QueryContinueDragEventArgs e);

/// <summary>
/// Provides drag-and-drop static helper methods and routed event identifiers.
/// </summary>
public static class DragDrop
{
    #region Attached Properties

    public static readonly DependencyProperty AllowDropProperty =
        DependencyProperty.RegisterAttached("AllowDrop", typeof(bool), typeof(DragDrop),
            new PropertyMetadata(false));

    public static bool GetAllowDrop(DependencyObject element) =>
        (bool)(element.GetValue(AllowDropProperty) ?? false);

    public static void SetAllowDrop(DependencyObject element, bool value) =>
        element.SetValue(AllowDropProperty, value);

    /// <summary>
    /// Identifies the ShowDragVisual attached property. When true (default),
    /// a semi-transparent copy of the drag source follows the cursor during drag.
    /// </summary>
    public static readonly DependencyProperty ShowDragVisualProperty =
        DependencyProperty.RegisterAttached("ShowDragVisual", typeof(bool), typeof(DragDrop),
            new PropertyMetadata(true));

    public static bool GetShowDragVisual(DependencyObject element) =>
        (bool)(element.GetValue(ShowDragVisualProperty) ?? true);

    public static void SetShowDragVisual(DependencyObject element, bool value) =>
        element.SetValue(ShowDragVisualProperty, value);

    #endregion

    #region Routed Events

    public static readonly RoutedEvent PreviewDragEnterEvent =
        EventManager.RegisterRoutedEvent("PreviewDragEnter", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent DragEnterEvent =
        EventManager.RegisterRoutedEvent("DragEnter", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent PreviewDragOverEvent =
        EventManager.RegisterRoutedEvent("PreviewDragOver", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent DragOverEvent =
        EventManager.RegisterRoutedEvent("DragOver", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent PreviewDragLeaveEvent =
        EventManager.RegisterRoutedEvent("PreviewDragLeave", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent DragLeaveEvent =
        EventManager.RegisterRoutedEvent("DragLeave", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent PreviewDropEvent =
        EventManager.RegisterRoutedEvent("PreviewDrop", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent DropEvent =
        EventManager.RegisterRoutedEvent("Drop", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent QueryContinueDragEvent =
        EventManager.RegisterRoutedEvent("QueryContinueDrag", RoutingStrategy.Bubble, typeof(QueryContinueDragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent GiveFeedbackEvent =
        EventManager.RegisterRoutedEvent("GiveFeedback", RoutingStrategy.Bubble, typeof(GiveFeedbackEventHandler), typeof(DragDrop));

    #endregion

    #region Event Handler Registration

    public static void AddDragEnterHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(DragEnterEvent, handler); }
    public static void RemoveDragEnterHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(DragEnterEvent, handler); }
    public static void AddDragOverHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(DragOverEvent, handler); }
    public static void RemoveDragOverHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(DragOverEvent, handler); }
    public static void AddDragLeaveHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(DragLeaveEvent, handler); }
    public static void RemoveDragLeaveHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(DragLeaveEvent, handler); }
    public static void AddDropHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(DropEvent, handler); }
    public static void RemoveDropHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(DropEvent, handler); }
    public static void AddPreviewDragEnterHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(PreviewDragEnterEvent, handler); }
    public static void RemovePreviewDragEnterHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(PreviewDragEnterEvent, handler); }
    public static void AddPreviewDragOverHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(PreviewDragOverEvent, handler); }
    public static void RemovePreviewDragOverHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(PreviewDragOverEvent, handler); }
    public static void AddPreviewDragLeaveHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(PreviewDragLeaveEvent, handler); }
    public static void RemovePreviewDragLeaveHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(PreviewDragLeaveEvent, handler); }
    public static void AddPreviewDropHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(PreviewDropEvent, handler); }
    public static void RemovePreviewDropHandler(DependencyObject element, DragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(PreviewDropEvent, handler); }
    public static void AddGiveFeedbackHandler(DependencyObject element, GiveFeedbackEventHandler handler) { if (element is UIElement ui) ui.AddHandler(GiveFeedbackEvent, handler); }
    public static void RemoveGiveFeedbackHandler(DependencyObject element, GiveFeedbackEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(GiveFeedbackEvent, handler); }
    public static void AddQueryContinueDragHandler(DependencyObject element, QueryContinueDragEventHandler handler) { if (element is UIElement ui) ui.AddHandler(QueryContinueDragEvent, handler); }
    public static void RemoveQueryContinueDragHandler(DependencyObject element, QueryContinueDragEventHandler handler) { if (element is UIElement ui) ui.RemoveHandler(QueryContinueDragEvent, handler); }

    #endregion

    #region DoDragDrop

    private static bool _isDragging;

    /// <summary>
    /// Gets whether a drag operation is in progress.
    /// </summary>
    public static bool IsDragging => _isDragging;

    /// <summary>
    /// Platform-specific DoDragDrop implementation. Set by the hosting platform layer (e.g. Jalium.UI.Controls on Windows).
    /// </summary>
    internal static Func<DependencyObject, IDataObject, DragDropEffects, DragDropEffects>? DoDragDropOverride { get; set; }

    /// <summary>
    /// Initiates a drag-and-drop operation.
    /// </summary>
    public static DragDropEffects DoDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(data);

        if (_isDragging)
            return DragDropEffects.None;

        var dataObj = data as IDataObject ?? new DataObject(data);
        _isDragging = true;

        try
        {
            return DoDragDropOverride?.Invoke(dragSource, dataObj, allowedEffects) ?? DragDropEffects.None;
        }
        finally
        {
            _isDragging = false;
        }
    }

    #endregion
}
