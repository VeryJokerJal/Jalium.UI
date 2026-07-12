namespace Jalium.UI;

/// <summary>
/// Connects a visual tree to the native presentation surface that reports input for it.
/// </summary>
/// <remarks>
/// Platform hosts derive from this type and keep the root visual and lifetime state in sync
/// with their native surface.  Input devices retain the source only while it is active.
/// </remarks>
public abstract class PresentationSource : DispatcherObject
{
    /// <summary>Gets the composition target used by this source.</summary>
    public Media.CompositionTarget? CompositionTarget => GetCompositionTargetCore();

    /// <summary>Gets or sets the root visual presented by this source.</summary>
    public abstract Visual? RootVisual { get; set; }

    /// <summary>Gets whether this source has been disposed.</summary>
    public abstract bool IsDisposed { get; }

    /// <summary>Gets the composition target supplied by the derived presentation source.</summary>
    protected abstract Media.CompositionTarget? GetCompositionTargetCore();
}
