using System.Collections.Concurrent;

namespace Jalium.UI.Styling;

public enum CssPropertySupport { Supported, Unsupported, Unknown }
public sealed record CssAuthoringProperty(string Name, CssPropertySupport Support, string? InitialValue = null)
{
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
    private static readonly ConcurrentDictionary<string, (int Version, CssAuthoringProperty Property)> Cache = new(StringComparer.OrdinalIgnoreCase);

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
        if (Cache.TryGetValue(name, out var cached) && cached.Version == version) return cached.Property;
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
        Cache[name] = (version, property);
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

    public static IReadOnlyList<CssAuthoringDiagnostic> AnalyzeStyleSheet(string text)
    {
        var sheet = CssStyleSheet.Parse(text);
        return sheet.Diagnostics.Select(diagnostic =>
        {
            var offset = 0;
            for (var line = 1; line < diagnostic.Line && offset < text.Length; line++)
            {
                var next = text.IndexOf('\n', offset);
                offset = next < 0 ? text.Length : next + 1;
            }
            return new CssAuthoringDiagnostic(offset, Math.Min(1, text.Length - offset), diagnostic.Severity, diagnostic.Message);
        }).ToArray();
    }

    public static IReadOnlyList<CssAuthoringDiagnostic> AnalyzeDeclarations(string text)
    {
        const string prefix = ":root {\n";
        return AnalyzeStyleSheet(prefix + text + "\n}").Select(diagnostic => diagnostic with
        { Start = Math.Clamp(diagnostic.Start - prefix.Length, 0, text.Length), Length = Math.Min(diagnostic.Length, text.Length) }).ToArray();
    }
}
