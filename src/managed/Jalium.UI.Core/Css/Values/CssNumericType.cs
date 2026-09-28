namespace Jalium.UI.Styling;

// Intermediate products retain all dimensions; only the result is matched against
// a property's grammar. A percent hint survives cancellation of its dimension.
internal readonly record struct CssNumericType(
    int Length = 0, int Angle = 0, int Time = 0, int Frequency = 0,
    int Resolution = 0, int Flex = 0, int Percent = 0, CssNumericKind? PercentHint = null)
{
    internal static CssNumericType Of(CssNumericKind kind) => kind switch
    {
        CssNumericKind.Length => new(Length: 1), CssNumericKind.Angle => new(Angle: 1),
        CssNumericKind.Time => new(Time: 1), CssNumericKind.Frequency => new(Frequency: 1),
        CssNumericKind.Resolution => new(Resolution: 1), CssNumericKind.Flex => new(Flex: 1),
        CssNumericKind.Percent => new(Percent: 1), _ => default,
    };

    internal CssNumericKind Kind
    {
        get
        {
            var plain = this with { PercentHint = null };
            if (plain == default) return CssNumericKind.Number;
            foreach (var kind in s_dimensions)
                if (plain == Of(kind)) return kind == CssNumericKind.Length && PercentHint is not null
                    ? CssNumericKind.LengthPercent : kind;
            return CssNumericKind.Compound;
        }
    }

    private static readonly CssNumericKind[] s_dimensions =
    [CssNumericKind.Length, CssNumericKind.Angle, CssNumericKind.Time, CssNumericKind.Frequency,
        CssNumericKind.Resolution, CssNumericKind.Flex, CssNumericKind.Percent];

    private CssNumericType? Hint(CssNumericKind hint)
    {
        if (PercentHint is { } previous && previous != hint) return null;
        var type = this with { Percent = 0, PercentHint = hint };
        return hint switch
        {
            CssNumericKind.Length => type with { Length = Length + Percent },
            CssNumericKind.Angle => type with { Angle = Angle + Percent },
            CssNumericKind.Time => type with { Time = Time + Percent },
            CssNumericKind.Frequency => type with { Frequency = Frequency + Percent },
            CssNumericKind.Resolution => type with { Resolution = Resolution + Percent },
            CssNumericKind.Flex => type with { Flex = Flex + Percent }, _ => null,
        };
    }

    private static bool ShareHint(ref CssNumericType a, ref CssNumericType b)
    {
        if ((a.PercentHint ?? b.PercentHint) is not { } hint) return true;
        if (a.Hint(hint) is not { } left || b.Hint(hint) is not { } right) return false;
        a = left; b = right; return true;
    }

    internal static CssNumericType? Add(CssNumericType a, CssNumericType b)
    {
        if (!ShareHint(ref a, ref b)) return null;
        if (a == b) return a;
        if (a.Percent == 0 && b.Percent == 0) return null;
        foreach (var hint in s_dimensions)
            if (hint != CssNumericKind.Percent && a.Hint(hint) is { } left && b.Hint(hint) is { } right && left == right)
                return left;
        return null;
    }

    internal static CssNumericType? Multiply(CssNumericType a, CssNumericType b, bool divide)
    {
        if (!ShareHint(ref a, ref b)) return null;
        var sign = divide ? -1 : 1;
        return new(a.Length + sign * b.Length, a.Angle + sign * b.Angle,
            a.Time + sign * b.Time, a.Frequency + sign * b.Frequency,
            a.Resolution + sign * b.Resolution, a.Flex + sign * b.Flex,
            a.Percent + sign * b.Percent, a.PercentHint ?? b.PercentHint);
    }

    internal CssNumericType Result(CssNumericKind kind) => Of(kind) with { PercentHint = PercentHint };
}
