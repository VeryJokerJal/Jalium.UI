using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

// A pasteboard item is valid only during its target visit. Read lazily while
// tracking, then retain bounded representations before application Drop handlers.
internal sealed class MacOSDragDataObject : IDataObject
{
    private readonly DataObject _formats = new();
    private readonly DataObject _values = new();
    private readonly string[] _mimeTypes;
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);
    // Bound both cached payloads and work done for an untrusted format list.
    internal const int MaxSnapshotBytes = 256 * 1024 * 1024;
    internal const int MaxFormats = 64;
    internal const int MaxFormatEntries = 1024;
    private int _remainingBytes;
    private Func<string, int, byte[]?>? _reader;
    private static SourceScope? s_source;

    internal MacOSDragDataObject(IEnumerable<string> mimeTypes, Func<string, byte[]?> reader)
        : this(mimeTypes, (mime, _) => reader(mime)) { }

    internal MacOSDragDataObject(IEnumerable<string> mimeTypes, Func<string, int, byte[]?> reader,
        int maxBytes = MaxSnapshotBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _remainingBytes = Math.Min(maxBytes, MaxSnapshotBytes);
        _mimeTypes = mimeTypes.Take(MaxFormatEntries)
            .Where(mime => !string.IsNullOrWhiteSpace(mime) && mime.Length <= 1024)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxFormats).ToArray();
        _reader = reader;
        foreach (string mime in _mimeTypes)
        {
            _formats.SetData(mime, Array.Empty<byte>(), autoConvert: false);
            if (ClipboardPlatform.GetFormatForMimeType(mime) is { } format && format != DataFormats.FileDrop)
                _formats.SetData(format, Array.Empty<byte>(), autoConvert: true);
        }
    }

    internal static IDisposable? BeginSource(Window window, IDataObject data)
    {
        if (s_source != null || window.Handle == 0) return null;
        return s_source = new SourceScope(window.Handle, data);
    }

    internal static IDataObject? GetSourceData(nint sourceHandle) =>
        sourceHandle != 0 && s_source is { } source && source.Handle == sourceHandle ? source.Data : null;

    private sealed class SourceScope(nint handle, IDataObject data) : IDisposable
    {
        internal nint Handle { get; } = handle;
        internal IDataObject Data { get; } = data;
        public void Dispose()
        {
            if (!ReferenceEquals(s_source, this)) return;
            s_source = null;
            NativeDropTarget.RevokeSource(Handle);
        }
    }

    private void Read(string mime, bool decode = true)
    {
        if (_reader != null && _remainingBytes > 0 && _read.Add(mime))
        {
            byte[]? bytes;
            try { bytes = _reader(mime, _remainingBytes); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // One unavailable representation must not discard the rest of a drop.
                return;
            }
            if (bytes == null || bytes.Length > _remainingBytes) return;
            _remainingBytes -= bytes.Length;
            _values.SetData(mime, bytes, autoConvert: false);
        }
        // Snapshot keeps encoded bytes only. In particular, do not inflate every
        // advertised image before the application asks for a converted value.
        if (!decode || _values.GetData(mime, autoConvert: false) is not byte[] cached) return;
        if (ClipboardPlatform.GetFormatForMimeType(mime) is { } format &&
            !_values.GetDataPresent(format, autoConvert: false))
        {
            object value = ClipboardPlatform.DecodeCrossPlatformRepresentation(format, cached);
            // A URI list may contain only web links. Advertise the file alias
            // only after its representation provides at least one file path.
            if (format == DataFormats.FileDrop)
            {
                if (value is not string[] { Length: > 0 }) return;
                _formats.SetData(format, value, autoConvert: true);
            }
            _values.SetData(format, value, autoConvert: true);
        }
    }

    private void EnsureFileDrop()
    {
        if (_formats.GetDataPresent(DataFormats.FileDrop, autoConvert: false)) return;
        foreach (string mime in _mimeTypes)
        {
            if (!ClipboardPlatform.MimeTypeMapsToFormat(mime, DataFormats.FileDrop)) continue;
            Read(mime);
            if (_formats.GetDataPresent(DataFormats.FileDrop, autoConvert: false)) break;
        }
    }

    internal void Snapshot()
    {
        try { foreach (string mime in _mimeTypes) Read(mime, decode: false); }
        finally { Detach(); }
    }

    internal void Detach() => _reader = null;

    public object? GetData(string format) => GetData(format, true);
    public object? GetData(Type format) => GetData(format.FullName ?? format.Name);
    public object? GetData(string format, bool autoConvert)
    {
        if (IsFileFormat(format, autoConvert)) EnsureFileDrop();
        if (!_values.GetDataPresent(format, autoConvert))
        {
            foreach (string mime in _mimeTypes)
            {
                if (mime.Equals(format, StringComparison.OrdinalIgnoreCase) ||
                    ClipboardPlatform.MimeTypeMapsToFormat(mime, format))
                {
                    Read(mime, decode: !mime.Equals(format, StringComparison.OrdinalIgnoreCase));
                    if (_values.GetDataPresent(format, autoConvert)) break;
                }
            }
        }
        return _values.GetData(format, autoConvert);
    }
    public bool GetDataPresent(string format) => GetDataPresent(format, true);
    public bool GetDataPresent(Type format) => GetDataPresent(format.FullName ?? format.Name);
    public bool GetDataPresent(string format, bool autoConvert)
    {
        if (IsFileFormat(format, autoConvert)) EnsureFileDrop();
        return _formats.GetDataPresent(format, autoConvert);
    }
    private static bool IsFileFormat(string format, bool autoConvert) =>
        string.Equals(format, DataFormats.FileDrop, StringComparison.OrdinalIgnoreCase) ||
        autoConvert && (string.Equals(format, "FileName", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(format, "FileNameW", StringComparison.OrdinalIgnoreCase));
    public string[] GetFormats() => GetFormats(true);
    public string[] GetFormats(bool autoConvert)
    {
        EnsureFileDrop();
        return _formats.GetFormats(autoConvert);
    }
    public void SetData(object data) => SetData(data.GetType(), data);
    public void SetData(Type format, object data) => SetData(format.FullName ?? format.Name, data);
    public void SetData(string format, object data) => SetData(format, data, true);
    public void SetData(string format, object data, bool autoConvert)
    {
        _formats.SetData(format, data, autoConvert);
        _values.SetData(format, data, autoConvert);
    }
}
