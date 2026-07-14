using System.Globalization;
using System.Text;

namespace Jalium.UI;

/// <summary>
/// A plain-text snapshot supplied to a native input-method context.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CursorIndex"/> and <see cref="AnchorIndex"/> are managed
/// UTF-16 indices into <see cref="Text"/>. Native Wayland text-input
/// protocols use UTF-8 byte offsets instead; the window backend performs that
/// conversion before crossing the native ABI.
/// </para>
/// <para>
/// When there is no selection, cursor and anchor are equal. When text is
/// selected they identify its active end and opposite anchor respectively.
/// </para>
/// </remarks>
internal readonly record struct ImeSurroundingTextSnapshot(
    string Text,
    int CursorIndex,
    int AnchorIndex);

/// <summary>
/// Interface for elements that support IME (Input Method Editor) input.
/// </summary>
internal interface IImeSupport
{
    /// <summary>
    /// Gets whether the element currently accepts IME composition input.
    /// Read-only text controls return <c>false</c> so the host window detaches
    /// the IME context and the candidate / composition window stays hidden.
    /// </summary>
    bool IsImeAllowed => true;

    /// <summary>
    /// Tries to expose the editable plain text around the current selection.
    /// </summary>
    /// <param name="snapshot">
    /// Receives the text and UTF-16 cursor/anchor indices when supported.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when surrounding text may be shared with the
    /// input method; otherwise <see langword="false"/>. The default is
    /// intentionally safe and exposes no text, which is appropriate for
    /// password, terminal, and custom controls until they opt in explicitly.
    /// </returns>
    bool TryGetImeSurroundingText(out ImeSurroundingTextSnapshot snapshot)
    {
        snapshot = default;
        return false;
    }

    /// <summary>
    /// Deletes text requested by the native input method around the current
    /// cursor, including the current selection.
    /// </summary>
    /// <param name="beforeUtf8ByteCount">
    /// Number of UTF-8 bytes requested before the selection/cursor.
    /// </param>
    /// <param name="afterUtf8ByteCount">
    /// Number of UTF-8 bytes requested after the selection/cursor.
    /// </param>
    /// <returns><see langword="true"/> when the request was applied.</returns>
    /// <remarks>
    /// The counts come directly from native Wayland and are UTF-8 byte counts,
    /// not managed character indices. Implementations must convert them to
    /// UTF-16 and must not split a Unicode scalar or grapheme cluster. The
    /// default rejects the request without mutating control state.
    /// </remarks>
    bool DeleteImeSurroundingText(int beforeUtf8ByteCount, int afterUtf8ByteCount) => false;

    /// <summary>
    /// Gets the caret position for IME composition window positioning.
    /// </summary>
    /// <returns>The caret position in element-local DIPs.</returns>
    Point GetImeCaretPosition();

    /// <summary>
    /// Gets the caret rectangle in element-local DIPs for native candidate
    /// window placement.
    /// </summary>
    /// <remarks>
    /// Existing implementations that only expose a caret point remain valid;
    /// their point is represented as a one-DIP rectangle.
    /// </remarks>
    Rect GetImeCaretRectangle()
    {
        Point point = GetImeCaretPosition();
        return new Rect(point.X, point.Y, 1, 1);
    }

    /// <summary>
    /// Called when IME composition starts.
    /// </summary>
    void OnImeCompositionStart();

    /// <summary>
    /// Called when the IME composition string is updated.
    /// </summary>
    /// <param name="compositionString">The current composition string.</param>
    /// <param name="cursorPosition">The cursor position within the composition string.</param>
    void OnImeCompositionUpdate(string compositionString, int cursorPosition);

    /// <summary>
    /// Called when IME composition ends.
    /// </summary>
    /// <param name="resultString">The final committed string, or null if cancelled.</param>
    void OnImeCompositionEnd(string? resultString);
}

/// <summary>
/// Shared UTF-16/UTF-8 mapping for managed IME surrounding-text contracts.
/// </summary>
internal static class ImeTextEncoding
{
    internal const int MaximumSurroundingTextUtf8Bytes = 4000;

