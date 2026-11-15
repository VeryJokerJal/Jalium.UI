using System.Runtime.CompilerServices;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media;

/// <summary>An SVG mask reference has target-relative geometry and no intrinsic image size.</summary>
internal sealed class SvgMaskImage(SvgParser.MaskDefinition? definition) : ImageSource
{
    private sealed class Readers { internal List<WeakReference<SvgMaskImage>> Values = []; }
    private static readonly ConditionalWeakTable<ImageSource, Readers> Dependents = new();
    static SvgMaskImage()
    {
        RasterChanged += SourceChanged;
    }
    private static void SourceChanged(ImageSource source)
    {
        if (!Dependents.TryGetValue(source, out var readers)) return;
        WeakReference<SvgMaskImage>[] copy;
        lock (readers) copy = readers.Values.ToArray();
        foreach (var reference in copy)
            if (reference.TryGetTarget(out var mask)) { Interlocked.Increment(ref mask._generation); RaiseRasterChanged(mask); }
    }
    private static void SourceLoaded(object? sender, EventArgs args)
    { if (sender is ImageSource source) SourceChanged(source); }
    private long _generation;
    internal override long ContentGeneration => Interlocked.Read(ref _generation);
    internal SvgParser.MaskDefinition? Definition { get; } = definition;
    internal void Observe(IEnumerable<ImageSource> sources)
    {
        foreach (var source in sources)
        {
            if (ReferenceEquals(source, this)) continue;
            var readers = Dependents.GetValue(source, static image =>
            {
                // Eager StreamSource/FromBytes publications use OnImageLoaded;
                // deferred decodes and animated frames use RasterChanged.
                if (image is BitmapImage bitmap) bitmap.OnImageLoaded += SourceLoaded;
                return new Readers();
            });
            lock (readers)
            {
                readers.Values.RemoveAll(reference => !reference.TryGetTarget(out _));
                if (!readers.Values.Any(reference => reference.TryGetTarget(out var mask) && ReferenceEquals(mask, this))) readers.Values.Add(new(this));
            }
        }
    }
    public override double Width => 0;
    public override double Height => 0;
    public override nint NativeHandle => 0;
    public override ImageMetadata? Metadata => null;
    protected override Freezable CreateInstanceCore() => new SvgMaskImage(Definition);
}
