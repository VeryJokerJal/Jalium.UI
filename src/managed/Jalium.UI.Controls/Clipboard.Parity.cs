using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Controls.Platform;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;

namespace Jalium.UI;

/// <summary>
/// Provides the WPF-compatible public surface for transferring data to and
/// from the system clipboard. Platform details are isolated behind an internal
/// provider and are never exposed as a second Clipboard API.
/// </summary>
/// <remarks>
/// Windows access requires an STA thread, matching WPF. Linux exposes native
/// X11/Wayland MIME formats, but persistence after process exit depends on an
/// external clipboard manager because the current native ABI has no
/// SAVE_TARGETS handoff. Android currently supports text only. Other platforms
/// throw <see cref="PlatformNotSupportedException"/>.
/// </remarks>
public static class Clipboard
{
    private static IClipboardProvider Provider => ClipboardProvider.Current;

    public static void Clear()
    {
        VerifyAccess();
        ThrowOnFailure(Provider.Clear(), nameof(Clear));
    }

    public static bool ContainsAudio() => ContainsDataInternal(DataFormats.WaveAudio);

    public static bool ContainsData(string format)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);
        return ContainsDataInternal(format);
    }

    public static bool ContainsFileDropList() => ContainsDataInternal(DataFormats.FileDrop);

    public static bool ContainsImage() => ContainsDataInternal(DataFormats.Bitmap);

    public static bool ContainsText() => ContainsDataInternal(DataFormats.UnicodeText);

    public static bool ContainsText(TextDataFormat format)
    {
        ValidateTextDataFormat(format);
        return ContainsDataInternal(GetTextFormatName(format));
    }

    /// <summary>
    /// Permanently renders the current clipboard data when the platform can
    /// transfer ownership independently of this process.
    /// </summary>
    /// <remarks>
    /// On Linux the current X11/Wayland provider retains ownership only for the
    /// process lifetime unless the desktop clipboard manager persists it.
    /// </remarks>
    public static void Flush()
    {
        VerifyAccess();
        ThrowOnFailure(Provider.Flush(), nameof(Flush));
    }

    public static Stream? GetAudioStream() =>
        GetTypedDataIfAvailable<Stream>(DataFormats.WaveAudio);

    public static object? GetData(string format)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);
        return GetDataInternal(format);
    }

    public static IDataObject? GetDataObject()
    {
        VerifyAccess();
        return Provider.GetDataObject();
    }

    public static StringCollection GetFileDropList()
    {
        var result = new StringCollection();
        object? value = GetDataInternal(DataFormats.FileDrop);
        switch (value)
        {
            case string[] files:
                result.AddRange(files);
                break;
            case StringCollection collection:
                result.AddRange(collection.Cast<string>().ToArray());
                break;
        }

        return result;
    }

    public static BitmapSource? GetImage() =>
        GetTypedDataIfAvailable<BitmapSource>(DataFormats.Bitmap);

    public static string GetText() => GetText(TextDataFormat.UnicodeText);

    public static string GetText(TextDataFormat format)
    {
        ValidateTextDataFormat(format);
        return GetTypedDataIfAvailable<string>(GetTextFormatName(format)) ?? string.Empty;
    }

    public static bool IsCurrent(IDataObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        VerifyAccess();
        return Provider.IsCurrent(data);
    }

    public static void SetAudio(byte[] audioBytes)
    {
        ArgumentNullException.ThrowIfNull(audioBytes);
        SetAudio(new MemoryStream(audioBytes, writable: false));
    }

    public static void SetAudio(Stream audioStream)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        SetDataInternal(DataFormats.WaveAudio, audioStream);
    }

    public static void SetData(string format, object data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(data);
        SetDataInternal(format, data);
    }

    public static void SetDataAsJson<T>(string format, T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var dataObject = new DataObject();
#pragma warning disable IL2026, IL3050
        dataObject.SetDataAsJson(format, data);
#pragma warning restore IL2026, IL3050
        SetDataObject(dataObject, copy: true);
    }

    public static void SetDataObject(object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        SetDataObject(data, copy: false);
    }

    public static void SetDataObject(object data, bool copy)
    {
        ArgumentNullException.ThrowIfNull(data);
        VerifyAccess();

        IDataObject dataObject = data as IDataObject ?? new DataObject(data);
        ThrowOnFailure(Provider.SetDataObject(dataObject, copy), nameof(SetDataObject));
    }

    public static void SetFileDropList(StringCollection fileDropList)
    {
        ArgumentNullException.ThrowIfNull(fileDropList);
        if (fileDropList.Count == 0)
            throw new ArgumentException("The file drop list cannot be empty.", nameof(fileDropList));

        var files = new string[fileDropList.Count];
        fileDropList.CopyTo(files, 0);
        for (int index = 0; index < files.Length; index++)
        {
            string? file = files[index];
            if (string.IsNullOrEmpty(file) || file.Contains('\0'))
                throw new ArgumentException("The file drop list contains an invalid path.", nameof(fileDropList));

            // Match WPF's requirement that every entry can be resolved to a full path.
            files[index] = Path.GetFullPath(file);
        }

        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.FileDrop, files, autoConvert: true);
        SetDataObject(dataObject, copy: true);
    }

    public static void SetImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        SetDataInternal(DataFormats.Bitmap, image);
    }

    public static void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SetText(text, TextDataFormat.UnicodeText);
    }

    public static void SetText(string text, TextDataFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateTextDataFormat(format);
        SetDataInternal(GetTextFormatName(format), text);
    }

    public static bool TryGetData<T>(
        string format,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        data = default;
        ArgumentException.ThrowIfNullOrEmpty(format);
        if (GetDataObject() is not { } dataObject)
            return false;

        if (dataObject is ITypedDataObject typed &&
            typed.TryGetData(format, autoConvert: false, out data))
            return true;

        object? raw = dataObject.GetData(format, autoConvert: false);
        if (raw is T value)
        {
            data = value;
            return true;
        }

        return TryDeserializeJson(raw, out data);
    }

