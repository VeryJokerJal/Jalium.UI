using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.Primitives;

/// <summary>
/// Zero-allocation slice over a <see cref="string"/>. Mirrors MS <c>StringSegment</c>.
/// </summary>
public readonly struct StringSegment : IEquatable<StringSegment>, IEquatable<string>
{
    public static readonly StringSegment Empty = new(string.Empty, 0, 0);

    public StringSegment(string? buffer)
    {
        Buffer = buffer;
        Offset = 0;
        Length = buffer?.Length ?? 0;
    }

    public StringSegment(string? buffer, int offset, int length)
    {
        if (buffer == null)
        {
            if (offset != 0 || length != 0) throw new ArgumentOutOfRangeException(nameof(offset));
        }
        else
        {
            if ((uint)offset > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if ((uint)length > (uint)(buffer.Length - offset)) throw new ArgumentOutOfRangeException(nameof(length));
        }
        Buffer = buffer;
        Offset = offset;
        Length = length;
    }

    public string? Buffer { get; }
    public int Offset { get; }
    public int Length { get; }
    public bool HasValue => Buffer != null;
    public ReadOnlySpan<char> AsSpan() => Buffer == null ? ReadOnlySpan<char>.Empty : Buffer.AsSpan(Offset, Length);
    public ReadOnlyMemory<char> AsMemory() => Buffer == null ? ReadOnlyMemory<char>.Empty : Buffer.AsMemory(Offset, Length);

    public char this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(index));
            return Buffer![Offset + index];
        }
    }

    public StringSegment Subsegment(int offset)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return new StringSegment(Buffer, Offset + offset, Length - offset);
    }
    public StringSegment Subsegment(int offset, int length)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if ((uint)length > (uint)(Length - offset)) throw new ArgumentOutOfRangeException(nameof(length));
        return new StringSegment(Buffer, Offset + offset, length);
    }

    public StringSegment Trim()
    {
        if (Buffer == null) return this;
        var start = Offset;
        var end = Offset + Length - 1;
        while (start <= end && char.IsWhiteSpace(Buffer[start])) start++;
        while (end >= start && char.IsWhiteSpace(Buffer[end])) end--;
        return new StringSegment(Buffer, start, end - start + 1);
    }

    public bool StartsWith(string value, StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > Length) return false;
        return AsSpan().StartsWith(value.AsSpan(), comparison);
    }

    public bool EndsWith(string value, StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > Length) return false;
        return AsSpan().EndsWith(value.AsSpan(), comparison);
    }

    public int IndexOf(char c)
    {
        if (Buffer == null) return -1;
        for (int i = 0; i < Length; i++) if (Buffer[Offset + i] == c) return i;
        return -1;
    }

    public bool Equals(StringSegment other) => Equals(other, StringComparison.Ordinal);
    public bool Equals(StringSegment other, StringComparison comparison) => AsSpan().Equals(other.AsSpan(), comparison);
    public bool Equals([NotNullWhen(true)] string? other) => Equals(other, StringComparison.Ordinal);
    public bool Equals([NotNullWhen(true)] string? other, StringComparison comparison)
    {
        if (other == null) return Buffer == null;
        return AsSpan().Equals(other.AsSpan(), comparison);
    }
    public override bool Equals(object? obj) => obj switch
    {
        StringSegment s => Equals(s),
        string str => Equals(str),
        _ => false,
    };
    public override int GetHashCode()
    {
        if (Buffer == null) return 0;
        return string.GetHashCode(AsSpan());
    }
    public override string ToString() => Buffer == null ? string.Empty : Buffer.Substring(Offset, Length);

    public static bool operator ==(StringSegment a, StringSegment b) => a.Equals(b);
    public static bool operator !=(StringSegment a, StringSegment b) => !a.Equals(b);
    public static implicit operator StringSegment(string? s) => new(s);
    public static implicit operator ReadOnlySpan<char>(StringSegment s) => s.AsSpan();
}
