using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides clipboard operations via Windows API.
/// </summary>
public static partial class Clipboard
{
    // Standard clipboard formats
    private const uint CF_TEXT = 1;
    private const uint CF_BITMAP = 2;
    private const uint CF_DIB = 8;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_DIBV5 = 17;
    private const uint CF_HDROP = 15;

    /// <summary>
    /// Gets text from the clipboard.
    /// </summary>
    /// <returns>The text from the clipboard, or null if no text is available.</returns>
    public static string? GetText()
    {
        if (!OpenClipboard(nint.Zero))
            return null;

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == nint.Zero)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == nint.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(ptr);
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

        if (!OpenClipboard(nint.Zero))
            return false;

        try
        {
            EmptyClipboard();

            // Calculate the number of bytes needed (including null terminator)
            var byteCount = (text.Length + 1) * sizeof(char);
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
        return IsClipboardFormatAvailable(CF_UNICODETEXT);
    }

    /// <summary>
    /// Clears the clipboard.
    /// </summary>
    /// <returns>True if successful, false otherwise.</returns>
    public static bool Clear()
    {
        if (!OpenClipboard(nint.Zero))
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
        return IsClipboardFormatAvailable(CF_HDROP);
    }

    /// <summary>
    /// Gets an image from the clipboard as raw bitmap data.
    /// </summary>
    /// <returns>A tuple of (width, height, stride, pixel data) or null if no image is available.</returns>
    public static (int Width, int Height, int Stride, byte[] Data)? GetImage()
    {
        if (!OpenClipboard(nint.Zero))
            return null;

        try
        {
            // Try CF_DIB first (device independent bitmap)
            var handle = GetClipboardData(CF_DIB);
            if (handle == nint.Zero)
                handle = GetClipboardData(CF_DIBV5);

            if (handle == nint.Zero)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == nint.Zero)
                return null;

            try
            {
                // Read BITMAPINFOHEADER
                var biSize = Marshal.ReadInt32(ptr, 0);
                var width = Marshal.ReadInt32(ptr, 4);
                var height = Marshal.ReadInt32(ptr, 8);
                var biBitCount = Marshal.ReadInt16(ptr, 14);
                var biCompression = Marshal.ReadInt32(ptr, 16);

                // Only support uncompressed 24-bit or 32-bit bitmaps
                if (biCompression != 0 || (biBitCount != 24 && biBitCount != 32))
                    return null;

                var bytesPerPixel = biBitCount / 8;
                var stride = (width * bytesPerPixel + 3) & ~3; // Align to 4-byte boundary
                var dataSize = Math.Abs(height) * stride;

                // Calculate offset to pixel data
                var colorTableSize = 0;
                var headerOffset = biSize + colorTableSize;

                // Copy pixel data
                var data = new byte[dataSize];
                Marshal.Copy(ptr + headerOffset, data, 0, dataSize);

                // DIBs are typically stored bottom-up, convert to top-down
                if (height > 0)
                {
                    var flippedData = new byte[dataSize];
                    for (int y = 0; y < height; y++)
                    {
                        var srcOffset = (height - 1 - y) * stride;
                        var dstOffset = y * stride;
                        Array.Copy(data, srcOffset, flippedData, dstOffset, stride);
                    }
                    data = flippedData;
                }

                return (width, Math.Abs(height), stride, data);
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
        if (!OpenClipboard(nint.Zero))
            return null;

        try
        {
            var handle = GetClipboardData(CF_HDROP);
            if (handle == nint.Zero)
                return null;

            var count = DragQueryFileW(handle, 0xFFFFFFFF, null, 0);
            if (count == 0)
                return null;

            var files = new string[count];
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFileW(handle, i, null, 0);
                var buffer = new char[length + 1];
                DragQueryFileW(handle, i, buffer, (uint)buffer.Length);
                files[i] = new string(buffer, 0, (int)length);
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

        if (!OpenClipboard(nint.Zero))
            return false;

        try
        {
            EmptyClipboard();

            // Calculate buffer size: DROPFILES structure + null-terminated strings + final null
            var totalLength = Marshal.SizeOf<DROPFILES>();
            foreach (var file in files)
            {
                totalLength += (file.Length + 1) * sizeof(char);
            }
            totalLength += sizeof(char); // Final null terminator

            var hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)totalLength);
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
                // Write DROPFILES header
                var dropFiles = new DROPFILES
                {
                    pFiles = (uint)Marshal.SizeOf<DROPFILES>(),
                    fWide = true
                };
                Marshal.StructureToPtr(dropFiles, ptr, false);

                // Write file paths
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
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Gets the data object from the clipboard.
    /// </summary>
    /// <returns>A ClipboardDataObject containing the clipboard data.</returns>
    public static ClipboardDataObject GetDataObject()
    {
        var dataObject = new ClipboardDataObject();

        if (ContainsText())
        {
            dataObject.SetData(DataFormats.Text, GetText());
        }

        if (ContainsFileDropList())
        {
            dataObject.SetData(DataFormats.FileDrop, GetFileDropList());
        }

        return dataObject;
    }

    /// <summary>
    /// Sets data to the clipboard.
    /// </summary>
    /// <param name="data">The data object to set.</param>
    /// <param name="copy">Whether to leave data on clipboard after application exits.</param>
    public static void SetDataObject(object data, bool copy = true)
    {
        if (data is string text)
        {
            SetText(text);
        }
        else if (data is string[] files)
        {
            SetFileDropList(files);
        }
        else if (data is ClipboardDataObject dataObj)
        {
            if (dataObj.GetDataPresent(DataFormats.Text))
            {
                SetText(dataObj.GetData(DataFormats.Text) as string);
            }
            if (dataObj.GetDataPresent(DataFormats.FileDrop))
            {
                SetFileDropList(dataObj.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>());
            }
        }
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint DragQueryFileW(nint hDrop, uint iFile, char[]? lpszFile, uint cch);

    #endregion
}

/// <summary>
/// Provides standard clipboard data format identifiers.
/// </summary>
public static class DataFormats
{
    /// <summary>
    /// Specifies the standard ANSI text format.
    /// </summary>
    public const string Text = "Text";

    /// <summary>
    /// Specifies the standard Windows Unicode text format.
    /// </summary>
    public const string UnicodeText = "UnicodeText";

    /// <summary>
    /// Specifies the standard rich text format (RTF) format.
    /// </summary>
    public const string Rtf = "Rich Text Format";

    /// <summary>
    /// Specifies the standard HTML format.
    /// </summary>
    public const string Html = "HTML Format";

    /// <summary>
    /// Specifies the Windows file drop format.
    /// </summary>
    public const string FileDrop = "FileDrop";

    /// <summary>
    /// Specifies a Windows bitmap format.
    /// </summary>
    public const string Bitmap = "Bitmap";

    /// <summary>
    /// Specifies the Windows device-independent bitmap (DIB) format.
    /// </summary>
    public const string Dib = "DeviceIndependentBitmap";

    /// <summary>
    /// Specifies a format that encapsulates any type of serializable data objects.
    /// </summary>
    public const string Serializable = "Serializable";
}

/// <summary>
/// Implements a basic data object that stores data in multiple formats.
/// </summary>
public sealed class ClipboardDataObject : IDataObject
{
    private readonly Dictionary<string, object?> _data = new();

    /// <summary>
    /// Gets data from the data object.
    /// </summary>
    /// <param name="format">The format of the data to retrieve.</param>
    /// <returns>The data in the specified format, or null if not available.</returns>
    public object? GetData(string format)
    {
        return _data.GetValueOrDefault(format);
    }

    /// <summary>
    /// Gets data from the data object, converting it to the specified type if necessary.
    /// </summary>
    /// <param name="format">The format of the data to retrieve.</param>
    /// <param name="autoConvert">Whether to automatically convert the data to the specified format.</param>
    /// <returns>The data in the specified format, or null if not available.</returns>
    public object? GetData(string format, bool autoConvert)
    {
        return GetData(format);
    }

    /// <summary>
    /// Gets data from the data object.
    /// </summary>
    /// <param name="format">The type of the data to retrieve.</param>
    /// <returns>The data in the specified format, or null if not available.</returns>
    public object? GetData(Type format)
    {
        return GetData(format.FullName ?? format.Name);
    }

    /// <summary>
    /// Checks if data of the specified format is present.
    /// </summary>
    /// <param name="format">The format to check.</param>
    /// <returns>True if data of the specified format is present; otherwise, false.</returns>
    public bool GetDataPresent(string format)
    {
        return _data.ContainsKey(format);
    }

    /// <summary>
    /// Checks if data of the specified format is present.
    /// </summary>
    /// <param name="format">The format to check.</param>
    /// <param name="autoConvert">Whether to check for convertible formats.</param>
    /// <returns>True if data of the specified format is present; otherwise, false.</returns>
    public bool GetDataPresent(string format, bool autoConvert)
    {
        return GetDataPresent(format);
    }

    /// <summary>
    /// Checks if data of the specified type is present.
    /// </summary>
    /// <param name="format">The type to check.</param>
    /// <returns>True if data of the specified type is present; otherwise, false.</returns>
    public bool GetDataPresent(Type format)
    {
        return GetDataPresent(format.FullName ?? format.Name);
    }

    /// <summary>
    /// Gets the formats in which the data is stored.
    /// </summary>
    /// <returns>An array of format names.</returns>
    public string[] GetFormats()
    {
        return _data.Keys.ToArray();
    }

    /// <summary>
    /// Gets the formats in which the data is stored.
    /// </summary>
    /// <param name="autoConvert">Whether to include convertible formats.</param>
    /// <returns>An array of format names.</returns>
    public string[] GetFormats(bool autoConvert)
    {
        return GetFormats();
    }

    /// <summary>
    /// Stores data in the data object.
    /// </summary>
    /// <param name="format">The format of the data.</param>
    /// <param name="data">The data to store.</param>
    public void SetData(string format, object? data)
    {
        _data[format] = data;
    }

    /// <summary>
    /// Stores data in the data object.
    /// </summary>
    /// <param name="format">The format of the data.</param>
    /// <param name="autoConvert">Whether to convert the data to other formats.</param>
    /// <param name="data">The data to store.</param>
    public void SetData(string format, object? data, bool autoConvert)
    {
        SetData(format, data);
    }

    /// <summary>
    /// Stores data in the data object.
    /// </summary>
    /// <param name="format">The type of the data.</param>
    /// <param name="data">The data to store.</param>
    public void SetData(Type format, object? data)
    {
        SetData(format.FullName ?? format.Name, data);
    }

    /// <summary>
    /// Stores data in the data object, inferring the format from the data type.
    /// </summary>
    /// <param name="data">The data to store.</param>
    public void SetData(object data)
    {
        if (data != null)
        {
            SetData(data.GetType(), data);
        }
    }
}

/// <summary>
/// Provides a format-independent mechanism for transferring data.
/// </summary>
public interface IDataObject
{
    /// <summary>
    /// Gets data from the data object.
    /// </summary>
    object? GetData(string format);

    /// <summary>
    /// Gets data from the data object.
    /// </summary>
    object? GetData(string format, bool autoConvert);

    /// <summary>
    /// Gets data from the data object.
    /// </summary>
    object? GetData(Type format);

    /// <summary>
    /// Checks if data of the specified format is present.
    /// </summary>
    bool GetDataPresent(string format);

    /// <summary>
    /// Checks if data of the specified format is present.
    /// </summary>
    bool GetDataPresent(string format, bool autoConvert);

    /// <summary>
    /// Checks if data of the specified type is present.
    /// </summary>
    bool GetDataPresent(Type format);

    /// <summary>
    /// Gets the formats in which the data is stored.
    /// </summary>
    string[] GetFormats();

    /// <summary>
    /// Gets the formats in which the data is stored.
    /// </summary>
    string[] GetFormats(bool autoConvert);

    /// <summary>
    /// Stores data in the data object.
    /// </summary>
    void SetData(string format, object? data);

    /// <summary>
    /// Stores data in the data object.
    /// </summary>
    void SetData(string format, object? data, bool autoConvert);

    /// <summary>
    /// Stores data in the data object.
    /// </summary>
    void SetData(Type format, object? data);

    /// <summary>
    /// Stores data in the data object.
    /// </summary>
    void SetData(object data);
}
