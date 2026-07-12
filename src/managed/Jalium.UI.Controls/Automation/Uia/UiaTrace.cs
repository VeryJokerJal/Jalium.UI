using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Automation.Uia;

internal static partial class UiaTrace
{
    private static readonly object s_gate = new();
    private static readonly bool s_enabled = IsEnabled();
    private static readonly string s_path =
        Environment.GetEnvironmentVariable("JALIUM_UIA_TRACE_FILE")
        ?? Path.Combine(Path.GetTempPath(), "Jalium.UI.uia.log");

    internal static bool Enabled => s_enabled;
    internal static string LogPath => s_path;

    internal static void Log(string message)
    {
        if (!s_enabled)
            return;

        try
        {
            var line = $"{DateTimeOffset.Now:O} [tid {Environment.CurrentManagedThreadId}] {message}";
            Debug.WriteLine(line);
            if (OperatingSystem.IsWindows())
                OutputDebugString(line + Environment.NewLine);

            lock (s_gate)
            {
                File.AppendAllText(s_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never let diagnostics affect a COM callback.
        }
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable("JALIUM_UIA_TRACE");
        return value is "1" or "true" or "TRUE" or "yes" or "YES";
    }

    [LibraryImport("kernel32.dll", EntryPoint = "OutputDebugStringW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void OutputDebugString(string lpOutputString);
}
