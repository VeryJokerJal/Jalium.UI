using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jalium.UI.Controls;

/// <summary>
/// Navigates and splits UTF-16 text by <em>grapheme cluster</em> — one
/// user-perceived character — instead of by <see cref="char"/> (a single UTF-16
/// code unit) or by Unicode scalar value (a code point).
/// </summary>
/// <remarks>
/// <para>
/// A single grapheme cluster routinely spans several <see cref="char"/> values:
/// </para>
/// <list type="bullet">
///   <item><description>A supplementary code point (most emoji, e.g. 🚀) is a
///   <em>surrogate pair</em> — two <see cref="char"/>s.</description></item>
///   <item><description>A skin-tone emoji (👋🏿) is a base emoji plus a modifier,
///   four <see cref="char"/>s.</description></item>
///   <item><description>A ZWJ sequence (👨‍👩‍👧, or the Emoji 15.1 "head shaking"
///   face 🙂‍↔️) joins several emoji with U+200D — up to a dozen
///   <see cref="char"/>s.</description></item>
///   <item><description>A flag (🇨🇳) is a pair of regional-indicator symbols.</description></item>
///   <item><description>A base letter plus combining marks (e + U+0301) or a
///   keycap (1️⃣) is one cluster.</description></item>
/// </list>
/// <para>
/// Text editing that steps by <see cref="char"/> — or even by code point — cuts
/// these clusters apart: the caret lands inside an emoji, a selection highlights
/// half of one, Backspace orphans a fragment that renders as a "tofu" box. Every
/// caret, selection, hit-test and delete operation must therefore move by whole
/// grapheme clusters. This type is the single shared implementation every text
/// control routes through.
/// </para>
/// <para>
/// Cluster boundaries follow Unicode Standard Annex #29 <em>extended grapheme
/// cluster</em> rules. The scan delegates to
/// <see cref="StringInfo.GetTextElementEnumerator(string)"/>, whose text-element
/// segmentation has implemented UAX #29 — including the emoji ZWJ and
/// regional-indicator rules — since .NET 5. Relying on the framework segmenter
/// rather than a hand-rolled table keeps cluster handling correct as the runtime
/// tracks new Unicode versions. This is deliberately <em>not</em> the legacy
/// <see cref="StringInfo.ParseCombiningCharacters(string)"/>, which groups only a
/// base character with its combining marks and still splits ZWJ sequences and
/// emoji modifiers.
/// </para>
/// <para>
/// All offsets are UTF-16 code-unit indices into the supplied string, within
/// <c>[0, text.Length]</c>. The boundary set always contains <c>0</c> and
/// <c>text.Length</c>. The most recent scan is memoised per thread and keyed on
/// string reference identity, so repeated navigation between edits — and the
/// inner loops of word selection — stays cheap without re-scanning. Callers that
/// loop should fetch the text once and pass that same instance to every call.
/// </para>
/// </remarks>
internal static class GraphemeClusters
{
    /// <summary>Boundary set for empty or <see langword="null"/> text.</summary>
    private static readonly int[] EmptyBoundaries = { 0 };

    /// <summary>The text behind <see cref="_cachedBoundaries"/>; identity-compared.</summary>
    [ThreadStatic]
    private static string? _cachedText;

    /// <summary>Memoised boundary set for <see cref="_cachedText"/>.</summary>
    [ThreadStatic]
    private static int[]? _cachedBoundaries;

    /// <summary>
    /// Returns the ascending set of grapheme-cluster boundary offsets for
    /// <paramref name="text"/>, always including <c>0</c> and <c>text.Length</c>.
    /// The array is shared, memoised state — callers must treat it as read-only.
    /// </summary>
    internal static int[] GetBoundaries(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return EmptyBoundaries;
        }

        // Strings are immutable, so reference identity is enough to know a
        // previous scan is still valid for this exact text.
        if (ReferenceEquals(text, _cachedText) && _cachedBoundaries is not null)
        {
            return _cachedBoundaries;
        }

