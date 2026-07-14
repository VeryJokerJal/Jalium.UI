using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Controls.Platform;

internal enum LinuxXSettingKind : byte
{
    Integer = 0,
    String = 1,
    Color = 2,
}

internal readonly record struct LinuxXSettingValue(
    LinuxXSettingKind Kind,
    int Integer,
    string? String);

/// <summary>
/// Reads the freedesktop XSETTINGS selection without depending on GTK. The
/// parser is kept separate from Xlib access so malformed/untrusted properties
/// can be validated with deterministic tests.
/// </summary>
internal static partial class LinuxXSettings
{
    private const string X11Library = "libX11.so.6";
    private const int Success = 0;
    private const int MaxPropertyBytes = 1024 * 1024;
    private const int MaxSettingCount = 4096;
    private const byte MsbFirst = 0;
    private const byte LsbFirst = 1;
    private static readonly object s_xlibGate = new();
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static bool TryRead(out IReadOnlyDictionary<string, LinuxXSettingValue> settings)
    {
        settings = new Dictionary<string, LinuxXSettingValue>(StringComparer.Ordinal);
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return false;

        try
        {
            lock (s_xlibGate)
            {
                nint display = XOpenDisplay(null);
                if (display == 0)
                    return false;

                nint previousErrorHandler;
                unsafe
                {
                    previousErrorHandler = XSetErrorHandler(
                        (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&IgnoreXError);
                }

                bool grabbed = false;
                nint propertyData = 0;
                try
                {
                    _ = XGrabServer(display);
                    grabbed = true;

                    int screen = XDefaultScreen(display);
                    nint selection = XInternAtom(display, $"_XSETTINGS_S{screen}", 1);
                    if (selection == 0)
                        return false;
                    nuint owner = XGetSelectionOwner(display, selection);
                    if (owner == 0)
                        return false;

                    nint property = XInternAtom(display, "_XSETTINGS_SETTINGS", 1);
                    if (property == 0)
                        return false;

                    int status = XGetWindowProperty(
                        display,
                        owner,
                        property,
                        0,
                        MaxPropertyBytes / 4,
                        0,
                        0,
                        out nint actualType,
                        out int actualFormat,
                        out nuint itemCount,
                        out nuint bytesAfter,
                        out propertyData);
                    _ = XSync(display, 0);
                    if (status != Success || actualType != property || actualFormat != 8 ||
                        bytesAfter != 0 || itemCount == 0 || itemCount > MaxPropertyBytes ||
                        propertyData == 0)
                    {
                        return false;
                    }

                    var bytes = new byte[(int)itemCount];
                    Marshal.Copy(propertyData, bytes, 0, bytes.Length);
                    return TryParse(bytes, out settings);
                }
                finally
                {
                    if (propertyData != 0)
                        _ = XFree(propertyData);
                    if (grabbed)
                    {
                        _ = XUngrabServer(display);
                        _ = XSync(display, 0);
                    }
                    _ = XCloseDisplay(display);
                    _ = XSetErrorHandler(previousErrorHandler);
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or
                                          EntryPointNotFoundException or
                                          BadImageFormatException)
        {
            return false;
        }
    }

    internal static bool TryGetInteger(
        IReadOnlyDictionary<string, LinuxXSettingValue>? settings,
        string name,
        out int value)
    {
        if (settings != null && settings.TryGetValue(name, out LinuxXSettingValue setting) &&
            setting.Kind == LinuxXSettingKind.Integer)
        {
            value = setting.Integer;
            return true;
        }

        value = 0;
        return false;
    }

    internal static bool TryGetString(
        IReadOnlyDictionary<string, LinuxXSettingValue>? settings,
        string name,
        out string value)
    {
        if (settings != null && settings.TryGetValue(name, out LinuxXSettingValue setting) &&
            setting.Kind == LinuxXSettingKind.String && setting.String != null)
        {
            value = setting.String;
            return true;
        }

        value = string.Empty;
        return false;
    }

    internal static bool TryParse(
        ReadOnlySpan<byte> data,
        out IReadOnlyDictionary<string, LinuxXSettingValue> settings)
    {
        var result = new Dictionary<string, LinuxXSettingValue>(StringComparer.Ordinal);
        settings = result;
        if (data.Length < 12 || (data[0] != MsbFirst && data[0] != LsbFirst))
            return false;

        bool littleEndian = data[0] == LsbFirst;
        int offset = 4;
        if (!TryReadUInt32(data, ref offset, littleEndian, out _) ||
            !TryReadUInt32(data, ref offset, littleEndian, out uint settingCount) ||
            settingCount > MaxSettingCount)
        {
            return false;
        }

        try
        {
            for (uint settingIndex = 0; settingIndex < settingCount; settingIndex++)
            {
                if (offset > data.Length - 4)
                    return false;
                var kind = (LinuxXSettingKind)data[offset];
                offset += 2; // type + one byte of padding
                if (!TryReadUInt16(data, ref offset, littleEndian, out ushort nameLength) ||
                    offset > data.Length - nameLength)
                {
                    return false;
                }

                ReadOnlySpan<byte> nameBytes = data.Slice(offset, nameLength);
                if (!IsValidSettingName(nameBytes))
                    return false;
                string name = Encoding.ASCII.GetString(nameBytes);
                offset += nameLength;
                if (!TryAlign4(data.Length, ref offset) ||
                    !TryReadUInt32(data, ref offset, littleEndian, out _))
                {
                    return false;
                }

                switch (kind)
                {
                    case LinuxXSettingKind.Integer:
                        if (!TryReadUInt32(data, ref offset, littleEndian, out uint integer))
                            return false;
                        if (!result.TryAdd(
                                name,
                                new LinuxXSettingValue(kind, unchecked((int)integer), null)))
                        {
                            return false;
                        }
                        break;
                    case LinuxXSettingKind.String:
                        if (!TryReadUInt32(data, ref offset, littleEndian, out uint stringLength) ||
                            stringLength > int.MaxValue || offset > data.Length - (int)stringLength)
                        {
                            return false;
                        }
                        string text = s_strictUtf8.GetString(data.Slice(offset, (int)stringLength));
                        offset += (int)stringLength;
                        if (!TryAlign4(data.Length, ref offset))
                            return false;
                        if (!result.TryAdd(name, new LinuxXSettingValue(kind, 0, text)))
                            return false;
                        break;
                    case LinuxXSettingKind.Color:
                        for (int channel = 0; channel < 4; channel++)
                        {
                            if (!TryReadUInt16(data, ref offset, littleEndian, out _))
                                return false;
                        }
                        if (!result.TryAdd(name, new LinuxXSettingValue(kind, 0, null)))
                            return false;
                        break;
                    default:
                        return false;
                }
            }
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return true;
    }

    private static bool IsValidSettingName(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || name[0] == (byte)'/' || name[^1] == (byte)'/')
            return false;

        bool segmentStart = true;
        foreach (byte character in name)
        {
            if (character == (byte)'/')
            {
                if (segmentStart)
                    return false;
                segmentStart = true;
                continue;
            }

            bool alpha = character is >= (byte)'A' and <= (byte)'Z' or
                         >= (byte)'a' and <= (byte)'z';
            bool digit = character is >= (byte)'0' and <= (byte)'9';
            if (!alpha && !digit && character != (byte)'_')
                return false;
            if (segmentStart && digit)
                return false;
            segmentStart = false;
        }
        return !segmentStart;
    }

    private static bool TryReadUInt16(
        ReadOnlySpan<byte> data,
        ref int offset,
        bool littleEndian,
        out ushort value)
    {
        if (offset > data.Length - sizeof(ushort))
        {
            value = 0;
            return false;
        }
        ReadOnlySpan<byte> bytes = data.Slice(offset, sizeof(ushort));
        value = littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);
        offset += sizeof(ushort);
        return true;
    }

    private static bool TryReadUInt32(
        ReadOnlySpan<byte> data,
        ref int offset,
        bool littleEndian,
        out uint value)
    {
        if (offset > data.Length - sizeof(uint))
        {
            value = 0;
            return false;
        }
        ReadOnlySpan<byte> bytes = data.Slice(offset, sizeof(uint));
        value = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        offset += sizeof(uint);
        return true;
    }

    private static bool TryAlign4(int length, ref int offset)
    {
        int aligned = (offset + 3) & ~3;
        if (aligned < offset || aligned > length)
            return false;
        offset = aligned;
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IgnoreXError(nint display, nint errorEvent) => 0;

    [LibraryImport(X11Library, EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string? displayName);

    [LibraryImport(X11Library, EntryPoint = "XCloseDisplay")]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(X11Library, EntryPoint = "XDefaultScreen")]
    private static partial int XDefaultScreen(nint display);

    [LibraryImport(X11Library, EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XInternAtom(nint display, string atomName, int onlyIfExists);

    [LibraryImport(X11Library, EntryPoint = "XGetSelectionOwner")]
    private static partial nuint XGetSelectionOwner(nint display, nint selection);

    [LibraryImport(X11Library, EntryPoint = "XGetWindowProperty")]
    private static partial int XGetWindowProperty(
        nint display,
        nuint window,
        nint property,
        nint longOffset,
        nint longLength,
        int delete,
        nint requestedType,
        out nint actualType,
        out int actualFormat,
        out nuint itemCount,
        out nuint bytesAfter,
        out nint propertyData);

    [LibraryImport(X11Library, EntryPoint = "XFree")]
    private static partial int XFree(nint data);

    [LibraryImport(X11Library, EntryPoint = "XGrabServer")]
    private static partial int XGrabServer(nint display);

    [LibraryImport(X11Library, EntryPoint = "XUngrabServer")]
    private static partial int XUngrabServer(nint display);

    [LibraryImport(X11Library, EntryPoint = "XSync")]
    private static partial int XSync(nint display, int discard);

    [LibraryImport(X11Library, EntryPoint = "XSetErrorHandler")]
    private static partial nint XSetErrorHandler(nint handler);
}
