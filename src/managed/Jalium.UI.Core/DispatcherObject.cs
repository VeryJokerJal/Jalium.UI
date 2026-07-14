using System.ComponentModel;

namespace Jalium.UI.Threading;

/// <summary>
/// Represents an object that is associated with a <see cref="Dispatcher"/>.
/// </summary>
public abstract class DispatcherObject
{
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherObject"/> class.
    /// </summary>
    protected DispatcherObject()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Gets the <see cref="Dispatcher"/> this <see cref="DispatcherObject"/> is associated with.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public Dispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Determines whether the calling thread has access to this <see cref="DispatcherObject"/>.
    /// </summary>
    /// <returns>true if the calling thread has access to this object; otherwise, false.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool CheckAccess()
    {
        return _dispatcher.CheckAccess();
    }

    /// <summary>
    /// Enforces that the calling thread has access to this <see cref="DispatcherObject"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread does not have access to this <see cref="DispatcherObject"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void VerifyAccess()
    {
        _dispatcher.VerifyAccess();
    }
}
