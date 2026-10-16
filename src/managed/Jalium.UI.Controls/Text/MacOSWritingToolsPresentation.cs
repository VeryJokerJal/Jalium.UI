using System.Runtime.CompilerServices;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using RenderTargetBitmap = Jalium.UI.Media.Imaging.RenderTargetBitmap;
using WriteableBitmap = Jalium.UI.Media.Imaging.WriteableBitmap;

namespace Jalium.UI;

internal static class MacOSWritingToolsPresentation
{
    private static readonly ConditionalWeakTable<UIElement, State> States = new();
    internal static bool UsesShapedGlyphRanges => OperatingSystem.IsMacOS() && Jalium.UI.Interop.RenderContext.Current?.Backend == Jalium.UI.Interop.RenderBackend.Metal;
    [ThreadStatic] private static int _previewDepth;
    [ThreadStatic] private static UIElement? _previewOwner;
    [ThreadStatic] private static IAutomationTextProviderSource? _previewSource;
    [ThreadStatic] private static int _previewStart, _previewLength;
    [ThreadStatic] private static int _undoDepth;
    internal static bool IsUndoRedo => _undoDepth != 0;
    internal static UndoScope BeginUndoRedo() => new(true);
    internal readonly struct UndoScope : IDisposable
    {
        internal UndoScope(bool _) => ++_undoDepth;
        public void Dispose() => --_undoDepth;
    }
    private sealed class Animation(int start, int length, int kind)
    {
        internal int Start = start, Length = length;
        internal readonly int Kind = kind;
        internal ImageSource? Inactive;
        internal Rect InactiveFrame;
    }
    private sealed class State(IAutomationTextProviderSource source, Func<bool> current)
    {
        internal readonly IAutomationTextProviderSource Source = source;
        internal readonly Func<bool> Current = current;
        internal readonly Dictionary<string, List<Animation>> Animations = new();
        internal string Text = source.Text;
        internal int Activity;
        internal Geometry? Mask;
    }
    internal sealed record Preview(byte[] Png, Rect Frame, IReadOnlyList<Rect> Rectangles);

    internal static bool IsPresenting(UIElement owner) => _previewDepth == 0 &&
        States.TryGetValue(owner, out var state) && state.Current() && (state.Activity != 0 || state.Animations.Count != 0);

    internal static void Clear(UIElement owner)
    {
        if (States.Remove(owner)) Invalidate(owner);
    }
    private static void Invalidate(UIElement owner)
    {
        owner.InvalidateVisual();
        if (owner is TextBoxBase box) box.InvalidateWritingToolsText();
    }
    internal static void ObserveText(UIElement owner, string text)
    {
        if (!States.TryGetValue(owner, out var state)) return;
        state.Mask = null; // Caret/viewport refreshes also report reflow.
        foreach (var animation in state.Animations.Values.SelectMany(list => list)) animation.Inactive = null;
        if (state.Text == text) return;
        string old = state.Text; state.Text = text;
        int start = 0, suffix = 0;
        while (start < old.Length && start < text.Length && old[start] == text[start]) ++start;
        while (suffix < old.Length - start && suffix < text.Length - start &&
            old[old.Length - suffix - 1] == text[text.Length - suffix - 1]) ++suffix;
        int oldEnd = old.Length - suffix, newEnd = text.Length - suffix;
        int delta = newEnd - oldEnd;
        foreach (var animation in state.Animations.Values.SelectMany(list => list))
        {
            int end = animation.Start + animation.Length;
            int mappedStart = animation.Start <= start ? animation.Start : animation.Start >= oldEnd ? animation.Start + delta : start;
            int mappedEnd = end < start ? end : end >= oldEnd ? end + delta : newEnd;
            animation.Start = Math.Clamp(mappedStart, 0, text.Length);
            animation.Length = Math.Clamp(mappedEnd - animation.Start, 0, text.Length - animation.Start);
            animation.Inactive = null;
        }
        Invalidate(owner);
    }

