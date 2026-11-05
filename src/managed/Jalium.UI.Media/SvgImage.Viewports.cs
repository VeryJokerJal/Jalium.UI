using System.Runtime.CompilerServices;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Media;

/// <summary>Presence flags distinguish missing natural lengths from real zero lengths.</summary>
internal readonly record struct SvgIntrinsicDimensions(double Width, double Height, double Ratio)
{
    internal bool HasWidth { get; init; } = Width > 0;
    internal bool HasHeight { get; init; } = Height > 0;
}

public sealed partial class SvgImage
{
    private sealed record Document(string Content, bool TextOutlines, SvgIntrinsicDimensions Intrinsic);
    private sealed class Viewports
    {
        internal Document? Document;
        internal readonly Dictionary<Size, DrawingImage> Images = [];
    }
    private Document? _viewportDocument;
    private ConditionalWeakTable<Dispatcher, Viewports> _viewportPlans = new();
    internal bool UsesCssViewport { get; private init; }
    internal SvgIntrinsicDimensions CssIntrinsic => Volatile.Read(ref _viewportDocument)?.Intrinsic ??
        new(Width, Height, Width > 0 && Height > 0 ? Width / Height : 0);

    private void ClearViewportDocument()
    {
        Volatile.Write(ref _viewportDocument, null);
        Volatile.Write(ref _viewportPlans, new());
    }
    private void SetViewportDocument(string content, bool outlines, SvgIntrinsicDimensions intrinsic)
    {
        Volatile.Write(ref _viewportPlans, new());
        Volatile.Write(ref _viewportDocument, new(content, outlines, intrinsic));
    }

    /// <summary>
    /// An image element/CSS image negotiates a concrete SVG viewport. Canvas
    /// DrawImage retains the intrinsic drawing and its separate scale instead.
    /// Plans belong to the rendering dispatcher, like SVG mask plans, so a
    /// shared source never hands a mutable XML/drawing plan to another thread.
    /// </summary>
    internal ImageSource ImageForViewport(Size viewport)
    {
        var document = Volatile.Read(ref _viewportDocument);
        if (document is null || !(viewport.Width > 0 && viewport.Height > 0) ||
            !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)) return this;
        var plans = Volatile.Read(ref _viewportPlans).GetOrCreateValue(Dispatcher.CurrentDispatcher);
        if (!ReferenceEquals(plans.Document, document)) { plans.Images.Clear(); plans.Document = document; }
        if (plans.Images.TryGetValue(viewport, out var image)) return image;
        var extent = new Rect(0, 0, viewport.Width, viewport.Height);
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(extent) };
        // A transparent extent preserves the image viewport even with empty or
        // overflowing content; DrawingImage must not substitute the ink bounds.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(extent)));
        try { drawing.Children.Add(SvgParser.ParseViewport(document.Content, viewport, document.TextOutlines).Drawing); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException or InvalidDataException)
        {
            CssDiagnostics.Report("SVG viewport", CssDiagnosticReason.LossyConversion, null, "Cannot render this SVG viewport: " + error.Message);
        }
        image = new DrawingImage(drawing) { DrawOwner = this };
        if (plans.Images.Count >= 4) plans.Images.Remove(plans.Images.Keys.First());
        plans.Images.Add(viewport, image);
        return image;
    }
}
