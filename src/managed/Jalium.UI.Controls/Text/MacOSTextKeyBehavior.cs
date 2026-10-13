using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>Native text navigation for captured macOS keyboard events.</summary>
internal static partial class MacOSTextKeyBehavior
{
    internal static bool IsCommandNavigation(KeyEventArgs e) => e.IsCommandDown &&
        (e.PhysicalModifiers.GetValueOrDefault() & (ModifierKeys.Alt | ModifierKeys.Control)) == 0;

    internal static bool IsOptionWord(KeyEventArgs e) => OperatingSystem.IsMacOS() &&
        e.PhysicalModifiers is { } physical &&
        (physical & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows)) == ModifierKeys.Alt;

    internal static Key ResolveEditingKey(KeyEventArgs e)
    {
        if (!OperatingSystem.IsMacOS() || e.PhysicalModifiers is not { } physical ||
            (physical & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != ModifierKeys.Control)
            return e.Key;

        return e.Key switch
        {
            Key.A => Key.Home,
            Key.E => Key.End,
            Key.B => Key.Left,
            Key.F => Key.Right,
            Key.P => Key.Up,
            Key.N => Key.Down,
            Key.H => Key.Back,
            Key.D => Key.Delete,
            _ => e.Key
        };
    }

    internal static int FindWordBoundary(string text, int index, bool forward, bool physical = false,
        int selectionStart = 0, int selectionLength = 0)
    {
        index = GraphemeClusters.Snap(text, Math.Clamp(index, 0, text.Length), forward);
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                int direction = physical ? forward ? 2 : 3 : forward ? 0 : 1;
                int destination = selectionLength > 0
                    ? PlatformWordSelectionBoundary(text, (uint)text.Length, selectionStart, selectionLength, direction)
                    : PlatformWordBoundary(text, (uint)text.Length, index, direction);
                if (destination >= 0 && destination <= text.Length)
                    return GraphemeClusters.Snap(text, destination, destination >= index);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        if (forward)
        {
            // Option-Right stops at a word's end; the Windows Control-Right
            // helper instead includes trailing separators to the next start.
            while (index < text.Length && IsSeparator(text, index))
                index = GraphemeClusters.NextBoundary(text, index);
            while (index < text.Length && !IsSeparator(text, index))
                index = GraphemeClusters.NextBoundary(text, index);
        }
        else
        {
            while (index > 0 && IsSeparator(text, GraphemeClusters.PreviousBoundary(text, index)))
                index = GraphemeClusters.PreviousBoundary(text, index);
            while (index > 0 && !IsSeparator(text, GraphemeClusters.PreviousBoundary(text, index)))
                index = GraphemeClusters.PreviousBoundary(text, index);
        }
        return index;
    }

    internal static bool TryGetWordRange(string text, int index, out int start, out int length)
    {
        start = length = 0;
        if (!OperatingSystem.IsMacOS()) return false;
        if (text.Length == 0) return true;
        index = GraphemeClusters.Snap(text, Math.Clamp(index, 0, text.Length - 1), forward: false);
        try
        {
            if (PlatformWordRange(text, (uint)text.Length, index, out int nativeStart, out int nativeLength) != 0 ||
                nativeStart < 0 || nativeLength < 0 || nativeStart > text.Length - nativeLength) return false;
            start = GraphemeClusters.Snap(text, nativeStart, forward: false);
            int end = GraphemeClusters.Snap(text, nativeStart + nativeLength, forward: true);
            length = end - start;
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    internal static int GetWordIndexFromPoint(IImeSupport editor, string text, Point point, int caretIndex)
    {
        if (!OperatingSystem.IsMacOS() || text.Length == 0) return caretIndex;
        int index = GraphemeClusters.Snap(text, Math.Clamp(caretIndex, 0, text.Length), forward: false);
        // Caret hit tests choose the nearest insertion edge. Mouse word
        // selection needs the glyph under the pointer, including its trailing
        // half; the next word must not win merely because its edge is nearer.
        int previous = GraphemeClusters.PreviousBoundary(text, index);
        foreach (int candidate in new[] { index, previous })
        {
            if (candidate >= text.Length) continue;
            int end = GraphemeClusters.NextBoundary(text, candidate);
            if (editor.TryGetImeTextRangeGeometry(candidate, end - candidate, false, out var geometry))
            {
                var rectangle = geometry.Rectangle;
                if (point.X >= rectangle.Left && point.X < rectangle.Right &&
                    point.Y >= rectangle.Top && point.Y < rectangle.Bottom) return candidate;
            }
        }
        return index;
    }

    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_platform_text_word_boundary", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PlatformWordBoundary(string text, uint length, int index, int direction);

    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_platform_text_word_selection_boundary", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PlatformWordSelectionBoundary(string text, uint length, int start, int rangeLength, int direction);

    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_platform_text_word_range", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PlatformWordRange(string text, uint length, int index, out int start, out int rangeLength);

    private static bool IsSeparator(string text, int index)
    {
        // A whole emoji/combining cluster remains indivisible. Keep identifier
        // underscores within the same word, consistent with editor selection.
        return GraphemeClusters.NextBoundary(text, index) == index + 1 &&
            (char.IsWhiteSpace(text[index]) || (text[index] != '_' && char.IsPunctuation(text[index])));
    }
}
