using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
    All = Copy | Move | Scroll
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
/// Identifies the Windows Shell drop image and description style shown next to
/// the drag image while the pointer is over a drop target. The values mirror the
/// Win32 <c>DROPIMAGETYPE</c> constants consumed by <c>IDropTargetHelper</c>.
/// </summary>
public enum DropImageType
{
    /// <summary>No custom drop description; the Shell shows its default text.</summary>
    Invalid = -1,

    /// <summary>The drop would not be accepted (a "no-drop" badge).</summary>
    None = 0,

    /// <summary>The drop copies the data (matches <c>DROPEFFECT_COPY</c>).</summary>
    Copy = 1,

    /// <summary>The drop moves the data (matches <c>DROPEFFECT_MOVE</c>).</summary>
    Move = 2,

    /// <summary>The drop creates a link to the data (matches <c>DROPEFFECT_LINK</c>).</summary>
    Link = 4,

    /// <summary>A neutral label badge with no effect glyph.</summary>
    Label = 6,

    /// <summary>A warning badge.</summary>
    Warning = 7,

    /// <summary>Show the description text with no accompanying image glyph.</summary>
    NoImage = 8,
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
public sealed class DataObject : ITypedDataObject
{
    private const string FileNameAnsiFormat = "FileName";
    private const string FileNameUnicodeFormat = "FileNameW";
    private const string BinaryBitmapFormat = "System.Drawing.Bitmap";
    private const string BinaryMetafileFormat = "System.Drawing.Imaging.Metafile";

    private readonly Dictionary<string, DataEntry> _data = new(StringComparer.OrdinalIgnoreCase);

    public static readonly RoutedEvent CopyingEvent = EventManager.RegisterRoutedEvent(
        "Copying",
        RoutingStrategy.Bubble,
        typeof(DataObjectCopyingEventHandler),
        typeof(DataObject));

    public static readonly RoutedEvent PastingEvent = EventManager.RegisterRoutedEvent(
        "Pasting",
        RoutingStrategy.Bubble,
        typeof(DataObjectPastingEventHandler),
        typeof(DataObject));

    public static readonly RoutedEvent SettingDataEvent = EventManager.RegisterRoutedEvent(
        "SettingData",
        RoutingStrategy.Bubble,
        typeof(DataObjectSettingDataEventHandler),
        typeof(DataObject));

    public DataObject() { }
    public DataObject(object data) => SetData(data);
    public DataObject(string format, object data) => SetData(format, data);
    public DataObject(string format, object data, bool autoConvert) => SetData(format, data, autoConvert);
    public DataObject(Type format, object data) => SetData(format, data);

    public object? GetData(string format) => GetData(format, autoConvert: true);

    public object? GetData(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetData(GetTypeFormat(format));
    }

    public object? GetData(string format, bool autoConvert)
    {
        if (!TryGetEntry(format, autoConvert, out DataEntry entry))
        {
            return null;
        }

        return entry.Data is IJsonData jsonData ? jsonData.Deserialize() : entry.Data;
    }

    public bool GetDataPresent(string format) => GetDataPresent(format, autoConvert: true);

    public bool GetDataPresent(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetDataPresent(GetTypeFormat(format));
    }

    public bool GetDataPresent(string format, bool autoConvert)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        if (_data.ContainsKey(format))
        {
            return true;
        }

        if (!autoConvert)
        {
            return false;
        }

