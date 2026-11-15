using System.Text;
using System.Xml;
using System.Xml.Linq;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>One asynchronous, origin-approved first-frame decode shared by a compiled URL.</summary>
internal sealed class CssShapeImageResource(Uri? sourceUri, Uri? styleReferrer, ICssResourceResolver resourceResolver)
{
    private readonly Uri? _source = sourceUri, _referrer = styleReferrer;
    private readonly ICssResourceResolver _resolver = resourceResolver;
    internal bool Matches(CssShapeImageResource other) => Equals(_source, other._source) && Equals(_referrer, other._referrer) && ReferenceEquals(_resolver, other._resolver);
    private readonly object _gate = new();
    private readonly List<WeakReference<UIElement>> _users = [];
    private readonly Dictionary<Size, CssShapeImagePixels> _svgRasters = [];
    private Task? _load;
    private CssShapeImagePixels? _pixels;
    private string? _svg;
    internal bool HasMultipleResolutions { get; private set; }
    internal Task Ready { get { lock (_gate) return _load ??= Task.Run(LoadAsync); } }

    internal void Subscribe(UIElement element)
    {
        lock (_gate)
        {
            _users.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (!_users.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, element))) _users.Add(new(element));
        }
        _ = Ready;
    }

    internal CssShapeImagePixels? Resolve(Size size)
    {
        lock (_gate)
        {
            if (_pixels is not null || _svg is null) return _pixels;
            if (_svgRasters.TryGetValue(size, out var cached)) return cached;
            try
            {
                using var svg = SvgImage.FromSvgStringWithTextOutlines(_svg);
                var brush = new ImageBrush(svg) { Stretch = Stretch.Fill };
                var pixels = CssShapeImageValue.Rasterize(brush, size);
                if (pixels is null) return null;
                pixels = pixels.WithIntrinsic(CssImageIntrinsicSize.FromSource(svg));
                if (_svgRasters.Count >= 8) _svgRasters.Clear();
                return _svgRasters[size] = pixels;
            }
            catch (Exception error) when (Expected(error)) { return null; }
        }
    }

    private async Task LoadAsync()
    {
        CssShapeImagePixels? pixels = null; string? svg = null;
        if (_source is not null)
        {
            try
            {
                var resource = _resolver is ICssImageResourceResolver images
                    ? await images.ResolveImageAsync(_source, _referrer).ConfigureAwait(false)
                    : await _resolver.ResolveAsync(_source).ConfigureAwait(false);
                using (resource.Content)
                {
                    var remote = IsHttp(_source) || IsHttp(resource.Uri);
                    if (resource.ImagePixelAccessAllowed == false || remote && resource.ImagePixelAccessAllowed != true)
                        throw new InvalidDataException("CSS shape image pixels are not origin-approved");
                    using var buffer = new MemoryStream(); var block = new byte[16384]; int count;
                    while ((count = await resource.Content.ReadAsync(block).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + count > 32_000_000) throw new InvalidDataException("CSS shape image exceeds the encoded byte budget");
                        buffer.Write(block, 0, count);
                    }
                    var bytes = buffer.ToArray();
                    HasMultipleResolutions = CssImageFragment.IsMultiResolution(bytes);
                    if (TrySvg(bytes, out svg)) { }
                    else
                    {
                        var decoder = BitmapImage.ResolveDecoder();
                        if (decoder.TryReadDimensions(bytes, out var width, out var height) &&
                            (width <= 0 || height <= 0 || (long)width * height > CssShapeImagePixels.MaximumPixels))
                            throw new InvalidDataException("CSS shape image exceeds the pixel budget");
                        // FromBytes is the eager static path: no animated substitute or
                        // display decode bucket can change the shape after frame zero.
                        using var bitmap = BitmapImage.FromBytes(bytes);
                        pixels = CssShapeImagePixels.FromBitmap(bitmap);
                    }
                }
            }
            catch (Exception error) when (Expected(error)) { pixels = null; svg = null; }
        }
        WeakReference<UIElement>[] users;
        lock (_gate) { _pixels = pixels; _svg = svg; users = [.. _users]; }
        foreach (var reference in users)
        {
            if (!reference.TryGetTarget(out var element) || element.Dispatcher.HasShutdownStarted) continue;
            void Refresh()
            {
                if (element.GetValue(CssShapeOutsideProperties.ValueProperty) is CssShapeImageValue active && active.UsesResource(this))
                    CssFloatProperties.Changed(element, default);
            }
            if (element.Dispatcher.CheckAccess()) Refresh(); else _ = element.Dispatcher.BeginInvoke(DispatcherPriority.Render, Refresh);
        }
    }

    private static bool IsHttp(Uri uri) => uri.IsAbsoluteUri && uri.Scheme is "http" or "https";
    internal static bool Expected(Exception error) => error is IOException or InvalidDataException or
        System.Net.Http.HttpRequestException or XmlException or UriFormatException or NotSupportedException or
        InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or
        DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or FormatException or
        OperationCanceledException or Jalium.UI.Media.Native.NativeMediaException;

    internal static bool TrySvg(byte[] bytes, out string? svg)
    {
        svg = null;
        // Binary rasters avoid XML parsing; XML encodings (including UTF-16)
        // are decoded by XmlReader rather than by a guessed UTF-8 conversion.
        var start = 0;
        while (start < bytes.Length && bytes[start] is 9 or 10 or 13 or 32) start++;
        var textStart = start < bytes.Length && bytes[start] == (byte)'<';
        var bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ||
            bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) || bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ||
            bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff }) || bytes.AsSpan().StartsWith(new byte[] { 0, 0x3c });
        if (!textStart && !bom) return false;
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32_000_000 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "svg") return false;
        // SVG-as-image may use embedded raster bytes, but imports no external
        // subresources. Remove those image nodes while preserving other shapes.
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName is "image" or "feImage").ToArray())
        {
            var href = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value;
            if (href is null || !href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) { element.Remove(); continue; }
            var comma = href.IndexOf(',');
            if (comma < 0 || !href[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) { element.Remove(); continue; }
            var embedded = Convert.FromBase64String(href[(comma + 1)..]);
            if (BitmapImage.ResolveDecoder().TryReadDimensions(embedded, out var width, out var height) &&
                (width <= 0 || height <= 0 || (long)width * height > CssShapeImagePixels.MaximumPixels))
                throw new InvalidDataException("Embedded CSS shape image exceeds the pixel budget");
        }
        svg = document.ToString(SaveOptions.DisableFormatting); return true;
    }
}
