using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI;

public partial class Window
{
    private MacOSManipulationSession? _macOSManipulation;
    private ulong _macOSLastGestureSequence;
    private long _macOSGestureDispatch;
    private const ManipulationModes MacOSGestureModes = ManipulationModes.Scale | ManipulationModes.Rotate;

    private bool MacOSGestureWindowAvailable => OperatingSystem.IsMacOS() &&
        _platformWindow != null && Handle != 0 && !_isClosing && !_managedTeardownStarted &&
        !_managedTeardownCompleted && double.IsFinite(_dpiScale) && _dpiScale > 0 && IsEnabled && IsVisible && IsHitTestVisible && WindowState != WindowState.Minimized;

    private sealed class GestureRoute
    {
        internal readonly List<Visual> Nodes;
        private readonly ulong[] _parents;
        private readonly (Popup Popup, long Revision)[] _popups;
        internal GestureRoute(UIElement element, Visual boundary)
        {
            Nodes = InputRoute.Capture(element, boundary);
            _parents = Nodes.Select(v => v.InputParentRevision).ToArray();
            _popups = Nodes.OfType<Popup>().Select(p => (p, p.MacOSInteractionRevision)).ToArray();
        }
        internal bool IsCurrent => Nodes.Count != 0 && InputRoute.IsCurrent(Nodes) &&
            Nodes.Select((node, index) => node.InputParentRevision == _parents[index]).All(value => value) &&
            _popups.All(pair => pair.Popup.MacOSInteractionRevision == pair.Revision);
    }

    private sealed class MacOSManipulationSession : IManipulationSession
    {
        internal readonly Window Owner;
        internal readonly UIElement Target;
        internal readonly GestureRoute Route;
        internal readonly IPlatformWindow Platform;
        internal readonly nint Handle;
        internal readonly ulong Sequence;
        internal readonly long Lifecycle;
        internal readonly ulong ModalRevision;
        internal readonly MouseCommandInput Input;
        internal uint Kinds;
        internal bool Started, Ended, Inertial, HasAngularSample;
        internal double LastAngularTime;
        internal Point LastWindowPoint, LastOrigin;
        internal ManipulationDelta Total = GestureDelta(1, 0);
        internal ManipulationVelocities Velocities = new();
        internal ManipulationInertiaProcessor? Inertia;
        internal GestureRoute ContainerRoute = null!;
        private UIElement _container = null!;
        private readonly List<(UIElement Node, Action<DependencyProperty, object?, object?> Handler)> _subscriptions = [];

        internal MacOSManipulationSession(Window owner, UIElement target, ulong sequence)
        {
            Owner = owner; Target = target; Sequence = sequence;
            Platform = owner._platformWindow!; Handle = owner.Handle; Lifecycle = owner._renderLifecycleGeneration;
            Route = new(target, owner.MacOSInputOwner);
            ModalRevision = owner.MacOSInputOwner._modalInputRevision;
            Input = ((IInputDispatcherHost)owner).CaptureNativeMouseCommandInput(target, null)
                ?? throw new InvalidOperationException("Native gesture input has no current host.");
            Container = Manipulation.GetManipulationContainer(target);
            Mode = Manipulation.GetManipulationMode(target) & MacOSGestureModes;
            Pivot = Manipulation.GetManipulationPivot(target);
        }

        public bool IsActive => Started && Current;
        private bool SameContext => !Ended && ReferenceEquals(Owner._macOSManipulation, this) &&
            ReferenceEquals(Owner._platformWindow, Platform) && Owner.Handle == Handle &&
            Owner._renderLifecycleGeneration == Lifecycle;
        internal bool Current
        {
            get
            {
                if (!SameContext) return false;
                // IsEnabledCore and input-route providers may execute consumer
                // code. Recheck identity after all eligibility reads.
                bool available = Owner.MacOSGestureWindowAvailable && Target.IsManipulationEnabled &&
                    ModalRevision == Owner.MacOSInputOwner._modalInputRevision && Input.IsCurrent() &&
                    Route.IsCurrent && ContainerRoute.IsCurrent &&
                    Owner.MacOSInputOwner.IsMacOSInputTargetValid(Target) &&
                    InputRoute.IsWithin(_container, Owner.MacOSInputOwner) &&
                    Route.Nodes.OfType<UIElement>().All(node => node.IsHitTestVisible);
                return available && SameContext;
            }
        }

