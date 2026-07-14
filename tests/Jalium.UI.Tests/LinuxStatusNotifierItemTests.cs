using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

public class LinuxStatusNotifierItemTests
{
    [Fact]
    public void Introspection_ShouldExposeBothDeployedStatusNotifierInterfaces()
    {
        string xml = LinuxStatusNotifierItem.IntrospectionXml;

        Assert.Contains("org.freedesktop.StatusNotifierItem", xml, StringComparison.Ordinal);
        Assert.Contains("org.kde.StatusNotifierItem", xml, StringComparison.Ordinal);
        Assert.Contains("name=\"IconPixmap\" type=\"a(iiay)\"", xml, StringComparison.Ordinal);
        Assert.Contains("name=\"ToolTip\" type=\"(sa(iiay)ss)\"", xml, StringComparison.Ordinal);
        Assert.Contains("method name=\"ContextMenu\"", xml, StringComparison.Ordinal);
        Assert.Contains("method name=\"Activate\"", xml, StringComparison.Ordinal);
        Assert.Contains("method name=\"SecondaryActivate\"", xml, StringComparison.Ordinal);
        Assert.Contains("method name=\"Scroll\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusNotifierItem_ShouldKeepPrimaryActivationWhenContextMenuExists()
    {
        // ItemIsMenu means "menu-only", not merely "has a menu". NotifyIcon
        // exposes Click/DoubleClick activation independently of ContextMenu.
        Assert.False(LinuxStatusNotifierItem.ItemIsMenu);
        Assert.Equal("/NO_DBUSMENU", LinuxStatusNotifierItem.MenuObjectPath);
    }

    [Fact]
    public void ConvertBgraToNetworkArgb_ShouldUseProtocolChannelOrder()
    {
        byte[] result = LinuxStatusNotifierItem.ConvertBgraToNetworkArgb(
            new byte[] { 0x11, 0x22, 0x33, 0x44, 0xAA, 0xBB, 0xCC, 0xDD });

        Assert.Equal(
            new byte[] { 0x44, 0x33, 0x22, 0x11, 0xDD, 0xCC, 0xBB, 0xAA },
            result);
    }

    [Fact]
    public void ConvertBgraToNetworkArgb_ShouldRejectPartialPixels()
    {
        Assert.Throws<ArgumentException>(() =>
            LinuxStatusNotifierItem.ConvertBgraToNetworkArgb(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void NotifyIcon_PlatformScroll_ShouldRaiseInheritedMouseWheelEvent()
    {
        using var icon = new NotifyIcon();
        int events = 0;
        int delta = 0;
        icon.MouseWheel += (_, e) =>
        {
            events++;
            delta = e.Delta;
        };

        icon.RaiseScrollFromPlatform(120);

        Assert.Equal(1, events);
        Assert.Equal(120, delta);
    }
}
