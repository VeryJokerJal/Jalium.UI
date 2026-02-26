using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class ManipulationRoutingTests
{
    [Fact]
    public void IsManipulationEnabled_DefaultShouldBeFalse()
    {
        var element = new Border();
        Assert.False(element.IsManipulationEnabled);

        element.IsManipulationEnabled = true;
        Assert.True(element.IsManipulationEnabled);
    }

    [Fact]
    public void ManipulationStartingPipeline_ShouldRaisePreviewThenBubble()
    {
        var window = new Window();
        var order = new List<string>();

        window.AddHandler(UIElement.PreviewManipulationStartingEvent, new RoutedEventHandler((_, _) => order.Add("Preview")));
        window.AddHandler(UIElement.ManipulationStartingEvent, new RoutedEventHandler((_, _) => order.Add("Bubble")));

        bool result = InvokeManipulationStartingPipeline(window, window);

        Assert.True(result);
        Assert.Equal(new[] { "Preview", "Bubble" }, order);
    }

    [Fact]
    public void ManipulationStartingPipeline_WhenCanceled_ShouldStopBubble()
    {
        var window = new Window();
        int previewCount = 0;
        int bubbleCount = 0;

        window.AddHandler(UIElement.PreviewManipulationStartingEvent, new RoutedEventHandler((_, e) =>
        {
            previewCount++;
            ((ManipulationStartingEventArgs)e).Cancel = true;
        }));
        window.AddHandler(UIElement.ManipulationStartingEvent, new RoutedEventHandler((_, _) => bubbleCount++));

        bool result = InvokeManipulationStartingPipeline(window, window);

        Assert.False(result);
        Assert.Equal(1, previewCount);
        Assert.Equal(0, bubbleCount);
    }

    [Fact]
    public void ManipulationEvents_ShouldRoutePreviewAndBubble()
    {
        var parent = new TestElement();
        var child = new TestElement();
        parent.AddVisualChild(child);

        var order = new List<string>();
        parent.AddHandler(UIElement.PreviewManipulationDeltaEvent, new RoutedEventHandler((_, _) => order.Add("ParentPreview")));
        child.AddHandler(UIElement.PreviewManipulationDeltaEvent, new RoutedEventHandler((_, _) => order.Add("ChildPreview")));
        child.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, _) => order.Add("ChildBubble")));
        parent.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, _) => order.Add("ParentBubble")));

        child.RaiseEvent(new ManipulationDeltaEventArgs
        {
            RoutedEvent = UIElement.PreviewManipulationDeltaEvent,
            ManipulationContainer = child,
            ManipulationOrigin = new Point(0, 0),
            DeltaManipulation = new ManipulationDelta { Translation = new Vector(1, 0), Scale = new Vector(1, 1), Expansion = Vector.Zero, Rotation = 0 },
            CumulativeManipulation = new ManipulationDelta { Translation = new Vector(1, 0), Scale = new Vector(1, 1), Expansion = Vector.Zero, Rotation = 0 },
            Velocities = new ManipulationVelocities { LinearVelocity = new Vector(1, 0), AngularVelocity = 0, ExpansionVelocity = Vector.Zero },
            IsInertial = false
        });

        child.RaiseEvent(new ManipulationDeltaEventArgs
        {
            RoutedEvent = UIElement.ManipulationDeltaEvent,
            ManipulationContainer = child,
            ManipulationOrigin = new Point(0, 0),
            DeltaManipulation = new ManipulationDelta { Translation = new Vector(1, 0), Scale = new Vector(1, 1), Expansion = Vector.Zero, Rotation = 0 },
            CumulativeManipulation = new ManipulationDelta { Translation = new Vector(1, 0), Scale = new Vector(1, 1), Expansion = Vector.Zero, Rotation = 0 },
            Velocities = new ManipulationVelocities { LinearVelocity = new Vector(1, 0), AngularVelocity = 0, ExpansionVelocity = Vector.Zero },
            IsInertial = false
        });

        Assert.Equal(new[] { "ParentPreview", "ChildPreview", "ChildBubble", "ParentBubble" }, order);
    }

    private static bool InvokeManipulationStartingPipeline(Window window, UIElement target)
    {
        var method = typeof(Window).GetMethod("RaiseManipulationStartingPipeline", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(window, new object[] { target });
        Assert.NotNull(result);
        return (bool)result!;
    }

    private sealed class TestElement : FrameworkElement
    {
        public new void AddVisualChild(Visual child)
        {
            base.AddVisualChild(child);
        }
    }
}
