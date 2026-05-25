using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CalendarSwipeMonthTests
{
    [Fact]
    public void SwipeLeftBeyondThreshold_AdvancesMonth()
    {
        var calendar = new Calendar { DisplayDate = new DateTime(2025, 5, 15) };
        RaiseManipulationCompleted(calendar, totalX: -120);
        Assert.Equal(6, calendar.DisplayDate.Month);
        Assert.Equal(2025, calendar.DisplayDate.Year);
    }

    [Fact]
    public void SwipeRightBeyondThreshold_GoesBackOneMonth()
    {
        var calendar = new Calendar { DisplayDate = new DateTime(2025, 5, 15) };
        RaiseManipulationCompleted(calendar, totalX: 120);
        Assert.Equal(4, calendar.DisplayDate.Month);
    }

    [Fact]
    public void SwipeBelowThreshold_DoesNotChangeMonth()
    {
        var calendar = new Calendar { DisplayDate = new DateTime(2025, 5, 15) };
        RaiseManipulationCompleted(calendar, totalX: 20);
        Assert.Equal(5, calendar.DisplayDate.Month);
    }

    private static void RaiseManipulationCompleted(Calendar target, double totalX)
    {
        var args = new ManipulationCompletedEventArgs
        {
            RoutedEvent = UIElement.ManipulationCompletedEvent,
            ManipulationContainer = target,
            ManipulationOrigin = new Point(0, 0),
            TotalManipulation = new ManipulationDelta
            {
                Translation = new Vector(totalX, 0),
                Scale = new Vector(1, 1)
            },
            FinalVelocities = new ManipulationVelocities
            {
                LinearVelocity = Vector.Zero,
                AngularVelocity = 0,
                ExpansionVelocity = Vector.Zero
            }
        };
        target.RaiseEvent(args);
    }
}