        public IInputElement Container
        {
            get => _container;
            set
            {
                if (value is not UIElement container || !InputRoute.IsWithin(container, Owner.MacOSInputOwner))
                    throw new ArgumentException("The manipulation container must belong to the input window.", nameof(value));
                var route = new GestureRoute(container, Owner.MacOSInputOwner);
                if (!route.IsCurrent) throw new ArgumentException("The manipulation container is detached.", nameof(value));
                if (Started)
                {
                    var origin = TranslateMacOSGesturePoint(LastOrigin, _container, container);
                    if (!Finite(origin)) throw new ArgumentException("The manipulation container has no invertible coordinates.", nameof(value));
                    LastOrigin = origin;
                }
                _container = container; ContainerRoute = route;
            }
        }
        public ManipulationModes Mode { get; set; }
        public ManipulationPivot? Pivot { get; set; }
        public void Complete() => Owner.FinishMacOSManipulation(this);
        public void StartInertia() => Owner.StartMacOSGestureInertia(this);

        internal void Watch()
        {
            foreach (var node in Route.Nodes.OfType<UIElement>().Append(Owner).Distinct())
            {
                Action<DependencyProperty, object?, object?> changed = (property, _, value) =>
                {
                    bool disqualifies = property == IsEnabledProperty && value is false ||
                        property == VisibilityProperty && value is Visibility visibility && visibility != Visibility.Visible ||
                        property == IsHitTestVisibleProperty && value is false ||
                        ReferenceEquals(node, Target) && property == IsManipulationEnabledProperty && value is false ||
                        node is Window && property == IsActiveProperty && value is false;
                    if (disqualifies || !Current) Owner.FinishMacOSManipulation(this);
                };
                node.PropertyChangedInternal += changed;
                _subscriptions.Add((node, changed));
            }
        }
        internal void Unwatch()
        {
            foreach (var item in _subscriptions) item.Node.PropertyChangedInternal -= item.Handler;
            _subscriptions.Clear();
        }
    }

    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static ManipulationDelta GestureDelta(double scale, double rotation) =>
        new(Vector.Zero, rotation, new Vector(scale, scale), Vector.Zero);

    private Point GetMacOSGestureOrigin(Point windowPoint, UIElement container) =>
        TranslateMacOSGesturePoint(windowPoint, this, container);

    private static Point TranslateMacOSGesturePoint(Point point, UIElement source, UIElement target)
    {
        var from = GetMacOSPhysicalWindow(source);
        var to = GetMacOSPhysicalWindow(target);
        if (from == null || to == null) return new(double.NaN, double.NaN);
        Point local = source.TranslatePoint(point, from);
        if (!ReferenceEquals(from, to)) local = to.PointFromScreen(from.PointToScreen(local));
        return to.TranslatePoint(local, target);
    }

    private T RaiseMacOSGesturePair<T>(MacOSManipulationSession session, long revision,
        Func<bool, T?, T> create, Func<T, bool>? stop = null, bool completing = false) where T : InputEventArgs
    {
        bool Current() => (completing
            ? !_managedTeardownCompleted && session.Route.IsCurrent
            : session.Current) && revision == _macOSGestureDispatch;
        var preview = create(true, null);
        if (!Current()) return preview;
        InputRoute.Raise(session.Target, preview, Current);
        if (!Current() || preview.Handled || stop?.Invoke(preview) == true) return preview;
        var bubble = create(false, preview);
        InputRoute.Raise(session.Target, bubble, Current);
        return bubble;
    }

