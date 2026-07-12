namespace Jalium.UI;

/// <summary>
/// Base class for the data-object copying, pasting, and setting-data events.
/// </summary>
public abstract class DataObjectEventArgs : RoutedEventArgs
{
    internal DataObjectEventArgs(RoutedEvent routedEvent, bool isDragDrop)
    {
        if (routedEvent != global::Jalium.UI.DataObject.CopyingEvent &&
            routedEvent != global::Jalium.UI.DataObject.PastingEvent &&
            routedEvent != global::Jalium.UI.DataObject.SettingDataEvent)
        {
            throw new ArgumentOutOfRangeException(nameof(routedEvent));
        }

        RoutedEvent = routedEvent;
        IsDragDrop = isDragDrop;
    }

    public bool IsDragDrop { get; }

    public bool CommandCancelled { get; private set; }

    public void CancelCommand()
    {
        CommandCancelled = true;
    }
}

/// <summary>
/// Provides data for the <see cref="DataObject.CopyingEvent"/> routed event.
/// </summary>
public sealed class DataObjectCopyingEventArgs : DataObjectEventArgs
{
    public DataObjectCopyingEventArgs(IDataObject dataObject, bool isDragDrop)
        : base(global::Jalium.UI.DataObject.CopyingEvent, isDragDrop)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        DataObject = dataObject;
    }

    public IDataObject DataObject { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        ((DataObjectCopyingEventHandler)handler)(target, this);
    }
}

public delegate void DataObjectCopyingEventHandler(object sender, DataObjectCopyingEventArgs e);

/// <summary>
/// Provides data for the <see cref="DataObject.PastingEvent"/> routed event.
/// </summary>
public sealed class DataObjectPastingEventArgs : DataObjectEventArgs
{
    private readonly IDataObject _sourceDataObject;
    private IDataObject _dataObject;
    private string _formatToApply;

    public DataObjectPastingEventArgs(IDataObject dataObject, bool isDragDrop, string formatToApply)
        : base(global::Jalium.UI.DataObject.PastingEvent, isDragDrop)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        ArgumentNullException.ThrowIfNull(formatToApply);

        if (formatToApply.Length == 0)
        {
            throw new ArgumentException("The data format cannot be empty.", nameof(formatToApply));
        }

        if (!dataObject.GetDataPresent(formatToApply))
        {
            throw new ArgumentException(
                $"The data format '{formatToApply}' is not present on the data object.",
                nameof(formatToApply));
        }

        _sourceDataObject = dataObject;
        _dataObject = dataObject;
        _formatToApply = formatToApply;
    }

    public IDataObject SourceDataObject => _sourceDataObject;

    public IDataObject DataObject
    {
        get => _dataObject;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            string[]? availableFormats = value.GetFormats(autoConvert: false);
            if (availableFormats is not { Length: > 0 })
            {
                throw new ArgumentException(
                    "The data object must contain at least one format.",
                    nameof(value));
            }

            _dataObject = value;
            _formatToApply = availableFormats[0];
        }
    }

    public string FormatToApply
    {
        get => _formatToApply;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!_dataObject.GetDataPresent(value))
            {
                throw new ArgumentException(
                    $"The data format '{value}' is not present on the data object.",
                    nameof(value));
            }

            _formatToApply = value;
        }
    }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        ((DataObjectPastingEventHandler)handler)(target, this);
    }
}

public delegate void DataObjectPastingEventHandler(object sender, DataObjectPastingEventArgs e);

/// <summary>
/// Provides data for the <see cref="DataObject.SettingDataEvent"/> routed event.
/// </summary>
public sealed class DataObjectSettingDataEventArgs : DataObjectEventArgs
{
    public DataObjectSettingDataEventArgs(IDataObject dataObject, string format)
        : base(global::Jalium.UI.DataObject.SettingDataEvent, isDragDrop: false)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        ArgumentNullException.ThrowIfNull(format);

        DataObject = dataObject;
        Format = format;
    }

    public IDataObject DataObject { get; }

    public string Format { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        ((DataObjectSettingDataEventHandler)handler)(target, this);
    }
}

public delegate void DataObjectSettingDataEventHandler(object sender, DataObjectSettingDataEventArgs e);
