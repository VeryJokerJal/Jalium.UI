using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[CollectionDefinition("macOS Window globals", DisableParallelization = true)]
public sealed class MacOSWindowTestCollection;

[Collection("macOS Window globals")]
public sealed class MacOSWindowBehaviorTests : MacOSGeometryTestBase
{
    private static readonly MethodInfo PlatformEventMethod = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Dispatch(Window window, PlatformEvent evt) => PlatformEventMethod.Invoke(window, [evt]);
    private static void SetField(Window window, string name, object? value) =>
        typeof(Window).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    [Fact]
    public void TabNavigation_WrapsWithinMacOSWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var first = new Button();
        var last = new Button();
        var panel = new StackPanel();
        panel.Children.Add(first); panel.Children.Add(last);
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = panel };
        try
        {
            Assert.True(first.Focus());
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x09 });
            Assert.Same(last, Keyboard.FocusedElement);
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x09 });
            Assert.Same(first, Keyboard.FocusedElement);
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x09, Modifiers = 0x01 });
            Assert.Same(last, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TabNavigation_SkipsCssHiddenOrExitingTargets(bool hiddenAncestor, bool exiting)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var first = new TextBox(); var skipped = new Button(); var last = new Button();
        var group = new StackPanel(); group.Children.Add(skipped);
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(group); panel.Children.Add(last);
        var window = new DisplayedTestWindow { Content = panel };
        UIElement hiddenTarget = hiddenAncestor ? group : skipped;
        try
        {
            if (exiting) Css.SetStyle(hiddenTarget, "display: block; transition: display 60s allow-discrete");
            Css.SetStyle(hiddenTarget, exiting ? "display: none; transition: display 60s allow-discrete" : "visibility: hidden");
            Assert.True(exiting ? CssDisplayProperties.IsExitInert(skipped) : !skipped.IsVisible);
            Assert.True(first.Focus());
            Assert.Same(last, KeyboardNavigation.PredictFocus(first, FocusNavigationDirection.Next));
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x09 });
            Assert.Same(last, Keyboard.FocusedElement);
            Assert.Same(first, KeyboardNavigation.PredictFocus(last, FocusNavigationDirection.Previous));
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x09, Modifiers = 0x01 });
            Assert.Same(first, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabNavigation_UsesCssVisibleDescendantWithinHiddenContainer(bool reverse)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var first = new Button(); var revealed = new Button(); var last = new Button();
        var group = new StackPanel(); group.Children.Add(revealed);
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(group); panel.Children.Add(last);
        var window = new DisplayedTestWindow { Content = panel };
        try
        {
            Css.SetStyle(group, "visibility: hidden"); Css.SetStyle(revealed, "visibility: visible");
            Assert.False(group.IsVisible); Assert.True(revealed.IsVisible);
            Assert.True((reverse ? last : first).Focus());
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x09, Modifiers = reverse ? 0x01 : 0 });
            Assert.Same(revealed, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void Activation_RestoresLogicalFocusForEachMacOSWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var firstText = new TextBox();
        var secondText = new TextBox();
        var first = new DisplayedTestWindow { Content = firstText };
        var second = new DisplayedTestWindow { Content = secondText };
        try
        {
            Assert.True(firstText.Focus());
            Dispatch(first, new() { Type = PlatformEventType.Activate });
            Dispatch(first, new() { Type = PlatformEventType.Deactivate });
            Assert.True(secondText.Focus());
            Dispatch(second, new() { Type = PlatformEventType.Activate });
            Dispatch(second, new() { Type = PlatformEventType.Deactivate });
            Dispatch(first, new() { Type = PlatformEventType.Activate });
            Assert.Same(firstText, Keyboard.FocusedElement);
            Dispatch(first, new() { Type = PlatformEventType.CharInput, Codepoint = 'a' });
            Assert.Equal("a", firstText.Text);
            Assert.Empty(secondText.Text);
        }
        finally { Keyboard.ClearFocus(); second.Close(); first.Close(); }
    }

    [Fact]
    public void Input_DoesNotReachAnotherMacOSWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var otherText = new TextBox { Text = "keep" };
        var window = new DisplayedTestWindow();
        var other = new DisplayedTestWindow { Content = otherText };
        try
        {
            Assert.True(otherText.Focus());
            Dispatch(window, new() { Type = PlatformEventType.CharInput, Codepoint = 'a' });
            Assert.Equal("keep", otherText.Text);
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x08 });
            Assert.Equal("keep", otherText.Text);
            Assert.Same(otherText, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); other.Close(); window.Close(); }
    }

    [Fact]
    public void Activation_DoesNotRestoreDetachedFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var detached = new TextBox();
        var window = new DisplayedTestWindow { Content = detached };
        var otherText = new TextBox();
        var other = new DisplayedTestWindow { Content = otherText };
        try
        {
            Assert.True(detached.Focus());
            window.Content = new Border();
            Assert.True(otherText.Focus());
            Dispatch(window, new() { Type = PlatformEventType.Activate });
            Assert.Null(Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); other.Close(); window.Close(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DialogButtons_SkipCssHiddenTargets(bool hiddenAncestor, bool useCancel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var hidden = new Button { IsDefault = !useCancel, IsCancel = useCancel };
        var visible = new Button { IsDefault = !useCancel, IsCancel = useCancel };
        var hiddenPanel = new StackPanel(); hiddenPanel.Children.Add(hidden);
        var panel = new StackPanel(); panel.Children.Add(hiddenPanel); panel.Children.Add(visible);
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = panel };
        try
        {
            Css.SetStyle(hiddenAncestor ? hiddenPanel : hidden, "visibility: hidden");
            Assert.Equal(Visibility.Visible, hidden.Visibility); Assert.False(hidden.IsVisible);
            var host = (IInputDispatcherHost)window;
            Assert.Same(visible, host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
            int hiddenClicks = 0, visibleClicks = 0;
            hidden.Click += (_, _) => hiddenClicks++; visible.Click += (_, _) => visibleClicks++;
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = useCancel ? 0x1b : 0x0d });
            Assert.Equal(0, hiddenClicks); Assert.Equal(1, visibleClicks);
            Css.SetStyle(visible, "visibility: hidden");
            Assert.Null(host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogButtons_AllowCssVisibleOverridesButRespectCollapsedSubtrees(bool useCancel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var revealed = new Button { IsDefault = !useCancel, IsCancel = useCancel };
        var parent = new StackPanel(); parent.Children.Add(revealed);
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = parent };
        try
        {
            Css.SetStyle(parent, "visibility: hidden"); Css.SetStyle(revealed, "visibility: visible");
            Assert.False(parent.IsVisible); Assert.True(revealed.IsVisible);
            var host = (IInputDispatcherHost)window;
            Assert.Same(revealed, host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
            parent.Visibility = Visibility.Collapsed;
            Assert.Null(host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
            parent.ClearValue(UIElement.VisibilityProperty); Css.SetStyle(parent, "display: none");
            Assert.False(revealed.IsVisible);
            Assert.Null(host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DialogButtons_StayHiddenAfterRemovingExitStyle(bool resetDisplayFirst, bool useCancel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var descendant = new Button { IsDefault = !useCancel, IsCancel = useCancel };
        var fallback = new Button { IsDefault = !useCancel, IsCancel = useCancel };
        var group = new StackPanel(); group.Children.Add(descendant);
        var flex = new StackPanel(); flex.Children.Add(group); Css.SetStyle(flex, "display: flex");
        var panel = new StackPanel(); panel.Children.Add(flex); panel.Children.Add(fallback);
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = panel };
        try
        {
            Css.SetStyle(descendant, "visibility: visible");
            Css.SetStyle(group, "display: block; visibility: visible; transition: display 60s allow-discrete");
            Css.SetStyle(group, "display: none; visibility: visible; transition: display 60s allow-discrete");
            Assert.True(CssDisplayProperties.IsExitInert(descendant));
            if (resetDisplayFirst)
                Css.SetStyle(group, "display: block; visibility: visible; transition: none");
            Css.SetStyle(group, "visibility: hidden"); Css.SetStyle(descendant, string.Empty);
            Assert.False(descendant.IsVisible);
            var host = (IInputDispatcherHost)window;
            Assert.Same(fallback, host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
            int hiddenClicks = 0, fallbackClicks = 0;
            descendant.Click += (_, _) => hiddenClicks++; fallback.Click += (_, _) => fallbackClicks++;
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = useCancel ? 0x1b : 0x0d });
            Assert.Equal(0, hiddenClicks); Assert.Equal(1, fallbackClicks);
            Css.SetStyle(descendant, "visibility: visible");
            Assert.True(descendant.IsVisible);
            Assert.Same(descendant, host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
            Css.SetStyle(descendant, string.Empty);
            Assert.False(descendant.IsVisible);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DialogButtons_ExcludeExitingCssDisplayTransitions(bool exitingAncestor, bool useCancel)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var exiting = new Button { IsDefault = !useCancel, IsCancel = useCancel };
        var fallback = new Button { IsDefault = !useCancel, IsCancel = useCancel };
        var group = new StackPanel(); group.Children.Add(exiting);
        var panel = new StackPanel(); panel.Children.Add(group); panel.Children.Add(fallback);
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = panel };
        UIElement transitionTarget = exitingAncestor ? group : exiting;
        try
        {
            Css.SetStyle(transitionTarget, "display: block; transition: display 60s allow-discrete");
            var host = (IInputDispatcherHost)window;
            Assert.Same(exiting, host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
            Css.SetStyle(transitionTarget, "display: none; transition: display 60s allow-discrete");
            Assert.True(exiting.IsVisible);
            Assert.True(CssDisplayProperties.IsExitInert(exiting));
            Assert.Same(fallback, host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
            int exitingClicks = 0, fallbackClicks = 0;
            exiting.Click += (_, _) => exitingClicks++; fallback.Click += (_, _) => fallbackClicks++;
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = useCancel ? 0x1b : 0x0d });
            Assert.Equal(0, exitingClicks); Assert.Equal(1, fallbackClicks);
            Css.SetStyle(transitionTarget, "display: block; transition: none");
            Assert.False(CssDisplayProperties.IsExitInert(exiting));
            Assert.Same(exiting, host.FindButton(window, button => useCancel ? button.IsCancel : button.IsDefault));
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Activation_DropsExitingCssFocusBeforeDeferredRevalidation(bool exitingAncestor, bool focusGained)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = new TextBox { Text = "保留内容" }; var group = new StackPanel(); group.Children.Add(editor);
        var window = new DisplayedTestWindow { Content = group };
        UIElement transitionTarget = exitingAncestor ? group : editor;
        try
        {
            Css.SetStyle(transitionTarget, "display: block; transition: display 60s allow-discrete");
            Assert.True(editor.Focus()); Dispatch(window, new() { Type = PlatformEventType.Activate });
            Css.SetStyle(transitionTarget, "display: none; transition: display 60s allow-discrete");
            Assert.True(editor.IsVisible); Assert.True(CssDisplayProperties.IsExitInert(editor));
            Assert.Same(editor, Keyboard.FocusedElement);
            Dispatch(window, new() { Type = focusGained ? PlatformEventType.FocusGained : PlatformEventType.Activate });
            Assert.Null(Keyboard.FocusedElement);
            Dispatch(window, new() { Type = PlatformEventType.CharInput, Codepoint = 'x' });
            Assert.Equal("保留内容", editor.Text);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Input_SkipsHiddenOrExitingFocusBeforeDeferredRevalidation(bool hiddenAncestor, bool exiting)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var editor = new TextBox { Text = "保留内容" }; var group = new StackPanel(); group.Children.Add(editor);
        var window = new DisplayedTestWindow { Content = group };
        UIElement hiddenTarget = hiddenAncestor ? group : editor;
        try
        {
            if (exiting) Css.SetStyle(hiddenTarget, "display: block; transition: display 60s allow-discrete");
            Assert.True(editor.Focus());
            Css.SetStyle(hiddenTarget, exiting ? "display: none; transition: display 60s allow-discrete" : "visibility: hidden");
            Assert.Same(editor, Keyboard.FocusedElement);
            Assert.True(exiting ? CssDisplayProperties.IsExitInert(editor) : !editor.IsVisible);
            int editorKeys = 0; editor.PreviewKeyDown += (_, _) => editorKeys++;
            Dispatch(window, new() { Type = PlatformEventType.KeyDown, KeyCode = 0x2e });
            Assert.Equal(0, editorKeys); Assert.Equal("保留内容", editor.Text);
            Dispatch(window, new() { Type = PlatformEventType.CharInput, Codepoint = 'x' });
            Assert.Equal("保留内容", editor.Text);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Activation_DoesNotLeaveAnotherWindowsFocusWhenSavedTargetIsCssHidden(bool hiddenAncestor)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var saved = new TextBox(); var parent = new StackPanel(); parent.Children.Add(saved);
        var window = new DisplayedTestWindow { Content = parent }; var otherText = new TextBox(); var other = new DisplayedTestWindow { Content = otherText };
        try
        {
            Assert.True(saved.Focus()); Dispatch(window, new() { Type = PlatformEventType.Activate });
            Dispatch(window, new() { Type = PlatformEventType.Deactivate });
            Css.SetStyle(hiddenAncestor ? parent : saved, "visibility: hidden");
            Assert.False(saved.IsVisible); Assert.True(otherText.Focus());
            Dispatch(window, new() { Type = PlatformEventType.Activate });
            Assert.Null(Keyboard.FocusedElement);
            Assert.Equal(Visibility.Visible, saved.Visibility);
        }
        finally { Keyboard.ClearFocus(); other.Close(); window.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Activation_RestoresCssVisibleDescendantWithinHiddenAncestor(bool cssHidden)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var saved = new TextBox(); var parent = new StackPanel(); parent.Children.Add(saved);
        var window = new DisplayedTestWindow { Content = parent }; var otherText = new TextBox(); var other = new DisplayedTestWindow { Content = otherText };
        try
        {
            if (cssHidden) { Css.SetStyle(parent, "visibility: hidden"); Css.SetStyle(saved, "visibility: visible"); }
            Assert.True(saved.IsVisible); Assert.True(saved.Focus());
            Dispatch(window, new() { Type = PlatformEventType.Activate });
            Dispatch(window, new() { Type = PlatformEventType.Deactivate });
            Assert.True(otherText.Focus()); Dispatch(window, new() { Type = PlatformEventType.Activate });
            Assert.Same(saved, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); other.Close(); window.Close(); }
    }

    [Fact]
    public void Activation_DisabledMacOSWindowDoesNotShowOrActivate()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Window.IsEnabled = false;
        Assert.False(fixture.Window.Activate());
        Assert.Equal(0, fixture.Platform.ShowCalls);
        Assert.Equal(0, fixture.Platform.ActivateCalls);
    }

    [Theory]
    [InlineData(false)] // Key-window callback during Show.
    [InlineData(true)] // Callback during Activate.
    public void Activation_ClosedByCallbackReturnsFalseWithoutUsingTheDestroyedWindow(bool duringActivate)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var keepAlive = new DisplayedTestWindow();
        openWindows.Add((nint)0x1234, fixture.Window);
        openWindows.Add((nint)0x1235, keepAlive);
        if (duringActivate) fixture.Platform.ActivateAction = fixture.Window.Close;
        else fixture.Platform.ShowAction = fixture.Window.Close;
        try
        {
            Assert.False(fixture.Window.Activate());
            Assert.Equal(nint.Zero, fixture.Window.Handle);
            Assert.True(fixture.Platform.Disposed);
            Assert.Equal(duringActivate ? 1 : 0, fixture.Platform.ActivateCalls);
        }
        finally
        {
            openWindows.Remove((nint)0x1234); openWindows.Remove((nint)0x1235);
            keepAlive.Close();
        }
    }

    [Fact]
    public void Activation_HiddenMacOSWindowRestoresManagedVisibility()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Window.Visibility = Visibility.Hidden;
        Assert.True(fixture.Window.Activate());
        Assert.Equal(Visibility.Visible, fixture.Window.Visibility);
        Assert.Equal(1, fixture.Platform.ShowCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Show_ClosedByNativeCallbackDoesNotContinueShowingTheMacOSWindow(bool startMaximized)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        if (startMaximized) fixture.Window.WindowState = WindowState.Maximized;
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var keepAlive = new DisplayedTestWindow();
        openWindows.Add((nint)0x1234, fixture.Window);
        openWindows.Add((nint)0x1235, keepAlive);
        int loaded = 0, contentRendered = 0, shown = 0;
        fixture.Window.Loaded += (_, _) => loaded++;
        fixture.Window.ContentRendered += (_, _) => contentRendered++;
        fixture.Window.Shown += (_, _) => shown++;
        fixture.Platform.ShowAction = fixture.Window.Close;
        try
        {
            fixture.Window.Show();
            Assert.True(fixture.Window.IsClosedForPlatformTermination);
            Assert.False(fixture.Window.IsLoaded);
            Assert.Equal(0, loaded);
            Assert.Equal(0, contentRendered);
            Assert.Equal(0, shown);
        }
        finally
        {
            openWindows.Remove((nint)0x1234); openWindows.Remove((nint)0x1235);
            keepAlive.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Show_HiddenByNativeCallbackPreservesTheHiddenMacOSWindow(bool startMaximized)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        if (startMaximized) fixture.Window.WindowState = WindowState.Maximized;
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        int loaded = 0, shown = 0;
        fixture.Window.Loaded += (_, _) => loaded++;
        fixture.Window.Shown += (_, _) => shown++;
        fixture.Platform.ShowAction = fixture.Window.Hide;
        fixture.Window.Show();
        Assert.Equal(Visibility.Hidden, fixture.Window.Visibility);
        Assert.True((bool)typeof(Window).GetField("_nativeWindowHidden", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Window)!);
        Assert.Equal((nint)0x1234, fixture.Window.Handle);
        Assert.False(fixture.Platform.Disposed);
        Assert.Equal(0, loaded);
        Assert.Equal(0, shown);
    }

    [Fact]
    public void Show_StateRequestDuringNativeShowSupersedesTheOriginalMacOSState()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        fixture.Window.WindowState = WindowState.Maximized;
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Platform.ShowAction = () => fixture.Window.WindowState = WindowState.Minimized;
        fixture.Window.Show();
        Assert.Equal(WindowState.Minimized, fixture.Window.WindowState);
        Assert.NotEmpty(fixture.Platform.StateRequests);
        Assert.Equal(WindowState.Minimized, fixture.Platform.StateRequests[^1]);
    }

    [Fact]
    public void Show_StateRequestDuringStartupPositionIsPreservedOnMacOS()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var fixture = new PlatformFixture();
        fixture.Window.Owner = owner.Window;
        fixture.Window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        fixture.Window.WindowState = WindowState.Maximized;
        SetField(owner.Window, "<Handle>k__BackingField", (nint)0x1235);
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Platform.MoveAction = () => fixture.Window.WindowState = WindowState.Minimized;
        fixture.Platform.ShowAction = fixture.Window.Hide;
        fixture.Window.Show();
        Assert.Equal(WindowState.Minimized, fixture.Window.WindowState);
        Assert.Equal(Visibility.Hidden, fixture.Window.Visibility);
    }

    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Minimized)]
    public void NativeStateChanged_AnApplicationRequestInTheCallbackReachesMacOS(WindowState requested)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Window.StateChanged += (_, _) =>
        {
            if (fixture.Window.WindowState == WindowState.Maximized) fixture.Window.WindowState = requested;
        };
        Dispatch(fixture.Window, new() { Type = PlatformEventType.StateChanged, NewState = (int)WindowState.Maximized });
        Assert.Equal(requested, fixture.Window.WindowState);
        Assert.Contains(requested, fixture.Platform.StateRequests);
    }

    [Theory]
    [InlineData(WindowState.Normal, false)]
    [InlineData(WindowState.Minimized, false)]
    [InlineData(WindowState.Normal, true)]
    [InlineData(WindowState.Minimized, true)]
    public void NativeStateChanged_ARequestAfterPropertyMetadataReachesMacOS(WindowState requested, bool internalListener)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new CallbackPropertyWindow();
        using var fixture = new PlatformFixture(window);
        SetField(window, "<Handle>k__BackingField", (nint)0x1234);
        void Request(DependencyProperty property)
        {
            if (property == Window.WindowStateProperty && window.WindowState == WindowState.Maximized)
                window.WindowState = requested;
        }
        if (internalListener) window.PropertyChangedInternal += (property, _, _) => Request(property);
        else window.PropertyChangedAction = e => Request(e.Property);

        Dispatch(window, new() { Type = PlatformEventType.StateChanged, NewState = (int)WindowState.Maximized });
        Assert.Equal(requested, window.WindowState);
        Assert.Contains(requested, fixture.Platform.StateRequests);
    }

    [Fact]
    public void LateImeEvents_DoNotAffectAnotherMacOSWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var window = new DisplayedTestWindow();
        var text = new TextBox();
        var other = new DisplayedTestWindow { Content = text };
        try
        {
            Assert.True(text.Focus());
            InputMethod.StartComposition();
            InputMethod.UpdateComposition("pin", 3);
            Dispatch(window, new() { Type = PlatformEventType.CompositionUpdate, CompositionText = "late" });
            Assert.Equal("pin", InputMethod.CompositionString);
            Dispatch(window, new() { Type = PlatformEventType.CompositionEnd, CompositionText = "late" });
            Assert.True(InputMethod.IsComposing);
            Assert.Empty(text.Text);
        }
        finally { InputMethod.CancelComposition(); Keyboard.ClearFocus(); other.Close(); window.Close(); }
    }

    [Fact]
    public void OwnerClose_ClosesMacOSOwnedWindowsWithoutAnotherClosingEvent()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var owner = new DisplayedTestWindow();
        var child = new DisplayedTestWindow { Owner = owner };
        int childClosing = 0, childClosed = 0;
        child.Closing += (_, e) => { childClosing++; e.Cancel = true; };
        child.Closed += (_, _) => childClosed++;
        owner.Close();
        Assert.Equal(0, childClosing);
        Assert.Equal(1, childClosed);
        Assert.Null(child.Owner);
        Assert.Empty(owner.OwnedWindows.Cast<Window>());
    }

    private static Application ApplicationForWindows(params Window[] windows)
    {
        // AppKit initialization belongs to the native suite on its main thread.
        // This shell supplies only the live collection and shutdown policy used
        // by the managed quit coordinator; it never replaces Application.Current.
        var application = (Application)RuntimeHelpers.GetUninitializedObject(typeof(Application));
        typeof(Application).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(application, new WindowCollection(() =>
                windows.Where(window => !window.IsClosedForPlatformTermination).ToArray()));
        application.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return application;
    }

    [Fact]
    public void AppKitQuit_StopsAtCancelledWindowAndRestoresShutdownPolicy()
    {
        var first = new DisplayedTestWindow();
        var other = new DisplayedTestWindow();
        EventHandler<System.ComponentModel.CancelEventArgs> cancel = (_, e) => e.Cancel = true;
        first.Closing += cancel;
        var application = ApplicationForWindows(first, other);
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        try
        {
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Begin());
            Assert.False(first.IsCloseRequestedForPlatformTermination);
            Assert.False(other.IsCloseRequestedForPlatformTermination);
            Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
        }
        finally { first.Closing -= cancel; other.Close(); first.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppKitQuit_AnOwnedWindowCanCancelBeforeItsOwnerCloses(bool implicitModalOwner)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var owner = new DisplayedTestWindow();
        var child = new DisplayedTestWindow();
        if (implicitModalOwner) SetField(child, "_modalOwner", owner);
        else child.Owner = owner;
        int childClosing = 0;
        EventHandler<System.ComponentModel.CancelEventArgs> cancel = (_, e) => { childClosing++; e.Cancel = true; };
        child.Closing += cancel;
        var application = ApplicationForWindows(owner, child);
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        try
        {
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Begin());
            Assert.Equal(1, childClosing);
            Assert.False(owner.IsCloseRequestedForPlatformTermination);
            Assert.False(child.IsCloseRequestedForPlatformTermination);
            Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
        }
        finally
        {
            child.Closing -= cancel;
            SetField(child, "_modalOwner", null);
            child.Close(); owner.Close();
        }
    }

    [Fact]
    public void AppKitQuit_NestedOwnedWindowsCloseBeforeTheirOwners()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var owner = new DisplayedTestWindow();
        var child = new DisplayedTestWindow { Owner = owner };
        var grandchild = new DisplayedTestWindow { Owner = child };
        var closing = new List<Window>();
        foreach (var window in new[] { owner, child, grandchild })
            window.Closing += (_, _) => closing.Add(window);
        var application = ApplicationForWindows(owner, child, grandchild);
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.Equal([grandchild, child, owner], closing);
        Assert.True(owner.IsClosedForPlatformTermination);
        Assert.True(child.IsClosedForPlatformTermination);
        Assert.True(grandchild.IsClosedForPlatformTermination);
    }

    [Fact]
    public void AppKitQuit_WaitsForDeferredRenderTeardownAndRepliesOnce()
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationForWindows(window);
        var replies = new List<bool>();
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        SetField(window, "_renderState", 1 << 1);
        try
        {
            Assert.Equal(MacOSWindowCloseResult.Pending, request.Begin());
            Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
            Assert.Empty(replies);
            SetField(window, "_renderState", 0);
            var finish = typeof(Window).GetMethod("CompletePendingManagedTeardown", BindingFlags.Instance | BindingFlags.NonPublic)!;
            finish.Invoke(window, null);
            finish.Invoke(window, null);
            Assert.Equal([true], replies);
            Assert.Equal(MacOSWindowCloseResult.Complete, request.Result);
            Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
        }
        finally { SetField(window, "_renderState", 0); window.Close(); }
    }

    [Fact]
    public void AppKitQuit_CompletesAfterEveryWindowReleasesResources()
    {
        var first = new DisplayedTestWindow();
        var second = new DisplayedTestWindow();
        var application = ApplicationForWindows(first, second);
        first.Closing += (_, _) => Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.True(first.IsClosedForPlatformTermination);
        Assert.True(second.IsClosedForPlatformTermination);
        Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
    }

    [Fact]
    public void AppKitQuit_CancelsWhenClosingCallbackThrows()
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationForWindows(window);
        EventHandler<System.ComponentModel.CancelEventArgs> fail = (_, _) => throw new InvalidOperationException("Closing failed");
        window.Closing += fail;
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        try
        {
            Assert.Equal(MacOSWindowCloseResult.Cancelled, request.Begin());
            Assert.False(window.IsCloseRequestedForPlatformTermination);
            Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
        }
        finally { window.Closing -= fail; window.Close(); }
    }

    [Fact]
    public void AppKitQuit_ClosedCallbackFailureDoesNotUndoCompletedTeardown()
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationForWindows(window);
        window.Closed += (_, _) => throw new InvalidOperationException("Closed failed");
        using var request = new MacOSWindowCloseRequest(application, _ => Assert.Fail("Unexpected deferred reply"));
        Assert.Equal(MacOSWindowCloseResult.Complete, request.Begin());
        Assert.True(window.IsClosedForPlatformTermination);
        Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
    }

    [Fact]
    public void AppKitQuit_DeferredClosedCallbackFailureStillCompletesReply()
    {
        var window = new DisplayedTestWindow();
        var application = ApplicationForWindows(window);
        var replies = new List<bool>();
        window.Closed += (_, _) => throw new InvalidOperationException("Closed failed");
        using var request = new MacOSWindowCloseRequest(application, replies.Add);
        SetField(window, "_renderState", 1 << 1);
        try
        {
            Assert.Equal(MacOSWindowCloseResult.Pending, request.Begin());
            SetField(window, "_renderState", 0);
            var finish = typeof(Window).GetMethod("CompletePendingManagedTeardown", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var failure = Assert.Throws<TargetInvocationException>(() => finish.Invoke(window, null));
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Equal([true], replies);
            Assert.Equal(MacOSWindowCloseResult.Complete, request.Result);
        }
        finally { SetField(window, "_renderState", 0); window.Close(); }
    }

    [Fact]
    public void OwnerClose_ContinuesAfterAnOwnedMacOSClosedCallbackFails()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var owner = new DisplayedTestWindow();
        var child = new DisplayedTestWindow { Owner = owner };
        child.Closed += (_, _) => throw new InvalidOperationException("Owned Closed failed");
        owner.Close();
        Assert.True(owner.IsClosedForPlatformTermination);
        Assert.True(child.IsClosedForPlatformTermination);
        Assert.Null(child.Owner);
    }

    [Fact]
    public void ActivationAndFocusNotifications_RaiseActivationOnlyOnce()
    {
        var window = new DisplayedTestWindow();
        int activated = 0, deactivated = 0;
        window.Activated += (_, _) => activated++;
        window.Deactivated += (_, _) => deactivated++;
        Dispatch(window, new() { Type = PlatformEventType.Activate });
        Dispatch(window, new() { Type = PlatformEventType.FocusGained });
        Assert.True(window.IsActive);
        Assert.Equal(1, activated);
        Dispatch(window, new() { Type = PlatformEventType.Deactivate });
        Dispatch(window, new() { Type = PlatformEventType.FocusLost });
        Assert.False(window.IsActive);
        Assert.Equal(1, deactivated);
        window.Close();
    }

    [Fact]
    public void Deactivation_ReleasesPointerCapture()
    {
        UIElement.ForceReleaseMouseCapture();
        var window = new DisplayedTestWindow();
        var element = new Border();
        Assert.True(element.CaptureMouse());
        Dispatch(window, new() { Type = PlatformEventType.Deactivate });
        Assert.Null(Mouse.Captured);
        window.Close();
    }

    [Fact]
    public void FirstResponderChanges_DoNotDeactivateTheMacOSWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new DisplayedTestWindow();
        Dispatch(window, new() { Type = PlatformEventType.Activate });
        Dispatch(window, new() { Type = PlatformEventType.FocusLost });
        Assert.True(window.IsActive);
        Dispatch(window, new() { Type = PlatformEventType.Deactivate });
        Assert.False(window.IsActive);
        window.Close();
    }

    [Fact]
    public void InactiveFirstResponder_DoesNotStealAnotherMacOSWindowFocus()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        var window = new DisplayedTestWindow();
        var text = new TextBox();
        var other = new DisplayedTestWindow { Content = text };
        try
        {
            Assert.True(text.Focus());
            Dispatch(window, new() { Type = PlatformEventType.FocusGained });
            Assert.Same(text, Keyboard.FocusedElement);
            Assert.False(window.IsActive);
        }
        finally { Keyboard.ClearFocus(); other.Close(); window.Close(); }
    }

    [Fact]
    public void Owner_RejectsSelfAndCyclesWithoutChangingTheRelationship()
    {
        var owner = new DisplayedTestWindow();
        var child = new DisplayedTestWindow { Owner = owner };
        var grandchild = new DisplayedTestWindow { Owner = child };
        Assert.Throws<ArgumentException>(() => owner.Owner = owner);
        Assert.Throws<ArgumentException>(() => owner.Owner = grandchild);
        Assert.Null(owner.Owner);
        Assert.Same(owner, child.Owner);
        Assert.Single(owner.OwnedWindows);
        grandchild.Close(); child.Close(); owner.Close();
    }

    [Fact]
    public void MacOSDialog_InfersTheActiveWindowBeforeTheMainWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var main = new PlatformFixture();
        using var active = new PlatformFixture();
        var windows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        SetField(main.Window, "<Handle>k__BackingField", (nint)0x2201);
        SetField(active.Window, "<Handle>k__BackingField", (nint)0x2202);
        SetField(active.Window, "_nativeWindowHidden", false);
        typeof(Window).GetMethod("SetIsActive", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(active.Window, [true]);
        var application = ApplicationForWindows(main.Window, active.Window);
        typeof(Application).GetField("_mainWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(application, main.Window);
        var currentField = typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousApplication = currentField.GetValue(null);
        windows.Add((nint)0x2201, main.Window);
        windows.Add((nint)0x2202, active.Window);
        try
        {
            currentField.SetValue(null, application);
            Assert.Same(active.Window, DialogOwnerResolver.ResolveWindow());
            Assert.Same(main.Window, DialogOwnerResolver.ResolveWindow(main.Window));
            active.Window.IsEnabled = false;
            Assert.Same(main.Window, DialogOwnerResolver.ResolveWindow());
        }
        finally
        {
            currentField.SetValue(null, previousApplication);
            windows.Remove((nint)0x2201); windows.Remove((nint)0x2202);
        }
    }

    [Fact]
    public void Show_RebindsTheCurrentMacOSDialogOwner()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var fixture = new PlatformFixture();
        SetField(owner.Window, "<Handle>k__BackingField", (nint)0x2201);
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x2202);
        SetField(fixture.Window, "_modalOwner", owner.Window);
        SetField(fixture.Window, "_isModal", true);
        fixture.Platform.OwnerHandle = 0; // AppKit detached the previous owner while hiding.
        fixture.Platform.ShowAction = fixture.Window.Hide;
        fixture.Window.Show();
        Assert.Equal(owner.Window.Handle, fixture.Platform.OwnerHandle);
        Assert.Equal(Visibility.Hidden, fixture.Window.Visibility);
        SetField(fixture.Window, "_modalOwner", null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MacOSOwnedWindow_VisibilityAndActivationRebindOwner(bool activate)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var fixture = new PlatformFixture();
        SetField(owner.Window, "<Handle>k__BackingField", (nint)0x2201);
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x2202);
        fixture.Window.Owner = owner.Window;
        fixture.Window.Hide();
        fixture.Platform.OwnerHandle = 0; // AppKit detached the child while hiding.
        if (activate) Assert.True(fixture.Window.Activate());
        else fixture.Window.Visibility = Visibility.Visible;
        Assert.Equal(owner.Window.Handle, fixture.Platform.OwnerHandle);
        Assert.Equal(Visibility.Visible, fixture.Window.Visibility);
        Assert.Same(owner.Window, fixture.Window.Owner);
    }

    [Fact]
    public void MacOSDialog_AnExplicitOwnerChangeBeforeShowingSupersedesTheInferredOwner()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var inferred = new PlatformFixture();
        using var explicitOwner = new PlatformFixture();
        using var fixture = new PlatformFixture();
        SetField(inferred.Window, "<Handle>k__BackingField", (nint)0x2201);
        SetField(explicitOwner.Window, "<Handle>k__BackingField", (nint)0x2202);
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x2203);
        SetField(fixture.Window, "_modalOwner", inferred.Window);
        SetField(fixture.Window, "_isModal", true);
        fixture.Window.Owner = explicitOwner.Window;
        Assert.Same(explicitOwner.Window, fixture.Window.OwnerForPlatformTermination);
        fixture.Platform.ShowAction = fixture.Window.Hide;
        fixture.Window.Show();
        Assert.Equal(explicitOwner.Window.Handle, fixture.Platform.OwnerHandle);
        SetField(fixture.Window, "_modalOwner", null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owner_ChangingAVisibleMacOSDialogIsRejected(bool clearOwner)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var other = new PlatformFixture();
        using var fixture = new PlatformFixture();
        fixture.Window.Owner = owner.Window;
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x2202);
        SetField(fixture.Window, "_isModal", true);
        SetField(fixture.Window, "_nativeWindowHidden", false);
        Assert.Throws<InvalidOperationException>(() => fixture.Window.Owner = clearOwner ? null : other.Window);
        Assert.Same(owner.Window, fixture.Window.Owner);
        Assert.Contains(fixture.Window, owner.Window.OwnedWindows.Cast<Window>());
        Assert.Empty(other.Window.OwnedWindows.Cast<Window>());
    }

    [Theory]
    [InlineData(1, 1, 5)]
    [InlineData(160, 1, 1)]
    [InlineData(319, 1, 9)]
    [InlineData(1, 120, 4)]
    [InlineData(319, 120, 8)]
    [InlineData(1, 239, 6)]
    [InlineData(160, 239, 2)]
    [InlineData(319, 239, 10)]
    [InlineData(160, 120, 0)]
    [InlineData(-1, 120, 0)]
    [InlineData(320, 120, 0)]
    public void BorderlessResize_UsesAllEightNativeEdges(double x, double y, int edge)
    {
        Assert.Equal(edge, Window.GetMacOSResizeEdge(new Point(x, y), new Size(320, 240)));
    }

    [Theory]
    [InlineData(ResizeMode.CanMinimize)]
    [InlineData(ResizeMode.CanResize)]
    public void CaptionDoubleClick_UsesTheNativePolicyOnRelease(ResizeMode resizeMode)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        fixture.Window.TitleBarStyle = WindowTitleBarStyle.Custom;
        fixture.Window.ResizeMode = resizeMode;
        fixture.Platform.TitleBarActionState = WindowState.Minimized;
        Dispatch(fixture.Window, new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 32, ClickCount = 2 });
        Assert.Equal(0, fixture.Platform.TitleBarDoubleClickCalls);
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        Dispatch(fixture.Window, new() { Type = PlatformEventType.MouseUp, Button = 0, MouseX = 200, MouseY = 32 });
        Assert.Equal(1, fixture.Platform.TitleBarDoubleClickCalls);
        Assert.Equal(WindowState.Minimized, fixture.Window.WindowState);
    }

    [Fact]
    public void CaptionDoubleClick_NoActionDoesNotForceMaximization()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        fixture.Window.TitleBarStyle = WindowTitleBarStyle.Custom;
        Dispatch(fixture.Window, new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 32, ClickCount = 2 });
        Dispatch(fixture.Window, new() { Type = PlatformEventType.MouseUp, Button = 0, MouseX = 200, MouseY = 32 });
        Assert.Equal(1, fixture.Platform.TitleBarDoubleClickCalls);
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
    }

    [Theory]
    [InlineData(0)] // Release in content rather than the caption.
    [InlineData(1)] // Window deactivated while the second press was held.
    [InlineData(2)] // Window disabled while the second press was held.
    [InlineData(3)] // Release outside the window.
    public void CaptionDoubleClick_CancelsWhenTheReleaseIsNoLongerValid(int reason)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        fixture.Window.TitleBarStyle = WindowTitleBarStyle.Custom;
        Dispatch(fixture.Window, new() { Type = PlatformEventType.MouseDown, Button = 0, MouseX = 200, MouseY = 32, ClickCount = 2 });
        if (reason == 1) Dispatch(fixture.Window, new() { Type = PlatformEventType.Deactivate });
        if (reason == 2) fixture.Window.IsEnabled = false;
        Dispatch(fixture.Window, new() { Type = PlatformEventType.MouseUp, Button = 0,
            MouseX = reason == 3 ? -20 : 200, MouseY = reason == 0 ? 160 : 32 });
        Assert.Equal(0, fixture.Platform.TitleBarDoubleClickCalls);
        Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
    }

    [Fact]
    public void DialogResult_ClosesTheModalWindow()
    {
        var window = new DisplayedTestWindow();
        SetField(window, "_isModal", true);
        int closed = 0;
        window.Closed += (_, _) => closed++;
        window.DialogResult = true;
        Assert.Equal(1, closed);
        Assert.True(window.DialogResult);
    }

    [Fact]
    public void CancelledDialogResultClose_ClearsResultAndKeepsModalLoop()
    {
        var window = new DisplayedTestWindow();
        SetField(window, "_isModal", true);
        EventHandler<System.ComponentModel.CancelEventArgs> cancel = (_, e) => e.Cancel = true;
        window.Closing += cancel;
        window.DialogResult = false;
        Assert.Null(window.DialogResult);
        Assert.True(window.IsModalForCss);
        SetField(window, "_isModal", false);
        window.Closing -= cancel;
        window.Close();
    }

    [Theory]
    [InlineData(0)] // Hide()
    [InlineData(1)] // Visibility.Hidden
    [InlineData(2)] // Visibility.Collapsed
    public void HiddenMacOSDialog_EndsModalSessionWithoutClosing(int hideKind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        SetField(fixture.Window, "_isModal", true);
        int closing = 0, closed = 0;
        fixture.Window.Closing += (_, _) => closing++;
        fixture.Window.Closed += (_, _) => closed++;

        if (hideKind == 0) fixture.Window.Hide();
        else fixture.Window.Visibility = hideKind == 1 ? Visibility.Hidden : Visibility.Collapsed;

        Assert.False(fixture.Window.IsModalForCss);
        Assert.False(fixture.Window.DialogResult);
        Assert.Equal(hideKind == 2 ? Visibility.Collapsed : Visibility.Hidden, fixture.Window.Visibility);
        Assert.Equal((nint)0x1234, fixture.Window.Handle);
        Assert.False(fixture.Platform.Disposed);
        Assert.Equal(1, fixture.Platform.HideCalls);
        Assert.Equal(0, closing);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void MacOSDialog_CloseWithoutAResultReturnsFalse()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new DisplayedTestWindow();
        SetField(window, "_isModal", true);
        window.Close();
        Assert.False(window.DialogResult);
        Assert.False(window.IsModalForCss);
    }

    [Fact]
    public void MacOSDialog_NativeDestructionReturnsFalse()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "_isModal", true);
        fixture.Platform.Raise(new() { Type = PlatformEventType.Destroyed });
        Assert.False(fixture.Window.DialogResult);
        Assert.False(fixture.Window.IsModalForCss);
    }

    [Fact]
    public void MacOSDialog_CancelledClosingMayHideAndEndTheSession()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        SetField(fixture.Window, "_isModal", true);
        fixture.Window.Closing += (_, e) => { e.Cancel = true; fixture.Window.Hide(); };
        fixture.Window.Close();
        Assert.False(fixture.Window.DialogResult);
        Assert.False(fixture.Window.IsModalForCss);
        Assert.False(fixture.Window.IsCloseRequestedForPlatformTermination);
        Assert.Equal((nint)0x1234, fixture.Window.Handle);
        Assert.False(fixture.Platform.Disposed);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void MacOSDialog_HideDuringShowReturnsAndPreservesOwnerEnabledState(int hideKind, bool ownerEnabled)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        owner.Window.IsEnabled = ownerEnabled;
        var dialog = new CallbackShowWindow();
        using var fixture = new PlatformFixture(dialog);
        dialog.Owner = owner.Window;
        SetField(dialog, "_dispatcher", Dispatcher.CurrentDispatcher);
        dialog.ShowAction = () =>
        {
            Assert.False(owner.Window.IsEnabled);
            Assert.True(dialog.IsModalForCss);
            Assert.Null(dialog.DialogResult);
            SetField(dialog, "<Handle>k__BackingField", (nint)0x1234);
            if (hideKind == 0) dialog.Hide();
            else dialog.Visibility = hideKind == 1 ? Visibility.Hidden : Visibility.Collapsed;
        };

        Assert.False(dialog.ShowDialog());
        Assert.Equal(ownerEnabled, owner.Window.IsEnabled);
        Assert.False(dialog.IsModalForCss);
        Assert.Equal((nint)0x1234, dialog.Handle);
        Assert.False(fixture.Platform.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MacOSDialog_DisablesOtherApplicationWindowsAndRestoresTheirPriorState(bool failDuringShow)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var other = new PlatformFixture();
        using var alreadyDisabled = new PlatformFixture();
        alreadyDisabled.Window.IsEnabled = false;
        var dialog = new CallbackShowWindow { Owner = owner.Window };
        using var fixture = new PlatformFixture(dialog);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        openWindows.Add((nint)0x2201, owner.Window);
        openWindows.Add((nint)0x2202, other.Window);
        openWindows.Add((nint)0x2203, alreadyDisabled.Window);
        dialog.ShowAction = () =>
        {
            Assert.False(owner.Window.IsEnabled);
            Assert.False(other.Window.IsEnabled);
            Assert.False(alreadyDisabled.Window.IsEnabled);
            if (failDuringShow) throw new InvalidOperationException("Dialog Show failed");
            SetField(dialog, "<Handle>k__BackingField", (nint)0x2204);
            dialog.Hide();
        };
        try
        {
            if (failDuringShow) Assert.Throws<InvalidOperationException>(() => dialog.ShowDialog());
            else Assert.False(dialog.ShowDialog());
            Assert.True(owner.Window.IsEnabled);
            Assert.True(other.Window.IsEnabled);
            Assert.False(alreadyDisabled.Window.IsEnabled);
            Assert.False(dialog.IsModalForCss);
        }
        finally
        {
            openWindows.Remove((nint)0x2201); openWindows.Remove((nint)0x2202); openWindows.Remove((nint)0x2203);
        }
    }

    [Fact]
    public void MacOSDialog_NestedModalRestoresOnlyThePreviousDialog()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var other = new PlatformFixture();
        var outer = new CallbackShowWindow { Owner = owner.Window };
        var inner = new CallbackShowWindow { Owner = outer };
        using var outerFixture = new PlatformFixture(outer);
        using var innerFixture = new PlatformFixture(inner);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        openWindows.Add((nint)0x2201, owner.Window);
        openWindows.Add((nint)0x2202, other.Window);
        outer.ShowAction = () =>
        {
            SetField(outer, "<Handle>k__BackingField", (nint)0x2204);
            openWindows.Add((nint)0x2204, outer);
            inner.ShowAction = () =>
            {
                Assert.False(outer.IsEnabled);
                Assert.False(owner.Window.IsEnabled);
                Assert.False(other.Window.IsEnabled);
                SetField(inner, "<Handle>k__BackingField", (nint)0x2205);
                inner.Hide();
            };
            Assert.False(inner.ShowDialog());
            Assert.True(outer.IsEnabled);
            Assert.False(owner.Window.IsEnabled);
            Assert.False(other.Window.IsEnabled);
            outer.Hide();
        };
        try
        {
            Assert.False(outer.ShowDialog());
            Assert.True(owner.Window.IsEnabled);
            Assert.True(other.Window.IsEnabled);
        }
        finally
        {
            openWindows.Remove((nint)0x2201); openWindows.Remove((nint)0x2202); openWindows.Remove((nint)0x2204);
        }
    }

    [Fact]
    public void MacOSDialog_RestoresThePreviouslyActiveWindowWhenItIsNotTheOwner()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var previouslyActive = new PlatformFixture();
        SetField(owner.Window, "<Handle>k__BackingField", (nint)0x2201);
        SetField(previouslyActive.Window, "<Handle>k__BackingField", (nint)0x2202);
        var dialog = new CallbackShowWindow { Owner = owner.Window };
        using var fixture = new PlatformFixture(dialog);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        openWindows.Add((nint)0x2201, owner.Window);
        openWindows.Add((nint)0x2202, previouslyActive.Window);
        Dispatch(previouslyActive.Window, new() { Type = PlatformEventType.Activate });
        dialog.ShowAction = () =>
        {
            SetField(dialog, "<Handle>k__BackingField", (nint)0x2204);
            dialog.Hide();
        };
        try
        {
            Assert.False(dialog.ShowDialog());
            Assert.Equal(1, previouslyActive.Platform.ActivateCalls);
            Assert.Equal(0, owner.Platform.ActivateCalls);
        }
        finally
        {
            openWindows.Remove((nint)0x2201); openWindows.Remove((nint)0x2202);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MacOSDialog_EnabledCallbackFailureStillRestoresEveryWindow(bool failWhenEnabling)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var other = new PlatformFixture();
        var dialog = new CallbackShowWindow { Owner = owner.Window };
        using var fixture = new PlatformFixture(dialog);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        openWindows.Add((nint)0x2201, owner.Window);
        openWindows.Add((nint)0x2202, other.Window);
        DependencyPropertyChangedEventHandler fail = (_, e) =>
        {
            if ((bool)e.NewValue! == failWhenEnabling) throw new InvalidOperationException("Enabled callback failed");
        };
        owner.Window.IsEnabledChanged += fail;
        dialog.ShowAction = () =>
        {
            SetField(dialog, "<Handle>k__BackingField", (nint)0x2204);
            dialog.Hide();
        };
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => dialog.ShowDialog());
            Assert.Equal("Enabled callback failed", exception.Message);
            Assert.True(owner.Window.IsEnabled);
            Assert.True(other.Window.IsEnabled);
            Assert.False(dialog.IsModalForCss);
            Assert.Same(owner.Window, dialog.Owner);
            Assert.Null(typeof(Window).GetField("_modalOwner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog));
        }
        finally
        {
            owner.Window.IsEnabledChanged -= fail;
            openWindows.Remove((nint)0x2201); openWindows.Remove((nint)0x2202);
        }
    }

    [Fact]
    public void MacOSDialog_DoesNotReenableAWindowClosedDuringTheModalSession()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new PlatformFixture();
        using var other = new PlatformFixture();
        SetField(other.Window, "<Handle>k__BackingField", (nint)0x2202);
        var dialog = new CallbackShowWindow { Owner = owner.Window };
        using var fixture = new PlatformFixture(dialog);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        openWindows.Add((nint)0x2201, owner.Window);
        openWindows.Add((nint)0x2202, other.Window);
        dialog.ShowAction = () =>
        {
            other.Window.Close();
            SetField(dialog, "<Handle>k__BackingField", (nint)0x2204);
            dialog.Hide();
        };
        try
        {
            Assert.False(dialog.ShowDialog());
            Assert.True(other.Window.IsClosedForPlatformTermination);
            Assert.False(other.Window.IsEnabled);
            Assert.Equal(0, other.Platform.ActivateCalls);
            Assert.True(owner.Window.IsEnabled);
        }
        finally
        {
            openWindows.Remove((nint)0x2201); openWindows.Remove((nint)0x2202);
        }
    }

    [Fact]
    public void MacOSDialog_OwnerCloseReturnsFalseWithoutAnotherClosingRequest()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var owner = new DisplayedTestWindow();
        var dialog = new DisplayedTestWindow { Owner = owner };
        SetField(dialog, "_isModal", true);
        int closing = 0;
        dialog.Closing += (_, _) => closing++;
        owner.Close();
        Assert.False(dialog.DialogResult);
        Assert.False(dialog.IsModalForCss);
        Assert.True(dialog.IsClosedForPlatformTermination);
        Assert.Equal(0, closing);
    }

    [Theory]
    [InlineData(0)] // Already modal.
    [InlineData(1)] // Already visible.
    [InlineData(2)] // Already closed.
    public void MacOSDialog_RejectsInvalidEntryBeforeChangingCurrentState(int state)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "_dialogResult", true);
        if (state == 0) SetField(fixture.Window, "_isModal", true);
        else if (state == 1)
        {
            SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
            SetField(fixture.Window, "_nativeWindowHidden", false);
        }
        else fixture.Window.Close();
        Assert.Throws<InvalidOperationException>(() => fixture.Window.ShowDialog());
        Assert.True(fixture.Window.DialogResult);
        Assert.Equal(state == 0, fixture.Window.IsModalForCss);
    }

    [Fact]
    public void ProgrammaticSize_SynchronizesNativeClientSizeWithoutFeedback()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        fixture.Window.Width = 400;
        fixture.Window.Height = 300;
        Assert.Equal(800, fixture.Platform.Width);
        Assert.Equal(600, fixture.Platform.Height);
        Assert.Equal(2, fixture.Platform.ResizeCalls);
        fixture.Platform.Raise(new() { Type = PlatformEventType.Resize, Width = 900, Height = 700 });
        Assert.Equal(450, fixture.Window.Width);
        Assert.Equal(350, fixture.Window.Height);
        Assert.Equal(2, fixture.Platform.ResizeCalls);
    }

    [Fact]
    public void NativeUserResize_DisablesSizeToContent()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        fixture.Window.SizeToContent = SizeToContent.WidthAndHeight;
        fixture.Platform.Raise(new() { Type = PlatformEventType.Resize, Width = 900, Height = 700, IsUserInitiatedResize = true });
        Assert.Equal(SizeToContent.Manual, fixture.Window.SizeToContent);
    }

    [Fact]
    public void SizeToContent_UsesNaturalContentSizeAndUpdatesNativeWindow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        fixture.Window.Content = new Border { Width = 180, Height = 90 };
        fixture.Window.SizeToContent = SizeToContent.WidthAndHeight;
        typeof(Window).GetMethod("UpdateMacOSSizeToContent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Window, null);
        Assert.Equal(180, fixture.Window.Width);
        Assert.Equal(90, fixture.Window.Height);
        Assert.Equal(360, fixture.Platform.Width);
        Assert.Equal(180, fixture.Platform.Height);
        Assert.Equal(1, fixture.Platform.ResizeCalls);
    }

    [Fact]
    public void RestoreBounds_NormalMacOSWindowUsesLatestNativeGeometry()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        Assert.Equal(Rect.Empty, fixture.Window.RestoreBounds);
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        Assert.Equal(new Rect(60, 80, 320, 240), fixture.Window.RestoreBounds);
        fixture.Platform.Width = 900;
        fixture.Platform.Height = 700;
        Assert.Equal(new Rect(60, 80, 450, 350), fixture.Window.RestoreBounds);
        fixture.Platform.RestoreBounds = (240, 320, 1100, 800);
        Assert.Equal(new Rect(120, 160, 550, 400), fixture.Window.RestoreBounds);
    }

    [Fact]
    public void RestoreBounds_ClosedMacOSWindowReturnsEmpty()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var keepAlive = new DisplayedTestWindow();
        openWindows.Add((nint)0x1234, fixture.Window);
        openWindows.Add((nint)0x1235, keepAlive);
        Rect boundsAtClosing = Rect.Empty;
        fixture.Window.Closing += (_, _) => boundsAtClosing = fixture.Window.RestoreBounds;
        try
        {
            Assert.False(fixture.Window.RestoreBounds.IsEmpty);
            fixture.Window.Close();
            Assert.Equal(new Rect(60, 80, 320, 240), boundsAtClosing);
            Assert.Equal(nint.Zero, fixture.Window.Handle);
            Assert.Equal(Rect.Empty, fixture.Window.RestoreBounds);
        }
        finally
        {
            openWindows.Remove((nint)0x1234); openWindows.Remove((nint)0x1235);
            keepAlive.Close();
        }
    }

    [Fact]
    public void Maximize_CapturesFiniteNativeRestoreBounds()
    {
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Window.WindowState = WindowState.Maximized;
        Assert.Equal(new Rect(60, 80, 320, 240), fixture.Window.RestoreBounds);
    }

    [Fact]
    public void NativeFullScreen_UsesNormalBoundsSavedBeforeAppKitResized()
    {
        using var fixture = new PlatformFixture();
        fixture.Platform.RestoreBounds = (120, 160, 640, 480);
        fixture.Platform.Width = 2000; fixture.Platform.Height = 1000;
        fixture.Platform.Raise(new() { Type = PlatformEventType.Resize, Width = 2000, Height = 1000 });
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Platform.Raise(new() { Type = PlatformEventType.StateChanged, NewState = 3 });
        Assert.Equal(WindowState.FullScreen, fixture.Window.WindowState);
        Assert.Equal(new Rect(60, 80, 320, 240), fixture.Window.RestoreBounds);
    }

    [Fact]
    public void DpiChanged_ReappliesConstraintsAndRaisesRoutedEvent()
    {
        using var fixture = new PlatformFixture();
        fixture.Window.MinWidth = 100;
        fixture.Window.MaxHeight = 500;
        int dpiEvents = 0;
        fixture.Window.DpiChanged += (_, e) =>
        {
            Assert.Equal(2, e.OldDpi.DpiScaleX);
            Assert.Equal(1, e.NewDpi.DpiScaleX);
            dpiEvents++;
        };
        fixture.Platform.Raise(new() { Type = PlatformEventType.DpiChanged, DpiX = 96, DpiY = 96 });
        Assert.Equal(100, fixture.Platform.MinWidth);
        Assert.Equal(500, fixture.Platform.MaxHeight);
        Assert.Equal(1, dpiEvents);
    }

    [Fact]
    public void RuntimeResizeMode_AppliesMinimizeOnlyStyle()
    {
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Window.ResizeMode = ResizeMode.CanMinimize;
        Assert.NotEqual(0u, fixture.Platform.Style & 0x10);
        Assert.Equal(0u, fixture.Platform.Style & (0x02 | 0x20));
    }

    [Fact]
    public void RuntimeTransparency_UpdatesTheMacOSSurfaceStyleInBothDirections()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        fixture.Window.AllowsTransparency = true;
        Assert.NotEqual(0u, fixture.Platform.Style & 0x100);
        fixture.Window.AllowsTransparency = false;
        Assert.Equal(0u, fixture.Platform.Style & 0x100);
    }

    [Theory]
    [InlineData(WindowBackdropType.Auto)]
    [InlineData(WindowBackdropType.Mica)]
    [InlineData(WindowBackdropType.Acrylic)]
    [InlineData(WindowBackdropType.MicaAlt)]
    public void RuntimeSystemBackdrop_AppliesMacOSMaterialAndPreservesBackground(WindowBackdropType backdrop)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        var background = new SolidColorBrush(Color.FromArgb(96, 20, 30, 40));
        fixture.Window.Background = background;
        fixture.Window.SystemBackdrop = backdrop;
        Assert.Equal((int)backdrop, fixture.Platform.Backdrop);
        Assert.Same(background, fixture.Window.Background);
        Assert.False(fixture.Window.AllowsTransparency);
        fixture.Window.SystemBackdrop = WindowBackdropType.None;
        Assert.Equal(0, fixture.Platform.Backdrop);
        Assert.Same(background, fixture.Window.Background);
    }

    [Fact]
    public void ImeContext_FollowsMacOSFocusCaretAndReadOnlyState()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var text = new TextBox { Text = "a中😀", Margin = new Thickness(30, 40, 0, 0) };
        fixture.Window.Content = text;
        fixture.Window.Measure(new Size(320, 240));
        fixture.Window.Arrange(new Rect(0, 0, 320, 240));
        try
        {
            Assert.True(text.Focus());
            Dispatch(fixture.Window, new() { Type = PlatformEventType.Activate });
            text.CaretIndex = 1;
            fixture.Window.UpdateImeCompositionWindow();
            Assert.True(fixture.Platform.ImeContext.Enabled);
            Assert.Equal("a中😀", fixture.Platform.ImeContext.SurroundingText);
            Assert.Equal(1, fixture.Platform.ImeContext.CursorUtf8ByteOffset);
            Assert.True(fixture.Platform.ImeContext.CaretX >= 60);
            Assert.True(fixture.Platform.ImeContext.CaretY >= 80);
            Assert.True(fixture.Platform.ImeContext.CaretHeight > 0);
            text.CaretIndex = 2;
            fixture.Window.RefreshLinuxImeContext();
            Assert.Equal(4, fixture.Platform.ImeContext.CursorUtf8ByteOffset);
            InputMethod.StartComposition();
            Assert.True(InputMethod.IsComposing);
            text.IsReadOnly = true;
            Assert.False(fixture.Platform.ImeContext.Enabled);
            Assert.False(InputMethod.IsComposing);
            text.IsReadOnly = false;
            Assert.True(fixture.Platform.ImeContext.Enabled);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.Deactivate });
            Assert.False(fixture.Platform.ImeContext.Enabled);
        }
        finally { InputMethod.CancelComposition(); Keyboard.ClearFocus(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AppKitReplacement_PreservesGraphemesAndIsOneUndoableEdit(int editorKind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var editor = CreateImeEditor(editorKind, "ab😀e\u0301cd");
        fixture.Window.Content = editor.Element;
        string original = editor.Text();
        try
        {
            Assert.True(editor.Element.Focus());
            Dispatch(fixture.Window, new() { Type = PlatformEventType.Activate });
            var request = new PlatformImeTextRequest(3, 1, "中", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.True(request.Applied);
            Assert.Equal(original[..2] + "中" + original[4..], editor.Text());
            Assert.True(editor.Undo());
            Assert.Equal(original, editor.Text());
        }
        finally { Keyboard.ClearFocus(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MacOSPreEdit_CancelPreservesSelectedCommittedText(int editorKind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var editor = CreateImeEditor(editorKind, editorKind == 4 ? "123456" : "ab😀cd");
        Assert.True(editor.Support.TrySetImeSelection(2, 2));
        string original = editor.Text();
        editor.Support.OnImeCompositionStart();
        editor.Support.OnImeCompositionUpdate("pin", 2);
        Assert.Equal(original, editor.Text());
        editor.Support.OnImeCompositionEnd(null);
        Assert.Equal(original, editor.Text());
        Assert.True(editor.Support.TryGetImeSurroundingText(out var snapshot));
        Assert.Equal(2, Math.Min(snapshot.CursorIndex, snapshot.AnchorIndex));
        Assert.Equal(4, Math.Max(snapshot.CursorIndex, snapshot.AnchorIndex));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MacOSPreEdit_CandidateRectangleFollowsTheCompositionCursor(int editorKind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var editor = CreateImeEditor(editorKind, "123456");
        editor.Element.Measure(new Size(320, 180));
        editor.Element.Arrange(new Rect(0, 0, 320, 180));
        Assert.True(editor.Support.TrySetImeSelection(2, 3));
        editor.Support.OnImeCompositionStart();
        editor.Support.OnImeCompositionUpdate("pin", 0);
        Rect start = editor.Support.GetImeCaretRectangle();
        editor.Support.OnImeCompositionUpdate("pin", 2);
        Rect updated = editor.Support.GetImeCaretRectangle();
        Assert.True(updated.X > start.X);
        Assert.Equal(start.Y, updated.Y);
        editor.Support.OnImeCompositionEnd(null);
    }

    [MacOSNativeGeometryFact]
    public void MacOSPrivatePreEdit_CancelPreservesThePasswordAndSelection()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var password = new PasswordBox { Password = "ab😀cd" };
        password.Measure(new Size(320, 180));
        password.Arrange(new Rect(0, 0, 320, 180));
        password.Select(2, 2);
        var support = (IImeSupport)password;
        support.OnImeCompositionStart();
        support.OnImeCompositionUpdate("pin", 0);
        Rect start = support.GetImeCaretRectangle();
        support.OnImeCompositionUpdate("pin", 2);
        Assert.True(support.GetImeCaretRectangle().X > start.X);
        support.OnImeCompositionEnd(null);
        Assert.Equal("ab😀cd", password.Password);
        Assert.Equal(2, password.SelectionStart); Assert.Equal(2, password.SelectionLength);
        Assert.False(support.TryGetImeSurroundingText(out _));
    }

    [Fact]
    public void MacOSPrivateCommit_DoesNotFollowFocusChangedByCompositionEnd()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var password = new PasswordBox { Password = "private" };
        var other = new TextBox { Text = "other" };
        var panel = new StackPanel(); panel.Children.Add(password); panel.Children.Add(other);
        fixture.Window.Content = panel;
        EventHandler<CompositionResultEventArgs> handler = (_, args) => { if (args.Result != null) other.Focus(); };
        InputMethod.CompositionEnded += handler;
        try
        {
            Assert.True(password.Focus());
            Dispatch(fixture.Window, new() { Type = PlatformEventType.CompositionStart });
            Dispatch(fixture.Window, new() { Type = PlatformEventType.CompositionEnd, CompositionText = "中" });
            Assert.Same(other, Keyboard.FocusedElement);
            Assert.Equal("private", password.Password); Assert.Equal("other", other.Text);
            Assert.False(InputMethod.IsComposing);
        }
        finally { InputMethod.CompositionEnded -= handler; InputMethod.CancelComposition(); Keyboard.ClearFocus(); }
    }

    [Fact]
    public void AppKitNumberReplacement_FiltersAgainstTheRetainedTextAndPreservesUndo()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var number = new NumberBox { Text = "-12.34" };
        fixture.Window.Content = number;
        try
        {
            Assert.True(number.Focus());
            var request = new PlatformImeTextRequest(0, number.Text.Length, "-56..78abc", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.True(request.Applied); Assert.Equal("-56.78", number.Text);
            Assert.True(number.Undo()); Assert.Equal("-12.34", number.Text);
            request = new(1, 2, "5.6", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.True(request.Applied); Assert.Equal("-56.34", number.Text);
            Assert.True(((IImeSupport)number).TrySetImeSelection(1, 2));
            request = new(1, 2, "abc", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.True(request.Applied); Assert.Equal("-56.34", number.Text);
            Assert.True(((IImeSupport)number).TryGetImeSurroundingText(out var snapshot));
            Assert.Equal(1, Math.Min(snapshot.CursorIndex, snapshot.AnchorIndex));
            Assert.Equal(3, Math.Max(snapshot.CursorIndex, snapshot.AnchorIndex));
            request = new(1, 2, string.Empty, replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.True(request.Applied); Assert.Equal("-.34", number.Text);
        }
        finally { Keyboard.ClearFocus(); System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public void AppKitReplacement_StillHonorsPreviewTextInput()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var text = new TextBox { Text = "keep" };
        fixture.Window.Content = text;
        int previews = 0;
        text.AddHandler(UIElement.PreviewTextInputEvent, new TextCompositionEventHandler((_, args) =>
        { previews++; args.Handled = true; }));
        try
        {
            Assert.True(text.Focus());
            var request = new PlatformImeTextRequest(0, 4, "replace", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.True(request.Applied); // The preview intentionally consumed the request.
            Assert.Equal(1, previews);
            Assert.Equal("keep", text.Text);
        }
        finally { Keyboard.ClearFocus(); }
    }

    [Fact]
    public void AppKitReplacement_DoesNotFollowFocusChangedByPreview()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var first = new TextBox { Text = "first" };
        var second = new TextBox { Text = "second" };
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second);
        fixture.Window.Content = panel;
        first.AddHandler(UIElement.PreviewTextInputEvent, new TextCompositionEventHandler((_, _) => second.Focus()));
        try
        {
            Assert.True(first.Focus());
            var request = new PlatformImeTextRequest(0, 5, "replace", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.False(request.Applied);
            Assert.Equal("first", first.Text); Assert.Equal("second", second.Text);
        }
        finally { Keyboard.ClearFocus(); }
    }

    [Fact]
    public void AppKitRangeRequests_RejectReadOnlyPrivateAndForeignTargets()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var readOnly = new TextBox { Text = "read only", IsReadOnly = true };
        var password = new PasswordBox { Password = "private" };
        var foreign = new TextBox { Text = "other" };
        var otherWindow = new DisplayedTestWindow { Content = foreign };
        try
        {
            fixture.Window.Content = readOnly; Assert.True(readOnly.Focus());
            var request = new PlatformImeTextRequest(0, 1, "X", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.False(request.Applied); Assert.Equal("read only", readOnly.Text);
            fixture.Window.Content = password; Assert.True(password.Focus());
            request = new(0, 1, "X", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.False(request.Applied); Assert.Equal("private", password.Password);
            Assert.True(foreign.Focus()); request = new(0, 1, "X", replace: true);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeTextRequest, ImeTextRequest = request });
            Assert.False(request.Applied); Assert.Equal("other", foreign.Text);
        }
        finally { Keyboard.ClearFocus(); otherWindow.Close(); }
    }

    [Fact]
    public void MacOSImeContext_KeepsFullDocumentOffsetsBeyond4000Utf8Bytes()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        string content = new string('a', 6000) + "中😀";
        var text = new TextBox { Text = content };
        fixture.Window.Content = text;
        try
        {
            Assert.True(text.Focus());
            text.CaretIndex = 6003;
            Dispatch(fixture.Window, new() { Type = PlatformEventType.Activate });
            fixture.Window.RefreshLinuxImeContext();
            Assert.Equal(content, fixture.Platform.ImeContext.SurroundingText);
            Assert.Equal(6007, fixture.Platform.ImeContext.CursorUtf8ByteOffset);
        }
        finally { Keyboard.ClearFocus(); }
    }

    [Fact]
    public void MacOSFocusSwitch_DiscardsThePreviousEditorsNativeTextContext()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var first = new TextBox { Text = "first" };
        var second = new TextBox { Text = "second" };
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second);
        fixture.Window.Content = panel;
        try
        {
            Assert.True(first.Focus());
            Dispatch(fixture.Window, new() { Type = PlatformEventType.Activate });
            fixture.Window.RefreshLinuxImeContext();
            fixture.Platform.ImeContexts.Clear();
            InputMethod.StartComposition();
            Assert.True(second.Focus());
            fixture.Window.RefreshLinuxImeContext();
            int discarded = fixture.Platform.ImeContexts.FindIndex(context => !context.Enabled);
            int replaced = fixture.Platform.ImeContexts.FindLastIndex(context => context.Enabled && context.SurroundingText == "second");
            Assert.True(discarded >= 0 && replaced > discarded);
            Assert.Equal("second", fixture.Platform.ImeContext.SurroundingText);
            Assert.False(InputMethod.IsComposing);
            Assert.Equal("first", first.Text); Assert.Equal("second", second.Text);
        }
        finally { InputMethod.CancelComposition(); Keyboard.ClearFocus(); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void NativeImeRequest_MarshallingCopiesTextAndAcknowledgesOriginalNativeRequest(bool accept, bool throwFromCallback)
    {
        var native = (NativePlatformWindow)RuntimeHelpers.GetUninitializedObject(typeof(NativePlatformWindow));
        PlatformImeTextRequest? received = null;
        typeof(NativePlatformWindow).GetField("_eventHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(native, (Action<PlatformEvent>)(evt =>
            {
                received = evt.ImeTextRequest;
                if (throwFromCallback) throw new InvalidOperationException("input handler failure");
                received!.Applied = accept;
            }));
        GCHandle context = GCHandle.Alloc(native);
        nint payload = Marshal.AllocHGlobal(72), text = Marshal.StringToCoTaskMemUTF8("中😀"), result = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.Copy(new byte[72], 0, payload, 72);
            Marshal.WriteInt32(payload, 0, 47);
            Marshal.WriteIntPtr(payload, 16, text);
            Marshal.WriteInt32(payload, 24, 5); Marshal.WriteInt32(payload, 28, 3); Marshal.WriteInt32(payload, 32, 1);
            Marshal.WriteIntPtr(payload, 40, result); Marshal.WriteInt32(result, 0);
            typeof(NativePlatformWindow).GetMethod("OnNativeEventStatic", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [payload, GCHandle.ToIntPtr(context)]);
            Assert.NotNull(received); Assert.Equal("中😀", received.Text);
            Assert.Equal(5, received.Start); Assert.Equal(3, received.Length); Assert.True(received.Replace);
            Assert.Equal(accept ? 1 : -1, Marshal.ReadInt32(result));
        }
        finally { context.Free(); Marshal.FreeHGlobal(payload); Marshal.FreeCoTaskMem(text); Marshal.FreeHGlobal(result); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AppKitGeometry_QueriesGraphemesWithoutChangingTextOrSelection(int kind)
    {
        var editor = CreateImeEditor(kind, kind == 4 ? "123456" : "ab😀cd");
        editor.Element.Measure(new Size(400, 180));
        editor.Element.Arrange(new Rect(0, 0, 400, 180));
        Assert.True(editor.Support.TrySetImeSelection(0, 1));
        Assert.True(editor.Support.TryGetImeSurroundingText(out var before));
        Assert.True(editor.Support.TryGetImeTextRangeGeometry(3, 1, false, out var geometry));
        Assert.Equal(kind == 4 ? 3 : 2, geometry.Start);
        Assert.Equal(kind == 4 ? 1 : 2, geometry.Length);
        Assert.True(geometry.Rectangle.Width > 0);
        Assert.True(geometry.Rectangle.Height > 0);
        var point = new Point(geometry.Rectangle.X + geometry.Rectangle.Width / 2,
            geometry.Rectangle.Y + geometry.Rectangle.Height / 2);
        Assert.True(editor.Support.TryGetImeCharacterIndex(point, false, out int index));
        Assert.InRange(index, geometry.Start, geometry.Start + geometry.Length);
        Assert.Equal(index, ImeTextEncoding.SnapToGraphemeBoundary(before.Text, index, false));
        Assert.False(editor.Support.TryGetImeCharacterIndex(new Point(-20, -20), false, out _));
        Assert.True(editor.Support.TryGetImeSurroundingText(out var after));
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AppKitGeometry_MultilineRangesReturnOnlyTheFirstLine(int kind)
    {
        var editor = CreateImeEditor(kind, "ab\ncd");
        editor.Element.Measure(new Size(400, 180));
        editor.Element.Arrange(new Rect(0, 0, 400, 180));
        Assert.True(editor.Support.TryGetImeTextRangeGeometry(0, 5, false, out var first));
        Assert.Equal(0, first.Start); Assert.Equal(3, first.Length);
        Assert.True(editor.Support.TryGetImeTextRangeGeometry(3, 2, false, out var second));
        Assert.Equal(3, second.Start); Assert.Equal(2, second.Length);
        Assert.True(second.Rectangle.Y > first.Rectangle.Y);
        Assert.True(editor.Support.TryGetImeTextRangeGeometry(3, 0, false, out var insertion));
        Assert.Equal(0, insertion.Rectangle.Width);
        Assert.Equal(second.Rectangle.Y, insertion.Rectangle.Y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AppKitGeometry_ProvisionalQueriesMatchTheCompositionCaret(int kind)
    {
        var editor = CreateImeEditor(kind, "123456");
        editor.Element.Measure(new Size(400, 180));
        editor.Element.Arrange(new Rect(0, 0, 400, 180));
        Assert.True(editor.Support.TrySetImeSelection(2, 2));
        editor.Support.OnImeCompositionStart();
        editor.Support.OnImeCompositionUpdate("pin", 2);
        Assert.True(editor.Support.TryGetImeTextRangeGeometry(2, 0, true, out var insertion));
        Rect caret = editor.Support.GetImeCaretRectangle();
        Assert.Equal(caret.X, insertion.Rectangle.X);
        Assert.Equal(caret.Y, insertion.Rectangle.Y);
        Assert.True(editor.Support.TryGetImeTextRangeGeometry(1, 1, true, out var letter));
        Assert.True(letter.Rectangle.Width > 0);
        Assert.True(editor.Support.TryGetImeCharacterIndex(new Point(letter.Rectangle.X + letter.Rectangle.Width / 4,
            letter.Rectangle.Y + letter.Rectangle.Height / 2), true, out int index));
        Assert.InRange(index, 1, 2);
        Assert.Equal("123456", editor.Text().TrimEnd('\n'));
        editor.Support.OnImeCompositionEnd(null);
        Assert.False(editor.Support.TryGetImeTextRangeGeometry(0, 1, true, out _));
    }

    [Fact]
    public void AppKitGeometry_PasswordExposesOnlyProvisionalLayout()
    {
        var password = new PasswordBox { Password = "secret", Width = 400, Height = 100 };
        password.Measure(new Size(400, 100)); password.Arrange(new Rect(0, 0, 400, 100));
        password.Select(2, 2);
        IImeSupport support = password;
        support.OnImeCompositionStart(); support.OnImeCompositionUpdate("pin", 2);
        Assert.True(support.TryGetImeTextRangeGeometry(0, 3, true, out var marked));
        Assert.True(marked.Rectangle.Width > 0);
        Assert.False(support.TryGetImeTextRangeGeometry(0, 3, false, out _));
        Assert.False(support.TryGetImeCharacterIndex(new Point(30, 30), false, out _));
        Assert.False(support.TryGetImeSurroundingText(out _));
        Assert.Equal("secret", password.Password);
        support.OnImeCompositionEnd(null);
    }

    [MacOSNativeGeometryFact]
    public void AppKitGeometry_WindowUsesDpiAndTheFullControlTransform()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var text = new TextBox { Text = "abcd", Margin = new Thickness(30, 40, 0, 0), RenderTransform = new ScaleTransform(2, 3) };
        text.RenderOffset = new Point(7, 9);
        fixture.Window.Content = text;
        fixture.Window.Measure(new Size(400, 220)); fixture.Window.Arrange(new Rect(0, 0, 400, 220));
        try
        {
            Assert.True(text.Focus()); Dispatch(fixture.Window, new() { Type = PlatformEventType.Activate });
            IImeSupport support = text;
            Assert.True(support.TryGetImeTextRangeGeometry(1, 2, false, out var local));
            var request = new PlatformImeGeometryRequest(0, 1, 2, Point.Zero);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeGeometryRequest, ImeGeometryRequest = request });
            Assert.True(request.Handled);
            Point origin = text.TransformToAncestor(null);
            origin = new Point(origin.X + 7, origin.Y + 9);
            Assert.Equal((origin.X + local.Rectangle.X * 2) * 2, request.Geometry.Rectangle.X, 4);
            Assert.Equal((origin.Y + local.Rectangle.Y * 3) * 2, request.Geometry.Rectangle.Y, 4);
            Assert.Equal(local.Rectangle.Width * 4, request.Geometry.Rectangle.Width, 4);
            Assert.Equal(local.Rectangle.Height * 6, request.Geometry.Rectangle.Height, 4);
            var hit = new PlatformImeGeometryRequest(1, 0, 0,
                new Point(request.Geometry.Rectangle.X + request.Geometry.Rectangle.Width / 3,
                    request.Geometry.Rectangle.Y + request.Geometry.Rectangle.Height / 2));
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeGeometryRequest, ImeGeometryRequest = hit });
            Assert.True(hit.Handled); Assert.InRange(hit.CharacterIndex, 1, 3);
            text.CaretIndex = 1; fixture.Window.RefreshLinuxImeContext();
            Assert.Equal((int)Math.Round(support.GetImeCaretRectangle().Height * 6), fixture.Platform.ImeContext.CaretHeight);
            text.IsReadOnly = true;
            request = new(0, 0, 1, Point.Zero);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeGeometryRequest, ImeGeometryRequest = request });
            Assert.False(request.Handled);
        }
        finally { Keyboard.ClearFocus(); }
    }

    [Fact]
    public void AppKitGeometry_RejectsChangedFocusAndSingularTransforms()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Keyboard.Initialize();
        using var fixture = new PlatformFixture();
        var target = new GeometryControl();
        var other = new TextBox();
        var panel = new StackPanel(); panel.Children.Add(target); panel.Children.Add(other);
        fixture.Window.Content = panel;
        try
        {
            Assert.True(target.Focus()); Dispatch(fixture.Window, new() { Type = PlatformEventType.Activate });
            target.DuringQuery = () => other.Focus();
            var request = new PlatformImeGeometryRequest(0, 0, 1, Point.Zero);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeGeometryRequest, ImeGeometryRequest = request });
            Assert.False(request.Handled);
            target.DuringQuery = null; target.RenderTransform = new ScaleTransform(0, 1);
            Assert.True(target.Focus());
            request = new(1, 0, 0, new Point(4, 5));
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeGeometryRequest, ImeGeometryRequest = request });
            Assert.False(request.Handled);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.Deactivate });
            request = new(0, 0, 1, Point.Zero);
            Dispatch(fixture.Window, new() { Type = PlatformEventType.ImeGeometryRequest, ImeGeometryRequest = request });
            Assert.False(request.Handled);
        }
        finally { Keyboard.ClearFocus(); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void NativeImeGeometry_MarshallingWritesToTheOriginalResult(bool accept, bool throws)
    {
        var native = (NativePlatformWindow)RuntimeHelpers.GetUninitializedObject(typeof(NativePlatformWindow));
        PlatformImeGeometryRequest? received = null;
        typeof(NativePlatformWindow).GetField("_eventHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(native, (Action<PlatformEvent>)(evt =>
            {
                received = evt.ImeGeometryRequest;
                if (throws) throw new InvalidOperationException("geometry handler failure");
                received!.Handled = accept;
                received.Geometry = new(new Rect(12.5, 18, 32.25, 40), 1, 2);
                received.CharacterIndex = 7;
            }));
        GCHandle context = GCHandle.Alloc(native);
        nint payload = Marshal.AllocHGlobal(72), result = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.Copy(new byte[72], 0, payload, 72); Marshal.Copy(new byte[32], 0, result, 32);
            Marshal.WriteInt32(payload, 0, 48); Marshal.WriteInt32(payload, 16, 2);
            Marshal.WriteInt32(payload, 20, 1); Marshal.WriteInt32(payload, 24, 2);
            Marshal.WriteInt32(payload, 28, BitConverter.SingleToInt32Bits(12));
            Marshal.WriteInt32(payload, 32, BitConverter.SingleToInt32Bits(20));
            Marshal.WriteIntPtr(payload, 40, result);
            typeof(NativePlatformWindow).GetMethod("OnNativeEventStatic", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [payload, GCHandle.ToIntPtr(context)]);
            Assert.NotNull(received); Assert.Equal(2, received.Kind);
            Assert.Equal(new Point(12, 20), received.Point);
            Assert.Equal(accept ? 1 : -1, Marshal.ReadInt32(result));
            if (accept)
            {
                Assert.Equal(1, Marshal.ReadInt32(result, 4)); Assert.Equal(2, Marshal.ReadInt32(result, 8));
                Assert.Equal(7, Marshal.ReadInt32(result, 12));
                Assert.Equal(12.5f, BitConverter.Int32BitsToSingle(Marshal.ReadInt32(result, 16)));
                Assert.Equal(32.25f, BitConverter.Int32BitsToSingle(Marshal.ReadInt32(result, 24)));
            }
        }
        finally { context.Free(); Marshal.FreeHGlobal(payload); Marshal.FreeHGlobal(result); }
    }

    private sealed class GeometryControl : Control, IImeSupport
    {
        public Action? DuringQuery;
        public GeometryControl() => Focusable = true;
        public Point GetImeCaretPosition() => new(0, 14);
        public void OnImeCompositionStart() { }
        public void OnImeCompositionUpdate(string text, int cursor) { }
        public void OnImeCompositionEnd(string? text) { }
        public bool TryGetImeTextRangeGeometry(int start, int length, bool composition, out ImeTextRangeGeometry geometry)
        { DuringQuery?.Invoke(); geometry = new(new Rect(0, 0, 10, 14), start, length); return true; }
        public bool TryGetImeCharacterIndex(Point point, bool composition, out int index)
        { index = 0; return true; }
    }

    private static (UIElement Element, IImeSupport Support, Func<string> Text, Func<bool> Undo) CreateImeEditor(int kind, string content)
    {
        if (kind == 0)
        {
            var text = new TextBox { Text = content };
            return (text, text, () => text.Text, () => text.Undo());
        }
        if (kind == 1)
        {
            var rich = new RichTextBox(); rich.SetPlainText(content);
            return (rich, rich, rich.GetPlainText, () => rich.Undo());
        }
        if (kind == 3)
        {
            var autoComplete = new AutoCompleteBox { Text = content };
            return (autoComplete, autoComplete, () => autoComplete.Text, () => autoComplete.Undo());
        }
        if (kind == 4)
        {
            var number = new NumberBox { Text = content };
            return (number, number, () => number.Text, () => number.Undo());
        }
        var editor = new EditControl(); editor.LoadText(content);
        return (editor, editor, () => editor.Text, () => { bool possible = editor.CanUndo; editor.Undo(); return possible; });
    }

    [Fact]
    public void NativeDestruction_CompletesManagedTeardownOnce()
    {
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", nint.Zero);
        int closed = 0;
        fixture.Window.Closed += (_, _) => closed++;
        fixture.Platform.Raise(new() { Type = PlatformEventType.Destroyed });
        Assert.Equal(1, closed);
        Assert.True(fixture.Platform.Disposed);
        fixture.Window.Close();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Close_WithPlatformHandle_ReleasesWindowAndRaisesClosed()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new PlatformFixture();
        SetField(fixture.Window, "<Handle>k__BackingField", (nint)0x1234);
        var openWindows = (Dictionary<nint, Window>)typeof(Window)
            .GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var keepAlive = new DisplayedTestWindow();
        openWindows.Add((nint)0x1234, fixture.Window);
        openWindows.Add((nint)0x1235, keepAlive);
        int closed = 0;
        fixture.Window.Closed += (_, _) => closed++;
        try
        {
            fixture.Window.Close();
            Assert.Equal(1, closed);
            Assert.True(fixture.Platform.Disposed);
            Assert.Equal(nint.Zero, fixture.Window.Handle);
            Assert.False(openWindows.ContainsKey((nint)0x1234));
        }
        finally
        {
            openWindows.Remove((nint)0x1234); openWindows.Remove((nint)0x1235);
            keepAlive.Close();
        }
    }

    private sealed class PlatformFixture : IDisposable
    {
        public Window Window { get; }
        public FakePlatformWindow Platform { get; } = new();
        public PlatformFixture(Window? window = null)
        {
            Window = window ?? new DisplayedTestWindow { Width = 320, Height = 240, TitleBarStyle = WindowTitleBarStyle.Native };
            if (OperatingSystem.IsMacOS() && Window.Visibility != Visibility.Visible)
                typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(Window, [Visibility.Visible]);
            SetField(Window, "_platformWindow", Platform);
            SetField(Window, "_dpiScale", 2d);
            Platform.SetEventHandler(evt => Dispatch(Window, evt));
        }
        public void Dispose()
        {
            SetField(Window, "<Handle>k__BackingField", nint.Zero);
            SetField(Window, "_platformWindow", null);
            Platform.Dispose();
            Window.Close();
        }
    }

    private sealed class CallbackShowWindow : Window
    {
        public Action? ShowAction;
        public override void Show()
        {
            SetField(this, "_dispatcher", Dispatcher.CurrentDispatcher);
            ShowAction?.Invoke();
        }
    }

    private sealed class CallbackPropertyWindow : Window
    {
        public Action<DependencyPropertyChangedEventArgs>? PropertyChangedAction;
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            PropertyChangedAction?.Invoke(e);
        }
    }

    private sealed class FakePlatformWindow : IPlatformWindow
    {
        private Action<PlatformEvent>? _handler;
        public int Width = 640, Height = 480, ResizeCalls, MinWidth, MaxHeight;
        public uint Style;
        public int Backdrop;
        public int TitleBarDoubleClickCalls;
        public int HideCalls;
        public int ShowCalls, ActivateCalls;
        public Action? ShowAction, ActivateAction, MoveAction;
        public List<WindowState> StateRequests = [];
        public nint OwnerHandle;
        public WindowState? TitleBarActionState;
        public bool Disposed;
        public PlatformImeContext ImeContext;
        public List<PlatformImeContext> ImeContexts = [];
        public (int X, int Y, int Width, int Height)? RestoreBounds;
        public nint NativeHandle => 0x1234;
        public NativeSurfaceDescriptor GetSurface() => default;
        public void Show() { ShowCalls++; ShowAction?.Invoke(); }
        public void Hide() => HideCalls++;
        public void Close() { }
        public void SetTitle(string title) { }
        public void Resize(int width, int height)
        {
            ResizeCalls++;
            Width = width; Height = height;
            Raise(new() { Type = PlatformEventType.Resize, Width = width, Height = height });
        }
        public void Raise(PlatformEvent evt) => _handler?.Invoke(evt);
        public void Move(int x, int y) => MoveAction?.Invoke();
        public int GetWidth() => Width;
        public int GetHeight() => Height;
        public void GetPosition(out int x, out int y) { x = 120; y = 160; }
        public bool TryGetRestoreBounds(out int x, out int y, out int width, out int height)
        {
            x = y = width = height = 0;
            if (RestoreBounds is not { } bounds) return false;
            (x, y, width, height) = bounds;
            return true;
        }
        public void SetMinMaxSize(int minWidth, int minHeight, int maxWidth, int maxHeight) { MinWidth = minWidth; MaxHeight = maxHeight; }
        public bool BeginMoveDrag() => true;
        public bool PerformTitleBarDoubleClick()
        {
            TitleBarDoubleClickCalls++;
            if (TitleBarActionState is { } state)
                Raise(new() { Type = PlatformEventType.StateChanged, NewState = (int)state });
            return true;
        }
        public bool BeginResizeDrag(int edge) => true;
        public bool SetIcon(uint[]? pixels, int width, int height) => true;
        public bool SetTopmost(bool topmost) => true;
        public bool SetEnabled(bool enabled) => true;
        public bool SetOpacity(double opacity) => true;
        public bool SetShowInTaskbar(bool show) => true;
        public bool SetResizable(bool resizable) => true;
        public bool SetDecorated(bool decorated) => true;
        public bool SetStyle(uint style) { Style = style; return true; }
        public bool SetSystemBackdrop(int backdrop) { Backdrop = backdrop; return true; }
        public bool SetOwner(nint owner) { OwnerHandle = owner; return true; }
        public bool Activate() { ActivateCalls++; ActivateAction?.Invoke(); return true; }
        public bool ShowSystemMenu(int x, int y) => false;
        public void SetState(WindowState state) => StateRequests.Add(state);
        public WindowState GetState() => WindowState.Normal;
        public void Invalidate() { }
        public float GetDpiScale() => 2;
        public int GetMonitorRefreshRate() => 60;
        public void SetCursor(int shape) { }
        public void UpdateImeContext(PlatformImeContext context) { ImeContext = context; ImeContexts.Add(context); }
        public void SetEventHandler(Action<PlatformEvent>? handler) => _handler = handler;
        public void Dispose() { _handler = null; Disposed = true; }
    }
}