    internal static void Prepare(UIElement owner, IAutomationTextProviderSource source, string token,
        int start, int length, int kind, Func<bool> current)
    {
        if (!current()) return;
        var state = States.GetValue(owner, _ => new(source, current));
        ObserveText(owner, source.Text);
        if (!state.Animations.TryGetValue(token, out var animations)) state.Animations[token] = animations = new();
        animations.Add(new(start, length, kind));
        state.Mask = null;
        Invalidate(owner);
    }
    internal static void Finish(UIElement owner, string token)
    {
        if (!States.TryGetValue(owner, out var state) || !state.Animations.TryGetValue(token, out var list)) return;
        list.RemoveAt(list.Count - 1);
        if (list.Count == 0) state.Animations.Remove(token);
        state.Mask = null;
        if (state.Activity == 0 && state.Animations.Count == 0) States.Remove(owner);
        Invalidate(owner);
    }
    internal static void SetState(UIElement owner, IAutomationTextProviderSource source, int activity,
        JsonElement payload, Func<bool> current)
    {
        if (activity == 0) { Clear(owner); return; }
        var state = States.GetValue(owner, _ => new(source, current));
        state.Activity = activity;
        foreach (string key in state.Animations.Keys.Where(key => key.StartsWith("system-state:", StringComparison.Ordinal)).ToArray())
            state.Animations.Remove(key);
        if (activity == 1)
        {
            int index = 0;
            if (payload.TryGetProperty("ranges", out var ranges))
                foreach (var range in ranges.EnumerateArray())
                    Prepare(owner, source, "system-state:" + index++, range[0].GetInt32(), range[1].GetInt32(), 8, current);
        }
        state.Mask = null;
        Invalidate(owner);
    }

    internal static void ApplyGlyphRanges(UIElement owner, FormattedText text, int documentOffset, int? layoutLength = null)
    {
        if (!UsesShapedGlyphRanges) return;
        bool preview = _previewDepth != 0 && ReferenceEquals(owner, _previewOwner);
        State? state = null;
        if (!preview && (_previewDepth != 0 || !States.TryGetValue(owner, out state) || !state.Current())) return;
        int length = layoutLength ?? text.Text.Length;
        List<uint> ranges = new();
        void Add(int start, int count)
        {
            int from = Math.Max(start, documentOffset), to = (int)Math.Min((long)start + count, (long)documentOffset + length);
            if (to > from) { ranges.Add((uint)(from - documentOffset)); ranges.Add((uint)(to - from)); }
        }
        if (preview)
        {
            Add(_previewStart, _previewLength);
            text.GlyphVisibility = 1;
        }
        else
        {
            foreach (var animation in state!.Animations.Values.SelectMany(list => list).ToArray())
                if (animation.Kind != 8 || EnsureInactive(owner, state, animation)) Add(animation.Start, animation.Length);
            if (ranges.Count == 0 || !state.Current()) return;
            text.GlyphVisibility = 2;
        }
        text.GlyphRanges = ranges.ToArray();
    }

    internal readonly struct ClipScope(DrawingContext? context) : IDisposable
    {
        public void Dispose() => context?.Pop();
    }
    internal static ClipScope PushDecorationRanges(UIElement owner, DrawingContext context, Point offset = default)
    {
        if (_previewDepth == 0) return PushHidden(owner, context, offset);
        if (!ReferenceEquals(owner, _previewOwner)) return default;
        // Decorations follow the selected advances; glyph ink is filtered in
        // the shaped run, separately, so this clip cannot cut italic glyphs.
        if (_previewSource is null) return default;
        Geometry? included = null;
        foreach (var rect in _previewSource.GetBoundingRectangles(_previewStart, _previewLength))
            if (Finite(rect) && rect.Width > 0)
            {
                var local = new Rect(rect.X - offset.X, rect.Y - offset.Y, rect.Width, rect.Height);
                included = included is null ? new RectangleGeometry(local) : new CombinedGeometry(GeometryCombineMode.Union, included, new RectangleGeometry(local));
            }
        context.PushClip(included ?? Geometry.Empty);
        return new(context);
    }
    internal static ClipScope PushTextClip(UIElement owner, DrawingContext context, Point offset = default) =>
        UsesShapedGlyphRanges ? default : PushHidden(owner, context, offset);

