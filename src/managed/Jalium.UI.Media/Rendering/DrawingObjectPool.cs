using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// Canonicalizes the "value-typed" media primitives captured during draw
/// recording — solid-color brushes, simple pens, and formatted text layouts —
/// so that logically-equal user allocations collapse to a single shared
/// instance inside recorded <see cref="RecordedDrawing"/> command lists.
/// </summary>
/// <remarks>
/// <para>
/// The pool exists for two reasons, in order of impact:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     Canonical brushes and pens are frozen, so draw-input snapshots can
///     share them directly. User code that constructs equivalent mutable
///     primitives in a hot <c>OnRender</c> loop otherwise requires fresh
///     freeze-clones and their dependency-property storage on every capture.
///     </description>
///   </item>
///   <item>
///     <description>
///     Shared canonical instances reduce GC pressure on replay-heavy
///     scenarios by keeping the working set of rendering primitives
///     bounded, and reduce the retained Drawing size by replacing many
///     user allocations with a handful of pooled ones.
///     </description>
///   </item>
/// </list>
/// <para>
/// Gradient brushes, image brushes, and custom dash patterns keep their
/// specialized snapshot paths. Undashed solid pens preserve all stroke settings.
/// </para>
/// <para>
/// Each sub-pool has a hard cap (<see cref="MaxPoolSize"/>). Clock eviction
/// gives recently used values a second chance without flushing every hot
/// value when an animation introduces new colors.
/// </para>
/// </remarks>
internal static class DrawingObjectPool
{
    private const int MaxPoolSize = 512;

    private static readonly object s_lock = new();

    private readonly record struct SolidBrushKey(uint Color, long Opacity);
    private readonly record struct SolidPenKey(SolidBrushKey Brush, long Thickness,
        PenLineCap StartCap, PenLineCap EndCap, PenLineCap DashCap, PenLineJoin Join, long MiterLimit);

    private static readonly ValueCache<SolidBrushKey, SolidColorBrush> s_solidBrushes = new();
    private static readonly ValueCache<SolidPenKey, Pen> s_simplePens = new();
    private static readonly ValueCache<int, FormattedText> s_formattedTexts = new();

    // 写入与清空由 s_lock 串行化；命中只读字典并设置一个引用位，不分配对象。
    private sealed class ValueCache<TKey, TValue> where TKey : notnull where TValue : class
    {
        private sealed class Entry(TKey key, TValue value)
        {
            internal readonly TKey Key = key;
            internal readonly TValue Value = value;
            internal int Referenced;
        }

        private readonly ConcurrentDictionary<TKey, Entry> _values = new();
        private readonly Queue<Entry> _clock = new(MaxPoolSize);

        internal bool TryGetValue(TKey key, out TValue value)
        {
            if (_values.TryGetValue(key, out var entry))
            {
                Volatile.Write(ref entry.Referenced, 1);
                value = entry.Value;
                return true;
            }
            value = null!;
            return false;
        }

        internal TValue this[TKey key]
        {
            set
            {
                int secondChances = MaxPoolSize;
                while (_clock.Count >= MaxPoolSize)
                {
                    var candidate = _clock.Dequeue();
                    if (!_values.TryGetValue(candidate.Key, out var current) || !ReferenceEquals(candidate, current))
                        continue;
                    if (Interlocked.Exchange(ref candidate.Referenced, 0) != 0 && secondChances-- > 0)
                    {
                        _clock.Enqueue(candidate);
                        continue;
                    }
                    _values.TryRemove(candidate.Key, out _);
                }
                var entry = new Entry(key, value);
                _values[key] = entry;
                _clock.Enqueue(entry);
            }
        }

        internal void Clear()
        {
            _values.Clear();
            _clock.Clear();
        }
    }

