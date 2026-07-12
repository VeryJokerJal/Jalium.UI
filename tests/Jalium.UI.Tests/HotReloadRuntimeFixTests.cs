using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// 热重载引擎修复回归（审计 F1/F2/F5）。每个测试用【独占的根类型】注册，避免与
/// <see cref="HotReloadRuntimeTests"/>（Viewbox/StackPanel/Grid/ScrollViewer）跨测试串扰。
public class HotReloadRuntimeFixTests
{
    private const string Pres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void F2_ContentControlRootWithContent_PatchesInPlace_NoThrow()
    {
        // 根是 ContentControl（≈ UserControl/Window/Page）且有内容：修复前 CopyDependencyProperties
        // 无条件复制 ContentProperty(DP) → OnContentChanged → AddVisualChild 抛 → failed=1 / updated=0；
        // 修复后元素型 DP 留给子级处理器，内容被原地递归 patch，failed=0。
        var target = (ContentControl)XamlReader.Parse(
            $"""
            <ContentControl xmlns="{Pres}" xmlns:x="{X}">
                <Border x:Name="B" Width="10" />
            </ContentControl>
            """);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(ContentControl).FullName!, "f.jalxaml",
            $"""
            <ContentControl xmlns="{Pres}" xmlns:x="{X}">
                <Border x:Name="B" Width="20" />
            </ContentControl>
            """);

        Assert.Equal(0, result.FailedElements);
        Assert.True(result.UpdatedElements >= 1, $"expected ≥1 updated, got {result.UpdatedElements}");
        var border = Assert.IsType<Border>(target.Content);
        Assert.Equal(20, border.Width);
    }

    [Fact]
    public void ContentControl_IncompatibleElementReplacement_ReleasesSourceOwnershipBeforeGraft()
    {
        var target = (UserControl)XamlReader.Parse(
            $"""
            <UserControl xmlns="{Pres}">
                <Border />
            </UserControl>
            """);
        var original = Assert.IsType<Border>(target.Content);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(UserControl).FullName!, "f.jalxaml",
            $"""
            <UserControl xmlns="{Pres}">
                <TextBlock Text="replacement" />
            </UserControl>
            """);

        Assert.Equal(0, result.FailedElements);
        var replacement = Assert.IsType<TextBlock>(target.Content);
        Assert.Equal("replacement", replacement.Text);
        Assert.Same(target, replacement.Parent);
        Assert.Null(original.Parent);
    }

    [Fact]
    public void HeaderedContentControl_IncompatibleHeaderReplacement_ReleasesSourceOwnershipBeforeGraft()
    {
        var target = (GroupBox)XamlReader.Parse(
            $"""
            <GroupBox xmlns="{Pres}">
                <GroupBox.Header>
                    <Border />
                </GroupBox.Header>
            </GroupBox>
            """);
        var original = Assert.IsType<Border>(target.Header);
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(GroupBox).FullName!, "f.jalxaml",
            $"""
            <GroupBox xmlns="{Pres}">
                <GroupBox.Header>
                    <TextBlock Text="replacement header" />
                </GroupBox.Header>
            </GroupBox>
            """);

        Assert.Equal(0, result.FailedElements);
        var replacement = Assert.IsType<TextBlock>(target.Header);
        Assert.Equal("replacement header", replacement.Text);
        Assert.Same(target, replacement.Parent);
        Assert.Null(original.Parent);
    }

    [Fact]
    public void F1_MultiInstance_EachInstanceKeepsOwnChildren_NoSteal()
    {
        // 两个同类实例，patch 做结构改动（加一个子级，走 fallback）。修复前同一棵 parsed 源树被复用，
        // 源子级对象从 inst1 被偷到 inst2（前 N-1 个实例丢内容）；修复后每实例按各自 re-parse 的源树
        // 独立 graft，两个实例的子级对象互不相同。
        static Canvas Make() => (Canvas)XamlReader.Parse(
            $"""<Canvas xmlns="{Pres}"><Border /></Canvas>""");
        var inst1 = Make();
        var inst2 = Make();
        HotReloadRuntime.RegisterComponent(inst1);
        HotReloadRuntime.RegisterComponent(inst2);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(Canvas).FullName!, "f.jalxaml",
            $"""<Canvas xmlns="{Pres}"><Border /><Border /></Canvas>""");

        Assert.Equal(0, result.FailedElements);
        Assert.Equal(2, inst1.Children.Count);
        Assert.Equal(2, inst2.Children.Count);
        // 关键不变式：没有任何子级对象被两个实例共享（即没有发生跨实例偷取）。
        foreach (var c1 in inst1.Children)
        {
            Assert.DoesNotContain(c1, inst2.Children);
        }
    }

    [Fact]
    public void F5_IncompatibleRoot_SurfacesFailedElements()
    {
        // 注册 Border，patch 根是不兼容的 Button → ApplyElementPatch 在 AreTypesCompatible 处早退并
        // counters.FailedElements++。修复前该计数从不并入结果（IDE 误判 success、不重启）；修复后上报。
        var target = new Border();
        HotReloadRuntime.RegisterComponent(target);

        var result = HotReloadRuntime.ApplyPatch(
            typeof(Border).FullName!, "f.jalxaml",
            $"""<Button xmlns="{Pres}" />""");

        Assert.True(result.FailedElements >= 1,
            $"expected failed≥1 (incompatible root surfaced), got updated={result.UpdatedElements} failed={result.FailedElements}");
        Assert.Equal(0, result.UpdatedElements);
    }
}
