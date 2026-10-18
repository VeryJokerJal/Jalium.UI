namespace Jalium.UI.Controls.Platform;

/// <summary>Optional platform appearance hooks installed by the desktop host.</summary>
internal static class PlatformThemeIntegration
{
    internal static Func<IDisposable>? MonitorAppearance { get; set; }
    internal static Func<uint>? ReadColorScheme { get; set; }
    internal static Func<bool>? ReadHighContrast { get; set; }
    internal static Action<Window, object?>? ApplyWindowAppearance { get; set; }
}