        foreach ((string storedFormat, DataEntry entry) in _data)
        {
            if (!entry.AutoConvert)
            {
                continue;
            }

            foreach (string mappedFormat in GetMappedFormats(storedFormat))
            {
                if (format.Equals(mappedFormat, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public string[] GetFormats() => GetFormats(autoConvert: true);

    public string[] GetFormats(bool autoConvert)
    {
        if (!autoConvert)
        {
            return _data.Keys.ToArray();
        }

        var formats = new List<string>(_data.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string format, DataEntry entry) in _data)
        {
            AddDistinct(format);
            if (entry.AutoConvert)
            {
                foreach (string mappedFormat in GetMappedFormats(format))
                {
                    AddDistinct(mappedFormat);
                }
            }
        }

        return formats.ToArray();

        void AddDistinct(string format)
        {
            if (seen.Add(format))
            {
                formats.Add(format);
            }
        }
    }

    public void SetData(object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        SetData(data.GetType(), data);
    }

    public void SetData(string format, object data) => SetData(format, data, autoConvert: true);

    public void SetData(Type format, object data)
    {
        ArgumentNullException.ThrowIfNull(format);
        SetData(GetTypeFormat(format), data);
    }

    public void SetData(string format, object data, bool autoConvert)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        _data[format] = new DataEntry(data, autoConvert);
    }

    public bool TryGetData<T>([NotNullWhen(true), MaybeNullWhen(false)] out T data) =>
        TryGetData(GetTypeFormat(typeof(T)), autoConvert: true, out data);

    public bool TryGetData<T>(
        string format,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data) =>
        TryGetData(format, autoConvert: true, out data);

    public bool TryGetData<T>(
        string format,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        data = default;
        if (!IsValidTypeForFormat<T>(format) || !TryGetEntry(format, autoConvert, out DataEntry entry))
        {
            return false;
        }

        if (entry.Data is T value)
        {
            data = value;
            return true;
        }

        if (entry.Data is JsonData<T> jsonData)
        {
            data = (T)jsonData.Deserialize();
            return true;
        }

        return false;
    }

    public bool TryGetData<T>(
        string format,
        Func<TypeName, Type?> resolver,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        data = default;
        ArgumentNullException.ThrowIfNull(resolver);
        return TryGetData(format, autoConvert, out data);
    }

    [RequiresUnreferencedCode("Use the JsonTypeInfo<T> overload for trimming and NativeAOT.")]
    [RequiresDynamicCode("Use the JsonTypeInfo<T> overload for NativeAOT.")]
    public void SetDataAsJson<T>(T data) =>
        SetDataAsJson(GetTypeFormat(typeof(T)), data);

    [RequiresUnreferencedCode("Use the JsonTypeInfo<T> overload for trimming and NativeAOT.")]
    [RequiresDynamicCode("Use the JsonTypeInfo<T> overload for NativeAOT.")]
    public void SetDataAsJson<T>(string format, T data)
    {
        ValidateJsonData(format, data);
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            throw new NotSupportedException(
                "Reflection-based JSON metadata is disabled. Use SetDataAsJson with a source-generated JsonTypeInfo<T>.");
        }

        _data[format] = new DataEntry(JsonData<T>.CreateReflection(data), AutoConvert: false);
    }

    public void SetDataAsJson<T>(T data, JsonTypeInfo<T> jsonTypeInfo) =>
        SetDataAsJson(GetTypeFormat(typeof(T)), data, jsonTypeInfo);

    public void SetDataAsJson<T>(string format, T data, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ValidateJsonData(format, data);
        _data[format] = new DataEntry(JsonData<T>.Create(data, jsonTypeInfo), AutoConvert: false);
    }

    private static void ValidateJsonData<T>(string format, T data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(data);

        if (IsPredefinedFormat(format))
        {
            throw new ArgumentException("Predefined data formats cannot be JSON serialized.", nameof(format));
        }

        if (typeof(T).IsAssignableTo(typeof(DataObject)))
        {
            throw new ArgumentException("A DataObject cannot be JSON serialized as data.", nameof(data));
        }

    }

    public bool ContainsAudio() => GetDataPresent(DataFormats.WaveAudio, autoConvert: false);

    public bool ContainsFileDropList() => GetDataPresent(DataFormats.FileDrop, autoConvert: false);

    public bool ContainsImage() => GetDataPresent(DataFormats.Bitmap, autoConvert: false);

    public bool ContainsText() => ContainsText(TextDataFormat.UnicodeText);

    public bool ContainsText(TextDataFormat format)
    {
        ValidateTextDataFormat(format);
        return GetDataPresent(ConvertTextDataFormat(format), autoConvert: false);
    }

    public Stream? GetAudioStream() => GetData(DataFormats.WaveAudio, autoConvert: false) as Stream;

    public StringCollection GetFileDropList()
    {
        var collection = new StringCollection();
        if (GetData(DataFormats.FileDrop, autoConvert: true) is string[] files)
        {
            collection.AddRange(files);
        }

        return collection;
    }

    public Media.Imaging.BitmapSource? GetImage() =>
        GetData(DataFormats.Bitmap, autoConvert: false) as Media.Imaging.BitmapSource;

    public void SetFileDropList(StringCollection fileDropList)
    {
        ArgumentNullException.ThrowIfNull(fileDropList);
        if (fileDropList.Count == 0)
        {
            throw new ArgumentException("The file drop list cannot be empty.", nameof(fileDropList));
        }

        var files = new string[fileDropList.Count];
        fileDropList.CopyTo(files, 0);
        foreach (string? file in files)
        {
            if (string.IsNullOrEmpty(file) || file.Contains('\0'))
            {
                throw new ArgumentException("The file drop list contains an invalid path.", nameof(fileDropList));
            }
        }

        SetData(DataFormats.FileDrop, files, autoConvert: true);
    }

    public void SetImage(Media.Imaging.BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        SetData(DataFormats.Bitmap, image, autoConvert: false);
    }

    public string GetText() => GetText(TextDataFormat.UnicodeText);

    public string GetText(TextDataFormat format)
    {
        ValidateTextDataFormat(format);
        return GetData(ConvertTextDataFormat(format), autoConvert: false) as string ?? string.Empty;
    }

    public void SetAudio(byte[] audioBytes)
    {
        ArgumentNullException.ThrowIfNull(audioBytes);
        SetAudio(new MemoryStream(audioBytes));
    }

    public void SetAudio(Stream audioStream)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        SetData(DataFormats.WaveAudio, audioStream, autoConvert: false);
    }

