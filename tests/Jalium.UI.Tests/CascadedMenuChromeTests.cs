using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// 级联菜单的每一级都是独立弹出框，各建各的外框；这组测试钉住"整条链同一套造型"。
///
/// <para>
/// 回归背景：<see cref="MenuItem"/> 的子菜单外框以前写死 <c>CornerRadius=4 / Padding=2</c>，
/// 而 <see cref="ContextMenu"/> 的主题 Style 给的是 <c>14 / 5</c>——同一次交互里一级是大圆角、
/// 二级几乎是直角。子菜单看不到宿主的 Style，只能反过来向宿主取造型。
/// </para>
///
/// <para>
/// 离屏没有窗口，弹窗不会实体化，可视父链接不起来；造型解析因此不走可视树，而是像 OwnerMenu
/// 那样在搬运子项时直接登记，这几条用例正好覆盖那条路径。
/// </para>
/// </summary>
[Collection("Application")]
public class CascadedMenuChromeTests
{
    // 刻意用非主题值：命中说明确实跟着宿主走，而不是碰巧两边都取了主题默认。
    private static readonly CornerRadius HostCornerRadius = new(11);
    private static readonly Thickness HostPadding = new(7);

    private static ContextMenu CreateHostMenu() => new()
    {
        CornerRadius = HostCornerRadius,
        Padding = HostPadding,
        BorderThickness = new Thickness(1),
    };

    [Fact]
    public void Submenu_ShouldMatchContextMenuFrame()
    {
        var menu = CreateHostMenu();
        var category = new MenuItem { Header = "常用控件" };
        category.Items.Add(new MenuItem { Header = "Button" });
        menu.Items.Add(category);

        menu.IsOpen = true;
        category.IsSubmenuOpen = true;

        var frame = category.SubmenuFrame;
        Assert.NotNull(frame);
        Assert.Equal(HostCornerRadius, frame!.CornerRadius);
        Assert.Equal(HostPadding, frame.Padding);
    }

    [Fact]
    public void ThirdLevelSubmenu_ShouldMatchContextMenuFrame()
    {
        var menu = CreateHostMenu();
        var category = new MenuItem { Header = "布局容器" };
        var nested = new MenuItem { Header = "网格定义" };
        nested.Items.Add(new MenuItem { Header = "RowDefinition" });
        category.Items.Add(nested);
        menu.Items.Add(category);

        menu.IsOpen = true;
        category.IsSubmenuOpen = true;
        nested.IsSubmenuOpen = true;

        var frame = nested.SubmenuFrame;
        Assert.NotNull(frame);
        Assert.Equal(HostCornerRadius, frame!.CornerRadius);
        Assert.Equal(HostPadding, frame.Padding);
    }

    [Fact]
    public void Submenu_ShouldFollowLaterHostChanges()
    {
        var menu = CreateHostMenu();
        var category = new MenuItem { Header = "列表与数据" };
        category.Items.Add(new MenuItem { Header = "ListBox" });
        menu.Items.Add(category);

        menu.IsOpen = true;
        category.IsSubmenuOpen = true;
        category.IsSubmenuOpen = false;

        // 主题切换 / 宿主改样式后再展开：造型每次展开都重取，不能停在第一次建 popup 时的值。
        menu.CornerRadius = new CornerRadius(3);
        category.IsSubmenuOpen = true;

        Assert.Equal(new CornerRadius(3), category.SubmenuFrame!.CornerRadius);
    }

    [Fact]
    public void SubmenuWithoutDismissableHost_ShouldUseFrameworkDefault()
    {
        // 常驻菜单栏没有自己的弹出框，子菜单退回框架默认（与 MenuFlyout 的 presenter 对齐）。
        var item = new MenuItem { Header = "文件" };
        item.Items.Add(new MenuItem { Header = "新建" });

        item.IsSubmenuOpen = true;

        var frame = item.SubmenuFrame;
        Assert.NotNull(frame);
        Assert.Equal(new CornerRadius(8), frame!.CornerRadius);
        Assert.Equal(new Thickness(4), frame.Padding);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
    }

    [Fact]
    public void NestedMenuFlyoutSubMenus_ShouldShareOneFrame()
    {
        var outer = new MenuFlyoutSubItem { Text = "布局容器" };
        var inner = new MenuFlyoutSubItem { Text = "网格定义" };
        inner.Items.Add(new MenuFlyoutItem { Text = "RowDefinition" });
        outer.Items.Add(inner);

        outer.ShowSubMenu();
        inner.ShowSubMenu();

        var outerFrame = outer.SubMenuFrame;
        var innerFrame = inner.SubMenuFrame;
        Assert.NotNull(outerFrame);
        Assert.NotNull(innerFrame);
        Assert.Equal(outerFrame!.CornerRadius, innerFrame!.CornerRadius);
        Assert.Equal(outerFrame.Padding, innerFrame.Padding);
        Assert.Equal(outerFrame.BorderThickness, innerFrame.BorderThickness);
    }
}
