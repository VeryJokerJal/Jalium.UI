using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWindowCommandBarTests
{
    [Fact]
    public void NativeCommandsUseOneThemedHostAndPreserveWindowContent()
    {
        var content = new Border(); var left = new Button { Content = "Search" }; var right = new Button { Content = "Tools" };
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = content, LeftWindowCommands = left, RightWindowCommands = right };
        try
        {
            var bar = Host(window); Assert.Same(left, bar.LeftCommands); Assert.Same(right, bar.RightCommands);
            Assert.Same(content, window.Content); Assert.False(bar.IsTitleVisible);
            window.ExtendContentIntoTitleBar = true;
            Assert.Same(bar, Host(window)); Assert.Equal(new Thickness(80, 0, 0, 0), bar.Padding);
            window.LeftWindowCommands = null; window.Title = "Renamed native window";
            Assert.True(bar.IsTitleVisible); Assert.Equal(window.Title, bar.Title); Assert.Same(content, window.Content);
            window.IsShowTitle = false; Assert.False(bar.IsTitleVisible);
            window.RightWindowCommands = null; Assert.Null(Field(window));
            Assert.Null(bar.LeftCommands); Assert.Null(bar.RightCommands); Assert.Same(content, window.Content);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ManagedOrBorderlessWindowsDoNotCreateTheNativeCommandHost()
    {
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Custom, RightWindowCommands = new Button() };
        try
        {
            Assert.Null(Field(window));
            window.TitleBarStyle = WindowTitleBarStyle.Native; Assert.NotNull(Field(window));
            window.WindowStyle = WindowStyle.None; Assert.Null(Field(window));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ExtendedCaptionKeepsInteractiveCommandsInTheClientArea()
    {
        var button = new Button { Width = 80, Height = 24, Content = "Tools" };
        var window = new DisplayedTestWindow
        {
            Width = 400, Height = 240, TitleBarStyle = WindowTitleBarStyle.Native,
            ExtendContentIntoTitleBar = true, RightWindowCommands = button, Title = "Workspace"
        };
        window.Resources.MergedDictionaries.Add(MenuFlyoutTemplateTestHelper.Themes.Value);
        try
        {
            window.Measure(new Size(400, 240)); window.Arrange(new Rect(0, 0, 400, 240)); window.UpdateLayout();
            var position = button.TransformToAncestor(window);
            var hit = typeof(Window).GetMethod("IsExtendedMacOSTitleBarCaption", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.True((bool)hit.Invoke(window, [new Point(100, 12)])!);
            Assert.False((bool)hit.Invoke(window, [new Point(position.X + 5, position.Y + 5)])!);
            Assert.False((bool)hit.Invoke(window, [new Point(40, 12)])!); // Native traffic lights.
        }
        finally { window.Close(); }
    }

    private static object? Field(Window window) => typeof(Window).GetField("_macOSWindowCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static WindowCommandBar Host(Window window) => Assert.IsType<WindowCommandBar>(Field(window));
}
