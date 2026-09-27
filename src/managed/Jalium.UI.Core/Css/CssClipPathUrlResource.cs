using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>Loads an SVG clipPath reference once and notifies its live CSS consumers.</summary>
internal sealed class CssClipPathUrlResource(
    Uri? source, string fragment, ICssResourceResolver resolver)
{
    private const int MaximumBytes = 8_000_000;
    private readonly object _gate = new();
    private readonly List<WeakReference<UIElement>> _users = [];
    private readonly Dictionary<Size, Geometry?> _geometryCache = [];
    private Task? _load;
    private SvgParser.ClipPathDefinition? _definition;

    internal Task Ready
    {
        get
        {
            lock (_gate) return _load ??= Task.Run(LoadAsync);
        }
    }

    internal void Subscribe(UIElement element)
    {
        lock (_gate)
        {
            _users.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (!_users.Any(reference => reference.TryGetTarget(out var target) &&
                ReferenceEquals(target, element)))
                _users.Add(new(element));
        }
        _ = Ready;
    }

    internal Geometry? Resolve(Size size)
    {
        SvgParser.ClipPathDefinition? definition;
        lock (_gate)
        {
            definition = _definition;
            if (definition is null) return null;
            if (_geometryCache.TryGetValue(size, out var cached)) return cached;
        }
        Geometry? geometry;
        try { geometry = definition.Resolve(size); }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        { return null; }
        lock (_gate)
        {
            if (ReferenceEquals(_definition, definition))
            {
                if (_geometryCache.Count >= 16) _geometryCache.Clear();
                _geometryCache[size] = geometry;
            }
        }
        return geometry;
    }

    private async Task LoadAsync()
    {
        SvgParser.ClipPathDefinition? definition = null;
        if (source is not null && fragment.Length > 0)
        {
            try
            {
                var resource = await resolver.ResolveAsync(source).ConfigureAwait(false);
                using (resource.Content)
                using (var buffer = new MemoryStream())
                {
                    var block = new byte[16384];
                    int count;
                    while ((count = await resource.Content.ReadAsync(block).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + count > MaximumBytes)
                            throw new InvalidDataException("SVG clip-path resource is too large");
                        buffer.Write(block, 0, count);
                    }
                    definition = SvgParser.ParseClipPathDefinition(buffer.ToArray(), fragment);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                System.Net.Http.HttpRequestException or System.Xml.XmlException or
                UriFormatException or NotSupportedException or InvalidOperationException or
                UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            { }
        }

        WeakReference<UIElement>[] users;
        lock (_gate)
        {
            _definition = definition;
            _geometryCache.Clear();
            users = [.. _users];
        }
        foreach (var reference in users)
        {
            if (!reference.TryGetTarget(out var element) || element.Dispatcher.HasShutdownStarted)
                continue;
            void Refresh()
            {
                if (element.GetValue(CssClipPathProperties.ValueProperty) is not
                    CssClipPathValue { UrlResource: { } active } || !ReferenceEquals(active, this))
                    return;
                UIElement.InvalidateHitTestCache();
                element.InvalidateVisual();
            }
            if (element.Dispatcher.CheckAccess()) Refresh();
            else _ = element.Dispatcher.BeginInvoke(DispatcherPriority.Render, Refresh);
        }
    }
}

internal sealed class CssClipPathUrlCompiledValue(CssClipPathUrlResource resource)
    : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssClipPathProperties.ValueProperty,
            new CssClipPathValue(CssClipShape.Url, CssClipBox.Border, [], UrlResource: resource));
        return true;
    }
}
