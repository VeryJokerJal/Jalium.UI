using System.Text;

namespace Jalium.UI.Styling;

public sealed partial class CssStyleSheet
{
    private const int MaximumDataUriBytes = 64_000_000;

    private static CssResource DecodeDataUri(Uri uri)
    {
        var address = uri.AbsoluteUri;
        var fragment = address.IndexOf('#');
        if (fragment >= 0) address = address[..fragment];
        var comma = address.IndexOf(',', "data:".Length);
        if (comma < 0) throw new InvalidDataException("Data URL has no comma separator");

        var metadata = address["data:".Length..comma];
        var encoded = address.AsSpan(comma + 1);
        if (encoded.Length > MaximumDataUriBytes * 3L)
            throw new InvalidDataException("Data URL is too large");

        var base64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        if (base64) metadata = metadata[..^7];
        var contentType = metadata.Length == 0 ? "text/plain;charset=US-ASCII"
            : metadata.StartsWith(';') ? "text/plain" + metadata : metadata;

        var decoded = PercentDecodeData(encoded);
        if (base64)
        {
            try { decoded = DecodeForgivingBase64(decoded); }
            catch (FormatException error)
            {
                throw new InvalidDataException("Data URL has invalid base64", error);
            }
        }
        if (decoded.Length > MaximumDataUriBytes)
            throw new InvalidDataException("Data URL is too large");
        return new CssResource(uri, new MemoryStream(decoded, writable: false), contentType);
    }

    private static byte[] PercentDecodeData(ReadOnlySpan<char> value)
    {
        using var output = new MemoryStream(Math.Min(value.Length, MaximumDataUriBytes));
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '%' && index + 2 < value.Length &&
                Hex(value[index + 1]) is var high and >= 0 &&
                Hex(value[index + 2]) is var low and >= 0)
            {
                output.WriteByte((byte)((high << 4) | low));
                index += 2;
            }
            else if (character <= 0x7f)
                output.WriteByte((byte)character);
            else
            {
                Span<byte> bytes = stackalloc byte[4];
                var count = Encoding.UTF8.GetBytes(value.Slice(index, 1), bytes);
                output.Write(bytes[..count]);
            }
            if (output.Length > MaximumDataUriBytes)
                throw new InvalidDataException("Data URL is too large");
        }
        return output.ToArray();

        static int Hex(char character) => character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1,
        };
    }

    private static byte[] DecodeForgivingBase64(ReadOnlySpan<byte> value)
    {
        var encoded = new StringBuilder(value.Length + 3);
        foreach (var octet in value)
        {
            if (octet is 0x09 or 0x0a or 0x0c or 0x0d or 0x20) continue;
            if (octet > 0x7f) throw new FormatException("Base64 data is not ASCII");
            encoded.Append((char)octet);
        }

        if (encoded.Length % 4 == 0)
        {
            if (encoded.Length > 0 && encoded[^1] == '=') encoded.Length--;
            if (encoded.Length > 0 && encoded[^1] == '=') encoded.Length--;
        }
        if (encoded.Length % 4 == 1)
            throw new FormatException("Base64 data has an invalid length");
        for (var index = 0; index < encoded.Length; index++)
        {
            var character = encoded[index];
            if (character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or
                >= '0' and <= '9' or '+' or '/'))
                throw new FormatException("Base64 data has an invalid character");
        }
        while (encoded.Length % 4 != 0) encoded.Append('=');
        return Convert.FromBase64String(encoded.ToString());
    }
}