    internal static ClipScope PushHidden(UIElement owner, DrawingContext context, Point offset = default)
    {
        if (_previewDepth != 0 || !States.TryGetValue(owner, out var state) || !state.Current()) return default;
        Geometry? mask = state.Mask;
        if (mask is null)
        {
            Geometry? holes = null;
            foreach (var animation in state.Animations.Values.SelectMany(list => list).ToArray())
            {
                if (animation.Kind == 8 && !EnsureInactive(owner, state, animation)) continue;
                foreach (var rect in state.Source.GetBoundingRectangles(animation.Start, animation.Length))
                    if (Finite(rect) && rect.Width > 0)
                        holes = holes is null ? new RectangleGeometry(rect)
                            : new CombinedGeometry(GeometryCombineMode.Union, holes, new RectangleGeometry(rect));
            }
            if (holes is null || !state.Current()) return default;
            var bounds = owner is FrameworkElement element
                ? new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height)
                : holes.Bounds;
            mask = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), holes);
            state.Mask = mask;
        }
        if (offset.X != 0 || offset.Y != 0)
            mask = new CombinedGeometry(GeometryCombineMode.Union, mask, Geometry.Empty,
                new TranslateTransform(-offset.X, -offset.Y));
        context.PushClip(mask);
        return new(context);
    }

    internal static void DrawInactive(UIElement owner, DrawingContext context, Point offset = default)
    {
        if (_previewDepth != 0 || !States.TryGetValue(owner, out var state) || !state.Current()) return;
        foreach (var animation in state.Animations.Values.SelectMany(list => list).Where(animation => animation.Kind == 8).ToArray())
        {
            if (animation.Inactive is null) continue;
            if (!state.Current()) return;
            var frame = animation.InactiveFrame;
            context.DrawImage(animation.Inactive, new Rect(frame.X - offset.X, frame.Y - offset.Y, frame.Width, frame.Height));
        }
    }

    private static bool EnsureInactive(UIElement owner, State state, Animation animation)
    {
        if (animation.Inactive is not null) return true;
        var bitmap = RenderRange(owner, state.Source, animation.Start, animation.Length,
            Matrix.Identity, 1, null, state.Current);
        if (bitmap is null) return false;
        var image = bitmap.Value.Bitmap;
        int stride = checked(image.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * image.PixelHeight)];
        image.CopyPixels(pixels, stride, 0);
        for (int i = 0; i < pixels.Length; i += 4)
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 128;
        var tinted = new WriteableBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
        tinted.WritePixels(new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight), pixels, stride, 0);
        animation.Inactive = tinted; animation.InactiveFrame = bitmap.Value.Frame;
        return state.Current();
    }

    internal static Preview? CreatePreview(UIElement owner, IAutomationTextProviderSource source,
        int start, int length, Matrix transform, double dpi, Rect? requested, Func<bool> current)
    {
        var rendered = RenderRange(owner, source, start, length, transform, dpi, requested, current);
        if (rendered is null) return null;
        using var output = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered.Value.Bitmap));
        encoder.Save(output);
        if (output.Length > 48 * 1024 * 1024 || !current()) return null;
        return new(output.ToArray(), rendered.Value.Frame, rendered.Value.Rectangles);
    }
    private static (WriteableBitmap Bitmap, Rect Frame, IReadOnlyList<Rect> Rectangles)? RenderRange(
        UIElement owner, IAutomationTextProviderSource source, int start, int length,
        Matrix transform, double dpi, Rect? requested, Func<bool> current)
    {
        if (!current() || !double.IsFinite(dpi) || dpi <= 0) return null;
        var rectangles = source.GetBoundingRectangles(start, length).Where(Finite).Where(rect => rect.Width > 0).ToArray();
        if (rectangles.Length == 0 || !current()) return null;
        if (!UsesShapedGlyphRanges) return RenderLegacyRange(owner, source, start, length, transform, dpi, requested, current);
        // The caret rectangles describe advances, not glyph ink. Capture the
        // visible viewport with a shaped-glyph filter, then trim transparent
        // margins. Italics, marks and color emoji can extend past those advances.
        if (owner is not FrameworkElement element) return null;
        Rect viewport = Transform(new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height), transform);
        if (requested is { } clip) viewport = Rect.Intersect(viewport, clip);
        if (!Finite(viewport) || viewport.Width <= 0) return null;
        double left = Math.Floor(viewport.X * dpi), top = Math.Floor(viewport.Y * dpi);
        double width = Math.Ceiling(viewport.Right * dpi) - left, height = Math.Ceiling(viewport.Bottom * dpi) - top;
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || width * height > 16 * 1024 * 1024) return null;
        Rect captured = new(left / dpi, top / dpi, width / dpi, height / dpi);
        var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)width, (int)height, dpi, dc =>
        {
            dc.PushTransform(new TranslateTransform(-captured.X, -captured.Y));
            dc.PushTransform(new MatrixTransform(transform));
            var previousOwner = _previewOwner; var previousSource = _previewSource; int previousStart = _previewStart, previousLength = _previewLength;
            ++_previewDepth; _previewOwner = owner; _previewSource = source; _previewStart = start; _previewLength = length;
            try
            {
                switch (owner)
                {
                    case TextBox text: text.RenderWritingToolsText(dc); break;
                    case RichTextBox rich: rich.RenderWritingToolsText(dc); break;
                    case EditControl editor: editor.RenderWritingToolsText(dc); break;
                }
            }
            finally
            {
                --_previewDepth; _previewOwner = previousOwner; _previewSource = previousSource; _previewStart = previousStart; _previewLength = previousLength;
                dc.Pop(); dc.Pop();
            }
        }, current);
        if (bitmap is null || !current()) return null;
        var projected = rectangles.Select(rect => Transform(rect, transform)).ToArray();
        Rect frame = Rect.Empty;
        foreach (var rect in projected) frame = frame.IsEmpty ? rect : Rect.Union(frame, rect);
        frame = Rect.Intersect(frame, captured);
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        int inkLeft = bitmap.PixelWidth, inkTop = bitmap.PixelHeight, inkRight = 0, inkBottom = 0;
        for (int row = 0; row < bitmap.PixelHeight; ++row)
            for (int col = 0; col < bitmap.PixelWidth; ++col)
                if (pixels[row * stride + col * 4 + 3] != 0)
                {
                    inkLeft = Math.Min(inkLeft, col); inkTop = Math.Min(inkTop, row);
                    inkRight = Math.Max(inkRight, col + 1); inkBottom = Math.Max(inkBottom, row + 1);
                }
        if (inkRight > inkLeft)
        {
            Rect ink = new(captured.X + inkLeft / dpi, captured.Y + inkTop / dpi,
                (inkRight - inkLeft) / dpi, (inkBottom - inkTop) / dpi);
            frame = frame.IsEmpty ? ink : Rect.Union(frame, ink);
        }
        if (!Finite(frame) || frame.Width <= 0) return null;
        int cropX = Math.Clamp((int)Math.Floor((frame.X - captured.X) * dpi), 0, bitmap.PixelWidth - 1);
        int cropY = Math.Clamp((int)Math.Floor((frame.Y - captured.Y) * dpi), 0, bitmap.PixelHeight - 1);
        int cropRight = Math.Clamp((int)Math.Ceiling((frame.Right - captured.X) * dpi), cropX + 1, bitmap.PixelWidth);
        int cropBottom = Math.Clamp((int)Math.Ceiling((frame.Bottom - captured.Y) * dpi), cropY + 1, bitmap.PixelHeight);
        int cropWidth = cropRight - cropX, cropHeight = cropBottom - cropY;
        byte[] trimmed = new byte[checked(cropWidth * cropHeight * 4)];
        for (int row = 0; row < cropHeight; ++row)
            Buffer.BlockCopy(pixels, (cropY + row) * stride + cropX * 4, trimmed, row * cropWidth * 4, cropWidth * 4);
        var result = new WriteableBitmap(cropWidth, cropHeight, 96 * dpi, 96 * dpi, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, cropWidth, cropHeight), trimmed, cropWidth * 4, 0);
        frame = new Rect(captured.X + cropX / dpi, captured.Y + cropY / dpi, cropWidth / dpi, cropHeight / dpi);
        var candidates = projected.Select(rect => Rect.Intersect(rect, frame)).Where(Finite).ToArray();
        return current() ? (result, frame, candidates) : null;
    }
    // Preserve the existing bounds-based path for renderers without the optional
    // glyph-range capability (for example an explicitly selected Software backend).
    private static (WriteableBitmap Bitmap, Rect Frame, IReadOnlyList<Rect> Rectangles)? RenderLegacyRange(
        UIElement owner, IAutomationTextProviderSource source, int start, int length,
        Matrix transform, double dpi, Rect? requested, Func<bool> current)
    {
        if (!current() || !double.IsFinite(dpi) || dpi <= 0) return null;
        var rectangles = source.GetBoundingRectangles(start, length).Where(Finite).Where(rect => rect.Width > 0).ToArray();
        if (rectangles.Length == 0 || !current()) return null;
        Rect frame = Rect.Empty;
        var projected = rectangles.Select(rect => Transform(rect, transform)).ToArray();
        foreach (var rect in projected) frame = frame.IsEmpty ? rect : Rect.Union(frame, rect);
        if (requested is { } clip) frame = Rect.Intersect(frame, clip);
        if (!Finite(frame) || frame.Width <= 0) return null;
        // Snap outwards to device pixels, so frame and PNG have the same scale.
        double left = Math.Floor(frame.X * dpi), top = Math.Floor(frame.Y * dpi);
        double width = Math.Ceiling(frame.Right * dpi) - left, height = Math.Ceiling(frame.Bottom * dpi) - top;
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || width * height > 16 * 1024 * 1024) return null;
        frame = new Rect(left / dpi, top / dpi, width / dpi, height / dpi);
        Geometry? included = null;
        foreach (var rect in rectangles)
            included = included is null ? new RectangleGeometry(rect) :
                new CombinedGeometry(GeometryCombineMode.Union, included, new RectangleGeometry(rect));
        Rect captured = frame;
        var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)width, (int)height, dpi, dc =>
        {
            dc.PushTransform(new TranslateTransform(-captured.X, -captured.Y));
            dc.PushTransform(new MatrixTransform(transform));
            dc.PushClip(included!);
            ++_previewDepth;
            try
            {
                switch (owner)
                {
                    case TextBox text: text.RenderWritingToolsText(dc); break;
                    case RichTextBox rich: rich.RenderWritingToolsText(dc); break;
                    case EditControl editor: editor.RenderWritingToolsText(dc); break;
                }
            }
            finally { --_previewDepth; dc.Pop(); dc.Pop(); dc.Pop(); }
        }, current);
        var candidates = projected.Select(rect => Rect.Intersect(rect, frame)).Where(Finite).ToArray();
        return current() && bitmap is not null ? (bitmap, frame, candidates) : null;
    }
    private static bool Finite(Rect rect) => !rect.IsEmpty && double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
        double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && rect.Width >= 0 && rect.Height > 0;
    private static Rect Transform(Rect rect, Matrix matrix)
    {
        Point a = matrix.Transform(new Point(rect.Left, rect.Top)), b = matrix.Transform(new Point(rect.Right, rect.Top));
        Point c = matrix.Transform(new Point(rect.Left, rect.Bottom)), d = matrix.Transform(new Point(rect.Right, rect.Bottom));
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        return new Rect(left, top, Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - left,
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - top);
    }
}

