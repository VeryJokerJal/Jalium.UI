using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDragDropTests
{
    [Fact]
    public void WindowRegistersTheMacOSPublicDragSourceEntryPoint()
    {
        using var fixture = new RouteFixture();
        Assert.NotNull(DragDrop.DoDragDropOverride);
        Assert.Equal("DoNativeDragDrop", DragDrop.DoDragDropOverride.Method.Name);
        Assert.NotNull(DragDrop.DoShellDragDropOverride);
    }

    [Fact]
    public void ExternalFormatsAreAdvertisedWithoutReadingAndLoadedOnce()
    {
        var reads = new List<string>();
        var data = new MacOSDragDataObject(["text/plain;charset=utf-8", "text/html", "application/vnd.example"], mime =>
        {
            reads.Add(mime);
            return Encoding.UTF8.GetBytes(mime == "text/html" ? "<b>中文🙂</b>" : "中文🙂");
        });
        Assert.True(data.GetDataPresent(DataFormats.Text));
        Assert.True(data.GetDataPresent(DataFormats.Html, false));
        Assert.Contains("application/vnd.example", data.GetFormats(false));
        Assert.Empty(reads);
        Assert.Equal("中文🙂", data.GetData(DataFormats.UnicodeText));
        Assert.Equal("中文🙂", data.GetData(DataFormats.StringFormat));
        Assert.Single(reads);
        Assert.Equal("<b>中文🙂</b>", data.GetData(DataFormats.Html));
        Assert.Equal(2, reads.Count);
        Assert.IsType<byte[]>(data.GetData("application/vnd.example"));
        Assert.Equal(3, reads.Count);
    }

    [Fact]
    public void DropSnapshotRetainsAllFormatsAfterPasteboardOwnerChanges()
    {
        bool retired = false;
        var data = new MacOSDragDataObject(["text/plain", "text/html", "text/uri-list", "application/x-empty"], mime =>
            retired ? null : mime switch
            {
                "text/plain" => Encoding.UTF8.GetBytes("快照🙂"),
                "text/html" => Encoding.UTF8.GetBytes("<p>快照</p>"),
                "text/uri-list" => Encoding.UTF8.GetBytes("file:///tmp/one%20file.txt\r\nfile:///tmp/two.txt\r\n"),
                _ => [],
            });
        data.Snapshot(); retired = true;
        Assert.Equal("快照🙂", data.GetData(DataFormats.Text));
        Assert.Equal("<p>快照</p>", data.GetData(DataFormats.Html));
        Assert.Equal(["/tmp/one file.txt", "/tmp/two.txt"], Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));
        Assert.Empty(Assert.IsType<byte[]>(data.GetData("application/x-empty")));
    }

    [Fact]
    public void RetiredVisitKeepsReadDataAndCannotReadUncachedFormats()
    {
        int reads = 0;
        var data = new MacOSDragDataObject(["text/plain", "text/html"], _ => { reads++; return Encoding.UTF8.GetBytes("保存"); });
        Assert.Equal("保存", data.GetData(DataFormats.UnicodeText));
        data.Detach();
        Assert.Equal("保存", data.GetData(DataFormats.Text));
        Assert.Null(data.GetData(DataFormats.Html));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void BitmapAndAudioKeepCanonicalTypesAndCopiedBytes()
    {
        var pixels = new byte[] { 0x30, 0x20, 0x10, 0xff };
        var png = ClipboardPlatform.EncodePng(1, 1, 4, pixels);
        var data = new MacOSDragDataObject(["image/png", "audio/wav"], mime => mime == "image/png" ? png : [1, 2, 3]);
        data.Snapshot();
        var bitmap = Assert.IsAssignableFrom<Jalium.UI.Media.Imaging.BitmapSource>(data.GetData(DataFormats.Bitmap));
        var copied = new byte[4]; bitmap.CopyPixels(copied, 4, 0);
        Assert.Equal(pixels, copied);
        Assert.Equal([1, 2, 3], Assert.IsType<MemoryStream>(data.GetData(DataFormats.WaveAudio)).ToArray());
    }

    [Fact]
    public void NativeMouseMovesKeepPressedButtonsAndDragCleanupDoesNotClick()
    {
        using var fixture = new RouteFixture();
        var seen = new List<MouseButtonState>();
        int ups = 0;
        fixture.Target.MouseMove += (_, e) => seen.Add(e.LeftButton);
        fixture.Target.MouseUp += (_, _) => ups++;
        var dispatch = typeof(Window).GetMethod("OnPlatformEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Send(PlatformEventType type, uint buttons) => dispatch.Invoke(fixture.Window,
            [new PlatformEvent { Type = type, MouseX = 50, MouseY = 50, MouseButtons = buttons, HasMouseButtonStates = true }]);
        Send(PlatformEventType.MouseDown, 1);
        Send(PlatformEventType.MouseMove, 1);
        Assert.Equal([MouseButtonState.Pressed], seen);
        fixture.Window.CompletePlatformDragInput();
        Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);
        Assert.Equal(0, ups);
        Send(PlatformEventType.MouseMove, 0);
        Assert.Equal([MouseButtonState.Pressed, MouseButtonState.Released], seen);
    }

    [Fact]
    public void DragCleanupClearsTheOwnedPressWhenLostCaptureThrows()
    {
        using var fixture = new RouteFixture();
        fixture.SendPointer(PlatformEventType.MouseDown, 1);
        Assert.True(fixture.Target.CaptureMouse());
        var failure = new InvalidOperationException("owned capture callback");
        MouseEventHandler handler = (_, _) => throw failure;
        fixture.Target.LostMouseCapture += handler;
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Window.CompletePlatformDragInput));
            Assert.Null(Mouse.Captured);
            Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);
            Assert.False((bool)fixture.Target.GetValue(UIElement.IsPressedProperty)!);
        }
        finally
        {
            fixture.Target.LostMouseCapture -= handler;
            fixture.Target.ReleaseMouseCapture();
            fixture.SendPointer(PlatformEventType.MouseUp, 0);
        }
    }

    [Fact]
    public void DragCleanupPreservesAnotherWindowsCaptureAndPress()
    {
        using var source = new RouteFixture(); using var other = new RouteFixture();
        source.SendPointer(PlatformEventType.MouseDown, 1);
        other.SendPointer(PlatformEventType.MouseDown, 1);
        Assert.True(other.Target.CaptureMouse());
        try
        {
            source.Window.CompletePlatformDragInput();
            Assert.Same(other.Target, Mouse.Captured);
            Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
            Assert.True((bool)other.Target.GetValue(UIElement.IsPressedProperty)!);
            Assert.False((bool)source.Target.GetValue(UIElement.IsPressedProperty)!);
        }
        finally
        {
            other.Target.ReleaseMouseCapture();
            other.SendPointer(PlatformEventType.MouseUp, 0);
            source.SendPointer(PlatformEventType.MouseUp, 0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceCleanupDetachesEveryTargetWhenLeaveCallbacksThrow(bool bothThrow)
    {
        using var first = new RouteFixture(); using var second = new RouteFixture();
        first.Send(PlatformEventType.DragEnter, 1); second.Send(PlatformEventType.DragEnter, 2);
        var states = (System.Collections.IDictionary)typeof(NativeDropTarget)
            .GetField("s_states", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var readers = new List<MacOSDragDataObject>();
        foreach (var fixture in new[] { first, second })
        {
            var reader = new MacOSDragDataObject(["text/plain", "text/html"], _ => Encoding.UTF8.GetBytes("保留"));
            Assert.Equal("保留", reader.GetData(DataFormats.UnicodeText)); readers.Add(reader);
            object state = states[fixture.Window]!;
            state.GetType().GetField("SourceHandle")!.SetValue(state, (nint)77);
            state.GetType().GetField("CurrentData")!.SetValue(state, reader);
        }
        var errors = new[] { new InvalidOperationException("first leave"), new InvalidOperationException("second leave") };
        int leaves = 0;
        DragEventHandler firstHandler = (_, _) => { leaves++; throw errors[0]; };
        DragEventHandler secondHandler = (_, _) => { leaves++; if (bothThrow) throw errors[1]; };
        first.Target.PreviewDragLeave += firstHandler; second.Target.PreviewDragLeave += secondHandler;
        try
        {
            if (bothThrow)
            {
                var result = Assert.Throws<AggregateException>(() => NativeDropTarget.RevokeSource(77));
                Assert.Equal(2, result.InnerExceptions.Count);
                Assert.All(errors, error => Assert.Contains(error, result.InnerExceptions));
            }
            else Assert.Same(errors[0], Assert.Throws<InvalidOperationException>(() => NativeDropTarget.RevokeSource(77)));
            Assert.Equal(2, leaves);
            Assert.False(states.Contains(first.Window)); Assert.False(states.Contains(second.Window));
            Assert.All(readers, reader => { Assert.Null(reader.GetData(DataFormats.Html)); Assert.Equal("保留", reader.GetData(DataFormats.UnicodeText)); });
        }
        finally
        {
            first.Target.PreviewDragLeave -= firstHandler; second.Target.PreviewDragLeave -= secondHandler;
        }
    }

    [Fact]
    public void SourceRepresentationsCoverRichTextCsvXamlAndRegisteredFormats()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "中文🙂");
        data.SetData(DataFormats.Html, "<b>中文</b>");
        data.SetData(DataFormats.Rtf, "{\\rtf1 test}");
        data.SetData(DataFormats.CommaSeparatedValue, "a,b\n1,2");
        data.SetData(DataFormats.Xaml, "<Span />");
        data.SetData("应用自定义", new byte[] { 3, 7 });
        var representations = ClipboardPlatform.BuildCrossPlatformRepresentations(data);
        Assert.Equal("中文🙂", Encoding.UTF8.GetString(representations["text/plain;charset=utf-8"]));
        Assert.Equal("<b>中文</b>", Encoding.UTF8.GetString(representations["text/html"]));
        Assert.Equal("a,b\n1,2", Encoding.UTF8.GetString(representations["text/csv"]));
        Assert.Equal("<Span />", Encoding.UTF8.GetString(representations["application/xaml+xml"]));
        string custom = ClipboardPlatform.GetMimeTypesForFormat("应用自定义")[0];
        var received = new MacOSDragDataObject(representations.Keys, mime => representations[mime]);
        received.Snapshot();
        Assert.Equal([3, 7], Assert.IsType<byte[]>(received.GetData("应用自定义")));
        Assert.Contains(custom, received.GetFormats());
    }

    [Theory]
    [InlineData(0, DragDropEffects.Move)]
    [InlineData(32, DragDropEffects.Copy)]
    [InlineData(96, DragDropEffects.Link)]
    [InlineData(8, DragDropEffects.Move)]
    public void MacOperationSelectionHonorsOptionAndCommand(uint keys, DragDropEffects expected) =>
        Assert.Equal(expected, NativeDropTarget.SelectSingleEffect(DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link,
            DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, keys, macOS: true));

    [Fact]
    public void PreviewEffectFlowsToBubbleAndStaleCallbacksCannotReviveTargets()
    {
        using var fixture = new RouteFixture();
        int enters = 0, drops = 0, leaves = 0;
        fixture.Target.PreviewDragEnter += (_, e) => e.Effects = DragDropEffects.Copy;
        fixture.Target.DragEnter += (_, e) => { enters++; Assert.Equal(DragDropEffects.Copy, e.Effects); };
        fixture.Target.Drop += (_, _) => drops++;
        fixture.Target.DragLeave += (_, _) => leaves++;
        fixture.Send(PlatformEventType.DragEnter, 1);
        fixture.Send(PlatformEventType.DragEnter, 2);
        fixture.Send(PlatformEventType.DragLeave, 1);
        fixture.Send(PlatformEventType.Drop, 1);
        fixture.Send(PlatformEventType.DragOver, 1);
        Assert.Equal(2, enters); Assert.Equal(1, leaves); Assert.Equal(0, drops);
        fixture.Send(PlatformEventType.Drop, 2);
        fixture.Send(PlatformEventType.Drop, 2);
        Assert.Equal(1, drops);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("disable")]
    [InlineData("hide")]
    [InlineData("close")]
    public void PreviewRevocationStopsBubbleAndFutureDrop(string action)
    {
        using var fixture = new RouteFixture();
        int bubbles = 0, drops = 0;
        fixture.Target.PreviewDragEnter += (_, _) =>
        {
            if (action == "revoke") NativeDropTarget.RevokeWindow(fixture.Window);
            else if (action == "disable") fixture.Window.IsEnabled = false;
            else if (action == "hide") fixture.Window.Visibility = Visibility.Hidden;
            else fixture.Window.Close();
        };
        fixture.Target.DragEnter += (_, _) => bubbles++;
        fixture.Target.Drop += (_, _) => drops++;
        fixture.Send(PlatformEventType.DragEnter, 1);
        fixture.Send(PlatformEventType.Drop, 1);
        Assert.Equal(0, bubbles); Assert.Equal(0, drops);
    }

    [Fact]
    public void ReentrantEnterDuringLeaveSurvivesOldVisitCleanup()
    {
        using var fixture = new RouteFixture();
        int leaves = 0, enters = 0, drops = 0;
        fixture.Target.DragEnter += (_, _) => enters++;
        fixture.Target.PreviewDragLeave += (_, _) => { leaves++; fixture.Send(PlatformEventType.DragEnter, 3); };
        fixture.Target.Drop += (_, _) => drops++;
        fixture.Send(PlatformEventType.DragEnter, 1);
        fixture.Send(PlatformEventType.DragEnter, 2);
        fixture.Send(PlatformEventType.Drop, 2);
        fixture.Send(PlatformEventType.Drop, 3);
        Assert.Equal(2, enters); Assert.Equal(1, drops); Assert.Equal(1, leaves);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("outside")]
    public void InvalidTargetCannotReceiveDrop(string state)
    {
        using var fixture = new RouteFixture();
        int drops = 0;
        fixture.Target.Drop += (_, _) => drops++;
        fixture.Send(PlatformEventType.DragEnter, 1);
        if (state == "disabled") fixture.Target.IsEnabled = false;
        if (state == "hidden") fixture.Target.Visibility = Visibility.Hidden;
        fixture.Send(PlatformEventType.Drop, 1, state == "outside" ? 900 : 50);
        Assert.Equal(0, drops);
    }

    private sealed class RouteFixture : IDisposable
    {
        internal Border Target { get; } = new() { AllowDrop = true, Background = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Colors.White) };
        internal DisplayedTestWindow Window { get; }
        internal RouteFixture()
        {
            Window = new DisplayedTestWindow { Width = 240, Height = 180, TitleBarStyle = WindowTitleBarStyle.Native, Content = Target };
            Window.Measure(new Size(240, 180)); Window.Arrange(new Rect(0, 0, 240, 180));
        }
        internal void Send(PlatformEventType type, ulong id, float x = 50) => NativeDropTarget.ProcessEvent(Window,
            new PlatformEvent { Type = type, DragSessionId = id, MouseX = x, MouseY = 50,
                DragAllowedEffects = (uint)DragDropEffects.Copy, DragMimeTypes = ["text/plain"], DragKeyStates = 1 });
        internal void SendPointer(PlatformEventType type, uint buttons) => typeof(Window)
            .GetMethod("OnPlatformEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(Window, [new PlatformEvent { Type = type, MouseX = 50, MouseY = 50, MouseButtons = buttons, HasMouseButtonStates = true }]);
        public void Dispose() { NativeDropTarget.RevokeWindow(Window); Window.Close(); }
    }
}
