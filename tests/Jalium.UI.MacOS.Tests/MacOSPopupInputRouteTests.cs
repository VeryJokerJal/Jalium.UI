using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupInputRouteTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void OwnedPopupReceivesKeysTextAndKeyRelease(int nested)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(nested: nested); int up = 0;
        h.Editor.KeyUp += (_, _) => up++;
        h.Editor.Select(0, 0); h.Key(Key.End); h.Key(Key.End, up: true); h.Text("x");
        Assert.Equal("abx", h.Editor.Text); Assert.Equal(1, up); Assert.Equal("background", h.Background.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PopupInputReachesItsLogicalAnchorWithOriginalSources(bool declaredWithoutTarget)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(declaredWithoutTarget: declaredWithoutTarget); int key = 0, text = 0;
        UIElement owner = declaredWithoutTarget ? h.Popups[0] : h.Anchor;
        owner.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, e) =>
        { Assert.Same(h.Editor, e.Source); Assert.Same(h.Editor, e.OriginalSource); key++; }), true);
        owner.AddHandler(UIElement.TextInputEvent, new TextCompositionEventHandler((_, e) =>
        { Assert.Same(h.Editor, e.Source); Assert.Same(h.Editor, e.OriginalSource); text++; }), true);
        h.Key(Key.K, up: true); h.Text("x"); Assert.Equal((1, 1), (key, text));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void PopupCompositionCommitsAndCancelsOnTheFocusedEditor(int nested)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(nested: nested); h.Editor.Select(2, 0);
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart });
        Assert.True(InputMethod.IsComposing); Assert.Same(h.Editor, InputMethod.CurrentTarget);
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionUpdate, CompositionText = "zhong", CompositionCursor = 2 });
        Assert.Equal("ab", h.Editor.Text);
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionEnd, CompositionText = "中" });
        Assert.Equal("ab中", h.Editor.Text); Assert.False(InputMethod.IsComposing);
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart });
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionUpdate, CompositionText = "wen", CompositionCursor = 3 });
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionEnd, CompositionText = "" });
        Assert.Equal("ab中", h.Editor.Text); Assert.False(InputMethod.IsComposing);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeSelectionAndReplacementRequestsReachTheOwnedPopup(bool replace)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); var request = new PlatformImeTextRequest(1, 1, replace ? "中" : null, replace);
        h.Send(new PlatformEvent { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
        Assert.True(request.Applied);
        if (replace) Assert.Equal("a中", h.Editor.Text);
        else Assert.Equal((1, 1), (h.Editor.SelectionStart, h.Editor.SelectionLength));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NearestPopupBindingKeepsItsFocusedCommandTarget(bool routed)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); Assert.True(h.Plain.Focus()); int executed = 0, background = 0;
        if (routed)
        {
            var command = new RoutedCommand("Popup", typeof(MacOSPopupInputRouteTests));
            command.InputGestures.Add(new KeyGesture(Key.K, ModifierKeys.Control));
            h.Panel.CommandBindings.Add(new CommandBinding(command, (_, e) =>
            { Assert.Same(h.Plain, e.OriginalSource); executed++; }, (_, e) => e.CanExecute = true));
        }
        else h.Panel.InputBindings.Add(new KeyBinding(new ActionCommand(() => executed++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Window.InputBindings.Add(new KeyBinding(new ActionCommand(() => background++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Key(Key.K, modifiers: 8); Assert.Equal((1, 0), (executed, background));
    }

    [Fact]
    public void DialogRoutedCommandsFromPopupDoNotReachBackgroundObservers()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); Assert.True(h.Plain.Focus()); int executed = 0, background = 0;
        h.Dialog.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find,
            (_, e) => { Assert.Same(h.Plain, e.Source); executed++; }, (_, e) => e.CanExecute = true));
        h.Window.AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler((_, _) => background++), true);
        h.Key(Key.F, modifiers: 8); Assert.Equal((1, 0), (executed, background));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PopupInstanceBindingsParticipateInTheChildInputRoute(bool declaredWithoutTarget)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(declaredWithoutTarget: declaredWithoutTarget); Assert.True(h.Plain.Focus()); int executed = 0;
        h.Popups[0].InputBindings.Add(new KeyBinding(new ActionCommand(() => executed++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Key(Key.K, modifiers: 8); Assert.Equal(1, executed);
    }

    [Theory]
    [InlineData("close")] [InlineData("retarget")] [InlineData("focus")]
    public void PreviewCannotDeliverTheOldPopupKeyAfterItsRouteChanges(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: false); h.Editor.Select(0, 0); int preview = 0;
        h.Editor.PreviewKeyDown += (_, _) => { preview++; h.Mutate(mutation); };
        h.Key(Key.Delete); Assert.Equal(1, preview); Assert.Equal("ab", h.Editor.Text);
    }

    [Theory]
    [InlineData("close")] [InlineData("retarget")] [InlineData("focus")]
    public void PreviewCannotDeliverTheOldPopupTextAfterItsRouteChanges(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: false); int preview = 0;
        h.Editor.PreviewTextInput += (_, _) => { preview++; h.Mutate(mutation); };
        h.Text("x"); Assert.Equal(1, preview); Assert.Equal("ab", h.Editor.Text);
    }

    [Theory]
    [InlineData("retarget")] [InlineData("reparent")]
    public void AQueryCannotExecuteThroughAnOldPopupAnchor(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: false); Assert.True(h.Plain.Focus()); int queries = 0, executed = 0;
        h.Panel.InputBindings.Add(new KeyBinding(new ActionCommand(() => executed++, () =>
        { queries++; h.Mutate(mutation); return true; }), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Key(Key.K, modifiers: 8); Assert.Equal(1, queries); Assert.Equal(0, executed);
    }

    [Theory]
    [InlineData("close")] [InlineData("hide-anchor")] [InlineData("disable-anchor")]
    [InlineData("foreign-anchor")] [InlineData("cycle")]
    public void InvalidPopupOwnershipCannotReceiveText(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: false); using var foreign = new Host(); Assert.True(h.Editor.Focus());
        if (mutation == "foreign-anchor") h.Popups[0].PlacementTarget = foreign.Anchor;
        else if (mutation == "cycle") h.Popups[0].PlacementTarget = h.Editor;
        else h.Mutate(mutation);
        h.Text("x"); Assert.Equal("ab", h.Editor.Text);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TabNeverMovesBehindTheActiveDialogFromItsPopup(bool reverse)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host();
        for (int i = 0; i < 6; i++)
        { h.Key(Key.Tab, modifiers: reverse ? 1 : 0); Assert.NotSame(h.Background, Keyboard.FocusedElement); }
    }

    [Fact]
    public void ClosingAnOwnedPopupRestoresItsValidAnchorFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Popups[0].IsOpen = false;
        Assert.Same(h.Anchor, Keyboard.FocusedElement);
    }

    [Fact]
    public void ClosingTheParentClosesItsOwnedNestedPopup()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(nested: 1); h.Popups[0].IsOpen = false;
        Assert.False(h.Popups[1].IsOpen); Assert.Same(h.Anchor, Keyboard.FocusedElement);
    }

    [Fact]
    public void AChildClosedCallbackCannotReopenAgainstItsClosingParent()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(nested: 1); var child = h.Popups[1];
        EventHandler handler = (_, _) => child.IsOpen = true;
        child.Closed += handler;
        try
        {
            h.Popups[0].IsOpen = false; Assert.False(child.IsOpen);
            Assert.Null(typeof(Popup).GetField("_popupRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(child));
            Assert.Same(h.Anchor, Keyboard.FocusedElement);
        }
        finally { child.Closed -= handler; }
    }

    [Fact]
    public void ClosingThePopupCancelsItsCompositionWithoutCommitting()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart });
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionUpdate, CompositionText = "old", CompositionCursor = 3 });
        Assert.True(InputMethod.IsComposing); h.Popups[0].IsOpen = false;
        Assert.False(InputMethod.IsComposing); Assert.Equal("ab", h.Editor.Text);
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionEnd, CompositionText = "旧" });
        Assert.Equal("ab", h.Editor.Text);
    }

    [Fact]
    public void CompositionCancellationKeepsTheFocusChosenByItsCallback()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart });
        EventHandler<CompositionResultEventArgs> handler = (_, _) => Assert.True(h.Other.Focus());
        InputMethod.CompositionEnded += handler;
        try { h.Popups[0].IsOpen = false; Assert.Same(h.Other, Keyboard.FocusedElement); }
        finally { InputMethod.CompositionEnded -= handler; }
    }

    [Fact]
    public void AClosedCallbackMayStartANewPopupComposition()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart });
        h.Send(new PlatformEvent { Type = PlatformEventType.CompositionUpdate, CompositionText = "old", CompositionCursor = 3 });
        EventHandler handler = (_, _) =>
        { h.Popups[0].IsOpen = true; Assert.True(h.Editor.Focus()); h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart }); };
        h.Popups[0].Closed += handler;
        try
        {
            h.Popups[0].IsOpen = false; Assert.True(h.Popups[0].IsOpen); Assert.True(InputMethod.IsComposing);
            Assert.Same(h.Editor, InputMethod.CurrentTarget); Assert.Equal("", InputMethod.CompositionString);
        }
        finally { h.Popups[0].Closed -= handler; }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReopeningTheSameEditorDuringPreviewRetiresTheOldInput(bool text)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(modal: false); h.Editor.Select(0,0);
        void Reopen() { h.Popups[0].IsOpen = false; h.Popups[0].IsOpen = true; Assert.True(h.Editor.Focus()); }
        if (text) h.Editor.PreviewTextInput += (_, _) => Reopen(); else h.Editor.PreviewKeyDown += (_, _) => Reopen();
        if (text) h.Text("x"); else h.Key(Key.Delete);
        Assert.Equal("ab", h.Editor.Text);
    }

    [Fact]
    public void ClosingRestoresTheFocusThatPrecededOpeningThePopup()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(preFocus: true); h.Popups[0].IsOpen = false;
        Assert.Same(h.Other, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData("retarget")] [InlineData("reparent")]
    public void NativeReplacementDoesNotContinueAfterItsPreviewChangesThePopupRoute(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int previews = 0;
        h.Editor.PreviewTextInput += (_, _) => { previews++; h.Mutate(mutation); };
        var request = new PlatformImeTextRequest(0, 1, "中", true);
        h.Send(new PlatformEvent { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
        Assert.Equal(1, previews); Assert.False(request.Applied); Assert.Equal("ab", h.Editor.Text);
    }

    [Theory]
    [InlineData("retarget")] [InlineData("reparent")]
    public void CompositionEndDoesNotCommitThroughAnOldPopupRoute(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart });
        Assert.True(InputMethod.IsComposing);
        EventHandler<CompositionResultEventArgs> handler = (_, _) => h.Mutate(mutation);
        InputMethod.CompositionEnded += handler;
        try
        {
            h.Send(new PlatformEvent { Type = PlatformEventType.CompositionEnd, CompositionText = "中" });
            Assert.Equal("ab", h.Editor.Text);
        }
        finally { InputMethod.CompositionEnded -= handler; }
    }

    private sealed class ActionCommand(Action execute, Func<bool>? query = null) : ICommand
    {
        public bool CanExecute(object? parameter) => query?.Invoke() ?? true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private sealed class DialogWithContent : ContentDialog
    { internal DialogWithContent(UIElement content) { Visibility = Visibility.Visible; AddVisualChild(content); } }
    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly StackPanel Root = new(), Inside = new(), Replacement = new();
        internal readonly Control Anchor = new() { Focusable = true, Width = 180, Height = 30 }, Other = new() { Focusable = true };
        internal readonly TextBox Background = new() { Text = "background" };
        internal readonly ContentDialog Dialog;
        internal readonly List<Popup> Popups = [];
        internal StackPanel Panel = null!;
        internal TextBox Editor = null!;
        internal Control Plain = null!;
        private static readonly PropertyInfo Active = typeof(Window).GetProperty("ActiveContentDialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        internal Host(bool modal = true, int nested = 0, bool declaredWithoutTarget = false, bool preFocus = false)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); if (InputMethod.IsComposing) InputMethod.EndComposition();
            Inside.Children.Add(Anchor); Inside.Children.Add(Other); Inside.Children.Add(Replacement);
            Dialog = new DialogWithContent(Inside); Root.Children.Add(Dialog); Root.Children.Add(Background);
            Window = new DisplayedTestWindow { Width = 500, Height = 450, Content = Root, TitleBarStyle = WindowTitleBarStyle.Native };
            Layout(); if (modal) Active.SetValue(Window, Dialog);
            if (preFocus) Assert.True(Other.Focus());
            UIElement anchor = Anchor;
            for (int i = 0; i <= nested; i++)
            {
                Panel = new StackPanel { Width = 220, Height = 100 }; Editor = new TextBox { Text = "ab", Width = 180, Height = 45 };
                Plain = new Control { Focusable = true, Width = 180, Height = 30 }; Panel.Children.Add(Editor); Panel.Children.Add(Plain);
                var popup = new Popup { PlacementTarget = declaredWithoutTarget ? null : anchor, Child = Panel,
                    StaysOpen = true, ShouldConstrainToRootBounds = true, PopupAnimation = PopupAnimation.None };
                if (declaredWithoutTarget) Inside.Children.Add(popup);
                Popups.Add(popup); popup.IsOpen = true; Layout();
                Assert.IsType<PopupRoot>(Panel.VisualParent); Assert.Same(Window.OverlayLayer, Panel.VisualParent!.VisualParent);
                anchor = Plain;
            }
            Assert.True(Editor.Focus()); Editor.Select(2, 0);
        }
        private void Layout() { Window.Measure(new Size(500,450)); Window.Arrange(new Rect(0,0,500,450)); }
        internal void Mutate(string kind)
        {
            if (kind == "close") Popups[0].IsOpen = false;
            else if (kind == "retarget") Popups[0].PlacementTarget = Other;
            else if (kind == "focus") Assert.True(Other.Focus());
            else if (kind == "reparent") { Inside.Children.Remove(Anchor); Replacement.Children.Add(Anchor); }
            else if (kind == "hide-anchor") Anchor.Visibility = Visibility.Collapsed;
            else if (kind == "disable-anchor") Anchor.IsEnabled = false;
        }
        internal void Key(Key key, int modifiers = 0, bool up = false) => Send(new PlatformEvent { Type = up ? PlatformEventType.KeyUp : PlatformEventType.KeyDown,
            KeyCode = KeyInterop.VirtualKeyFromKey(key), Modifiers = modifiers });
        internal void Text(string text) { foreach (char c in text) Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = c }); }
        internal void Send(PlatformEvent e) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [e]);
        public void Dispose()
        {
            if (InputMethod.IsComposing) InputMethod.EndComposition();
            foreach (var popup in Popups.AsEnumerable().Reverse()) popup.IsOpen = false;
            Active.SetValue(Window, null); Keyboard.ClearFocus(); Window.Close();
        }
    }
}
