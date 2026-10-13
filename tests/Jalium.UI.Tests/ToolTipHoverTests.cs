using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ToolTipHoverTests : IDisposable
{
    private readonly Window _window = new();
    private readonly WindowInputDispatcher _input;

    public ToolTipHoverTests()
    {
        ToolTipService.Cleanup();
        UIElement.ForceReleaseMouseCapture();
        _input = new WindowInputDispatcher(_window);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BriefHover_LeaveBeforeDelay_DoesNotOpenToolTip(bool disabled)
    {
        var hint = Hint();
        var owner = new Button { ToolTip = hint, IsEnabled = !disabled };
        ToolTipService.SetShowOnDisabled(owner, true);
        int opened = 0;
        hint.Opened += (_, _) => opened++;

        Hover(owner);
        Hover(null);
        PumpFor(120);

        Assert.False(hint.IsOpen);
        Assert.Equal(0, opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SustainedHover_LeavingOwner_ClosesToolTip(bool disabled)
    {
        var hint = Hint();
        var owner = new Button { ToolTip = hint, IsEnabled = !disabled };
        ToolTipService.SetShowOnDisabled(owner, true);

        Hover(owner);
        WaitForOpen(hint);
        Hover(null);

        Assert.False(hint.IsOpen);
        Assert.False(owner.IsMouseOver);
    }

    [Fact]
    public void DisabledOwner_DefaultSettings_DoesNotRequestToolTip()
    {
        var hint = Hint();
        var owner = new Button { ToolTip = hint, IsEnabled = false };
        int openings = 0;
        owner.ToolTipOpening += (_, _) => openings++;

        Hover(owner);
        Hover(null);
        PumpFor(120);

        Assert.False(hint.IsOpen);
        Assert.Equal(0, openings);
        Assert.False(owner.IsMouseOver);
    }

    [Fact]
    public void DisabledAncestor_DefaultSettings_DoesNotRequestChildToolTip()
    {
        var hint = Hint();
        var owner = new Button { ToolTip = hint };
        var parent = new Border { IsEnabled = false, Child = owner };
        int openings = 0;
        owner.ToolTipOpening += (_, _) => openings++;

        Hover(owner);

        Assert.False(owner.IsEnabled);
        Assert.Equal(0, openings);
        Assert.False(hint.IsOpen);
        GC.KeepAlive(parent);
    }

    [Fact]
    public void DisabledToolTipService_DoesNotRequestToolTip()
    {
        var hint = Hint();
        var owner = new Button { ToolTip = hint };
        ToolTipService.SetIsEnabled(owner, false);
        int openings = 0;
        owner.ToolTipOpening += (_, _) => openings++;

        Hover(owner);

        Assert.Equal(0, openings);
        Assert.False(hint.IsOpen);
    }

    [Fact]
    public void DisabledOwner_ShowOnDisabled_StillReceivesPairedEnterAndLeave()
    {
        var owner = new Button { IsEnabled = false };
        int enters = 0, leaves = 0;
        owner.MouseEnter += (_, _) => enters++;
        owner.MouseLeave += (_, _) => leaves++;

        Hover(owner);
        Assert.False(owner.IsMouseOver);
        Hover(null);

        Assert.Equal(1, enters);
        Assert.Equal(1, leaves);
    }

    [Fact]
    public void DisabledOwner_MovingBetweenChildren_DoesNotRestartHover()
    {
        var hint = Hint();
        var first = new Border();
        var second = new Border();
        var owner = new StackPanel { IsEnabled = false, ToolTip = hint };
        owner.Children.Add(first);
        owner.Children.Add(second);
        ToolTipService.SetShowOnDisabled(owner, true);
        int enters = 0, leaves = 0, openings = 0;
        owner.MouseEnter += (_, _) => enters++;
        owner.MouseLeave += (_, _) => leaves++;
        owner.ToolTipOpening += (_, _) => openings++;

        Hover(first);
        Hover(second);
        WaitForOpen(hint);
        Hover(first);

        Assert.True(hint.IsOpen);
        Assert.False(owner.IsMouseOver);
        Assert.Equal(1, enters);
        Assert.Equal(0, leaves);
        Assert.Equal(1, openings);

        Hover(null);
        Assert.Equal(1, leaves);
        Assert.False(hint.IsOpen);
    }

    [Fact]
    public void HoveredOwner_DisabledBeforeLeaving_DoesNotLeaveToolTipOpen()
    {
        var hint = Hint();
        var owner = new Button { ToolTip = hint };

        Hover(owner);
        WaitForOpen(hint);
        owner.IsEnabled = false;
        Assert.False(owner.IsMouseOver);
        Hover(null);

        Assert.False(hint.IsOpen);
    }

    [Fact]
    public void DisabledOwner_LeavingWindow_CancelsPendingToolTip()
    {
        var hint = Hint();
        var owner = new Button { IsEnabled = false, ToolTip = hint };
        ToolTipService.SetShowOnDisabled(owner, true);

        Hover(owner);
        _input.HandleMouseLeave();
        PumpFor(120);

        Assert.False(hint.IsOpen);
    }

    [Fact]
    public void SwitchingOwners_CancelsOldDelayAndOpensOnlyNewToolTip()
    {
        var oldHint = Hint();
        var newHint = Hint();
        var first = new Button { IsEnabled = false, ToolTip = oldHint };
        var second = new Button { ToolTip = newHint };
        ToolTipService.SetShowOnDisabled(first, true);

        Hover(first);
        Hover(second);
        WaitForOpen(newHint);

        Assert.False(oldHint.IsOpen);
        Hover(null);
        Assert.False(newHint.IsOpen);
    }

    [Fact]
    public void BriefHover_ThenReenter_OpensAndClosesNormally()
    {
        var hint = Hint();
        var owner = new Button { IsEnabled = false, ToolTip = hint };
        ToolTipService.SetShowOnDisabled(owner, true);

        Hover(owner);
        Hover(null);
        PumpFor(120);
        Assert.False(hint.IsOpen);

        Hover(owner);
        WaitForOpen(hint);
        Hover(null);
        Assert.False(hint.IsOpen);
    }

    [Fact]
    public void ChangingToolTipContentDuringDelay_CancelsOldToolTip()
    {
        var hint = Hint();
        var owner = new Button { ToolTip = hint };

        Hover(owner);
        owner.ToolTip = null;
        PumpFor(120);

        Assert.False(hint.IsOpen);
    }

    private static ToolTip Hint() => new() { Content = "发送（Enter）", InitialShowDelay = 40 };

    private void Hover(UIElement? target) => _input.UpdateMouseOverState(target, Environment.TickCount);

    private static void WaitForOpen(ToolTip hint)
    {
        var elapsed = Stopwatch.StartNew();
        while (!hint.IsOpen && elapsed.ElapsedMilliseconds < 2000)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Thread.Sleep(5);
        }
        Assert.True(hint.IsOpen);
    }

    private static void PumpFor(int milliseconds)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Thread.Sleep(5);
        }
        Dispatcher.CurrentDispatcher.ProcessQueue();
    }

    public void Dispose()
    {
        Hover(null);
        ToolTipService.Cleanup();
        _window.Close();
    }
}
