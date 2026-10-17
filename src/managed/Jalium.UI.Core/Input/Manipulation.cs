using System.Runtime.CompilerServices;
using Jalium.UI.Input.Manipulations;

namespace Jalium.UI.Input;

// Native indirect gesture providers have no raw touch contacts. Connect them
// to the same imperative API without inventing IManipulator finger identities.
internal interface IManipulationSession
{
    bool IsActive { get; }
    IInputElement Container { get; set; }
    ManipulationModes Mode { get; set; }
    ManipulationPivot? Pivot { get; set; }
    void Complete();
    void StartInertia();
}

/// <summary>Provides imperative access to active manipulations.</summary>
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
        lock (state.Gate)
        {
            removed = state.Manipulators.Remove(manipulator);
        }
        if (removed)
            manipulator.ManipulationEnded(cancel: false);
        lock (state.Gate)
        {
            // Ended callbacks may add a contact or start a native session.
            if (state.Manipulators.Count == 0 && state.Session == null &&
                s_states.TryGetValue(element, out var current) && ReferenceEquals(current, state))
                s_states.Remove(element);
        }
    }

    public static void CompleteManipulation(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!s_states.TryGetValue(element, out ManipulationState? state))
            return;

        if (state.Session is { } session) { session.Complete(); return; }

        RaiseCompleted(element, state, isInertial: false);
        EndManipulators(element, state, cancel: false);
    }

    public static void StartInertia(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!s_states.TryGetValue(element, out ManipulationState? state))
            throw new InvalidOperationException("No active manipulation is associated with the element.");

        if (state.Session is { } session) { session.StartInertia(); return; }

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
            if (bubble.CancelRequested)
                CompleteManipulation(element);
        }
        else if (preview.CancelRequested)
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
            return state.Session?.IsActive == true || state.Manipulators.Count != 0;
    }

    public static IInputElement GetManipulationContainer(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_states.TryGetValue(element, out ManipulationState? state)
            ? state.Session?.Container ?? state.Container
            : element;
    }

    public static void SetManipulationContainer(UIElement element, IInputElement container)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(container);
        var state = s_states.GetValue(element, static owner => new(owner));
        if (state.Session is { } session) session.Container = container;
        state.Container = container;
    }

    public static ManipulationModes GetManipulationMode(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_states.TryGetValue(element, out ManipulationState? state)
            ? state.Session?.Mode ?? state.Mode
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
        var state = s_states.GetValue(element, static owner => new(owner));
        if (state.Session is { } session) session.Mode = mode;
        state.Mode = mode;
    }

    public static ManipulationPivot? GetManipulationPivot(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_states.TryGetValue(element, out ManipulationState? state) ? state.Session?.Pivot ?? state.Pivot : null;
    }

    public static void SetManipulationPivot(UIElement element, ManipulationPivot? pivot)
    {
        ArgumentNullException.ThrowIfNull(element);
        var state = s_states.GetValue(element, static owner => new(owner));
        if (state.Session is { } session) session.Pivot = pivot;
        state.Pivot = pivot;
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

    internal static bool TryAttachSession(UIElement element, IManipulationSession session)
    {
        var state = s_states.GetValue(element, static owner => new(owner));
        lock (state.Gate)
        {
            if (state.Session is not null || state.Manipulators.Count != 0) return false;
            state.Session = session;
            return true;
        }
    }

    internal static void DetachSession(UIElement element, IManipulationSession session)
    {
        if (!s_states.TryGetValue(element, out var state)) return;
        lock (state.Gate)
            if (ReferenceEquals(state.Session, session)) state.Session = null;
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
        public IManipulationSession? Session { get; set; }
        public HashSet<IManipulator> Manipulators { get; } = [];
        public List<ManipulationParameters2D> Parameters { get; } = [];
        public IInputElement Container { get; set; } = owner;
        public ManipulationModes Mode { get; set; } = ManipulationModes.All;
        public ManipulationPivot? Pivot { get; set; }
    }
}
