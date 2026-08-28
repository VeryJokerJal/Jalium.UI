using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// 复现 2026-08-25 真机静默失效：Window 根 + Grid + TextBlock，patch 改 Text/Foreground 后
/// agent 报 updated=5/failed=0 但值（和视觉）未变。1:1 保真用户 app 的标记
/// （legacy jalium xmlns、同属性集、同兄弟结构）。
public class HotReloadWindowRootPatchTests
{
    private const string J = "http://schemas.jalium.ui/2024";
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void WindowRoot_TextAndForegroundEdit_PatchesLiveValues()
    {
        var target = (Window)XamlReader.Parse(
            $"""
            <Window xmlns="{J}" xmlns:x="{X}"
                    Title="MyJaliumApp2.Desktop"
                    Width="800" Height="600">
                <Grid>
                    <TextBlock Text="欢迎使用 MyJaliuktop"
                               HorizontalAlignment="Center"
                               Foreground="Orange"
                               VerticalAlignment="Center"
                               FontSize="20" />
                    <Border>
                        <Grid>
                        </Grid>
                    </Border>
                </Grid>
            </Window>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(Window).FullName!, "MainWindow.jalxaml",
            $"""
            <Window xmlns="{J}" xmlns:x="{X}"
                    Title="MyJaliumApp2.Desktop"
                    Width="800" Height="600">
                <Grid>
                    <TextBlock Text="HotReload-OK-B-Route" Foreground="LimeGreen"
                               HorizontalAlignment="Center"

                               VerticalAlignment="Center"
                               FontSize="20" />
                    <Border>
                        <Grid>
                        </Grid>
                    </Border>
                </Grid>
            </Window>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.True(result.UpdatedElements >= 1, $"expected ≥1 updated, got {result.UpdatedElements}");

        var grid = Assert.IsType<Grid>(target.Content);
        var textBlock = Assert.IsType<TextBlock>(grid.Children[0]);
        Assert.Equal("HotReload-OK-B-Route", textBlock.Text);
        var brush = Assert.IsType<SolidColorBrush>(textBlock.Foreground);
        Assert.Equal(Colors.LimeGreen, brush.Color);
    }
}