    public void SetText(string textData) => SetText(textData, TextDataFormat.UnicodeText);

    public void SetText(string textData, TextDataFormat format)
    {
        ArgumentNullException.ThrowIfNull(textData);
        ValidateTextDataFormat(format);
        SetData(ConvertTextDataFormat(format), textData, autoConvert: false);
    }

    public static void AddCopyingHandler(DependencyObject element, DataObjectCopyingEventHandler handler) =>
        AddHandler(element, CopyingEvent, handler);

    public static void RemoveCopyingHandler(DependencyObject element, DataObjectCopyingEventHandler handler) =>
        RemoveHandler(element, CopyingEvent, handler);

    public static void AddPastingHandler(DependencyObject element, DataObjectPastingEventHandler handler) =>
        AddHandler(element, PastingEvent, handler);

    public static void RemovePastingHandler(DependencyObject element, DataObjectPastingEventHandler handler) =>
        RemoveHandler(element, PastingEvent, handler);

    public static void AddSettingDataHandler(DependencyObject element, DataObjectSettingDataEventHandler handler) =>
        AddHandler(element, SettingDataEvent, handler);

    public static void RemoveSettingDataHandler(DependencyObject element, DataObjectSettingDataEventHandler handler) =>
        RemoveHandler(element, SettingDataEvent, handler);

    private static void AddHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);

        if (element is not UIElement uiElement)
        {
            throw new ArgumentException("The element must be a UIElement.", nameof(element));
        }

