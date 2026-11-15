using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>
/// Contract for CSS Painting API (<c>paint(&lt;ident&gt;, ...)</c>) worklets.
/// </summary>
public interface ICssPaintWorklet
{
    void Paint(
        DrawingContext context,
        Size size,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<string> arguments);
}

/// <summary>
/// Registry for CSS Painting API worklets (<c>paint(name, ...)</c>).
/// </summary>
public static class CssPaintWorkletRegistry
{
    private static readonly ConcurrentDictionary<string, ICssPaintWorklet> Worklets =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ConsumersGate = new();
    private static readonly List<WeakReference<UIElement>> Consumers = [];
    private static long s_revision = 1;

    public static long Revision => Volatile.Read(ref s_revision);

    public static void Register(string name, ICssPaintWorklet worklet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(worklet);
        Worklets[name.Trim()] = worklet;
        BumpRevisionAndInvalidate();
    }

    public static bool Unregister(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var removed = Worklets.TryRemove(name.Trim(), out _);
        if (removed) BumpRevisionAndInvalidate();
        return removed;
    }

    public static void Clear()
    {
        Worklets.Clear();
        BumpRevisionAndInvalidate();
    }

    public static bool TryGet(string name, out ICssPaintWorklet? worklet)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            worklet = null;
            return false;
        }
        return Worklets.TryGetValue(name.Trim(), out worklet);
    }

    internal static void TrackConsumer(UIElement? element)
    {
        if (element is null) return;
        lock (ConsumersGate)
        {
            for (var i = Consumers.Count - 1; i >= 0; i--)
            {
                if (!Consumers[i].TryGetTarget(out var existing))
                {
                    Consumers.RemoveAt(i);
                    continue;
                }
                if (ReferenceEquals(existing, element))
                    return;
            }
            Consumers.Add(new WeakReference<UIElement>(element));
        }
    }

    private static void BumpRevisionAndInvalidate()
    {
        Interlocked.Increment(ref s_revision);
        List<UIElement> targets = [];
        lock (ConsumersGate)
        {
            for (var i = Consumers.Count - 1; i >= 0; i--)
            {
                if (!Consumers[i].TryGetTarget(out var target))
                {
                    Consumers.RemoveAt(i);
                    continue;
                }
                targets.Add(target);
            }
        }
        foreach (var target in targets)
            target.InvalidateVisual();
    }
}

/// <summary>
/// Registry and resolver for CSS <c>element(#id)</c> live element image references,
/// including cycle detection to prevent recursive rendering loops.
/// </summary>
public static class CssElementImageRegistry
{
    private static readonly ConcurrentDictionary<string, WeakReference<FrameworkElement>> Elements =
        new(StringComparer.Ordinal);
    private static readonly object ConsumersGate = new();
    private static readonly List<WeakReference<UIElement>> Consumers = [];
    [ThreadStatic] private static HashSet<FrameworkElement>? s_activeRenderStack;
    private static long s_revision = 1;

    public static long Revision => Volatile.Read(ref s_revision);

