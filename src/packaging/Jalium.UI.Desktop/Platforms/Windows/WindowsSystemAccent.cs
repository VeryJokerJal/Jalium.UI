using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using JaliumColor = Jalium.UI.Media.Color;

namespace Jalium.UI.Desktop.Platforms.Windows;

/// <summary>
/// Reads the active Windows accent color (Settings → Personalization → Colors)
/// from the user's DWM registry slot. The value is shaped exactly the way
/// <see cref="Jalium.UI.Controls.Themes.ThemeManager.SystemAccentResolver"/>
/// expects — a nullable <see cref="JaliumColor"/>, with <c>null</c> meaning
/// "no opinion, keep the framework default".
///
/// This file is the only place in the framework that touches the Win32
/// registry; it lives in the <c>Jalium.UI.Desktop</c> package so the
/// cross-platform core never drags in <see cref="Microsoft.Win32"/>.
///
/// The accent is exposed in HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\DWM
/// as a DWORD whose little-endian byte order is R, G, B, A — i.e. the int
/// value reads as <c>0xAABBGGRR</c>. DWM rewrites the key whenever the user
/// picks a new accent in Settings, so reading once at startup is enough to
/// reflect the current choice.
/// </summary>
internal static class WindowsSystemAccent
{
    private const string DwmKeyPath = @"SOFTWARE\Microsoft\Windows\DWM";
    private const string AccentColorValueName = "AccentColor";

    /// <summary>
    /// Resolver entry point handed to ThemeManager.SystemAccentResolver.
    /// Returns <c>null</c> on non-Windows hosts, when the registry value
    /// is missing, or when access is denied.
    /// </summary>
    public static JaliumColor? Resolve()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        return ReadFromRegistry();
    }

    [SupportedOSPlatform("windows")]
    private static JaliumColor? ReadFromRegistry()
    {
        try
        {
            using var dwmKey = Registry.CurrentUser.OpenSubKey(DwmKeyPath);
            if (dwmKey == null)
                return null;

            var raw = dwmKey.GetValue(AccentColorValueName);
            if (raw is not int dword)
                return null;

            return DecodeAccentDword(unchecked((uint)dword));
        }
        catch
        {
            // Locked-down profiles or aggressive antivirus can refuse the read;
            // the caller treats null as "no platform accent, keep default".
            return null;
        }
    }

    /// <summary>
    /// Decodes the DWM byte order:
    /// <c>0xAABBGGRR</c> → bits 0..7 = R, 8..15 = G, 16..23 = B, 24..31 = A.
    /// Some Windows builds leave A=0; clamp those to fully opaque so derived
    /// brushes don't render invisible.
    /// </summary>
    private static JaliumColor DecodeAccentDword(uint dword)
    {
        var r = (byte)((dword >> 0) & 0xFF);
        var g = (byte)((dword >> 8) & 0xFF);
        var b = (byte)((dword >> 16) & 0xFF);
        var a = (byte)((dword >> 24) & 0xFF);

        if (a == 0)
            a = 0xFF;

        return JaliumColor.FromArgb(a, r, g, b);
    }
}
