using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed partial class MacOSAccessibilityFocusHostTests
{
    private const MacOSAXOperation PopupKeyboardOperation = (MacOSAXOperation)64;

    [Theory] [InlineData("owner")] [InlineData("overlay")] [InlineData("native")]
    public void OwnKeyTransitionCompletesTheExplicitFocusRequest(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        h.SetActivation(() => { h.Change("activation-away-back"); return true; });
        h.Accept(); h.Process();
        Assert.Equal(1, h.ActivationCalls); Assert.Same(h.Target, Keyboard.FocusedElement);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PopupKeyboardEligibilityIsIndependentOfPointerHitTesting(bool hitTest)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest);
        Assert.Equal(hitTest, h.Source.IsHitTestVisible);
        Assert.True(PopupKeyboardEligible(h));
        Assert.Same(h.Before, Keyboard.FocusedElement);
        Assert.Equal(0, h.ActivationCalls);
    }

    [Theory]
    [InlineData("not-focusable")] [InlineData("disabled")] [InlineData("hidden")]
    [InlineData("collapsed")] [InlineData("parent-disabled")] [InlineData("parent-hidden")]
    [InlineData("modal")]
    public void PopupKeyboardEligibilityRejectsUnavailableContent(string state)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false);
        if (state == "not-focusable") h.Target.Focusable = h.Other.Focusable = false;
        else if (state == "disabled") h.Target.IsEnabled = h.Other.IsEnabled = false;
        else if (state == "hidden") h.Target.Visibility = h.Other.Visibility = Visibility.Hidden;
        else if (state == "collapsed") h.Target.Visibility = h.Other.Visibility = Visibility.Collapsed;
        else if (state == "parent-disabled") h.Body.IsEnabled = false;
        else if (state == "parent-hidden") h.Body.Visibility = Visibility.Hidden;
        else h.Change("modal");
        Assert.False(PopupKeyboardEligible(h)); Assert.Same(h.Before, Keyboard.FocusedElement);
    }

    [Fact]
    public void PopupKeyboardEligibilityTracksChangesWithoutALayoutPass()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false);
        h.Other.Focusable = false; h.Target.Focusable = false;
        Assert.False(PopupKeyboardEligible(h));
        h.Target.Focusable = true; Assert.True(PopupKeyboardEligible(h));
        h.Target.IsEnabled = false; Assert.False(PopupKeyboardEligible(h));
        h.Target.IsEnabled = true; Assert.True(PopupKeyboardEligible(h));
        h.Target.Visibility = Visibility.Collapsed; Assert.False(PopupKeyboardEligible(h));
        h.Target.Visibility = Visibility.Visible; Assert.True(PopupKeyboardEligible(h));
        Assert.False(h.Source.IsHitTestVisible); Assert.Equal(0, h.ActivationCalls);
    }

    [Fact]
    public void PopupKeyboardEligibilityKeepsTextOnlyTipsNoninteractive()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false);
        h.Body.Children.Clear(); h.Body.Children.Add(new TextBlock { Text = "提示中文🙂", Focusable = false });
        Assert.False(PopupKeyboardEligible(h)); Assert.False(h.Source.IsHitTestVisible);
    }

    [Fact]
    public void PopupKeyboardEligibilityPreservesTooltipNonactivation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false, popupChild: new ToolTip
        { Focusable = true, Content = new TextBox { Text = "提示" } });
        Assert.False(PopupKeyboardEligible(h)); Assert.Equal(0, h.ActivationCalls);
    }

    [Fact]
    public void PopupKeyboardEligibilityIncludesSelectableTextWithoutATabStop()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false); h.Body.Children.Clear();
        h.Body.Children.Add(new TextBlock { Text = "可选择中文🙂" });
        Assert.True(PopupKeyboardEligible(h)); Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData("owner")] [InlineData("overlay")]
    public void PopupKeyboardCapabilityIsNotAdvertisedByOrdinaryWindowRoots(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        var request = new MacOSAXRequest { NodeId = 1, Operation = PopupKeyboardOperation };
        Assert.False(h.Tree.Handle(ref request));
    }

    [Fact]
    public void PopupKeyboardCapabilityRejectsNonRootAndRetiredNodes()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false);
        Assert.True(h.Tree.TryGetId(h.Target.GetAutomationPeer()!, out var id));
        var request = new MacOSAXRequest { NodeId = id, Operation = PopupKeyboardOperation };
        Assert.False(h.Tree.Handle(ref request));
        h.Tree.Dispose(); request.NodeId = 1; Assert.False(h.Tree.Handle(ref request));
    }

    [Theory]
    [InlineData("hide")] [InlineData("modal")] [InlineData("detach")] [InlineData("disable")]
    public void PopupKeyboardQueryCannotPublishAnObsoleteTree(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false);
        h.Body.Children.Clear();
        var candidate = new Control { Focusable = true };
        var probe = new KeyboardProbe(candidate); h.Body.Children.Add(probe);
        probe.Read = () =>
        {
            if (change == "hide") Visible(h.Source, Visibility.Hidden);
            else if (change == "modal") h.Change("modal");
            else if (change == "detach") h.Body.Children.Remove(probe);
            else candidate.IsEnabled = false;
        };
        // Hide makes the root unavailable; other changes return a supported
        // negative capability. Neither may preserve the old positive result.
        var request = new MacOSAXRequest { NodeId = 1, Operation = PopupKeyboardOperation };
        bool accepted = h.Tree.Handle(ref request);
        if (change != "hide") Assert.True(accepted);
        Assert.Equal(0ul, request.ResultId);
        Assert.True(probe.Reads > 0); Assert.Equal(0, h.ActivationCalls);
    }

    [Fact]
    public void PopupKeyboardQueryBoundsRecursiveQueriesAndRecovers()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false);
        h.Body.Children.Clear(); var probe = new KeyboardProbe(new Control { Focusable = true }); h.Body.Children.Add(probe);
        probe.Read = () => Assert.False(PopupKeyboardEligible(h));
        Assert.True(PopupKeyboardEligible(h)); Assert.True(PopupKeyboardEligible(h));
        Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData("detach")] [InlineData("modal")]
    public void PopupKeyboardQueryRechecksContextAfterTheLastEnabledGetter(string change)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false); h.Body.Children.Clear();
        var candidate = new KeyboardEnabledProbe { Focusable = true };
        h.Body.Children.Add(candidate);
        candidate.Reads = 0;
        candidate.Read = () =>
        {
            if (change == "detach") h.Body.Children.Remove(candidate);
            else h.Change("modal");
        };
        Assert.False(PopupKeyboardEligible(h)); Assert.True(candidate.Reads >= 3);
        Assert.Equal(0, h.ActivationCalls);
    }

    [Fact]
    public void PopupKeyboardQueryFindsAFocusableChildAfterALargeNonfocusablePrefix()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host("native", hitTest: false); h.Body.Children.Clear();
        for (int index = 0; index < 5000; index++) h.Body.Children.Add(new Control());
        h.Body.Children.Add(new Control { Focusable = true });
        Assert.True(PopupKeyboardEligible(h)); Assert.Equal(0, h.ActivationCalls);
    }

    private static bool PopupKeyboardEligible(Host h)
    {
        var request = new MacOSAXRequest { NodeId = 1, Operation = PopupKeyboardOperation };
        Assert.True(h.Tree.Handle(ref request)); return request.ResultId != 0;
    }

    private sealed class KeyboardProbe : FrameworkElement
    {
        private readonly UIElement _child;
        internal Action? Read;
        internal int Reads;
        internal KeyboardProbe(UIElement child) { _child = child; AddVisualChild(child); SyncVisualChildrenCount(); }
        protected override int VisualChildrenCount => _child == null ? 0 : 1;
        protected override Visual? GetVisualChild(int index)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            Reads++; var read = Read; Read = null; read?.Invoke(); return _child;
        }
    }

    private sealed class KeyboardEnabledProbe : Control
    {
        internal int Reads;
        internal Action? Read;
        protected override bool IsEnabledCore
        {
            get
            {
                if (++Reads == 3) { var read = Read; Read = null; read?.Invoke(); }
                return true;
            }
        }
    }
}
