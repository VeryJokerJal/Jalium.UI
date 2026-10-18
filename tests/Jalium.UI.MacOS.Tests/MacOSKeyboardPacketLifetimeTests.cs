using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSKeyboardPacketLifetimeTests
{
    public static IEnumerable<object[]> ReentryCases()
    {
        foreach (string phase in new[] { "window", "preview", "bubble", "query" })
        foreach (string replacement in new[] { "down", "up", "text" }) yield return [phase, replacement];
    }

    [Theory, MemberData(nameof(ReentryCases))]
    public void ANewPacketRetiresTheOldKeyAtEveryCallback(string phase, string replacement)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int executions = 0, oldBubbleTail = 0, newest = 0; bool nested = false;
        void Replace()
        {
            if (nested) return; nested = true;
            if (replacement == "text") h.Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = '新' });
            else h.Key(Key.L, up: replacement == "up");
        }
        h.Window.PreviewDown = key => { if (key == Key.K && phase == "window") Replace(); };
        h.Target.PreviewKeyDown += (_, e) => { if (e.Key == Key.K && phase == "preview") Replace(); };
        h.Target.KeyDown += (_, e) => { if (e.Key == Key.K && phase == "bubble") Replace(); };
        h.Target.KeyDown += (_, e) => { if (e.Key == Key.K) oldBubbleTail++; else if (e.Key == Key.L) newest++; };
        h.Target.KeyUp += (_, e) => { if (e.Key == Key.L) newest++; };
        h.Target.TextInput += (_, e) => { if (e.Text == "新") newest++; };
        h.Target.InputBindings.Add(new KeyBinding(new Counter(() => { if (phase == "query") Replace(); return true; }, () => executions++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Key(Key.K, control: true);
        Assert.True(nested); Assert.Equal(1, newest); Assert.Equal(0, executions);
        Assert.Equal(phase == "query" ? 1 : 0, oldBubbleTail);
        Assert.Same(h.Target, Keyboard.FocusedElement); Assert.True(h.Window.IsActive);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void NewInputStopsOlderKeyUpHandlers(bool windowHook)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); bool nested = false; int tail = 0, newest = 0;
        void Replace() { if (nested) return; nested = true; h.Key(Key.L); }
        h.Window.PreviewUp = k => { if (k == Key.K && windowHook) Replace(); };
        h.Target.PreviewKeyUp += (_, e) => { if (e.Key == Key.K && !windowHook) Replace(); };
        h.Target.KeyUp += (_, e) => { if (e.Key == Key.K) tail++; };
        h.Target.KeyDown += (_, e) => { if (e.Key == Key.L) newest++; };
        h.Key(Key.K, up: true);
        Assert.True(nested); Assert.Equal(1, newest); Assert.Equal(0, tail);
    }

    [Theory, InlineData("key"), InlineData("text")]
    public void NewInputStopsOlderTextHandlers(string replacement)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); bool nested = false; int tail = 0, newest = 0;
        h.Target.PreviewTextInput += (_, e) =>
        {
            if (nested || e.Text != "旧") return; nested = true;
            if (replacement == "key") h.Key(Key.L);
            else h.Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = '新' });
        };
        h.Target.TextInput += (_, e) => { if (e.Text == "旧") tail++; else if (e.Text == "新") newest++; };
        h.Target.KeyDown += (_, e) => { if (e.Key == Key.L) newest++; };
        h.Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = '旧' });
        Assert.True(nested); Assert.Equal(1, newest); Assert.Equal(0, tail);
    }

    [Theory, InlineData(Key.Tab), InlineData(Key.Enter), InlineData(Key.Escape)]
    public void AReplacedPacketCannotPerformDefaultNavigationOrButtons(Key key)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int clicks = 0; bool nested = false;
        var button = new Button { IsDefault = true, IsCancel = true, Content = "确认" }; h.Panel.Children.Add(button); button.Click += (_, _) => clicks++;
        h.Target.PreviewKeyDown += (_, e) => { if (e.Key == key && !nested) { nested = true; h.Key(Key.L); } };
        h.Key(key);
        Assert.True(nested); Assert.Equal(0, clicks); Assert.Same(h.Target, Keyboard.FocusedElement);
    }

    [Theory, InlineData(false, Key.Space), InlineData(true, Key.Space), InlineData(true, Key.L)]
    public void RetiringAnOldPacketPreservesTheNewestPressedState(bool oldUp, Key newerKey)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); if (oldUp) h.Key(Key.Enter);
        bool nested = false;
        KeyEventHandler replace = (_, e) => { if (e.Key == Key.Enter && !nested) { nested = true; h.Key(newerKey); } };
        if (oldUp) h.Target.PreviewKeyUp += replace; else h.Target.PreviewKeyDown += replace;
        h.Key(Key.Enter, up: oldUp);
        Assert.True(nested); Assert.Equal(newerKey == Key.Space, h.Target.IsPressed);
        Assert.Equal(newerKey == Key.Space, h.Panel.IsPressed);
        h.Key(newerKey, up: true); Assert.False(h.Target.IsPressed); Assert.False(h.Panel.IsPressed);
    }

    [Theory]
    [InlineData(false, Key.Space), InlineData(true, Key.Space)]
    [InlineData(false, Key.L), InlineData(true, Key.L)]
    public void WindowKeyUpHooksRetireOnlyTheOldPressedChain(bool consume, Key newerKey)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Key(Key.Enter); bool nested = false;
        h.Window.ConsumeUp = consume;
        h.Window.PreviewUp = key => { if (key == Key.Enter && !nested) { nested = true; h.Key(newerKey); } };
        h.Key(Key.Enter, up: true);
        Assert.True(nested); Assert.Equal(newerKey == Key.Space, h.Target.IsPressed);
        Assert.Equal(newerKey == Key.Space, h.Panel.IsPressed);
        h.Window.ConsumeUp = false; h.Key(newerKey, up: true);
        Assert.False(h.Target.IsPressed); Assert.False(h.Panel.IsPressed);
    }

    [Fact]
    public void ConsumingWindowKeyUpStillReleasesItsPressedChain()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); h.Key(Key.Enter); Assert.True(h.Target.IsPressed);
        h.Window.ConsumeUp = true; h.Key(Key.Enter, up: true);
        Assert.False(h.Target.IsPressed); Assert.False(h.Panel.IsPressed);
    }

    [Theory, InlineData("key"), InlineData("text"), InlineData("ime")]
    public void NativeReplacementStopsWhenItsPreviewReceivesNewerInput(string replacement)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(editor: true); var editor = (TextBox)h.Target;
        string before = editor.Text; editor.Select(before.Length, 0); bool nested = false; int oldTail = 0;
        editor.PreviewTextInput += (_, e) =>
        {
            if (nested || e.Text != "旧") return; nested = true;
            if (replacement == "key") h.Key(Key.L);
            else if (replacement == "text") h.Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = '新' });
            else h.Send(new PlatformEvent { Type = PlatformEventType.ImeTextRequest,
                ImeTextRequest = new PlatformImeTextRequest(before.Length, 0, "新", true) });
        };
        editor.AddHandler(UIElement.TextInputEvent, new TextCompositionEventHandler((_, e) => { if (e.Text == "旧") oldTail++; }), handledEventsToo: true);
        var request = new PlatformImeTextRequest(before.Length, 0, "旧", true);
        h.Send(new PlatformEvent { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
        Assert.True(nested); Assert.False(request.Applied); Assert.Equal(0, oldTail);
        Assert.Equal(before + (replacement == "key" ? "" : "新"), editor.Text);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ANativeTextRequestRetiresTheOlderKeyPacket(bool replace)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(editor: true); var editor = (TextBox)h.Target; int tail = 0, executed = 0; bool nested = false;
        editor.PreviewKeyDown += (_, e) =>
        {
            if (nested || e.Key != Key.K) return; nested = true;
            var request = new PlatformImeTextRequest(0, 1, replace ? "新" : null, replace);
            h.Send(new PlatformEvent { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.True(request.Applied);
        };
        editor.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) => { if (e.Key == Key.K) tail++; }), handledEventsToo: true);
        editor.InputBindings.Add(new KeyBinding(new Counter(() => true, () => executed++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Key(Key.K, control: true); Assert.True(nested); Assert.Equal(0, tail); Assert.Equal(0, executed);
    }

    [Fact]
    public void CompositionEndDoesNotCommitAfterItsCallbackReceivesNewText()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(editor: true); var editor = (TextBox)h.Target; string before = editor.Text;
        editor.Select(before.Length, 0); h.Send(new PlatformEvent { Type = PlatformEventType.CompositionStart });
        EventHandler<CompositionResultEventArgs> newer = (_, _) => h.Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = '新' });
        InputMethod.CompositionEnded += newer;
        try { h.Send(new PlatformEvent { Type = PlatformEventType.CompositionEnd, CompositionText = "旧" }); Assert.Equal(before + "新", editor.Text); }
        finally { InputMethod.CompositionEnded -= newer; InputMethod.CancelComposition(); }
    }

    [Fact]
    public void CompositionUpdateDoesNotResumeAfterItsStartCallbackReceivesNewInput()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(editor: true); int updated = 0;
        EventHandler newer = (_, _) => h.Key(Key.L);
        EventHandler<CompositionEventArgs> onUpdate = (_, _) => updated++;
        InputMethod.CompositionStarted += newer; InputMethod.CompositionUpdated += onUpdate;
        try { h.Send(new PlatformEvent { Type = PlatformEventType.CompositionUpdate, CompositionText = "旧", CompositionCursor = 1 }); Assert.Equal(0, updated); }
        finally { InputMethod.CompositionStarted -= newer; InputMethod.CompositionUpdated -= onUpdate; InputMethod.CancelComposition(); }
    }

    [Fact]
    public void CapturingReadOnlyFocusLeasesDoesNotRetireTheCurrentPacket()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int tail = 0;
        h.Target.PreviewKeyDown += (_, _) =>
        {
            var one = ((IInputDispatcherHost)h.Window).CaptureNativeInputLifetime(h.Target, false)!;
            var two = ((IInputDispatcherHost)h.Window).CaptureNativeInputLifetime(h.Target, false)!;
            Assert.True(one()); Assert.True(two());
        };
        h.Target.KeyDown += (_, _) => tail++; h.Key(Key.L); Assert.Equal(1, tail);
    }

    [Fact]
    public void AnUnreplacedNativeShortcutExecutesExactlyOnce()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int queried = 0, executed = 0;
        h.Target.InputBindings.Add(new KeyBinding(new Counter(() => { queried++; return true; }, () => executed++), new KeyGesture(Key.K, ModifierKeys.Control)));
        h.Key(Key.K, control: true); h.Key(Key.K, up: true, control: true);
        Assert.Equal(1, queried); Assert.Equal(1, executed);
    }

    [Fact]
    public void AnUnreplacedNativeTextPacketStillRoutesOnce()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(); int preview = 0, bubble = 0;
        h.Target.PreviewTextInput += (_, e) => { if (e.Text == "中") preview++; };
        h.Target.TextInput += (_, e) => { if (e.Text == "中") bubble++; };
        h.Send(new PlatformEvent { Type = PlatformEventType.CharInput, Codepoint = '中' });
        Assert.Equal(1, preview); Assert.Equal(1, bubble);
    }

    private sealed class Counter(Func<bool> query, Action execute) : ICommand
    { public bool CanExecute(object? p) => query(); public void Execute(object? p) => execute(); public event EventHandler? CanExecuteChanged { add { } remove { } } }
    private sealed class ProbeWindow : Window
    {
        internal Action<Key>? PreviewDown, PreviewUp;
        internal bool ConsumeUp;
        internal ProbeWindow() => typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, [Visibility.Visible]);
        protected override bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat) { PreviewDown?.Invoke(key); return false; }
        protected override bool OnPreviewWindowKeyUp(Key key, ModifierKeys modifiers) { PreviewUp?.Invoke(key); return ConsumeUp; }
    }
    private sealed class Host : IDisposable
    {
        internal readonly ProbeWindow Window;
        internal readonly StackPanel Panel = new();
        internal readonly Control Target;
        internal Host(bool editor = false)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Target = editor ? new TextBox { Text = "中文🙂", Focusable = true, Width = 200, Height = 40 } : new Control { Focusable = true, Width = 100, Height = 40 };
            Panel.Children.Add(Target); Panel.Children.Add(new Control { Focusable = true, Width = 100, Height = 40 });
            Window = new ProbeWindow { Width = 400, Height = 250, Content = Panel, TitleBarStyle = WindowTitleBarStyle.Native };
            Window.Measure(new Size(400, 250)); Window.Arrange(new Rect(0, 0, 400, 250));
            Assert.True(Target.Focus()); Send(new PlatformEvent { Type = PlatformEventType.Activate });
        }
        internal void Key(Key key, bool up = false, bool control = false) => Send(new PlatformEvent { Type = up ? PlatformEventType.KeyUp : PlatformEventType.KeyDown, KeyCode = KeyInterop.VirtualKeyFromKey(key), Modifiers = control ? 8 : 0 });
        internal void Send(PlatformEvent e) => typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, [e]);
        public void Dispose() { Keyboard.ClearFocus(); Window.Close(); }
    }
}
