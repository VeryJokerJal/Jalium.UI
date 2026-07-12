namespace Jalium.UI.Media;

/// <summary>
/// Represents the rendering destination associated with a presentation source.
/// </summary>
/// <remarks>
/// Jalium's process-wide frame scheduler remains <see cref="Jalium.UI.CompositionTarget"/>.
/// This instance type models WPF's per-source composition target and deliberately forwards
/// the scheduler timing member used by existing media code.
/// </remarks>
public abstract partial class CompositionTarget : Jalium.UI.Threading.DispatcherObject, IDisposable
{
    private Visual? _rootVisual;
    private bool _disposed;

    /// <summary>Gets or sets the root visual rendered by this target.</summary>
    public virtual Visual? RootVisual
    {
        get => _rootVisual;
        set
        {
            ThrowIfDisposed();
            _rootVisual = value;
        }
    }

    /// <summary>Gets the transform from device pixels to device-independent pixels.</summary>
    public abstract Matrix TransformFromDevice { get; }

    /// <summary>Gets the transform from device-independent pixels to device pixels.</summary>
    public abstract Matrix TransformToDevice { get; }

    /// <summary>
    /// Gets Jalium's current process-wide frame interval.
    /// </summary>
    public static TimeSpan FrameInterval => Jalium.UI.CompositionTarget.FrameInterval;

    /// <summary>Releases resources owned by this composition target.</summary>
    public virtual void Dispose()
    {
        _rootVisual = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>Throws when a member is used after disposal.</summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
