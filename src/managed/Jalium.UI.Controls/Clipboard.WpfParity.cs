using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Text;
using Jalium.UI.Media;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using LegacyClipboard = Jalium.UI.Controls.Clipboard;
using LegacyDataFormats = Jalium.UI.Controls.DataFormats;

namespace Jalium.UI;

/// <summary>
/// Provides the WPF-compatible clipboard surface in the canonical root namespace.
/// Platform-native text, file, bitmap and binary formats are delegated to the existing
/// desktop backend; typed and JSON values retain their real <see cref="IDataObject"/>
/// representation for lossless in-process round trips.
/// </summary>
public static class Clipboard
{
    private static readonly object s_gate = new();
    private static IDataObject? s_currentDataObject;
    private static BitmapSource? s_currentImage;
    private static byte[]? s_currentAudio;

    /// <summary>Removes all data from the clipboard.</summary>
    public static void Clear()
    {
        lock (s_gate)
        {
            s_currentDataObject = null;
            s_currentImage = null;
            s_currentAudio = null;
        }

        _ = LegacyClipboard.Clear();
    }

    public static bool ContainsAudio()
    {
        lock (s_gate)
        {
            if (s_currentAudio is { Length: > 0 })
            {
                return true;
            }
        }

        return LegacyClipboard.ContainsDataFormat(DataFormats.WaveAudio);
    }

    public static bool ContainsData(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        lock (s_gate)
        {
            if (s_currentDataObject?.GetDataPresent(format, autoConvert: true) == true)
            {
                return true;
            }
        }

        return format switch
        {
            var value when IsPlainTextFormat(value) => LegacyClipboard.ContainsText(),
            var value when value == DataFormats.FileDrop => LegacyClipboard.ContainsFileDropList(),
            var value when value == DataFormats.Bitmap || value == DataFormats.Dib => LegacyClipboard.ContainsImage(),
            _ => LegacyClipboard.ContainsDataFormat(format),
        };
    }

    public static bool ContainsFileDropList() =>
        ContainsData(DataFormats.FileDrop) || LegacyClipboard.ContainsFileDropList();

    public static bool ContainsImage()
    {
        lock (s_gate)
        {
            if (s_currentImage != null)
            {
                return true;
            }
        }

        return LegacyClipboard.ContainsImage();
    }

    public static bool ContainsText() =>
        ContainsData(DataFormats.UnicodeText) || ContainsData(DataFormats.Text);

    public static bool ContainsText(TextDataFormat format)
    {
        ValidateTextDataFormat(format);
        return ContainsData(GetTextFormatName(format));
    }

    /// <summary>
    /// Ensures clipboard data is owned independently of the source object. Native writes are
    /// already immediate, and managed values are snapshotted when set, so no extra work is needed.
    /// </summary>
    public static void Flush()
    {
    }

    public static Stream? GetAudioStream()
    {
        byte[]? audio;
        lock (s_gate)
        {
            audio = s_currentAudio?.ToArray();
        }

        audio ??= LegacyClipboard.GetBinaryData(DataFormats.WaveAudio);
        return audio == null ? null : new MemoryStream(audio, writable: false);
    }

    public static object? GetData(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        lock (s_gate)
        {
            if (s_currentDataObject?.GetDataPresent(format, autoConvert: true) == true)
            {
                return s_currentDataObject.GetData(format, autoConvert: true);
            }
        }

        if (IsPlainTextFormat(format))
        {
            return LegacyClipboard.GetText() ?? string.Empty;
        }

        if (format == DataFormats.FileDrop)
        {
            return GetFileDropList();
        }

        if (format == DataFormats.Bitmap || format == DataFormats.Dib)
        {
            return GetImage();
        }

        if (format == DataFormats.WaveAudio)
        {
            return GetAudioStream();
        }

        var bytes = LegacyClipboard.GetBinaryData(format);
        if (bytes == null)
        {
            return null;
        }

        if (IsEncodedTextFormat(format))
        {
            var count = Array.IndexOf(bytes, (byte)0);
            return Encoding.UTF8.GetString(bytes, 0, count < 0 ? bytes.Length : count);
        }

        return bytes;
    }

