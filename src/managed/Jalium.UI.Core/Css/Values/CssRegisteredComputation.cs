using System.Collections;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Resolves custom properties and their implicit font/color dependencies in one graph.</summary>
internal sealed class CssRegisteredComputation : IReadOnlyDictionary<string, string>
{
    private const string FontNode = "@font-size";
    private const string ColorNode = "@color";
    private const string LineNode = "@line-height";
    private readonly CssNode _element;
    private readonly IReadOnlyDictionary<string, string>? _inherited;
    private readonly IReadOnlyDictionary<string, CssCustomPropertyValue> _declarations;
    private readonly IReadOnlyDictionary<string, CssPropertyRegistration> _registrations;
    private readonly CssLengthContext _lengths;
    private readonly CssCompiledValue? _font, _color;
    private readonly CssCompiledValue? _line;
    private readonly IReadOnlyDictionary<string, CssCompiledValue> _faces;
    private readonly Dictionary<string, object?> _faceValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _computed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cyclic = new(StringComparer.Ordinal);
    private readonly List<string> _visiting = [];
    private readonly Dictionary<string, HashSet<string>> _dependencies;
    private readonly string[] _names;
    private double? _fontSize;
    private double? _lineHeight;
    private Color? _foreground;
    internal Dictionary<string, CssTypedValue> TypedValues { get; } = new(StringComparer.Ordinal);
    internal bool FontCycle => _cyclic.Contains(FontNode);
    internal bool ColorCycle => _cyclic.Contains(ColorNode);
    internal bool LineCycle => _cyclic.Contains(LineNode);
    internal bool FontPropertyCycle(string name) => _cyclic.Contains("@" + name);

