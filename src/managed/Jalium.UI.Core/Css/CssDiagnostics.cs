using System.Collections.Concurrent;

namespace Jalium.UI.Styling;

internal enum CssDiagnosticReason : byte
{
    UnknownProperty,
    UnsupportedProperty,
    TargetPropertyMissing,
    InvalidValue,
    LossyConversion,
}

/// <summary>
/// Runtime diagnostics for the CSS engine. Mirrors Style.ReportUnresolvedSetter: deduplicated
/// per (property, reason, target type), one-shot, never throws. A style sheet applied to
/// hundreds of elements reports each problem once.
/// </summary>
public static class CssDiagnostics
{
    private static readonly ConcurrentDictionary<(string, byte, Type?), byte> s_reported = new();

    /// <summary>Set to false to silence CSS diagnostics entirely (default: enabled).</summary>
    public static bool LogUnresolvedProperties { get; set; } = true;

    /// <summary>Raised for parse-time diagnostics when style sheets are parsed through the engine.</summary>
    public static event Action<CssParseDiagnostic>? ParseDiagnostic;

    internal static void RaiseParseDiagnostics(IReadOnlyList<CssParseDiagnostic> diagnostics)
    {
        var handler = ParseDiagnostic;
        if (handler is null)
        {
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            handler(diagnostic);
        }
    }

    internal static void Report(string cssProperty, CssDiagnosticReason reason, Type? targetType, string detail)
    {
        if (!LogUnresolvedProperties)
        {
            return;
        }

        if (!s_reported.TryAdd((cssProperty, (byte)reason, targetType), 0))
        {
            return;
        }

        var target = targetType is null ? string.Empty : $" (on {targetType.Name})";
        var message = $"[Jalium.UI.Css] {reason}: '{cssProperty}'{target} — {detail}";
        System.Diagnostics.Debug.WriteLine(message);
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // Console may be unavailable in some hosts; diagnostics must never throw.
        }
    }

    internal static void ResetForTests() => s_reported.Clear();
}