        uiElement.AddHandler(routedEvent, handler);
    }

    private static void RemoveHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);

        if (element is not UIElement uiElement)
        {
            throw new ArgumentException("The element must be a UIElement.", nameof(element));
        }

        uiElement.RemoveHandler(routedEvent, handler);
    }

    private bool TryGetEntry(string? format, bool autoConvert, out DataEntry entry)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            entry = default;
            return false;
        }

        if (_data.TryGetValue(format, out entry))
        {
            return true;
        }

        if (autoConvert)
        {
            foreach (string mappedFormat in GetMappedFormats(format))
            {
                if (_data.TryGetValue(mappedFormat, out entry))
                {
                    return true;
                }
            }
        }

        entry = default;
        return false;
    }

    private static IEnumerable<string> GetMappedFormats(string format)
    {
        if (format.Equals(DataFormats.Text, StringComparison.OrdinalIgnoreCase))
        {
            yield return DataFormats.StringFormat;
            yield return DataFormats.UnicodeText;
        }
        else if (format.Equals(DataFormats.UnicodeText, StringComparison.OrdinalIgnoreCase))
        {
            yield return DataFormats.StringFormat;
            yield return DataFormats.Text;
        }
        else if (format.Equals(DataFormats.StringFormat, StringComparison.OrdinalIgnoreCase))
        {
            yield return DataFormats.Text;
            yield return DataFormats.UnicodeText;
        }
        else if (format.Equals(DataFormats.FileDrop, StringComparison.OrdinalIgnoreCase))
        {
            yield return FileNameUnicodeFormat;
            yield return FileNameAnsiFormat;
        }
        else if (format.Equals(FileNameUnicodeFormat, StringComparison.OrdinalIgnoreCase))
        {
            yield return DataFormats.FileDrop;
            yield return FileNameAnsiFormat;
        }
        else if (format.Equals(FileNameAnsiFormat, StringComparison.OrdinalIgnoreCase))
        {
            yield return DataFormats.FileDrop;
            yield return FileNameUnicodeFormat;
        }
        else if (format.Equals(DataFormats.Bitmap, StringComparison.OrdinalIgnoreCase))
        {
            yield return BinaryBitmapFormat;
        }
        else if (format.Equals(BinaryBitmapFormat, StringComparison.OrdinalIgnoreCase))
        {
            yield return DataFormats.Bitmap;
        }
        else if (format.Equals(DataFormats.EnhancedMetafile, StringComparison.OrdinalIgnoreCase))
        {
            yield return BinaryMetafileFormat;
        }
        else if (format.Equals(BinaryMetafileFormat, StringComparison.OrdinalIgnoreCase))
        {
            yield return DataFormats.EnhancedMetafile;
        }
    }

    private static bool IsValidTypeForFormat<T>(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        Type type = typeof(T);
        bool valid = IsTextFormat(format)
            ? type == typeof(string)
            : IsFileDropFormat(format)
                ? type == typeof(string[])
                : true;

        if (!valid)
        {
            throw new NotSupportedException($"The data format '{format}' is not compatible with type '{type.FullName}'.");
        }

        return true;
    }

    private static bool IsTextFormat(string format) =>
        format.Equals(DataFormats.Text, StringComparison.OrdinalIgnoreCase) ||
        format.Equals(DataFormats.UnicodeText, StringComparison.OrdinalIgnoreCase) ||
        format.Equals(DataFormats.StringFormat, StringComparison.OrdinalIgnoreCase) ||
        format.Equals(DataFormats.Rtf, StringComparison.OrdinalIgnoreCase) ||
        format.Equals(DataFormats.Html, StringComparison.OrdinalIgnoreCase) ||
        format.Equals(DataFormats.OemText, StringComparison.OrdinalIgnoreCase);

    private static bool IsFileDropFormat(string format) =>
        format.Equals(DataFormats.FileDrop, StringComparison.OrdinalIgnoreCase) ||
        format.Equals(FileNameAnsiFormat, StringComparison.OrdinalIgnoreCase) ||
        format.Equals(FileNameUnicodeFormat, StringComparison.OrdinalIgnoreCase);

    private static bool IsPredefinedFormat(string format) => format is
        "Text" or
        "UnicodeText" or
        "Rich Text Format" or
        "HTML Format" or
        "OEMText" or
        "FileDrop" or
        FileNameAnsiFormat or
        FileNameUnicodeFormat or
        "System.String" or
        BinaryBitmapFormat or
        "CSV" or
        "Csv" or
        "DeviceIndependentBitmap" or
        "DataInterchangeFormat" or
        "Locale" or
        "PenData" or
        "RiffAudio" or
        "SymbolicLink" or
        "TaggedImageFileFormat" or
        "WaveAudio" or
        "Bitmap" or
        "EnhancedMetafile" or
        "Palette" or
        "MetaFilePict";

    private static string ConvertTextDataFormat(TextDataFormat format) => format switch
    {
        TextDataFormat.Text => DataFormats.Text,
        TextDataFormat.UnicodeText => DataFormats.UnicodeText,
        TextDataFormat.Rtf => DataFormats.Rtf,
        TextDataFormat.Html => DataFormats.Html,
        TextDataFormat.CommaSeparatedValue => DataFormats.CommaSeparatedValue,
        TextDataFormat.Xaml => DataFormats.Xaml,
        _ => DataFormats.UnicodeText,
    };

    private static void ValidateTextDataFormat(TextDataFormat format)
    {
        if (format is < TextDataFormat.Text or > TextDataFormat.Xaml)
        {
            throw new InvalidEnumArgumentException(nameof(format), (int)format, typeof(TextDataFormat));
        }
    }

    private static string GetTypeFormat(Type type) =>
        type.FullName ?? throw new ArgumentException("The type must have a full name.", nameof(type));

    private readonly record struct DataEntry(object Data, bool AutoConvert);

    private interface IJsonData
    {
        object Deserialize();
    }

    private sealed class JsonData<T> : IJsonData
    {
        private readonly byte[] _jsonBytes;
        private readonly Func<byte[], T?> _deserialize;

        private JsonData(byte[] jsonBytes, Func<byte[], T?> deserialize)
        {
            _jsonBytes = jsonBytes;
            _deserialize = deserialize;
        }

        public static JsonData<T> Create(T data, JsonTypeInfo<T> jsonTypeInfo) =>
            new(
                JsonSerializer.SerializeToUtf8Bytes(data, jsonTypeInfo),
                bytes => JsonSerializer.Deserialize(bytes, jsonTypeInfo));

        [RequiresUnreferencedCode("Use Create with JsonTypeInfo<T> for trimming and NativeAOT.")]
        [RequiresDynamicCode("Use Create with JsonTypeInfo<T> for NativeAOT.")]
        public static JsonData<T> CreateReflection(T data) =>
            new(
                JsonSerializer.SerializeToUtf8Bytes(data),
                static bytes => JsonSerializer.Deserialize<T>(bytes));

        public object Deserialize()
        {
            try
            {
                return _deserialize(_jsonBytes)
                    ?? throw new InvalidOperationException("JSON deserialization returned null.");
            }
            catch (Exception exception)
            {
                return new NotSupportedException(exception.Message, exception);
            }
        }
    }
}