    private void HandleMacOSGesture(MacOSGestureRequest request)
    {
        var sample = request.Snapshot;
        if (!OperatingSystem.IsMacOS() || sample.Size != 64 || sample.Sequence == 0 || sample.Phase is < 1 or > 4 ||
            sample.Kind is < 0 or > 2) return;
        var session = _macOSManipulation;
        if (sample.Phase != 1 && (session == null || session.Sequence != sample.Sequence)) return;
        if (sample.Phase == 1 && session?.Sequence != sample.Sequence && sample.Sequence <= _macOSLastGestureSequence) return;
        long revision = ++_macOSGestureDispatch;
        if (!double.IsFinite(sample.X) || !double.IsFinite(sample.Y) || !double.IsFinite(sample.Scale) || sample.Scale <= 0 ||
            !double.IsFinite(sample.Rotation) || !double.IsFinite(sample.Timestamp) || sample.Timestamp < 0)
        {
            if (session?.Sequence == sample.Sequence) FinishMacOSManipulation(session);
            return;
        }
        try
        {
            if (session != null && session.Sequence != sample.Sequence)
            {
                FinishMacOSManipulation(session);
                // A completion callback may begin a newer gesture.
                if (_macOSManipulation != null || !MacOSGestureWindowAvailable) return;
                revision = ++_macOSGestureDispatch;
                session = null;
            }
            if (session == null)
            {
                if (sample.Phase != 1 || sample.Kind == 0 || !MacOSGestureWindowAvailable) return;
                var position = new Point(sample.X / _dpiScale, sample.Y / _dpiScale);
                var hit = HitTestElement(position, "macos-gesture");
                if (!MacOSGestureWindowAvailable || revision != _macOSGestureDispatch || hit == null) return;
                var target = InputRoute.Capture(hit, MacOSInputOwner).OfType<UIElement>()
                    .FirstOrDefault(element => element.IsManipulationEnabled);
                if (target == null || !MacOSInputOwner.IsMacOSInputTargetValid(target) ||
                    revision != _macOSGestureDispatch || _macOSManipulation != null) return;
                session = new(this, target, sample.Sequence);
                // Eligibility getters and input-host capture can acquire a
                // newer gesture on a different target before attachment.
                if (revision != _macOSGestureDispatch || _macOSManipulation != null) return;
                if (!Manipulation.TryAttachSession(target, session)) return;
                _macOSManipulation = session;
                _macOSLastGestureSequence = sample.Sequence;
                session.Watch();
                var starting = RaiseMacOSGesturePair(session, revision, (bool preview, ManipulationStartingEventArgs? previous) => new()
                {
                    RoutedEvent = preview ? PreviewManipulationStartingEvent : ManipulationStartingEvent,
                    ManipulationContainer = previous is null ? session.Container : previous.ManipulationContainer ?? session.Target,
                    Mode = previous?.Mode ?? session.Mode,
                    Pivot = previous is null ? session.Pivot : previous.Pivot
                }, args => args.CancelRequested);
                if (!session.Current || revision != _macOSGestureDispatch) { FinishMacOSManipulation(session); return; }
                if (starting.CancelRequested || (starting.Mode & MacOSGestureModes) == 0)
                { FinishMacOSManipulation(session); return; }
                session.Container = starting.ManipulationContainer ?? session.Target;
                session.Mode = starting.Mode; session.Pivot = starting.Pivot;
                session.LastWindowPoint = position;
                session.LastOrigin = GetMacOSGestureOrigin(position, (UIElement)session.Container);
                if (!Finite(session.LastOrigin) || !session.Current) { FinishMacOSManipulation(session); return; }
            }
            // AppKit shares one sequence across magnification and rotation.
            // Claim it while an enabled kind can still join, without exposing
            // an active manipulation until that kind actually arrives.
            request.Handled = true;
            if (!session.Current) { FinishMacOSManipulation(session); return; }
            if (revision != _macOSGestureDispatch || session.Inertial) return;
            if (sample.Phase == 4 || (session.Mode & MacOSGestureModes) == 0)
            { FinishMacOSManipulation(session); return; }
            uint bit = sample.Kind == 0 ? 0u : 1u << (sample.Kind - 1);
            if (sample.Phase == 1) session.Kinds |= bit;
            var kindMode = sample.Kind == 1 ? ManipulationModes.Scale : sample.Kind == 2 ? ManipulationModes.Rotate : ManipulationModes.None;
            if (!session.Started && sample.Phase is 1 or 2 && (session.Mode & kindMode) != 0)
            {
                var position = new Point(sample.X / _dpiScale, sample.Y / _dpiScale);
                session.LastOrigin = GetMacOSGestureOrigin(position, (UIElement)session.Container);
                session.LastWindowPoint = position;
                if (!Finite(session.LastOrigin) || !session.Current) { FinishMacOSManipulation(session); return; }
                if (revision != _macOSGestureDispatch) return;
                session.Started = true;
                var started = RaiseMacOSGesturePair(session, revision, (bool preview, ManipulationStartedEventArgs? _) => new()
                {
                    RoutedEvent = preview ? PreviewManipulationStartedEvent : ManipulationStartedEvent,
                    ManipulationContainer = session.Container, ManipulationOrigin = session.LastOrigin
                }, args => args.CancelRequested || args.CompleteRequested);
                if (started.CancelRequested || started.CompleteRequested) { FinishMacOSManipulation(session); return; }
            }
            if (!session.Current) { FinishMacOSManipulation(session); return; }
            if (revision != _macOSGestureDispatch || session.Inertial) return;
            if (session.Started && sample.Kind != 0 && !ApplyMacOSGestureDelta(session, sample, revision)) return;
            if (sample.Phase == 3)
            {
                session.Kinds &= ~bit;
                if (sample.Kind == 0 || session.Kinds == 0 && (sample.Flags & 1) == 0) FinishMacOSManipulation(session);
            }
        }
        catch
        {
            if (session != null && ReferenceEquals(_macOSManipulation, session) && revision == _macOSGestureDispatch)
                FinishMacOSManipulation(session, raiseCompletion: false);
            throw;
        }
    }

