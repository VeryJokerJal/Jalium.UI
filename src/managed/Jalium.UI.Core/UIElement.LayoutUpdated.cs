namespace Jalium.UI;

public partial class UIElement
{
    [ThreadStatic] private static int s_layoutNotificationDepth;
    [ThreadStatic] private static HashSet<UIElement>? s_deferredLayoutRoots;

    /// <summary>
    /// Coalesces LayoutUpdated notifications from synchronous UpdateLayout calls.
    /// Layout still runs immediately; each visual root is notified once on disposal.
    /// The scope must be disposed on its creating thread.
    /// </summary>
    public static IDisposable DeferLayoutUpdatedNotifications() => new LayoutNotificationScope();

    private static void PublishLayoutUpdated(UIElement root)
    {
        if (s_layoutNotificationDepth == 0) RaiseLayoutUpdatedRecursive(root);
        else (s_deferredLayoutRoots ??= new(ReferenceEqualityComparer.Instance)).Add(root);
    }

    private sealed class LayoutNotificationScope : IDisposable
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _disposed;
        public LayoutNotificationScope() => s_layoutNotificationDepth++;
        public void Dispose()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Layout notification scopes must be disposed on their creating thread.");
            _disposed = true;
            if (--s_layoutNotificationDepth != 0) return;
            var roots = s_deferredLayoutRoots;
            s_deferredLayoutRoots = null;
            if (roots is null) return;
            foreach (var root in roots) RaiseLayoutUpdatedRecursive(root);
        }
    }
}
