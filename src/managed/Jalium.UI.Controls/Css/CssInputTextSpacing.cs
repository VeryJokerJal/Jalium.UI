using System.Globalization;
using System.Text;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Uniform editor tracking in the native paragraph's source coordinates.</summary>
internal readonly record struct CssInputTextSpacing(double Letter, double Word)
{
    internal bool Active => Letter != 0 || Word != 0;
    internal bool Affects(string text) => Letter != 0 || Word != 0 && text.EnumerateRunes().Any(IsSeparator);
    private static bool IsSeparator(Rune rune) => rune.Value is 0x0020 or 0x00A0 or 0x1361 or 0x10100 or 0x10101 or 0x1039F or 0x1091F;
    internal static CssInputTextSpacing For(Control owner)
    {
        var word = ((CssLayoutLength)owner.GetValue(CssFlowProperties.WordSpacingProperty)!).Resolve(owner.FontSize, 0);
        return new(CssTextSpacing.Letter(owner), double.IsFinite(word) ? word : 0);
    }

    internal NativeMethods.ParagraphAdvanceUnit[] Units(string text)
    {
        var boundaries = GraphemeClusters.GetBoundaries(text);
        var units = new NativeMethods.ParagraphAdvanceUnit[Math.Max(0, boundaries.Length - 1)];
        for (var i = 0; i < units.Length; i++)
        {
            var start = boundaries[i]; var end = boundaries[i + 1]; var ignored = true;
            foreach (var rune in text.AsSpan(start, end - start).EnumerateRunes())
                if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) { ignored = false; break; }
            Rune.TryGetRuneAt(text, start, out var first);
            // Match the existing TextBlock shaper: joined scripts retain their
            // connected glyphs, and format-only source units acquire no gap.
            var cursive = Rune.IsLetter(first) && IsCursive(first);
            var separator = IsSeparator(first);
            var word = separator ? Word : 0;
            units[i] = new() { TextPosition = (uint)start, Length = (uint)(end - start),
                Spacing = (float)Letter, Leading = (float)(word / 2), Trailing = (float)(word / 2),
                Flags = ignored ? 1u : (cursive ? 2u : 0u) | (separator ? 4u : 0u) };
        }
        return units;
    }

    internal static TextBlock.EditorLayout Composition(Control owner, string text, double width, Brush ink)
        => new(owner, text, Math.Max(1, width), owner is TextBox input ? input.TextWrapping : TextWrapping.NoWrap,
            TextAlignment.Left, ink);

    internal static void DrawComposition(Control owner, DrawingContext context, FormattedText text, Point origin)
    {
        if (!For(owner).Active) { context.DrawText(text, origin); return; }
        using var layout = Composition(owner, text.Text, text.MaxTextWidth, text.Foreground ?? Brushes.Black);
        layout.Draw(context, text, origin);
    }

    private static bool IsCursive(Rune rune) => rune.Value is >= 0x0600 and <= 0x08FF or
        >= 0x1800 and <= 0x18AF or >= 0xA840 and <= 0xA87F or
        >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF or >= 0x1E900 and <= 0x1E95F;
}
