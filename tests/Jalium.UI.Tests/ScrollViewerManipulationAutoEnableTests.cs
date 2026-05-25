using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class ScrollViewerManipulationAutoEnableTests
{
    [Fact]
    public void DefaultScrollViewer_HasTouchPanningEnabled()
    {
        // ScrollViewer ships touch-friendly out of the box: PanningMode defaults to
        // VerticalFirst (WinUI parity) and the ctor mirrors that into
        // IsManipulationEnabled = true so finger drags inside the viewport scroll
        // without requiring the consumer to opt in.
        var sv = new ScrollViewer();
        Assert.Equal(PanningMode.VerticalFirst, sv.PanningMode);
        Assert.True(sv.IsManipulationEnabled);
    }

    [Theory]
    [InlineData(PanningMode.HorizontalOnly)]
    [InlineData(PanningMode.VerticalOnly)]
    [InlineData(PanningMode.Both)]
    [InlineData(PanningMode.HorizontalFirst)]
    [InlineData(PanningMode.VerticalFirst)]
    public void SettingNonNonePanningMode_AutomaticallyEnablesManipulation(PanningMode mode)
    {
        var sv = new ScrollViewer { PanningMode = mode };
        Assert.True(sv.IsManipulationEnabled,
            $"Expected IsManipulationEnabled=true after setting PanningMode={mode}");
    }

    [Fact]
    public void SettingPanningModeBackToNone_DoesNotAutoClear()
    {
        // Avoid clobbering an app-level opt-in: PanningMode → None must not auto-disable.
        var sv = new ScrollViewer { PanningMode = PanningMode.Both };
        Assert.True(sv.IsManipulationEnabled);
        sv.PanningMode = PanningMode.None;
        Assert.True(sv.IsManipulationEnabled);
    }
}
