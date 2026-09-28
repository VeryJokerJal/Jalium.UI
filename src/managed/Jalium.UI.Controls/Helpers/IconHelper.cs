using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using static Jalium.UI.Interop.Win32.Win32GdiMethods;

namespace Jalium.UI.Controls.Helpers;

internal readonly record struct ProcessIconPixels(
    byte[] Pixels,
    int Width,
    int Height,
    int Stride);

internal enum IconGroupResourceState
{
    Unknown,
    Absent,
    Present,
}

/// <summary>
/// Extracts an application icon from an executable and encodes it as PNG
/// using only Win32 P/Invoke — no System.Drawing dependency.
/// </summary>
internal static partial class IconHelper
{
    private static readonly Lazy<IconGroupResourceState> s_defaultProcessIconGroupResources =
        new(static () => OperatingSystem.IsWindows()
            ? GetCurrentProcessIconGroupResourceState()
            : IconGroupResourceState.Unknown);

    private static readonly Lazy<DefaultProcessIconHandles> s_defaultProcessIconHandles =
        new(static () => OperatingSystem.IsWindows()
            ? CreateDefaultProcessIconHandles()
            : default);

    private static readonly Lazy<ProcessIconPixels?> s_defaultProcessIconFallbackPixels =
        new(static () => OperatingSystem.IsWindows()
            ? CreateDefaultProcessIconFallbackPixels()
            : null);

