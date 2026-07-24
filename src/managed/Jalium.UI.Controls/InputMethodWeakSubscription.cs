using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Bridges the process-wide input-method composition events to a control
/// without allowing those static events to keep an unloaded visual tree alive.
/// </summary>
internal sealed class InputMethodWeakSubscription<TOwner>
    where TOwner : class
{
    private readonly WeakReference<TOwner> _owner;
    private readonly Action<TOwner, object?, EventArgs> _started;
    private readonly Action<TOwner, object?, CompositionEventArgs> _updated;
    private readonly Action<TOwner, object?, CompositionResultEventArgs> _ended;
    private bool _isAttached;

    public InputMethodWeakSubscription(
        TOwner owner,
        Action<TOwner, object?, EventArgs> started,
        Action<TOwner, object?, CompositionEventArgs> updated,
        Action<TOwner, object?, CompositionResultEventArgs> ended)
    {
        _owner = new WeakReference<TOwner>(owner);
        _started = started;
        _updated = updated;
        _ended = ended;
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        Registry.Add(this);
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _isAttached = false;
        Registry.Remove(this);
    }

    private void DispatchCompositionStarted(object? sender, EventArgs e)
    {
        if (_owner.TryGetTarget(out var owner))
        {
            _started(owner, sender, e);
        }
        else
        {
            Detach();
        }
    }

    private void DispatchCompositionUpdated(object? sender, CompositionEventArgs e)
    {
        if (_owner.TryGetTarget(out var owner))
        {
            _updated(owner, sender, e);
        }
        else
        {
            Detach();
        }
    }

    private void DispatchCompositionEnded(object? sender, CompositionResultEventArgs e)
    {
        if (_owner.TryGetTarget(out var owner))
        {
            _ended(owner, sender, e);
        }
        else
        {
            Detach();
        }
    }

    /// <summary>
    /// One process-wide bridge per closed owner type keeps the static input
    /// event invocation lists constant-sized. New attachments and composition
    /// events prune subscriptions whose weak owner has already disappeared.
    /// </summary>
    private static class Registry
    {
        private static readonly object Gate = new();
        private static readonly List<InputMethodWeakSubscription<TOwner>> Subscriptions = [];

        static Registry()
        {
            InputMethod.CompositionStarted += OnCompositionStarted;
            InputMethod.CompositionUpdated += OnCompositionUpdated;
            InputMethod.CompositionEnded += OnCompositionEnded;
        }

        public static void Add(InputMethodWeakSubscription<TOwner> subscription)
        {
            lock (Gate)
            {
                PruneDeadSubscriptions();
                if (!Subscriptions.Contains(subscription))
                {
                    Subscriptions.Add(subscription);
                }
            }
        }

        public static void Remove(InputMethodWeakSubscription<TOwner> subscription)
        {
            lock (Gate)
            {
                Subscriptions.Remove(subscription);
            }
        }

        private static void OnCompositionStarted(object? sender, EventArgs e)
        {
            foreach (var subscription in Snapshot())
            {
                subscription.DispatchCompositionStarted(sender, e);
            }
        }

        private static void OnCompositionUpdated(object? sender, CompositionEventArgs e)
        {
            foreach (var subscription in Snapshot())
            {
                subscription.DispatchCompositionUpdated(sender, e);
            }
        }

        private static void OnCompositionEnded(object? sender, CompositionResultEventArgs e)
        {
            foreach (var subscription in Snapshot())
            {
                subscription.DispatchCompositionEnded(sender, e);
            }
        }

        private static InputMethodWeakSubscription<TOwner>[] Snapshot()
        {
            lock (Gate)
            {
                PruneDeadSubscriptions();
                return Subscriptions.ToArray();
            }
        }

        private static void PruneDeadSubscriptions()
        {
            Subscriptions.RemoveAll(static subscription =>
                !subscription._isAttached || !subscription._owner.TryGetTarget(out _));
        }
    }
}
