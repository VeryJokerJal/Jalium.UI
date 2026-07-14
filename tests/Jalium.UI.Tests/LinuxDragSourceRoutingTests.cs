using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

public sealed class LinuxDragSourceRoutingTests
{
    [Fact]
    public void GiveFeedback_RaisesPreviewThenBubble()
    {
        var source = new Border();
        var events = new List<string>();
        source.PreviewGiveFeedback += (_, e) =>
        {
            events.Add("preview");
            Assert.Equal(DragDropEffects.Copy, e.Effects);
        };
        source.GiveFeedback += (_, e) =>
        {
            events.Add("bubble");
            Assert.Equal(DragDropEffects.Copy, e.Effects);
        };

        DragDropPlatform.RaiseLinuxGiveFeedback(source, DragDropEffects.Copy);

        Assert.Equal(["preview", "bubble"], events);
    }

    [Fact]
    public void GiveFeedback_HandledPreviewSuppressesBubble()
    {
        var source = new Border();
        int bubbleCount = 0;
        source.PreviewGiveFeedback += (_, e) => e.Handled = true;
        source.GiveFeedback += (_, _) => bubbleCount++;

        DragDropPlatform.RaiseLinuxGiveFeedback(source, DragDropEffects.Move);

        Assert.Equal(0, bubbleCount);
    }

    [Fact]
    public void QueryContinueDrag_UsesDesktopDefaults()
    {
        var source = new Border();

        Assert.Equal(
            PlatformDragContinueAction.Continue,
            DragDropPlatform.RaiseLinuxQueryContinueDrag(
                source, DragDropKeyStates.LeftMouseButton, escapePressed: false));
        Assert.Equal(
            PlatformDragContinueAction.Drop,
            DragDropPlatform.RaiseLinuxQueryContinueDrag(
                source, DragDropKeyStates.None, escapePressed: false));
        Assert.Equal(
            PlatformDragContinueAction.Cancel,
            DragDropPlatform.RaiseLinuxQueryContinueDrag(
                source, DragDropKeyStates.LeftMouseButton, escapePressed: true));
    }

    [Fact]
    public void QueryContinueDrag_HandledRouteOverridesDefault()
    {
        var source = new Border();
        var events = new List<string>();
        source.PreviewQueryContinueDrag += (_, _) => events.Add("preview");
        source.QueryContinueDrag += (_, e) =>
        {
            events.Add("bubble");
            e.Action = DragAction.Cancel;
            e.Handled = true;
        };

        PlatformDragContinueAction action = DragDropPlatform.RaiseLinuxQueryContinueDrag(
            source, DragDropKeyStates.LeftMouseButton, escapePressed: false);

        Assert.Equal(PlatformDragContinueAction.Cancel, action);
        Assert.Equal(["preview", "bubble"], events);
    }
}