    public static void Register(string id, FrameworkElement element)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(element);
        var normalized = NormalizeId(id);
        Elements[normalized] = new WeakReference<FrameworkElement>(element);
        BumpRevisionAndInvalidate();
    }

    public static bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var removed = Elements.TryRemove(NormalizeId(id), out _);
        if (removed) BumpRevisionAndInvalidate();
        return removed;
    }

    public static void Clear()
    {
        Elements.Clear();
        BumpRevisionAndInvalidate();
    }

    public static void NotifyChanged(string? id = null) => BumpRevisionAndInvalidate();

    public static bool TryResolve(string id, DependencyObject? consumer, out FrameworkElement? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(id)) return false;
        var normalized = NormalizeId(id);

        if (Elements.TryGetValue(normalized, out var weak) && weak.TryGetTarget(out var registered))
        {
            target = registered;
            return true;
        }

        if (consumer is not null)
        {
            var visualStart = consumer as Visual ?? CssNode.Get(consumer).VisualOwner;
            if (visualStart is not null)
            {
                var root = VisualTreeHelper.GetRoot(visualStart) as Visual ?? visualStart;
                target = FindByNameOrId(root, normalized);
                if (target is not null)
                    return true;
            }
        }

        return false;
    }

    internal static bool IsRendering(FrameworkElement element)
        => s_activeRenderStack is { } set && set.Contains(element);

    internal static bool TryEnterRender(FrameworkElement element)
    {
        s_activeRenderStack ??= new HashSet<FrameworkElement>(ReferenceEqualityComparer.Instance);
        return s_activeRenderStack.Add(element);
    }

    internal static void ExitRender(FrameworkElement element)
    {
        s_activeRenderStack?.Remove(element);
    }

    internal static void TrackConsumer(UIElement? element)
    {
        if (element is null) return;
        lock (ConsumersGate)
        {
            for (var i = Consumers.Count - 1; i >= 0; i--)
            {
                if (!Consumers[i].TryGetTarget(out var existing))
                {
                    Consumers.RemoveAt(i);
                    continue;
                }
                if (ReferenceEquals(existing, element))
                    return;
            }
            Consumers.Add(new WeakReference<UIElement>(element));
        }
    }

    private static FrameworkElement? FindByNameOrId(Visual current, string id)
    {
        if (current is FrameworkElement fe)
        {
            if (string.Equals(fe.Name, id, StringComparison.Ordinal))
            {
                return fe;
            }
        }

        var count = current.InternalVisualChildrenCount;
        for (var i = 0; i < count; i++)
        {
            if (current.InternalGetVisualChild(i) is { } child &&
                FindByNameOrId(child, id) is { } match)
            {
                return match;
            }
        }
        return null;
    }

    internal static string NormalizeId(string id)
    {
        var trimmed = id.Trim();
        return trimmed.StartsWith('#') ? trimmed[1..].Trim() : trimmed;
    }

    private static void BumpRevisionAndInvalidate()
    {
        Interlocked.Increment(ref s_revision);
        List<UIElement> targets = [];
        lock (ConsumersGate)
        {
            for (var i = Consumers.Count - 1; i >= 0; i--)
            {
                if (!Consumers[i].TryGetTarget(out var target))
                {
                    Consumers.RemoveAt(i);
                    continue;
                }
                targets.Add(target);
            }
        }
        foreach (var target in targets)
            target.InvalidateVisual();
    }
}

/// <summary>
/// Base representation of procedural CSS images (<c>element()</c> and <c>paint()</c>).
/// </summary>
public abstract class CssImage
{
    internal abstract Brush MaterializeBrush(in CssApplyContext context);
}

/// <summary>
/// Represents a CSS Images Level 4 <c>element(#id)</c> value.
/// </summary>
public sealed class CssElementImage : CssImage
{
    public string TargetId { get; }

    public CssElementImage(string targetId)
    {
        TargetId = CssElementImageRegistry.NormalizeId(targetId);
    }

    internal override Brush MaterializeBrush(in CssApplyContext context)
    {
        var visual = context.Element.VisualOwner;
        CssElementImageRegistry.TrackConsumer(visual as UIElement);
        var dpi = visual is null ? 1 : VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(visual) as Visual ?? visual).DpiScaleY;
        return new ImageBrush
        {
            Stretch = Stretch.Fill,
            CssImageLayout = new CssElementImageLayout(TargetId, context.Element, dpi),
        };
    }
}

/// <summary>
/// Represents a CSS Painting API <c>paint(&lt;ident&gt;, &lt;args&gt;...)</c> value.
/// </summary>
public sealed class CssPaintImage : CssImage
{
    public string WorkletName { get; }
    public IReadOnlyList<string> Arguments { get; }

    public CssPaintImage(string workletName, IReadOnlyList<string> arguments)
    {
        WorkletName = workletName;
        Arguments = arguments;
    }

    internal override Brush MaterializeBrush(in CssApplyContext context)
    {
        var visual = context.Element.VisualOwner;
        CssPaintWorkletRegistry.TrackConsumer(visual as UIElement);
        var dpi = visual is null ? 1 : VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(visual) as Visual ?? visual).DpiScaleY;
        return new ImageBrush
        {
            Stretch = Stretch.Fill,
            CssImageLayout = new CssPaintImageLayout(WorkletName, Arguments, context.Element, dpi),
        };
    }
}

internal sealed class CssElementImageLayout(string targetId, CssNode consumer, double dpi) : CssImageLayout
{
    internal override bool IsValid =>
        CssElementImageRegistry.TryResolve(targetId, consumer.Target, out var target) &&
        target is not null &&
        !ReferenceEquals(target, consumer.Target) &&
        !CssElementImageRegistry.IsRendering(target);

    internal override Size? NaturalSize
    {
        get
        {
            if (!CssElementImageRegistry.TryResolve(targetId, consumer.Target, out var target) || target is null)
                return null;
            EnsureMeasured(target);
            return target.RenderSize.Width > 0 && target.RenderSize.Height > 0
                ? target.RenderSize
                : null;
        }
    }

