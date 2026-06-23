using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// 热重载完整性回归：覆盖本轮新增的递归覆盖（ScrollViewer / ItemsControl）与属性镜像
/// （附加属性 Grid.Row）能力。每个测试用【独占的根类型】注册，避免与其它 HotReload* 测试
/// 跨用例串扰（ApplyPatch 会广播到同 x:Class 的全部存活实例）。
public class HotReloadCompletenessTests
{
    private const string Pres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ScrollViewer_Content_IsPatchedRecursively()
    {
        // 修复前 ScrollViewer:Control 不在 ApplyElementPatch 的递归分支里，Content 子树被截断
        // （实测 Page>ScrollViewer>StackPanel 只 updated=2）。现在应递归进 Content 更新子级。
        var target = (ScrollViewer)XamlReader.Parse(
            $"""
            <ScrollViewer xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="Inner" Text="old" />
            </ScrollViewer>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(ScrollViewer).FullName!, "f.jalxaml",
            $"""
            <ScrollViewer xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="Inner" Text="new" />
            </ScrollViewer>
            """);

        Assert.Equal(0, result.FailedElements);
        var inner = Assert.IsType<TextBlock>(target.Content);
        Assert.Equal("new", inner.Text);
    }

    [Fact]
    public void ItemsControl_InlineItems_ArePatchedByName()
    {
        // ItemsControl 家族的内联子级落在 Items 集合（非 Children/Content），修复前完全不被处理。
        var target = (ItemsControl)XamlReader.Parse(
            $"""
            <ItemsControl xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="Row" Text="old" />
            </ItemsControl>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(ItemsControl).FullName!, "f.jalxaml",
            $"""
            <ItemsControl xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="Row" Text="new" />
            </ItemsControl>
            """);

        Assert.Equal(0, result.FailedElements);
        var item = Assert.IsType<TextBlock>(Assert.Single(target.Items));
        Assert.Equal("new", item.Text);
    }

    [Fact]
    public void ItemsControl_AddingInlineItem_GrowsTheCollection()
    {
        var target = (ItemsControl)XamlReader.Parse(
            $"""
            <ItemsControl xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="A" Text="a" />
            </ItemsControl>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(ItemsControl).FullName!, "f.jalxaml",
            $"""
            <ItemsControl xmlns="{Pres}" xmlns:x="{X}">
                <TextBlock x:Name="A" Text="a" />
                <TextBlock x:Name="B" Text="b" />
            </ItemsControl>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.Equal(2, target.Items.Count);
    }

    [Fact]
    public void AttachedProperty_GridRow_IsMirroredOnReusedChild()
    {
        // 附加属性（Grid.Row）的值存在子元素自己的 _localValues 里、以 Grid.RowProperty 为键；
        // 修复前 CopyDependencyProperties 只反射子元素【自身类型】的 DP 字段，永远看不到它，
        // 因此就地复用的子元素改了 Grid.Row 不生效。现在应从 source 的本地值集合镜像过来。
        var target = (Grid)XamlReader.Parse(
            $"""
            <Grid xmlns="{Pres}" xmlns:x="{X}">
                <Button x:Name="Cell" Grid.Row="0" />
            </Grid>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(Grid).FullName!, "f.jalxaml",
            $"""
            <Grid xmlns="{Pres}" xmlns:x="{X}">
                <Button x:Name="Cell" Grid.Row="2" />
            </Grid>
            """);

        Assert.Equal(0, result.FailedElements);
        var cell = Assert.IsType<Button>(Assert.Single(target.Children));
        Assert.Equal(2, Grid.GetRow(cell));
    }

    [Fact]
    public void DeletedProperty_RevertsToDefault_OnNextPatch()
    {
        // B2: a property one patch sets but the next omits must revert to its default rather than
        // linger. The per-instance baseline tracks only DPs WE set, so code-behind / runtime-set
        // values are never touched. DockPanel is this test's exclusive root (avoids cross-test bleed).
        var target = new DockPanel();
        HotReloadRuntime.RegisterComponent(target);

        HotReloadRuntime.ApplyPatch(
            typeof(DockPanel).FullName!, "f.jalxaml",
            $"""<DockPanel xmlns="{Pres}" Opacity="0.5" />""");
        Assert.Equal(0.5, target.Opacity);

        // Second patch omits Opacity → baseline diff reverts it to the default (1.0).
        HotReloadRuntime.ApplyPatch(
            typeof(DockPanel).FullName!, "f.jalxaml",
            $"""<DockPanel xmlns="{Pres}" />""");
        Assert.Equal(1.0, target.Opacity);
    }
}
