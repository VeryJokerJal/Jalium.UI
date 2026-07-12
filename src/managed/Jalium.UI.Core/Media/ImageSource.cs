using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI.Media;

/// <summary>
/// Represents the source of an image.
/// </summary>
public abstract class ImageSource : Animation.Animatable, IFormattable
{
    private Exception? _loadFailure;

    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    public abstract double Width { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    public abstract double Height { get; }

    /// <summary>
    /// Gets the native handle of the image (platform-specific).
    /// </summary>
    public abstract nint NativeHandle { get; }

    /// <summary>Gets image metadata when the source exposes any.</summary>
    public abstract ImageMetadata? Metadata { get; }

    /// <summary>Converts a physical pixel count at the specified DPI to device-independent units.</summary>
    protected static double PixelsToDIPs(double dpi, int pixels) =>
        dpi > 0 && double.IsFinite(dpi) ? pixels * 96d / dpi : pixels;

    /// <summary>Creates a modifiable copy of this image source.</summary>
    public new ImageSource Clone() => (ImageSource)base.Clone();

    /// <summary>Creates a modifiable copy using the current dependency-property values.</summary>
    public new ImageSource CloneCurrentValue() => (ImageSource)base.CloneCurrentValue();

    public override string ToString() => ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string ToString(IFormatProvider? provider) => GetType().Name;
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);

    /// <summary>
    /// Provides a compatibility factory for existing custom image-source implementations.
    /// Framework image sources with stateful construction override this method explicitly.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "This fallback only supports compatibility ImageSource implementations with a parameterless constructor; stateful framework types provide an explicit factory override.")]
    protected override Freezable CreateInstanceCore()
    {
        if (Activator.CreateInstance(GetType(), nonPublic: true) is Freezable instance)
        {
            return instance;
        }

        throw new InvalidOperationException($"Image source type '{GetType().FullName}' cannot be cloned.");
    }

    /// <summary>
    /// Raised internally when loading or decoding this source fails.
    /// </summary>
    internal event Action<ImageSource, Exception>? LoadFailed;

    /// <summary>Gets the most recent unresolved load failure.</summary>
    internal Exception? LoadFailure => _loadFailure;

    /// <summary>Reports a source loading or decoding failure.</summary>
    protected void ReportLoadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _loadFailure = exception;
        LoadFailed?.Invoke(this, exception);
    }

    /// <summary>Clears a previously reported failure after a successful load.</summary>
    protected void ClearLoadFailure() => _loadFailure = null;

    /// <summary>
    /// Raised when an image source wants every GPU-side bitmap cache (one per
    /// active <c>RenderTargetDrawingContext</c>) to drop its cached upload of
    /// the source so the underlying <c>NativeBitmap</c> texture is released.
    /// Used by the idle-resource reclaimer when an <c>IReclaimableResource</c>
    /// element decides its source has been off-screen long enough to free GPU
    /// memory; the upload is rebuilt from the bitmap's raw or encoded data on
    /// the next render.
    /// </summary>
    /// <remarks>
    /// Each <c>RenderTargetDrawingContext</c> subscribes in its constructor and
    /// unsubscribes when the context closes. Handlers run synchronously on the
    /// thread that raised the event — typically the UI thread — and must be
    /// allocation-free; the source is the <see cref="ImageSource"/> whose GPU
    /// upload should be dropped.
    /// </remarks>
    internal static event Action<ImageSource>? GpuCacheEvictionRequested;

    /// <summary>
    /// Asks every subscribed bitmap cache to drop its GPU upload of
    /// <paramref name="source"/>. No-op when nothing is subscribed.
    /// </summary>
    internal static void RaiseGpuCacheEviction(ImageSource source)
    {
        var handler = GpuCacheEvictionRequested;
        if (handler != null)
        {
            handler(source);
        }
    }
}