    internal CssRegisteredComputation(CssNode element, IReadOnlyDictionary<string, string>? inherited,
        IReadOnlyDictionary<string, CssCustomPropertyValue> declarations, IReadOnlyDictionary<string, CssPropertyRegistration> registrations,
        CssLengthContext lengths, CssCompiledValue? font, CssCompiledValue? color, CssCompiledValue? line = null,
        IReadOnlyDictionary<string, CssCompiledValue>? faces = null)
    {
        _element = element; _inherited = inherited; _declarations = declarations; _registrations = registrations;
        _lengths = lengths; _font = font; _color = color;
        _line = line; _faces = faces ?? new Dictionary<string, CssCompiledValue>();
        _names = (inherited?.Keys ?? []).Concat(registrations.Keys).Concat(declarations.Keys).Distinct(StringComparer.Ordinal).ToArray();
        _dependencies = declarations.ToDictionary(pair => pair.Key,
            pair => CssCustomProperties.References(pair.Value.RawValue).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        AddOrdinaryDependencies(FontNode, "FontSize", font);
        AddOrdinaryDependencies(ColorNode, "Foreground", color);
        AddOrdinaryDependencies(LineNode, "LineHeight", line);
        foreach (var name in new[] { "font-family", "font-weight", "font-style" })
            AddOrdinaryDependencies("@" + name, NativeFontName(name), _faces.GetValueOrDefault(name));
        _cyclic.UnionWith(CssCustomProperties.CyclicNames(_dependencies));
    }

    internal IReadOnlyDictionary<string, string> Compute()
    {
        for (var pass = 0; pass < 8; pass++)
        {
            foreach (var name in _names) Resolve(name);
            // Unit/color computation discovers implicit edges. Revisit earlier branches
            // if those edges join a larger component than the recursive back-edge path.
            var completeCycles = CssCustomProperties.CyclicNames(_dependencies);
            if (completeCycles.All(_cyclic.Contains)) break;
            _cyclic.UnionWith(completeCycles);
            _computed.Clear(); _resolved.Clear(); TypedValues.Clear(); _fontSize = null; _foreground = null;
            _lineHeight = null; _faceValues.Clear();
        }
        return _computed;
    }

    private void AddOrdinaryDependencies(string name, string propertyName, CssCompiledValue? value)
    {
        var property = CssDependencyPropertyLookup.Find(_element.GetType(), propertyName);
        _dependencies[name] = value is CssPendingSubstitution pending && (property is null || !_element.HasLocalOrAnimatedValue(property))
            ? CssCustomProperties.References(pending.RawValue).ToHashSet(StringComparer.Ordinal) : [];
    }

    private void AddImplicitDependency(string name)
    {
        if (_visiting.Count > 0 && _dependencies.TryGetValue(_visiting[^1], out var dependencies)) dependencies.Add(name);
    }

    private void MarkCycle(string name)
    {
        var start = _visiting.IndexOf(name);
        if (start < 0) return;
        foreach (var value in _visiting.Skip(start)) _cyclic.Add(value);
    }

    private string? Resolve(string name)
    {
        if (_visiting.Contains(name)) { MarkCycle(name); return null; }
        if (_resolved.Contains(name)) return _computed.GetValueOrDefault(name);
        if (_visiting.Count >= 128) { _cyclic.Add(name); return null; }
        if (!_declarations.TryGetValue(name, out var declaration))
        { _resolved.Add(name); return SetDefault(name, false); }
        var keyword = CssPropertyMetadata.WideKeyword(declaration.RawValue) ?? declaration.RawValue.Trim();
        if (CssPropertyMetadata.IsWideKeyword(keyword))
        {
            _resolved.Add(name);
            return SetDefault(name, keyword.Equals("inherit", StringComparison.OrdinalIgnoreCase), keyword.Equals("initial", StringComparison.OrdinalIgnoreCase));
        }
        if (_cyclic.Contains(name)) { _resolved.Add(name); return SetDefault(name, false); }
        _visiting.Add(name);
        string? computed = null;
        try
        {
            if (CssCustomProperties.Substitute(declaration.RawValue, dependency => Resolve(dependency), out var expanded,
                validateFallback: ValidateFallback, validateFallbackFor: _registrations.ContainsKey))
            {
                if (!_registrations.TryGetValue(name, out var registration)) computed = expanded;
                else if (registration.Syntax.TryCompute(expanded, Context(declaration.BaseUri), out var typed))
                { computed = typed!.Text; TypedValues[name] = typed; }
            }
        }
        finally { _visiting.RemoveAt(_visiting.Count - 1); }
        _resolved.Add(name);
        if (computed is null || _cyclic.Contains(name))
        { TypedValues.Remove(name); return SetDefault(name, false); }
        _computed[name] = computed;
        return computed;
    }

    private bool ValidateFallback(string name, string fallback)
        => !_registrations.TryGetValue(name, out var registration) || registration.Syntax.TryCompute(fallback, new(_lengths), out _);

    private string? SetDefault(string name, bool explicitInherit, bool initial = false)
    {
        _registrations.TryGetValue(name, out var registration);
        if (!initial && (explicitInherit || registration?.Inherits != false) && _inherited is not null && _inherited.TryGetValue(name, out var inherited))
        {
            _computed[name] = inherited;
            if (registration is not null && registration.Syntax.TryCompute(inherited, Context(null), out var typed)) TypedValues[name] = typed!;
            return inherited;
        }
        if (registration?.InitialValue is { } initialValue && registration.Syntax.TryCompute(initialValue, Context(registration.BaseUri, true), out var value))
        {
            TypedValues[name] = value!; _computed[name] = value!.Text; return value.Text;
        }
        return null;
    }

    private CssPropertyValueContext Context(Uri? baseUri, bool initial = false)
        => new(_lengths, FontSize, ReferenceEquals(CssMatcher.Root(_element), _element), initial, baseUri, LineHeight, Font)
            { CurrentColor = Foreground };

    private static string NativeFontName(string name) => name == "font-family" ? "FontFamily" : name == "font-weight" ? "FontWeight" : "FontStyle";

    private CssFontInfo Font()
    {
        var result = (_lengths.Fonts ?? CssFontContext.Initial).Element;
        foreach (var name in new[] { "font-family", "font-weight", "font-style" })
        {
            var node = "@" + name;
            AddImplicitDependency(node);
            if (_faceValues.TryGetValue(name, out var cached)) { result = result.With(NativeFontName(name), cached); continue; }
            if (_visiting.Contains(node)) { MarkCycle(node); continue; }
            var property = CssDependencyPropertyLookup.Find(_element.GetType(), NativeFontName(name));
            if (!_faces.TryGetValue(name, out var declaration) || property is not null && _element.HasLocalOrAnimatedValue(property)) continue;
            _visiting.Add(node);
            var sink = new ProbeSink();
            try { declaration.TryApply(new CssApplyContext(_element, _lengths, new()), sink); }
            finally { _visiting.RemoveAt(_visiting.Count - 1); }
            if (property is not null && !_cyclic.Contains(node) && sink.Values.TryGetValue(property, out var value))
            { _faceValues[name] = value; result = result.With(property.Name, value); }
        }
        return result;
    }

    private double LineHeight()
    {
        AddImplicitDependency(LineNode);
        if (_lineHeight is { } cached) return cached;
        var fonts = _lengths.Fonts ?? CssFontContext.Initial;
        var fallback = _lengths.WithElementFontSize(FontSize()).WithFonts(fonts with { Element = Font(), ElementLine = fonts.ParentLine });
        if (_visiting.Contains(LineNode)) { MarkCycle(LineNode); return fallback.Fonts!.Resolve(CssUnit.Lh, fallback); }
        var property = CssDependencyPropertyLookup.Find(_element.GetType(), "LineHeight");
        if (_line is null || property is not null && _element.HasLocalOrAnimatedValue(property))
            return fonts.Resolve(CssUnit.Lh, _lengths.WithElementFontSize(FontSize()));
        _visiting.Add(LineNode);
        var sink = new ProbeSink();
        var context = _lengths.WithElementFontSize(FontSize()).WithFonts(fonts with { Element = Font() });
        try { _line.TryApply(new CssApplyContext(_element, context, new()), sink); }
        finally { _visiting.RemoveAt(_visiting.Count - 1); }
        if (!LineCycle && sink.Values.TryGetValue(CssComputedLineHeightValue.Property, out var value) && value is CssLineHeight line)
        {
            context = context.WithFonts(context.Fonts! with { ElementLine = line });
            _lineHeight = context.Fonts!.Resolve(CssUnit.Lh, context);
        }
        else _lineHeight = fallback.Fonts!.Resolve(CssUnit.Lh, fallback);
        return _lineHeight.Value;
    }

    private double FontSize()
    {
        AddImplicitDependency(FontNode);
        if (_fontSize is { } cached) return cached;
        if (_visiting.Contains(FontNode)) { MarkCycle(FontNode); return _lengths.InheritedFontSize; }
        var property = CssDependencyPropertyLookup.Find(_element.GetType(), "FontSize");
        if (property is not null && _element.HasLocalOrAnimatedValue(property)) return _lengths.ElementFontSize;
        if (_font is null) return _lengths.ElementFontSize;
        _visiting.Add(FontNode);
        var sink = new ProbeSink();
        var context = new CssApplyContext(_element, _lengths, new());
        try { _font.TryApply(context, sink); }
        finally { _visiting.RemoveAt(_visiting.Count - 1); }
        _fontSize = !FontCycle && property is not null && sink.Values.TryGetValue(property, out var value) && value is double size && size > 0
            ? size : _lengths.InheritedFontSize;
        return _fontSize.Value;
    }

    private Color Foreground()
    {
        AddImplicitDependency(ColorNode);
        if (_foreground is { } cached) return cached;
        var property = CssDependencyPropertyLookup.Find(_element.GetType(), "Foreground");
        Color Native() => property is not null && _element.GetValue(property) is SolidColorBrush brush ? brush.Color : Colors.Black;
        Color Inherited() => CssMatcher.CssAncestor(_element) is { } parent && property is not null && parent.GetValue(property) is SolidColorBrush brush ? brush.Color : Colors.Black;
        if (_visiting.Contains(ColorNode)) { MarkCycle(ColorNode); return Inherited(); }
        if (_color is null || property is not null && _element.HasLocalOrAnimatedValue(property)) return Native();
        _visiting.Add(ColorNode);
        var sink = new ProbeSink(); var context = new CssApplyContext(_element, _lengths, new());
        try { _color.TryApply(context, sink); }
        finally { _visiting.RemoveAt(_visiting.Count - 1); }
        _foreground = !ColorCycle && property is not null && sink.Values.TryGetValue(property, out var value) && value is SolidColorBrush brush ? brush.Color : Inherited();
        return _foreground.Value;
    }

    private sealed class ProbeSink : ICssSetterSink
    {
        internal readonly Dictionary<DependencyProperty, object?> Values = [];
        public bool CurrentValueIsState { get; set; }
        public void Set(DependencyProperty property, object? value) => Values[property] = value;
        public void SetLayoutState(CssLayoutState state) { }
    }

    public string this[string key] => Resolve(key) ?? throw new KeyNotFoundException(key);
    public IEnumerable<string> Keys => _names.Where(ContainsKey);
    public IEnumerable<string> Values => Keys.Select(key => this[key]);
    public int Count => Keys.Count();
    public bool ContainsKey(string key) => Resolve(key) is not null;
    public bool TryGetValue(string key, out string value) { value = Resolve(key)!; return value is not null; }
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Keys.Select(key => new KeyValuePair<string, string>(key, this[key])).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
