using System.Collections.Concurrent;

namespace Jalium.UI.Styling;

public enum CssPropertySupport { Supported, Unsupported, TargetDependent, Unknown = TargetDependent }
public sealed record CssAuthoringProperty(string Name, CssPropertySupport Support, string? InitialValue = null)
{
    public CssAuthoringProperty(string name, string canonicalName, CssPropertySupport support,
        string? initialValue, bool isShorthand) : this(name, support, initialValue)
    {
        CanonicalName = canonicalName;
        IsShorthand = isShorthand;
    }

    public string? CanonicalName { get; init; }
    public bool IsShorthand { get; init; }
    public bool IsInherited { get; init; }
    public string? UnsupportedReason { get; init; }
    /// <summary>Parser-validated keywords and examples. Numeric/string grammars are open-ended.</summary>
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
}
public sealed record CssAuthoringDiagnostic(int Start, int Length, CssDiagnosticSeverity Severity, string Message);

/// <summary>Editor metadata and diagnostics from the same registry/parser used at runtime.</summary>
public static class CssAuthoring
{
    private static readonly ConcurrentDictionary<string, (int Version, CssPropertyDescriptor? Descriptor, CssAuthoringProperty Property)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static CssAuthoringProperty[] GetProperties() => CssPropertyRegistry.GetAuthoringProperties();

    public static CssAuthoringProperty[] GetSupportedProperties()
        => GetProperties().Where(static property => property.Support == CssPropertySupport.Supported).ToArray();

    public static IReadOnlyList<string> GetValues(string name) => GetProperty(name).Values;

    public static CssAuthoringProperty GetProperty(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim().ToLowerInvariant();
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out var canonicalName);
        var version = CssPropertyRegistry.Version;
        if (Cache.TryGetValue(name, out var cached) && cached.Version == version &&
            ReferenceEquals(cached.Descriptor, descriptor)) return cached.Property;
        var support = descriptor == null || descriptor.IsFallback ? CssPropertySupport.Unknown
            : descriptor.Kind == CssPropertyKind.Unsupported ? CssPropertySupport.Unsupported : CssPropertySupport.Supported;
        var initial = support == CssPropertySupport.Supported ? CssPropertyMetadata.Initial(canonicalName) : null;
        var property = new CssAuthoringProperty(name, support, initial)
        {
            CanonicalName = canonicalName,
            IsShorthand = descriptor?.Kind == CssPropertyKind.Shorthand,
            IsInherited = CssPropertyMetadata.IsInherited(canonicalName),
            UnsupportedReason = descriptor?.UnsupportedReason,
            Values = support == CssPropertySupport.Supported
                ? Array.AsReadOnly(CssAuthoringValues.Candidates(canonicalName, initial)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Where(value => IsValueSupported(name, value)).ToArray())
                : Array.Empty<string>(),
        };
        Cache[name] = (version, descriptor, property);
        return property;
    }

    /// <summary>Checks the same implemented value grammar used by the runtime without emitting diagnostics.</summary>
    public static bool IsValueSupported(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        var descriptor = CssPropertyRegistry.Lookup(name);
        return descriptor is { IsFallback: false } && descriptor.Kind != CssPropertyKind.Unsupported &&
            CssDeclarationValueSyntax.IsValid(value) && CssEngine.CompileDeclarations(
                [new CssDeclaration { PropertyName = name, RawValue = value }], reportDiagnostics: false).Length > 0;
    }

    public static IReadOnlyList<CssParseDiagnostic> AnalyzeStyleSheet(string text)
        => Locate(CssStyleSheet.Parse(text).Diagnostics, text);

    public static IReadOnlyList<CssParseDiagnostic> AnalyzeDeclarations(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var diagnostics = new List<CssParseDiagnostic>();
        CssParser.ParseInlineDeclarations(text, diagnostics);
        return Locate(diagnostics, text);
    }

    private static IReadOnlyList<CssParseDiagnostic> Locate(IReadOnlyList<CssParseDiagnostic> diagnostics, string text)
    {
        if (diagnostics.Count == 0) return [];
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') lineStarts.Add(i + 1);
        var result = new CssParseDiagnostic[diagnostics.Count];
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var diagnostic = diagnostics[i];
            if (diagnostic.Start >= 0)
            {
                var mappedStart = OriginalOffset(text, diagnostic.Start);
                var mappedEnd = OriginalOffset(text, diagnostic.Start + diagnostic.Length);
                result[i] = new CssParseDiagnostic(diagnostic.Severity, diagnostic.Message,
                    diagnostic.Line, mappedStart, mappedEnd - mappedStart);
                continue;
            }
            var lineIndex = Math.Clamp(diagnostic.Line - 1, 0, lineStarts.Count - 1);
            var start = lineStarts[lineIndex];
            var end = lineIndex + 1 < lineStarts.Count ? lineStarts[lineIndex + 1] - 1 : text.Length;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            result[i] = new CssParseDiagnostic(diagnostic.Severity, diagnostic.Message,
                diagnostic.Line, start, Math.Min(1, text.Length - start));
        }
        return result;
    }

    private static int OriginalOffset(string text, int normalizedOffset)
    {
        var original = 0;
        var normalized = 0;
        while (normalized < normalizedOffset && original < text.Length)
        {
            if (text[original] == '\r' && original + 1 < text.Length && text[original + 1] == '\n')
                original += 2;
            else original++;
            normalized++;
        }
        return original;
    }
}
