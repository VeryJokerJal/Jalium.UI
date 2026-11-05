using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private CapsContextData? _capsContext, _sharedCapsContext;
    private int _sharedCapsOffset;
    private bool _hasCapsRequest, _hasEmojiRequest;
    private bool _ownCapsShaping;

    private void RebuildCapsContext()
    {
        _capsContext = _sharedCapsContext = null;
        _sharedCapsOffset = 0;
        var ranges = GetInlineTextRanges();
        var ownerCaps = (CssCapsVariant)GetValue(CssFontCaps.VariantProperty)!;
        _hasCapsRequest = ownerCaps != CssCapsVariant.Normal || ranges.Any(range =>
            (CssCapsVariant)range.Run.GetValue(CssFontCaps.VariantProperty)! != CssCapsVariant.Normal);
        _hasPositionRequest = (CssPositionVariant)GetValue(CssFontPosition.VariantProperty)! != CssPositionVariant.Normal ||
            ranges.Any(range => (CssPositionVariant)range.Run.GetValue(CssFontPosition.VariantProperty)! != CssPositionVariant.Normal);
        _hasEmojiRequest = _visualText.IndexOfAny(['\ufe0e', '\ufe0f']) >= 0;
        var ownerEmoji = (CssEmojiVariant)GetValue(CssFontEmoji.VariantProperty)!;
        int emojiAt = 0, emojiRange = 0;
        foreach (var rune in _visualText.EnumerateRunes())
        {
            while (emojiRange < ranges.Count && ranges[emojiRange].End <= emojiAt) emojiRange++;
            var variant = emojiRange < ranges.Count && ranges[emojiRange].Start <= emojiAt
                ? (CssEmojiVariant)ranges[emojiRange].Run.GetValue(CssFontEmoji.VariantProperty)! : ownerEmoji;
            if (variant != CssEmojiVariant.Normal && CssEmojiData.IsParticipating(rune.Value)) { _hasEmojiRequest = true; break; }
            emojiAt += rune.Utf16SequenceLength;
        }
        bool unicase = ownerCaps == CssCapsVariant.Unicase || ranges.Any(range =>
            (CssCapsVariant)range.Run.GetValue(CssFontCaps.VariantProperty)! == CssCapsVariant.Unicase);
        _usesCapsShaping = _ownCapsShaping = _hasEmojiRequest || _hasPositionRequest || _hasCapsRequest && ((_usesInlineFontLayout && ranges.Count > 1) ||
            (unicase && _visualText.Contains('\u03a3')));
        if ((_hasCapsRequest || _hasPositionRequest || _hasEmojiRequest) && OperatingSystem.IsMacOS()) _capsContext = new(_visualText);
    }

    private void SetSharedCapsContext(CapsContextData? context, int offset)
    {
        if (ReferenceEquals(_sharedCapsContext, context) && _sharedCapsOffset == offset) return;
        ClearShapedSpacing(); ClearNativeBidiLines(); _inlineFontMeasurements.Clear();
        _formattedLinesCacheDirty = true;
        _sharedCapsContext = context; _sharedCapsOffset = offset;
        _usesCapsShaping = _ownCapsShaping || context?.HasEmoji == true || _hasCapsRequest && context?.Operations is not null;
    }

    private byte CapsBoundaryFlags(int start, int end) => _sharedCapsContext is { } shared
        ? shared.Flags(_sharedCapsOffset + start, _sharedCapsOffset + end)
        : _capsContext?.Flags(start, end) ?? 0;

    private string CapsContextFamily(string family, int start, int end, int positionStart = -1)
        => FontFeatureRenderingSource.WithCapsFragment(family, CapsBoundaryFlags(start, end), CapsContinuation(start), PositionRunAt(positionStart < 0 ? start : positionStart), EmojiAfterAt(end));

    private byte EmojiAfterAt(int end) => _sharedCapsContext is { } shared
        ? shared.EmojiAfter(_sharedCapsOffset + end) : _capsContext?.EmojiAfter(end) ?? 0;

    private byte CapsContinuation(int position) => _sharedCapsContext?.Operations is { } operations &&
        position < _visualText.Length && Rune.TryGetRuneAt(_visualText, position, out var rune) &&
        (CssUnicodeCasingData.IsAbove(rune.Value) || CssUnicodeCasingData.IsOtherCombining(rune.Value)) &&
        !CssUnicodeCasingData.IsCased(rune.Value) ? operations[_sharedCapsOffset + position] : (byte)0;

    private sealed class CapsContextData
    {
        private readonly byte[] _before, _after, _emojiAfter;
        internal bool HasEmoji { get; set; }
        internal byte EmojiAfter(int end) => _emojiAfter[end];
        internal byte[]? Operations { get; set; }
        internal byte[]? PositionModes { get; set; }
        internal bool PositionResolved { get; set; }
        internal byte Flags(int start, int end) => (byte)(_before[start] | _after[end]);
        internal CapsContextData(string text)
        {
            // Keep only the six Unicode predicates, not the surrounding text, in
            // a fragment's rendering identity. Both passes are linear in UTF-16.
            _emojiAfter = new byte[text.Length + 1];
            for (int i = 0; i < text.Length; i++)
                _emojiAfter[i] = text[i] == '\ufe0e' ? (byte)1 : text[i] == '\ufe0f' ? (byte)2 : (byte)0;
            _before = new byte[text.Length + 1];
            _after = new byte[text.Length + 1];
            bool cased = false, afterI = false, soft = false;
            int at = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                byte before = (byte)((cased ? 1 : 0) | (afterI ? 4 : 0) | (soft ? 8 : 0));
                _before[at] = before;
                if (rune.Utf16SequenceLength == 2) _before[at + 1] = before;
                int value = rune.Value;
                if (!CssUnicodeCasingData.IsCaseIgnorable(value)) cased = CssUnicodeCasingData.IsCased(value);
                if (!CssUnicodeCasingData.IsOtherCombining(value))
                { afterI = value == 'I'; soft = CssUnicodeCasingData.IsSoftDotted(value); }
                at += rune.Utf16SequenceLength;
            }
            _before[at] = (byte)((cased ? 1 : 0) | (afterI ? 4 : 0) | (soft ? 8 : 0));
            cased = false; bool dot = false, above = false;
            for (at = text.Length; at > 0;)
            {
                byte after = (byte)((cased ? 2 : 0) | (dot ? 16 : 0) | (above ? 32 : 0));
                _after[at] = after;
                int length = at >= 2 && char.IsLowSurrogate(text[at - 1]) && char.IsHighSurrogate(text[at - 2]) ? 2 : 1;
                if (length == 2) _after[at - 1] = after;
                at -= length;
                int value = Rune.TryGetRuneAt(text, at, out var rune) ? rune.Value : text[at];
                if (!CssUnicodeCasingData.IsCaseIgnorable(value)) cased = CssUnicodeCasingData.IsCased(value);
                if (value == 0x0307) dot = true;
                else if (!CssUnicodeCasingData.IsOtherCombining(value)) dot = false;
                if (CssUnicodeCasingData.IsAbove(value)) above = true;
                else if (!CssUnicodeCasingData.IsOtherCombining(value)) above = false;
            }
            _after[0] = (byte)((cased ? 2 : 0) | (dot ? 16 : 0) | (above ? 32 : 0));
        }
    }
}
