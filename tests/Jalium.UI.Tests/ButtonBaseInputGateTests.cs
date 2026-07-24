using System;
using System.Windows.Input;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression tests for the "swallowed rapid click" bug: a ButtonBase bound to a command whose
/// CanExecute toggles while executing (e.g. ReactiveCommand) had IsEnabled driven false by an
/// asynchronously-delivered CanExecuteChanged, so the `if (!IsEnabled) return` input guards ate
/// clicks for a beat after the command finished. The fix routes those guards through
/// <see cref="ButtonBase.CanRespondToInput"/>, which re-queries the command live and treats a
/// stale command-driven disable as clickable (self-healing IsEnabled), while still honoring a real
/// disable (no command, disabled ancestor, or a command that genuinely cannot execute).
/// </summary>
// Join the "Application" collection so these run serialized with the other tests that touch
// shared Application / input statics. The end-to-end cases raise real mouse events (CaptureMouse /
// Focus); without this they could run in parallel with theme tests and corrupt their state.
[Collection("Application")]
public class ButtonBaseInputGateTests
{
    // ── CanRespondToInput decision logic (pure unit, no Application/render needed) ──────────────

    [Fact]
    public void CanRespondToInput_WhenEffectivelyEnabled_ReturnsTrue()
    {
        var button = new Button();

        Assert.True(button.IsEnabled);
        Assert.True(button.CanRespondToInput());
    }

    [Fact]
    public void CanRespondToInput_NoCommand_AppDisabled_ReturnsFalse()
    {
        // P1: an app-disabled button with no command must keep swallowing input.
        var button = new Button();
        button.SetValue(UIElement.IsEnabledProperty, false);

        Assert.False(button.CanRespondToInput());
        Assert.False(button.IsEnabled); // not healed
    }

    [Fact]
    public void CanRespondToInput_AncestorDisabled_ReturnsFalse_EvenWhenCommandCanExecute()
    {
        // P1: a disabled ancestor is a real disable, never overridden by a live command.
        var panel = new HostPanel();
        var button = new Button { Command = new ToggleCommand(() => true) };
        panel.Attach(button);

        panel.SetValue(UIElement.IsEnabledProperty, false);

        Assert.False(button.IsEnabled);            // effective = local && ancestor
        Assert.False(button.CanRespondToInput());  // blocked by ancestor branch
    }

    [Fact]
    public void CanRespondToInput_CommandCannotExecute_ReturnsFalse()
    {
        // P2 / P4: a command genuinely unable to run (async in-flight, or legitimately false)
        // keeps the button blocked. Binding a CanExecute=false command drives IsEnabled false
        // via UpdateCanExecute, exactly as the framework does at runtime.
        var button = new Button { Command = new ToggleCommand(() => false) };

        Assert.False(button.IsEnabled);
        Assert.False(button.CanRespondToInput());
    }

    [Fact]
    public void CanRespondToInput_StaleCommandFlap_ReturnsTrue_AndHealsIsEnabled()
    {
        // The core fix: command CAN execute right now, but IsEnabled is stuck false from a stale
        // async CanExecuteChanged. Treat it as clickable and self-heal the local IsEnabled value.
        var button = new Button { Command = new ToggleCommand(() => true) };
        button.SetValue(UIElement.IsEnabledProperty, false); // simulate the async flap residue
        Assert.False(button.IsEnabled);

        Assert.True(button.CanRespondToInput());
        Assert.True(button.IsEnabled); // healed so the disabled-style trigger reverts immediately
    }

    [Fact]
    public void CanRespondToInput_RepeatButtonInheritsGate()
    {
        // RepeatButton is the worst case (auto-repeat while held); it must see the same gate.
        var repeat = new RepeatButton { Command = new ToggleCommand(() => true) };
        repeat.SetValue(UIElement.IsEnabledProperty, false);

        Assert.True(repeat.CanRespondToInput());
        Assert.True(repeat.IsEnabled);
    }

    // ── End-to-end: a stale-flap button is clickable again in the default ClickMode.Release ─────

