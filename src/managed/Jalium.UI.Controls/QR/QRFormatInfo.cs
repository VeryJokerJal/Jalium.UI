namespace Jalium.UI.Controls.QR;

internal static class QRFormatInfo
{
    private const int FormatPoly = 0x537; // x^10 + x^8 + x^5 + x^4 + x^2 + x + 1
    private const int FormatMask = 0x5412;

    /// <summary>
    /// Encode 5-bit (ECC level + mask pattern) into 15-bit BCH(15,5) format info, XOR'd with mask.
    /// </summary>
    public static int Encode(QRErrorCorrectionLevel ecc, int mask)
    {
        // Format spec order: bits 14..13 = ECC level (L=01, M=00, Q=11, H=10), bits 12..10 = mask.
        var eccBits = ecc switch
        {
            QRErrorCorrectionLevel.L => 0b01,
            QRErrorCorrectionLevel.M => 0b00,
            QRErrorCorrectionLevel.Q => 0b11,
            QRErrorCorrectionLevel.H => 0b10,
            _ => throw new ArgumentOutOfRangeException(nameof(ecc))
        };
        var data = (eccBits << 3) | (mask & 0x7);
        var bch = data << 10;
        for (var i = 4; i >= 0; i--)
        {
            if (((bch >> (i + 10)) & 1) != 0)
            {
                bch ^= FormatPoly << i;
            }
        }
        return ((data << 10) | bch) ^ FormatMask;
    }
}

internal static class QRVersionInfoBits
{
    private const int VersionPoly = 0x1F25; // x^12 + x^11 + x^10 + x^9 + x^8 + x^5 + x^2 + 1

    /// <summary>
    /// For version >= 7, returns the 18-bit version info word (6-bit version + 12-bit BCH).
    /// </summary>
    public static int Encode(int version)
    {
        if (version < 7) return 0;
        var bch = version << 12;
        for (var i = 5; i >= 0; i--)
        {
            if (((bch >> (i + 12)) & 1) != 0)
            {
                bch ^= VersionPoly << i;
            }
        }
        return (version << 12) | bch;
    }
}
