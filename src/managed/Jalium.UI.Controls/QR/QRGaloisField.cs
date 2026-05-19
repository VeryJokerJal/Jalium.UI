namespace Jalium.UI.Controls.QR;

internal static class QRGaloisField
{
    public static readonly byte[] Exp = new byte[512];
    public static readonly byte[] Log = new byte[256];

    static QRGaloisField()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
            {
                x ^= 0x11D;
            }
        }
        for (var i = 255; i < 512; i++)
        {
            Exp[i] = Exp[i - 255];
        }
    }

    public static int Multiply(int a, int b)
    {
        if (a == 0 || b == 0) return 0;
        return Exp[Log[a] + Log[b]];
    }
}
