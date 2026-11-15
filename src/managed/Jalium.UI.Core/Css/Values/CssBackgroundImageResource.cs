using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>Fetches only a selected image; each dispatcher retains its own live animation/decode source.</summary>
internal sealed class CssBackgroundImageResource(Uri? sourceUri, ICssResourceResolver resourceResolver,
    Uri? referrer = null, bool requirePixelAccess = false, bool allowSvgMaskReference = false)
{
    private static readonly ConditionalWeakTable<ImageSource, WeakReference<CssBackgroundImageResource>> SvgMaskUsers = new();
    static CssBackgroundImageResource()
    {
        ImageSource.RasterChanged += image =>
        {
            if (SvgMaskUsers.TryGetValue(image, out var reference) && reference.TryGetTarget(out var resource))
                resource.NotifyUsers(pixelsOnly: true, image.Dispatcher);
        };
    }
    private readonly Uri? _source = sourceUri;
    private readonly ICssResourceResolver _resolver = resourceResolver;
    private readonly Uri? _referrer = referrer;
    private readonly bool _requirePixelAccess = requirePixelAccess;
    private string? MaskId => allowSvgMaskReference && _source is { } source &&
        (source.IsAbsoluteUri ? source.Fragment : source.OriginalString.Contains('#') ? source.OriginalString[source.OriginalString.IndexOf('#')..] : "") is { Length: > 0 } fragment
            ? Uri.UnescapeDataString(fragment[1..]) : null;
    private SvgParser.MaskDefinition? _svgMask;
    internal bool Matches(CssBackgroundImageResource other) => Equals(_source, other._source) && ReferenceEquals(_resolver, other._resolver) &&
        _requirePixelAccess == other._requirePixelAccess && allowSvgMaskReference == other.AllowsSvgMaskReference && Equals(_referrer, other._referrer);
    private bool AllowsSvgMaskReference => allowSvgMaskReference;
    internal static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "CssBackgroundImageResources", typeof(CssBackgroundImageResource[]), typeof(CssBackgroundImageResource), new PropertyMetadata(null));
    private readonly object _gate = new();
    private readonly List<WeakReference<CssNode>> _users = [];
    private readonly List<WeakReference<CssNode>> _contentUsers = [];
    private sealed class SourceState { internal ImageSource Image = new BitmapImage(); internal bool Initialized; }
    private readonly ConditionalWeakTable<Dispatcher, SourceState> _sources = new();
    private Task? _ready;
    private byte[]? _bytes;
    private string? _svg;
    internal bool HasMultipleResolutions { get; private set; }
    private long _revision;
    internal long Revision => Interlocked.Read(ref _revision);
    internal Task Ready { get { lock (_gate) return _ready ??= Task.Run(Load); } }

    internal ImageSource? Get(CssNode element, bool content = false)
    {
        lock (_gate)
        {
            var users = content ? _contentUsers : _users;
            users.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (!users.Any(reference => reference.TryGetTarget(out var node) && ReferenceEquals(node, element))) users.Add(new(element));
            _ = Ready;
            var state = _sources.GetValue(element.Dispatcher, _ => new() { Image = MaskId is not null ? new SvgMaskImage(null) : new BitmapImage() });
            if (state.Initialized || _bytes is null && _svg is null && _svgMask is null) return state.Image;
            try
            {
                ImageSource image = MaskId is not null ? new SvgMaskImage(_svgMask)
                    : _svg is not null ? SvgImage.FromCssSvgString(_svg) : BitmapImage.FromDeferredBytes(_bytes!);
                if (image is BitmapImage bitmap)
                {
                    // A brush does not have Image's decode/animation subscriptions.
                    // A first draw can finish with no raster; the later publication
                    // must request another frame, including when the page is idle.
                    bitmap.OnImageLoaded += (_, _) => NotifyUsers(pixelsOnly: true, bitmap.Dispatcher);
                    bitmap.MetadataResolved += (_, _) => NotifyUsers(pixelsOnly: true, bitmap.Dispatcher);
                    bitmap.LoadFailed += (_, _) => NotifyUsers(pixelsOnly: true, bitmap.Dispatcher);
                }
                if (image is SvgMaskImage stencil)
                    SvgMaskUsers.Add(stencil, new(this));
                state.Image = image; state.Initialized = true;
                return image;
            }
            catch (Exception error) when (CssShapeImageResource.Expected(error)) { return null; }
        }
    }

    private async Task Load()
    {
        byte[]? bytes = null; string? svg = null; SvgParser.MaskDefinition? svgMask = null;
        try
        {
            if (_source is not null)
            {
                if (_requirePixelAccess && MaskId is null && _source.IsAbsoluteUri && _source.Fragment.Length > 0)
                {
                    CssDiagnostics.Report("mask-image", CssDiagnosticReason.LossyConversion, null,
                        "SVG image fragment views are not rendered in this image context");
                    throw new InvalidDataException("SVG image fragment requires a supported image view");
                }
                var request = MaskId is not null ? _source.IsAbsoluteUri ? new UriBuilder(_source) { Fragment = "" }.Uri
                    : new Uri(_source.OriginalString.Split('#')[0], UriKind.RelativeOrAbsolute) : _source;
                var resource = _requirePixelAccess && _resolver is ICssImageResourceResolver images
                    ? await images.ResolveImageAsync(request, _referrer).ConfigureAwait(false)
                    : await _resolver.ResolveAsync(request).ConfigureAwait(false);
                using (resource.Content)
                using (var buffer = new MemoryStream())
                {
                    static bool Remote(Uri uri) => uri.IsAbsoluteUri && uri.Scheme is "http" or "https";
                    if (_requirePixelAccess && (resource.ImagePixelAccessAllowed == false ||
                        (Remote(_source) || Remote(resource.Uri)) && resource.ImagePixelAccessAllowed != true))
                        throw new InvalidDataException("CSS mask image pixels are not origin-approved");
                    var block = new byte[16384]; int count;
                    while ((count = await resource.Content.ReadAsync(block).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + count > 32_000_000) throw new InvalidDataException("CSS background image exceeds the encoded byte budget");
                        buffer.Write(block, 0, count);
                    }
                    bytes = buffer.ToArray();
                    if (MaskId is { } id)
                    {
                        svgMask = SvgParser.ParseMaskDefinition(bytes, id); bytes = null;
                    }
                    if (bytes is not null && _requirePixelAccess && BitmapImage.ResolveDecoder().TryReadDimensions(bytes, out var width, out var height) &&
                        (width <= 0 || height <= 0 || (long)width * height > 16_777_216))
                        throw new InvalidDataException("CSS mask image exceeds the pixel budget");
                    if (bytes is not null)
                    {
                        HasMultipleResolutions = CssImageFragment.IsMultiResolution(bytes);
                        if (CssShapeImageResource.TrySvg(bytes, out svg)) bytes = null;
                    }
                }
            }
        }
        catch (Exception error) when (CssShapeImageResource.Expected(error)) { bytes = null; svg = null; }
        lock (_gate) { _bytes = bytes; _svg = svg; _svgMask = svgMask; }
        NotifyUsers(pixelsOnly: false);
    }

    private void NotifyUsers(bool pixelsOnly, Dispatcher? sourceDispatcher = null)
    {
        Interlocked.Increment(ref _revision);
        WeakReference<CssNode>[] users;
        lock (_gate) users = [.. _users, .. _contentUsers];
        foreach (var reference in users)
        {
            if (!reference.TryGetTarget(out var element) || element.Dispatcher.HasShutdownStarted ||
                sourceDispatcher is not null && !ReferenceEquals(sourceDispatcher, element.Dispatcher)) continue;
            void Refresh()
            {
                if (element.Target is CssGeneratedImage image && image.UsesResource(this))
                    image.ResourceChanged(pixelsOnly);
                var mask = element.GetValue(CssMaskProperties.ImagesProperty) is CssMaskImages masks && masks.Resources.Contains(this);
                var maskBorder = element.GetValue(CssMaskBorderProperties.SourceProperty) is CssMaskBorderSource stencil && stencil.Resources.Contains(this);
                if (element.GetValue(ActiveProperty) is CssBackgroundImageResource[] active && active.Contains(this) || mask || maskBorder ||
                    element.GetValue(CssBorderImageProperties.ResourcesProperty) is CssBackgroundImageResource[] border && border.Contains(this))
                {
                    if (mask || maskBorder) Jalium.UI.Controls.CssMaskPainter.Invalidate(element.Target);
                    if (pixelsOnly)
                    {
                        CssDocumentConsumers.Invalidate(element);
                        Jalium.UI.Controls.CssBorderImagePainter.InvalidateTemplate(element.Target);
                    }
                    else CssEvaluationScheduler.InvalidateElement(element);
                }
            }
            if (element.Dispatcher.CheckAccess()) Refresh(); else _ = element.Dispatcher.BeginInvoke(DispatcherPriority.Render, Refresh);
        }
    }
}
