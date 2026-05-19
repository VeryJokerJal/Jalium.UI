namespace Jalium.UI.Controls.QR;

/// <summary>
/// Reed-Solomon encoder over GF(256) for QR Code per ISO/IEC 18004.
/// Uses standard polynomial division: result = data·x^n mod g(x).
/// </summary>
internal static class QRReedSolomon
{
    /// <summary>
    /// Compute the <paramref name="ecCount"/> ECC bytes for <paramref name="data"/>.
    /// </summary>
    public static byte[] ComputeEcc(byte[] data, int ecCount)
    {
        var generator = BuildGenerator(ecCount); // length = ecCount + 1, leading coeff = 1
        var work = new byte[data.Length + ecCount];
        Buffer.BlockCopy(data, 0, work, 0, data.Length);

        for (var i = 0; i < data.Length; i++)
        {
            var coef = work[i];
            if (coef == 0) continue;
            var logCoef = QRGaloisField.Log[coef];
            for (var j = 0; j < generator.Length; j++)
            {
                var g = generator[j];
                if (g == 0) continue;
                work[i + j] ^= QRGaloisField.Exp[QRGaloisField.Log[g] + logCoef];
            }
        }

        var result = new byte[ecCount];
        Buffer.BlockCopy(work, data.Length, result, 0, ecCount);
        return result;
    }

    private static readonly Dictionary<int, byte[]> s_generatorCache = new();
    private static readonly object s_lock = new();

    /// <summary>
    /// Build the generator polynomial g(x) = (x - α^0)(x - α^1)...(x - α^(n-1)) of degree n,
    /// returned highest-degree-first with length n + 1.
    /// </summary>
    private static byte[] BuildGenerator(int n)
    {
        lock (s_lock)
        {
            if (s_generatorCache.TryGetValue(n, out var cached))
                return cached;

            byte[] poly = { 1 };
            for (var i = 0; i < n; i++)
            {
                // Multiply poly (stored highest-degree-first) by (x + α^i).
                // High-to-low recursion: new[j] = poly[j] (shifted) and new[j+1] += α^i * poly[j].
                var alphaPow = QRGaloisField.Exp[i];
                var next = new byte[poly.Length + 1];
                for (var j = 0; j < poly.Length; j++)
                {
                    next[j] ^= poly[j];
                    next[j + 1] ^= (byte)QRGaloisField.Multiply(poly[j], alphaPow);
                }
                poly = next;
            }
            s_generatorCache[n] = poly;
            return poly;
        }
    }
}
