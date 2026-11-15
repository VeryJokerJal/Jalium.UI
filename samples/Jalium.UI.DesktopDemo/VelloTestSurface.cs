using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;

namespace Jalium.UI.DesktopDemo;

/// <summary>Static engine-comparison matrix; its exact vector/gradient scene is authored in the theme.</summary>
public class VelloTestSurface : Control
{
    public static readonly DependencyProperty SceneWidthProperty = DependencyProperty.Register(nameof(SceneWidth), typeof(double), typeof(VelloTestSurface), new PropertyMetadata(1000d), value => value is double size && double.IsFinite(size) && size > 0);
    public static readonly DependencyProperty SceneHeightProperty = DependencyProperty.Register(nameof(SceneHeight), typeof(double), typeof(VelloTestSurface), new PropertyMetadata(660d), value => value is double size && double.IsFinite(size) && size > 0);
    public double SceneWidth { get => (double)GetValue(SceneWidthProperty)!; set => SetValue(SceneWidthProperty, value); }
    public double SceneHeight { get => (double)GetValue(SceneHeightProperty)!; set => SetValue(SceneHeightProperty, value); }
    public VelloTestSurface() { VelloTestTheme.Register(); DefaultStyleKey = typeof(VelloTestSurface); }
}