        var boundaries = new List<int>(text.Length + 1);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            // ElementIndex is the start offset of each text element; the first
            // element always starts at 0.
            boundaries.Add(enumerator.ElementIndex);
        }
        boundaries.Add(text.Length);

        var result = boundaries.ToArray();
        _cachedText = text;
        _cachedBoundaries = result;
        return result;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="offset"/> sits on a
    /// grapheme-cluster boundary — i.e. it does not cut a user-perceived
    /// character in half. <c>0</c> and <c>text.Length</c> are always boundaries;
    /// an out-of-range offset is never a boundary.
    /// </summary>
    internal static bool IsBoundary(string? text, int offset)
    {
        return Array.BinarySearch(GetBoundaries(text), offset) >= 0;
    }

    /// <summary>
    /// Returns the first grapheme-cluster boundary strictly greater than
    /// <paramref name="offset"/>, so a rightward caret step or forward delete
    /// always moves over one whole user-perceived character. An offset at or past
    /// the end returns <c>text.Length</c>.
    /// </summary>
    internal static int NextBoundary(string? text, int offset)
    {
        var boundaries = GetBoundaries(text);
        int end = boundaries[^1];
        if (offset < 0)
        {
            return boundaries[0];
        }
        if (offset >= end)
        {
            return end;
        }

        int index = Array.BinarySearch(boundaries, offset);
        // index >= 0: offset is itself a boundary — advance to the next one.
        // index <  0: ~index is the first boundary greater than offset.
        index = index >= 0 ? index + 1 : ~index;
        return boundaries[index];
    }

    /// <summary>
    /// Returns the last grapheme-cluster boundary strictly less than
    /// <paramref name="offset"/>, so a leftward caret step or Backspace always
    /// consumes one whole user-perceived character. An offset at or before the
    /// start returns <c>0</c>.
    /// </summary>
    internal static int PreviousBoundary(string? text, int offset)
    {
        var boundaries = GetBoundaries(text);
        if (offset <= 0)
        {
            return 0;
        }
        int end = boundaries[^1];
        if (offset > end)
        {
            return end;
        }

        int index = Array.BinarySearch(boundaries, offset);
        // index >= 0: offset is a boundary — step back to the previous one.
        // index <  0: ~index is the first boundary greater than offset, so the
        //             boundary before it is the largest one less than offset.
        index = index >= 0 ? index : ~index;
        return boundaries[index - 1];
    }

    /// <summary>
    /// Snaps <paramref name="offset"/> onto a grapheme-cluster boundary. An offset
    /// already on a boundary is returned unchanged; one that falls inside a
    /// cluster moves to the next boundary when <paramref name="forward"/> is
    /// <see langword="true"/>, otherwise to the previous one. The result is
    /// always clamped to <c>[0, text.Length]</c>.
    /// </summary>
    internal static int Snap(string? text, int offset, bool forward)
    {
        var boundaries = GetBoundaries(text);
        int end = boundaries[^1];
        if (offset <= 0)
        {
            return 0;
        }
        if (offset >= end)
        {
            return end;
        }
        if (Array.BinarySearch(boundaries, offset) >= 0)
        {
            return offset;
        }
        return forward ? NextBoundary(text, offset) : PreviousBoundary(text, offset);
    }

    /// <summary>
    /// Snaps <paramref name="offset"/> to the <em>nearest</em> grapheme-cluster
    /// boundary, by UTF-16 distance, preferring the later boundary on a tie. Use
    /// this for hit-testing, where a click landing inside a cluster should
    /// resolve to whichever edge of that cluster is closer to the pointer.
    /// </summary>
    internal static int SnapNearest(string? text, int offset)
    {
        var boundaries = GetBoundaries(text);
        int end = boundaries[^1];
        if (offset <= 0)
        {
            return 0;
        }
        if (offset >= end)
        {
            return end;
        }
        if (Array.BinarySearch(boundaries, offset) >= 0)
        {
            return offset;
        }
        int previous = PreviousBoundary(text, offset);
        int next = NextBoundary(text, offset);
        return (offset - previous) < (next - offset) ? previous : next;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into its grapheme clusters, one string per
    /// user-perceived character, in order. Returns an empty list for empty or
    /// <see langword="null"/> input.
    /// </summary>
    internal static List<string> Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var boundaries = GetBoundaries(text);
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            result.Add(text.Substring(boundaries[i], boundaries[i + 1] - boundaries[i]));
        }
        return result;
    }
}
