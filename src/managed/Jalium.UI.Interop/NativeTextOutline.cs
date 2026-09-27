using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Interop;

/// <summary>Shaped text ink as SVG path data, supplied by the platform text engine.</summary>
internal static partial class NativeTextOutline
{
    [LibraryImport(JaliumNativeLibraryNames.Core, EntryPoint = "jalium_text_copy_outline_path",
        StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int CopyWindows(string text, uint textLength,
        string family, uint familyLength, float fontSize, int fontWeight, int fontStyle,
        out float width, out float baseline, byte* buffer, int bufferSize);

    [LibraryImport(JaliumNativeLibraryNames.Text, EntryPoint = "jalium_text_copy_outline_path",
        StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int CopyUnix(string text, uint textLength,
        string family, uint familyLength, float fontSize, int fontWeight, int fontStyle,
        out float width, out float baseline, byte* buffer, int bufferSize);

    internal static unsafe bool TryGetPath(string text, string family, float fontSize,
        int fontWeight, int fontStyle, out string path, out float width, out float baseline)
    {
        path = string.Empty;
        width = baseline = 0;
        if (text.Length == 0 || text.Length > 4096 || family.Length is 0 or > 256 ||
            !float.IsFinite(fontSize) || fontSize <= 0 || fontSize > 35791) return false;
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsAndroid()) return false;

        try
        {
            var bytes = new byte[4096];
            int required;
            fixed (byte* buffer = bytes)
                required = Copy(buffer, bytes.Length, out width, out baseline);
            if (required is <= 0 or > 8 * 1024 * 1024) return false;
            if (required > bytes.Length)
            {
                bytes = new byte[required];
                fixed (byte* buffer = bytes)
                    if (Copy(buffer, bytes.Length, out width, out baseline) != required)
                        return false;
            }
            if (bytes[required - 1] != 0) return false;
            path = Encoding.UTF8.GetString(bytes, 0, required - 1);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }

        int Copy(byte* buffer, int bufferSize, out float resultWidth, out float resultBaseline)
            => OperatingSystem.IsWindows()
                ? CopyWindows(text, (uint)text.Length, family, (uint)family.Length,
                    fontSize, fontWeight, fontStyle, out resultWidth, out resultBaseline,
                    buffer, bufferSize)
                : CopyUnix(text, (uint)text.Length, family, (uint)family.Length,
                    fontSize, fontWeight, fontStyle, out resultWidth, out resultBaseline,
                    buffer, bufferSize);
    }
}
