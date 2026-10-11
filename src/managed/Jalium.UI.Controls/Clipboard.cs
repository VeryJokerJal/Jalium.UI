using System.Buffers.Binary;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using static Jalium.UI.Interop.Win32.Win32GdiMethods;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Provides clipboard operations. Uses Windows API on Windows,
/// and jalium.native.platform clipboard on Linux/Android.
/// </summary>
internal static partial class ClipboardPlatform
{
    // Standard clipboard formats
    private const uint CF_TEXT = 1;
    private const uint CF_BITMAP = 2;
    private const uint CF_OEMTEXT = 7;
    private const uint CF_DIB = 8;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_DIBV5 = 17;
    private const uint CF_HDROP = 15;

    // The Windows 11 clipboard-history / cloud-clipboard service routinely opens the
    // clipboard for a few milliseconds immediately after any change, so a single
    // OpenClipboard attempt frequently loses the race and fails with ERROR_ACCESS_DENIED.
    // Retry briefly before giving up — the same contention mitigation WPF/WinForms use.
    private const int ClipboardOpenRetryCount = 10;
    private const int ClipboardOpenRetryDelayMs = 10;
    internal const int MaxClipboardPayloadBytes = 256 * 1024 * 1024;
    internal const int MaxClipboardImageBytes = 128 * 1024 * 1024;
    internal const int MaxClipboardFileCount = 65_536;
    internal const int MaxClipboardPathChars = 32_767;

    /// <summary>
    /// Opens the Win32 clipboard, retrying a few times on transient failure. On Windows 11
    /// the clipboard-history / cloud-clipboard service grabs the clipboard right after any
    /// change, so rapid back-to-back access loses the race: a lone <c>OpenClipboard</c>
    /// returns false and the operation silently no-ops (an occasional copy/paste no-op for
    /// the user). Polling for a short window lets the contending process release the
    /// clipboard first. Callers must still pair a successful open with <c>CloseClipboard</c>.
    /// </summary>
    /// <returns>True if the clipboard was opened; false if every attempt failed.</returns>
    private static bool OpenClipboardWithRetry()
    {
        for (int attempt = 0; attempt < ClipboardOpenRetryCount; attempt++)
        {
            if (OpenClipboard(nint.Zero))
                return true;

            if (attempt < ClipboardOpenRetryCount - 1)
                Thread.Sleep(ClipboardOpenRetryDelayMs);
        }

        return false;
    }

    internal static bool TryGetUnicodeClipboardByteCount(int characterCount, out int byteCount)
    {
        long required = ((long)characterCount + 1) * sizeof(char);
        if (characterCount < 0 || required > MaxClipboardPayloadBytes)
        {
            byteCount = 0;
            return false;
        }

        byteCount = (int)required;
        return true;
    }

    internal static unsafe string? DecodeUnicodeClipboardText(nint pointer, nuint byteLength)
    {
        if (pointer == nint.Zero ||
            byteLength < sizeof(char) ||
            byteLength > MaxClipboardPayloadBytes ||
            (byteLength & 1) != 0)
        {
            return null;
        }

        int characterCount = (int)(byteLength / sizeof(char));
        var characters = new ReadOnlySpan<char>((void*)pointer, characterCount);
        int terminator = characters.IndexOf('\0');
        return terminator < 0 ? null : new string(characters[..terminator]);
    }

    internal readonly record struct ClipboardDibLayout(
        int HeaderOffset,
        int Width,
        int Height,
        int AbsoluteHeight,
        int BitsPerPixel,
        int SourceStride,
        int SourceDataSize,
        int TargetStride,
        int TargetDataSize);