    internal override Brush? Resolve(double width, double height)
    {
        if (!(width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height)))
            return null;
        if (!CssElementImageRegistry.TryResolve(targetId, consumer.Target, out var target) || target is null)
            return null;
        if (ReferenceEquals(target, consumer.Target) || !CssElementImageRegistry.TryEnterRender(target))
            return null;

        try
        {
            EnsureMeasured(target);
            var sourceSize = target.RenderSize;
            if (!(sourceSize.Width > 0 && sourceSize.Height > 0))
                return null;

            var scale = dpi > 0 && double.IsFinite(dpi) ? dpi : 1;
            scale = Math.Min(scale, Math.Sqrt(16_777_216d / Math.Max(1, width * height)));
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
            var renderTarget = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96 * pixelWidth / width,
                96 * pixelHeight / height,
                PixelFormats.Bgra32);
            var canvas = new SoftwareDrawingContext(renderTarget);
            var sx = width / sourceSize.Width;
            var sy = height / sourceSize.Height;
            var transformed = Math.Abs(sx - 1) > 1e-6 || Math.Abs(sy - 1) > 1e-6;
            if (transformed)
                canvas.PushTransform(new ScaleTransform(sx, sy));
            try
            {
                target.Render(canvas);
            }
            finally
            {
                if (transformed)
                    canvas.Pop();
                canvas.Close();
            }

            var pixels = renderTarget.GetPixelBuffer();
            var image = BitmapSource.Create(
                pixelWidth,
                pixelHeight,
                96 * pixelWidth / width,
                96 * pixelHeight / height,
                PixelFormats.Bgra32,
                null,
                pixels,
                pixelWidth * 4);
            var result = new ImageBrush(image) { Stretch = Stretch.Fill };
            if (result.CanFreeze) result.Freeze();
            return result;
        }
        finally
        {
            CssElementImageRegistry.ExitRender(target);
        }
    }

    private static void EnsureMeasured(FrameworkElement target)
    {
        if (target.RenderSize.Width > 0 && target.RenderSize.Height > 0)
            return;
        var w = double.IsFinite(target.Width) && target.Width > 0 ? target.Width : 0;
        var h = double.IsFinite(target.Height) && target.Height > 0 ? target.Height : 0;
        if (w > 0 && h > 0)
        {
            target.Measure(new Size(w, h));
            target.Arrange(new Rect(0, 0, w, h));
        }
    }
}

internal sealed class CssPaintImageLayout(
    string workletName,
    IReadOnlyList<string> arguments,
    CssNode consumer,
    double dpi) : CssImageLayout
{
    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal override bool IsValid => CssPaintWorkletRegistry.TryGet(workletName, out _);

    internal override Brush? Resolve(double width, double height)
    {
        if (!(width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height)))
            return null;
        if (!CssPaintWorkletRegistry.TryGet(workletName, out var worklet) || worklet is null)
            return null;

        var scale = dpi > 0 && double.IsFinite(dpi) ? dpi : 1;
        scale = Math.Min(scale, Math.Sqrt(16_777_216d / Math.Max(1, width * height)));
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var renderTarget = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96 * pixelWidth / width,
            96 * pixelHeight / height,
            PixelFormats.Bgra32);
        var canvas = new SoftwareDrawingContext(renderTarget);
        try
        {
            var props = CollectProperties(consumer);
            worklet.Paint(canvas, new Size(width, height), props, arguments);
        }
        finally
        {
            canvas.Close();
        }

        var pixels = renderTarget.GetPixelBuffer();
        var image = BitmapSource.Create(
            pixelWidth,
            pixelHeight,
            96 * pixelWidth / width,
            96 * pixelHeight / height,
            PixelFormats.Bgra32,
            null,
            pixels,
            pixelWidth * 4);
        var result = new ImageBrush(image) { Stretch = Stretch.Fill };
        if (result.CanFreeze) result.Freeze();
        return result;
    }

    private static IReadOnlyDictionary<string, string> CollectProperties(CssNode node)
    {
        Dictionary<string, string>? merged = null;
        for (var current = node; current is not null; current = current.FrameworkParent)
        {
            if (current.CssRuntimeState?.CustomProperties is { Count: > 0 } custom)
            {
                merged ??= new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in custom)
                    merged.TryAdd(kv.Key, kv.Value);
            }
        }
        return merged ?? EmptyProperties;
    }
}