    internal static int GetUtf8ByteOffset(string? text, int utf16Offset)
    {
        text ??= string.Empty;
        utf16Offset = Math.Clamp(utf16Offset, 0, text.Length);

        // Native offsets may never point into the low half of a surrogate
        // pair. Snap backward so malformed caller state cannot produce an
        // invalid Wayland cursor/anchor byte offset.
        if (utf16Offset > 0 && utf16Offset < text.Length &&
            char.IsHighSurrogate(text[utf16Offset - 1]) &&
            char.IsLowSurrogate(text[utf16Offset]))
        {
            utf16Offset--;
        }

        return Encoding.UTF8.GetByteCount(text.AsSpan(0, utf16Offset));
    }

    internal static bool TryGetDeleteRange(
        in ImeSurroundingTextSnapshot snapshot,
        int beforeUtf8ByteCount,
        int afterUtf8ByteCount,
        out int utf16Start,
        out int utf16Length)
    {
        utf16Start = 0;
        utf16Length = 0;
        if (beforeUtf8ByteCount < 0 || afterUtf8ByteCount < 0)
            return false;

        string text = snapshot.Text ?? string.Empty;
        int cursor = Math.Clamp(snapshot.CursorIndex, 0, text.Length);
        int anchor = Math.Clamp(snapshot.AnchorIndex, 0, text.Length);

        // The protocol defines before/after outside the current selection and
        // separately requires the selected text itself to be deleted.
        int selectionStart = SnapToGraphemeBoundary(text, Math.Min(cursor, anchor), forward: false);
        int selectionEnd = SnapToGraphemeBoundary(text, Math.Max(cursor, anchor), forward: true);
        int selectionStartByte = GetUtf8ByteOffset(text, selectionStart);
        int selectionEndByte = GetUtf8ByteOffset(text, selectionEnd);
        int totalBytes = Encoding.UTF8.GetByteCount(text);

        int requestedStartByte = Math.Max(0, selectionStartByte - beforeUtf8ByteCount);
        int requestedEndByte = (int)Math.Min(
            totalBytes,
            (long)selectionEndByte + afterUtf8ByteCount);

        // If a compositor supplies a length that lands in the middle of a
        // UTF-8 sequence, Wayland requires deleting at least that many bytes.
        // Snapping outward to extended grapheme boundaries also keeps a base +
        // combining mark and emoji ZWJ sequences indivisible.
        utf16Start = GetUtf16OffsetAtUtf8Byte(text, requestedStartByte, forward: false);
        int utf16End = GetUtf16OffsetAtUtf8Byte(text, requestedEndByte, forward: true);
        utf16Length = Math.Max(0, utf16End - utf16Start);
        return true;
    }

