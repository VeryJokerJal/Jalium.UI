using Jalium.UI.DesktopDemo.Themes;

namespace Jalium.UI.DesktopDemo;

internal static class RotationReproWindow
{
    public static Window Build() => new() { Title = "Jalium Rotation Repro", Width = 720, Height = 560, Style = RotationReproTheme.WindowStyle, Content = new RotationReproSurface() };
}