/// <summary>
/// Media Fragments URI 1.0 spatial clipping helper (<c>#xywh=[pixel:|percent:]x,y,w,h</c>).
/// </summary>
public static class CssMediaFragment
{
    public static bool TryParseXywh(string uri, out string baseUri, out Rect crop, out bool isPercent)
    {
        baseUri = uri;
        crop = default;
        isPercent = false;
        if (string.IsNullOrEmpty(uri)) return false;
        var hashIndex = uri.IndexOf('#');
        if (hashIndex < 0) return false;
        var fragmentText = uri[(hashIndex + 1)..];
        if (!fragmentText.StartsWith("xywh=", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!CssImageFragment.Read(uri, out baseUri, out var fragment) || fragment is null)
            return false;

        crop = new Rect(fragment.X, fragment.Y, fragment.Width, fragment.Height);
        isPercent = fragment.Percent;
        return true;
    }
}

internal sealed partial class CssImageValue
{
    private static readonly SolidColorBrush CustomImageSentinel = CreateCustomImageSentinel();
    private readonly CssImage? _customImage;

    private static SolidColorBrush CreateCustomImageSentinel()
    {
        var brush = new SolidColorBrush(Colors.Transparent);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private CssImageValue(CssImage customImage)
    {
        _customImage = customImage;
        _gradient = CustomImageSentinel;
    }

    private static bool TryReadElementFunction(ref CssTokenReader args, out CssImageValue? image)
    {
        image = null;
        if (!args.TryReadHash(out var id) || id.IsEmpty || !args.AtEnd)
            return false;
        image = new CssImageValue(new CssElementImage(id.ToString()));
        return true;
    }

    private static bool TryReadPaintFunction(ref CssTokenReader args, out CssImageValue? image)
    {
        image = null;
        if (!args.TryReadIdent(out var workletName) || workletName.IsEmpty)
            return false;
        var argumentList = new List<string>();
        while (!args.AtEnd)
        {
            if (!args.TryReadComma())
                return false;
            if (!args.TryReadUntilTopLevelComma(out var segment))
                return false;
            var trimmed = segment.Trim().ToString();
            if (trimmed.Length == 0)
                return false;
            argumentList.Add(trimmed);
        }
        image = new CssImageValue(new CssPaintImage(workletName.ToString(), argumentList));
        return true;
    }

    private static bool TryReadLegacyCrossFade(
        ref CssTokenReader args,
        CssCompileContext context,
        bool insideSet,
        int depth,
        out CssImageValue? image)
    {
        image = null;
        var probe = args;
        if (!probe.TryReadUntilTopLevelComma(out var firstSpan) || firstSpan.IsEmpty || !probe.TryReadComma())
            return false;
        if (!probe.TryReadUntilTopLevelComma(out var secondSpan) || secondSpan.IsEmpty || !probe.TryReadComma())
            return false;
        if (!probe.TryReadUntilTopLevelComma(out var thirdSpan) || thirdSpan.IsEmpty || !probe.AtEnd)
            return false;

        var firstReader = new CssTokenReader(firstSpan, args.NumericContext);
        CssImageValue? firstImage = null;
        CssImageColor? firstColor = null;
        if (!TryRead(ref firstReader, context, out firstImage, insideSet, depth))
        {
            firstReader = new CssTokenReader(firstSpan, args.NumericContext);
            if (!CssImageColor.TryRead(ref firstReader, out firstColor))
                return false;
        }
        if (!firstReader.AtEnd) return false;

        var secondReader = new CssTokenReader(secondSpan, args.NumericContext);
        CssImageValue? secondImage = null;
        CssImageColor? secondColor = null;
        if (!TryRead(ref secondReader, context, out secondImage, insideSet, depth))
        {
            secondReader = new CssTokenReader(secondSpan, args.NumericContext);
            if (!CssImageColor.TryRead(ref secondReader, out secondColor))
                return false;
        }
        if (!secondReader.AtEnd) return false;

        var weightReader = new CssTokenReader(thirdSpan, args.NumericContext);
        double secondWeight;
        if (weightReader.TryReadNumber(out var number, out var unit) && weightReader.AtEnd)
        {
            if (unit == CssUnit.Percent)
            {
                if (number < 0 || number > 100 || !double.IsFinite(number)) return false;
                secondWeight = number / 100d;
            }
            else if (unit == CssUnit.None)
            {
                if (number < 0 || number > 1 || !double.IsFinite(number)) return false;
                secondWeight = number;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        var firstWeight = 1d - secondWeight;
        args = probe;
        image = new CssImageValue(
        [
            new CrossFadeOption(firstImage, firstColor, firstWeight, null),
            new CrossFadeOption(secondImage, secondColor, secondWeight, null),
        ]);
        return true;
    }
}
