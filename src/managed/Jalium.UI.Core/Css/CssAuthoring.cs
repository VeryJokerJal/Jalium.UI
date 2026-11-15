namespace Jalium.UI.Styling;

/// <summary>The framework's view of a CSS property for editor authoring.</summary>
public enum CssPropertySupport : byte
{
    Supported,
    Unsupported,
    TargetDependent,
}

/// <summary>Read-only metadata derived from the same registry used when CSS is applied.</summary>
public sealed record CssAuthoringProperty(string Name, string CanonicalName,
    CssPropertySupport Support, string? InitialValue, bool IsShorthand);

/// <summary>CSS authoring information shared by editors and build tools.</summary>
public static class CssAuthoring
{
    public static CssAuthoringProperty[] GetSupportedProperties()
        => GetProperties().Where(static property => property.Support == CssPropertySupport.Supported).ToArray();

    public static IReadOnlyList<string> GetValues(string name)
    {
        var property = GetProperty(name);
        if (property.Support != CssPropertySupport.Supported) return [];
        return CssAuthoringValues.Candidates(property.CanonicalName, property.InitialValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value => CssDeclarationValueSyntax.IsValid(value) &&
                CssEngine.CompileDeclarations([new CssDeclaration { PropertyName = property.CanonicalName, RawValue = value }],
                    reportDiagnostics: false).Length > 0)
            .ToArray();
    }

    public static IReadOnlyList<CssAuthoringProperty> GetProperties()
        => CssPropertyRegistry.GetAuthoringNames()
            .Select(GetProperty)
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();

    public static CssAuthoringProperty GetProperty(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out var canonicalName);
        var support = descriptor is null || descriptor.IsFallback
            ? CssPropertySupport.TargetDependent
            : descriptor.Kind == CssPropertyKind.Unsupported
                ? CssPropertySupport.Unsupported : CssPropertySupport.Supported;
        return new(name, canonicalName, support, CssPropertyMetadata.Initial(canonicalName),
            descriptor?.Kind == CssPropertyKind.Shorthand);
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
