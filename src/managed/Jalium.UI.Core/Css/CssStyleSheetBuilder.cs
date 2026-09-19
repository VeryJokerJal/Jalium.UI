using System.ComponentModel;

namespace Jalium.UI.Styling;

/// <summary>
/// Construction contract used by generated CSS C#. Node handles belong to one builder.
/// Reconstructs parsed syntax without the stylesheet or selector parsers; property
/// conversion remains in the versioned runtime mapping pipeline.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CssStyleSheetBuilder
{
    private readonly List<object> _nodes = [];
    private readonly List<CssRule> _rules = [];
    private readonly List<CssImport> _imports = [];
    private readonly List<CssLayerDeclaration> _layers = [];
    private readonly List<CssPropertyRegistration> _properties = [];
    private readonly List<CssParseDiagnostic> _diagnostics = [];
    private bool _built;

    public CssStyleSheetBuilder(int formatVersion)
    {
        if (formatVersion != 1)
            throw new NotSupportedException($"Unsupported compiled CSS format {formatVersion}. Rebuild with matching Jalium.UI packages.");
    }

    private int Add(object node)
    {
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    private T Get<T>(int handle) where T : class => (T)_nodes[handle];
    private T? Optional<T>(int handle) where T : class => handle < 0 ? null : Get<T>(handle);
    private T[] GetAll<T>(int[] handles) where T : class => Array.ConvertAll(handles, Get<T>);

    public int Attribute(string name, string operation, string value, bool ignoreCase, string? namespaceUri)
        => Add(new CssAttributeSelector(name, operation, value, ignoreCase, namespaceUri));

    public int Function(string name, int[] selectors, int a, int b, int nestingScope)
        => Add(new CssFunctionalPseudo(name, GetAll<CssSelector>(selectors), a, b, Optional<CssScopeRule>(nestingScope)));

    public int Compound(string? typeName, bool universal, string? namespaceUri, bool expandedTypeName,
        bool explicitTypeSelector, bool defaultNamespaceApplied, string? id, string[]? classes,
        byte[]? pseudos, ushort stateMask, int[]? attributes, int[]? functions)
        => Add(new CssCompound
        {
            TypeName = typeName, Universal = universal, NamespaceUri = namespaceUri,
            ExpandedTypeName = expandedTypeName, ExplicitTypeSelector = explicitTypeSelector,
            DefaultNamespaceApplied = defaultNamespaceApplied, Id = id, Classes = classes,
            Pseudos = pseudos is null ? null : Array.ConvertAll(pseudos, p => (CssPseudoClass)p),
            StateMask = (CssStateMask)stateMask,
            Attributes = attributes is null ? null : GetAll<CssAttributeSelector>(attributes),
            Functions = functions is null ? null : GetAll<CssFunctionalPseudo>(functions),
        });

    public int Selector(int[] compounds, byte[] combinators, int specificity, ushort rightmostStates,
        ushort ancestorStates, bool containsNesting, bool containsScope)
        => Add(new CssSelector
        {
            Compounds = GetAll<CssCompound>(compounds),
            Combinators = Array.ConvertAll(combinators, c => (CssCombinator)c),
            Specificity = specificity, RightmostStates = (CssStateMask)rightmostStates,
            AncestorStates = (CssStateMask)ancestorStates,
            ContainsNesting = containsNesting, ContainsScope = containsScope,
        });

    public int Scope(int[]? starts, int[]? limits, int parent)
        => Add(new CssScopeRule(starts is null ? null : GetAll<CssSelector>(starts),
            limits is null ? null : GetAll<CssSelector>(limits), Optional<CssScopeRule>(parent)));

    public int Namespaces(string? defaultNamespace, string[] prefixes, string[] uris)
    {
        if (prefixes.Length != uris.Length) throw new ArgumentException("Namespace arrays must have equal lengths.");
        return Add(new CssNamespaceContext(defaultNamespace, prefixes.Zip(uris)));
    }

    public int Condition(string kind, string query, int left, int right, int namespaces)
        => Add(new CssCondition(kind, query, Optional<CssCondition>(left), Optional<CssCondition>(right),
            Optional<CssNamespaceContext>(namespaces))
        {
            ContainerQuery = kind == "container" ? CssContainerQuery.Parse(query) : null,
        });

    public int Declaration(string propertyName, string rawValue, bool important)
        => Add(new CssDeclaration { PropertyName = propertyName, RawValue = rawValue, Important = important });

    public void Rule(int[] selectors, int[] declarations, int condition, string? layerName, int scope)
        => _rules.Add(new CssRule
        {
            Selectors = GetAll<CssSelector>(selectors), Declarations = GetAll<CssDeclaration>(declarations),
            RuleIndex = _rules.Count, Condition = Optional<CssCondition>(condition), LayerName = layerName,
            Scope = Optional<CssScopeRule>(scope),
        });

    public string AnonymousLayer() => CssLayerOrder.AnonymousName();

    public void Layer(string name, int offset, int condition, bool anonymous)
        => _layers.Add(new CssLayerDeclaration(name, offset, Optional<CssCondition>(condition), anonymous));

    public void Import(string reference, int line, int offset, string? layerName, bool anonymous,
        int supports, int media)
        => _imports.Add(new CssImport(reference, line, offset, layerName, anonymous,
            Optional<CssCondition>(supports), Optional<CssCondition>(media)));

    public void Property(string name, string[] syntaxNames, bool[] syntaxTypes, char[] syntaxMultipliers,
        bool universal, bool inherits, string? initialValue, int offset, int condition, string? layerName)
    {
        if (syntaxNames.Length != syntaxTypes.Length || syntaxNames.Length != syntaxMultipliers.Length)
            throw new ArgumentException("Property syntax arrays must have equal lengths.");
        var components = new CssPropertySyntax.Component[syntaxNames.Length];
        for (var i = 0; i < components.Length; i++)
            components[i] = new(syntaxNames[i], syntaxTypes[i], syntaxMultipliers[i]);
        _properties.Add(new CssPropertyRegistration(name, new CssPropertySyntax(components, universal),
            inherits, initialValue, offset)
        {
            Condition = Optional<CssCondition>(condition), LayerName = layerName,
        });
    }

    public void Diagnostic(byte severity, string message, int line)
        => _diagnostics.Add(new CssParseDiagnostic((CssDiagnosticSeverity)severity, message, line));

    public CssStyleSheet Build(string? sourceLabel = null, Uri? baseUri = null)
    {
        if (_built) throw new InvalidOperationException("A CSS builder can only build one stylesheet.");
        _built = true;
        foreach (var rule in _rules) rule.BaseUri = baseUri;
        foreach (var property in _properties) property.BaseUri = baseUri;
        return new CssStyleSheet(_rules.ToArray(), _diagnostics.ToArray(), sourceLabel,
            _imports.ToArray(), _layers.ToArray(), _properties.ToArray());
    }
}