    internal static bool TryCreateUtf8SurroundingWindow(
        in ImeSurroundingTextSnapshot snapshot,
        int maximumUtf8Bytes,
        out ImeSurroundingTextSnapshot window)
    {
        window = default;
        if (maximumUtf8Bytes < 0)
            return false;

        string text = snapshot.Text ?? string.Empty;
        int cursor = SnapToGraphemeBoundary(text, snapshot.CursorIndex, forward: false);
        int anchor = SnapToGraphemeBoundary(text, snapshot.AnchorIndex, forward: false);
        var (utf16Boundaries, utf8Boundaries) = GetGraphemeBoundaryMap(text);

        int cursorBoundary = Array.BinarySearch(utf16Boundaries, cursor);
        int anchorBoundary = Array.BinarySearch(utf16Boundaries, anchor);
        if (cursorBoundary < 0 || anchorBoundary < 0)
            return false;

        int startBoundary = Math.Min(cursorBoundary, anchorBoundary);
        int endBoundary = Math.Max(cursorBoundary, anchorBoundary);
        int selectionBytes = utf8Boundaries[endBoundary] - utf8Boundaries[startBoundary];
        if (selectionBytes > maximumUtf8Bytes)
        {
            // The protocol requires the complete selection. If that alone does
            // not fit, advertise no surrounding text instead of lying about
            // cursor/anchor positions in a truncated selection.
            return false;
        }

        int remaining = maximumUtf8Bytes - selectionBytes;
        int leftBudget = remaining / 2;
        int rightBudget = remaining - leftBudget;

        while (startBoundary > 0)
        {
            int clusterBytes = utf8Boundaries[startBoundary] - utf8Boundaries[startBoundary - 1];
            if (clusterBytes > leftBudget)
                break;
            startBoundary--;
            leftBudget -= clusterBytes;
        }

        while (endBoundary + 1 < utf16Boundaries.Length)
        {
            int clusterBytes = utf8Boundaries[endBoundary + 1] - utf8Boundaries[endBoundary];
            if (clusterBytes > rightBudget)
                break;
            endBoundary++;
            rightBudget -= clusterBytes;
        }

        // Spend any one-sided remainder after the balanced first pass, without
        // ever cutting a grapheme cluster merely to fill the final bytes.
        int spare = maximumUtf8Bytes -
            (utf8Boundaries[endBoundary] - utf8Boundaries[startBoundary]);
        bool expanded;
        do
        {
            expanded = false;
            if (startBoundary > 0)
            {
                int clusterBytes = utf8Boundaries[startBoundary] - utf8Boundaries[startBoundary - 1];
                if (clusterBytes <= spare)
                {
                    startBoundary--;
                    spare -= clusterBytes;
                    expanded = true;
                }
            }

            if (endBoundary + 1 < utf16Boundaries.Length)
            {
                int clusterBytes = utf8Boundaries[endBoundary + 1] - utf8Boundaries[endBoundary];
                if (clusterBytes <= spare)
                {
                    endBoundary++;
                    spare -= clusterBytes;
                    expanded = true;
                }
            }
        }
        while (expanded && spare > 0);

        int utf16Start = utf16Boundaries[startBoundary];
        int utf16End = utf16Boundaries[endBoundary];
        window = new ImeSurroundingTextSnapshot(
            text[utf16Start..utf16End],
            cursor - utf16Start,
            anchor - utf16Start);
        return true;
    }

    internal static int SnapToGraphemeBoundary(string? text, int utf16Offset, bool forward)
    {
        text ??= string.Empty;
        utf16Offset = Math.Clamp(utf16Offset, 0, text.Length);
        if (text.Length == 0 || utf16Offset == 0 || utf16Offset == text.Length)
            return utf16Offset;

        int previous = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            int current = enumerator.ElementIndex;
            if (current == utf16Offset)
                return current;
            if (current > utf16Offset)
                return forward ? current : previous;
            previous = current;
        }

        return text.Length;
    }

    private static int GetUtf16OffsetAtUtf8Byte(string text, int byteOffset, bool forward)
    {
        byteOffset = Math.Clamp(byteOffset, 0, Encoding.UTF8.GetByteCount(text));
        if (byteOffset == 0 || text.Length == 0)
            return 0;

        int currentByte = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            int start = enumerator.ElementIndex;
            string element = enumerator.GetTextElement();
            int end = start + element.Length;
            int nextByte = currentByte + Encoding.UTF8.GetByteCount(element);

            if (byteOffset == currentByte)
                return start;
            if (byteOffset < nextByte)
                return forward ? end : start;

            currentByte = nextByte;
            if (byteOffset == currentByte)
                return end;
        }

        return text.Length;
    }

    private static (int[] Utf16, int[] Utf8) GetGraphemeBoundaryMap(string text)
    {
        if (text.Length == 0)
            return ([0], [0]);

        var utf16 = new List<int>(text.Length + 1);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            utf16.Add(enumerator.ElementIndex);
        utf16.Add(text.Length);

        var utf8 = new int[utf16.Count];
        for (int index = 1; index < utf16.Count; index++)
        {
            utf8[index] = utf8[index - 1] + Encoding.UTF8.GetByteCount(
                text.AsSpan(utf16[index - 1], utf16[index] - utf16[index - 1]));
        }

        return (utf16.ToArray(), utf8);
    }
}
