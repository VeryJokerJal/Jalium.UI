using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

/// <summary>
/// Invoking a context-menu item must dismiss the menu.
///
/// <para>
/// The regression this pins down: <see cref="MenuItem"/> closes the submenus of its *parent*
/// menu items when clicked, but the top-level items of a <see cref="ContextMenu"/> have no parent
/// MenuItem — and the ContextMenu itself is not on their visual parent chain either, because it
/// re-parents its items into a popup. So nothing owned the dismissal: the user picked a command,
/// the command ran, and the menu stayed on screen until they clicked elsewhere. Reported against
/// the Markdown control's "复制纯文本 / 复制 Markdown / 复制富文本" menu.
/// </para>
///
/// <para>
/// Items are invoked through the keyboard path (Enter on a focused item), which is the same
/// <c>OnClick</c> the mouse path funnels into but needs no hit-testing or real window.
/// </para>
/// </summary>
[Collection("Application")]
public class ContextMenuDismissOnItemClickTests
{
    public ContextMenuDismissOnItemClickTests() => LegacyMenuTemplateTestHelper.Register();

    private static void Invoke(MenuItem item) =>
        item.RaiseEvent(new KeyEventArgs(
            UIElement.KeyDownEvent, Key.Enter, ModifierKeys.None,
            isDown: true, isRepeat: false, timestamp: 0));

    [Fact]
    public void ContextMenu_ShouldClose_WhenTopLevelItemIsInvoked()
    {
        var menu = new ContextMenu();
        var copyPlain = new MenuItem { Header = "复制纯文本" };
        var invoked = 0;
        copyPlain.Click += (_, _) => invoked++;
        menu.Items.Add(copyPlain);
        menu.Items.Add(new MenuItem { Header = "复制 Markdown" });

        menu.IsOpen = true;
        Assert.True(menu.IsOpen);

        Invoke(copyPlain);

        Assert.Equal(1, invoked);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void ContextMenu_ShouldHandItsItemsAnOwnerReference()
    {
        // The dismissal hangs off this reference; items are re-parented into the popup, so the
        // ContextMenu can never be found by walking up from an item.
        var menu = new ContextMenu();
        var item = new MenuItem { Header = "复制富文本" };
        menu.Items.Add(item);

        menu.IsOpen = true;

        Assert.Same(menu, item.OwnerMenu);
    }

    [Fact]
    public void SubmenuEntries_ShouldNotClaimTheHostingMenu()
    {
        // Only top-level items carry the owner reference. A submenu entry has to walk up the
        // parent chain to the top-level item first and let *that* one dismiss the menu; if the
        // nested entry also held the reference it would tear down the outer popup while its own
        // parent submenu was still open, collapsing the two levels in the wrong order.
        //
        // Note the full nested path (invoke a child, watch both levels close) cannot be covered
        // offscreen: MenuItem.CloseParentMenus finds parents by walking the submenu popup's real
        // visual tree, and that popup is never realized without a window. What is verified here is
        // the wiring the nested path depends on.
        var menu = new ContextMenu();
        var parent = new MenuItem { Header = "复制为" };
        var child = new MenuItem { Header = "Markdown" };
        parent.Items.Add(child);
        menu.Items.Add(parent);

        menu.IsOpen = true;

        Assert.Same(menu, parent.OwnerMenu);
        Assert.Null(child.OwnerMenu);
    }

    [Fact]
    public void ContextMenu_ShouldStayOpen_ForItemsThatOptOut()
    {
        // StaysOpenOnClick is how checkable items keep the menu up for multiple toggles;
        // the new dismissal must respect it rather than closing unconditionally.
        var menu = new ContextMenu();
        var toggle = new MenuItem { Header = "自动换行", IsCheckable = true, StaysOpenOnClick = true };
        menu.Items.Add(toggle);

        menu.IsOpen = true;
        Invoke(toggle);

        Assert.True(toggle.IsChecked);
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void DisabledItem_ShouldNeitherInvokeNorDismiss()
    {
        var menu = new ContextMenu();
        var disabled = new MenuItem { Header = "复制纯文本", IsEnabled = false };
        var invoked = 0;
        disabled.Click += (_, _) => invoked++;
        menu.Items.Add(disabled);

        menu.IsOpen = true;
        Invoke(disabled);

        Assert.Equal(0, invoked);
        Assert.True(menu.IsOpen);
    }
}
