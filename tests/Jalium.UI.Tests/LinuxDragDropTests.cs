using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

public sealed class LinuxDragDropTests
{
    [Fact]
    public void NativeEvents_RoutePreviewBubbleAndDropToAllowDropElement_OnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var target = new Border
        {
            Width = 180,
            Height = 100,
            AllowDrop = true,
        };
        var window = new Window
        {
            Width = 200,
            Height = 160,
            Content = target,
        };
        window.Measure(new Size(200, 160));
        window.Arrange(new Rect(0, 0, 200, 160));

        var routed = new List<string>();
        string[]? droppedFiles = null;
        target.PreviewDragEnter += (_, e) =>
        {
            routed.Add("preview-enter");
            e.Effects = DragDropEffects.Copy;
        };
        target.DragEnter += (_, e) =>
        {
            routed.Add("enter");
            e.Effects = DragDropEffects.Copy;
        };
        target.DragOver += (_, e) => e.Effects = DragDropEffects.Copy;
        target.Drop += (_, e) =>
        {
            routed.Add("drop");
            droppedFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Effects = DragDropEffects.Copy;
        };

        try
        {
            LinuxDropTarget.ProcessEvent(window, new PlatformEvent
            {
                Type = PlatformEventType.DragEnter,
                DragSessionId = 42,
                DragAllowedEffects = (uint)DragDropEffects.Copy,
                DragMimeTypes = ["text/uri-list"],
                MouseX = 100,
                MouseY = 100,
            });
            LinuxDropTarget.ProcessEvent(window, new PlatformEvent
            {
                Type = PlatformEventType.DragOver,
                DragSessionId = 42,
                DragAllowedEffects = (uint)DragDropEffects.Copy,
                DragMimeTypes = ["text/uri-list"],
                MouseX = 100,
                MouseY = 100,
            });
            LinuxDropTarget.ProcessEvent(window, new PlatformEvent
            {
                Type = PlatformEventType.Drop,
                DragSessionId = 42,
                DragAllowedEffects = (uint)DragDropEffects.Copy,
                DragMimeTypes = ["text/uri-list"],
                DragDataMimeType = "text/uri-list",
                DragData = Encoding.UTF8.GetBytes("file:///tmp/routed-drop.txt\r\n"),
                MouseX = 100,
                MouseY = 100,
            });
        }
        finally
        {
            LinuxDropTarget.RevokeWindow(window);
        }

        Assert.Equal(["preview-enter", "enter", "drop"], routed);
        Assert.Equal(["/tmp/routed-drop.txt"], droppedFiles!);
    }

    [Fact]
    public void UriList_ParsesCommentsEscapesAndMultipleFiles()
    {
        string uriList = "# created by a file manager\r\n" +
                         "file:///tmp/hello%20world.txt\r\n" +
                         "file:///home/user/%E4%B8%AD%E6%96%87.md\n";

        string[] files = LinuxDropTarget.ParseUriList(uriList);

        Assert.Equal(2, files.Length);
        Assert.Equal("/tmp/hello world.txt", files[0]);
        Assert.Equal("/home/user/中文.md", files[1]);
    }

    [Fact]
    public void UriDrop_BecomesFileDropDataObject()
    {
        byte[] payload = Encoding.UTF8.GetBytes(
            "file:///tmp/one.txt\r\nfile:///tmp/two%20words.txt\r\n");

        DataObject data = LinuxDropTarget.CreateDataObject(
            ["text/uri-list", "text/plain;charset=utf-8"],
            "text/uri-list",
            payload);

        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
        Assert.Equal(
            ["/tmp/one.txt", "/tmp/two words.txt"],
            Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));
        Assert.Equal(payload, Assert.IsType<byte[]>(data.GetData("text/uri-list")));
    }

    [Theory]
    [InlineData(8u, 7, 1)] // Control prefers copy
    [InlineData(4u, 7, 2)] // Shift prefers move
    [InlineData(32u, 7, 4)] // Alt prefers link
    [InlineData(0u, 6, 2)] // otherwise move before link
    public void EffectSelection_RespectsModifiersAndAllowedMask(
        uint keyStates,
        int allowed,
        int expected)
    {
        DragDropEffects effect = LinuxDropTarget.SelectSingleEffect(
            DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link,
            (DragDropEffects)allowed,
            keyStates);

        Assert.Equal((DragDropEffects)expected, effect);
    }
}
