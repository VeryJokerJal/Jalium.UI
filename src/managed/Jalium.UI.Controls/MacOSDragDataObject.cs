using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

// A pasteboard item is valid only during its target visit. Read lazily while
// tracking, then retain every representation before application Drop handlers.
internal sealed class MacOSDragDataObject : IDataObject
{
    private readonly DataObject _formats = new();
    private readonly DataObject _values = new();
    private readonly string[] _mimeTypes;
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, byte[]?>? _reader;
    private static SourceScope? s_source;

    internal MacOSDragDataObject(IEnumerable<string> mimeTypes, Func<string, byte[]?> reader)
    {
        _mimeTypes = mimeTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _reader = reader;
        foreach (string mime in _mimeTypes)
        {
            _formats.SetData(mime, Array.Empty<byte>(), autoConvert: false);
            if (ClipboardPlatform.GetFormatForMimeType(mime) is { } format)
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

    private void Read(string mime)
    {
        if (_reader == null || !_read.Add(mime)) return;
        if (_reader(mime) is not { } bytes) return;
        _values.SetData(mime, bytes, autoConvert: false);
        if (ClipboardPlatform.GetFormatForMimeType(mime) is { } format &&
            !_values.GetDataPresent(format, autoConvert: false))
            _values.SetData(format, ClipboardPlatform.DecodeCrossPlatformRepresentation(format, bytes), autoConvert: true);
    }

    internal void Snapshot()
    {
        foreach (string mime in _mimeTypes) Read(mime);
        Detach();
    }

    internal void Detach() => _reader = null;

    public object? GetData(string format) => GetData(format, true);
    public object? GetData(Type format) => GetData(format.FullName ?? format.Name);
    public object? GetData(string format, bool autoConvert)
    {
        if (!_values.GetDataPresent(format, autoConvert))
        {
            foreach (string mime in _mimeTypes)
            {
                if (mime.Equals(format, StringComparison.OrdinalIgnoreCase) ||
                    ClipboardPlatform.MimeTypeMapsToFormat(mime, format))
                {
                    Read(mime);
                    if (_values.GetDataPresent(format, autoConvert)) break;
                }
            }
        }
        return _values.GetData(format, autoConvert);
    }
    public bool GetDataPresent(string format) => GetDataPresent(format, true);
    public bool GetDataPresent(Type format) => GetDataPresent(format.FullName ?? format.Name);
    public bool GetDataPresent(string format, bool autoConvert) => _formats.GetDataPresent(format, autoConvert);
    public string[] GetFormats() => GetFormats(true);
    public string[] GetFormats(bool autoConvert) => _formats.GetFormats(autoConvert);
    public void SetData(object data) => SetData(data.GetType(), data);
    public void SetData(Type format, object data) => SetData(format.FullName ?? format.Name, data);
    public void SetData(string format, object data) => SetData(format, data, true);
    public void SetData(string format, object data, bool autoConvert)
    {
        _formats.SetData(format, data, autoConvert);
        _values.SetData(format, data, autoConvert);
    }
}
