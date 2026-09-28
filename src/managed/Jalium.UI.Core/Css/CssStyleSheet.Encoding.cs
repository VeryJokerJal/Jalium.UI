using System.Net.Http.Headers;
using System.Text;

namespace Jalium.UI.Styling;

internal static class CssStyleSheetEncoding
{
    internal static (string Text, string EncodingName) Decode(
        ReadOnlySpan<byte> bytes, string? contentType = null, string? environmentEncoding = null)
    {
        var transportEncoding = ContentTypeEncoding(contentType);
        var (bomEncoding, bomLength) = ByteOrderMark(bytes);
        var encodingName = bomEncoding ?? transportEncoding ?? DeclaredEncoding(bytes) ??
            environmentEncoding ?? "UTF-8";
        return (DecodeBytes(bytes[bomLength..], encodingName), encodingName);
    }

    private static string? ContentTypeEncoding(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed) ||
            !string.Equals(parsed.MediaType, "text/css", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"CSS resource has unsupported Content-Type '{contentType}'.");
        return EncodingLabel(parsed.CharSet);
    }

    private static (string? EncodingName, int Length) ByteOrderMark(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return ("UTF-8", 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return ("UTF-16LE", 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return ("UTF-16BE", 2);
        return (null, 0);
    }

    private static string? DeclaredEncoding(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> prefix = "@charset \""u8;
        if (!bytes.StartsWith(prefix)) return null;
        var limit = Math.Min(bytes.Length, 1024);
        var end = prefix.Length;
        while (end < limit && bytes[end] != (byte)'"')
        {
            if (bytes[end] > 0x7F) return null;
            end++;
        }
        if (end + 1 >= limit || bytes[end + 1] != (byte)';') return null;
        var declared = EncodingLabel(Encoding.ASCII.GetString(bytes[prefix.Length..end]));
        // An ASCII @charset header cannot itself be encoded as UTF-16.
        return declared is "UTF-16LE" or "UTF-16BE" ? "UTF-8" : declared;
    }

    private static string? EncodingLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var normalized = label.Trim(' ', '\t', '\n', '\f', '\r')
            .Trim('"').Trim(' ', '\t', '\n', '\f', '\r');
        if (normalized.Any(character => character > 0x7F)) return null;
        normalized = normalized.ToLowerInvariant();
        return CssStyleSheetEncodingData.Labels.GetValueOrDefault(normalized);
    }

    private static string DecodeBytes(ReadOnlySpan<byte> bytes, string encodingName)
    {
        if (encodingName == "replacement") return bytes.IsEmpty ? string.Empty : "\uFFFD";
        if (encodingName == "x-user-defined")
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : (char)(0xF780 + bytes[i] - 0x80);
            return new string(chars);
        }
        if (CssStyleSheetEncodingData.SingleByteTable(encodingName) is { } table)
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : table[bytes[i] - 0x80];
            return new string(chars);
        }
        if (encodingName is "GBK" or "gb18030")
            return CssStyleSheetGb18030Decoder.Decode(bytes);
        if (encodingName == "Big5")
            return CssStyleSheetBig5Decoder.Decode(bytes);
        if (encodingName == "EUC-KR")
            return CssStyleSheetEastAsianDecoders.DecodeEucKr(bytes);
        if (encodingName == "EUC-JP")
            return CssStyleSheetEastAsianDecoders.DecodeEucJp(bytes);
        if (encodingName == "Shift_JIS")
            return CssStyleSheetEastAsianDecoders.DecodeShiftJis(bytes);
        if (encodingName == "ISO-2022-JP")
            return CssStyleSheetIso2022JpDecoder.Decode(bytes);

        var encoding = encodingName switch
        {
            "UTF-8" => Encoding.UTF8,
            "UTF-16LE" => Encoding.Unicode,
            "UTF-16BE" => Encoding.BigEndianUnicode,
            _ => throw new InvalidDataException($"Unsupported CSS encoding '{encodingName}'."),
        };
        return encoding.GetString(bytes);
    }
}
