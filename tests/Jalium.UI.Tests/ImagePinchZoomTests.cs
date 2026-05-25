using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ImagePinchZoomTests
{
    [Fact]
    public void IsZoomEnabledDefault_IsFalse_AndIsManipulationEnabledIsFalse()
    {
        var image = new Image();
        Assert.False(image.IsZoomEnabled);
        Assert.False(image.IsManipulationEnabled);
    }

    [Fact]
    public void SettingIsZoomEnabled_EnablesManipulation()
    {
        var image = new Image { IsZoomEnabled = true };
        Assert.True(image.IsManipulationEnabled);
    }

    [Fact]
    public void ManipulationDelta_AccumulatesScaleIntoCurrentZoom()
    {
        var image = new Image { IsZoomEnabled = true, MinZoom = 0.5, MaxZoom = 4.0 };

        RaiseManipulationDelta(image, scale: 2.0);
        Assert.Equal(2.0, image.CurrentZoom, precision: 4);

        RaiseManipulationDelta(image, scale: 1.5);
        Assert.Equal(3.0, image.CurrentZoom, precision: 4);
    }

    [Fact]
    public void ManipulationDelta_ClampsToMaxZoom_AndReportsBoundaryFeedback()
    {
        var image = new Image { IsZoomEnabled = true, MinZoom = 1.0, MaxZoom = 2.0 };
        var args = RaiseManipulationDelta(image, scale: 5.0);

        Assert.Equal(2.0, image.CurrentZoom, precision: 4);
        Assert.NotNull(args.UnusedManipulation);
        Assert.True(args.UnusedManipulation!.Scale.X > 0);
    }

    private static ManipulationDeltaEventArgs RaiseManipulationDelta(Image image, double scale)
    {
        var args = new ManipulationDeltaEventArgs
        {
            RoutedEvent = UIElement.ManipulationDeltaEvent,
            ManipulationContainer = image,
            ManipulationOrigin = new Point(0, 0),
            DeltaManipulation = new ManipulationDelta
            {
                Translation = Vector.Zero,
                Rotation = 0,
                Scale = new Vector(scale, scale),
                Expansion = Vector.Zero
            },
            CumulativeManipulation = new ManipulationDelta
            {
                Translation = Vector.Zero,
                Scale = new Vector(scale, scale)
            },
            Velocities = new ManipulationVelocities
            {
                LinearVelocity = Vector.Zero,
                AngularVelocity = 0,
                ExpansionVelocity = Vector.Zero
            },
            IsInertial = false
        };
        image.RaiseEvent(args);
        return args;
    }
}