    internal static byte[]? ExtractProcessIconAsPng(string exePath)
    {
        var pixels = ExtractProcessIconPixels(exePath);
        if (pixels == null)
            return null;

        var source = BitmapSource.Create(
            pixels.Value.Width, pixels.Value.Height, 96, 96, PixelFormat.Bgra32, null,
            pixels.Value.Pixels, pixels.Value.Stride);
        var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(source) } };
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    internal static ProcessIconPixels? ExtractProcessIconPixels(string exePath)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var resourceState = IsCurrentProcessExecutable(exePath)
            ? s_defaultProcessIconGroupResources.Value
            : IconGroupResourceState.Unknown;
        return ExtractProcessIconPixelsWindows(exePath, resourceState);
    }

    /// <summary>
    /// Gets the stock icon without scheduling file extraction when the current
    /// executable is known to have no icon group. An inconclusive resource probe
    /// leaves the asynchronous extraction path unchanged.
    /// </summary>
    internal static bool TryGetDefaultProcessIconPixelsSynchronously(out ProcessIconPixels? pixels)
    {
        pixels = null;
        if (!OperatingSystem.IsWindows())
            return false;

        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            return false;

        return TryGetUncustomizedProcessIconPixels(
            path, s_defaultProcessIconGroupResources.Value, out pixels);
    }

    internal static bool TryGetUncustomizedProcessIconPixels(
        string path, IconGroupResourceState resourceState, out ProcessIconPixels? pixels)
    {
        pixels = null;
        if (!OperatingSystem.IsWindows() || !ShouldSkipCurrentProcessIconExtraction(path, resourceState))
            return false;

        try
        {
            pixels = ExtractProcessIconPixelsWindows(path, resourceState);
        }
        catch
        {
            // Default icons are best-effort. Match the asynchronous path, which
            // treats extraction failure as an absent icon rather than a failed window.
        }
        return true;
    }

    /// <summary>
    /// Returns raw <c>HICON</c> handles for the running executable, sized to the system's large
    /// and small icon metrics, for use as a window class's <c>hIcon</c> / <c>hIconSm</c>.
    /// Either component is <c>0</c> when the icon cannot be extracted.
    /// </summary>
    /// <remarks>
    /// The returned handles are deliberately NOT destroyed by the caller: a window class owns its
    /// icons for as long as the class is registered, and the Jalium window class lives for the
    /// life of the process. Destroying them would leave the class pointing at freed handles.
    ///
    /// <para>These are what Windows draws for the window itself — the taskbar thumbnail header,
    /// Alt-Tab and the window menu — which is a DIFFERENT source from the taskbar *button*: the
    /// shell resolves that one from the executable independently, which is why an app can show a
    /// correct taskbar button and a stale default window icon at the same time.</para>
    /// </remarks>
    internal static (nint Large, nint Small) ExtractProcessIconHandles()
    {
        if (!OperatingSystem.IsWindows())
            return (0, 0);

        var handles = s_defaultProcessIconHandles.Value;
        return (handles.Large, handles.Small);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static DefaultProcessIconHandles CreateDefaultProcessIconHandles()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return default;

        var handles = ResolveDefaultProcessIconHandles(
            exePath,
            s_defaultProcessIconGroupResources.Value);
        return new DefaultProcessIconHandles(handles.Large, handles.Small);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static (nint Large, nint Small) ResolveDefaultProcessIconHandles(
        string exePath,
        IconGroupResourceState resourceState)
    {
        if (ShouldSkipCurrentProcessIconExtraction(exePath, resourceState))
        {
            // A zero class icon makes Windows draw the same stock application glyph that
            // the old failed extraction path produced, without retaining a shared HICON.
            return (0, 0);
        }

        var large = ExtractIconHandleWindows(
            exePath,
            GetSystemMetrics(SM_CXICON),
            GetSystemMetrics(SM_CYICON));
        var small = ExtractIconHandleWindows(
            exePath,
            GetSystemMetrics(SM_CXSMICON),
            GetSystemMetrics(SM_CYSMICON));

        if (large == 0 || small == 0)
        {
            // ExtractIconExW returns both standard sizes in one pass. Use that single pass
            // only when user32 could not supply a class icon, then retain just the handles
            // installed into the process-wide window class.
            var fallback = ExtractProcessShellIconHandles(exePath);

            if (large == 0)
            {
                large = fallback.Large;
            }

            if (small == 0)
            {
                if (fallback.Small != 0)
                {
                    small = fallback.Small;
                }
                else if (fallback.Large != 0)
                {
                    small = fallback.Large;
                }
            }

            if (fallback.Large != 0 && fallback.Large != large && fallback.Large != small)
                DestroyIcon(fallback.Large);
            if (fallback.Small != 0 &&
                fallback.Small != fallback.Large &&
                fallback.Small != large &&
                fallback.Small != small)
                DestroyIcon(fallback.Small);
        }

        return (large, small);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static nint ExtractIconHandleWindows(string exePath, int cx, int cy)
    {
        if (cx <= 0 || cy <= 0)
        {
            cx = cy = 32;
        }

        // Keep the common path entirely in user32. The process-wide Shell fallback is
        // resolved once by CreateDefaultProcessIconHandles only when either size is absent.
        var extracted = PrivateExtractIconsW(exePath, 0, cx, cy, out var hIcon, 0, 1, 0);
        if (extracted != 0 && hIcon != 0)
        {
            return hIcon;
        }

        if (hIcon != 0)
        {
            DestroyIcon(hIcon);
        }

        return 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static ProcessIconPixels? ExtractProcessIconPixelsWindows(
        string exePath,
        IconGroupResourceState resourceState)
    {
        nint hIcon = 0;
        bool isSharedIcon = false;
        try
        {
            if (!ShouldSkipCurrentProcessIconExtraction(exePath, resourceState))
            {
                // Prefer the highest-resolution frame. ExtractIconExW only ever returns the
                // system "large" icon (SM_CXICON, typically 32x32); a finely-drawn round logo
                // taken at 32px and shown in the title bar reads as a coarse low-poly shape —
                // the reported "circle became a heptagon". PrivateExtractIconsW lets us request
                // 256x256 explicitly, so Windows returns the icon's largest/closest frame, which
                // stays smooth when scaled down to the title-bar size.
                var extracted = PrivateExtractIconsW(exePath, 0, 256, 256, out hIcon, 0, 1, 0);
                if (extracted == 0 || hIcon == 0)
                {
                    if (hIcon != 0)
                    {
                        DestroyIcon(hIcon);
                    }
                    hIcon = 0;

                    if (IsCurrentProcessExecutable(exePath))
                    {
                        // Reuse the process-wide class-icon cache when the high-resolution
                        // request fails. If class registration has not happened yet, the same
                        // cache resolves the standard icons once and retains them for later.
                        var cachedFallback = s_defaultProcessIconFallbackPixels.Value;
                        if (cachedFallback.HasValue)
                        {
                            return cachedFallback.Value;
                        }
                    }
                    else
                    {
                        // Arbitrary executable paths retain the historical Shell fallback.
                        var count = ExtractIconExW(exePath, 0, out var hIconLarge, out var hIconSmall, 1);
                        if (count != 0)
                        {
                            hIcon = hIconLarge != 0 ? hIconLarge : hIconSmall;
                            var unused = hIcon == hIconLarge ? hIconSmall : hIconLarge;
                            if (unused != 0 && unused != hIcon)
                            {
                                DestroyIcon(unused);
                            }
                        }
                        else
                        {
                            if (hIconLarge != 0) DestroyIcon(hIconLarge);
                            if (hIconSmall != 0 && hIconSmall != hIconLarge) DestroyIcon(hIconSmall);
                        }
                    }
                }
            }

            if (hIcon == 0)
            {
                // Fallback 2: the shared system application icon. A shared icon must NOT be
                // passed to DestroyIcon, so flag it so the finally block leaves it alone.
                hIcon = LoadIconW(0, IDI_APPLICATION);
                isSharedIcon = true;
                if (hIcon == 0)
                {
                    return null;
                }
            }

            return IconHandleToPixels(hIcon);
        }
        finally
        {
            if (hIcon != 0 && !isSharedIcon) DestroyIcon(hIcon);
        }
    }

    private static bool IsCurrentProcessExecutable(string exePath)
    {
        var processPath = Environment.ProcessPath;
        return !string.IsNullOrEmpty(processPath) &&
               string.Equals(processPath, exePath, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldSkipCurrentProcessIconExtraction(
        string exePath,
        IconGroupResourceState resourceState)
    {
        return resourceState == IconGroupResourceState.Absent &&
               IsCurrentProcessExecutable(exePath);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IconGroupResourceState GetCurrentProcessIconGroupResourceState()
    {
        try
        {
            var mainModule = GetModuleHandleW(null);
            return mainModule == 0
                ? IconGroupResourceState.Unknown
                : ProbeIconGroupResources(mainModule);
        }
        catch
        {
            // Resource inspection is only an optimization. Any loader, marshalling, or
            // validation failure must preserve the established extraction fallbacks.
            return IconGroupResourceState.Unknown;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static unsafe IconGroupResourceState ProbeIconGroupResources(nint module)
    {
        if (module == 0)
        {
            return IconGroupResourceState.Unknown;
        }

        try
        {
            int found = 0;
            Marshal.SetLastPInvokeError(0);
            bool completed = EnumResourceNamesExW(
                module,
                RT_GROUP_ICON,
                (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)&RecordIconGroupResource,
                (nint)(&found),
                // LangId 0 follows the loader's current language fallback. Searching both
                // the language-neutral module and its associated MUI keeps a localized-only
                // icon group from being mistaken for an iconless executable.
                RESOURCE_ENUM_LN | RESOURCE_ENUM_MUI | RESOURCE_ENUM_VALIDATE,
                0);

            if (found != 0)
            {
                return IconGroupResourceState.Present;
            }

            int error = Marshal.GetLastPInvokeError();
            // EnumResourceNamesExW also returns false for callback cancellation and malformed
            // resources. Only the documented missing-type error proves that no group exists.
            return !completed && error == ERROR_RESOURCE_TYPE_NOT_FOUND
                ? IconGroupResourceState.Absent
                : IconGroupResourceState.Unknown;
        }
        catch
        {
            return IconGroupResourceState.Unknown;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int RecordIconGroupResource(
        nint module,
        nint resourceType,
        nint resourceName,
        nint state)
    {
        *(int*)state = 1;
        return 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static DefaultProcessIconHandles ExtractProcessShellIconHandles(string exePath)
    {
        var count = ExtractIconExW(exePath, 0, out var large, out var small, 1);
        if (count == 0)
        {
            if (large != 0) DestroyIcon(large);
            if (small != 0 && small != large) DestroyIcon(small);
            return default;
        }

        return new DefaultProcessIconHandles(large, small);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static ProcessIconPixels? CreateDefaultProcessIconFallbackPixels()
    {
        var handles = s_defaultProcessIconHandles.Value;
        var pixelSource = handles.Large != 0 ? handles.Large : handles.Small;
        if (pixelSource == 0)
        {
            return null;
        }

        try
        {
            return IconHandleToPixels(pixelSource);
        }
        catch
        {
            // Icon pixels are cosmetic. The cached HICON remains valid for the registered
            // window class even when bitmap conversion is unavailable.
            return null;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static ProcessIconPixels? IconHandleToPixels(nint hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo))
        {
            return null;
        }

        nint hdc = 0;
        try
        {
            var hbmColor = iconInfo.hbmColor;
            if (hbmColor == 0)
            {
                return null;
            }

            // Get bitmap dimensions.
            var bmpSize = Marshal.SizeOf<BITMAP>();
            var bmp = new BITMAP();
            if (GetObjectW(hbmColor, bmpSize, ref bmp) == 0)
            {
                return null;
            }

            var width = bmp.bmWidth;
            var height = bmp.bmHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            // Prepare BITMAPINFOHEADER for 32-bit BGRA.
            var bih = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };

            var pixelData = new byte[width * height * 4];
            hdc = CreateCompatibleDC(0);
            if (hdc == 0)
            {
                return null;
            }

            var lines = GetDIBits(hdc, hbmColor, 0, (uint)height, pixelData, ref bih, 0);
            if (lines == 0)
            {
                return null;
            }

            // Check if the color bitmap already has an alpha channel.
            var hasAlpha = false;
            for (var i = 3; i < pixelData.Length; i += 4)
            {
                if (pixelData[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha && iconInfo.hbmMask != 0)
            {
                // Read the mask bitmap and apply it as alpha.
                var maskData = new byte[width * height * 4];
                var maskBih = bih;
                var maskLines = GetDIBits(hdc, iconInfo.hbmMask, 0, (uint)height, maskData, ref maskBih, 0);
                if (maskLines > 0)
                {
                    for (var i = 0; i < width * height; i++)
                    {
                        // In AND mask: 0 = opaque, 1 = transparent (when reading as 32bpp, 0x00 = opaque, 0xFF = transparent).
                        pixelData[i * 4 + 3] = (byte)(maskData[i * 4] == 0 ? 255 : 0);
                    }
                }
                else
                {
                    // If mask read fails, make fully opaque.
                    for (var i = 3; i < pixelData.Length; i += 4)
                    {
                        pixelData[i] = 255;
                    }
                }
            }

            return new ProcessIconPixels(pixelData, width, height, width * 4);
        }
        finally
        {
            if (hdc != 0) DeleteDC(hdc);
            if (iconInfo.hbmColor != 0) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != 0) DeleteObject(iconInfo.hbmMask);
        }
    }

    #region Win32 P/Invoke

    private const nint IDI_APPLICATION = 32512;
    private const nint RT_GROUP_ICON = 14;

    private const uint RESOURCE_ENUM_LN = 0x0001;
    private const uint RESOURCE_ENUM_MUI = 0x0002;
    private const uint RESOURCE_ENUM_VALIDATE = 0x0008;

    private const int ERROR_RESOURCE_TYPE_NOT_FOUND = 1813;

    // System icon metrics, used to size the window class's hIcon / hIconSm.
    private const int SM_CXICON = 11;
    private const int SM_CYICON = 12;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("kernel32.dll", EntryPoint = "EnumResourceNamesExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumResourceNamesExW(
        nint module,
        nint resourceType,
        nint callback,
        nint state,
        uint flags,
        ushort language);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconExW(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    // Extracts an icon at an explicit pixel size (we ask for 256x256) instead of the fixed
    // system large-icon size ExtractIconExW returns. Returns the number of icons extracted.
    [LibraryImport("user32.dll", EntryPoint = "PrivateExtractIconsW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PrivateExtractIconsW(string szFileName, int nIconIndex, int cxIcon, int cyIcon, out nint phicon, nint piconId, uint nIcons, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial nint LoadIconW(nint hInstance, nint lpIconName);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObjectW(nint h, int c, ref BITMAP pv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    private readonly record struct DefaultProcessIconHandles(nint Large, nint Small);

    #endregion
}
