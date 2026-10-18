using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MouseDoubleClickLifetimeTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void RetiringTheSourcePacketStopsSyntheticHandlers(bool preview, bool classCallback)
    {
        var parent = new StackPanel(); var target = new ProbeControl(); parent.Children.Add(target);
        bool alive = true; int late = 0; MouseButtonEventArgs? retained = null;
        var routed = preview ? Control.PreviewMouseDoubleClickEvent : Control.MouseDoubleClickEvent;
        target.Callback = e => { if (classCallback && e.RoutedEvent == routed) alive = false; };
        target.AddHandler(routed, new MouseButtonEventHandler((_, e) => { retained = e; if (!classCallback) alive = false; }));
        target.AddHandler(routed, new MouseButtonEventHandler((_, _) => late++));
        var source = Packet(preview); InputRoute.Raise(target, source, () => alive);
        Assert.False(alive); Assert.Equal(0, late);
        if (retained != null) Assert.Null(retained.CommandInputLifetime);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AnOrdinaryDoubleClickPreservesHandlingAndModifierSnapshots(bool preview)
    {
        var target = new ProbeControl(); MouseButtonEventArgs? retained = null;
        target.AddHandler(preview ? Control.PreviewMouseDoubleClickEvent : Control.MouseDoubleClickEvent,
            new MouseButtonEventHandler((_, e) => { retained = e; e.Handled = true; }));
        var source = Packet(preview); InputRoute.Raise(target, source, () => true);
        Assert.NotNull(retained); Assert.True(source.Handled); Assert.Equal(2, retained.ClickCount);
        Assert.Equal(ModifierKeys.Control, retained.KeyboardModifiers); Assert.Equal(ModifierKeys.Windows, retained.PhysicalModifiers);
        Assert.Null(retained.CommandInputLifetime);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ThrowingHandlersReleaseTheRetainedSyntheticLifetime(bool preview)
    {
        var target = new ProbeControl(); MouseButtonEventArgs? retained = null;
        target.AddHandler(preview ? Control.PreviewMouseDoubleClickEvent : Control.MouseDoubleClickEvent,
            new MouseButtonEventHandler((_, e) => { retained = e; throw new InvalidOperationException("callback"); }));
        Assert.Throws<InvalidOperationException>(() => InputRoute.Raise(target, Packet(preview), () => true));
        Assert.NotNull(retained); Assert.Null(retained.CommandInputLifetime);
    }

    private static MouseButtonEventArgs Packet(bool preview) => new(preview ? UIElement.PreviewMouseDownEvent : UIElement.MouseDownEvent,
        new Point(10, 10), MouseButton.Left, MouseButtonState.Pressed, 2, MouseButtonState.Pressed,
        MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
        ModifierKeys.Control, 123) { PhysicalModifiers = ModifierKeys.Windows };
    private sealed class ProbeControl : Control
    {
        internal Action<MouseButtonEventArgs>? Callback;
        protected override void OnMouseDoubleClick(MouseButtonEventArgs e) { base.OnMouseDoubleClick(e); Callback?.Invoke(e); }
        protected override void OnPreviewMouseDoubleClick(MouseButtonEventArgs e) { base.OnPreviewMouseDoubleClick(e); Callback?.Invoke(e); }
    }
}
