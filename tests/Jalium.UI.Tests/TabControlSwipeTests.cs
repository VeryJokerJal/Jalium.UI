using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class TabControlSwipeTests
{
    [Fact]
    public void SwipeLeftBeyondThreshold_AdvancesSelectedTab()
    {
        var tabControl = BuildThreeTabControl();

        RaiseManipulationCompleted(tabControl, totalX: -120);

        Assert.Equal(1, tabControl.SelectedIndex);
    }

    [Fact]
    public void SwipeRightBeyondThreshold_GoesBackToPreviousTab()
    {
        var tabControl = BuildThreeTabControl();
        tabControl.SelectedIndex = 2;

        RaiseManipulationCompleted(tabControl, totalX: 120);

        Assert.Equal(1, tabControl.SelectedIndex);
    }

    [Fact]
    public void SwipeBelowThreshold_DoesNotChangeTab()
    {
        var tabControl = BuildThreeTabControl();
        tabControl.SelectedIndex = 1;

        RaiseManipulationCompleted(tabControl, totalX: 10);

        Assert.Equal(1, tabControl.SelectedIndex);
    }

    [Fact]
    public void IsSwipeEnabledFalse_SuppressesTabSwitch()
    {
        var tabControl = BuildThreeTabControl();
        tabControl.IsSwipeEnabled = false;

        RaiseManipulationCompleted(tabControl, totalX: -200);

        Assert.Equal(0, tabControl.SelectedIndex);
    }

    private static TabControl BuildThreeTabControl()
    {
        var tabControl = new TabControl();
        tabControl.Items.Add(new TabItem { Header = "A" });
        tabControl.Items.Add(new TabItem { Header = "B" });
        tabControl.Items.Add(new TabItem { Header = "C" });
        tabControl.SelectedIndex = 0;
        return tabControl;
    }

    private static void RaiseManipulationCompleted(TabControl target, double totalX)
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
