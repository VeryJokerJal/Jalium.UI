using Microsoft.Win32;

namespace Jalium.UI;

public static partial class SystemParameters
{
    // 0 = not read yet, 1 = dark, 2 = light (including no expressed preference).
    private static int s_preferredColorScheme;

    internal static bool PrefersDarkColorScheme
    {
        get
        {
            var scheme = Volatile.Read(ref s_preferredColorScheme);
            if (scheme == 0 && OperatingSystem.IsWindows())
            {
                var detected = ReadWindowsSystemColorSchemeRaw();
                Interlocked.CompareExchange(ref s_preferredColorScheme,
                    detected == 1 ? 1 : 2, 0);
                scheme = Volatile.Read(ref s_preferredColorScheme);
            }

            return scheme == 1;
        }
    }

    internal static int ReadWindowsSystemColorSchemeRaw()
    {
        if (!OperatingSystem.IsWindows()) return 0;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme
                ? appsUseLightTheme == 0 ? 1 : 2
                : 0;
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            return 0;
        }
    }

    internal static void RecordPreferredColorScheme(uint scheme)
    {
        // The desktop portal reports 0 when no preference was expressed.
        var normalized = scheme == 1 ? 1 : 2;
        if (Interlocked.Exchange(ref s_preferredColorScheme, normalized) != normalized)
            NotifyStaticPropertyChanged(nameof(PrefersDarkColorScheme));
    }

    private static void RefreshPreferredColorSchemeFromPlatform()
    {
        if (!OperatingSystem.IsWindows()) return;
        var detected = ReadWindowsSystemColorSchemeRaw();
        Volatile.Write(ref s_preferredColorScheme, detected == 1 ? 1 : 2);
    }
}
