using Microsoft.Win32.SafeHandles;
using Jalium.UI.Media;
using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>One immutable CoreText paragraph shared by layout, drawing and hit testing.</summary>
internal sealed class NativeTextParagraph : IDisposable
{
    internal readonly record struct Span(string Text, string FontFamily, double FontSize,
        int FontWeight, int FontStyle, Color Color, double Opacity = 1);

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
        NativeMethods.ParagraphLineMetrics Metrics, NativeMethods.ParagraphFragment[] Fragments) : IPlatformTextLine
    {
        private ParagraphDrawingHandle? DrawingHandle { get; init; }

        object IPlatformTextLine.CreateRenderSnapshot() => DrawingHandle is not null ? this
            : new Line(Paragraph, Index, Metrics, Fragments)
            {
                DrawingHandle = Paragraph.RetainForDrawing(),
            };

        internal bool DrawLine(nint target, float x, float y) => DrawingHandle is { } handle
            ? Use(handle, value => NativeMethods.DrawParagraphLine(target, value, Index, x, y, 1) == 0)
            : Paragraph.DrawLine(target, Index, x, y);
    }

    private readonly ParagraphHandle _handle;
    private int _disposed;
    internal string Text { get; }
    internal Line[] Lines { get; }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private unsafe NativeTextParagraph(nint handle, RenderContext.BackendResourceLease lease, string text)
    {
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

    internal static unsafe NativeTextParagraph? TryCreate(IReadOnlyList<Span> spans,
        string defaultFamily, double defaultSize, double width, double minLineHeight,
        TextAlignment alignment, FlowDirection direction, bool naturalDirection = false, bool noWrap = false)
    {
        var context = RenderContext.Current;
        if (!OperatingSystem.IsMacOS() || context is not { IsValid: true, Backend: RenderBackend.Metal } ||
            !double.IsFinite(width) || width <= 0) return null;
        RenderContext.BackendResourceLease? lease = null;
        var formats = new List<NativeTextFormat>();
        var fontCache = new Dictionary<(string, double, int, int), NativeTextFormat>();
        try
        {
            lease = context.AcquireBackendResourceLease();
            defaultFamily = Jalium.UI.Styling.CssFontFaces.MaterializeSource(defaultFamily, null);
            var defaultFormat = TextMeasurement.CreateTextFormatFromFamilyList(context, defaultFamily, (float)defaultSize, 400, 0);
            formats.Add(defaultFormat);
            fontCache[(defaultFamily, defaultSize, 400, 0)] = defaultFormat;
            string text = string.Concat(spans.Select(span => span.Text));
            var inputs = new NativeMethods.ParagraphSpan[spans.Count];
            uint offset = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                var family = Jalium.UI.Styling.CssFontFaces.MaterializeSource(span.FontFamily, span.Text);
                var key = (family, span.FontSize, span.FontWeight, span.FontStyle);
                if (!fontCache.TryGetValue(key, out var format))
                {
                    format = TextMeasurement.CreateTextFormatFromFamilyList(context, family, (float)span.FontSize, span.FontWeight, span.FontStyle);
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
            fixed (NativeMethods.ParagraphSpan* input = inputs)
            {
                int baseDirection = naturalDirection ? -1 : direction == FlowDirection.RightToLeft ? 1 : 0;
                handle = noWrap
                    ? NativeMethods.TextParagraphCreateWithWrapping(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, (float)width, (float)minLineHeight, (int)alignment, baseDirection, 1)
                    : NativeMethods.TextParagraphCreate(defaultFormat.Handle, text, (uint)text.Length,
                        input, (uint)inputs.Length, (float)width, (float)minLineHeight, (int)alignment, baseDirection);
            }
            if (handle == 0) return null; // Older or unsupported native backend.
            var ownedLease = lease;
            lease = null; // The SafeHandle owns this even if reading metrics fails.
            return new NativeTextParagraph(handle, ownedLease, text);
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
            throw new InvalidOperationException("Cannot resolve shaped paragraph caret.");
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