/// <summary>
/// Provides standard data format names.
/// </summary>
public static class DataFormats
{
    private static readonly object s_syncRoot = new();
    private static readonly Dictionary<string, DataFormat> s_byName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, DataFormat> s_byId = new();
    private static int s_nextRegisteredId = 0xBFFF;

    public static readonly string Text = "Text";
    public static readonly string UnicodeText = "UnicodeText";
    public static readonly string Rtf = "Rich Text Format";
    public static readonly string Html = "HTML Format";
    public static readonly string FileDrop = "FileDrop";
    public static readonly string Bitmap = "Bitmap";
    public static readonly string Dib = "DeviceIndependentBitmap";
    public static readonly string Xaml = "Xaml";
    public static readonly string XamlPackage = "XamlPackage";
    public static readonly string Serializable = "PersistentObject";
    public static readonly string StringFormat = "System.String";
    public static readonly string Locale = "Locale";
    public static readonly string OemText = "OEMText";
    public static readonly string CommaSeparatedValue = "CSV";
    public static readonly string EnhancedMetafile = "EnhancedMetafile";
    public static readonly string MetafilePicture = "MetaFilePict";
    public static readonly string SymbolicLink = "SymbolicLink";
    public static readonly string Dif = "DataInterchangeFormat";
    public static readonly string Tiff = "TaggedImageFileFormat";
    public static readonly string Palette = "Palette";
    public static readonly string PenData = "PenData";
    public static readonly string Riff = "RiffAudio";
    public static readonly string WaveAudio = "WaveAudio";

    static DataFormats()
    {
        RegisterPredefined(Text, 1);
        RegisterPredefined(Bitmap, 2);
        RegisterPredefined(MetafilePicture, 3);
        RegisterPredefined(SymbolicLink, 4);
        RegisterPredefined(Dif, 5);
        RegisterPredefined(Tiff, 6);
        RegisterPredefined(OemText, 7);
        RegisterPredefined(Dib, 8);
        RegisterPredefined(Palette, 9);
        RegisterPredefined(PenData, 10);
        RegisterPredefined(Riff, 11);
        RegisterPredefined(WaveAudio, 12);
        RegisterPredefined(UnicodeText, 13);
        RegisterPredefined(EnhancedMetafile, 14);
        RegisterPredefined(FileDrop, 15);
        RegisterPredefined(Locale, 16);
    }