    public static IDataObject? GetDataObject()
    {
        lock (s_gate)
        {
            if (s_currentDataObject != null)
            {
                return s_currentDataObject;
            }
        }

        var snapshot = new DataObject();
        var hasData = false;

        if (LegacyClipboard.ContainsText())
        {
            var text = LegacyClipboard.GetText() ?? string.Empty;
            snapshot.SetData(DataFormats.UnicodeText, text);
            snapshot.SetData(DataFormats.Text, text);
            hasData = true;
        }

        if (LegacyClipboard.ContainsFileDropList())
        {
            snapshot.SetData(DataFormats.FileDrop, GetFileDropList());
            hasData = true;
        }

        if (LegacyClipboard.ContainsImage() && GetImage() is { } image)
        {
            snapshot.SetData(DataFormats.Bitmap, image);
            hasData = true;
        }

        return hasData ? snapshot : null;
    }

    public static StringCollection GetFileDropList()
    {
        var collection = new StringCollection();
        var files = LegacyClipboard.GetFileDropList();
        if (files is { Length: > 0 })
        {
            collection.AddRange(files);
        }
        else
        {
            lock (s_gate)
            {
                if (s_currentDataObject?.GetData(DataFormats.FileDrop, autoConvert: true) is StringCollection current)
                {
                    collection.AddRange(current.Cast<string>().ToArray());
                }
                else if (s_currentDataObject?.GetData(DataFormats.FileDrop, autoConvert: true) is string[] paths)
                {
                    collection.AddRange(paths);
                }
            }
        }

        return collection;
    }

    public static BitmapSource? GetImage()
    {
        lock (s_gate)
        {
            if (s_currentImage != null)
            {
                return s_currentImage;
            }
        }

        var raw = LegacyClipboard.GetImage();
        if (raw == null)
        {
            return null;
        }

        var (width, height, stride, data) = raw.Value;
        var pixels = ConvertClipboardPixelsToBgra(width, height, stride, data);
        return new ClipboardBitmapSource(width, height, pixels);
    }

    public static string GetText() => LegacyClipboard.GetText() ?? GetTextFromSnapshot() ?? string.Empty;

    public static string GetText(TextDataFormat format)
    {
        ValidateTextDataFormat(format);
        return GetData(GetTextFormatName(format)) as string ?? string.Empty;
    }

    public static bool IsCurrent(IDataObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (s_gate)
        {
            return ReferenceEquals(s_currentDataObject, data);
        }
    }

    public static void SetAudio(byte[] audioBytes)
    {
        ArgumentNullException.ThrowIfNull(audioBytes);
        SetAudioCore(audioBytes.ToArray());
    }

