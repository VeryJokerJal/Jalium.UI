using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Provides weak subscriptions to <see cref="ICommand.CanExecuteChanged"/>.
/// </summary>
public sealed class CanExecuteChangedEventManager : WeakEventManager
{
    private static CanExecuteChangedEventManager CurrentManager
    {
        get
        {
            var manager = (CanExecuteChangedEventManager?)GetCurrentManager(
                typeof(CanExecuteChangedEventManager));
            if (manager is not null)
                return manager;

            manager = new CanExecuteChangedEventManager();
            SetCurrentManager(typeof(CanExecuteChangedEventManager), manager);
            return manager;
        }
    }

    private CanExecuteChangedEventManager()
    {
    }

    public static void AddHandler(ICommand source, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedAddHandler(source, handler);
    }

    public static void RemoveHandler(ICommand source, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedRemoveHandler(source, handler);
    }

    public static void AddListener(ICommand source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedAddListener(source, listener);
    }

    public static void RemoveListener(ICommand source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedRemoveListener(source, listener);
    }

    protected override void StartListening(object source) =>
        ((ICommand)source).CanExecuteChanged += OnCanExecuteChanged;

    protected override void StopListening(object source) =>
        ((ICommand)source).CanExecuteChanged -= OnCanExecuteChanged;

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        if (sender is not null)
            DeliverEvent(sender, e);
    }
}