    internal static bool TryReadClipboardDibLayout(
        nint pointer,
        nuint allocationSize,
        out ClipboardDibLayout layout)
    {
        layout = default;
        if (pointer == nint.Zero || allocationSize < 40)
            return false;

        int headerOffset = Marshal.ReadInt32(pointer, 0);
        int width = Marshal.ReadInt32(pointer, 4);
        int height = Marshal.ReadInt32(pointer, 8);
        int bitsPerPixel = Marshal.ReadInt16(pointer, 14);
        int compression = Marshal.ReadInt32(pointer, 16);

        if (headerOffset < 40 ||
            width <= 0 ||
            height == 0 ||
            height == int.MinValue ||
            compression != 0 ||
            (bitsPerPixel != 24 && bitsPerPixel != 32) ||
            (nuint)headerOffset > allocationSize)
        {
            return false;
        }

        int absoluteHeight = Math.Abs(height);
        int sourceStride;
        int sourceDataSize;
        int targetStride;
        int targetDataSize;
        try
        {
            int rowBytes = checked(width * (bitsPerPixel / 8));
            sourceStride = checked((rowBytes + 3) & ~3);
            sourceDataSize = checked(absoluteHeight * sourceStride);
            targetStride = bitsPerPixel == 32 ? sourceStride : checked(width * 4);
            targetDataSize = checked(absoluteHeight * targetStride);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (sourceDataSize > MaxClipboardImageBytes ||
            targetDataSize > MaxClipboardImageBytes ||
            (nuint)sourceDataSize > allocationSize - (nuint)headerOffset)
        {
            return false;
        }

        layout = new ClipboardDibLayout(
            headerOffset,
            width,
            height,
            absoluteHeight,
            bitsPerPixel,
            sourceStride,
            sourceDataSize,
            targetStride,
            targetDataSize);
        return true;
    }

    internal static bool TryGetFileDropPayloadSize(
        IReadOnlyList<string> files,
        out int payloadSize)
    {
        payloadSize = 0;
        if (files == null || files.Count == 0 || files.Count > MaxClipboardFileCount)
            return false;

        long required = Marshal.SizeOf<DROPFILES>() + sizeof(char);
        for (int index = 0; index < files.Count; index++)
        {
            string? file = files[index];
            if (string.IsNullOrEmpty(file) ||
                file.Length > MaxClipboardPathChars ||
                file.IndexOf('\0') >= 0)
            {
                return false;
            }

            required += ((long)file.Length + 1) * sizeof(char);
            if (required > MaxClipboardPayloadBytes)
                return false;
        }

        payloadSize = (int)required;
        return true;
    }

    /// <summary>
    /// Gets text from the clipboard.
    /// </summary>
    /// <returns>The text from the clipboard, or null if no text is available.</returns>
    public static string? GetText()
    {
        if (!PlatformFactory.IsWindows)
            return GetTextCrossPlatform();

        if (!OpenClipboardWithRetry())
            return null;

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == nint.Zero)
                return null;

            nuint byteLength = GlobalSize(handle);
            if (byteLength < sizeof(char) ||
                byteLength > MaxClipboardPayloadBytes ||
                (byteLength & 1) != 0)
            {
                return null;
            }

            var ptr = GlobalLock(handle);
            if (ptr == nint.Zero)
                return null;

            try
            {
                return DecodeUnicodeClipboardText(ptr, byteLength);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Sets text to the clipboard.
    /// </summary>
    /// <param name="text">The text to set.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public static bool SetText(string? text)
    {
        if (text == null)
            return false;

        if (!TryGetUnicodeClipboardByteCount(text.Length, out int byteCount))
            return false;

        if (!PlatformFactory.IsWindows)
        {
            if (Encoding.UTF8.GetByteCount(text) > MaxClipboardPayloadBytes)
                return false;
            return SetTextCrossPlatform(text);
        }

        if (!OpenClipboardWithRetry())
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)byteCount);

            if (hGlobal == nint.Zero)
                return false;

            var ptr = GlobalLock(hGlobal);
            if (ptr == nint.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            try
            {
                // Copy the string to the global memory
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                // Add null terminator
                Marshal.WriteInt16(ptr + text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == nint.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Checks if the clipboard contains text.
    /// </summary>
    /// <returns>True if the clipboard contains text, false otherwise.</returns>
    public static bool ContainsText()
    {
        if (!PlatformFactory.IsWindows)
            return GetTextCrossPlatform() != null;

        return IsClipboardFormatAvailable(CF_UNICODETEXT);
    }

    /// <summary>
    /// Clears the clipboard.
    /// </summary>
    /// <returns>True if successful, false otherwise.</returns>
    public static bool Clear()
    {
        if (!PlatformFactory.IsWindows)
            return NativeMethods.ClipboardClear() == 0;

        if (!OpenClipboardWithRetry())
            return false;

        try
        {
            return EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Checks if the clipboard contains an image.
    /// </summary>
    /// <returns>True if the clipboard contains an image, false otherwise.</returns>
    public static bool ContainsImage()
    {
        if (!PlatformFactory.IsWindows)
            return ContainsDataFormat(DataFormats.Bitmap);

        return IsClipboardFormatAvailable(CF_BITMAP) ||
               IsClipboardFormatAvailable(CF_DIB) ||
               IsClipboardFormatAvailable(CF_DIBV5);
    }

    /// <summary>
    /// Checks if the clipboard contains file drop list.
    /// </summary>
    /// <returns>True if the clipboard contains file paths, false otherwise.</returns>
    public static bool ContainsFileDropList()
    {
        if (!PlatformFactory.IsWindows)
            return ContainsDataFormat(DataFormats.FileDrop);

        return IsClipboardFormatAvailable(CF_HDROP);
    }

    /// <summary>
    /// Gets an image from the clipboard as raw bitmap data.
    /// </summary>
    /// <returns>A tuple of (width, height, stride, pixel data) or null if no image is available.</returns>
    public static (int Width, int Height, int Stride, byte[] Data)? GetImage()
    {
        if (!PlatformFactory.IsWindows)
        {
            byte[]? png = GetBinaryData(DataFormats.Bitmap);
            return png == null ? null : DecodePng(png);
        }

        if (!OpenClipboardWithRetry())
            return null;

        try
        {
            // Try CF_DIB first (device independent bitmap)
            var handle = GetClipboardData(CF_DIB);
            if (handle == nint.Zero)
                handle = GetClipboardData(CF_DIBV5);

            if (handle == nint.Zero)
                return null;

            nuint allocationSize = GlobalSize(handle);
            if (allocationSize < 40)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == nint.Zero)
                return null;

            try
            {
                if (!TryReadClipboardDibLayout(ptr, allocationSize, out ClipboardDibLayout layout))
                    return null;

                // Copy pixel data
                var data = new byte[layout.SourceDataSize];
                Marshal.Copy(ptr + layout.HeaderOffset, data, 0, layout.SourceDataSize);

                // DIBs are typically stored bottom-up, convert to top-down
                if (layout.Height > 0)
                {
                    var flippedData = new byte[layout.SourceDataSize];
                    for (int y = 0; y < layout.AbsoluteHeight; y++)
                    {
                        var srcOffset = (layout.AbsoluteHeight - 1 - y) * layout.SourceStride;
                        var dstOffset = y * layout.SourceStride;
                        Array.Copy(data, srcOffset, flippedData, dstOffset, layout.SourceStride);
                    }
                    data = flippedData;
                }

                if (layout.BitsPerPixel == 32)
                    return (layout.Width, layout.AbsoluteHeight, layout.SourceStride, data);

                var bgra = new byte[layout.TargetDataSize];
                for (int y = 0; y < layout.AbsoluteHeight; y++)
                {
                    for (int x = 0; x < layout.Width; x++)
                    {
                        int sourceOffset = y * layout.SourceStride + x * 3;
                        int targetOffset = y * layout.TargetStride + x * 4;
                        data.AsSpan(sourceOffset, 3).CopyTo(bgra.AsSpan(targetOffset, 3));
                        bgra[targetOffset + 3] = 255;
                    }
                }

                return (layout.Width, layout.AbsoluteHeight, layout.TargetStride, bgra);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Gets file drop list from the clipboard.
    /// </summary>
    /// <returns>Array of file paths, or null if no file drop list is available.</returns>
    public static string[]? GetFileDropList()
    {
        if (!PlatformFactory.IsWindows)
        {
            byte[]? uriList = GetBinaryData(DataFormats.FileDrop);
            return uriList == null ? null : ParseUriList(uriList);
        }
        if (!OpenClipboardWithRetry())
            return null;

        try
        {
            var handle = GetClipboardData(CF_HDROP);
            if (handle == nint.Zero)
                return null;

            nuint allocationSize = GlobalSize(handle);
            if (allocationSize < (nuint)Marshal.SizeOf<DROPFILES>() ||
                allocationSize > MaxClipboardPayloadBytes)
            {
                return null;
            }

            var count = DragQueryFileW(handle, 0xFFFFFFFF, null, 0);
            if (count == 0 || count > MaxClipboardFileCount)
                return null;

            var files = new string[(int)count];
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFileW(handle, i, null, 0);
                if (length == 0 || length > MaxClipboardPathChars)
                    return null;

                var buffer = new char[length + 1];
                uint written = DragQueryFileW(handle, i, buffer, (uint)buffer.Length);
                if (written != length)
                    return null;

                files[(int)i] = new string(buffer, 0, (int)written);
            }

            return files;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Sets file drop list to the clipboard.
    /// </summary>
    /// <param name="files">Array of file paths to set.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public static bool SetFileDropList(string[] files)
    {
        if (files == null || files.Length == 0)
            return false;

        if (!TryGetFileDropPayloadSize(files, out _))
            return false;

        if (!PlatformFactory.IsWindows)
            return SetBinaryData(DataFormats.FileDrop, Encoding.UTF8.GetBytes(BuildUriList(files)));

        if (!OpenClipboardWithRetry())
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            return SetFileDropListInSession(files);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Gets the data object from the clipboard.
    /// </summary>
    /// <returns>A canonical data object containing the clipboard data.</returns>
    public static global::Jalium.UI.IDataObject? GetDataObject()
    {
        var dataObject = new global::Jalium.UI.DataObject();
        bool hasData = false;

        if (ContainsText())
        {
            string text = GetText() ?? string.Empty;
            dataObject.SetData(DataFormats.UnicodeText, text, autoConvert: true);
            hasData = true;
        }

        if (ContainsFileDropList())
        {
            if (GetFileDropList() is { Length: > 0 } files)
            {
                dataObject.SetData(DataFormats.FileDrop, files, autoConvert: true);
                hasData = true;
            }
        }

        if (ContainsImage() && GetImage() is { } rawImage)
        {
            var (width, height, stride, pixels) = rawImage;
            dataObject.SetData(
                DataFormats.Bitmap,
                new PlatformClipboardBitmapSource(width, height, stride, pixels),
                autoConvert: true);
            hasData = true;
        }

        foreach (string format in GetAvailableDataFormats())
        {
            if (dataObject.GetDataPresent(format, autoConvert: false) ||
                format == DataFormats.Bitmap || format == DataFormats.Dib || format == DataFormats.FileDrop)
                continue;

            byte[]? bytes = GetBinaryData(format);
            if (bytes == null)
                continue;

            object value = IsEncodedTextFormat(format)
                ? DecodeClipboardText(format, bytes)
                : format == DataFormats.WaveAudio
                    ? new MemoryStream(bytes, writable: false)
                    : bytes;
            dataObject.SetData(format, value, autoConvert: false);
            hasData = true;
        }

        return hasData ? dataObject : null;
    }

    private static bool IsEncodedTextFormat(string format) =>
        format == DataFormats.Text || format == DataFormats.UnicodeText ||
        format == DataFormats.Rtf || format == DataFormats.Html ||
        format == DataFormats.CommaSeparatedValue || format == DataFormats.Xaml ||
        format == DataFormats.StringFormat || format == DataFormats.OemText;

    private static string DecodeClipboardText(string format, byte[] bytes)
    {
        int length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
            length--;

        if (PlatformFactory.IsWindows && format == DataFormats.Text)
            return DecodeWindowsCodePageText(bytes, length, codePage: 0);
        if (PlatformFactory.IsWindows && format == DataFormats.OemText)
            return DecodeWindowsCodePageText(bytes, length, codePage: 1);
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static string DecodeWindowsCodePageText(byte[] bytes, int length, uint codePage)
    {
        if (length == 0)
            return string.Empty;

        int charCount = MultiByteToWideChar(codePage, 0, bytes, length, null, 0);
        if (charCount <= 0)
            return Encoding.UTF8.GetString(bytes, 0, length);

        var chars = new char[charCount];
        int written = MultiByteToWideChar(codePage, 0, bytes, length, chars, chars.Length);
        return written == charCount ? new string(chars) : Encoding.UTF8.GetString(bytes, 0, length);
    }

    /// <summary>
    /// Sets data to the clipboard.
    /// </summary>
    /// <param name="data">The data object to set.</param>
    /// <param name="copy">Whether to leave data on clipboard after application exits.</param>
    public static bool SetDataObject(object data, bool copy = true)
    {
        if (data is string text)
        {
            return SetText(text);
        }
        else if (data is string[] files)
        {
            return SetFileDropList(files);
        }
        else if (data is global::Jalium.UI.IDataObject dataObj)
        {
            return SetDataObjectCore(dataObj);
        }

        return SetDataObjectCore(new global::Jalium.UI.DataObject(data));
    }

    private static bool SetDataObjectCore(global::Jalium.UI.IDataObject dataObj)
    {
        var text = GetExactStringData(dataObj, DataFormats.Text);
        var unicode = GetExactStringData(dataObj, DataFormats.UnicodeText);
        var stringValue = GetExactStringData(dataObj, DataFormats.StringFormat);
        var oemText = GetExactStringData(dataObj, DataFormats.OemText);
        var rtf = GetExactStringData(dataObj, DataFormats.Rtf);
        var html = GetExactStringData(dataObj, DataFormats.Html);
        var plain = unicode ?? text ?? stringValue ?? oemText;

        if (PlatformFactory.IsAndroid)
        {
            string[] formats = dataObj.GetFormats(autoConvert: false);
            bool textOnly = formats.All(format =>
                format == DataFormats.Text || format == DataFormats.UnicodeText ||
                format == DataFormats.StringFormat);
            return plain != null && textOnly && SetTextCrossPlatform(plain);
        }

        if (!PlatformFactory.IsWindows)
        {
            return SetDataObjectCrossPlatform(dataObj);
        }

        // On Windows, write every requested format in a single clipboard session so the
        // formats coexist (calling SetText repeatedly would EmptyClipboard each time).
        if (!OpenClipboardWithRetry())
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
                return false;

            bool success = true;
            bool wroteAny = false;
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (unicode != null || stringValue != null)
            {
                string value = unicode ?? stringValue!;
                success &= SetClipboardMemory(CF_UNICODETEXT, Encoding.Unicode.GetBytes(value + '\0'));
                wroteAny = true;
                handled.Add(DataFormats.UnicodeText);
                handled.Add(DataFormats.StringFormat);
            }
            if (text != null || stringValue != null)
            {
                string value = text ?? stringValue!;
                success &= SetClipboardMemory(CF_TEXT, EncodeWindowsCodePageText(value, codePage: 0));
                wroteAny = true;
                handled.Add(DataFormats.Text);
                handled.Add(DataFormats.StringFormat);
            }
            if (oemText != null)
            {
                success &= SetClipboardMemory(CF_OEMTEXT, EncodeWindowsCodePageText(oemText, codePage: 1));
                wroteAny = true;
                handled.Add(DataFormats.OemText);
            }
            if (rtf != null)
            {
                var rtfFormat = RegisterClipboardFormatW(DataFormats.Rtf);
                if (rtfFormat != 0)
                {
                    success &= SetClipboardMemory(rtfFormat, Encoding.ASCII.GetBytes(rtf + '\0'));
                    wroteAny = true;
                    handled.Add(DataFormats.Rtf);
                }
            }
            if (html != null)
            {
                var htmlFormat = RegisterClipboardFormatW(DataFormats.Html);
                if (htmlFormat != 0)
                {
                    var bytes = Encoding.UTF8.GetBytes(BuildCfHtml(html!) + '\0');
                    success &= SetClipboardMemory(htmlFormat, bytes);
                    wroteAny = true;
                    handled.Add(DataFormats.Html);
                }
            }

            if (dataObj.GetDataPresent(DataFormats.FileDrop) &&
                TryGetFileDropData(dataObj.GetData(DataFormats.FileDrop), out string[] files))
            {
                success &= SetFileDropListInSession(files);
                wroteAny = true;
                handled.Add(DataFormats.FileDrop);
            }

            foreach (string format in dataObj.GetFormats(autoConvert: false))
            {
                if (handled.Contains(format))
                    continue;

                object? value = dataObj.GetData(format, autoConvert: false);
                if (value == null || !TryEncodeWindowsValue(format, value, out uint nativeFormat, out byte[] bytes))
                    continue;

                success &= SetClipboardMemory(nativeFormat, bytes);
                wroteAny = true;
            }

            return success && wroteAny;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Copies <paramref name="data"/> into a global memory block and hands it to the clipboard
    /// for <paramref name="format"/>. Must be called inside an open clipboard session.
    /// </summary>
    private static bool SetClipboardMemory(uint format, byte[] data)
    {
        if (data.Length > MaxClipboardPayloadBytes)
            return false;

        int allocationSize = Math.Max(1, data.Length);
        var hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)allocationSize);
        if (hGlobal == nint.Zero)
        {
            return false;
        }

        var ptr = GlobalLock(hGlobal);
        if (ptr == nint.Zero)
        {
            GlobalFree(hGlobal);
            return false;
        }

        try
        {
            if (data.Length != 0)
                Marshal.Copy(data, 0, ptr, data.Length);
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        if (SetClipboardData(format, hGlobal) == nint.Zero)
        {
            GlobalFree(hGlobal);
            return false;
        }

        return true;
    }

    private static bool SetFileDropListInSession(string[] files)
    {
        if (!TryGetFileDropPayloadSize(files, out int totalLength))
            return false;

        var hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)totalLength);
        if (hGlobal == nint.Zero)
        {
            return false;
        }

        var ptr = GlobalLock(hGlobal);
        if (ptr == nint.Zero)
        {
            GlobalFree(hGlobal);
            return false;
        }

        try
        {
            var dropFiles = new DROPFILES { pFiles = (uint)Marshal.SizeOf<DROPFILES>(), fWide = true };
            Marshal.StructureToPtr(dropFiles, ptr, false);

            var offset = Marshal.SizeOf<DROPFILES>();
            foreach (var file in files)
            {
                var chars = file.ToCharArray();
                Marshal.Copy(chars, 0, ptr + offset, chars.Length);
                offset += (file.Length + 1) * sizeof(char);
            }
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        if (SetClipboardData(CF_HDROP, hGlobal) == nint.Zero)
        {
            GlobalFree(hGlobal);
            return false;
        }

        return true;
    }

    private static bool TryEncodeWindowsValue(
        string format,
        object value,
        out uint nativeFormat,
        out byte[] bytes)
    {
        if ((format == DataFormats.Bitmap || format == DataFormats.Dib) && value is BitmapSource bitmap)
        {
            if (!TryGetBgraPixels(bitmap, out int width, out int height, out int stride, out byte[] pixels))
            {
                nativeFormat = 0;
                bytes = [];
                return false;
            }

            nativeFormat = CF_DIB;
            bytes = EncodeDib(width, height, stride, pixels);
            return true;
        }

        nativeFormat = ResolveClipboardFormat(format);
        switch (value)
        {
            case string stringValue:
                bytes = Encoding.UTF8.GetBytes(stringValue + '\0');
                return true;
            case byte[] array:
                bytes = array.ToArray();
                return true;
            case MemoryStream memory:
                bytes = memory.ToArray();
                return true;
            case Stream stream:
                return TryReadBytes(stream, out bytes);
            default:
                try
                {
#pragma warning disable IL2026, IL3050
                    bytes = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType());
#pragma warning restore IL2026, IL3050
                    return true;
                }
                catch (Exception exception) when (exception is NotSupportedException or JsonException)
                {
                    bytes = [];
                    return false;
                }
        }
    }

    private static string? GetExactStringData(global::Jalium.UI.IDataObject dataObject, string format) =>
        dataObject.GetDataPresent(format, autoConvert: false)
            ? dataObject.GetData(format, autoConvert: false) as string
            : null;

    private static byte[] EncodeWindowsCodePageText(string value, uint codePage)
    {
        if (value.Length == 0)
            return [0];

        int byteCount = WideCharToMultiByte(
            codePage,
            0,
            value,
            value.Length,
            null,
            0,
            nint.Zero,
            nint.Zero);
        if (byteCount <= 0)
            return Encoding.UTF8.GetBytes(value + '\0');

        var bytes = new byte[checked(byteCount + 1)];
        int written = WideCharToMultiByte(
            codePage,
            0,
            value,
            value.Length,
            bytes,
            byteCount,
            nint.Zero,
            nint.Zero);
        return written == byteCount ? bytes : Encoding.UTF8.GetBytes(value + '\0');
    }

    /// <summary>Tests a canonical WPF format name against the platform clipboard.</summary>
    internal static bool ContainsDataFormat(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        if (PlatformFactory.IsWindows)
            return IsClipboardFormatAvailable(ResolveClipboardFormat(format));

        return ResolveOfferedMime(format) != null;
    }

    /// <summary>Reads a global-memory clipboard format.</summary>
    internal static byte[]? GetBinaryData(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        if (!PlatformFactory.IsWindows)
        {
            string? mimeType = ResolveOfferedMime(format);
            return mimeType == null ? null : GetNativeClipboardData(mimeType);
        }

        if (!OpenClipboardWithRetry())
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(ResolveClipboardFormat(format));
            if (handle == nint.Zero)
            {
                return null;
            }

            var nativeSize = GlobalSize(handle);
            if (nativeSize == 0 || nativeSize > MaxClipboardPayloadBytes)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            if (pointer == nint.Zero)
            {
                return null;
            }

            try
            {
                var bytes = new byte[(int)nativeSize];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Publishes a global-memory clipboard format in one transaction.</summary>
    internal static bool SetBinaryData(string format, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaxClipboardPayloadBytes)
            return false;

        if (!PlatformFactory.IsWindows)
        {
            byte[] normalized = NormalizeCrossPlatformBytes(format, data);
            var representations = GetMimeTypesForFormat(format)
                .Select(mimeType => new ClipboardRepresentation(mimeType, normalized))
                .ToArray();
            return SetNativeClipboardData(representations);
        }

        if (!OpenClipboardWithRetry())
        {
            return false;
        }

        try
        {
            return EmptyClipboard() && SetClipboardMemory(ResolveClipboardFormat(format), data);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Publishes top-down BGRA pixels as a standard bottom-up CF_DIB payload.</summary>
    internal static bool SetImagePixels(int width, int height, int stride, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (!TryGetClipboardImageLayout(
                width,
                height,
                stride,
                out _,
                out int sourceByteCount,
                out _))
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The declared image dimensions, stride, or byte count are invalid or exceed the clipboard image limit.");
        }
        if (pixels.Length < sourceByteCount)
        {
            throw new ArgumentException("The pixel buffer is smaller than the declared image.", nameof(pixels));
        }

        if (!PlatformFactory.IsWindows)
            return SetBinaryData(DataFormats.Bitmap, EncodePng(width, height, stride, pixels));

        return SetBinaryData(DataFormats.Dib, EncodeDib(width, height, stride, pixels));
    }

    private static byte[] EncodeDib(int width, int height, int stride, byte[] pixels)
    {
        const int bitmapInfoHeaderSize = 40;
        if (!TryGetClipboardImageLayout(
                width,
                height,
                stride,
                out int dibStride,
                out int sourceByteCount,
                out int dibByteCount) ||
            pixels.Length < sourceByteCount)
        {
            throw new ArgumentException("The pixel buffer layout is invalid or exceeds the clipboard image limit.", nameof(pixels));
        }

        var dib = new byte[checked(bitmapInfoHeaderSize + dibByteCount)];
        var header = dib.AsSpan(0, bitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[0..4], bitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], height); // positive => bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(header[12..14], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..16], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 0); // BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(header[20..24], dibStride * height);

        for (var y = 0; y < height; y++)
        {
            pixels.AsSpan(y * stride, dibStride).CopyTo(
                dib.AsSpan(bitmapInfoHeaderSize + ((height - 1 - y) * dibStride), dibStride));
        }

        return dib;
    }

    private static bool TryGetBgraPixels(
        BitmapSource image,
        out int width,
        out int height,
        out int stride,
        out byte[] pixels)
    {
        width = image.PixelWidth;
        height = image.PixelHeight;
        stride = 0;
        pixels = [];
        if (width <= 0 || width > int.MaxValue / 4 ||
            !TryGetClipboardImageLayout(
                width,
                height,
                width * 4,
                out stride,
                out _,
                out int pixelByteCount))
        {
            return false;
        }

        BitmapSource normalized = image.Format == PixelFormat.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormat.Bgra32, null, 0);
        pixels = new byte[pixelByteCount];
        normalized.CopyPixels(pixels, stride, 0);

        return true;
    }

    internal static bool TryGetClipboardImageLayout(
        int width,
        int height,
        int sourceStride,
        out int packedStride,
        out int sourceByteCount,
        out int packedByteCount)
    {
        packedStride = 0;
        sourceByteCount = 0;
        packedByteCount = 0;
        if (width <= 0 || height <= 0 || sourceStride <= 0)
            return false;

        try
        {
            packedStride = checked(width * 4);
            if (sourceStride < packedStride)
                return false;

            sourceByteCount = checked(sourceStride * height);
            packedByteCount = checked(packedStride * height);
            return sourceByteCount <= MaxClipboardImageBytes &&
                packedByteCount <= MaxClipboardImageBytes;
        }
        catch (OverflowException)
        {
            packedStride = 0;
            sourceByteCount = 0;
            packedByteCount = 0;
            return false;
        }
    }

    private const string MimeTextUtf8 = "text/plain;charset=utf-8";
    private const string MimeTextPlain = "text/plain";
    private const string MimeUtf8String = "UTF8_STRING";
    private const string MimeHtml = "text/html";
    private const string MimeRtf = "text/rtf";
    private const string MimeApplicationRtf = "application/rtf";
    private const string MimeUriList = "text/uri-list";
    private const string MimePng = "image/png";
    private const string MimeWave = "audio/wav";
    private const string MimeWaveAlternative = "audio/x-wav";
    private const string MimeCsv = "text/csv";
    private const string MimeXaml = "application/xaml+xml";
    private const string CustomMimePrefix = "application/x-jalium-clipboard-format-";

    private const string StringFormatName = "System.String";
    private const string WaveAudioFormatName = "WaveAudio";
    private const string CsvFormatName = "CSV";
    private const string XamlFormatName = "Xaml";

    private readonly record struct ClipboardRepresentation(string MimeType, byte[] Data);

    /// <summary>
    /// Maps a WPF clipboard format to the interoperable MIME representations used by
    /// X11 selections and the Wayland data-device protocol. Unknown MIME-shaped names
    /// are preserved verbatim; other registered WPF names receive a reversible Jalium
    /// MIME name so two processes can still exchange their raw representation.
    /// </summary>
    internal static string[] GetMimeTypesForFormat(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        if (IsAnyFormat(format, DataFormats.Text, DataFormats.UnicodeText, StringFormatName))
            return [MimeTextUtf8, MimeTextPlain, MimeUtf8String];
        if (IsAnyFormat(format, DataFormats.Html))
            return [MimeHtml];
        if (IsAnyFormat(format, DataFormats.Rtf))
            return [MimeRtf, MimeApplicationRtf];
        if (IsAnyFormat(format, DataFormats.FileDrop))
            return [MimeUriList];
        if (IsAnyFormat(format, DataFormats.Bitmap, DataFormats.Dib))
            return [MimePng];
        if (IsAnyFormat(format, WaveAudioFormatName))
            return [MimeWave, MimeWaveAlternative];
        if (IsAnyFormat(format, CsvFormatName))
            return [MimeCsv];
        if (IsAnyFormat(format, XamlFormatName))
            return [MimeXaml];

        if (LooksLikeMimeType(format))
            return [format];

        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(format))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return [CustomMimePrefix + encoded];
    }

    /// <summary>Returns WPF-style format names currently advertised by the OS clipboard.</summary>
    internal static string[] GetAvailableDataFormats()
    {
        if (PlatformFactory.IsWindows)
        {
            if (!OpenClipboardWithRetry())
                return [];

            try
            {
                var win32Formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                uint current = 0;
                while ((current = EnumClipboardFormats(current)) != 0)
                {
                    string? format = GetWindowsFormatName(current);
                    if (format != null)
                        win32Formats.Add(format);
                }

                return [.. win32Formats];
            }
            finally
            {
                CloseClipboard();
            }
        }

        var formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string mimeType in GetNativeClipboardFormats())
        {
            string? format = GetFormatForMimeType(mimeType);
            if (format == null)
                continue;

            formats.Add(format);
            if (IsAnyFormat(format, DataFormats.UnicodeText))
            {
                formats.Add(DataFormats.Text);
                formats.Add(StringFormatName);
            }
            else if (IsAnyFormat(format, DataFormats.Bitmap))
            {
                formats.Add(DataFormats.Dib);
            }
        }

        return [.. formats];
    }

    private static string? GetWindowsFormatName(uint format) => format switch
    {
        CF_TEXT => DataFormats.Text,
        CF_BITMAP => DataFormats.Bitmap,
        CF_OEMTEXT => DataFormats.OemText,
        CF_DIB => DataFormats.Dib,
        CF_UNICODETEXT => DataFormats.UnicodeText,
        CF_DIBV5 => DataFormats.Dib,
        CF_HDROP => DataFormats.FileDrop,
        11 => DataFormats.Riff,
        12 => DataFormats.WaveAudio,
        _ => GetRegisteredClipboardFormatName(format),
    };

    private static string? GetRegisteredClipboardFormatName(uint format)
    {
        var buffer = new char[256];
        int length = GetClipboardFormatNameW(format, buffer, buffer.Length);
        return length <= 0 ? null : new string(buffer, 0, length);
    }

    internal static string BuildUriList(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var builder = new StringBuilder();
        foreach (string file in files)
        {
            if (string.IsNullOrWhiteSpace(file) || file.Contains('\0'))
                continue;

            if (Uri.TryCreate(file, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                builder.Append(uri.AbsoluteUri);
            else
                builder.Append(new Uri(Path.GetFullPath(file)).AbsoluteUri);
            builder.Append("\r\n");
        }
        return builder.ToString();
    }

    internal static string[] ParseUriList(ReadOnlySpan<byte> bytes)
    {
        string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var paths = new List<string>();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile)
            {
                paths.Add(uri.LocalPath);
            }
            else if (Path.IsPathFullyQualified(line))
            {
                paths.Add(line);
            }
        }
        return [.. paths];
    }

    private static bool SetDataObjectCrossPlatform(global::Jalium.UI.IDataObject dataObject)
    {
        return SetNativeClipboardData(BuildCrossPlatformRepresentations(dataObject)
            .Select(pair => new ClipboardRepresentation(pair.Key, pair.Value)).ToArray());
    }

    // Clipboard and native drag sources publish the same desktop data contract.
    internal static Dictionary<string, byte[]> BuildCrossPlatformRepresentations(global::Jalium.UI.IDataObject dataObject)
    {
        var representations = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        string? text = GetStringData(dataObject, DataFormats.UnicodeText)
            ?? GetStringData(dataObject, DataFormats.Text)
            ?? GetStringData(dataObject, StringFormatName);
        if (text != null)
            AddRepresentations(representations, DataFormats.UnicodeText, Encoding.UTF8.GetBytes(text));

        string? html = GetStringData(dataObject, DataFormats.Html);
        if (html != null)
            AddRepresentations(representations, DataFormats.Html, Encoding.UTF8.GetBytes(html));

        string? rtf = GetStringData(dataObject, DataFormats.Rtf);
        if (rtf != null)
            AddRepresentations(representations, DataFormats.Rtf, Encoding.UTF8.GetBytes(rtf));

        foreach (string format in new[] { DataFormats.CommaSeparatedValue, DataFormats.Xaml })
        {
            if (GetStringData(dataObject, format) is { } value)
                AddRepresentations(representations, format, Encoding.UTF8.GetBytes(value));
        }

        if (TryGetFileDropData(dataObject.GetData(DataFormats.FileDrop), out string[] files))
            AddRepresentations(representations, DataFormats.FileDrop, Encoding.UTF8.GetBytes(BuildUriList(files)));

        object? bitmap = dataObject.GetData(DataFormats.Bitmap) ?? dataObject.GetData(DataFormats.Dib);
        if (bitmap is BitmapSource bitmapSource && TryEncodeBitmapSource(bitmapSource, out byte[]? png))
            AddRepresentations(representations, DataFormats.Bitmap, png);
        else if (bitmap is byte[] encodedPng && IsPng(encodedPng))
            AddRepresentations(representations, DataFormats.Bitmap, encodedPng);

        object? audio = dataObject.GetData(WaveAudioFormatName);
        if (TryReadBytes(audio, out byte[]? audioBytes))
            AddRepresentations(representations, WaveAudioFormatName, audioBytes);

        foreach (string format in dataObject.GetFormats())
        {
            if (IsKnownCrossPlatformFormat(format))
                continue;

            object? value = dataObject.GetData(format);
            if (value is string stringValue)
                AddRepresentations(representations, format, Encoding.UTF8.GetBytes(stringValue));
            else if (TryReadBytes(value, out byte[]? rawBytes))
                AddRepresentations(representations, format, rawBytes);
            else if (value != null)
            {
                try
                {
#pragma warning disable IL2026, IL3050
                    AddRepresentations(
                        representations,
                        format,
                        JsonSerializer.SerializeToUtf8Bytes(value, value.GetType()));
#pragma warning restore IL2026, IL3050
                }
                catch (Exception exception) when (exception is NotSupportedException or JsonException)
                {
                    // The canonical facade will report failure when no representation
                    // can be published; one unsupported value must not discard the
                    // other valid formats in the same transaction.
                }
            }
        }

        return representations;
    }

    internal static object DecodeCrossPlatformRepresentation(string format, byte[] bytes)
    {
        if (format == DataFormats.FileDrop) return ParseUriList(bytes);
        if (format == DataFormats.Bitmap && DecodePng(bytes) is { } image)
            return new PlatformClipboardBitmapSource(image.Width, image.Height, image.Stride, image.Data);
        if (format == DataFormats.WaveAudio) return new MemoryStream(bytes, writable: false);
        return IsEncodedTextFormat(format) ? DecodeClipboardText(format, bytes) : bytes;
    }

    private static string? GetStringData(global::Jalium.UI.IDataObject dataObject, string format) =>
        dataObject.GetDataPresent(format) ? dataObject.GetData(format) as string : null;

    private static bool TryGetFileDropData(object? value, out string[] files)
    {
        switch (value)
        {
            case string[] array:
                files = array.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
                return files.Length != 0;
            case StringCollection collection:
                files = collection.Cast<string>()
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();
                return files.Length != 0;
            default:
                files = [];
                return false;
        }
    }

    private static bool TryReadBytes(object? value, out byte[] bytes)
    {
        switch (value)
        {
            case byte[] array:
                bytes = array.ToArray();
                return true;
            case MemoryStream memory:
                bytes = memory.ToArray();
                return true;
            case Stream stream:
            {
                long originalPosition = stream.CanSeek ? stream.Position : 0;
                try
                {
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    bytes = copy.ToArray();
                    return true;
                }
                finally
                {
                    if (stream.CanSeek)
                        stream.Position = originalPosition;
                }
            }
            default:
                bytes = [];
                return false;
        }
    }

    private static bool IsKnownCrossPlatformFormat(string format) =>
        IsAnyFormat(
            format,
            DataFormats.Text,
            DataFormats.UnicodeText,
            StringFormatName,
            DataFormats.Html,
            DataFormats.Rtf,
            CsvFormatName,
            XamlFormatName,
            DataFormats.FileDrop,
            DataFormats.Bitmap,
            DataFormats.Dib,
            WaveAudioFormatName);

    private static void AddRepresentations(
        Dictionary<string, byte[]> representations,
        string format,
        byte[] data)
    {
        byte[] normalized = NormalizeCrossPlatformBytes(format, data);
        foreach (string mimeType in GetMimeTypesForFormat(format))
            representations[mimeType] = normalized;
    }

    private static byte[] NormalizeCrossPlatformBytes(string format, byte[] data)
    {
        bool text = IsAnyFormat(
                format,
                DataFormats.Text,
                DataFormats.UnicodeText,
                StringFormatName,
                DataFormats.Html,
                DataFormats.Rtf,
                CsvFormatName,
                XamlFormatName) ||
            (LooksLikeMimeType(format) && format.StartsWith("text/", StringComparison.OrdinalIgnoreCase));
        if (!text || data.Length == 0 || data[^1] != 0)
            return data;

        int length = data.Length;
        while (length > 0 && data[length - 1] == 0)
            length--;
        return data.AsSpan(0, length).ToArray();
    }

    private static string? ResolveOfferedMime(string format)
    {
        string[] offered = GetNativeClipboardFormats();
        foreach (string candidate in GetMimeTypesForFormat(format))
        {
            string? exact = offered.FirstOrDefault(value =>
                value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
        }

        return offered.FirstOrDefault(value => MimeTypeMapsToFormat(value, format));
    }

    internal static bool MimeTypeMapsToFormat(string mimeType, string format)
    {
        string? mapped = GetFormatForMimeType(mimeType);
        if (mapped == null)
            return false;
        if (IsAnyFormat(format, DataFormats.Text, DataFormats.UnicodeText, StringFormatName))
            return IsAnyFormat(mapped, DataFormats.UnicodeText);
        if (IsAnyFormat(format, DataFormats.Bitmap, DataFormats.Dib))
            return IsAnyFormat(mapped, DataFormats.Bitmap);
        return mapped.Equals(format, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? GetFormatForMimeType(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return null;

        string baseType = mimeType.Split(';', 2)[0].Trim();
        if (baseType.Equals(MimeTextPlain, StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals(MimeUtf8String, StringComparison.OrdinalIgnoreCase))
            return DataFormats.UnicodeText;
        if (baseType.Equals(MimeHtml, StringComparison.OrdinalIgnoreCase))
            return DataFormats.Html;
        if (baseType.Equals(MimeRtf, StringComparison.OrdinalIgnoreCase) ||
            baseType.Equals(MimeApplicationRtf, StringComparison.OrdinalIgnoreCase))
            return DataFormats.Rtf;
        if (baseType.Equals(MimeUriList, StringComparison.OrdinalIgnoreCase))
            return DataFormats.FileDrop;
        if (baseType.Equals(MimePng, StringComparison.OrdinalIgnoreCase))
            return DataFormats.Bitmap;
        if (baseType.Equals(MimeWave, StringComparison.OrdinalIgnoreCase) ||
            baseType.Equals(MimeWaveAlternative, StringComparison.OrdinalIgnoreCase))
            return WaveAudioFormatName;
        if (baseType.Equals(MimeCsv, StringComparison.OrdinalIgnoreCase))
            return CsvFormatName;
        if (baseType.Equals(MimeXaml, StringComparison.OrdinalIgnoreCase))
            return XamlFormatName;
        if (mimeType.StartsWith(CustomMimePrefix, StringComparison.OrdinalIgnoreCase))
            return DecodeCustomFormat(mimeType[CustomMimePrefix.Length..]);
        return LooksLikeMimeType(mimeType) ? mimeType : null;
    }

    private static string? DecodeCustomFormat(string encoded)
    {
        try
        {
            string base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            string result = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool LooksLikeMimeType(string value)
    {
        int slash = value.IndexOf('/');
        return slash > 0 && slash < value.Length - 1 &&
            !value.Contains('\0') && !value.Contains('\r') && !value.Contains('\n');
    }

    private static bool IsAnyFormat(string value, params string[] formats) =>
        formats.Any(format => value.Equals(format, StringComparison.OrdinalIgnoreCase));

    private static string[] GetNativeClipboardFormats()
    {
        nint formatsPointer = nint.Zero;
        try
        {
            if (NativeMethods.ClipboardGetFormats(out formatsPointer) != 0 ||
                formatsPointer == nint.Zero)
                return [];

            string formats = Marshal.PtrToStringUTF8(formatsPointer) ?? string.Empty;
            return formats.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        finally
        {
            if (formatsPointer != nint.Zero)
                NativeMethods.PlatformFree(formatsPointer);
        }
    }

    private static byte[]? GetNativeClipboardData(string mimeType)
    {
        nint dataPointer = nint.Zero;
        try
        {
            if (NativeMethods.ClipboardGetData(mimeType, out dataPointer, out uint dataSize) != 0 ||
                dataPointer == nint.Zero || dataSize > MaxClipboardPayloadBytes)
                return null;

            var data = new byte[(int)dataSize];
            if (data.Length != 0)
                Marshal.Copy(dataPointer, data, 0, data.Length);
            return data;
        }
        finally
        {
            if (dataPointer != nint.Zero)
                NativeMethods.PlatformFree(dataPointer);
        }
    }

    private static unsafe bool SetNativeClipboardData(IReadOnlyList<ClipboardRepresentation> representations)
    {
        if (representations.Count == 0)
            return NativeMethods.ClipboardClear() == 0;
        if (representations.Count > MaxClipboardFileCount ||
            representations.Any(representation =>
                representation.Data.Length > MaxClipboardPayloadBytes))
        {
            return false;
        }

        using var payload = new NativeClipboardPayload(representations);
        fixed (NativeDragDataItem* items = payload.Items)
            return NativeMethods.ClipboardSetData((nint)items, (uint)payload.Items.Length) == 0;
    }

    private sealed class NativeClipboardPayload : IDisposable
    {
        private readonly List<nint> _allocations = [];

        public NativeClipboardPayload(IReadOnlyList<ClipboardRepresentation> representations)
        {
            Items = new NativeDragDataItem[representations.Count];
            try
            {
                for (int index = 0; index < representations.Count; index++)
                {
                    ClipboardRepresentation representation = representations[index];
                    if (representation.Data.LongLength > uint.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(representations), "Clipboard data exceeds the native ABI limit.");

                    nint mimePointer = Marshal.StringToCoTaskMemUTF8(representation.MimeType);
                    _allocations.Add(mimePointer);
                    nint dataPointer = Marshal.AllocHGlobal(Math.Max(1, representation.Data.Length));
                    _allocations.Add(dataPointer);
                    if (representation.Data.Length != 0)
                        Marshal.Copy(representation.Data, 0, dataPointer, representation.Data.Length);

                    Items[index] = new NativeDragDataItem
                    {
                        MimeType = mimePointer,
                        Data = dataPointer,
                        DataSize = (uint)representation.Data.Length,
                    };
                }
            }
            catch
            {
                ReleaseAllocations();
                throw;
            }
        }

        public NativeDragDataItem[] Items { get; }

        public void Dispose()
        {
            ReleaseAllocations();
        }

        private void ReleaseAllocations()
        {
            for (int index = 0; index < _allocations.Count; index++)
            {
                if ((index & 1) == 0)
                    Marshal.FreeCoTaskMem(_allocations[index]);
                else
                    Marshal.FreeHGlobal(_allocations[index]);
            }
            _allocations.Clear();
        }
    }

    internal static bool TryEncodeBitmapSource(BitmapSource source, out byte[] png)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width <= 0 || width > int.MaxValue / 4 ||
            !TryGetClipboardImageLayout(
                width,
                height,
                width * 4,
                out int stride,
                out _,
                out int pixelByteCount))
        {
            png = [];
            return false;
        }

        BitmapSource normalized = source.Format == PixelFormat.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormat.Bgra32, null, 0);
        var bgra = new byte[pixelByteCount];
        normalized.CopyPixels(bgra, stride, 0);
        png = EncodePng(width, height, stride, bgra);
        return true;
    }

    internal static (int Width, int Height, int Stride, byte[] Data)? DecodePng(byte[] png)
    {
        if (png.Length > MaxClipboardPayloadBytes ||
            !TryReadPngBgraLayout(
                png,
                out int declaredWidth,
                out int declaredHeight,
                out int declaredStride,
                out int declaredByteCount))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(png, writable: false);
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            int width = frame.PixelWidth;
            int height = frame.PixelHeight;
            if (width != declaredWidth || height != declaredHeight)
                return null;

            BitmapSource normalized = frame.Format == PixelFormat.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormat.Bgra32, null, 0);
            var bgra = new byte[declaredByteCount];
            normalized.CopyPixels(bgra, declaredStride, 0);
            return (width, height, declaredStride, bgra);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException or ArgumentException or
                InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException or OverflowException)
        {
            return null;
        }
    }

    internal static byte[] EncodePng(int width, int height, int stride, byte[] bgraPixels)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        if (!TryGetClipboardImageLayout(
                width,
                height,
                stride,
                out _,
                out int sourceByteCount,
                out _) ||
            bgraPixels.Length < sourceByteCount)
        {
            throw new ArgumentException("The pixel buffer layout is invalid or exceeds the clipboard image limit.", nameof(bgraPixels));
        }

        var source = BitmapSource.Create(
            width, height, 96, 96, PixelFormat.Bgra32, null, bgraPixels, stride);
        var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(source) } };
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    internal static bool TryReadPngBgraLayout(
        ReadOnlySpan<byte> png,
        out int width,
        out int height,
        out int stride,
        out int byteCount)
    {
        width = 0;
        height = 0;
        stride = 0;
        byteCount = 0;
        if (png.Length < 33 || !IsPng(png) ||
            BinaryPrimitives.ReadInt32BigEndian(png[8..12]) != 13 ||
            !png[12..16].SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(png[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(png[20..24]);
        if (width <= 0 || width > int.MaxValue / 4 ||
            !TryGetClipboardImageLayout(
                width,
                height,
                width * 4,
                out stride,
                out _,
                out byteCount))
        {
            width = 0;
            height = 0;
            stride = 0;
            byteCount = 0;
            return false;
        }

        return true;
    }

    private static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 &&
        data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    private static uint ResolveClipboardFormat(string format)
    {
        return format switch
        {
            "Text" => CF_TEXT,
            "Bitmap" => CF_BITMAP,
            "OEMText" => CF_OEMTEXT,
            "DeviceIndependentBitmap" => CF_DIB,
            "RiffAudio" => 11,
            "WaveAudio" => 12,
            "UnicodeText" => CF_UNICODETEXT,
            "FileDrop" => CF_HDROP,
            _ => RegisterClipboardFormatW(format),
        };
    }

    private sealed class PlatformClipboardBitmapSource : BitmapSource
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _pixels;

        public PlatformClipboardBitmapSource(int width, int height, int sourceStride, byte[] source)
        {
            _width = width;
            _height = height;
            _pixels = new byte[checked(width * height * 4)];
            bool sourceIs32Bit = sourceStride >= checked(width * 4);
            int sourceBytesPerPixel = sourceIs32Bit ? 4 : 3;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sourceOffset = checked((y * sourceStride) + (x * sourceBytesPerPixel));
                    int targetOffset = checked(((y * width) + x) * 4);
                    source.AsSpan(sourceOffset, sourceBytesPerPixel)
                        .CopyTo(_pixels.AsSpan(targetOffset, sourceBytesPerPixel));
                    _pixels[targetOffset + 3] = sourceIs32Bit ? source[sourceOffset + 3] : (byte)255;
                }
            }
        }

        public override double Width => _width;
        public override double Height => _height;
        public override nint NativeHandle => nint.Zero;
        public override PixelFormat Format => PixelFormat.Bgra32;

        public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            Int32Rect rect = sourceRect.IsEmpty
                ? new Int32Rect(0, 0, _width, _height)
                : sourceRect;
            int rowBytes = checked(rect.Width * 4);
            for (int y = 0; y < rect.Height; y++)
            {
                _pixels.AsSpan(checked(((rect.Y + y) * _width + rect.X) * 4), rowBytes)
                    .CopyTo(pixels.AsSpan(checked(offset + y * stride), rowBytes));
            }
        }
    }

    /// <summary>
    /// Wraps an HTML fragment in the CF_HTML clipboard format expected by Windows
    /// (Word, browsers, etc.), computing the required byte offsets.
    /// </summary>
    internal static string BuildCfHtml(string fragment)
    {
        const string headerTemplate =
            "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";

        // The header length is fixed because the offsets are zero-padded to 10 digits.
        var headerLength = Encoding.UTF8.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));
        var startHtml = headerLength;
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(pre);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(post);

        return string.Format(headerTemplate, startHtml, endHtml, startFragment, endFragment) + pre + fragment + post;
    }

    /// <summary>
    /// Captures a platform ownership token used to invalidate managed delayed-rendering
    /// state when another application replaces the system clipboard.
    /// </summary>
    internal static string CaptureChangeToken()
    {
        if (PlatformFactory.IsWindows)
            return $"win32:{GetClipboardSequenceNumber()}";

        var hash = new HashCode();
        string[] formats = GetAvailableDataFormats();
        Array.Sort(formats, StringComparer.OrdinalIgnoreCase);
        foreach (string format in formats)
        {
            hash.Add(format, StringComparer.OrdinalIgnoreCase);
            byte[]? bytes = GetBinaryData(format);
            if (bytes == null)
                continue;
            hash.Add(bytes.Length);
            foreach (byte value in bytes)
                hash.Add(value);
        }

        // Android currently exposes only the text ClipData bridge and therefore has
        // no MIME list. Include text on every platform so the token remains useful
        // while a backend is operating in that reduced mode.
        hash.Add(GetTextCrossPlatform(), StringComparer.Ordinal);
        return $"native:{hash.ToHashCode()}";
    }

    #region Native Methods

    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint pFiles;
        public int ptX;
        public int ptY;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fNC;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fWide;
    }

    // ================================================================
    // Cross-platform clipboard (Linux/Android via jalium.native.platform)
    // ================================================================

    private static string? GetTextCrossPlatform()
    {
        int result = NativeMethods.ClipboardGetText(out nint textPtr);
        if (result != 0 || textPtr == nint.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUni(textPtr);
        }
        finally
        {
            NativeMethods.PlatformFree(textPtr);
        }
    }

    private static bool SetTextCrossPlatform(string text)
    {
        return NativeMethods.ClipboardSetText(text) == 0;
    }

    // ================================================================
    // Win32 P/Invoke
    // ================================================================

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint GlobalSize(nint hMem);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int WideCharToMultiByte(
        uint codePage,
        uint flags,
        string wideChars,
        int wideCharCount,
        [Out] byte[]? multiByteChars,
        int multiByteCount,
        nint defaultChar,
        nint usedDefaultChar);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int MultiByteToWideChar(
        uint codePage,
        uint flags,
        byte[] multiByteChars,
        int multiByteCount,
        [Out] char[]? wideChars,
        int wideCharCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint EnumClipboardFormats(uint format);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetClipboardFormatNameW(uint format, [Out] char[] lpszFormatName, int cchMaxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint DragQueryFileW(nint hDrop, uint iFile, char[]? lpszFile, uint cch);

    #endregion
}

/// <summary>
/// Internal system-clipboard boundary. The public WPF-compatible surface lives
/// exclusively on <see cref="global::Jalium.UI.Clipboard"/>.
/// </summary>
internal interface IClipboardProvider
{
    bool RequiresSta { get; }
    bool SupportsPersistence { get; }
    bool Clear();
    global::Jalium.UI.IDataObject? GetDataObject();
    bool SetDataObject(global::Jalium.UI.IDataObject dataObject, bool copy);
    bool Flush();
    bool IsCurrent(global::Jalium.UI.IDataObject dataObject);
}

internal static class ClipboardProvider
{
    internal static IClipboardProvider Current { get; } = Create();

    private static IClipboardProvider Create()
    {
        if (PlatformFactory.IsWindows)
            return new Win32ClipboardProvider();
        if (PlatformFactory.IsAndroid)
            return new AndroidClipboardProvider();
        if (PlatformFactory.IsLinux)
            return new LinuxClipboardProvider();
        if (OperatingSystem.IsMacOS())
            return new MacOSClipboardProvider();
        return new UnsupportedClipboardProvider();
    }
}

internal abstract class ClipboardProviderBase : IClipboardProvider
{
    private readonly object _gate = new();
    private global::Jalium.UI.IDataObject? _ownedDataObject;
    private string? _ownershipToken;

    public abstract bool RequiresSta { get; }
    public abstract bool SupportsPersistence { get; }

    public bool Clear()
    {
        if (!ClipboardPlatform.Clear())
            return false;

        lock (_gate)
        {
            _ownedDataObject = null;
            _ownershipToken = null;
        }

        return true;
    }

    public global::Jalium.UI.IDataObject? GetDataObject()
    {
        lock (_gate)
        {
            if (_ownedDataObject != null && IsOwnershipTokenCurrent())
                return _ownedDataObject;

            _ownedDataObject = null;
            _ownershipToken = null;
        }

        return ClipboardPlatform.GetDataObject();
    }

    public bool SetDataObject(global::Jalium.UI.IDataObject dataObject, bool copy)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        if (!ClipboardPlatform.SetDataObject(dataObject, copy))
            return false;

        lock (_gate)
        {
            _ownedDataObject = dataObject;
            _ownershipToken = ClipboardPlatform.CaptureChangeToken();
        }

        return !copy || Flush();
    }

    public virtual bool Flush()
    {
        // Win32 and Android receive eagerly materialized data. X11/Wayland keep
        // serving the copied native buffers for the lifetime of this process;
        // clipboard-manager handoff is not available in the current native ABI.
        return true;
    }

    public bool IsCurrent(global::Jalium.UI.IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        lock (_gate)
        {
            if (!ReferenceEquals(_ownedDataObject, dataObject))
                return false;

            if (IsOwnershipTokenCurrent())
                return true;

            _ownedDataObject = null;
            _ownershipToken = null;
            return false;
        }
    }

    private bool IsOwnershipTokenCurrent()
    {
        if (_ownershipToken == null)
            return false;

        try
        {
            return string.Equals(
                _ownershipToken,
                ClipboardPlatform.CaptureChangeToken(),
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class Win32ClipboardProvider : ClipboardProviderBase
{
    public override bool RequiresSta => true;
    public override bool SupportsPersistence => true;
}

internal sealed class LinuxClipboardProvider : ClipboardProviderBase
{
    public override bool RequiresSta => false;
    public override bool SupportsPersistence => false;
}

internal sealed class AndroidClipboardProvider : ClipboardProviderBase
{
    public override bool RequiresSta => false;
    public override bool SupportsPersistence => true;
}

internal sealed class MacOSClipboardProvider : ClipboardProviderBase
{
    public override bool RequiresSta => false;
    public override bool SupportsPersistence => true;
}

internal sealed class UnsupportedClipboardProvider : IClipboardProvider
{
    public bool RequiresSta => false;
    public bool SupportsPersistence => false;

    public bool Clear() => false;
    public global::Jalium.UI.IDataObject? GetDataObject() => null;
    public bool SetDataObject(global::Jalium.UI.IDataObject dataObject, bool copy) => false;
    public bool Flush() => false;
    public bool IsCurrent(global::Jalium.UI.IDataObject dataObject) => false;
}
