using System.Text;

namespace Jalium.UI.Controls.QR;

/// <summary>
/// QR Code encoder per ISO/IEC 18004. Pure managed, AOT/trim-safe.
/// </summary>
public static class QREncoder
{
    private const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
    private const int Eci_Utf8 = 26;

    /// <summary>
    /// Encode <paramref name="text"/> into a QR symbol. Picks the smallest version that fits.
    /// </summary>
    /// <param name="text">Text to encode (non-null, can be empty).</param>
    /// <param name="ecc">Error correction level.</param>
    /// <param name="byteEncoding">Byte-stream encoding policy.</param>
    /// <param name="forcedVersion">0 = auto, 1..40 = force that version.</param>
    /// <param name="forcedMask">-1 = auto, 0..7 = force that mask.</param>
    public static QRSymbol Encode(
        string text,
        QRErrorCorrectionLevel ecc = QRErrorCorrectionLevel.M,
        QRByteEncoding byteEncoding = QRByteEncoding.Auto,
        int forcedVersion = 0,
        int forcedMask = -1)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var mode = SelectMode(text, byteEncoding, out var byteData, out var useEci);

        // Pick smallest version that fits.
        int version;
        if (forcedVersion is >= 1 and <= 40)
        {
            version = forcedVersion;
            if (!Fits(text, byteData, mode, ecc, version, useEci))
                throw new ArgumentException(
                    $"Data does not fit in QR version {version} at ECC {ecc}.", nameof(forcedVersion));
        }
        else if (forcedVersion == 0)
        {
            version = -1;
            for (var v = 1; v <= 40; v++)
            {
                if (Fits(text, byteData, mode, ecc, v, useEci))
                {
                    version = v;
                    break;
                }
            }
            if (version < 0)
                throw new ArgumentException("Data is too large to fit in any QR version.", nameof(text));
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(forcedVersion), "Use 0 for auto or 1..40.");
        }

        var bits = BuildBitStream(text, byteData, mode, ecc, version, useEci);
        var codewords = InterleaveBlocks(bits, version, ecc);
        var (matrix, mask) = QRMatrixBuilder.Build(version, ecc, codewords, forcedMask);
        return new QRSymbol(matrix, version, ecc, mask, mode);
    }

    private static QRMode SelectMode(string text, QRByteEncoding requested, out byte[] byteData, out bool useEci)
    {
        useEci = false;
        byteData = Array.Empty<byte>();

        switch (requested)
        {
            case QRByteEncoding.Iso8859_1:
                byteData = Encoding.Latin1.GetBytes(text);
                return QRMode.Byte;
            case QRByteEncoding.Utf8:
                byteData = Encoding.UTF8.GetBytes(text);
                useEci = true;
                return QRMode.Byte;
            case QRByteEncoding.ShiftJis:
                // Use code page 932 for Shift-JIS. Falls back to '?' for unmappable code points.
                byteData = Encoding.GetEncoding(932).GetBytes(text);
                return QRMode.Byte;
        }

        // Auto: numeric → alphanumeric → byte
        if (text.Length > 0 && IsNumeric(text))
        {
            return QRMode.Numeric;
        }
        if (text.Length > 0 && IsAlphanumeric(text))
        {
            return QRMode.Alphanumeric;
        }
        if (IsLatin1(text))
        {
            byteData = Encoding.Latin1.GetBytes(text);
            return QRMode.Byte;
        }
        byteData = Encoding.UTF8.GetBytes(text);
        useEci = true;
        return QRMode.Byte;
    }

    private static bool IsNumeric(string s)
    {
        foreach (var c in s)
            if (c < '0' || c > '9') return false;
        return true;
    }

    private static bool IsAlphanumeric(string s)
    {
        foreach (var c in s)
            if (AlphanumericChars.IndexOf(c) < 0) return false;
        return true;
    }

    private static bool IsLatin1(string s)
    {
        foreach (var c in s)
            if (c > 0xFF) return false;
        return true;
    }

    private static bool Fits(string text, byte[] byteData, QRMode mode, QRErrorCorrectionLevel ecc, int version, bool useEci)
    {
        var available = QRVersionInfo.TotalDataBits(version, ecc);
        var required = ComputeBitLength(text, byteData, mode, version, useEci);
        return required <= available;
    }

    private static int ComputeBitLength(string text, byte[] byteData, QRMode mode, int version, bool useEci)
    {
        var bits = 0;
        if (useEci) bits += 4 + 8; // ECI mode indicator + 1-byte designator (for 0..127).
        bits += 4; // mode indicator
        bits += QRVersionInfo.CharacterCountBits(mode, version);
        bits += mode switch
        {
            QRMode.Numeric => NumericBitLength(text.Length),
            QRMode.Alphanumeric => AlphanumericBitLength(text.Length),
            QRMode.Byte => byteData.Length * 8,
            QRMode.Kanji => (text.Length) * 13,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return bits;
    }

    private static int NumericBitLength(int n)
    {
        // 3 digits → 10 bits; 2 → 7; 1 → 4.
        var groups = n / 3;
        var rem = n % 3;
        return groups * 10 + rem switch { 0 => 0, 1 => 4, 2 => 7, _ => 0 };
    }

    private static int AlphanumericBitLength(int n)
    {
        // 2 chars → 11 bits; 1 → 6.
        return (n / 2) * 11 + (n % 2) * 6;
    }

    private static byte[] BuildBitStream(string text, byte[] byteData, QRMode mode, QRErrorCorrectionLevel ecc, int version, bool useEci)
    {
        var bb = new QRBitBuffer();

        if (useEci)
        {
            bb.AppendBits(0b0111, 4); // ECI mode indicator
            bb.AppendBits(Eci_Utf8, 8);
        }

        bb.AppendBits((int)mode, 4);
        bb.AppendBits(CharacterCount(mode, text, byteData), QRVersionInfo.CharacterCountBits(mode, version));

        switch (mode)
        {
            case QRMode.Numeric:
                AppendNumeric(bb, text);
                break;
            case QRMode.Alphanumeric:
                AppendAlphanumeric(bb, text);
                break;
            case QRMode.Byte:
                bb.AppendBytes(byteData);
                break;
            case QRMode.Kanji:
                AppendKanji(bb, text);
                break;
        }

        var capacityBits = QRVersionInfo.TotalDataBits(version, ecc);
        // Terminator: up to 4 zero bits.
        var terminator = Math.Min(4, capacityBits - bb.BitLength);
        for (var i = 0; i < terminator; i++) bb.AppendBit(0);
        // Pad to byte boundary.
        while ((bb.BitLength & 7) != 0) bb.AppendBit(0);
        // Fill with alternating pad codewords until capacity reached.
        var paddingBytes = (capacityBits - bb.BitLength) / 8;
        for (var i = 0; i < paddingBytes; i++)
        {
            bb.AppendBits((i & 1) == 0 ? 0xEC : 0x11, 8);
        }
        return bb.ToBytes();
    }

    private static int CharacterCount(QRMode mode, string text, byte[] byteData)
    {
        return mode == QRMode.Byte ? byteData.Length : text.Length;
    }

    private static void AppendNumeric(QRBitBuffer bb, string text)
    {
        var i = 0;
        while (i + 3 <= text.Length)
        {
            var v = (text[i] - '0') * 100 + (text[i + 1] - '0') * 10 + (text[i + 2] - '0');
            bb.AppendBits(v, 10);
            i += 3;
        }
        var rem = text.Length - i;
        if (rem == 2)
        {
            bb.AppendBits((text[i] - '0') * 10 + (text[i + 1] - '0'), 7);
        }
        else if (rem == 1)
        {
            bb.AppendBits(text[i] - '0', 4);
        }
    }

    private static void AppendAlphanumeric(QRBitBuffer bb, string text)
    {
        var i = 0;
        while (i + 2 <= text.Length)
        {
            var v = AlphanumericChars.IndexOf(text[i]) * 45 + AlphanumericChars.IndexOf(text[i + 1]);
            bb.AppendBits(v, 11);
            i += 2;
        }
        if (i < text.Length)
        {
            bb.AppendBits(AlphanumericChars.IndexOf(text[i]), 6);
        }
    }

    private static void AppendKanji(QRBitBuffer bb, string text)
    {
        var sjis = Encoding.GetEncoding(932).GetBytes(text);
        for (var i = 0; i + 1 < sjis.Length; i += 2)
        {
            var word = (sjis[i] << 8) | sjis[i + 1];
            int subtract;
            if (word is >= 0x8140 and <= 0x9FFC) subtract = 0x8140;
            else if (word is >= 0xE040 and <= 0xEBBF) subtract = 0xC140;
            else throw new ArgumentException("Character outside Shift-JIS Kanji range.");
            word -= subtract;
            var encoded = ((word >> 8) * 0xC0) + (word & 0xFF);
            bb.AppendBits(encoded, 13);
        }
    }

    private static byte[] InterleaveBlocks(byte[] dataCodewords, int version, QRErrorCorrectionLevel ecc)
    {
        var info = QRVersionInfo.GetBlockInfo(version, ecc);
        var blocks = new List<(byte[] data, byte[] ecc)>(info.TotalBlocks);
        var pos = 0;
        for (var b = 0; b < info.Group1Blocks; b++)
        {
            var blockData = new byte[info.Group1DataCodewordsPerBlock];
            Buffer.BlockCopy(dataCodewords, pos, blockData, 0, blockData.Length);
            pos += blockData.Length;
            blocks.Add((blockData, QRReedSolomon.ComputeEcc(blockData, info.EcCodewordsPerBlock)));
        }
        for (var b = 0; b < info.Group2Blocks; b++)
        {
            var blockData = new byte[info.Group2DataCodewordsPerBlock];
            Buffer.BlockCopy(dataCodewords, pos, blockData, 0, blockData.Length);
            pos += blockData.Length;
            blocks.Add((blockData, QRReedSolomon.ComputeEcc(blockData, info.EcCodewordsPerBlock)));
        }

        var maxData = Math.Max(info.Group1DataCodewordsPerBlock, info.Group2DataCodewordsPerBlock);
        var maxEcc = info.EcCodewordsPerBlock;
        var totalSize = QRVersionInfo.TotalCodewords(version);
        var result = new byte[totalSize];
        var write = 0;

        for (var col = 0; col < maxData; col++)
        {
            foreach (var (data, _) in blocks)
            {
                if (col < data.Length)
                {
                    result[write++] = data[col];
                }
            }
        }
        for (var col = 0; col < maxEcc; col++)
        {
            foreach (var (_, eccBlock) in blocks)
            {
                if (col < eccBlock.Length)
                {
                    result[write++] = eccBlock[col];
                }
            }
        }
        return result;
    }
}
