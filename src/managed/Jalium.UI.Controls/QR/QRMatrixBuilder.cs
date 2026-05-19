namespace Jalium.UI.Controls.QR;

/// <summary>
/// Lays out the QR symbol grid: function patterns, format/version info, then zig-zag data placement.
/// </summary>
internal static class QRMatrixBuilder
{
    /// <summary>
    /// Build the final boolean matrix for a given version/ECC, with mask chosen to minimise penalty.
    /// </summary>
    public static (bool[,] Modules, int Mask) Build(int version, QRErrorCorrectionLevel ecc, byte[] interleavedCodewords, int forcedMask = -1)
    {
        var size = QRVersionInfo.ModuleCount(version);

        // Pre-place function modules and remember which cells are reserved.
        var reserved = new bool[size, size];
        var template = new bool[size, size];
        PlaceFinderPatterns(template, reserved, size);
        PlaceSeparators(reserved, size);
        PlaceTimingPatterns(template, reserved, size);
        PlaceAlignmentPatterns(template, reserved, version, size);
        ReserveFormatArea(reserved, size);
        if (version >= 7) ReserveVersionArea(reserved, size);
        PlaceDarkModule(template, reserved, version);

        // Place data bits in zig-zag pattern.
        var dataMatrix = (bool[,])template.Clone();
        PlaceDataBits(dataMatrix, reserved, interleavedCodewords, size);

        // Choose mask.
        int chosenMask;
        bool[,] finalMatrix;
        if (forcedMask >= 0 && forcedMask <= 7)
        {
            chosenMask = forcedMask;
            finalMatrix = ApplyMask(dataMatrix, reserved, chosenMask, size);
            ApplyFormatInfo(finalMatrix, size, ecc, chosenMask);
            if (version >= 7) ApplyVersionInfo(finalMatrix, size, version);
        }
        else
        {
            var bestScore = int.MaxValue;
            var bestMask = 0;
            bool[,]? best = null;
            for (var m = 0; m < 8; m++)
            {
                var candidate = ApplyMask(dataMatrix, reserved, m, size);
                ApplyFormatInfo(candidate, size, ecc, m);
                if (version >= 7) ApplyVersionInfo(candidate, size, version);
                var score = QRMask.Penalty(candidate);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMask = m;
                    best = candidate;
                }
            }
            chosenMask = bestMask;
            finalMatrix = best!;
        }
        return (finalMatrix, chosenMask);
    }

    private static void PlaceFinderPatterns(bool[,] m, bool[,] reserved, int size)
    {
        DrawFinder(m, reserved, 0, 0);
        DrawFinder(m, reserved, size - 7, 0);
        DrawFinder(m, reserved, 0, size - 7);
    }

    private static void DrawFinder(bool[,] m, bool[,] reserved, int top, int left)
    {
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                bool dark = (r == 0 || r == 6 || c == 0 || c == 6) ||
                            (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                m[top + r, left + c] = dark;
                reserved[top + r, left + c] = true;
            }
        }
    }

    private static void PlaceSeparators(bool[,] reserved, int size)
    {
        // White ring of 1 cell around each finder. Modules already default false; just reserve.
        for (var i = 0; i < 8; i++)
        {
            reserved[7, i] = true;
            reserved[i, 7] = true;
            reserved[size - 8, i] = true;
            reserved[i, size - 8] = true;
            reserved[7, size - 1 - i] = true;
            reserved[size - 1 - i, 7] = true;
        }
    }

    private static void PlaceTimingPatterns(bool[,] m, bool[,] reserved, int size)
    {
        for (var i = 8; i < size - 8; i++)
        {
            var dark = i % 2 == 0;
            m[6, i] = dark;
            m[i, 6] = dark;
            reserved[6, i] = true;
            reserved[i, 6] = true;
        }
    }

    private static void PlaceAlignmentPatterns(bool[,] m, bool[,] reserved, int version, int size)
    {
        var centers = QRVersionInfo.AlignmentPatternCenters[version - 1];
        for (var i = 0; i < centers.Length; i++)
        {
            for (var j = 0; j < centers.Length; j++)
            {
                var cy = centers[i];
                var cx = centers[j];
                // Skip overlap with finder patterns.
                if ((cy == 6 && cx == 6) ||
                    (cy == 6 && cx == size - 7) ||
                    (cy == size - 7 && cx == 6))
                {
                    continue;
                }
                DrawAlignment(m, reserved, cy, cx);
            }
        }
    }

    private static void DrawAlignment(bool[,] m, bool[,] reserved, int cy, int cx)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var dark = (Math.Max(Math.Abs(dy), Math.Abs(dx)) != 1) || (dy == 0 && dx == 0);
                m[cy + dy, cx + dx] = dark;
                reserved[cy + dy, cx + dx] = true;
            }
        }
    }

    private static void ReserveFormatArea(bool[,] reserved, int size)
    {
        for (var i = 0; i < 9; i++)
        {
            reserved[8, i] = true;
            reserved[i, 8] = true;
        }
        for (var i = 0; i < 8; i++)
        {
            reserved[8, size - 1 - i] = true;
            reserved[size - 1 - i, 8] = true;
        }
    }

    private static void ReserveVersionArea(bool[,] reserved, int size)
    {
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                reserved[r, size - 11 + c] = true;
                reserved[size - 11 + c, r] = true;
            }
        }
    }

    private static void PlaceDarkModule(bool[,] m, bool[,] reserved, int version)
    {
        var pos = 4 * version + 9; // row index of always-dark module
        m[pos, 8] = true;
        reserved[pos, 8] = true;
    }

    private static void PlaceDataBits(bool[,] m, bool[,] reserved, byte[] codewords, int size)
    {
        var bitIndex = 0;
        var totalBits = codewords.Length * 8;
        var col = size - 1;
        var upward = true;
        while (col > 0)
        {
            if (col == 6) col--; // skip vertical timing column
            for (var k = 0; k < size; k++)
            {
                var row = upward ? size - 1 - k : k;
                for (var c = 0; c < 2; c++)
                {
                    var cc = col - c;
                    if (reserved[row, cc]) continue;
                    if (bitIndex < totalBits)
                    {
                        var bit = (codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1;
                        m[row, cc] = bit == 1;
                        bitIndex++;
                    }
                    // Else leave as 0 (remainder bits).
                }
            }
            col -= 2;
            upward = !upward;
        }
    }

    private static bool[,] ApplyMask(bool[,] dataMatrix, bool[,] reserved, int mask, int size)
    {
        var result = (bool[,])dataMatrix.Clone();
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                if (reserved[r, c]) continue;
                if (QRMask.Apply(mask, r, c))
                {
                    result[r, c] = !result[r, c];
                }
            }
        }
        return result;
    }

    private static void ApplyFormatInfo(bool[,] m, int size, QRErrorCorrectionLevel ecc, int mask)
    {
        var word = QRFormatInfo.Encode(ecc, mask);
        // Top-left copy.
        for (var i = 0; i <= 5; i++) m[8, i] = ((word >> i) & 1) != 0;
        m[8, 7] = ((word >> 6) & 1) != 0;
        m[8, 8] = ((word >> 7) & 1) != 0;
        m[7, 8] = ((word >> 8) & 1) != 0;
        for (var i = 9; i < 15; i++) m[14 - i, 8] = ((word >> i) & 1) != 0;
        // Split copy: bottom-left vertical (7 cells, bits 0..6) + top-right horizontal (8 cells, bits 7..14).
        for (var i = 0; i < 7; i++) m[size - 1 - i, 8] = ((word >> i) & 1) != 0;
        for (var i = 7; i < 15; i++) m[8, size - 15 + i] = ((word >> i) & 1) != 0;
        // Always-dark module ensured by PlaceDarkModule.
    }

    private static void ApplyVersionInfo(bool[,] m, int size, int version)
    {
        var word = QRVersionInfoBits.Encode(version);
        for (var i = 0; i < 18; i++)
        {
            var bit = ((word >> i) & 1) != 0;
            var r = i / 3;
            var c = (i % 3) + size - 11;
            m[r, c] = bit;
            m[c, r] = bit;
        }
    }
}
