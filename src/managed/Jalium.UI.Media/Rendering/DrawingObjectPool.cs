using System;
using System.Collections.Generic;

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
///     The rendering backend (<c>RenderTargetDrawingContext</c>) keeps a
///     <c>Dictionary&lt;Brush, NativeBrush&gt;</c> identity cache that only
///     hits when the same managed brush reference is used across frames.
///     User code that writes <c>new SolidColorBrush(color)</c> in a hot
///     <c>OnRender</c> loop produces a fresh reference every frame and
///     forces a native-brush miss every draw. Canonicalizing at record
///     time redirects all such references onto one pooled instance, so
///     the native cache hits on subsequent frames.
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
/// The pool does not try to canonicalize gradient brushes, image brushes,
/// or pens with custom dash / cap / join / miter settings. Those fall
/// through as-is; users who want them deduplicated should cache them
/// themselves (standard WPF pattern).
/// </para>
/// <para>
/// Each sub-pool has a hard cap (<see cref="MaxPoolSize"/>); overflowing
/// clears the pool wholesale rather than evicting one-by-one. This is
/// intentionally coarse — in normal apps the pool stabilises at &lt;100
/// entries, and a pathological creation rate is better served by the
/// full reset than by an LRU walk on every hit.
/// </para>
/// </remarks>
internal static class DrawingObjectPool
{
    private const int MaxPoolSize = 512;

    private static readonly object s_lock = new();

    private static readonly Dictionary<uint, SolidColorBrush> s_solidBrushes = new();
    private static readonly Dictionary<long, Pen> s_simplePens = new();
    private static readonly Dictionary<int, FormattedText> s_formattedTexts = new();

    /// <summary>
    /// Returns a canonical instance for <paramref name="brush"/> when it is
    /// a <see cref="SolidColorBrush"/>; otherwise returns it unchanged. A
    /// <c>null</c> input yields <c>null</c>.
    /// </summary>
    public static Brush? CanonicalizeBrush(Brush? brush)
    {
        if (brush is not SolidColorBrush scb)
        {
            return brush;
        }

        uint key = PackArgb(scb.Color);
        lock (s_lock)
        {
            if (s_solidBrushes.TryGetValue(key, out var canonical))
            {
                return canonical;
            }

            if (s_solidBrushes.Count >= MaxPoolSize)
            {
                s_solidBrushes.Clear();
            }

            // Store a fresh copy so mutation of the user's brush after record
            // cannot leak into the canonical (user's Color could legally be
            // changed later — we don't want that to change pool entries).
            var pooled = new SolidColorBrush(scb.Color);
            s_solidBrushes[key] = pooled;
            return pooled;
        }
    }

    /// <summary>
    /// Returns a canonical instance for a "simple" pen — solid-color brush,
    /// no dash style, flat caps, miter join, default miter limit — so that
    /// identical stroke settings across visuals share a reference. Any pen
    /// with non-default attributes is returned unchanged.
    /// </summary>
    public static Pen? CanonicalizePen(Pen? pen)
    {
        if (pen is null)
        {
            return null;
        }
        if (pen.DashStyle is not null)
        {
            return pen;
        }
        if (pen.StartLineCap != PenLineCap.Flat ||
            pen.EndLineCap != PenLineCap.Flat ||
            pen.DashCap != PenLineCap.Flat ||
            pen.LineJoin != PenLineJoin.Miter)
        {
            return pen;
        }
        if (pen.MiterLimit != 10)
        {
            return pen;
        }
        if (pen.Brush is not SolidColorBrush scb)
        {
            return pen;
        }

        uint colorKey = PackArgb(scb.Color);
        long thicknessBits = BitConverter.DoubleToInt64Bits(pen.Thickness);
        long key = ((long)colorKey << 32) ^ thicknessBits;

        lock (s_lock)
        {
            if (s_simplePens.TryGetValue(key, out var canonical))
            {
                return canonical;
            }

            if (s_simplePens.Count >= MaxPoolSize)
            {
                s_simplePens.Clear();
            }

            // Use the canonical brush too so the native side also deduplicates.
            var canonicalBrush = s_solidBrushes.TryGetValue(colorKey, out var existingBrush)
                ? existingBrush
                : s_solidBrushes[colorKey] = new SolidColorBrush(scb.Color);

            var pooledPen = new Pen(canonicalBrush, pen.Thickness);
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

        lock (s_lock)
        {
            if (s_formattedTexts.TryGetValue(hash, out var canonical) &&
                FormattedTextValueEquals(canonical, text, canonicalFg))
            {
                return canonical;
            }

            if (s_formattedTexts.Count >= MaxPoolSize)
            {
                s_formattedTexts.Clear();
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