    [Fact]
    public void Button_StaleCommandFlap_MouseDownThenUp_StillClicks_ReleaseMode()
    {
        var command = new ToggleCommand(() => true);
        var button = new Button { ClickMode = ClickMode.Release, Command = command };

        int clicks = 0;
        button.Click += (_, _) => clicks++;

        // Stale async flap: command can run, but IsEnabled is stuck false.
        button.SetValue(UIElement.IsEnabledProperty, false);
        Assert.False(button.IsEnabled);

        button.RaiseEvent(CreateMouseDown());

        // Down self-heals IsEnabled and re-asserts hover/pressed so the Release condition
        // (wasPressed && IsMouseOver) below holds — without this the click is silently dropped.
        Assert.True(button.IsEnabled);
        Assert.True(button.IsMouseOver);
        Assert.True(button.IsPressed);

        button.RaiseEvent(CreateMouseUp());

        Assert.Equal(1, clicks);
        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public void Button_NoCommand_AppDisabled_MouseDownUp_DoesNotClick()
    {
        // P1 end-to-end: a genuinely disabled button still eats the gesture.
        var button = new Button { ClickMode = ClickMode.Release };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        button.SetValue(UIElement.IsEnabledProperty, false);

        button.RaiseEvent(CreateMouseDown());
        button.RaiseEvent(CreateMouseUp());

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Button_TemplateReplacement_ReparentsVisualContent_AndTextStillClicks()
    {
        var label = new TextBlock { Text = "Switch theme" };
        var content = new StackPanel();
        content.Children.Add(label);
        var button = new Button
        {
            Content = content,
            Template = CreateContentPresenterTemplate()
        };
        button.Measure(new Size(180, 40));
        button.Arrange(new Rect(0, 0, 180, 40));

        // Runtime resource/style refreshes can replace a control template while
        // preserving its UIElement Content. The replacement presenter must take
        // real visual ownership instead of rendering an element whose parent
        // still points into the discarded template tree.
        button.Template = CreateContentPresenterTemplate();
        button.Measure(new Size(180, 40));
        button.Arrange(new Rect(0, 0, 180, 40));

        Assert.True(IsVisualDescendantOf(label, button));

        var clicks = 0;
        button.Click += (_, _) => clicks++;
        try
        {
            label.RaiseEvent(CreateMouseDown());
            label.RaiseEvent(CreateMouseUp());
        }
        finally
        {
            if (button.IsMouseCaptured)
                button.ReleaseMouseCapture();
        }

        Assert.Equal(1, clicks);
    }

    // ── ClickMode.Hover gate (previously had no IsEnabled check at all) ─────────────────────────

    [Fact]
    public void Hover_AppDisabledNoCommand_DoesNotClickOnEnter()
    {
        // Before the fix a disabled Hover-mode button still fired Click on mouse-enter.
        var button = new Button { ClickMode = ClickMode.Hover };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        button.SetValue(UIElement.IsEnabledProperty, false);

        button.RaiseEvent(new MouseEventArgs(UIElement.MouseEnterEvent) { Source = button });

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Hover_EnabledButton_ClicksOnEnter()
    {
        var button = new Button { ClickMode = ClickMode.Hover };
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        button.RaiseEvent(new MouseEventArgs(UIElement.MouseEnterEvent) { Source = button });

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Hover_StaleCommandFlap_ClicksOnEnter()
    {
        // A Hover-mode button only transiently disabled by a command's async CanExecute flap
        // still activates on hover (and self-heals), like the other gates.
        var command = new ToggleCommand(() => true);
        var button = new Button { ClickMode = ClickMode.Hover, Command = command };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        button.SetValue(UIElement.IsEnabledProperty, false);

        button.RaiseEvent(new MouseEventArgs(UIElement.MouseEnterEvent) { Source = button });

        Assert.Equal(1, clicks);
        Assert.True(button.IsEnabled);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static MouseButtonEventArgs CreateMouseDown() => CreateMouse(UIElement.MouseDownEvent, MouseButtonState.Pressed);

    private static MouseButtonEventArgs CreateMouseUp() => CreateMouse(UIElement.MouseUpEvent, MouseButtonState.Released);

    private static MouseButtonEventArgs CreateMouse(RoutedEvent routedEvent, MouseButtonState leftState)
    {
        return new MouseButtonEventArgs(
            routedEvent,
            new Point(5, 5),
            MouseButton.Left,
            leftState,
            clickCount: 1,
            leftButton: leftState,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);
    }

    private static ControlTemplate CreateContentPresenterTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        template.SetVisualTree(static () => new Border
        {
            Child = new ContentPresenter()
        });
        return template;
    }

    private static bool IsVisualDescendantOf(Visual descendant, Visual ancestor)
    {
        for (Visual? current = descendant; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>Minimal visual-tree parent so a button has a VisualParent for the ancestor test.</summary>
    private sealed class HostPanel : FrameworkElement
    {
        public void Attach(UIElement child) => AddVisualChild(child);
    }

    /// <summary>ICommand whose CanExecute is driven by a predicate, mimicking a ReactiveCommand
    /// whose CanExecute toggles. ExecuteCount records invocations.</summary>
    private sealed class ToggleCommand : ICommand
    {
        private readonly Func<bool> _canExecute;
        public int ExecuteCount { get; private set; }

        public ToggleCommand(Func<bool> canExecute) => _canExecute = canExecute;

        // Empty accessors (like the suite's DelegateCommand) avoid CS0067; ButtonBase drives
        // IsEnabled through OnCommandChanged/UpdateCanExecute, which these tests exercise directly.
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => _canExecute();

        public void Execute(object? parameter) => ExecuteCount++;
    }
}
