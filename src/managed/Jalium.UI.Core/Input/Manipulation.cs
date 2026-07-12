using System.Runtime.CompilerServices;
using Jalium.UI.Input.Manipulations;

namespace Jalium.UI.Input;

/// <summary>Provides imperative access to active touch manipulations.</summary>
public static class Manipulation
{
    private static readonly ConditionalWeakTable<UIElement, ManipulationState> s_states = new();

    public static void AddManipulator(UIElement element, IManipulator manipulator)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(manipulator);
        if (!element.IsManipulationEnabled)
            throw new InvalidOperationException("Manipulation is not enabled on the element.");

        ManipulationState state = s_states.GetValue(element, static owner => new(owner));
        lock (state.Gate)
        {
            state.Manipulators.Add(manipulator);
        }
    }

    public static void RemoveManipulator(UIElement element, IManipulator manipulator)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(manipulator);
        if (!s_states.TryGetValue(element, out ManipulationState? state))
            return;

        bool removed;
        bool empty;
        lock (state.Gate)
        {
            removed = state.Manipulators.Remove(manipulator);
            empty = state.Manipulators.Count == 0;
        }
        if (removed)
            manipulator.ManipulationEnded(cancel: false);
        if (empty)
            s_states.Remove(element);
    }

    public static void CompleteManipulation(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!s_states.TryGetValue(element, out ManipulationState? state))
            return;

        RaiseCompleted(element, state, isInertial: false);
        EndManipulators(element, state, cancel: false);
    }

    public static void StartInertia(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!s_states.TryGetValue(element, out ManipulationState? state))
            throw new InvalidOperationException("No active manipulation is associated with the element.");

        var preview = new ManipulationInertiaStartingEventArgs
        {
            RoutedEvent = UIElement.PreviewManipulationInertiaStartingEvent,
            ManipulationContainer = state.Container,
            InitialVelocities = new ManipulationVelocities(),
        };
        element.RaiseEvent(preview);
        if (!preview.Handled && !preview.CancelRequested)
        {
            var bubble = new ManipulationInertiaStartingEventArgs
            {
                RoutedEvent = UIElement.ManipulationInertiaStartingEvent,
                ManipulationContainer = state.Container,
                ManipulationOrigin = preview.ManipulationOrigin,
                InitialVelocities = preview.InitialVelocities,
                TranslationBehavior = preview.TranslationBehavior,
                RotationBehavior = preview.RotationBehavior,
                ExpansionBehavior = preview.ExpansionBehavior,
            };
            element.RaiseEvent(bubble);
            if (bubble.CancelRequested || bubble.CompleteRequested)
                CompleteManipulation(element);
        }
        else if (preview.CancelRequested || preview.CompleteRequested)
        {
            CompleteManipulation(element);
        }
    }

    public static bool IsManipulationActive(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!s_states.TryGetValue(element, out ManipulationState? state))
            return false;
        lock (state.Gate)
            return state.Manipulators.Count != 0;
    }

    public static IInputElement GetManipulationContainer(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_states.TryGetValue(element, out ManipulationState? state)
            ? state.Container
            : element;
    }

    public static void SetManipulationContainer(UIElement element, IInputElement container)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(container);
        s_states.GetValue(element, static owner => new(owner)).Container = container;
    }

    public static ManipulationModes GetManipulationMode(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_states.TryGetValue(element, out ManipulationState? state)
            ? state.Mode
            : ManipulationModes.All;
    }

    public static void SetManipulationMode(UIElement element, ManipulationModes mode)
    {
        ArgumentNullException.ThrowIfNull(element);
        const ManipulationModes supported =
            ManipulationModes.TranslateX | ManipulationModes.TranslateY |
            ManipulationModes.Rotate | ManipulationModes.Scale;
        if ((mode & ~supported) != 0)
            throw new System.ComponentModel.InvalidEnumArgumentException(
                nameof(mode), (int)mode, typeof(ManipulationModes));
        s_states.GetValue(element, static owner => new(owner)).Mode = mode;
    }

    public static ManipulationPivot? GetManipulationPivot(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_states.TryGetValue(element, out ManipulationState? state) ? state.Pivot : null;
    }

    public static void SetManipulationPivot(UIElement element, ManipulationPivot? pivot)
    {
        ArgumentNullException.ThrowIfNull(element);
        s_states.GetValue(element, static owner => new(owner)).Pivot = pivot;
    }

    [System.ComponentModel.Browsable(false)]
    public static void SetManipulationParameter(UIElement element, ManipulationParameters2D parameter)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(parameter);
        ManipulationState state = s_states.GetValue(element, static owner => new(owner));
        lock (state.Gate)
            state.Parameters.Add(parameter);
    }

    internal static IEnumerable<IManipulator> GetManipulators(UIElement element)
    {
        if (!s_states.TryGetValue(element, out ManipulationState? state))
            return [];
        lock (state.Gate)
            return state.Manipulators.ToArray();
    }

    private static void RaiseCompleted(UIElement element, ManipulationState state, bool isInertial)
    {
        var preview = new ManipulationCompletedEventArgs
        {
            RoutedEvent = UIElement.PreviewManipulationCompletedEvent,
            ManipulationContainer = state.Container,
            TotalManipulation = new ManipulationDelta(),
            FinalVelocities = new ManipulationVelocities(),
            IsInertial = isInertial,
        };
        element.RaiseEvent(preview);
        if (!preview.Handled)
        {
            element.RaiseEvent(new ManipulationCompletedEventArgs
            {
                RoutedEvent = UIElement.ManipulationCompletedEvent,
                ManipulationContainer = state.Container,
                TotalManipulation = preview.TotalManipulation,
                FinalVelocities = preview.FinalVelocities,
                IsInertial = isInertial,
            });
        }
    }

    private static void EndManipulators(UIElement element, ManipulationState state, bool cancel)
    {
        IManipulator[] manipulators;
        lock (state.Gate)
        {
            manipulators = state.Manipulators.ToArray();
            state.Manipulators.Clear();
        }
        foreach (IManipulator manipulator in manipulators)
            manipulator.ManipulationEnded(cancel);
        s_states.Remove(element);
    }

    private sealed class ManipulationState(UIElement owner)
    {
        public object Gate { get; } = new();
        public HashSet<IManipulator> Manipulators { get; } = [];
        public List<ManipulationParameters2D> Parameters { get; } = [];
        public IInputElement Container { get; set; } = owner;
        public ManipulationModes Mode { get; set; } = ManipulationModes.All;
        public ManipulationPivot? Pivot { get; set; }
    }
}