    public static DataFormat GetDataFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.Length == 0)
        {
            throw new ArgumentException("The data format cannot be empty.", nameof(format));
        }

        lock (s_syncRoot)
        {
            if (s_byName.TryGetValue(format, out DataFormat? existing))
            {
                return existing;
            }

            var created = new DataFormat(format, ++s_nextRegisteredId);
            s_byName.Add(format, created);
            s_byId.Add(created.Id, created);
            return created;
        }
    }

    public static DataFormat GetDataFormat(int id)
    {
        lock (s_syncRoot)
        {
            if (s_byId.TryGetValue(id, out DataFormat? existing))
            {
                return existing;
            }

            var created = new DataFormat($"Format{id}", id);
            s_byId.Add(id, created);
            s_byName.TryAdd(created.Name, created);
            return created;
        }
    }

    private static void RegisterPredefined(string name, int id)
    {
        var format = new DataFormat(name, id);
        s_byName.Add(name, format);
        s_byId.Add(id, format);
    }
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

    /// <summary>
    /// Platform hook that writes a Windows Shell drop description onto the native
    /// data object that originated the drag. Installed by the Windows OLE drop
    /// target; <see langword="null"/> for in-app drags (where it is a no-op).
    /// </summary>
    internal Action<DropImageType, string?, string?>? DropDescriptionSetter { get; set; }

    /// <summary>
    /// Sets the Windows Shell drop description that appears beside the drag image
    /// while the pointer is over this target. Call it from a <c>DragEnter</c> or
    /// <c>DragOver</c> handler to indicate what the drop will do (for example
    /// <c>SetDropDescription(DropImageType.Copy, "复制到 %1", "文档")</c>, where the
    /// Shell substitutes <c>%1</c> with <paramref name="insert"/>).
    /// </summary>
    /// <param name="type">The badge/glyph shown with the description.</param>
    /// <param name="message">
    /// The description text. May contain a single <c>%1</c> placeholder. When
    /// <see langword="null"/> or empty, the Shell shows its default text for
    /// <paramref name="type"/>.
    /// </param>
    /// <param name="insert">The text substituted for the <c>%1</c> placeholder.</param>
    /// <remarks>
    /// Only effective for drags that originate from an external OLE source such as
    /// Windows Explorer (the platform can only annotate a native data object).
    /// For a purely in-app drag this is a silent no-op.
    /// </remarks>
    public void SetDropDescription(DropImageType type, string? message = null, string? insert = null)
        => DropDescriptionSetter?.Invoke(type, message, insert);

    /// <summary>
    /// Clears any drop description previously set with <see cref="SetDropDescription"/>,
    /// restoring the Shell's default text for the current effect.
    /// </summary>
    public void ClearDropDescription()
        => DropDescriptionSetter?.Invoke(DropImageType.Invalid, null, null);

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate handler, object target)
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
    protected override void InvokeEventHandler(Delegate handler, object target)
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
    protected override void InvokeEventHandler(Delegate handler, object target)
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

    /// <summary>
    /// Identifies the DragImage attached property. When set on a drag source, the
    /// value is rendered as the drag visual that follows the pointer instead of an
    /// automatic clone of the source element. The value may be a
    /// <see cref="Jalium.UI.Media.ImageSource"/> (rendered as a bitmap) or a
    /// <see cref="FrameworkElement"/> that is not already in a visual tree.
    /// </summary>
    /// <remarks>
    /// A per-call image supplied to
    /// <see cref="DoDragDrop(DependencyObject, object, DragDropEffects, object)"/>
    /// takes precedence over this attached value.
    /// </remarks>
    public static readonly DependencyProperty DragImageProperty =
        DependencyProperty.RegisterAttached("DragImage", typeof(object), typeof(DragDrop),
            new PropertyMetadata(null));

    public static object? GetDragImage(DependencyObject element) =>
        element.GetValue(DragImageProperty);

    public static void SetDragImage(DependencyObject element, object? value) =>
        element.SetValue(DragImageProperty, value);

    /// <summary>
    /// Identifies the DragImageOffset attached property — the point, in
    /// device-independent pixels measured from the drag image's top-left corner,
    /// that sits directly under the pointer during the drag.
    /// </summary>
    public static readonly DependencyProperty DragImageOffsetProperty =
        DependencyProperty.RegisterAttached("DragImageOffset", typeof(Point), typeof(DragDrop),
            new PropertyMetadata(default(Point)));

    public static Point GetDragImageOffset(DependencyObject element) =>
        (Point)(element.GetValue(DragImageOffsetProperty) ?? default(Point));

    public static void SetDragImageOffset(DependencyObject element, Point value) =>
        element.SetValue(DragImageOffsetProperty, value);

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

    public static readonly RoutedEvent PreviewQueryContinueDragEvent =
        EventManager.RegisterRoutedEvent("PreviewQueryContinueDrag", RoutingStrategy.Tunnel, typeof(QueryContinueDragEventHandler), typeof(DragDrop));

    public static readonly RoutedEvent PreviewGiveFeedbackEvent =
        EventManager.RegisterRoutedEvent("PreviewGiveFeedback", RoutingStrategy.Tunnel, typeof(GiveFeedbackEventHandler), typeof(DragDrop));

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
    public static void AddPreviewGiveFeedbackHandler(DependencyObject element, GiveFeedbackEventHandler handler) =>
        AddHandler(element, PreviewGiveFeedbackEvent, handler);
    public static void RemovePreviewGiveFeedbackHandler(DependencyObject element, GiveFeedbackEventHandler handler) =>
        RemoveHandler(element, PreviewGiveFeedbackEvent, handler);
    public static void AddPreviewQueryContinueDragHandler(DependencyObject element, QueryContinueDragEventHandler handler) =>
        AddHandler(element, PreviewQueryContinueDragEvent, handler);
    public static void RemovePreviewQueryContinueDragHandler(DependencyObject element, QueryContinueDragEventHandler handler) =>
        RemoveHandler(element, PreviewQueryContinueDragEvent, handler);

    private static void AddHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not IInputElement inputElement)
        {
            throw new ArgumentException("The element must implement IInputElement.", nameof(element));
        }

        inputElement.AddHandler(routedEvent, handler);
    }

    private static void RemoveHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not IInputElement inputElement)
        {
            throw new ArgumentException("The element must implement IInputElement.", nameof(element));
        }

        inputElement.RemoveHandler(routedEvent, handler);
    }

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
    /// Platform hook for a real OLE drag <em>source</em> (Windows), enabling
    /// cross-process drag-out. Set by the hosting platform layer; null where absent.
    /// </summary>
    internal static Func<DependencyObject, IDataObject, DragDropEffects, DragDropEffects>? DoShellDragDropOverride { get; set; }

    /// <summary>
    /// The drag image supplied to the current <see cref="DoDragDrop(DependencyObject, object, DragDropEffects, object)"/>
    /// call, consumed by the platform drag layer while a drag is in progress.
    /// </summary>
    internal static object? PendingDragImage { get; private set; }

    /// <summary>The pointer hotspot for <see cref="PendingDragImage"/>, in DIPs.</summary>
    internal static Point PendingDragImageOffset { get; private set; }

    /// <summary>Whether <see cref="PendingDragImageOffset"/> was explicitly supplied.</summary>
    internal static bool HasPendingDragImageOffset { get; private set; }

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

    /// <summary>
    /// Initiates a drag-and-drop operation using a caller-supplied drag image.
    /// </summary>
    /// <param name="dragSource">The element that starts the drag.</param>
    /// <param name="data">The data to transfer.</param>
    /// <param name="allowedEffects">The effects the source permits.</param>
    /// <param name="dragImage">
    /// The drag visual to render under the pointer — a
    /// <see cref="Jalium.UI.Media.ImageSource"/> or an unparented
    /// <see cref="FrameworkElement"/>. The pointer sits at the image's top-left
    /// corner; use the offset overload to change the hotspot.
    /// </param>
    public static DragDropEffects DoDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects, object dragImage)
        => DoDragDropWithImage(dragSource, data, allowedEffects, dragImage, default, hasOffset: false);

    /// <summary>
    /// Initiates a drag-and-drop operation using a caller-supplied drag image and
    /// an explicit pointer hotspot within that image.
    /// </summary>
    /// <param name="dragSource">The element that starts the drag.</param>
    /// <param name="data">The data to transfer.</param>
    /// <param name="allowedEffects">The effects the source permits.</param>
    /// <param name="dragImage">
    /// The drag visual to render under the pointer — a
    /// <see cref="Jalium.UI.Media.ImageSource"/> or an unparented
    /// <see cref="FrameworkElement"/>.
    /// </param>
    /// <param name="imageOffset">
    /// The point within <paramref name="dragImage"/> (in DIPs, from its top-left
    /// corner) that stays under the pointer.
    /// </param>
    public static DragDropEffects DoDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects, object dragImage, Point imageOffset)
        => DoDragDropWithImage(dragSource, data, allowedEffects, dragImage, imageOffset, hasOffset: true);

    private static DragDropEffects DoDragDropWithImage(DependencyObject dragSource, object data, DragDropEffects allowedEffects, object? dragImage, Point imageOffset, bool hasOffset)
    {
        PendingDragImage = dragImage;
        PendingDragImageOffset = imageOffset;
        HasPendingDragImageOffset = hasOffset;
        try
        {
            return DoDragDrop(dragSource, data, allowedEffects);
        }
        finally
        {
            PendingDragImage = null;
            PendingDragImageOffset = default;
            HasPendingDragImageOffset = false;
        }
    }

    /// <summary>
    /// Initiates a real Windows Shell drag that can be dropped onto <em>other</em>
    /// applications (e.g. copying files to Explorer), with the system drag image.
    /// Unlike <see cref="DoDragDrop(DependencyObject, object, DragDropEffects)"/> — which
    /// composites an in-app visual and never leaves the window — this hands the payload
    /// to the OS drag loop via a real OLE <c>IDataObject</c>/<c>IDropSource</c>. Drops
    /// back onto this app still raise the normal drag events through the window's OLE
    /// drop target. Falls back to the in-app managed drag when no Shell source is
    /// available (non-Windows).
    /// </summary>
    /// <param name="dragSource">The element that starts the drag.</param>
    /// <param name="data">
    /// The payload. Text and a file-path <see cref="string"/>[] (under
    /// <see cref="DataFormats.FileDrop"/>) are marshaled to the Shell; a bare
    /// <see cref="string"/> or <see cref="string"/>[] is wrapped automatically.
    /// </param>
    /// <param name="allowedEffects">The effects the source permits.</param>
    /// <param name="dragImage">
    /// Optional Shell drag image — a <see cref="Jalium.UI.Media.ImageSource"/> the Shell
    /// renders under the pointer (its pixels are premultiplied and handed to
    /// <c>IDragSourceHelper</c>).
    /// </param>
    /// <param name="imageOffset">
    /// The pointer hotspot within <paramref name="dragImage"/>, in <em>pixels</em> from
    /// its top-left corner.
    /// </param>
    public static DragDropEffects DoShellDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects, object? dragImage = null, Point imageOffset = default)
    {
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(data);

        if (_isDragging)
            return DragDropEffects.None;

        var dataObj = data as IDataObject ?? new DataObject(data);
        PendingDragImage = dragImage;
        PendingDragImageOffset = imageOffset;
        HasPendingDragImageOffset = dragImage != null;
        _isDragging = true;
        try
        {
            var shell = DoShellDragDropOverride;
            if (shell != null)
                return shell(dragSource, dataObj, allowedEffects);

            // No Shell source (e.g. non-Windows): fall back to the in-app managed drag.
            return DoDragDropOverride?.Invoke(dragSource, dataObj, allowedEffects) ?? DragDropEffects.None;
        }
        finally
        {
            _isDragging = false;
            PendingDragImage = null;
            PendingDragImageOffset = default;
            HasPendingDragImageOffset = false;
        }
    }

    #endregion
}