#pragma warning disable CS3021
    [CLSCompliant(false)]
    public static bool TryGetData<T>(
        string format,
        Func<TypeName, Type?> resolver,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        data = default;
        ArgumentException.ThrowIfNullOrEmpty(format);
        ArgumentNullException.ThrowIfNull(resolver);
        if (GetDataObject() is not { } dataObject)
            return false;

        if (dataObject is ITypedDataObject typed &&
            typed.TryGetData(format, resolver, autoConvert: false, out data))
            return true;

        return TryDeserializeJson(dataObject.GetData(format, autoConvert: false), out data);
    }
#pragma warning restore CS3021

    internal static string BuildCfHtml(string fragment) =>
        ClipboardPlatform.BuildCfHtml(fragment);

    private static bool ContainsDataInternal(string format) =>
        GetDataObject() is { } dataObject &&
        dataObject.GetDataPresent(format, IsDataFormatAutoConvert(format));

    private static object? GetDataInternal(string format) =>
        GetDataObject() is { } dataObject
            ? dataObject.GetData(format, IsDataFormatAutoConvert(format))
            : null;

    private static T? GetTypedDataIfAvailable<T>(string format)
    {
        IDataObject? dataObject = GetDataObject();
        if (dataObject is ITypedDataObject typed &&
            typed.TryGetData(format, autoConvert: true, out T? typedValue))
            return typedValue;

        return dataObject?.GetData(format, autoConvert: true) is T value ? value : default;
    }

    private static bool TryDeserializeJson<T>(
        object? raw,
        [NotNullWhen(true), MaybeNullWhen(false)] out T value)
    {
        value = default;
        if (raw is not byte[] bytes || bytes.Length == 0)
            return false;

        try
        {
#pragma warning disable IL2026, IL3050
            T? deserialized = JsonSerializer.Deserialize<T>(bytes);
#pragma warning restore IL2026, IL3050
            if (deserialized is null)
                return false;

            value = deserialized;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static void SetDataInternal(string format, object data)
    {
        var dataObject = new DataObject();
        dataObject.SetData(format, data, IsDataFormatAutoConvert(format));
        SetDataObject(dataObject, copy: true);
    }

    private static bool IsDataFormatAutoConvert(string format) =>
        format == DataFormats.FileDrop || format == DataFormats.Bitmap;

    private static string GetTextFormatName(TextDataFormat format) => format switch
    {
        TextDataFormat.Text => DataFormats.Text,
        TextDataFormat.UnicodeText => DataFormats.UnicodeText,
        TextDataFormat.Rtf => DataFormats.Rtf,
        TextDataFormat.Html => DataFormats.Html,
        TextDataFormat.CommaSeparatedValue => DataFormats.CommaSeparatedValue,
        TextDataFormat.Xaml => DataFormats.Xaml,
        _ => throw new InvalidEnumArgumentException(nameof(format), (int)format, typeof(TextDataFormat)),
    };

    private static void ValidateTextDataFormat(TextDataFormat format)
    {
        if (!Enum.IsDefined(format))
            throw new InvalidEnumArgumentException(nameof(format), (int)format, typeof(TextDataFormat));
    }

    private static void VerifyAccess()
    {
        if (Provider is UnsupportedClipboardProvider)
            throw new PlatformNotSupportedException("The system clipboard is not available on this platform.");

        if (Provider.RequiresSta && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new ThreadStateException(
                "The system clipboard can only be accessed from a single-threaded apartment (STA) thread.");
        }
    }

    private static void ThrowOnFailure(bool success, string operation)
    {
        if (!success)
            throw new ExternalException($"The system clipboard operation '{operation}' failed.");
    }
}