    private bool ApplyMacOSGestureDelta(MacOSManipulationSession session, MacOSGestureSnapshot sample, long revision)
    {
        double scale = (session.Mode & ManipulationModes.Scale) != 0 ? sample.Scale : 1;
        double rotation = (session.Mode & ManipulationModes.Rotate) != 0 ? sample.Rotation : 0;
        if (sample.Kind == 2 && sample.Phase == 1 && rotation == 0 && (session.Mode & ManipulationModes.Rotate) != 0)
        {
            session.LastAngularTime = sample.Timestamp;
            session.HasAngularSample = true;
        }
        if (scale == 1 && rotation == 0) return true;
        var position = new Point(sample.X / _dpiScale, sample.Y / _dpiScale);
        var container = (UIElement)session.Container;
        Point origin = GetMacOSGestureOrigin(position, container);
        if (rotation != 0)
        {
            var horizontal = GetMacOSGestureOrigin(new(position.X + 1, position.Y), container) - origin;
            var vertical = GetMacOSGestureOrigin(new(position.X, position.Y + 1), container) - origin;
            double determinant = horizontal.X * vertical.Y - horizontal.Y * vertical.X;
            if (!double.IsFinite(determinant) || determinant == 0) { FinishMacOSManipulation(session); return false; }
            rotation *= Math.Sign(determinant);
        }
        double totalScale = session.Total.Scale.X * scale;
        double totalRotation = session.Total.Rotation + rotation;
        if (!Finite(origin) || !double.IsFinite(totalScale) || totalScale <= 0 || !double.IsFinite(totalRotation))
        { FinishMacOSManipulation(session); return false; }
        if (!session.Current) { FinishMacOSManipulation(session); return false; }
        if (revision != _macOSGestureDispatch) return false;
        double angular = (session.Mode & ManipulationModes.Rotate) != 0 ? session.Velocities.AngularVelocity : 0;
        if (rotation != 0)
        {
            double dt = Math.Max(1, (sample.Timestamp - session.LastAngularTime) * 1000);
            angular = session.HasAngularSample ? rotation / dt : 0;
            session.LastAngularTime = sample.Timestamp; session.HasAngularSample = true;
        }
        var velocities = new ManipulationVelocities
        {
            LinearVelocity = session.Velocities.LinearVelocity,
            AngularVelocity = angular,
            ExpansionVelocity = session.Velocities.ExpansionVelocity
        };
        session.LastOrigin = origin; session.LastWindowPoint = position;
        session.Total = GestureDelta(totalScale, totalRotation); session.Velocities = velocities;
        var delta = GestureDelta(scale, rotation);
        var args = RaiseMacOSGesturePair(session, revision, (bool preview, ManipulationDeltaEventArgs? previous) =>
        {
            var value = new ManipulationDeltaEventArgs
            {
            RoutedEvent = preview ? PreviewManipulationDeltaEvent : ManipulationDeltaEvent,
            ManipulationContainer = container, ManipulationOrigin = origin,
            DeltaManipulation = delta, CumulativeManipulation = session.Total,
            Velocities = velocities, IsInertial = false
            };
            if (previous?.UnusedManipulation is { } unused) value.ReportBoundaryFeedback(unused);
            return value;
        }, value => value.CancelRequested || value.CompleteRequested);
        if (!session.Current) { FinishMacOSManipulation(session); return false; }
        if (revision != _macOSGestureDispatch) return false;
        if (args.StartInertiaRequested) { StartMacOSGestureInertia(session); return false; }
        if (args.CancelRequested || args.CompleteRequested) { FinishMacOSManipulation(session); return false; }
        if (args.UnusedManipulation is { } unused)
            _ = RaiseMacOSGesturePair(session, revision, (bool preview, ManipulationBoundaryFeedbackEventArgs? _) => new()
            {
                RoutedEvent = preview ? PreviewManipulationBoundaryFeedbackEvent : ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = session.Container, BoundaryFeedback = unused
            });
        if (!session.Current) { FinishMacOSManipulation(session); return false; }
        return revision == _macOSGestureDispatch;
    }

