using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// HotReloadRuntime（热重载 patch 引擎）首套测试：解析失败 / 无实例 / DP 镜像 / 按 x:Name 子级匹配。
/// 注意 ComponentsByClass 是按类型 FullName 的进程级注册表——每个测试用【不同根类型】注册，
/// 避免跨测试把彼此的实例也 patch 进去（弱引用存活期内 ApplyPatch 会广播到同类全部实例）。
public class HotReloadRuntimeTests
{
    private const string Pres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ApplyPatch_ParseError_ReportsFailed()
    {
        var result = HotReloadRuntime.ApplyPatch("Some.Class", "f.jalxaml", "<NotClosed");
        Assert.Equal(0, result.UpdatedElements);
        Assert.Equal(1, result.FailedElements);
        Assert.Contains("Failed to parse", result.Message);
    }

    [Fact]
    public void ApplyPatch_NoActiveInstances_NoOpWithMessage()
    {
        var result = HotReloadRuntime.ApplyPatch(
            "Totally.Unknown.Class", "f.jalxaml",
            $"""<Border xmlns="{Pres}" />""");
        Assert.Equal(0, result.UpdatedElements);
        Assert.Equal(0, result.FailedElements);
        Assert.Contains("No active instances", result.Message);
    }

    [Fact]
    public void ApplyPatch_MirrorsDependencyPropertyOnRoot()
    {
        // 注册键 = 实例类型 FullName；用 Viewbox 当本测试的专属根类型。
        var target = new Viewbox { Width = 10 };
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(Viewbox).FullName!, "f.jalxaml",
            $"""<Viewbox xmlns="{Pres}" Width="123" />""");

        Assert.Equal(0, result.FailedElements);
        Assert.True(result.UpdatedElements >= 1, $"expected ≥1 updated, got {result.UpdatedElements}");
        Assert.Equal(123, target.Width);
    }

    [Fact]
    public void ApplyPatch_MatchesChildByName_UpdatesItsProperties()
    {
        // 目标树经 XamlReader 构建（含命名子元素），patch 改命名 TextBlock 的 Text。
        var target = (StackPanel)XamlReader.Parse(
            $"""
            <StackPanel xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="Title" Text="old" />
            </StackPanel>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(StackPanel).FullName!, "f.jalxaml",
            $"""
            <StackPanel xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="Title" Text="new" />
            </StackPanel>
            """);

        Assert.Equal(0, result.FailedElements);
        var text = Assert.IsType<TextBlock>(Assert.Single(target.Children));
        Assert.Equal("new", text.Text);
    }

    [Fact]
    public void ApplyPatch_StructureChange_FallsBackToReplacement()
    {
        // 子级数量/类型变化走 fallback 整段替换路径：替换后子级应反映新结构。
        var target = (Grid)XamlReader.Parse(
            $"""
            <Grid xmlns="{Pres}">
                <TextBlock Text="a" />
            </Grid>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(Grid).FullName!, "f.jalxaml",
            $"""
            <Grid xmlns="{Pres}">
                <Border />
                <Border />
            </Grid>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.True(result.UpdatedElements + result.FallbackReplacements >= 1);
        Assert.Equal(2, target.Children.Count);
        Assert.All(target.Children.Cast<UIElement>(), c => Assert.IsType<Border>(c));
    }

    [Fact]
    public void ApplyPatch_ViewboxPatchesOverrideChildBeforeDecoratorFallback()
    {
        var target = new HotReloadViewbox { Child = new Border() };
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(HotReloadViewbox).FullName!,
            "f.jalxaml",
            $"""
            <Viewbox xmlns="{Pres}">
                <TextBlock Text="updated" />
            </Viewbox>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal("updated", Assert.IsType<TextBlock>(target.Child).Text);
        Assert.Same(target, ((FrameworkElement)target.Child).Parent);
    }

    [Fact]
    public void ApplyPatch_BulletDecoratorPatchesBulletAndChildSlots()
    {
        var target = new HotReloadBulletDecorator
        {
            Bullet = new Border { Width = 2 },
            Child = new TextBlock { Text = "old" },
        };
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(HotReloadBulletDecorator).FullName!,
            "f.jalxaml",
            $"""
            <BulletDecorator xmlns="{Pres}">
                <BulletDecorator.Bullet>
                    <Border Width="9" />
                </BulletDecorator.Bullet>
                <TextBlock Text="new" />
            </BulletDecorator>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal(9, Assert.IsType<Border>(target.Bullet).Width);
        Assert.Equal("new", Assert.IsType<TextBlock>(target.Child).Text);
    }

    private sealed class HotReloadViewbox : Viewbox
    {
    }

    private sealed class HotReloadBulletDecorator : BulletDecorator
    {
    }
}
