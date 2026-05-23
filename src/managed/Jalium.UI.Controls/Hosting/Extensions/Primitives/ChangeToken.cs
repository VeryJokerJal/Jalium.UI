namespace Jalium.Extensions.Primitives;

/// <summary>
/// Notification primitive for configuration / file-watcher subsystems. Roughly the
/// Jalium equivalent of <c>Microsoft.Extensions.Primitives.IChangeToken</c>.
/// </summary>
public interface IChangeToken
{
    bool HasChanged { get; }
    bool ActiveChangeCallbacks { get; }
    IDisposable RegisterChangeCallback(Action<object?> callback, object? state);
}

/// <summary>Static helpers that mirror <c>ChangeToken.OnChange</c>.</summary>
public static class ChangeToken
{
    public static IDisposable OnChange(Func<IChangeToken> producer, Action consumer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(consumer);
        return new ChangeTokenRegistration<object?>(producer, state => consumer(), null);
    }

    public static IDisposable OnChange<TState>(Func<IChangeToken> producer, Action<TState> consumer, TState state)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(consumer);
        return new ChangeTokenRegistration<TState>(producer, consumer, state);
    }

    private sealed class ChangeTokenRegistration<TState> : IDisposable
    {
        private readonly Func<IChangeToken> _producer;
        private readonly Action<TState> _consumer;
        private readonly TState _state;
        private IDisposable? _registration;
        private bool _disposed;

        public ChangeTokenRegistration(Func<IChangeToken> producer, Action<TState> consumer, TState state)
        {
            _producer = producer;
            _consumer = consumer;
            _state = state;
            RegisterNext();
        }

        private void RegisterNext()
        {
            if (_disposed) return;
            var token = _producer();
            _registration = token.RegisterChangeCallback(static (s) =>
            {
                var self = (ChangeTokenRegistration<TState>)s!;
                try { self._consumer(self._state); }
                finally { self.RegisterNext(); }
            }, this);
        }

        public void Dispose()
        {
            _disposed = true;
            _registration?.Dispose();
        }
    }
}

/// <summary>A change-token that has already fired. Useful when reloading immediately.</summary>
public sealed class CancellationChangeToken : IChangeToken
{
    private readonly CancellationToken _token;
    public CancellationChangeToken(CancellationToken token) { _token = token; }
    public bool HasChanged => _token.IsCancellationRequested;
    public bool ActiveChangeCallbacks => true;
    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => _token.Register(callback, state);
}