    private void StartMacOSGestureInertia(MacOSManipulationSession session)
    {
        if (!session.Current) return;
        if (!session.Started) throw new InvalidOperationException("The manipulation has not started.");
        if (session.Inertial) return;
        long revision = ++_macOSGestureDispatch;
        var starting = RaiseMacOSGesturePair(session, revision, (bool preview, ManipulationInertiaStartingEventArgs? previous) => new()
        {
            RoutedEvent = preview ? PreviewManipulationInertiaStartingEvent : ManipulationInertiaStartingEvent,
            ManipulationContainer = session.Container, ManipulationOrigin = previous?.ManipulationOrigin ?? session.LastOrigin,
            InitialVelocities = session.Velocities,
            TranslationBehavior = previous?.TranslationBehavior ?? new InertiaTranslationBehavior(session.Velocities.LinearVelocity),
            RotationBehavior = previous?.RotationBehavior ?? new InertiaRotationBehavior(session.Velocities.AngularVelocity),
            ExpansionBehavior = previous?.ExpansionBehavior ?? new InertiaExpansionBehavior(session.Velocities.ExpansionVelocity)
        }, args => args.CancelRequested);
        if (!session.Current || revision != _macOSGestureDispatch) return;
        if (starting.CancelRequested) { FinishMacOSManipulation(session); return; }
        if (!Finite(starting.ManipulationOrigin)) { FinishMacOSManipulation(session); return; }
        session.LastOrigin = starting.ManipulationOrigin;
        session.Inertia = new(session.Target, session.LastOrigin, session.Total, Dispatcher.CurrentDispatcher,
            lifetime: () => session.Current,
            coordinates: () => (session.Container, session.LastOrigin),
            completion: (total, velocities) =>
            {
                session.Total = total; session.Velocities = velocities;
                FinishMacOSManipulation(session);
            }, faulted: () => FinishMacOSManipulation(session, raiseCompletion: false));
        session.Inertial = session.Inertia.Start(Vector.Zero, session.Velocities.AngularVelocity, Vector.Zero,
            starting.TranslationBehavior, starting.RotationBehavior, starting.ExpansionBehavior);
        if (!session.Inertial) FinishMacOSManipulation(session);
    }

    private void FinishMacOSManipulation(MacOSManipulationSession session, bool raiseCompletion = true)
    {
        if (!ReferenceEquals(_macOSManipulation, session)) return;
        _macOSManipulation = null;
        long revision = ++_macOSGestureDispatch;
        session.Ended = true;
        session.Unwatch();
        Manipulation.DetachSession(session.Target, session);
        if (session.Inertia is { } inertia)
        {
            session.Total = inertia.Cumulative; session.Velocities = inertia.CurrentVelocities;
            inertia.Cancel();
        }
        if (!session.Started || !raiseCompletion) return;
        _ = RaiseMacOSGesturePair(session, revision, (bool preview, ManipulationCompletedEventArgs? _) => new()
        {
            RoutedEvent = preview ? PreviewManipulationCompletedEvent : ManipulationCompletedEvent,
            ManipulationContainer = session.Container, ManipulationOrigin = session.LastOrigin,
            TotalManipulation = session.Total, FinalVelocities = session.Velocities, IsInertial = session.Inertial
        }, completing: true);
    }
}
