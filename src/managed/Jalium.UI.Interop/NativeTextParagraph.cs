using Microsoft.Win32.SafeHandles;
using Jalium.UI.Media;
using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>One immutable native paragraph shared by layout, drawing and hit testing.</summary>
internal sealed class NativeTextParagraph : IDisposable
{
    internal bool ContextPaint { get; }
    internal readonly record struct Span(string Text, string FontFamily, double FontSize,
        int FontWeight, int FontStyle, Color Color, double Opacity = 1, int? TextOrientation = null);

    private sealed class ParagraphHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private RenderContext.BackendResourceLease? _lease;
        internal ParagraphHandle(nint value, RenderContext.BackendResourceLease lease) : base(true)
        {
            _lease = lease;
            SetHandle(value);
        }
        protected override bool ReleaseHandle()
        {
            try { NativeMethods.TextParagraphDestroy(handle); }
            finally { Interlocked.Exchange(ref _lease, null)?.Dispose(); }
            return true;
        }
    }

    // A drawing owns a separate SafeHandle reference to the same immutable
    // paragraph. Closing the layout owner prevents new layout queries while
    // recorded frames and the render worker can still use their retained pin.
    private sealed class ParagraphDrawingHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly ParagraphHandle _owner;
        internal ParagraphDrawingHandle(ParagraphHandle owner) : base(true)
        {
            bool pinned = false;
            owner.DangerousAddRef(ref pinned);
            _owner = owner;
            SetHandle(owner.DangerousGetHandle());
        }
        protected override bool ReleaseHandle()
        {
            _owner.DangerousRelease();
            return true;
        }
    }

    internal sealed record Line(NativeTextParagraph Paragraph, uint Index,
        NativeMethods.ParagraphLineMetrics Metrics, NativeMethods.ParagraphFragment[] Fragments) : IPlatformTextLine, IPlatformTextPaintLine
    {
        private ParagraphDrawingHandle? DrawingHandle { get; init; }
        private readonly record struct SourcePaint(uint Start, uint Length, Color Color, double Opacity,
            uint[]? GlyphRanges, int Visibility, Rect Bounds);
        private SourcePaint? Paint { get; init; }
        internal Rect BrushBounds => Paint?.Bounds ?? new Rect(Metrics.Line.X, 0, Math.Max(1, Metrics.Line.Width), Math.Max(1, Metrics.Line.Height));
        internal Line ForSource(int start, int length, Color color, double opacity, uint[]? glyphRanges = null, int visibility = 0)
        {
            // Capture geometry while the layout owner is live; recorded lines
            // can sample their brush after that owner has been replaced/disposed.
            var rectangles = Paragraph.Selection(Index, start, length);
            double left = rectangles.Length == 0 ? Metrics.Line.X : rectangles.Min(rect => rect.X);
            double right = rectangles.Length == 0 ? left : rectangles.Max(rect => rect.X + rect.Width);
            return this with { Paint = new((uint)start, (uint)length, color, opacity, glyphRanges, visibility,
                new Rect(left, 0, Math.Max(1, right - left), Math.Max(1, Metrics.Line.Height))) };
        }

        internal Line WithBrushLineBox(double top, double height)
        {
            if (!double.IsFinite(top)) throw new ArgumentOutOfRangeException(nameof(top));
            if (!double.IsFinite(height) || height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (Paint is not { } paint) throw new InvalidOperationException("A source slice is required for a line paint box.");
            return this with { Paint = paint with { Bounds = new Rect(paint.Bounds.X, top, paint.Bounds.Width, height) } };
        }

        internal Line WithBrushHorizontalBox(double left, double width)
        {
            if (!double.IsFinite(left)) throw new ArgumentOutOfRangeException(nameof(left));
            if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (Paint is not { } paint) throw new InvalidOperationException("A source slice is required for a brush box.");
            return this with { Paint = paint with { Bounds = new Rect(left, paint.Bounds.Y, Math.Max(1, width), paint.Bounds.Height) } };
        }

        internal Line WithBrushSourceRange(int start, int length)
        {
            if (Paint is not { } paint) throw new InvalidOperationException("A source slice is required for a brush range.");
            var rectangles = Paragraph.Selection(Index, start, length);
            double left = rectangles.Length == 0 ? Metrics.Line.X : rectangles.Min(rect => rect.X);
            double right = rectangles.Length == 0 ? left : rectangles.Max(rect => rect.X + rect.Width);
            return this with { Paint = paint with { Bounds = new Rect(left, paint.Bounds.Y, Math.Max(1, right - left), paint.Bounds.Height) } };
        }

        object IPlatformTextLine.CreateRenderSnapshot() => DrawingHandle is not null ? this
            : this with
            {
                DrawingHandle = Paragraph.RetainForDrawing(),
            };

        object IPlatformTextPaintLine.CreatePaintSnapshot(Brush? foreground)
        {
            if (foreground is not SolidColorBrush brush) return ((IPlatformTextLine)this).CreateRenderSnapshot();
            var paint = Paint is { } source ? source with { Color = brush.Color, Opacity = brush.Opacity }
                : new SourcePaint(Metrics.Line.TextPosition, Metrics.Line.Length, brush.Color, brush.Opacity, null, 0, BrushBounds);
            return this with { Paint = paint, DrawingHandle = DrawingHandle ?? Paragraph.RetainForDrawing() };
        }

        internal unsafe bool DrawLine(nint target, float x, float y, NativeBrush? brush = null)
        {
            bool Draw(nint value)
            {
                if (Paint is not { } paint)
                    return brush is null ? NativeMethods.DrawParagraphLine(target, value, Index, x, y, 1) == 0
                        : NativeMethods.DrawParagraphBrush(target, value, Index, x, y, Metrics.Line.TextPosition, Metrics.Line.Length, brush.Handle) == 0;
                // FormattedText retains source-local ranges for recording and
                // generic contexts. The retained line uses whole-paragraph
                // coordinates while filtering its already shaped glyphs.
                if (paint.Visibility != 0)
                    fixed (uint* ranges = paint.GlyphRanges)
                        if (NativeMethods.SetTextGlyphRanges(target, ranges, (uint)(paint.GlyphRanges?.Length ?? 0) / 2, paint.Visibility) != JaliumResult.Ok)
                            return false;
                try
                {
                    if (brush is not null)
                        return NativeMethods.DrawParagraphBrush(target, value, Index, x, y, paint.Start, paint.Length, brush.Handle) == 0;
                    return NativeMethods.DrawParagraphSlice(target, value, Index, x, y, paint.Start, paint.Length,
                        paint.Color.R / 255f, paint.Color.G / 255f, paint.Color.B / 255f, paint.Color.A / 255f * (float)paint.Opacity) == 0;
                }
                finally { if (paint.Visibility != 0) NativeMethods.SetTextGlyphRanges(target, null, 0, 0); }
            }
            try { return DrawingHandle is { } handle ? Use(handle, Draw) : Paragraph.Use(Draw); }
            catch (ObjectDisposedException) { return false; }
        }
    }

    private readonly ParagraphHandle _handle;
    private int _disposed;
    internal string Text { get; }
    internal Line[] Lines { get; }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private unsafe NativeTextParagraph(nint handle, RenderContext.BackendResourceLease lease, string text, bool contextPaint = false)
    {
        ContextPaint = contextPaint;
        _handle = new ParagraphHandle(handle, lease);
        Text = text;
        try
        {
            Lines = Use(value =>
            {
                var lines = new Line[NativeMethods.TextParagraphLineCount(value)];
                for (uint i = 0; i < lines.Length; i++)
                {
                    if (NativeMethods.TextParagraphGetLine(value, i, out var metrics) != 0 ||
                        NativeMethods.TextParagraphGetFragments(value, i, null, 0, out uint count) != 0)
                        throw new InvalidOperationException("Cannot read shaped paragraph metrics.");
                    var fragments = new NativeMethods.ParagraphFragment[count];
                    fixed (NativeMethods.ParagraphFragment* buffer = fragments)
                        if (NativeMethods.TextParagraphGetFragments(value, i, buffer, count, out _) != 0)
                            throw new InvalidOperationException("Cannot read shaped paragraph fragments.");
                    lines[i] = new Line(this, i, metrics, fragments);
                }
                return lines;
            });
        }
        catch { _handle.Dispose(); throw; }
    }

    internal static NativeTextParagraph? TryCreate(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double width, double minLineHeight,
        TextAlignment alignment, FlowDirection direction, bool naturalDirection = false, bool noWrap = false,
        DependencyObject? textOptionsOwner = null, bool verticalForms = false, bool uprightForms = false,
        IReadOnlyList<NativeMethods.ParagraphCombineUnit>? combined = null)
        => TryCreateCore(spans, defaultFamily, defaultSize, width, minLineHeight, alignment, direction, naturalDirection, noWrap, null,
            textOptionsOwner: textOptionsOwner, verticalForms: verticalForms, uprightForms: uprightForms, combined: combined);

    // The immutable result uses logical inline/block coordinates, including
    // caret and selection geometry. Consumers place columns in physical space.
    internal static NativeTextParagraph? TryCreateVertical(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double inlineSize, double minColumnWidth,
        TextAlignment alignment, FlowDirection direction, bool noWrap = false,
        DependencyObject? textOptionsOwner = null)
        => TryCreateCore(spans, defaultFamily, defaultSize, inlineSize, minColumnWidth,
            alignment, direction, false, noWrap, null, textOptionsOwner: textOptionsOwner, verticalForms: true);

    internal static NativeTextParagraph? TryCreateUpright(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double inlineSize, double minColumnWidth,
        TextAlignment alignment, FlowDirection direction, bool noWrap = false,
        DependencyObject? textOptionsOwner = null)
        => TryCreateCore(spans, defaultFamily, defaultSize, inlineSize, minColumnWidth,
            alignment, direction, false, noWrap, null, textOptionsOwner: textOptionsOwner, verticalForms: true, uprightForms: true);

    internal static NativeTextParagraph? TryCreateSpaced(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double width, double minLineHeight,
        TextAlignment alignment, FlowDirection direction, IReadOnlyList<NativeMethods.ParagraphSpacingUnit> spacing)
        => TryCreateCore(spans, defaultFamily, defaultSize, width, minLineHeight, alignment, direction, false, true, spacing);

    internal static NativeTextParagraph? TryCreateContextSpaced(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double width, double minLineHeight,
        FlowDirection direction, IReadOnlyList<NativeMethods.ParagraphSpacingUnit> spacing)
        => TryCreateCore(spans, defaultFamily, defaultSize, width, minLineHeight, TextAlignment.Left, direction, false, true, spacing, true);

    internal static NativeTextParagraph? TryCreateAdvanced(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double width, double minLineHeight,
        FlowDirection direction, IReadOnlyList<NativeMethods.ParagraphAdvanceUnit> advances, bool contextPaint = false,
        bool naturalDirection = false, DependencyObject? textOptionsOwner = null, bool verticalForms = false, bool uprightForms = false,
        IReadOnlyList<NativeMethods.ParagraphCombineUnit>? combined = null)
        => TryCreateCore(spans, defaultFamily, defaultSize, width, minLineHeight, TextAlignment.Left, direction,
            naturalDirection, true, null, contextPaint, advances, textOptionsOwner, verticalForms, uprightForms, combined);

    private static unsafe NativeTextParagraph? TryCreateCore(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double width, double minLineHeight,
        TextAlignment alignment, FlowDirection direction, bool naturalDirection, bool noWrap,
        IReadOnlyList<NativeMethods.ParagraphSpacingUnit>? spacing, bool contextPaint = false,
        IReadOnlyList<NativeMethods.ParagraphAdvanceUnit>? advances = null, DependencyObject? textOptionsOwner = null,
        bool verticalForms = false, bool uprightForms = false,
        IReadOnlyList<NativeMethods.ParagraphCombineUnit>? combined = null)
    {
        // Native font formats require positive finite sizes. Preserve the managed
        // layout path for CSS zero-sized text, including mixed-size paragraphs.
        if (!double.IsFinite(defaultSize) || defaultSize <= 0 ||
            spans.Any(span => !double.IsFinite(span.FontSize) || span.FontSize <= 0)) return null;
        if (uprightForms && !verticalForms) return null;
        bool sourceOrientations = false;
        int paragraphOrientation = textOptionsOwner is null ? (uprightForms ? 1 : 0)
            : (int)Jalium.UI.Styling.CssWritingModeProperties.GetTextOrientation(textOptionsOwner);
        if (verticalForms && spans.Any(span => span.TextOrientation.HasValue))
        {
            int fallback = paragraphOrientation;
            int first = spans[0].TextOrientation ?? fallback;
            sourceOrientations = first != paragraphOrientation || spans.Any(span => (span.TextOrientation ?? fallback) != first);
            if (!sourceOrientations) { verticalForms = first != 2; uprightForms = first == 1; }
            else uprightForms = false; // Only the upright source ranges override bidi.
        }
        var context = RenderContext.Current;
        bool supported = OperatingSystem.IsMacOS() && context?.Backend is RenderBackend.Metal or RenderBackend.Software ||
            OperatingSystem.IsLinux() && context?.Backend is RenderBackend.Vulkan or RenderBackend.Software;
        if (!supported || context is not { IsValid: true } || !double.IsFinite(width) || width <= 0) return null;
        RenderContext.BackendResourceLease? lease = null;
        var formats = new List<NativeTextFormat.NativeLease>();
        var fontCache = new Dictionary<(string, double, int, int, int?), NativeTextFormat.NativeLease>();
        NativeTextFormat.NativeLease? AcquireFormat(string family, float size, int weight, int style, int? orientation = null)
        {
            if (textOptionsOwner is null && orientation is null) return TextMeasurement.AcquireParagraphFormat(context, family, size, weight, style);
            // A private input format captures per-element rendering options
            // without mutating a format shared by the measurement cache.
            using var format = TextMeasurement.CreateTextFormatFromFamilyList(context, family, size, weight, style);
            if (format is null) return null;
            if (textOptionsOwner is not null)
            {
                format.SetTextRenderingMode((int)TextOptions.GetTextRenderingMode(textOptionsOwner));
                format.SetTextFormattingMode((int)TextOptions.GetTextFormattingMode(textOptionsOwner));
                format.SetTextHintingMode((int)TextOptions.GetTextHintingMode(textOptionsOwner));
            }
            if (orientation is { } value && !format.TrySetTextOrientation(value, paragraphOrientation == 2 ? 1 : 0)) return null;
            return format.RetainForParagraph();
        }
        try
        {
            lease = context.AcquireBackendResourceLease();
            defaultFamily = Jalium.UI.Styling.CssFontFaces.MaterializeSource(defaultFamily, null);
            var defaultFormat = AcquireFormat(defaultFamily, (float)defaultSize, 400, 0);
            if (defaultFormat is null) return null;
            formats.Add(defaultFormat);
            fontCache[(defaultFamily, defaultSize, 400, 0, null)] = defaultFormat;
            string text = string.Concat(spans.Select(span => span.Text));
            var inputs = new NativeMethods.ParagraphSpan[spans.Count];
            uint offset = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                var family = Jalium.UI.Styling.CssFontFaces.MaterializeSource(span.FontFamily, span.Text);
                int? orientation = sourceOrientations ? span.TextOrientation ?? paragraphOrientation : null;
                var key = (family, span.FontSize, span.FontWeight, span.FontStyle, orientation);
                if (!fontCache.TryGetValue(key, out var format))
                {
                    format = AcquireFormat(family, (float)span.FontSize, span.FontWeight, span.FontStyle, orientation);
                    if (format is null) return null;
                    formats.Add(format); fontCache.Add(key, format);
                }
                inputs[i] = new NativeMethods.ParagraphSpan
                {
                    Format = format.Handle, TextPosition = offset, Length = (uint)span.Text.Length,
                    R = span.Color.R / 255f, G = span.Color.G / 255f,
                    B = span.Color.B / 255f, A = span.Color.A / 255f * (float)span.Opacity
                };
                offset += (uint)span.Text.Length;
            }
            nint handle;
            var spacingArray = spacing?.ToArray();
            var advanceArray = advances?.ToArray();
            var combineArray = combined?.ToArray();
            if (contextPaint && spacingArray is not { Length: > 0 } && advanceArray is not { Length: > 0 } && combineArray is not { Length: > 0 }) return null;
            fixed (NativeMethods.ParagraphCombineUnit* combine = combineArray)
            fixed (NativeMethods.ParagraphAdvanceUnit* advance = advanceArray)
            fixed (NativeMethods.ParagraphSpacingUnit* unit = spacingArray)
            fixed (NativeMethods.ParagraphSpan* input = inputs)
            {
                int baseDirection = naturalDirection ? -1 : direction == FlowDirection.RightToLeft ? 1 : 0;
                handle = combineArray is { Length: > 0 }
                    ? NativeMethods.TextParagraphCreateCombined(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, combine, (uint)combineArray.Length, advance, (uint)(advanceArray?.Length ?? 0),
                        (float)width, (float)minLineHeight, (int)alignment, baseDirection, noWrap ? 1 : 0,
                        uprightForms ? 1 : verticalForms ? 0 : 2, contextPaint ? 1 : 0)
                    : uprightForms && advanceArray is { Length: > 0 }
                    ? NativeMethods.TextParagraphCreateUprightAdvanced(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, advance, (uint)advanceArray.Length, (float)width, (float)minLineHeight,
                        baseDirection, contextPaint ? 1 : 0)
                    : uprightForms
                    ? NativeMethods.TextParagraphCreateUpright(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, (float)width, (float)minLineHeight, (int)alignment, baseDirection, noWrap ? 1 : 0)
                    : verticalForms && advanceArray is { Length: > 0 }
                    ? NativeMethods.TextParagraphCreateVerticalAdvanced(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, advance, (uint)advanceArray.Length, (float)width, (float)minLineHeight,
                        baseDirection, contextPaint ? 1 : 0)
                    : verticalForms
                    ? NativeMethods.TextParagraphCreateVertical(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, (float)width, (float)minLineHeight, (int)alignment, baseDirection, noWrap ? 1 : 0)
                    : advanceArray is { Length: > 0 }
                    ? contextPaint
                        ? NativeMethods.TextParagraphCreateContextAdvanced(defaultFormat.Handle, text, (uint)text.Length,
                            input, (uint)inputs.Length, advance, (uint)advanceArray.Length, (float)width, (float)minLineHeight, baseDirection)
                        : NativeMethods.TextParagraphCreateAdvanced(defaultFormat.Handle, text, (uint)text.Length,
                            input, (uint)inputs.Length, advance, (uint)advanceArray.Length, (float)width, (float)minLineHeight, baseDirection)
                    : spacingArray is { Length: > 0 }
                    ? contextPaint
                        ? NativeMethods.TextParagraphCreateContextSpaced(defaultFormat.Handle, text, (uint)text.Length,
                            input, (uint)inputs.Length, unit, (uint)spacingArray.Length, (float)width, (float)minLineHeight, baseDirection)
                        : NativeMethods.TextParagraphCreateSpaced(defaultFormat.Handle, text, (uint)text.Length,
                            input, (uint)inputs.Length, unit, (uint)spacingArray.Length, (float)width, (float)minLineHeight, baseDirection)
                    : noWrap
                    ? NativeMethods.TextParagraphCreateWithWrapping(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, (float)width, (float)minLineHeight, (int)alignment, baseDirection, 1)
                    : NativeMethods.TextParagraphCreate(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, (float)width, (float)minLineHeight, (int)alignment, baseDirection);
            }
            if (handle == 0) return null; // Older or unsupported native backend.
            var ownedLease = lease;
            lease = null; // The SafeHandle owns this even if reading metrics fails.
            return new NativeTextParagraph(handle, ownedLease, text, contextPaint);
        }
        catch (EntryPointNotFoundException) { return null; }
        catch (ObjectDisposedException) { return null; }
        finally
        {
            foreach (var format in formats) format.Dispose();
            lease?.Dispose();
        }
    }

    private ParagraphDrawingHandle RetainForDrawing()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new ParagraphDrawingHandle(_handle);
    }

    private T Use<T>(Func<nint, T> action)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return Use(_handle, action);
    }

    private static T Use<T>(SafeHandle handle, Func<nint, T> action)
    {
        bool pinned = false;
        try
        {
            handle.DangerousAddRef(ref pinned);
            return action(handle.DangerousGetHandle());
        }
        finally { if (pinned) handle.DangerousRelease(); }
    }

    internal NativeMethods.ParagraphCaret Caret(uint line, int position, bool backward) => Use(handle =>
    {
        if (NativeMethods.TextParagraphGetCaret(handle, line, (uint)position, backward ? 1 : 0, out var caret) != 0)
            throw new InvalidOperationException($"Cannot resolve shaped paragraph caret (line {line}, position {position}, backward {backward}; row {Lines[line].Metrics.Line.TextPosition}+{Lines[line].Metrics.Line.Length}).");
        return caret;
    });
    internal NativeMethods.ParagraphCaret HitTest(uint line, double x) => Use(handle =>
    {
        if (NativeMethods.TextParagraphHitTest(handle, line, (float)x, out var caret) != 0)
            throw new InvalidOperationException("Cannot hit test shaped paragraph.");
        return caret;
    });
    internal unsafe TextRangeMetrics[] Selection(uint line, int start, int length) => Use(handle =>
    {
        if (NativeMethods.TextParagraphGetSelection(handle, line, (uint)start, (uint)length, null, 0, out uint count) != 0)
            throw new InvalidOperationException("Cannot measure shaped paragraph selection.");
        var rectangles = new TextRangeMetrics[count];
        fixed (TextRangeMetrics* buffer = rectangles)
            if (NativeMethods.TextParagraphGetSelection(handle, line, (uint)start, (uint)length, buffer, count, out _) != 0)
                throw new InvalidOperationException("Cannot read shaped paragraph selection.");
        return rectangles;
    });
    internal TextRangeMetrics? SourceBounds(uint line, int start, int length)
    {
        try { return Use<TextRangeMetrics?>(handle => NativeMethods.TextParagraphGetSourceBounds(handle, line, (uint)start, (uint)length, out var result) == 0 ? result : null); }
        catch (EntryPointNotFoundException) { return null; }
    }
    internal unsafe byte[]? PositionModes()
    {
        try
        {
            return Use<byte[]?>(handle => {
                var modes = new byte[Text.Length];
                fixed (byte* output = modes)
                {
                    var status = (JaliumResult)NativeMethods.TextParagraphGetPositionModes(handle, output, (uint)modes.Length);
                    if (status == JaliumResult.NotSupported) return null;
                    if (status != JaliumResult.Ok) throw new InvalidOperationException($"Cannot query font position modes: {status}");
                }
                return modes;
            });
        }
        catch (EntryPointNotFoundException) { return null; }
    }

    internal unsafe byte[]? CapsOperations()
    {
        try
        {
            return Use<byte[]?>(handle => {
                var operations = new byte[Text.Length];
                fixed (byte* output = operations)
                {
                    var status = (JaliumResult)NativeMethods.TextParagraphGetCapsOperations(handle, output, (uint)operations.Length);
                    if (status == JaliumResult.NotSupported) return null;
                    if (status != JaliumResult.Ok) throw new InvalidOperationException($"Cannot query font caps operations: {status}");
                }
                return operations;
            });
        }
        catch (EntryPointNotFoundException) { return null; }
    }
    internal bool DrawLine(nint target, uint line, float x, float y)
    {
        try { return Use(handle => NativeMethods.DrawParagraphLine(target, handle, line, x, y, 1) == 0); }
        catch (ObjectDisposedException) { return false; }
    }

    internal static unsafe bool TryNavigateWord(string text,
        IReadOnlyList<(NativeTextParagraph Paragraph, int Offset)> sources,
        int position, int selectionStart, int selectionLength, bool right,
        bool backwardAffinity, out NativeMethods.ParagraphCaret destination)
    {
        destination = default;
        if (sources.Count == 0 || position < 0 || position > text.Length || selectionStart < 0 ||
            selectionLength < 0 || selectionStart > text.Length - selectionLength) return false;
        var handles = new nint[sources.Count];
        var offsets = new uint[sources.Count];
        var pinned = new bool[sources.Count];
        try
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].Offset < 0) return false;
                if (sources[i].Paragraph.IsDisposed) return false;
                sources[i].Paragraph._handle.DangerousAddRef(ref pinned[i]);
                handles[i] = sources[i].Paragraph._handle.DangerousGetHandle();
                offsets[i] = (uint)sources[i].Offset;
            }
            fixed (nint* paragraphs = handles)
            fixed (uint* starts = offsets)
                return NativeMethods.TextParagraphNavigateWord(paragraphs, starts, (uint)sources.Count,
                    text, (uint)text.Length, (uint)position, (uint)selectionStart, (uint)selectionLength,
                    right ? 2 : 3, backwardAffinity ? 1 : 0, out destination) == 0;
        }
        catch (EntryPointNotFoundException) { return false; }
        catch (ObjectDisposedException) { return false; }
        finally
        {
            for (int i = 0; i < sources.Count; i++)
                if (pinned[i]) sources[i].Paragraph._handle.DangerousRelease();
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _handle.Dispose();
    }
}
