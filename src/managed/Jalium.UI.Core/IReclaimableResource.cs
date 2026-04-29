namespace Jalium.UI;

/// <summary>
/// Implemented by visuals (or anything reachable from one) that own GPU / pixel
/// buffers, decoded media frames, native handles, or other heavy resources that
/// can be safely re-acquired on demand. The framework's idle-resource reclaimer
/// (opted in via <c>app.UseIdleResourceReclamation()</c>) calls
/// <see cref="ReclaimIdleResources"/> after the visual has gone unrendered for
/// the configured idle window — typically because it is
/// <see cref="Visibility.Collapsed"/>/<see cref="Visibility.Hidden"/>, scrolled
/// out of the viewport, or hosted in a window that is no longer being painted.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be idempotent: <see cref="ReclaimIdleResources"/> may be
/// invoked repeatedly while the element stays idle. They must NOT throw on
/// re-entry, and must make the next render pass succeed (re-decoding, re-uploading,
/// or otherwise re-acquiring whatever was released).
/// </para>
/// <para>
/// "Idle" means the visual's <c>Render</c> method has not been entered for at
/// least <c>IdleTimeoutMs</c> milliseconds. That covers the common cases —
/// hidden / collapsed / clipped out of the viewport — without the implementer
/// having to subscribe to layout or visibility events.
/// </para>
/// </remarks>
public interface IReclaimableResource
{
    /// <summary>
    /// Release any cached pixel buffers, decoded media frames, GPU uploads, or
    /// other heavy resources that can be re-acquired the next time the element
    /// renders. Called on the UI thread by the reclaimer after the element has
    /// stayed unrendered for the configured idle window.
    /// </summary>
    void ReclaimIdleResources();
}
