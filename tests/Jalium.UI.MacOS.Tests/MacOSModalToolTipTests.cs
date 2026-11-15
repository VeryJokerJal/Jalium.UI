using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSModalToolTipTests
{
    private static ToolTip? CurrentToolTip =>
        (ToolTip?)typeof(ToolTipService).GetField("_currentToolTip", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);

    private static (Window Window, Grid Host, Button Owner) CreateWindow()
    {
        var host = new Grid();
        var owner = new Button { Content = "设置", Width = 100, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(owner);
        var window = new Window { Width = 800, Height = 600, Content = host };
        window.Measure(new Size(800, 600)); window.Arrange(new Rect(0, 0, 800, 600));
        return (window, host, owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningAModalRemovesTheOldWindowsVisibleOrPendingTooltip(bool visible)
    {
        ToolTipService.Cleanup();
        var (_, host, owner) = CreateWindow();
        var tip = new ToolTip { Content = "旧设置提示", InitialShowDelay = 60000 };
        var dialog = new ContentDialog { Title = "设置", CloseButtonText = "取消" };
        try
        {
            ToolTipService.ShowToolTip(owner, tip, new Point(10, 10));
            Assert.Same(tip, CurrentToolTip);
            if (visible) tip.IsOpen = true;
            Assert.Equal(visible, tip.IsOpen);
            host.Children.Add(dialog);
            var pending = dialog.ShowAsync(ContentDialogPlacement.InPlace);
            Assert.False(pending.IsCompleted);
            Assert.Null(CurrentToolTip);
            Assert.False(tip.IsOpen);
            Assert.Null(typeof(ToolTip).GetField("_showTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tip));
            dialog.Hide();
        }
        finally { dialog.OnHostWindowClosed(); ToolTipService.Cleanup(); }
    }

    [Fact]
    public void OpeningAModalDoesNotCancelAnotherWindowsTooltip()
    {
        ToolTipService.Cleanup();
        var (_, host, _) = CreateWindow();
        var (_, _, otherOwner) = CreateWindow();
        var tip = new ToolTip { Content = "另一个窗口", InitialShowDelay = 60000 };
        var dialog = new ContentDialog { Title = "设置", CloseButtonText = "取消" };
        try
        {
            ToolTipService.ShowToolTip(otherOwner, tip, new Point(10, 10));
            host.Children.Add(dialog);
            var pending = dialog.ShowAsync(ContentDialogPlacement.InPlace);
            Assert.Same(tip, CurrentToolTip);
            dialog.Hide();
        }
        finally { dialog.OnHostWindowClosed(); ToolTipService.Cleanup(); }
    }
}
