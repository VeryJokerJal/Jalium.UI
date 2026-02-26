using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Event handler delegates for manipulation routed events.
/// </summary>
public delegate void ManipulationStartingEventHandler(object sender, ManipulationStartingEventArgs e);
public delegate void ManipulationStartedEventHandler(object sender, ManipulationStartedEventArgs e);
public delegate void ManipulationDeltaEventHandler(object sender, ManipulationDeltaEventArgs e);
public delegate void ManipulationInertiaStartingEventHandler(object sender, ManipulationInertiaStartingEventArgs e);
public delegate void ManipulationBoundaryFeedbackEventHandler(object sender, ManipulationBoundaryFeedbackEventArgs e);
public delegate void ManipulationCompletedEventHandler(object sender, ManipulationCompletedEventArgs e);

/// <summary>
/// Routed events for manipulation input.
/// </summary>
public static class ManipulationEvents
{
    public static readonly RoutedEvent PreviewManipulationStartingEvent =
        UIElement.PreviewManipulationStartingEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent ManipulationStartingEvent =
        UIElement.ManipulationStartingEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewManipulationStartedEvent =
        UIElement.PreviewManipulationStartedEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent ManipulationStartedEvent =
        UIElement.ManipulationStartedEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewManipulationDeltaEvent =
        UIElement.PreviewManipulationDeltaEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent ManipulationDeltaEvent =
        UIElement.ManipulationDeltaEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewManipulationInertiaStartingEvent =
        UIElement.PreviewManipulationInertiaStartingEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent ManipulationInertiaStartingEvent =
        UIElement.ManipulationInertiaStartingEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewManipulationBoundaryFeedbackEvent =
        UIElement.PreviewManipulationBoundaryFeedbackEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent ManipulationBoundaryFeedbackEvent =
        UIElement.ManipulationBoundaryFeedbackEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewManipulationCompletedEvent =
        UIElement.PreviewManipulationCompletedEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent ManipulationCompletedEvent =
        UIElement.ManipulationCompletedEvent.AddOwner(typeof(UIElement));
}
