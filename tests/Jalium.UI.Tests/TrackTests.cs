using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public class TrackTests
{
    [Fact]
    public void Vertical_ValueFromDistance_DragDown_ShouldIncrease_WhenNotReversed()
    {
        var track = new Track
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            Value = 50
        };

        track.Arrange(new Rect(0, 0, 12, 100));

        var delta = track.ValueFromDistance(0, 10);

        Assert.True(delta > 0);
    }

    [Fact]
    public void Vertical_ValueFromDistance_DragDown_ShouldDecrease_WhenReversed()
    {
        var track = new Track
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            IsDirectionReversed = true
        };

        track.Arrange(new Rect(0, 0, 12, 100));

        var delta = track.ValueFromDistance(0, 10);

        Assert.True(delta < 0);
    }
}
