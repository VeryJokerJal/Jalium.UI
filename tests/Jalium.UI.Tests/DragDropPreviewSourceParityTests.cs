namespace Jalium.UI.Tests;

public sealed class DragDropPreviewSourceParityTests
{
    [Fact]
    public void PreviewSourceEventsAreTunnelEventsSharedWithUIElement()
    {
        Assert.Equal(RoutingStrategy.Tunnel, DragDrop.PreviewGiveFeedbackEvent.RoutingStrategy);
        Assert.Equal(RoutingStrategy.Tunnel, DragDrop.PreviewQueryContinueDragEvent.RoutingStrategy);
        Assert.Same(DragDrop.PreviewGiveFeedbackEvent, UIElement.PreviewGiveFeedbackEvent);
        Assert.Same(DragDrop.PreviewQueryContinueDragEvent, UIElement.PreviewQueryContinueDragEvent);
    }

    [Fact]
    public void PreviewRegistrationHelpersAddAndRemoveTypedHandlers()
    {
        var element = new ProbeElement();
        var feedbackCalls = 0;
        var queryCalls = 0;
        GiveFeedbackEventHandler feedback = (_, _) => feedbackCalls++;
        QueryContinueDragEventHandler query = (_, _) => queryCalls++;

        DragDrop.AddPreviewGiveFeedbackHandler(element, feedback);
        DragDrop.AddPreviewQueryContinueDragHandler(element, query);
        element.RaiseEvent(new GiveFeedbackEventArgs(
            DragDrop.PreviewGiveFeedbackEvent,
            DragDropEffects.Copy));
        element.RaiseEvent(new QueryContinueDragEventArgs(
            DragDrop.PreviewQueryContinueDragEvent,
            DragDropKeyStates.LeftMouseButton,
            escapePressed: false));

        Assert.Equal(1, feedbackCalls);
        Assert.Equal(1, queryCalls);

        DragDrop.RemovePreviewGiveFeedbackHandler(element, feedback);
        DragDrop.RemovePreviewQueryContinueDragHandler(element, query);
        element.RaiseEvent(new GiveFeedbackEventArgs(
            DragDrop.PreviewGiveFeedbackEvent,
            DragDropEffects.Copy));
        element.RaiseEvent(new QueryContinueDragEventArgs(
            DragDrop.PreviewQueryContinueDragEvent,
            DragDropKeyStates.LeftMouseButton,
            escapePressed: false));

        Assert.Equal(1, feedbackCalls);
        Assert.Equal(1, queryCalls);
    }

    [Fact]
    public void PreviewRegistrationHelpersValidateInputs()
    {
        GiveFeedbackEventHandler handler = (_, _) => { };

        Assert.Throws<ArgumentNullException>(() =>
            DragDrop.AddPreviewGiveFeedbackHandler(null!, handler));
        Assert.Throws<ArgumentNullException>(() =>
            DragDrop.AddPreviewGiveFeedbackHandler(new ProbeElement(), null!));
        Assert.Throws<ArgumentException>(() =>
            DragDrop.AddPreviewGiveFeedbackHandler(new DependencyObject(), handler));
    }

    private sealed class ProbeElement : UIElement;
}
