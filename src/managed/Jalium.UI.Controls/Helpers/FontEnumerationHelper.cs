using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Helpers;

/// <summary>
/// Enumerates installed font families using Win32 GDI — no System.Drawing dependency.
/// </summary>
internal static partial class FontEnumerationHelper
{
    internal static string[]? EnumerateSystemFontFamilies()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return EnumerateSystemFontFamiliesWindows();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string[]? EnumerateSystemFontFamiliesWindows()
    {
        var fontNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hdc = GetDC(0);
        if (hdc == 0)
        {
            return null;
        }

        try
        {
            var logFont = new LOGFONTW { lfCharSet = DEFAULT_CHARSET };

            var callback = new EnumFontFamExProcW((ref ENUMLOGFONTEXW lpelfe, nint lpntme, uint fontType, nint lParam) =>
            {
                var name = GetFaceName(ref lpelfe.elfLogFont);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fontNames.Add(name);
                }

                return 1; // continue enumeration
            });

            EnumFontFamiliesExW(hdc, ref logFont, callback, 0, 0);

            // Keep the delegate alive during native enumeration.
            GC.KeepAlive(callback);
        }
        finally
        {
            ReleaseDC(0, hdc);
        }

        if (fontNames.Count == 0)
        {
            return null;
        }

        var result = fontNames.ToArray();
        Array.Sort(result, StringComparer.CurrentCultureIgnoreCase);
        return result;
    }

    #region Win32 P/Invoke

    private const byte DEFAULT_CHARSET = 1;
    private const int LF_FACESIZE = 32;

    private delegate int EnumFontFamExProcW(ref ENUMLOGFONTEXW lpelfe, nint lpntme, uint fontType, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll", EntryPoint = "EnumFontFamiliesExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int EnumFontFamiliesExW(nint hdc, ref LOGFONTW lpLogfont, EnumFontFamExProcW lpProc, nint lParam, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct LOGFONTW
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        public fixed char lfFaceName[LF_FACESIZE];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ENUMLOGFONTEXW
    {
        public LOGFONTW elfLogFont;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string elfFullName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfStyle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfScript;
    }

    private static unsafe string GetFaceName(ref LOGFONTW logFont)
    {
        fixed (char* p = logFont.lfFaceName)
        {
            return new string(p);
        }
    }

    #endregion
}
