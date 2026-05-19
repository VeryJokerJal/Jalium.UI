namespace Jalium.UI.Controls.QR;

/// <summary>
/// Per-version/per-ECC structural information for QR codes (ISO/IEC 18004 Tables 7, 9).
/// </summary>
internal readonly struct QRBlockInfo
{
    public readonly int EcCodewordsPerBlock;
    public readonly int Group1Blocks;
    public readonly int Group1DataCodewordsPerBlock;
    public readonly int Group2Blocks;
    public readonly int Group2DataCodewordsPerBlock;

    public QRBlockInfo(int ec, int g1, int g1d, int g2, int g2d)
    {
        EcCodewordsPerBlock = ec;
        Group1Blocks = g1;
        Group1DataCodewordsPerBlock = g1d;
        Group2Blocks = g2;
        Group2DataCodewordsPerBlock = g2d;
    }

    public int TotalDataCodewords => Group1Blocks * Group1DataCodewordsPerBlock + Group2Blocks * Group2DataCodewordsPerBlock;
    public int TotalBlocks => Group1Blocks + Group2Blocks;
}

internal static class QRVersionInfo
{
    /// <summary>Smallest = version 1 (21 modules), largest = version 40 (177 modules).</summary>
    public const int MinVersion = 1;
    public const int MaxVersion = 40;

    public static int ModuleCount(int version) => 17 + version * 4;

