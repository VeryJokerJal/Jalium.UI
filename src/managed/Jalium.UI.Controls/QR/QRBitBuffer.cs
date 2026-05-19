namespace Jalium.UI.Controls.QR;

internal sealed class QRBitBuffer
{
    private readonly List<byte> _bytes = new();
    private int _bitLength;

    public int BitLength => _bitLength;

    public void AppendBits(int value, int bitCount)
    {
        if (bitCount < 0 || bitCount > 32)
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        for (var i = bitCount - 1; i >= 0; i--)
        {
            AppendBit((value >> i) & 1);
        }
    }

    public void AppendBit(int bit)
    {
        var byteIndex = _bitLength >> 3;
        if (byteIndex >= _bytes.Count)
        {
            _bytes.Add(0);
        }
        if (bit != 0)
        {
            var bitInByte = 7 - (_bitLength & 7);
            _bytes[byteIndex] |= (byte)(1 << bitInByte);
        }
        _bitLength++;
    }

    public void AppendBytes(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            AppendBits(b, 8);
        }
    }

    public byte[] ToBytes()
    {
        // Pad final byte (caller is responsible for any structural padding).
        return _bytes.ToArray();
    }
}
