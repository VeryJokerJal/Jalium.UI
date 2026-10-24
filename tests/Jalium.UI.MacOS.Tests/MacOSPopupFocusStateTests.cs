using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupFocusStateTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void PopupFocusFlagsIncludeInputOwnersAndThePhysicalOverlay(int nested)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(nested);
        Assert.True(h.Editor.IsKeyboardFocused); Assert.True(h.Window.IsKeyboardFocusWithin);
        Assert.True(h.Window.OverlayLayer.IsKeyboardFocusWithin);
        Assert.True(h.Dialog.IsKeyboardFocusWithin); Assert.True(h.Anchor.IsKeyboardFocusWithin);
        Assert.True(h.Left.IsKeyboardFocusWithin); Assert.True(h.Inside.IsKeyboardFocusWithin);
        Assert.All(h.Popups, popup => Assert.True(popup.IsKeyboardFocusWithin));
        Assert.Equal("inside", h.Anchor.Tag);
    }

    [Fact]
    public void MovingWithinPopupDoesNotToggleSharedOwnerFocusFlags()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int anchorChanges = 0, dialogChanges = 0;
        h.Anchor.IsKeyboardFocusWithinChanged += (_, _) => anchorChanges++;
        h.Dialog.IsKeyboardFocusWithinChanged += (_, _) => dialogChanges++;
        Assert.True(h.Plain.Focus()); Assert.True(h.Editor.Focus());
        Assert.True(h.Anchor.IsKeyboardFocusWithin); Assert.Equal((0, 0), (anchorChanges, dialogChanges));
    }

    [Fact]
    public void LeavingPopupClearsItsOwnerFlagsAndKeepsTheNewVisualAncestors()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); Assert.True(h.Background.Focus());
        Assert.False(h.Anchor.IsKeyboardFocusWithin); Assert.False(h.Dialog.IsKeyboardFocusWithin);
        Assert.False(h.Left.IsKeyboardFocusWithin); Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
        Assert.All(h.Popups, popup => Assert.False(popup.IsKeyboardFocusWithin));
        Assert.True(h.Window.IsKeyboardFocusWithin); Assert.Equal("outside", h.Anchor.Tag);
    }

    [Fact]
    public void ClosingPopupClearsDetachedFlagsImmediately()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var root = h.Panel.VisualParent as UIElement;
        h.Popups[0].IsOpen = false;
        Assert.Same(h.Other, Keyboard.FocusedElement); Assert.False(h.Editor.IsKeyboardFocusWithin);
        Assert.False(root!.IsKeyboardFocusWithin); Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
        Assert.False(h.Popups[0].IsKeyboardFocusWithin); Assert.True(h.Dialog.IsKeyboardFocusWithin);
    }

    [Fact]
    public void RetargetingKeepsTheEditorAndMovesOwnerFlagsWithoutLeavingStaleFlags()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Popups[0].PlacementTarget = h.Other; h.Pump();
        Assert.Same(h.Editor, Keyboard.FocusedElement); Assert.False(h.Anchor.IsKeyboardFocusWithin);
        Assert.True(h.Other.IsKeyboardFocusWithin); Assert.True(h.Right.IsKeyboardFocusWithin);
        Assert.True(h.Dialog.IsKeyboardFocusWithin); Assert.True(h.Window.OverlayLayer.IsKeyboardFocusWithin);
        Assert.True(h.Background.Focus()); Assert.False(h.Other.IsKeyboardFocusWithin); Assert.False(h.Right.IsKeyboardFocusWithin);
    }

    [Fact]
    public void ReparentingTheAnchorRefreshesOwnersWhileKeepingPopupFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Left.Children.Remove(h.Anchor); h.Right.Children.Add(h.Anchor); h.Pump();
        Assert.True(h.Popups[0].IsOpen); Assert.Same(h.Editor, Keyboard.FocusedElement);
        Assert.False(h.Left.IsKeyboardFocusWithin); Assert.True(h.Right.IsKeyboardFocusWithin);
        Assert.True(h.Anchor.IsKeyboardFocusWithin); Assert.True(h.Dialog.IsKeyboardFocusWithin);
    }

    [Theory]
    [InlineData("hide")] [InlineData("disable")] [InlineData("parent-hide")]
    [InlineData("parent-disable")] [InlineData("detach")] [InlineData("dialog-hide")]
    public void InvalidAnchorClosesTheVisiblePopupAndCancelsItsComposition(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(h.Window,
            [new Jalium.UI.Controls.Platform.PlatformEvent { Type = Jalium.UI.Controls.Platform.PlatformEventType.CompositionStart }]);
        Assert.True(InputMethod.IsComposing);
        if (mutation == "hide") h.Anchor.Visibility = Visibility.Collapsed;
        else if (mutation == "disable") h.Anchor.IsEnabled = false;
        else if (mutation == "parent-hide") h.Left.Visibility = Visibility.Collapsed;
        else if (mutation == "parent-disable") h.Left.IsEnabled = false;
        else if (mutation == "detach") h.Left.Children.Remove(h.Anchor);
        else h.Dialog.Visibility = Visibility.Collapsed;
        h.Pump(); Assert.False(h.Popups[0].IsOpen, "Invalid owner must close its popup");
        Assert.False(InputMethod.IsComposing, "Closing the popup must cancel its composition");
        Assert.Null(typeof(Popup).GetField("_popupRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(h.Popups[0]));
        Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
    }

    [Fact]
    public void RetargetingToAnotherWindowClosesTheOldHost()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); using var foreign = new Host(); Assert.True(h.Editor.Focus());
        h.Popups[0].PlacementTarget = foreign.Anchor; h.Pump();
        Assert.False(h.Popups[0].IsOpen); Assert.NotSame(h.Editor, Keyboard.FocusedElement);
        Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
    }

    [Fact]
    public void ChangesToAnOldAnchorDoNotCloseTheRetargetedPopup()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Popups[0].PlacementTarget = h.Other; h.Pump();
        h.Anchor.Visibility = Visibility.Collapsed; h.Pump();
        Assert.True(h.Popups[0].IsOpen); Assert.Same(h.Editor, Keyboard.FocusedElement); Assert.True(h.Other.IsKeyboardFocusWithin);
    }

    [Fact]
    public void AValidPopupReopenedByClosedIsNotRetiredByTheOldOwnerCheck()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var popup = h.Popups[0]; int closed = 0;
        EventHandler handler = (_, _) => { closed++; popup.PlacementTarget = h.Other; popup.IsOpen = true; Assert.True(h.Editor.Focus()); };
        popup.Closed += handler;
        try
        {
            h.Anchor.Visibility = Visibility.Collapsed; h.Pump(); h.Pump();
            Assert.Equal(1, closed); Assert.True(popup.IsOpen); Assert.Same(h.Editor, Keyboard.FocusedElement);
            Assert.True(h.Other.IsKeyboardFocusWithin); Assert.True(h.Window.OverlayLayer.IsKeyboardFocusWithin);
        }
        finally { popup.Closed -= handler; }
    }

    [Fact]
    public void FocusFlagCallbacksMayChooseAnotherFocusWithoutRestoringOldFlags()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); Assert.True(h.Background.Focus()); int calls = 0;
        h.Anchor.IsKeyboardFocusWithinChanged += (_, _) =>
        { if (h.Anchor.IsKeyboardFocusWithin) { calls++; Assert.True(h.Background.Focus()); } };
        h.Editor.Focus(); h.Pump();
        Assert.Equal(1, calls); Assert.Same(h.Background, Keyboard.FocusedElement);
        Assert.False(h.Anchor.IsKeyboardFocusWithin); Assert.False(h.Popups[0].IsKeyboardFocusWithin);
        Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin); Assert.True(h.Window.IsKeyboardFocusWithin);
    }

    [Fact]
    public void NonInteractivePopupMayRemainOnADisabledVisibleAnchor()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(interactive: false); h.Anchor.IsEnabled = false; h.Pump();
        Assert.True(h.Popups[0].IsOpen); Assert.Same(h.Other, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData("hide")] [InlineData("disable")]
    public void AnInvalidAnchorCannotCreateANewInteractiveHost(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var popup = h.Popups[0]; popup.IsOpen = false;
        if (mutation == "hide") h.Anchor.Visibility = Visibility.Collapsed;
        else h.Anchor.IsEnabled = false;
        popup.IsOpen = true;
        Assert.False(popup.IsOpen); Assert.Null(h.Panel.VisualParent);
        Assert.Same(h.Other, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData("hide")] [InlineData("disable")]
    public void TemporaryOwnerStateChangesKeepTheSameHostFocusAndComposition(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var root = h.Panel.VisualParent;
        typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(h.Window,
            [new Jalium.UI.Controls.Platform.PlatformEvent { Type = Jalium.UI.Controls.Platform.PlatformEventType.CompositionStart }]);
        if (mutation == "hide") { h.Left.Visibility = Visibility.Collapsed; h.Left.Visibility = Visibility.Visible; }
        else { h.Left.IsEnabled = false; h.Left.IsEnabled = true; }
        h.Pump(); Assert.True(h.Popups[0].IsOpen); Assert.Same(root, h.Panel.VisualParent);
        Assert.Same(h.Editor, Keyboard.FocusedElement); Assert.True(InputMethod.IsComposing);
        Assert.True(h.Anchor.IsKeyboardFocusWithin);
    }

    [Fact]
    public void AnUnfocusedPopupAlsoClosesWhenItsAnchorDetaches()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); Assert.True(h.Background.Focus());
        h.Left.Children.Remove(h.Anchor); Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.False(h.Popups[0].IsOpen); Assert.Same(h.Background, Keyboard.FocusedElement);
    }

    [Fact]
    public void RetargetingBeforeAPendingInvalidOwnerCheckKeepsTheNewLease()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var root = h.Panel.VisualParent;
        h.Anchor.IsEnabled = false; h.Popups[0].PlacementTarget = h.Other; h.Pump();
        Assert.True(h.Popups[0].IsOpen); Assert.Same(root, h.Panel.VisualParent);
        Assert.Same(h.Editor, Keyboard.FocusedElement); Assert.True(h.Other.IsKeyboardFocusWithin);
    }

    [Fact]
    public void RetargetingAlsoMovesTheVisibleHostToItsNewAnchor()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var root = Assert.IsType<PopupRoot>(h.Panel.VisualParent);
        // This fixture hosts a dialog without its production template. Give
        // both anchors explicit layout before checking placement geometry.
        h.Anchor.Measure(new Size(150, 25)); h.Anchor.Arrange(new Rect(0, 0, 150, 25));
        h.Other.Measure(new Size(150, 25)); h.Other.Arrange(new Rect(0, 100, 150, 25));
        h.Popups[0].UpdatePosition();
        double oldTop = Canvas.GetTop(root);
        h.Popups[0].PlacementTarget = h.Other; Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.Same(root, h.Panel.VisualParent); Assert.True(Canvas.GetTop(root) > oldTop + 1);
        Assert.Same(h.Editor, Keyboard.FocusedElement);
    }

    [Fact]
    public void InvalidOuterOwnerClosesItsNestedPopupAndClearsBothFocusRoutes()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(nested: 1); h.Left.IsEnabled = false; h.Pump();
        Assert.All(h.Popups, popup => { Assert.False(popup.IsOpen); Assert.False(popup.IsKeyboardFocusWithin); });
        Assert.Same(h.Other, Keyboard.FocusedElement); Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
    }

    [Fact]
    public void AFocusCallbackDuringDeferredRetargetingCanChooseAnotherElement()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int calls = 0;
        h.Other.IsKeyboardFocusWithinChanged += (_, _) =>
        { if (h.Other.IsKeyboardFocusWithin) { calls++; Assert.True(h.Background.Focus()); } };
        h.Popups[0].PlacementTarget = h.Other; h.Pump();
        Assert.Equal(1, calls); Assert.Same(h.Background, Keyboard.FocusedElement);
        Assert.False(h.Other.IsKeyboardFocusWithin); Assert.False(h.Anchor.IsKeyboardFocusWithin);
        Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin); Assert.True(h.Window.IsKeyboardFocusWithin);
    }

    [Fact]
    public void AFocusFlagCallbackMayCloseThePopupWithoutKeepingDetachedFlags()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); Assert.True(h.Background.Focus());
        h.Anchor.IsKeyboardFocusWithinChanged += (_, _) =>
        { if (h.Anchor.IsKeyboardFocusWithin) h.Popups[0].IsOpen = false; };
        h.Editor.Focus(); h.Pump();
        Assert.False(h.Popups[0].IsOpen); Assert.Same(h.Other, Keyboard.FocusedElement);
        Assert.False(h.Editor.IsKeyboardFocusWithin); Assert.False(h.Anchor.IsKeyboardFocusWithin);
        Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OwnerChecksRecoverFromAnAbortedOrInlineDispatcherPost(bool runInline)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var dispatcher = Dispatcher.CurrentDispatcher; bool handled = false;
        DispatcherHookEventHandler hook = (_, e) =>
        {
            if (handled || e.Operation.Priority != DispatcherPriority.Input) return;
            handled = true;
            if (runInline) dispatcher.ProcessQueue(); else Assert.True(e.Operation.Abort());
        };
        dispatcher.Hooks.OperationPosted += hook;
        try { h.Anchor.IsEnabled = false; }
        finally { dispatcher.Hooks.OperationPosted -= hook; }
        // After an aborted post, layout supplies a new notification and the
        // bounded dispatcher executes its check in the following batch.
        Assert.True(handled); h.Pump(); h.Pump();
        Assert.False(h.Popups[0].IsOpen); Assert.Same(h.Other, Keyboard.FocusedElement);
        Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
    }

    [Fact]
    public void RetargetingInsideThePostHookRetiresOnlyTheOldOwnerCheck()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var dispatcher = Dispatcher.CurrentDispatcher; bool handled = false;
        DispatcherHookEventHandler hook = (_, e) =>
        {
            if (handled || e.Operation.Priority != DispatcherPriority.Input) return;
            handled = true; h.Popups[0].PlacementTarget = h.Other; dispatcher.ProcessQueue();
        };
        dispatcher.Hooks.OperationPosted += hook;
        try { h.Anchor.IsEnabled = false; }
        finally { dispatcher.Hooks.OperationPosted -= hook; }
        Assert.True(handled); h.Pump();
        Assert.True(h.Popups[0].IsOpen); Assert.Same(h.Editor, Keyboard.FocusedElement);
        Assert.True(h.Other.IsKeyboardFocusWithin); Assert.False(h.Anchor.IsKeyboardFocusWithin);
    }

    [Fact]
    public void TheWindowRenderLayoutPathAlsoRetriesAnAbortedOwnerCheck()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var dispatcher = Dispatcher.CurrentDispatcher; bool aborted = false;
        DispatcherHookEventHandler hook = (_, e) =>
        {
            if (!aborted && e.Operation.Priority == DispatcherPriority.Input)
            { aborted = true; Assert.True(e.Operation.Abort()); }
        };
        dispatcher.Hooks.OperationPosted += hook;
        try { h.Anchor.IsEnabled = false; }
        finally { dispatcher.Hooks.OperationPosted -= hook; }
        Assert.True(aborted); Assert.True(h.Popups[0].IsOpen);
        typeof(Window).GetMethod("UpdateLayoutForRender", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(h.Window, null);
        dispatcher.ProcessQueue(); Assert.False(h.Popups[0].IsOpen);
        Assert.Same(h.Other, Keyboard.FocusedElement); Assert.False(h.Window.OverlayLayer.IsKeyboardFocusWithin);
    }

    private sealed class DialogWithContent : ContentDialog
    { internal DialogWithContent(UIElement content) { Visibility = Visibility.Visible; AddVisualChild(content); } }

    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly StackPanel Root = new(), Inside = new(), Left = new(), Right = new();
        internal readonly Control Anchor = new() { Focusable = true, Width = 150, Height = 25 }, Other = new() { Focusable = true, Width = 150, Height = 25 };
        internal readonly TextBox Background = new() { Text = "background" };
        internal readonly ContentDialog Dialog;
        internal readonly List<Popup> Popups = [];
        internal TextBox Editor = null!;
        internal Control Plain = null!;
        internal StackPanel Panel = null!;
        private static readonly PropertyInfo Active = typeof(Window).GetProperty("ActiveContentDialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        internal Host(int nested = 0, bool interactive = true)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); if (InputMethod.IsComposing) InputMethod.EndComposition();
            var style = new Style(typeof(Control)); style.Setters.Add(new Setter(FrameworkElement.TagProperty, "outside"));
            var trigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            trigger.Setters.Add(new Setter(FrameworkElement.TagProperty, "inside")); style.Triggers.Add(trigger); Anchor.Style = style;
            Left.Children.Add(Anchor); Right.Children.Add(Other); Inside.Children.Add(Left); Inside.Children.Add(Right);
            Dialog = new DialogWithContent(Inside); Root.Children.Add(Dialog); Root.Children.Add(Background);
            Window = new DisplayedTestWindow { Width = 500, Height = 450, Content = Root, TitleBarStyle = WindowTitleBarStyle.Native };
            Layout(); Active.SetValue(Window, Dialog); Assert.True(Other.Focus()); UIElement anchor = Anchor;
            for (int i = 0; i <= nested; i++)
            {
                Panel = new StackPanel { Width = 200, Height = 90 }; Editor = new TextBox { Text = "editor", Width = 150, Height = 40 };
                Plain = new Control { Focusable = true, Width = 150, Height = 25 }; Panel.Children.Add(Editor); Panel.Children.Add(Plain);
                var popup = new Popup { PlacementTarget = anchor, Child = Panel, StaysOpen = true,
                    IsHitTestVisible = interactive, ShouldConstrainToRootBounds = true, PopupAnimation = PopupAnimation.None };
                Popups.Add(popup); popup.IsOpen = true; Layout(); Assert.IsType<PopupRoot>(Panel.VisualParent); anchor = Plain;
            }
            if (interactive) Assert.True(Editor.Focus());
        }
        private void Layout() { Window.Measure(new Size(500,450)); Window.Arrange(new Rect(0,0,500,450)); }
        internal void Pump() { Window.InvalidateMeasure(); Window.UpdateLayout(); Dispatcher.CurrentDispatcher.ProcessQueue(); }
        public void Dispose()
        {
            if (InputMethod.IsComposing) InputMethod.EndComposition();
            foreach (var popup in Popups.AsEnumerable().Reverse()) popup.IsOpen = false;
            Active.SetValue(Window, null); Keyboard.ClearFocus(); Window.Close(); Dispatcher.CurrentDispatcher.ProcessQueue();
        }
    }
}
