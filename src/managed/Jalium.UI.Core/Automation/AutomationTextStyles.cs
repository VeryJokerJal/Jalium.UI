namespace Jalium.UI.Automation;

// Formatting is optional and separate from character offsets and visual rows.
internal interface IAutomationTextStyleSource
{
    IReadOnlyList<AutomationTextStyleSpan> GetTextStyles();
}

internal readonly record struct AutomationTextStyle(
    string FontFamily, double FontSize, int FontWeight, int FontStyle,
    uint? Foreground, int Underline = 0, uint? UnderlineColor = null,
    int Strikethrough = 0, uint? StrikethroughColor = null,
    int Alignment = 0, int Direction = -1, string? Language = null);

internal readonly record struct AutomationTextStyleSpan(int Start, int Length, AutomationTextStyle Style);

/// <summary>Values returned by text providers for attributes that differ within a range.</summary>
public static class AutomationTextAttributeValues
{
    /// <summary>Identifies a supported text attribute with multiple values in the queried range.</summary>
    public static object Mixed { get; } = new();
}

internal static class AutomationTextStyles
{
    internal static IReadOnlyList<AutomationTextStyleSpan> GetRuns(IAutomationTextProviderSource source)
        => source is IAutomationTextStyleSource styles ? styles.GetTextStyles() : [];

    internal static void Add(List<AutomationTextStyleSpan> spans, int start, int length, AutomationTextStyle style)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0 && spans.Count != 0) return;
        if (spans.Count > 0 && spans[^1] is var last && last.Start + last.Length == start && last.Style == style)
            spans[^1] = last with { Length = last.Length + length };
        else spans.Add(new(start, length, style));
    }

    internal static AutomationTextStyleSpan? At(IReadOnlyList<AutomationTextStyleSpan> runs, int offset, int textLength)
    {
        if (offset < 0 || offset > textLength) return null;
        foreach (var run in runs)
            if (offset >= run.Start && offset < run.Start + run.Length || run.Length == 0 && offset == run.Start) return run;
        return offset == textLength && runs.Count != 0 ? runs[^1] : null;
    }

    internal static object? Value(AutomationTextStyle style, int attribute) => attribute switch
    {
        40005 => style.FontFamily,
        40006 => style.FontSize,
        40007 => style.FontWeight,
        40008 => ColorRef(style.Foreground),
        40009 => style.Alignment switch { 1 => 2, 2 => 1, _ => style.Alignment },
        40014 => style.FontStyle != 0,
        40025 => ColorRef(style.StrikethroughColor),
        40026 => DecorationStyle(style.Strikethrough),
        40028 => style.Direction switch { 0 => 0, 1 => 1, _ => (int?)null },
        40029 => ColorRef(style.UnderlineColor),
        40030 => DecorationStyle(style.Underline),
        _ => null
    };

    private static int? ColorRef(uint? color) => color is { } c
        ? (int)((c & 0xff) << 16 | (c & 0xff00) | (c >> 16 & 0xff)) : null;
    private static int DecorationStyle(int style) => (style & 0xff) == 9 ? 3
        : (style & 0xff) == 0 ? 0 : (style & 0xf00) switch { 0x100 => 2, 0x200 => 5, _ => 1 };
}
