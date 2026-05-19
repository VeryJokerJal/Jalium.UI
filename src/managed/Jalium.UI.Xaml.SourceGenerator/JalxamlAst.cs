namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Compile-time abstract-syntax representation of a parsed JALXAML document. Captured
/// once during the SourceGenerator's parse pass and consumed by
/// <see cref="JalxamlCodeGenerator"/> to emit the strongly-typed
/// <c>InitializeComponent</c> body. This is the "uic" intermediate — it preserves every
/// piece of structure the runtime would need to re-derive from the raw XML so the
/// generator can lay it out as straight-line C#.
/// </summary>
public sealed class JalxamlAstNode
{
    /// <summary>Local name of the element (e.g. <c>Button</c>, <c>SaaSBackground</c>).</summary>
    public string LocalName { get; set; } = string.Empty;

    /// <summary>XML namespace URI the element was declared under.</summary>
    public string NamespaceUri { get; set; } = string.Empty;

    /// <summary>Optional prefix the author used (<c>x</c>, <c>controls</c>, ...). Diagnostics only.</summary>
    public string? Prefix { get; set; }

    /// <summary>Element line number in the source document.</summary>
    public int LineNumber { get; set; }

    /// <summary>Element line position in the source document.</summary>
    public int LinePosition { get; set; }

    /// <summary>Attributes on the element (xmlns:* / x:* and value attributes).</summary>
    public List<JalxamlAstAttribute> Attributes { get; } = new();

    /// <summary>
    /// Property-element children: <c>&lt;Foo.Bar&gt;...&lt;/Foo.Bar&gt;</c>. Captured separately
    /// from <see cref="Children"/> so the generator can emit
    /// <c>XamlBuilder.ApplyPropertyElementChild</c> for each one.
    /// </summary>
    public List<JalxamlAstPropertyElement> PropertyElements { get; } = new();

    /// <summary>
    /// Regular element children — emitted as <c>XamlBuilder.AddChild(parent, child, ctx)</c>.
    /// </summary>
    public List<JalxamlAstNode> Children { get; } = new();

    /// <summary>
    /// Significant text content (e.g. <c>&lt;TextBlock&gt;Hello&lt;/TextBlock&gt;</c>). Whitespace-only
    /// content is dropped during parsing.
    /// </summary>
    public string? TextContent { get; set; }

    /// <summary>
    /// Resolved CLR full type name (<c>Jalium.UI.Controls.Button</c>) — populated by the
    /// type-resolution pass in <see cref="JalxamlParser"/>. Null if the element name could
    /// not be proved to map to a known CLR type at parse time; the generator then bails out
    /// and emits the legacy LoadComponent fallback.
    /// </summary>
    public string? ResolvedClrTypeName { get; set; }

    /// <summary>
    /// Best-effort CLR type name for the element. Always non-null. Used for x:Name field
    /// declarations on the codebehind even when the codegen path is not taken — the
    /// codebehind authors reference derived members (e.g. <c>ToggleSwitch.IsOn</c>) so a
    /// generic <c>FrameworkElement</c> field is too weak even for the fallback.
    /// </summary>
    public string FallbackClrTypeName { get; set; } = "Jalium.UI.FrameworkElement";

    /// <summary>
    /// Combined Razor <c>@if</c> condition guarding this child, set when the element sat
    /// inside one or more <c>@if(...) { ... }</c> blocks that the parser lifted into the
    /// synthetic <c>__RazorIf</c> wrapper and then flattened away. Null for unconditional
    /// elements. The string mirrors the runtime
    /// <c>XamlReader.BuildCombinedIfConditionExpression</c> shape — each nesting level wrapped
    /// in parentheses, outermost first, joined by <c> &amp;&amp; </c> (e.g. <c>(A) &amp;&amp; (B)</c>).
    /// The generator emits one <c>XamlBuilder.SetRazorIfVisibility</c> call per conditional
    /// child so the runtime applies the exact same visibility binding the streaming parser
    /// would, with no document re-parse.
    /// </summary>
    public string? RazorIfCondition { get; set; }
}

/// <summary>
/// One attribute on a JALXAML element. Capable of representing:
/// <list type="bullet">
/// <item>Value attributes: <c>Width="100"</c>, <c>Background="#FFF"</c>, <c>Title="{Binding Foo}"</c>.</item>
/// <item>Attached properties: <c>Grid.Row="0"</c> (kind = <see cref="JalxamlAttributeKind.Attached"/>).</item>
/// <item>X-directives: <c>x:Name="Foo"</c>, <c>x:Class="Ns.Foo"</c>, <c>x:Key="Bar"</c>.</item>
/// <item>xmlns declarations: skipped during emit, recorded for diagnostics.</item>
/// </list>
/// </summary>
public sealed class JalxamlAstAttribute
{
    public JalxamlAttributeKind Kind { get; set; }

    /// <summary>The local name without prefix and without owner part.</summary>
    /// <remarks>For <c>Grid.Row</c> this is <c>Row</c>, for <c>Width</c> this is <c>Width</c>.</remarks>
    public string LocalName { get; set; } = string.Empty;

    /// <summary>Owner type for attached properties (<c>Grid</c> for <c>Grid.Row</c>).</summary>
    public string? AttachedOwner { get; set; }

    /// <summary>Author-supplied prefix (<c>x</c>, <c>controls</c>) — null when unprefixed.</summary>
    public string? Prefix { get; set; }

    /// <summary>Resolved XML namespace URI for the attribute.</summary>
    public string NamespaceUri { get; set; } = string.Empty;

    /// <summary>Raw attribute value as it appears in the document.</summary>
    public string Value { get; set; } = string.Empty;
}

public enum JalxamlAttributeKind
{
    Value,        // Width="100"
    Attached,     // Grid.Row="0"
    XDirective,   // x:Name / x:Class / x:Key
    XmlnsDecl,    // xmlns / xmlns:foo
}

/// <summary>
/// A property-element form on a parent — <c>&lt;Window.LeftWindowCommands&gt;...&lt;/Window.LeftWindowCommands&gt;</c>.
/// The generator emits one <c>XamlBuilder.ApplyPropertyElementChild</c> per immediate
/// child; the runtime then decides between collection-add, dictionary-insert, or
/// single-value set semantics based on the property's declared type.
/// </summary>
public sealed class JalxamlAstPropertyElement
{
    /// <summary>Owner type prefix part — e.g. <c>Window</c> in <c>Window.LeftWindowCommands</c>. Used to discriminate own-type vs attached-property element forms.</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Property suffix — e.g. <c>LeftWindowCommands</c>.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>XML namespace URI of the property element.</summary>
    public string NamespaceUri { get; set; } = string.Empty;

    /// <summary>Optional author-supplied prefix on the property element.</summary>
    public string? Prefix { get; set; }

    /// <summary>Children of the property-element body (one per child element). Each one is applied independently.</summary>
    public List<JalxamlAstNode> Children { get; } = new();
}