    public static void SetAudio(Stream audioStream)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        using var memory = new MemoryStream();
        audioStream.CopyTo(memory);
        SetAudioCore(memory.ToArray());
    }

    public static void SetData(string format, object data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(data);

        var dataObject = new DataObject();
        dataObject.SetData(format, data);
        ReplaceSnapshot(dataObject, data as BitmapSource, ExtractAudio(format, data));
        PublishSingleFormat(format, data);
    }

    [RequiresUnreferencedCode("JSON clipboard serialization uses runtime type metadata.")]
    [RequiresDynamicCode("JSON clipboard serialization may require runtime code generation.")]
    public static void SetDataAsJson<T>(string format, T data)
    {
        var dataObject = new DataObject();
        dataObject.SetDataAsJson(format, data);
        ReplaceSnapshot(dataObject, image: null, audio: null);
    }

    public static void SetDataObject(object data)
    {
        SetDataObject(data, copy: false);
    }

    public static void SetDataObject(object data, bool copy)
    {
        ArgumentNullException.ThrowIfNull(data);

        IDataObject dataObject = data as IDataObject ?? new DataObject(data);
        var image = FindImage(dataObject, data);
        var audio = FindAudio(dataObject);
        ReplaceSnapshot(dataObject, image, audio);
        PublishDataObject(dataObject, data, image, audio);
    }

    public static void SetFileDropList(StringCollection fileDropList)
    {
        ArgumentNullException.ThrowIfNull(fileDropList);
        var files = fileDropList.Cast<string>().ToArray();
        if (files.Length == 0 || files.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The file drop list must contain at least one non-empty path.", nameof(fileDropList));
        }

        var snapshot = new DataObject();
        var copied = new StringCollection();
        copied.AddRange(files);
        snapshot.SetData(DataFormats.FileDrop, copied);
        ReplaceSnapshot(snapshot, image: null, audio: null);
        _ = LegacyClipboard.SetFileDropList(files);
    }

    public static void SetImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var snapshot = new DataObject();
        snapshot.SetData(DataFormats.Bitmap, image);
        ReplaceSnapshot(snapshot, image, audio: null);
        PublishImage(image);
    }

    public static void SetText(string text)
    {
        SetText(text, TextDataFormat.UnicodeText);
    }

    public static void SetText(string text, TextDataFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateTextDataFormat(format);

        var formatName = GetTextFormatName(format);
        var snapshot = new DataObject();
        snapshot.SetData(formatName, text);
        ReplaceSnapshot(snapshot, image: null, audio: null);

        if (IsPlainTextFormat(formatName))
        {
            _ = LegacyClipboard.SetText(text);
        }
        else
        {
            PublishSingleFormat(formatName, text);
        }
    }

    public static bool TryGetData<T>(string format, [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        IDataObject? dataObject = GetDataObject();
        if (dataObject is ITypedDataObject typed && typed.TryGetData(format, out data))
        {
            return true;
        }

        if (GetData(format) is T value)
        {
            data = value;
            return true;
        }

        data = default;
        return false;
    }

#pragma warning disable CS3021 // Attribute is part of the .NET 10 WPF contract even though this assembly has no global CLS declaration.
    [CLSCompliant(false)]
    public static bool TryGetData<T>(
        string format,
        Func<TypeName, Type?> resolver,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(resolver);
        IDataObject? dataObject = GetDataObject();
        if (dataObject is ITypedDataObject typed &&
            typed.TryGetData(format, resolver, autoConvert: true, out data))
        {
            return true;
        }

        return TryGetData(format, out data);
    }
#pragma warning restore CS3021

    internal static string BuildCfHtml(string fragment) => LegacyClipboard.BuildCfHtml(fragment);

    private static void SetAudioCore(byte[] audio)
    {
        var snapshot = new DataObject();
        snapshot.SetData(DataFormats.WaveAudio, audio.ToArray());
        ReplaceSnapshot(snapshot, image: null, audio);
        _ = LegacyClipboard.SetBinaryData(DataFormats.WaveAudio, audio);
    }

    private static void ReplaceSnapshot(IDataObject dataObject, BitmapSource? image, byte[]? audio)
    {
        lock (s_gate)
        {
            s_currentDataObject = dataObject;
            s_currentImage = image;
            s_currentAudio = audio?.ToArray();
        }
    }

    private static void PublishDataObject(
        IDataObject dataObject,
        object original,
        BitmapSource? image,
        byte[]? audio)
    {
        if (original is string text)
        {
            _ = LegacyClipboard.SetText(text);
            return;
        }

        if (original is StringCollection collection)
        {
            _ = LegacyClipboard.SetFileDropList(collection.Cast<string>().ToArray());
            return;
        }

        if (original is string[] files)
        {
            _ = LegacyClipboard.SetFileDropList(files);
            return;
        }

        var legacy = new Controls.ClipboardDataObject();
        var hasLegacyData = CopyLegacyFormat(dataObject, DataFormats.Text, LegacyDataFormats.Text, legacy)
            | CopyLegacyFormat(dataObject, DataFormats.UnicodeText, LegacyDataFormats.UnicodeText, legacy)
            | CopyLegacyFormat(dataObject, DataFormats.Rtf, LegacyDataFormats.Rtf, legacy)
            | CopyLegacyFormat(dataObject, DataFormats.Html, LegacyDataFormats.Html, legacy)
            | CopyFileDropFormat(dataObject, legacy);

        if (hasLegacyData)
        {
            LegacyClipboard.SetDataObject(legacy);
            return;
        }

        if (image != null)
        {
            PublishImage(image);
            return;
        }

        if (audio != null)
        {
            _ = LegacyClipboard.SetBinaryData(DataFormats.WaveAudio, audio);
        }
    }

    private static bool CopyLegacyFormat(
        IDataObject source,
        string sourceFormat,
        string targetFormat,
        Controls.ClipboardDataObject target)
    {
        if (!source.GetDataPresent(sourceFormat, autoConvert: true))
        {
            return false;
        }

        target.SetData(targetFormat, source.GetData(sourceFormat, autoConvert: true));
        return true;
    }

    private static bool CopyFileDropFormat(IDataObject source, Controls.ClipboardDataObject target)
    {
        if (!source.GetDataPresent(DataFormats.FileDrop, autoConvert: true))
        {
            return false;
        }

        var value = source.GetData(DataFormats.FileDrop, autoConvert: true);
        if (value is StringCollection collection)
        {
            value = collection.Cast<string>().ToArray();
        }

        target.SetData(LegacyDataFormats.FileDrop, value);
        return true;
    }

    private static void PublishSingleFormat(string format, object data)
    {
        if (data is byte[] bytes)
        {
            _ = LegacyClipboard.SetBinaryData(format, bytes);
            return;
        }

        if (data is string text)
        {
            if (IsPlainTextFormat(format))
            {
                _ = LegacyClipboard.SetText(text);
            }
            else
            {
                _ = LegacyClipboard.SetBinaryData(format, Encoding.UTF8.GetBytes(text + '\0'));
            }
        }
    }

    private static void PublishImage(BitmapSource image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bytesPerPixel = image.Format == PixelFormat.Bgr24 || image.Format == PixelFormat.Rgb24
            ? 3
            : image.Format == PixelFormat.Gray8
                ? 1
                : image.Format == PixelFormat.Gray16
                    ? 2
                    : 4;
        var sourceStride = checked(width * bytesPerPixel);
        var source = new byte[checked(sourceStride * height)];
        image.CopyPixels(source, sourceStride, 0);

        var bgra = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = (y * sourceStride) + (x * bytesPerPixel);
                var targetOffset = ((y * width) + x) * 4;
                if (image.Format == PixelFormat.Rgba32)
                {
                    bgra[targetOffset] = source[sourceOffset + 2];
                    bgra[targetOffset + 1] = source[sourceOffset + 1];
                    bgra[targetOffset + 2] = source[sourceOffset];
                    bgra[targetOffset + 3] = source[sourceOffset + 3];
                }
                else if (image.Format == PixelFormat.Bgr24)
                {
                    bgra[targetOffset] = source[sourceOffset];
                    bgra[targetOffset + 1] = source[sourceOffset + 1];
                    bgra[targetOffset + 2] = source[sourceOffset + 2];
                    bgra[targetOffset + 3] = 255;
                }
                else if (image.Format == PixelFormat.Rgb24)
                {
                    bgra[targetOffset] = source[sourceOffset + 2];
                    bgra[targetOffset + 1] = source[sourceOffset + 1];
                    bgra[targetOffset + 2] = source[sourceOffset];
                    bgra[targetOffset + 3] = 255;
                }
                else if (image.Format == PixelFormat.Gray8)
                {
                    bgra[targetOffset] = bgra[targetOffset + 1] = bgra[targetOffset + 2] = source[sourceOffset];
                    bgra[targetOffset + 3] = 255;
                }
                else if (image.Format == PixelFormat.Gray16)
                {
                    var gray = source[sourceOffset + 1];
                    bgra[targetOffset] = bgra[targetOffset + 1] = bgra[targetOffset + 2] = gray;
                    bgra[targetOffset + 3] = 255;
                }
                else
                {
                    source.AsSpan(sourceOffset, 4).CopyTo(bgra.AsSpan(targetOffset, 4));
                }
            }
        }

        _ = LegacyClipboard.SetImagePixels(width, height, width * 4, bgra);
    }

    private static byte[] ConvertClipboardPixelsToBgra(int width, int height, int stride, byte[] source)
    {
        var target = new byte[checked(width * height * 4)];
        var appears32Bit = stride >= width * 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = (y * stride) + (x * (appears32Bit ? 4 : 3));
                var targetOffset = ((y * width) + x) * 4;
                target[targetOffset] = source[sourceOffset];
                target[targetOffset + 1] = source[sourceOffset + 1];
                target[targetOffset + 2] = source[sourceOffset + 2];
                target[targetOffset + 3] = appears32Bit ? source[sourceOffset + 3] : (byte)255;
            }
        }

        return target;
    }

    private static BitmapSource? FindImage(IDataObject dataObject, object original)
    {
        if (original is BitmapSource image)
        {
            return image;
        }

        return dataObject.GetData(DataFormats.Bitmap, autoConvert: true) as BitmapSource;
    }

    private static byte[]? FindAudio(IDataObject dataObject)
    {
        return dataObject.GetData(DataFormats.WaveAudio, autoConvert: true) switch
        {
            byte[] bytes => bytes.ToArray(),
            MemoryStream stream => stream.ToArray(),
            Stream stream => ReadRemainingBytes(stream),
            _ => null,
        };
    }

    private static byte[]? ExtractAudio(string format, object data) =>
        format == DataFormats.WaveAudio
            ? data switch
            {
                byte[] bytes => bytes.ToArray(),
                MemoryStream stream => stream.ToArray(),
                Stream stream => ReadRemainingBytes(stream),
                _ => null,
            }
            : null;

    private static byte[] ReadRemainingBytes(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? GetTextFromSnapshot()
    {
        lock (s_gate)
        {
            return s_currentDataObject?.GetData(DataFormats.UnicodeText, autoConvert: true) as string
                ?? s_currentDataObject?.GetData(DataFormats.Text, autoConvert: true) as string;
        }
    }

    private static bool IsPlainTextFormat(string format) =>
        format == DataFormats.Text || format == DataFormats.UnicodeText || format == DataFormats.StringFormat;

    private static bool IsEncodedTextFormat(string format) =>
        format == DataFormats.Rtf ||
        format == DataFormats.Html ||
        format == DataFormats.CommaSeparatedValue ||
        format == DataFormats.Xaml;

    private static string GetTextFormatName(TextDataFormat format) => format switch
    {
        TextDataFormat.Text => DataFormats.Text,
        TextDataFormat.UnicodeText => DataFormats.UnicodeText,
        TextDataFormat.Rtf => DataFormats.Rtf,
        TextDataFormat.Html => DataFormats.Html,
        TextDataFormat.CommaSeparatedValue => DataFormats.CommaSeparatedValue,
        TextDataFormat.Xaml => DataFormats.Xaml,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static void ValidateTextDataFormat(TextDataFormat format)
    {
        if (!Enum.IsDefined(format))
        {
            throw new System.ComponentModel.InvalidEnumArgumentException(
                nameof(format),
                (int)format,
                typeof(TextDataFormat));
        }
    }

    private sealed class ClipboardBitmapSource : BitmapSource
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _pixels;

        public ClipboardBitmapSource(int width, int height, byte[] pixels)
        {
            _width = width;
            _height = height;
            _pixels = pixels;
        }

        public override double Width => _width;
        public override double Height => _height;
        public override nint NativeHandle => nint.Zero;
        public override PixelFormat Format => PixelFormat.Bgra32;

        public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            var rect = sourceRect.IsEmpty ? new Int32Rect(0, 0, _width, _height) : sourceRect;
            if (rect.X < 0 || rect.Y < 0 || rect.Width < 0 || rect.Height < 0 ||
                rect.X + rect.Width > _width || rect.Y + rect.Height > _height)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceRect));
            }

            var rowBytes = checked(rect.Width * 4);
            if (stride < rowBytes || offset < 0 || pixels.Length < offset + checked(stride * rect.Height))
            {
                throw new ArgumentException("The destination buffer is too small.", nameof(pixels));
            }

            for (var y = 0; y < rect.Height; y++)
            {
                _pixels.AsSpan((((rect.Y + y) * _width) + rect.X) * 4, rowBytes)
                    .CopyTo(pixels.AsSpan(offset + (y * stride), rowBytes));
            }
        }
    }
}
