namespace Jalium.UI.Controls.QR;

internal static class QRMask
{
    public static bool Apply(int mask, int row, int column)
    {
        return mask switch
        {
            0 => (row + column) % 2 == 0,
            1 => row % 2 == 0,
            2 => column % 3 == 0,
            3 => (row + column) % 3 == 0,
            4 => ((row / 2) + (column / 3)) % 2 == 0,
            5 => ((row * column) % 2) + ((row * column) % 3) == 0,
            6 => (((row * column) % 2) + ((row * column) % 3)) % 2 == 0,
            7 => (((row + column) % 2) + ((row * column) % 3)) % 2 == 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mask))
        };
    }

    /// <summary>
    /// Compute penalty score per ISO/IEC 18004 8.8.2 (lower is better).
    /// </summary>
    public static int Penalty(bool[,] modules)
    {
        var size = modules.GetLength(0);
        return Rule1(modules, size) + Rule2(modules, size) + Rule3(modules, size) + Rule4(modules, size);
    }

    private static int Rule1(bool[,] m, int size)
    {
        var penalty = 0;
        for (var i = 0; i < size; i++)
        {
            for (var dir = 0; dir < 2; dir++)
            {
                bool prev = false;
                var run = 0;
                for (var j = 0; j < size; j++)
                {
                    var cell = dir == 0 ? m[i, j] : m[j, i];
                    if (j == 0 || cell != prev)
                    {
                        if (run >= 5) penalty += 3 + (run - 5);
                        prev = cell;
                        run = 1;
                    }
                    else
                    {
                        run++;
                    }
                }
                if (run >= 5) penalty += 3 + (run - 5);
            }
        }
        return penalty;
    }

    private static int Rule2(bool[,] m, int size)
    {
        var penalty = 0;
        for (var r = 0; r < size - 1; r++)
        {
            for (var c = 0; c < size - 1; c++)
            {
                var v = m[r, c];
                if (m[r, c + 1] == v && m[r + 1, c] == v && m[r + 1, c + 1] == v)
                {
                    penalty += 3;
                }
            }
        }
        return penalty;
    }

    private static int Rule3(bool[,] m, int size)
    {
        // Pattern: 1011101 with 4-or-more 0 padding on either side.
        var penalty = 0;
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c <= size - 11; c++)
            {
                if (MatchFinderPattern(m, r, c, horizontal: true)) penalty += 40;
            }
        }
        for (var c = 0; c < size; c++)
        {
            for (var r = 0; r <= size - 11; r++)
            {
                if (MatchFinderPattern(m, r, c, horizontal: false)) penalty += 40;
            }
        }
        return penalty;
    }

    private static bool MatchFinderPattern(bool[,] m, int r, int c, bool horizontal)
    {
        // 11-bit pattern: 0000_1011101 OR 1011101_0000 (must be true on either side)
        Span<bool> bits = stackalloc bool[11];
        for (var i = 0; i < 11; i++)
        {
            bits[i] = horizontal ? m[r, c + i] : m[r + i, c];
        }
        ReadOnlySpan<bool> core = stackalloc bool[] { true, false, true, true, true, false, true };

        // Pattern A: 1011101 at [0..6] + zeros at [7..10]
        var a = true;
        for (var i = 0; i < 7 && a; i++) if (bits[i] != core[i]) a = false;
        if (a)
        {
            for (var i = 7; i < 11 && a; i++) if (bits[i]) a = false;
            if (a) return true;
        }
        // Pattern B: zeros at [0..3] + 1011101 at [4..10]
        var b = true;
        for (var i = 0; i < 4 && b; i++) if (bits[i]) b = false;
        if (b)
        {
            for (var i = 0; i < 7 && b; i++) if (bits[4 + i] != core[i]) b = false;
            if (b) return true;
        }
        return false;
    }

    private static int Rule4(bool[,] m, int size)
    {
        var dark = 0;
        for (var r = 0; r < size; r++)
            for (var c = 0; c < size; c++)
                if (m[r, c]) dark++;
        var total = size * size;
        var percent = dark * 100 / total;
        var k = Math.Abs(percent - 50) / 5;
        return k * 10;
    }
}
