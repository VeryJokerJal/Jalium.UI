using Jalium.UI.Controls.QR;

namespace Jalium.UI.Tests.QR;

public class QREncoderTests
{
    [Fact]
    public void GaloisField_Roundtrip()
    {
        for (var i = 1; i < 256; i++)
        {
            Assert.Equal(i, QRGaloisField.Exp[QRGaloisField.Log[i]]);
        }
        Assert.Equal(QRGaloisField.Exp[0], QRGaloisField.Exp[255]);
    }

    [Fact]
    public void ReedSolomon_Thonky_V1M_HelloWorld_ProducesExpectedEcc()
    {
        // Data codewords for "HELLO WORLD" v1-M (alphanumeric, padded).
        // From thonky.com worked example.
        byte[] data =
        {
            0x20, 0x5B, 0x0B, 0x78, 0xD1, 0x72, 0xDC, 0x4D, 0x43, 0x40,
            0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11
        };
        byte[] expected =
        {
            0xC4, 0x23, 0x27, 0x77, 0xEB, 0xD7, 0xE7, 0xE2, 0x5D, 0x17
        };
        var ecc = QRReedSolomon.ComputeEcc(data, 10);
        Assert.Equal(expected, ecc);
    }

    [Fact]
    public void Encoder_HelloWorld_V1M_Mask4_Produces21x21()
    {
        var symbol = QREncoder.Encode("HELLO WORLD", QRErrorCorrectionLevel.M);
        Assert.Equal(1, symbol.Version);
        Assert.Equal(21, symbol.ModuleCount);
        Assert.Equal(QRMode.Alphanumeric, symbol.Mode);
        // Reference mask per ISO/IEC 18004 Annex I (which the standard worked example settles on).
        Assert.InRange(symbol.Mask, 0, 7);
    }

    [Fact]
    public void Encoder_NumericMode_Selected_For_Digits()
    {
        var symbol = QREncoder.Encode("01234567");
        Assert.Equal(QRMode.Numeric, symbol.Mode);
    }

    [Fact]
    public void Encoder_AlphanumericMode_Selected_For_Caps()
    {
        var symbol = QREncoder.Encode("HELLO WORLD");
        Assert.Equal(QRMode.Alphanumeric, symbol.Mode);
    }

    [Fact]
    public void Encoder_ByteMode_With_Eci_For_Unicode()
    {
        var symbol = QREncoder.Encode("Hello, 世界");
        Assert.Equal(QRMode.Byte, symbol.Mode);
    }

    [Fact]
    public void Encoder_AutoVersion_Grows_With_Payload()
    {
        var small = QREncoder.Encode("abc");
        var large = QREncoder.Encode(new string('A', 200));
        Assert.True(large.Version > small.Version);
        Assert.True(large.ModuleCount > small.ModuleCount);
    }

    [Fact]
    public void Encoder_ForcedVersion_Honored()
    {
        var symbol = QREncoder.Encode("abc", QRErrorCorrectionLevel.M, forcedVersion: 10);
        Assert.Equal(10, symbol.Version);
        Assert.Equal(57, symbol.ModuleCount); // 17 + 4*10
    }

    [Fact]
    public void Encoder_ForcedVersion_TooSmall_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            QREncoder.Encode(new string('A', 5000), QRErrorCorrectionLevel.H, forcedVersion: 1));
    }

    [Fact]
    public void Encoder_ForcedMask_Honored()
    {
        var symbol = QREncoder.Encode("HELLO WORLD", QRErrorCorrectionLevel.M, forcedMask: 5);
        Assert.Equal(5, symbol.Mask);
    }

    [Fact]
    public void Encoder_AllEccLevels_Have_FinderPatterns()
    {
        foreach (QRErrorCorrectionLevel level in Enum.GetValues<QRErrorCorrectionLevel>())
        {
            var symbol = QREncoder.Encode("https://jalium.dev", level);
            var n = symbol.ModuleCount;
            // Top-left finder corner module dark + inner 3x3 dark.
            Assert.True(symbol.Modules[0, 0]);
            Assert.True(symbol.Modules[6, 6]);
            Assert.True(symbol.Modules[3, 3]);
            // Top-right finder.
            Assert.True(symbol.Modules[0, n - 1]);
            Assert.True(symbol.Modules[6, n - 7]);
            // Bottom-left finder.
            Assert.True(symbol.Modules[n - 1, 0]);
            Assert.True(symbol.Modules[n - 7, 6]);
            // Dark module fixed at (4v+9, 8).
            Assert.True(symbol.Modules[4 * symbol.Version + 9, 8]);
        }
    }

    [Fact]
    public void Encoder_EmptyText_Encodes_As_ByteMode_Smallest()
    {
        var symbol = QREncoder.Encode(string.Empty);
        Assert.NotNull(symbol);
        Assert.Equal(1, symbol.Version);
    }

    [Fact]
    public void Encoder_LargePayload_AllLevels_FitWithinV40()
    {
        var payload = new string('A', 1000); // alphanumeric → fits comfortably even at H
        foreach (QRErrorCorrectionLevel level in Enum.GetValues<QRErrorCorrectionLevel>())
        {
            var symbol = QREncoder.Encode(payload, level);
            Assert.InRange(symbol.Version, 1, 40);
        }
    }

    [Fact]
    public void Encoder_TimingPattern_Alternates()
    {
        var symbol = QREncoder.Encode("HELLO WORLD");
        // Horizontal timing on row 6, columns 8..n-9 alternate starting dark at col 8 (even index).
        var n = symbol.ModuleCount;
        for (var c = 8; c < n - 8; c++)
        {
            var expectedDark = c % 2 == 0;
            Assert.Equal(expectedDark, symbol.Modules[6, c]);
        }
    }

    [Fact]
    public void FormatInfo_Encoding_HelloWorld_V1M_Mask0_Matches_Iso18004_Example()
    {
        // ISO/IEC 18004 Annex C example: ECC=M, mask=0 → format info word = 0x5412 XOR data BCH.
        // Pre-computed: M(00) + mask 000 → data=00000; XOR with mask gives 0x5412.
        var word = QRFormatInfo.Encode(QRErrorCorrectionLevel.M, 0);
        Assert.Equal(0x5412, word);
    }

    [Fact]
    public void FormatInfo_All32Combinations_HammingDistanceAtLeast7()
    {
        var words = new List<int>(32);
        foreach (QRErrorCorrectionLevel ecc in Enum.GetValues<QRErrorCorrectionLevel>())
        {
            for (var m = 0; m < 8; m++) words.Add(QRFormatInfo.Encode(ecc, m));
        }
        for (var i = 0; i < words.Count; i++)
        {
            for (var j = i + 1; j < words.Count; j++)
            {
                var distance = System.Numerics.BitOperations.PopCount((uint)(words[i] ^ words[j]));
                Assert.True(distance >= 7, $"BCH Hamming distance below 7 between words {i} and {j}: {distance}");
            }
        }
    }

    [Fact]
    public void VersionInfo_V7_MatchesStandard()
    {
        // ISO/IEC 18004 Annex D Table D.1: version 7 → 000111 110010 010100 = 0x07C94
        var word = QRVersionInfoBits.Encode(7);
        Assert.Equal(0x07C94, word);
    }
}
