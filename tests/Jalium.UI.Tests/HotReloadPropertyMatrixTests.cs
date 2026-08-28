using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// 2026-08-25 属性矩阵回归："只改 Foreground 文字消失"的根因是 CopyClrProperties 把 DP 的
/// CLR wrapper 也按【有效值】镜像——把 parse 树的运行时状态（WindowState=Normal、Left/Top=NaN、
/// 继承的字体值）固化成 live 元素的本地值：每次 patch 都把用户最小化的窗口弹回、位置重置，
/// 属性直写式还原把呈现搞坏。修复后 DP-backed 属性一律由 CopyDependencyProperties 以
/// local-only 语义处理，CLR 反射兜底只碰真正无 DP 的标量。
public class HotReloadPropertyMatrixTests
{
    private const string J = "http://schemas.jalium.ui/2024";
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string WindowXaml(string textBlockAttrs) =>
        $"""
        <Window xmlns="{J}" xmlns:x="{X}" Title="T" Width="800" Height="600">
            <Grid>
                <TextBlock {textBlockAttrs} />
            </Grid>
        </Window>
        """;

    private static (Window Root, TextBlock Tb) BuildLive(string textBlockAttrs)
    {
        var root = (Window)XamlReader.Parse(WindowXaml(textBlockAttrs));
        HotReloadRuntime.RegisterComponent(root);
        var tb = (TextBlock)((Grid)root.Content!).Children[0]!;
        return (root, tb);
    }

    private static HotReloadPatchResult Patch(string textBlockAttrs) =>
        HotReloadRuntime.ApplyPatch(typeof(Window).FullName!, "MainWindow.jalxaml", WindowXaml(textBlockAttrs));

    [Fact]
    public void RuntimeWindowState_LeftTop_SurviveEveryPatch()
    {
        var (root, _) = BuildLive("""Text="a" Foreground="Orange" """);
        // 模拟运行时状态：用户把窗口挪过、最小化过 —— 这些不是 markup 状态，patch 永远不能碰。
        root.WindowState = WindowState.Minimized;
        root.Left = 123;
        root.Top = 45;

        var result = Patch("""Text="a" Foreground="Red" """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal(WindowState.Minimized, root.WindowState);
        Assert.Equal(123, root.Left);
        Assert.Equal(45, root.Top);
    }

    [Fact]
    public void InheritedFontSize_NotHardenedToLocalValue()
    {
        var root = (Window)XamlReader.Parse(
            $"""
            <Window xmlns="{J}" xmlns:x="{X}" Title="T" Width="800" Height="600" FontSize="22">
                <Grid>
                    <TextBlock Text="a" />
                </Grid>
            </Window>
            """);
        HotReloadRuntime.RegisterComponent(root);
        var tb = (TextBlock)((Grid)root.Content!).Children[0]!;
        Assert.Equal(DependencyProperty.UnsetValue, tb.ReadLocalValue(TextBlock.FontSizeProperty));

        var result = HotReloadRuntime.ApplyPatch(typeof(Window).FullName!, "MainWindow.jalxaml",
            $"""
            <Window xmlns="{J}" xmlns:x="{X}" Title="T" Width="800" Height="600" FontSize="22">
                <Grid>
                    <TextBlock Text="b" />
                </Grid>
            </Window>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal("b", tb.Text);
        // patch 前后 TextBlock 都没在 markup 里写 FontSize —— 它必须继续继承，不能被固化成本地 22。
        Assert.Equal(DependencyProperty.UnsetValue, tb.ReadLocalValue(TextBlock.FontSizeProperty));
        Assert.Equal(22.0, tb.FontSize);
    }

    [Fact]
    public void TextBlock_CommonPropertyMatrix_AllPatchInPlace()
    {
        var (_, tb) = BuildLive(
            """Text="a" Foreground="Orange" FontSize="20" FontWeight="Normal" Opacity="1" Margin="0" HorizontalAlignment="Left" TextWrapping="NoWrap" """);

        var result = Patch(
            """Text="b" Foreground="LimeGreen" FontSize="32" FontWeight="Bold" Opacity="0.5" Margin="4,8,12,16" HorizontalAlignment="Center" TextWrapping="Wrap" """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal("b", tb.Text);
        Assert.Equal(Colors.LimeGreen, ((SolidColorBrush)tb.Foreground!).Color);
        Assert.Equal(32.0, tb.FontSize);
        Assert.Equal(FontWeights.Bold, tb.FontWeight);
        Assert.Equal(0.5, tb.Opacity);
        Assert.Equal(new Thickness(4, 8, 12, 16), tb.Margin);
        Assert.Equal(HorizontalAlignment.Center, tb.HorizontalAlignment);
        Assert.Equal(TextWrapping.Wrap, tb.TextWrapping);
    }

    [Fact]
    public void TextBlock_VisibilityToggle_PatchesInPlace()
    {
        var (_, tb) = BuildLive("""Text="a" Visibility="Visible" """);

        Assert.Equal(0, Patch("""Text="a" Visibility="Collapsed" """).FailedElements);
        Assert.Equal(Visibility.Collapsed, tb.Visibility);

        Assert.Equal(0, Patch("""Text="a" Visibility="Visible" """).FailedElements);
        Assert.Equal(Visibility.Visible, tb.Visibility);
    }

    [Fact]
    public void Border_AppearanceMatrix_AllPatchInPlace()
    {
        var root = (Window)XamlReader.Parse(
            $"""
            <Window xmlns="{J}" xmlns:x="{X}" Title="T" Width="800" Height="600">
                <Border Background="Black" BorderBrush="Gray" BorderThickness="1" CornerRadius="0" Padding="0" />
            </Window>
            """);
        HotReloadRuntime.RegisterComponent(root);
        var border = (Border)root.Content!;

        var result = HotReloadRuntime.ApplyPatch(typeof(Window).FullName!, "MainWindow.jalxaml",
            $"""
            <Window xmlns="{J}" xmlns:x="{X}" Title="T" Width="800" Height="600">
                <Border Background="Navy" BorderBrush="Gold" BorderThickness="3" CornerRadius="8" Padding="6" />
            </Window>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal(Colors.Navy, ((SolidColorBrush)border.Background!).Color);
        Assert.Equal(Colors.Gold, ((SolidColorBrush)border.BorderBrush!).Color);
        Assert.Equal(new Thickness(3), border.BorderThickness);
        Assert.Equal(new CornerRadius(8), border.CornerRadius);
        Assert.Equal(new Thickness(6), border.Padding);
    }

    [Fact]
    public void WindowTitle_DeclaredInMarkup_StillPatches()
    {
        var (root, _) = BuildLive("""Text="a" """);

        var result = HotReloadRuntime.ApplyPatch(typeof(Window).FullName!, "MainWindow.jalxaml",
            $"""
            <Window xmlns="{J}" xmlns:x="{X}" Title="Renamed" Width="800" Height="600">
                <Grid>
                    <TextBlock Text="a" />
                </Grid>
            </Window>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal("Renamed", root.Title);
    }
}
