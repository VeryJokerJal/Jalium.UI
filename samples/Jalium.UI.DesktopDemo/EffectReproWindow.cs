using Jalium.UI.DesktopDemo.Themes;

namespace Jalium.UI.DesktopDemo;

internal static class EffectReproWindow
{
    public static Window Build() => new() { Title = "Effect Repro (vulkan clip/shift)", Width = 980, Height = 720, Style = EffectReproTheme.WindowStyle, Content = new EffectReproSurface() };
}