    /// <summary>
    /// Returns a canonical instance for <paramref name="brush"/> when it is
    /// a <see cref="SolidColorBrush"/>; otherwise returns it unchanged. A
    /// <c>null</c> input yields <c>null</c>.
    /// </summary>
    public static Brush? CanonicalizeBrush(Brush? brush)
    {
        if (brush is not SolidColorBrush scb || scb.CssGradientLayout is not null)
        {
            return brush;
        }

        var color = scb.Color;
        double opacity = scb.Opacity;
        var key = new SolidBrushKey(PackArgb(color), BitConverter.DoubleToInt64Bits(opacity));
        if (s_solidBrushes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        lock (s_lock)
        {
            if (s_solidBrushes.TryGetValue(key, out var canonical))
            {
                return canonical;
            }

            // Store a fresh copy so mutation of the user's brush after record
            // cannot leak into the canonical (user's Color could legally be
            // changed later — we don't want that to change pool entries).
            var pooled = new SolidColorBrush(color);
            if (opacity != 1.0) pooled.Opacity = opacity;
            pooled.Freeze();
            s_solidBrushes[key] = pooled;
            return pooled;
        }
    }

    /// <summary>
    /// Returns a frozen canonical instance for an undashed solid pen,
    /// preserving opacity, thickness, caps, join, and miter limit.
    /// </summary>
    public static Pen? CanonicalizePen(Pen? pen)
    {
        if (pen is null)
        {
            return null;
        }
        if (pen.DashStyle.Dashes.Count != 0)
        {
            return pen;
        }
        if (pen.Brush is not SolidColorBrush scb || scb.CssGradientLayout is not null)
        {
            return pen;
        }

        var brushKey = new SolidBrushKey(PackArgb(scb.Color), BitConverter.DoubleToInt64Bits(scb.Opacity));
        double thickness = pen.Thickness;
        var startCap = pen.StartLineCap;
        var endCap = pen.EndLineCap;
        var dashCap = pen.DashCap;
        var join = pen.LineJoin;
        double miter = pen.MiterLimit;
        var key = new SolidPenKey(brushKey, BitConverter.DoubleToInt64Bits(thickness),
            startCap, endCap, dashCap, join, BitConverter.DoubleToInt64Bits(miter));

        if (s_simplePens.TryGetValue(key, out var cached))
        {
            return cached;
        }

        lock (s_lock)
        {
            if (s_simplePens.TryGetValue(key, out var canonical))
            {
                return canonical;
            }

            // Use the canonical brush too so the native side also deduplicates.
            var canonicalBrush = CanonicalizeBrush(scb)!;
            var pooledPen = new Pen(canonicalBrush, thickness);
            if (startCap != PenLineCap.Flat) pooledPen.StartLineCap = startCap;
            if (endCap != PenLineCap.Flat) pooledPen.EndLineCap = endCap;
            if (dashCap != PenLineCap.Square) pooledPen.DashCap = dashCap;
            if (join != PenLineJoin.Miter) pooledPen.LineJoin = join;
            if (miter != 10.0) pooledPen.MiterLimit = miter;
            pooledPen.Freeze();
            s_simplePens[key] = pooledPen;
            return pooledPen;
        }
    }

    /// <summary>
    /// Returns a canonical <see cref="FormattedText"/> with the same
    /// observable value fields as <paramref name="text"/>. The returned
    /// instance's <c>Foreground</c> is itself canonicalised; two calls with
    /// the same text / font / size / weight / style / stretch / wrap
    /// constraints / trimming / foreground colour return the same reference.
    /// </summary>
    public static FormattedText CanonicalizeFormattedText(FormattedText text)
    {
        var canonicalFg = CanonicalizeBrush(text.Foreground);
        int hash = HashFormattedText(text, canonicalFg);

        if (s_formattedTexts.TryGetValue(hash, out var cached) &&
            FormattedTextValueEquals(cached, text, canonicalFg))
        {
            return cached;
        }

        lock (s_lock)
        {
            if (s_formattedTexts.TryGetValue(hash, out var canonical) &&
                FormattedTextValueEquals(canonical, text, canonicalFg))
            {
                return canonical;
            }

            // Snapshot the already-computed value state. Reconstructing the
            // FormattedText here used to repeat character-format creation and
            // approximate layout for every pool miss (the common case for
            // virtualized rows whose text is unique).
            var snapshot = text.CreateRenderSnapshot(canonicalFg);

            s_formattedTexts[hash] = snapshot;
            return snapshot;
        }
    }

    /// <summary>
    /// Clears every sub-pool. Exposed for tests and for callers that know a
    /// pathological burst of distinct brushes / pens / texts is about to
    /// flush through and want to bound memory proactively.
    /// </summary>
    public static void Clear()
    {
        lock (s_lock)
        {
            s_solidBrushes.Clear();
            s_simplePens.Clear();
            s_formattedTexts.Clear();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static uint PackArgb(Color c)
    {
        return ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    }

    private static int HashFormattedText(FormattedText text, Brush? foreground)
    {
        // Two-step combine because HashCode.Combine takes max 8 args.
        int h1 = HashCode.Combine(
            text.Text,
            text.FontFamily,
            text.FontSize,
            text.FontWeight,
            text.FontStyle,
            text.FontStretch,
            text.MaxTextWidth,
            text.MaxTextHeight);
        return HashCode.Combine(
            h1,
            text.Trimming,
            foreground is SolidColorBrush scb ? (int)PackArgb(scb.Color) : 0);
    }

    private static bool FormattedTextValueEquals(FormattedText a, FormattedText b, Brush? canonicalForeground)
    {
        return a.Text == b.Text
            && a.FontFamily == b.FontFamily
            && a.FontSize == b.FontSize
            && a.FontWeight == b.FontWeight
            && a.FontStyle == b.FontStyle
            && a.FontStretch == b.FontStretch
            && a.MaxTextWidth == b.MaxTextWidth
            && a.MaxTextHeight == b.MaxTextHeight
            && a.Trimming == b.Trimming
            && ReferenceEquals(a.Foreground, canonicalForeground);
    }
}