    public static int CharacterCountBits(QRMode mode, int version)
    {
        // ISO/IEC 18004 Table 3.
        return mode switch
        {
            QRMode.Numeric => version <= 9 ? 10 : version <= 26 ? 12 : 14,
            QRMode.Alphanumeric => version <= 9 ? 9 : version <= 26 ? 11 : 13,
            QRMode.Byte => version <= 9 ? 8 : 16,
            QRMode.Kanji => version <= 9 ? 8 : version <= 26 ? 10 : 12,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Alignment pattern center positions per version (1..40). Version 1 has none.
    /// </summary>
    public static readonly int[][] AlignmentPatternCenters =
    [
        [],                                                          // 1
        [6, 18],                                                     // 2
        [6, 22],                                                     // 3
        [6, 26],                                                     // 4
        [6, 30],                                                     // 5
        [6, 34],                                                     // 6
        [6, 22, 38],                                                 // 7
        [6, 24, 42],                                                 // 8
        [6, 26, 46],                                                 // 9
        [6, 28, 50],                                                 // 10
        [6, 30, 54],                                                 // 11
        [6, 32, 58],                                                 // 12
        [6, 34, 62],                                                 // 13
        [6, 26, 46, 66],                                             // 14
        [6, 26, 48, 70],                                             // 15
        [6, 26, 50, 74],                                             // 16
        [6, 30, 54, 78],                                             // 17
        [6, 30, 56, 82],                                             // 18
        [6, 30, 58, 86],                                             // 19
        [6, 34, 62, 90],                                             // 20
        [6, 28, 50, 72, 94],                                         // 21
        [6, 26, 50, 74, 98],                                         // 22
        [6, 30, 54, 78, 102],                                        // 23
        [6, 28, 54, 80, 106],                                        // 24
        [6, 32, 58, 84, 110],                                        // 25
        [6, 30, 58, 86, 114],                                        // 26
        [6, 34, 62, 90, 118],                                        // 27
        [6, 26, 50, 74, 98, 122],                                    // 28
        [6, 30, 54, 78, 102, 126],                                   // 29
        [6, 26, 52, 78, 104, 130],                                   // 30
        [6, 30, 56, 82, 108, 134],                                   // 31
        [6, 34, 60, 86, 112, 138],                                   // 32
        [6, 30, 58, 86, 114, 142],                                   // 33
        [6, 34, 62, 90, 118, 146],                                   // 34
        [6, 30, 54, 78, 102, 126, 150],                              // 35
        [6, 24, 50, 76, 102, 128, 154],                              // 36
        [6, 28, 54, 80, 106, 132, 158],                              // 37
        [6, 32, 58, 84, 110, 136, 162],                              // 38
        [6, 26, 54, 82, 110, 138, 166],                              // 39
        [6, 30, 58, 86, 114, 142, 170],                              // 40
    ];

    /// <summary>
    /// EC blocks per version (1..40) × ECC level (L,M,Q,H).
    /// Each entry: (ecCwPerBlock, g1Blocks, g1DataCw, g2Blocks, g2DataCw).
    /// Source: ISO/IEC 18004 Table 9.
    /// </summary>
    private static readonly QRBlockInfo[,] s_blocks = BuildBlocks();

    public static QRBlockInfo GetBlockInfo(int version, QRErrorCorrectionLevel ecc)
        => s_blocks[version - 1, (int)ecc];

    public static int TotalDataBits(int version, QRErrorCorrectionLevel ecc)
        => GetBlockInfo(version, ecc).TotalDataCodewords * 8;

    public static int TotalCodewords(int version)
    {
        // Number of 8-bit codewords for the symbol = (raw module count - function modules) / 8.
        // Use precomputed table.
        return s_totalCodewords[version - 1];
    }

    private static readonly int[] s_totalCodewords =
    {
          26,   44,   70,  100,  134,  172,  196,  242,  292,  346,
         404,  466,  532,  581,  655,  733,  815,  901,  991, 1085,
        1156, 1258, 1364, 1474, 1588, 1706, 1828, 1921, 2051, 2185,
        2323, 2465, 2611, 2761, 2876, 3034, 3196, 3362, 3532, 3706
    };

    private static QRBlockInfo[,] BuildBlocks()
    {
        // Format: per version 4 entries (L, M, Q, H).
        // Reference: ISO/IEC 18004:2015 Table 9.
        int[,] raw =
        {
            // ec, g1b, g1d, g2b, g2d  for L, M, Q, H
            //  L                   M                   Q                   H
            { 7,1,19,0,0,  10,1,16,0,0,  13,1,13,0,0,  17,1, 9,0,0  }, // v1
            {10,1,34,0,0,  16,1,28,0,0,  22,1,22,0,0,  28,1,16,0,0  }, // v2
            {15,1,55,0,0,  26,1,44,0,0,  18,2,17,0,0,  22,2,13,0,0  }, // v3
            {20,1,80,0,0,  18,2,32,0,0,  26,2,24,0,0,  16,4, 9,0,0  }, // v4
            {26,1,108,0,0, 24,2,43,0,0,  18,2,15,2,16, 22,2,11,2,12 }, // v5
            {18,2,68,0,0,  16,4,27,0,0,  24,4,19,0,0,  28,4,15,0,0  }, // v6
            {20,2,78,0,0,  18,4,31,0,0,  18,2,14,4,15, 26,4,13,1,14 }, // v7
            {24,2,97,0,0,  22,2,38,2,39, 22,4,18,2,19, 26,4,14,2,15 }, // v8
            {30,2,116,0,0, 22,3,36,2,37, 20,4,16,4,17, 24,4,12,4,13 }, // v9
            {18,2,68,2,69, 26,4,43,1,44, 24,6,19,2,20, 28,6,15,2,16 }, // v10
            {20,4,81,0,0,  30,1,50,4,51, 28,4,22,4,23, 24,3,12,8,13 }, // v11
            {24,2,92,2,93, 22,6,36,2,37, 26,4,20,6,21, 28,7,14,4,15 }, // v12
            {26,4,107,0,0, 22,8,37,1,38, 24,8,20,4,21, 22,12,11,4,12}, // v13
            {30,3,115,1,116, 24,4,40,5,41, 20,11,16,5,17, 24,11,12,5,13 }, // v14
            {22,5,87,1,88, 24,5,41,5,42, 30,5,24,7,25, 24,11,12,7,13 }, // v15
            {24,5,98,1,99, 28,7,45,3,46, 24,15,19,2,20, 30,3,15,13,16}, // v16
            {28,1,107,5,108, 28,10,46,1,47, 28,1,22,15,23, 28,2,14,17,15 }, // v17
            {30,5,120,1,121, 26,9,43,4,44, 28,17,22,1,23, 28,2,14,19,15 }, // v18
            {28,3,113,4,114, 26,3,44,11,45, 26,17,21,4,22, 26,9,13,16,14 }, // v19
            {28,3,107,5,108, 26,3,41,13,42, 30,15,24,5,25, 28,15,15,10,16 }, // v20
            {28,4,116,4,117, 26,17,42,0,0, 28,17,22,6,23, 30,19,16,6,17 }, // v21
            {28,2,111,7,112, 28,17,46,0,0, 30,7,24,16,25, 24,34,13,0,0 }, // v22
            {30,4,121,5,122, 28,4,47,14,48, 30,11,24,14,25, 30,16,15,14,16 }, // v23
            {30,6,117,4,118, 28,6,45,14,46, 30,11,24,16,25, 30,30,16,2,17 }, // v24
            {26,8,106,4,107, 28,8,47,13,48, 30,7,24,22,25, 30,22,15,13,16 }, // v25
            {28,10,114,2,115, 28,19,46,4,47, 28,28,22,6,23, 30,33,16,4,17 }, // v26
            {30,8,122,4,123, 28,22,45,3,46, 30,8,23,26,24, 30,12,15,28,16 }, // v27
            {30,3,117,10,118, 28,3,45,23,46, 30,4,24,31,25, 30,11,15,31,16 }, // v28
            {30,7,116,7,117, 28,21,45,7,46, 30,1,23,37,24, 30,19,15,26,16 }, // v29
            {30,5,115,10,116, 28,19,47,10,48, 30,15,24,25,25, 30,23,15,25,16 }, // v30
            {30,13,115,3,116, 28,2,46,29,47, 30,42,24,1,25, 30,23,15,28,16 }, // v31
            {30,17,115,0,0,  28,10,46,23,47, 30,10,24,35,25, 30,19,15,35,16 }, // v32
            {30,17,115,1,116, 28,14,46,21,47, 30,29,24,19,25, 30,11,15,46,16 }, // v33
            {30,13,115,6,116, 28,14,46,23,47, 30,44,24,7,25, 30,59,16,1,17 }, // v34
            {30,12,121,7,122, 28,12,47,26,48, 30,39,24,14,25, 30,22,15,41,16 }, // v35
            {30,6,121,14,122, 28,6,47,34,48, 30,46,24,10,25, 30,2,15,64,16 }, // v36
            {30,17,122,4,123, 28,29,46,14,47, 30,49,24,10,25, 30,24,15,46,16 }, // v37
            {30,4,122,18,123, 28,13,46,32,47, 30,48,24,14,25, 30,42,15,32,16 }, // v38
            {30,20,117,4,118, 28,40,47,7,48, 30,43,24,22,25, 30,10,15,67,16 }, // v39
            {30,19,118,6,119, 28,18,47,31,48, 30,34,24,34,25, 30,20,15,61,16 }, // v40
        };

        var result = new QRBlockInfo[40, 4];
        for (var v = 0; v < 40; v++)
        {
            for (var e = 0; e < 4; e++)
            {
                var off = e * 5;
                result[v, e] = new QRBlockInfo(raw[v, off], raw[v, off + 1], raw[v, off + 2], raw[v, off + 3], raw[v, off + 4]);
            }
        }
        return result;
    }
}
