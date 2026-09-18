namespace Jalium.UI.Styling;

public enum CssDiagnosticSeverity : byte
{
    Info,
    Warning,
}

/// <summary>A parse-time diagnostic. CSS parsing never throws; malformed constructs are skipped.</summary>
public readonly struct CssParseDiagnostic
{
    public CssDiagnosticSeverity Severity { get; }
    public string Message { get; }
    public int Line { get; }

    internal CssParseDiagnostic(CssDiagnosticSeverity severity, string message, int line)
    {
        Severity = severity;
        Message = message;
        Line = line;
    }

    public override string ToString() => $"{Severity} (line {Line}): {Message}";
}

/// <summary>One declaration: property name (lower-cased), raw value text, and the !important flag.</summary>
internal sealed class CssDeclaration
{
    public required string PropertyName;
    public required string RawValue;
    public bool Important;

    /// <summary>Per-target-type conversion cache, populated lazily by the apply pipeline.</summary>
    internal object? ConversionCache;
}

/// <summary>A rule set: selector group plus declarations, in document order.</summary>
internal sealed class CssRule
{
    public required CssSelector[] Selectors;
    public required CssDeclaration[] Declarations;
    public int RuleIndex;
    public Uri? BaseUri;
    public CssCondition? Condition;
    public string? LayerName;
    public CssScopeRule? Scope;

    internal IEnumerable<CssSelector> DependencySelectors => Scope is null ? Selectors : Selectors.Concat(Scope.Selectors());
    internal bool HasStructuralDependencies => Scope is not null || Selectors.Any(s => s.HasStructuralDependencies);

    /// <summary>Longhand-expanded declarations, populated lazily by the apply pipeline.</summary>
    internal object? ExpandedDeclarations;

    /// <summary>Registry version the expansion was compiled against (public-mapping invalidation).</summary>
    internal int ExpandedVersion;
}

/// <summary>
/// A parsed CSS style sheet. Instances are immutable after parsing and can be shared
/// across elements, windows, and threads.
/// </summary>
public sealed partial class CssStyleSheet
{
    private readonly CssParseDiagnostic[] _diagnostics;

    internal CssRule[] Rules { get; }
    internal CssImport[] Imports { get; }
    internal CssLayerDeclaration[] Layers { get; }
    internal CssPropertyRegistration[] Properties { get; }

    internal CssStateMask AncestorStateUnion { get; }

    internal CssStateMask AnyStateUnion { get; }

    internal bool HasCombinators { get; }

    internal bool UsesId { get; }

    public string? SourceLabel { get; }

    public IReadOnlyList<CssParseDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Resolves a pack/application URI to a stream. Registered by the Jalium.UI.Xaml assembly
    /// (module initializer), mirroring <see cref="TypeResolver.ResolveTypeByName"/>.
    /// </summary>
    internal static Func<Uri, Stream?>? UriStreamResolver { get; set; }

    internal CssStyleSheet(CssRule[] rules, CssParseDiagnostic[] diagnostics, string? sourceLabel, CssImport[]? imports = null,
        CssLayerDeclaration[]? layers = null, CssPropertyRegistration[]? properties = null)
    {
        Rules = rules;
        Imports = imports ?? [];
        Layers = layers ?? [];
        Properties = properties ?? [];
        if (Properties.Length > 0) CssRegisteredProperties.IsActive = true;
        _diagnostics = diagnostics;
        SourceLabel = sourceLabel;

        var ancestorStates = CssStateMask.None;
        var anyStates = CssStateMask.None;
        var hasCombinators = false;
        var usesId = false;
        foreach (var rule in rules)
        {
            foreach (var selector in rule.DependencySelectors)
            {
                ancestorStates |= selector.AncestorStates;
                anyStates |= selector.RightmostStates | selector.AncestorStates;
                hasCombinators |= selector.HasCombinators;
                foreach (var compound in selector.Compounds)
                {
                    usesId |= compound.Id is not null;
                }
            }
        }

        AncestorStateUnion = ancestorStates;
        AnyStateUnion = anyStates;
        HasCombinators = hasCombinators;
        UsesId = usesId;
    }

    public static CssStyleSheet Parse(string cssText, string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        return CssParser.Parse(cssText, sourceLabel);
    }

    public static CssStyleSheet Parse(string cssText, string? sourceLabel, Uri? baseUri)
    {
        var sheet = Parse(cssText, sourceLabel);
        foreach (var rule in sheet.Rules) rule.BaseUri = baseUri;
        foreach (var property in sheet.Properties) property.BaseUri = baseUri;
        return sheet;
    }

    public static CssStyleSheet FromStream(Stream stream, string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), sourceLabel);
    }

    /// <summary>Loads a style sheet from a pack/application URI (e.g. "/App;component/styles/app.css").</summary>
    public static CssStyleSheet FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var resolver = UriStreamResolver;
        var stream = resolver?.Invoke(uri);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Unable to resolve CSS style sheet '{uri}'. Ensure the resource exists and the Jalium.UI.Xaml assembly is loaded.");
        }

        using (stream)
        {
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd(), uri.ToString(), uri);
        }
    }
}
