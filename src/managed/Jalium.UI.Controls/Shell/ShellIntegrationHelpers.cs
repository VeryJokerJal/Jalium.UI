using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shell;

/// <summary>
/// Provides a way to register or unregister an application for file associations.
/// </summary>
public static class FileRegistrationHelper
{
    public static void SetFileAssociation(string extension, string progId, string description, string? iconPath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        ArgumentException.ThrowIfNullOrEmpty(progId);
    }

    public static void RemoveFileAssociation(string extension, string progId)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        ArgumentException.ThrowIfNullOrEmpty(progId);
    }

    public static void NotifyShellOfChange()
    {
    }
}

/// <summary>
/// Provides system-related utilities for shell integration.
/// </summary>
public static class SystemParameters2
{
    public static double HorizontalScrollBarHeight => 17;
    public static double VerticalScrollBarWidth => 17;
    public static double WindowCaptionHeight => 30;
    public static Thickness WindowResizeBorderThickness => new(4);
    public static Thickness WindowNonClientFrameThickness => new(3, 3, 3, 3);
    public static bool IsGlassEnabled => true;
    public static Color WindowGlassColor => Color.FromArgb(255, 100, 149, 237);
    public static SolidColorBrush WindowGlassBrush => new(WindowGlassColor);
    public static Size WindowCaptionButtonSize => new(46, 30);
    public static Size SmallIconSize => new(16, 16);

    public static event EventHandler? IsGlassEnabledChanged;
    public static event EventHandler? WindowGlassColorChanged;

    internal static void NotifyGlassEnabledChanged() =>
        IsGlassEnabledChanged?.Invoke(null, EventArgs.Empty);

    internal static void NotifyWindowGlassColorChanged() =>
        WindowGlassColorChanged?.Invoke(null, EventArgs.Empty);
}
