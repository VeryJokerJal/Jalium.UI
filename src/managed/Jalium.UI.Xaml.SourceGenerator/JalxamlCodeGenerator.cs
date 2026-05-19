using System.Text;
using Microsoft.CodeAnalysis;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Emits the C# body of <c>InitializeComponent</c> from a parsed JALXAML AST tree. The
/// generator walks the <see cref="JalxamlAstNode"/> tree and produces straight-line
/// constructor + setter calls routed through <see cref="Jalium.UI.Markup.XamlBuilder"/>.
/// This is the "uic" path — XML lexing, type-name lookup and named-element field reflection
/// all happen at compile time. Type converters, markup extensions and add-collection
/// dispatch still flow through the runtime so the existing semantics are preserved.
/// </summary>
internal static class JalxamlCodeGenerator
{
    /// <summary>
    /// Try to emit the body of <c>InitializeComponent</c>. Returns null when the document
    /// uses features the codegen does not support yet — unresolved CLR type for an element,
    /// illegal template shape, or structural Razor (<c>@if</c> / <c>@section</c>) which only
    /// the runtime parser can interpret. Caller falls back to the runtime LoadComponent path.
    /// VALUE-EXPRESSION Razor (<c>@(expr)</c> / <c>@identifier</c>) is lowered in-place by
    /// the per-attribute / per-text-node emitters and does NOT bail here.
    /// </summary>
    public static string? TryEmitInitializeBody(JalxamlParseResult result, SymbolTypeHelper? symbols, XmlnsTypeResolver? xmlnsResolver = null)
    {
        if (result.Root == null || result.HasStructuralRazor)
        {
            return null;
        }
        AugmentUnresolvedTypes(result.Root!, xmlnsResolver);
        if (string.IsNullOrEmpty(result.Root!.ResolvedClrTypeName))
        {
            return null;
        }
        // The codegen cannot represent any element whose type the parser failed to resolve.
        if (HasUnresolvedNode(result.Root!))
        {
            return null;
        }
        // ControlTemplate / DataTemplate / ItemsPanelTemplate / HierarchicalDataTemplate
        // are factory types — LoadContent() must produce a fresh visual tree on every
        // call. We handle them by wrapping the visual root in a lambda and routing through
        // SetVisualTree(...) — see EmitTemplateNode. Multi-child template bodies (rare)
        // fall back to the runtime parser via HasIllegalTemplateShape.
        if (HasIllegalTemplateShape(result.Root!))
        {
            return null;
        }

        var sb = new StringBuilder();
        var counter = new IndexCounter();
        var namedAlready = new HashSet<string>(StringComparer.Ordinal);
        var ctx = new EmitContext(symbols, result.RootPrefixMappings, xmlnsResolver);

        // Open: cast `this` to the strongly-typed root, begin a build context.
        sb.AppendLine("        var __ctx = global::Jalium.UI.Markup.XamlBuilder.BeginComponent(this, baseUri: null, sourceAssembly: null);");
        sb.AppendLine($"        var __root = (global::{result.Root.ResolvedClrTypeName})this;");

        // Emit the root: PushParent(root), apply attributes / property elements / children, PopParent.
        EmitElementBody(sb, result.Root, "__root", counter, namedAlready, indent: 8, ctx);

        // Wire x:Name fields — direct field assignment from the runtime named-elements map.
        // The SG-emitted code knows the field names (and types) statically, so no reflection.
        if (result.NamedElements.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("        // x:Name field wiring (direct assignment, no reflection).");
            foreach (var named in result.NamedElements)
            {
                if (string.IsNullOrEmpty(named.Name))
                    continue;
                if (!namedAlready.Add(named.Name))
                    continue;
                // GetNamed returns object?; cast to the field's declared type. The cast is
                // safe — the parser only ever populates the named-elements map with the
                // element instance whose declared element type matches the field.
                sb.AppendLine($"        {named.Name} = (global::{named.TypeName})global::Jalium.UI.Markup.XamlBuilder.GetNamed(\"{named.Name}\", __ctx)!;");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        global::Jalium.UI.Markup.XamlBuilder.EndComponent(__root, __ctx);");

        return sb.ToString();
    }

    /// <summary>
    /// Per-emission context — caches per-type symbol resolutions so the generator does not
    /// re-walk the inheritance chain for every attribute on the same element. Lifetime
    /// scoped to one <see cref="TryEmitInitializeBody"/> call.
    /// </summary>
    private sealed class EmitContext
    {
        public SymbolTypeHelper? Symbols { get; }
        public Dictionary<string, INamedTypeSymbol?> ResolvedTypeCache { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Snapshot of <c>xmlns:prefix → namespaceUri</c> declarations on the document root.
        /// Used to resolve attribute-value prefix references like
        /// <c>TargetType="controls:WelcomeTemplateCard"</c> at compile time.
        /// </summary>
        public IReadOnlyDictionary<string, string> RootPrefixes { get; }

        public XmlnsTypeResolver? XmlnsResolver { get; }

        /// <summary>
        /// C# expression that evaluates to the document owner inside the generated
        /// <c>InitializeComponent</c> body. Used by template <c>SetVisualTree</c> lambdas
        /// to push the owner onto the build context's ambient parent stack so nested
        /// <c>{StaticResource X}</c> lookups can reach <c>Owner.Resources</c>.
        /// <list type="bullet">
        /// <item>Path A (codebehind partial class) → <c>"this"</c> (the FE/UserControl).</item>
        /// <item>Path B (anonymous dictionary builder) → <c>"__target"</c> (the supplied
        /// <see cref="Jalium.UI.ResourceDictionary"/>).</item>
        /// </list>
        /// </summary>
        public string OwnerExpression { get; }

        public EmitContext(SymbolTypeHelper? symbols, IReadOnlyDictionary<string, string>? rootPrefixes = null, XmlnsTypeResolver? xmlnsResolver = null, string ownerExpression = "this")
        {
            Symbols = symbols;
            RootPrefixes = rootPrefixes ?? new Dictionary<string, string>(StringComparer.Ordinal);
            XmlnsResolver = xmlnsResolver;
            OwnerExpression = ownerExpression;
        }

        public INamedTypeSymbol? ResolveType(string fullMetadataName)
        {
            if (Symbols == null)
                return null;
            if (ResolvedTypeCache.TryGetValue(fullMetadataName, out var cached))
                return cached;
            var resolved = Symbols.ResolveType(fullMetadataName);
            ResolvedTypeCache[fullMetadataName] = resolved;
            return resolved;
        }

        /// <summary>
        /// Try to lower a string of the form <c>"prefix:LocalName"</c> (or bare
        /// <c>"LocalName"</c> for the default xmlns) into a fully-qualified
        /// <c>global::Foo.Bar.Baz</c> CLR type name. Used by the Type-typed property
        /// fast path so <c>TargetType="controls:Foo"</c> compiles directly to
        /// <c>typeof(global::Project.Controls.Foo)</c> without a runtime SetProperty
        /// round-trip — which the runtime <c>TypeTypeConverter</c> would have failed
        /// (it consults the simple-name <c>XamlTypeRegistry</c> and doesn't understand
        /// <c>prefix:</c> notation).
        /// </summary>
        public bool TryResolvePrefixedType(string value, out string globalQualifiedName)
        {
            globalQualifiedName = string.Empty;
            if (XmlnsResolver == null || string.IsNullOrEmpty(value))
                return false;

            var trimmed = value.Trim();
            string prefix;
            string localName;
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx > 0)
            {
                prefix = trimmed.Substring(0, colonIdx);
                localName = trimmed.Substring(colonIdx + 1);
            }
            else
            {
                prefix = string.Empty;
                localName = trimmed;
            }

            if (!RootPrefixes.TryGetValue(prefix, out var nsUri) || string.IsNullOrEmpty(nsUri))
                return false;

            var resolved = XmlnsResolver.ResolveToGlobalQualifiedName(localName, nsUri);
            if (string.IsNullOrEmpty(resolved))
                return false;

            globalQualifiedName = resolved!;
            return true;
        }
    }

    /// <summary>
    /// Emit the body of the prebuilt-dictionary <c>Build(target, ctx)</c> static method.
    /// Mirrors <see cref="TryEmitInitializeBody"/> but operates on a caller-supplied
    /// ResourceDictionary rather than casting <c>this</c>. No x:Name field wiring (the
    /// document has no codebehind), no <c>BeginComponent</c> / <c>EndComponent</c>
    /// (the caller is responsible for those — the runtime ThemeLoader hands us a
    /// pre-existing context for the lookup).
    /// </summary>
    public static string? TryEmitDictionaryBuildBody(JalxamlParseResult result, SymbolTypeHelper? symbols, XmlnsTypeResolver? xmlnsResolver = null)
    {
        if (result.Root == null || result.HasStructuralRazor)
            return null;
        AugmentUnresolvedTypes(result.Root!, xmlnsResolver);
        if (string.IsNullOrEmpty(result.Root!.ResolvedClrTypeName))
            return null;
        if (HasUnresolvedNode(result.Root!))
            return null;
        // Same factory-template-shape guard as TryEmitInitializeBody — see comment there.
        if (HasIllegalTemplateShape(result.Root!))
            return null;

        var sb = new StringBuilder();
        var counter = new IndexCounter();
        var namedAlready = new HashSet<string>(StringComparer.Ordinal);
        var ctx = new EmitContext(symbols, result.RootPrefixMappings, xmlnsResolver, ownerExpression: "__target");

        // The runtime supplies the dictionary instance directly. We push it onto the
        // parent stack so any descendant lookups (resource references, parent-aware
        // markup extensions) behave exactly like the streaming parser would have.
        EmitElementBody(sb, result.Root, "__target", counter, namedAlready, indent: 8, ctx);

        return sb.ToString();
    }

    /// <summary>
    /// Fill in element types the parser's curated table could not resolve, using the
    /// compilation-backed <see cref="XmlnsTypeResolver"/> (clr-namespace + assembly-declared
    /// <c>XmlnsDefinition</c> + every referenced assembly). Without this, an element whose
    /// XML namespace is a custom URI mapped via <c>[assembly: XmlnsDefinition]</c>
    /// (third-party / library controls) is left unresolved and the WHOLE document falls
    /// back to the runtime parser (<c>XamlReader.LoadComponentFromString</c>). Only fills
    /// when the parser left the type empty — the curated fast path and the parser's
    /// <c>clr-namespace:</c> concatenation stay authoritative where they apply. A null
    /// resolver (e.g. white-box unit tests) makes this a no-op so prior behaviour is
    /// preserved. Genuinely unknown types stay empty and still bail via
    /// <see cref="HasUnresolvedNode"/> — this never forces a wrong type.
    /// </summary>
    private static void AugmentUnresolvedTypes(JalxamlAstNode node, XmlnsTypeResolver? resolver)
    {
        if (resolver == null)
            return;

        if (string.IsNullOrEmpty(node.ResolvedClrTypeName))
        {
            var g = resolver.ResolveToGlobalQualifiedName(node.LocalName, node.NamespaceUri);
            if (!string.IsNullOrEmpty(g))
            {
                // ResolveToGlobalQualifiedName yields a `global::`-prefixed name; the
                // codegen re-prefixes `global::` itself, so store the bare FQN.
                var bare = g!.StartsWith("global::", StringComparison.Ordinal) ? g!.Substring(8) : g!;
                node.ResolvedClrTypeName = bare;
                node.FallbackClrTypeName = bare;
            }
        }

        foreach (var child in node.Children)
            AugmentUnresolvedTypes(child, resolver);
        foreach (var pe in node.PropertyElements)
            foreach (var child in pe.Children)
                AugmentUnresolvedTypes(child, resolver);
    }

    private static bool HasUnresolvedNode(JalxamlAstNode node)
    {
        if (string.IsNullOrEmpty(node.ResolvedClrTypeName))
            return true;
        foreach (var child in node.Children)
        {
            if (HasUnresolvedNode(child))
                return true;
        }
        foreach (var pe in node.PropertyElements)
        {
            foreach (var child in pe.Children)
            {
                if (HasUnresolvedNode(child))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns true when the AST contains a template node the codegen cannot fold into a
    /// <c>SetVisualTree(() =&gt; ...)</c> lambda. The only structural shape we cannot lower
    /// is a multi-child template (template factories accept a single visual-tree root).
    ///
    /// <para>
    /// Complex markup extensions inside a template body (multi-arg, nested, <c>{Binding}</c>
    /// with a converter etc.) are <b>not</b> a problem: they flow through
    /// <see cref="Jalium.UI.Markup.XamlBuilder.SetProperty"/> which forwards the unparsed
    /// string to the runtime markup-extension parser. Each <c>LoadContent</c> invocation
    /// re-runs the lambda, so the parser executes against a fresh element instance — the
    /// same timing the streaming XAML reader would have honoured. Resource resolution
    /// (<c>StaticResource</c> / <c>DynamicResource</c>) walks the live ancestor chain at
    /// apply time, not at lambda-execution time, so deferred lookup keeps working.
    /// </para>
    /// </summary>
    private static bool HasIllegalTemplateShape(JalxamlAstNode node)
    {
        var enteringTemplate = IsTemplateClrType(node.ResolvedClrTypeName);
        if (enteringTemplate && node.Children.Count > 1)
            return true;

        foreach (var child in node.Children)
        {
            if (HasIllegalTemplateShape(child))
                return true;
        }
        foreach (var pe in node.PropertyElements)
        {
            foreach (var child in pe.Children)
            {
                if (HasIllegalTemplateShape(child))
                    return true;
            }
        }
        return false;
    }

    private static bool IsTemplateClrType(string? clrTypeName)
    {
        if (string.IsNullOrEmpty(clrTypeName))
            return false;
        // Match by exact full name — there is no template hierarchy inheritance the SG
        // would need to follow without symbol-level information, and the framework has
        // a small fixed set.
        switch (clrTypeName)
        {
            case "Jalium.UI.ControlTemplate":
            case "Jalium.UI.DataTemplate":
            case "Jalium.UI.HierarchicalDataTemplate":
            case "Jalium.UI.ItemsPanelTemplate":
            case "Jalium.UI.ItemContainerTemplate":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Emit the per-element body — attributes, property-element bodies, child elements,
    /// text content. The variable holding the element instance is provided as
    /// <paramref name="varName"/>; for the root this is <c>__root</c>, otherwise a
    /// freshly-allocated <c>__cN</c> from the parent's emission.
    /// </summary>
    private static void EmitElementBody(
        StringBuilder sb,
        JalxamlAstNode node,
        string varName,
        IndexCounter counter,
        HashSet<string> namedAlready,
        int indent,
        EmitContext ctx)
    {
        var pad = new string(' ', indent);

        sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.PushParent({varName}, __ctx);");

        // Resolve the element's CLR type symbol once per element. Used by the value-attribute
        // path to look up declared property types and emit strongly-typed setters when the
        // value is a literal we can convert at compile time.
        var elementSymbol = node.ResolvedClrTypeName != null
            ? ctx.ResolveType(node.ResolvedClrTypeName)
            : null;

        // Apply x:Directive attributes BEFORE value attributes — x:Name registration must
        // happen before any property setter that might reference the named element through
        // a binding source. (This mirrors the streaming parser's order.)
        foreach (var attr in node.Attributes)
        {
            if (attr.Kind != JalxamlAttributeKind.XDirective)
                continue;
            if (attr.LocalName == "Class")
                continue; // x:Class is handled at compile time, not at runtime.
            sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.ApplyXDirective({varName}, \"{attr.LocalName}\", {EscapeStringLiteral(attr.Value)}, __ctx);");
        }

        // Compatibility: runtime parser also treats unprefixed `Name="..."` as x:Name.
        foreach (var attr in node.Attributes)
        {
            if (attr.Kind == JalxamlAttributeKind.Value &&
                attr.LocalName == "Name" &&
                string.IsNullOrEmpty(attr.Prefix))
            {
                sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.ApplyXDirective({varName}, \"Name\", {EscapeStringLiteral(attr.Value)}, __ctx);");
            }
        }

        // Value attributes — try strongly-typed compile-time setter first, fall back to the
        // runtime SetProperty pipeline (which handles type converters, markup extensions
        // and Razor expressions).
        foreach (var attr in node.Attributes)
        {
            if (attr.Kind != JalxamlAttributeKind.Value)
                continue;
            if (attr.LocalName == "Name" && string.IsNullOrEmpty(attr.Prefix))
                continue;
            EmitValueAttribute(sb, varName, attr, elementSymbol, ctx, pad, node.ResolvedClrTypeName);
        }

        // Attached properties — use strongly-typed fast paths for known framework owners,
        // generic SetAttachedProperty (reflective) for the rest.
        foreach (var attr in node.Attributes)
        {
            if (attr.Kind != JalxamlAttributeKind.Attached)
                continue;
            EmitAttached(sb, varName, attr, pad);
        }

        // Property elements — emit one ApplyPropertyElementChild per immediate child.
        foreach (var pe in node.PropertyElements)
        {
            EmitPropertyElement(sb, varName, node, pe, counter, namedAlready, indent, ctx);
        }

        // Templates have factory semantics (LoadContent must produce a fresh visual tree
        // per call) so the visual root is wrapped in a <c>SetVisualTree(() =&gt; ...)</c>
        // lambda instead of being AddChild'd onto the template instance. Each LoadContent
        // call re-runs the lambda, producing an independent visual tree as the runtime
        // parser would have. Templates have at most one direct child by contract; multi-
        // child templates are filtered earlier by HasIllegalTemplateShape.
        var isTemplate = IsTemplateClrType(node.ResolvedClrTypeName);
        if (isTemplate)
        {
            EmitTemplateVisualTree(sb, varName, node, counter, namedAlready, indent, ctx);
        }
        else
        {
            // Regular children — construct + recurse + AddChild. Pass the parent's AST node
            // so EmitAddChildToParent can resolve its CLR type symbol and emit a strongly-
            // typed property/collection access (Panel.Children.Add / Border.Child / etc.)
            // instead of the reflective XamlBuilder.AddChild dispatch.
            foreach (var child in node.Children)
            {
                EmitChildElement(sb, varName, child, counter, namedAlready, indent, ctx, asPropertyElementChild: null, parentNode: node);
            }
        }

        // Text content (e.g. <TextBlock>Hello</TextBlock>, <Color>#FFFFFF</Color>). Route
        // through XamlBuilder.SetContentText, which delegates to the runtime
        // SetContentProperty pipeline so [ContentProperty] / TypeConverter rules apply
        // exactly as the streaming parser would. The call MAY replace the instance
        // (e.g. <Color>#FFFFFF</Color> → ColorConverter.ConvertFrom returns a new boxed
        // Color). When we know the element's CLR type we cast the returned object back
        // and reassign so subsequent AddChild / ApplyPropertyElementChild on the parent
        // see the parsed value rather than the default-constructed stub. Without this
        // reassignment the dictionary entry would still be the default Color (0,0,0,0).
        if (!isTemplate && !string.IsNullOrEmpty(node.TextContent))
        {
            // Razor text-content fast path: TextBlock "Count: @Items.Count" → SetContentRazorBinding.
            if (RazorExpressionLowering.TryLowerAttributeValue(node.TextContent!, out var contentExpr, out var contentDeps))
            {
                sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.SetContentRazorBinding({varName}, {EscapeStringLiteral(contentExpr!)}, {RazorExpressionLowering.EmitDependencyArray(contentDeps!)}, __ctx);");
            }
            else
            {
                var castType = elementSymbol != null
                    ? SymbolTypeHelper.ToGlobalName(elementSymbol)
                    : (node.ResolvedClrTypeName != null ? $"global::{node.ResolvedClrTypeName}" : "global::System.Object");
                sb.AppendLine($"{pad}{varName} = ({castType})global::Jalium.UI.Markup.XamlBuilder.SetContentText({varName}, {EscapeStringLiteral(node.TextContent!)}, __ctx);");
            }
        }

        sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.PopParent(__ctx);");
    }

    /// <summary>
    /// Emit the visual-tree root of a template as a <c>SetVisualTree(() =&gt; ...)</c>
    /// lambda. Each invocation of <c>LoadContent</c> re-runs the lambda and produces a
    /// fresh visual tree, matching the factory semantics of <see cref="Jalium.UI.ControlTemplate"/>
    /// / <see cref="Jalium.UI.DataTemplate"/>.
    /// </summary>
    /// <remarks>
    /// The lambda captures the outer <c>__ctx</c> via a normal C# closure. Markup-extension
    /// fast paths (<c>SetDynamicResource</c>) and runtime <c>SetProperty</c> reuse the
    /// captured context, which is fine because the build session has finished by the time
    /// templates are instantiated and the parent stack is empty. Resource resolution
    /// (DynamicResource subscription, ResourceLookup) doesn't depend on the build context's
    /// parent stack — it walks the FE's live ancestor chain at apply time.
    /// </remarks>
    private static void EmitTemplateVisualTree(
        StringBuilder sb,
        string templateVar,
        JalxamlAstNode templateNode,
        IndexCounter counter,
        HashSet<string> namedAlready,
        int indent,
        EmitContext ctx)
    {
        if (templateNode.Children.Count == 0)
            return;

        var pad = new string(' ', indent);
        var rootChild = templateNode.Children[0];

        sb.AppendLine($"{pad}{templateVar}.SetVisualTree(() =>");
        sb.AppendLine($"{pad}{{");

        var inner = indent + 4;
        var innerPad = new string(' ', inner);
        var rootIndex = counter.Next();
        var rootVar = $"__t{rootIndex}";

        // Re-prime the build context's ambient resource stack with the captured
        // codebehind / dictionary instance so {StaticResource X} markup-extension
        // resolution inside the template body can walk back into the owning view's
        // Resources. Without this push the lambda runs with an empty parent stack
        // (BeginComponent/EndComponent already finished by template instantiation
        // time), so any nested {Binding ..., Converter={StaticResource Y}} fails its
        // ambient lookup and lands on Binding.PendingConverterKey for late resolution
        // — which works for templated FEs that get parented, but is brittle for
        // non-FE Converter resources or initial paint scenarios. Pushing the codebehind
        // here lets the parser hit the resource directly on first try, mirroring the
        // streaming XAML reader's behaviour where ambient parents are always available.
        // We push/pop strictly around the body so concurrent LoadContent calls don't
        // accumulate stack entries.
        sb.AppendLine($"{innerPad}global::Jalium.UI.Markup.XamlBuilder.PushParent({ctx.OwnerExpression}, __ctx);");
        sb.AppendLine($"{innerPad}try");
        sb.AppendLine($"{innerPad}{{");

        var bodyInner = inner + 4;
        var bodyPad = new string(' ', bodyInner);

        // Build the visual root inside the lambda body. EmitElementBody recurses into
        // child elements; we use a fresh named-alreday set so x:Name fields declared
        // outside the template don't collide with names inside. Templates expose names
        // via FrameworkTemplate.FindName at apply time, not via codebehind fields.
        sb.AppendLine($"{bodyPad}var {rootVar} = new global::{rootChild.ResolvedClrTypeName!}();");
        EmitElementBody(sb, rootChild, rootVar, counter, new HashSet<string>(StringComparer.Ordinal), bodyInner, ctx);
        sb.AppendLine($"{bodyPad}return {rootVar};");

        sb.AppendLine($"{innerPad}}}");
        sb.AppendLine($"{innerPad}finally");
        sb.AppendLine($"{innerPad}{{");
        sb.AppendLine($"{bodyPad}global::Jalium.UI.Markup.XamlBuilder.PopParent(__ctx);");
        sb.AppendLine($"{innerPad}}}");

        sb.AppendLine($"{pad}}});");
    }

    /// <summary>
    /// Emit a compiled <c>@section Name { body }</c> registration. The body is built by a
    /// captured factory delegate (same lambda + ambient-resource priming as
    /// <see cref="EmitTemplateVisualTree"/>) and registered via
    /// <c>XamlBuilder.RegisterRazorSection</c>. <see cref="Jalium.UI.Markup.RazorSectionHost"/>
    /// invokes the factory per render — no XAML string is stored or re-parsed. The body is
    /// a single element (enforced by <c>JalxamlParser.ValidateLiftedSections</c>); a lifted
    /// <c>@if</c> directly wrapping it carries its condition through, applied before return.
    /// </summary>
    private static void EmitRazorSectionRegistration(
        StringBuilder sb,
        JalxamlAstNode sectionNode,
        IndexCounter counter,
        int indent,
        EmitContext ctx)
    {
        if (sectionNode.Children.Count == 0)
            return; // empty @section — nothing to register (matches an empty runtime body).

        string sectionName = "";
        foreach (var a in sectionNode.Attributes)
        {
            if (a.Kind == JalxamlAttributeKind.Value &&
                string.Equals(a.LocalName, "__SectionName", StringComparison.Ordinal))
            {
                sectionName = a.Value;
                break;
            }
        }
        if (sectionName.Length == 0)
            return;

        var pad = new string(' ', indent);
        var rootChild = sectionNode.Children[0];

        sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.RegisterRazorSection({EscapeStringLiteral(sectionName)}, () =>");
        sb.AppendLine($"{pad}{{");

        var inner = indent + 4;
        var innerPad = new string(' ', inner);
        var rootIndex = counter.Next();
        var rootVar = $"__sec{rootIndex}";

        // Prime the ambient resource stack with the defining component so
        // {StaticResource} inside the section body resolves against its Resources —
        // same rationale as EmitTemplateVisualTree.
        sb.AppendLine($"{innerPad}global::Jalium.UI.Markup.XamlBuilder.PushParent({ctx.OwnerExpression}, __ctx);");
        sb.AppendLine($"{innerPad}try");
        sb.AppendLine($"{innerPad}{{");

        var bodyInner = inner + 4;
        var bodyPad = new string(' ', bodyInner);

        sb.AppendLine($"{bodyPad}var {rootVar} = new global::{rootChild.ResolvedClrTypeName!}();");
        EmitElementBody(sb, rootChild, rootVar, counter, new HashSet<string>(StringComparer.Ordinal), bodyInner, ctx);

        // A lifted @if wrapping the whole section body stamped the root with a combined
        // condition — bind its Visibility exactly as elsewhere so the section content
        // honours the conditional.
        if (!string.IsNullOrEmpty(rootChild.RazorIfCondition))
        {
            var ifDeps = RazorExpressionLowering.ExtractConditionDependencies(rootChild.RazorIfCondition);
            sb.AppendLine(
                $"{bodyPad}global::Jalium.UI.Markup.XamlBuilder.SetRazorIfVisibility({rootVar}, {EscapeStringLiteral(rootChild.RazorIfCondition!)}, {RazorExpressionLowering.EmitDependencyArray(ifDeps)}, __ctx);");
        }

        sb.AppendLine($"{bodyPad}return ({rootVar} as object);");

        sb.AppendLine($"{innerPad}}}");
        sb.AppendLine($"{innerPad}finally");
        sb.AppendLine($"{innerPad}{{");
        sb.AppendLine($"{bodyPad}global::Jalium.UI.Markup.XamlBuilder.PopParent(__ctx);");
        sb.AppendLine($"{innerPad}}}");

        sb.AppendLine($"{pad}}});");
    }

    /// <summary>
    /// Emit one value attribute (<c>PropName="value"</c>). Tries to lower the assignment
    /// to a strongly-typed setter call (<c>__c.Prop = literal;</c>) when:
    /// <list type="bullet">
    ///   <item>The element's CLR type symbol is resolvable, and</item>
    ///   <item>It declares a writable public instance property with the right name, and</item>
    ///   <item>The attribute value is a simple literal (no markup extensions, no Razor),
    ///   and the property type is one <see cref="LiteralValueConverter"/> recognises.</item>
    /// </list>
    /// Falls back to <c>XamlBuilder.SetProperty</c> for everything else.
    /// </summary>
    private static void EmitValueAttribute(
        StringBuilder sb,
        string varName,
        JalxamlAstAttribute attr,
        INamedTypeSymbol? elementSymbol,
        EmitContext ctx,
        string pad,
        string? elementClrTypeName = null)
    {
        // Razor value-expression fast path. Detected forms (in attribute values):
        //   PropName="@Identifier"         — bind to DataContext path "Identifier"
        //   PropName="@(expr)"             — bind to evaluated expression with auto-detected deps
        //   PropName="@($.Self)"           — bind to self via $.Self
        //   PropName="@(#.DataModel)"      — bind to DataContext directly via #.X
        //   PropName="literal @prop tail"  — interpolated string with embedded Razor segments
        // Lowered to XamlBuilder.SetRazorBinding(target, "Prop", "expr", new[]{"dep1",...}).
        // Compile-time dependency analysis runs in RazorExpressionLowering — keeps runtime
        // away from the reflection-heavy RazorExpressionAnalyzer in the hot path.
        if (RazorExpressionLowering.TryLowerAttributeValue(attr.Value, out var razorExpr, out var razorDeps))
        {
            sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.SetRazorBinding({varName}, \"{attr.LocalName}\", {EscapeStringLiteral(razorExpr!)}, {RazorExpressionLowering.EmitDependencyArray(razorDeps!)}, __ctx);");
            return;
        }

        // First — markup-extension fast paths. The SG recognises the three simple
        // single-key forms used by every theme dictionary:
        //   {StaticResource Foo}  → XamlBuilder.SetStaticResource(target, "Prop", "Foo", __ctx)
        //   {ThemeResource Foo}   → XamlBuilder.SetDynamicResource(target, "Prop", "Foo", __ctx)
        //   {DynamicResource Foo} → XamlBuilder.SetDynamicResource(target, "Prop", "Foo", __ctx)
        //   {x:Null}              → null literal (only when the property type is a reference type)
        // Anything else (Binding, TemplateBinding, x:Static, x:Type, multi-arg extensions)
        // falls through to the runtime SetProperty path which keeps full markup-string
        // parsing semantics.
        //
        // EXCEPTION: <Setter Property="..." Value="{StaticResource X}"> needs *deferred*
        // resolution. The runtime XAML reader records the markup-extension string on the
        // Setter and re-evaluates it via PostProcessSetter / SetterResourceLookup when the
        // setter is applied to a target — by which point the owning dictionary has been
        // merged into the FE's resource lookup chain and X is reachable. The SG fast path
        // calls StaticResourceExtension.ProvideValue immediately (during the dictionary's
        // own construction), at which point the dict isn't merged yet and X is unreachable.
        // So we route Setter.Value through the runtime SetProperty(string) path
        // unconditionally, which invokes the same deferred-resolution machinery the
        // streaming parser used.
        // Recognise <Setter Value="..."> symbol-independently: the deferred-resolution
        // exclusion is behaviour-critical, so it must hold even when no SymbolTypeHelper
        // resolved the element symbol (the parser always resolves <Setter> to the CLR
        // type name via the curated table). Relying on the symbol alone would wrongly
        // eager-resolve a Setter.Value markup extension at construction time whenever
        // the symbol is unavailable.
        bool isSetterValueAttribute =
            string.Equals(attr.LocalName, "Value", StringComparison.Ordinal) &&
            (string.Equals(elementSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                           "global::Jalium.UI.Setter", StringComparison.Ordinal) ||
             string.Equals(elementClrTypeName, "Jalium.UI.Setter", StringComparison.Ordinal));

        if (!isSetterValueAttribute && TryMatchSimpleMarkupExtension(attr.Value, out var extKind, out var extKey))
        {
            switch (extKind)
            {
                case SimpleMarkupKind.StaticResource:
                    sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.SetStaticResource({varName}, \"{attr.LocalName}\", {EscapeStringLiteral(extKey!)}, __ctx);");
                    return;
                case SimpleMarkupKind.ThemeResource:
                case SimpleMarkupKind.DynamicResource:
                    sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.SetDynamicResource({varName}, \"{attr.LocalName}\", {EscapeStringLiteral(extKey!)}, __ctx);");
                    return;
                case SimpleMarkupKind.TemplateBinding:
                    sb.AppendLine($"{pad}global::Jalium.UI.Markup.XamlBuilder.SetTemplateBinding({varName}, \"{attr.LocalName}\", {EscapeStringLiteral(extKey!)}, __ctx);");
                    return;
                case SimpleMarkupKind.XNull:
                    sb.AppendLine($"{pad}{varName}.{attr.LocalName} = null!;");
                    return;
            }
        }

        // {Binding ...} — split the envelope at compile time and hand the structured
        // parts to the runtime trampoline, which rebuilds the BindingExtension through
        // the verbatim SetBindingParameter and applies it via the same path a
        // runtime-parsed binding uses (Converter / Source / RelativeSource / nested
        // markup all handled identically). Setter.Value stays on the runtime
        // SetProperty path — its markup-extension resolution is intentionally deferred
        // to setter-apply time, so it must NOT be evaluated here during construction.
        if (!isSetterValueAttribute &&
            BindingMarkupLowering.TryLower(attr.Value, out var bindPath, out var bindNames, out var bindValues))
        {
            var pathExpr = bindPath == null ? "null" : EscapeStringLiteral(bindPath);
            sb.AppendLine(
                $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetCompiledBinding({varName}, \"{attr.LocalName}\", {pathExpr}, {EmitStringArray(bindNames)}, {EmitStringArray(bindValues)}, __ctx);");
            return;
        }

        if (elementSymbol != null && ctx.Symbols != null)
        {
            var property = ctx.Symbols.ResolveProperty(elementSymbol, attr.LocalName);
            if (property != null && !property.IsReadOnly && property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public)
            {
                // Type-typed property + "prefix:LocalName" attribute value (typical:
                // <Style TargetType="controls:Foo">). Resolve the prefix at compile time
                // through XmlnsTypeResolver so we can emit a typed `typeof(...)` literal.
                // The runtime TypeTypeConverter cannot do this — it doesn't see the
                // document's xmlns declarations and only consults the simple-name
                // XamlTypeRegistry. Without this fast path, implicit-keyed Styles
                // (`<Style TargetType="controls:WelcomeTemplateCard">` with no x:Key)
                // get a null TargetType and AddChild silently drops them from the
                // ResourceDictionary, so the templates never apply.
                var propTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if ((propTypeName == "global::System.Type" || propTypeName == "System.Type") &&
                    !string.IsNullOrEmpty(attr.Value) &&
                    ctx.TryResolvePrefixedType(attr.Value, out var typeofName))
                {
                    sb.AppendLine($"{pad}{varName}.{attr.LocalName} = typeof({typeofName});");
                    return;
                }

                var converted = LiteralValueConverter.TryConvert(attr.Value, property.Type);
                if (converted != null)
                {
                    // Direct setter on the property — strongly typed, no SetProperty round trip.
                    sb.AppendLine($"{pad}{varName}.{attr.LocalName} = {converted};");
                    return;
                }
            }
        }

        // Runtime SetProperty path. The runtime may swap the instance for a replacement
        // (e.g. <c>ResourceDictionary.Source</c> triggers external loading and the loaded
        // dictionary replaces the empty stub). Reassign the local variable so subsequent
        // AddChild / ApplyPropertyElementChild calls see the post-load instance. Casting
        // to the element's static type is safe — SetProperty's contract is to return
        // either the same instance or a replacement of the same declared type.
        var castType = elementSymbol != null
            ? SymbolTypeHelper.ToGlobalName(elementSymbol)
            : "global::System.Object";
        sb.AppendLine($"{pad}{varName} = ({castType})global::Jalium.UI.Markup.XamlBuilder.SetProperty({varName}, \"{attr.LocalName}\", {EscapeStringLiteral(attr.Value)}, __ctx);");
    }

    private enum SimpleMarkupKind
    {
        None,
        StaticResource,
        ThemeResource,
        DynamicResource,
        TemplateBinding,
        XNull,
    }

    /// <summary>
    /// Match the three simplest jalxaml markup-extension forms:
    /// <c>{StaticResource X}</c> / <c>{ThemeResource X}</c> / <c>{DynamicResource X}</c>
    /// (single positional key, no commas, no nested extensions) and the literal-null
    /// extension <c>{x:Null}</c>. Returns false for anything more elaborate so the SG falls
    /// through to the runtime markup-extension parser.
    /// </summary>
    private static bool TryMatchSimpleMarkupExtension(string raw, out SimpleMarkupKind kind, out string? key)
    {
        kind = SimpleMarkupKind.None;
        key = null;
        if (raw == null || raw.Length < 4)
            return false;

        var trimmed = raw.Trim();
        if (trimmed.Length < 4 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            return false;

        // {} prefix is an "escape literal brace" sequence — not a markup extension.
        if (trimmed.StartsWith("{}", StringComparison.Ordinal))
            return false;

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (string.Equals(inner, "x:Null", StringComparison.Ordinal))
        {
            kind = SimpleMarkupKind.XNull;
            return true;
        }

        // Reject anything containing nested braces or commas — they signal multi-arg / complex markup.
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '{' || inner[i] == '}' || inner[i] == ',' || inner[i] == '=')
                return false;
        }

        // Find first whitespace separator between the extension name and the key.
        var spaceIdx = -1;
        for (var i = 0; i < inner.Length; i++)
        {
            if (char.IsWhiteSpace(inner[i]))
            {
                spaceIdx = i;
                break;
            }
        }
        if (spaceIdx <= 0)
            return false;

        var name = inner.Substring(0, spaceIdx).Trim();
        var rest = inner.Substring(spaceIdx + 1).Trim();
        if (rest.Length == 0)
            return false;

        // The "key" must be a single token — anything with whitespace is multi-arg.
        for (var i = 0; i < rest.Length; i++)
        {
            if (char.IsWhiteSpace(rest[i]))
                return false;
        }

        switch (name)
        {
            case "StaticResource":
                kind = SimpleMarkupKind.StaticResource;
                key = rest;
                return true;
            case "ThemeResource":
                kind = SimpleMarkupKind.ThemeResource;
                key = rest;
                return true;
            case "DynamicResource":
                kind = SimpleMarkupKind.DynamicResource;
                key = rest;
                return true;
            case "TemplateBinding":
                kind = SimpleMarkupKind.TemplateBinding;
                key = rest;
                return true;
            default:
                return false;
        }
    }

    private static void EmitAttached(StringBuilder sb, string targetVar, JalxamlAstAttribute attr, string pad)
    {
        var owner = attr.AttachedOwner ?? string.Empty;
        var prop = attr.LocalName;
        var value = attr.Value;

        // Fast paths: known framework attached properties get parsed at compile time and
        // call the strongly-typed setter directly (zero reflection).
        var fastPath = TryEmitAttachedFastPath(owner, prop, value, targetVar, pad);
        if (fastPath != null)
        {
            sb.Append(fastPath);
            return;
        }

        // Generic path: forward to the runtime which resolves the owner type and converter.
        sb.AppendLine(
            $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetAttachedProperty({targetVar}, \"{owner}\", \"{prop}\", {EscapeStringLiteral(value)}, __ctx, {EscapeStringLiteral(attr.NamespaceUri ?? string.Empty)});");
    }

    private static string? TryEmitAttachedFastPath(string owner, string prop, string value, string targetVar, string pad)
    {
        // Only attempt fast paths for integer / double / Dock literals. If the value is a
        // markup extension or a non-literal, fall through to the runtime path.
        bool IsIntLiteral(out int parsed) => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed);
        bool IsDoubleLiteral(out double parsed) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed);

        if (string.Equals(owner, "Grid", StringComparison.Ordinal))
        {
            switch (prop)
            {
                case "Row" when IsIntLiteral(out var r):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetGridRow((global::Jalium.UI.UIElement){targetVar}, {r});\n";
                case "Column" when IsIntLiteral(out var c):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetGridColumn((global::Jalium.UI.UIElement){targetVar}, {c});\n";
                case "RowSpan" when IsIntLiteral(out var rs):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetGridRowSpan((global::Jalium.UI.UIElement){targetVar}, {rs});\n";
                case "ColumnSpan" when IsIntLiteral(out var cs):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetGridColumnSpan((global::Jalium.UI.UIElement){targetVar}, {cs});\n";
            }
        }
        else if (string.Equals(owner, "Canvas", StringComparison.Ordinal))
        {
            switch (prop)
            {
                case "Left" when IsDoubleLiteral(out var l):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetCanvasLeft((global::Jalium.UI.UIElement){targetVar}, {FormatDouble(l)});\n";
                case "Top" when IsDoubleLiteral(out var t):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetCanvasTop((global::Jalium.UI.UIElement){targetVar}, {FormatDouble(t)});\n";
                case "Right" when IsDoubleLiteral(out var rr):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetCanvasRight((global::Jalium.UI.UIElement){targetVar}, {FormatDouble(rr)});\n";
                case "Bottom" when IsDoubleLiteral(out var bb):
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetCanvasBottom((global::Jalium.UI.UIElement){targetVar}, {FormatDouble(bb)});\n";
            }
        }
        else if (string.Equals(owner, "Panel", StringComparison.Ordinal) ||
                 string.Equals(owner, "Canvas", StringComparison.Ordinal))
        {
            if (prop == "ZIndex" && IsIntLiteral(out var z))
            {
                return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetPanelZIndex((global::Jalium.UI.UIElement){targetVar}, {z});\n";
            }
        }
        else if (string.Equals(owner, "DockPanel", StringComparison.Ordinal))
        {
            if (prop == "Dock")
            {
                // Try to resolve to the Dock enum at compile time. Fall through to runtime
                // setter if the literal isn't recognised. The fast path takes the integer
                // form of the enum so XamlBuilder (in Jalium.UI.Core) does not need to
                // reference Jalium.UI.Controls.Dock; cast back happens inside Controls.
                var enumExpr = TryParseDockEnum(value);
                if (enumExpr != null)
                {
                    return $"{pad}global::Jalium.UI.Markup.XamlBuilder.SetDockPanelDock((global::Jalium.UI.UIElement){targetVar}, (int)({enumExpr}));\n";
                }
            }
        }
        return null;
    }

    private static string? TryParseDockEnum(string value)
    {
        switch (value)
        {
            case "Left": return "global::Jalium.UI.Controls.Dock.Left";
            case "Top": return "global::Jalium.UI.Controls.Dock.Top";
            case "Right": return "global::Jalium.UI.Controls.Dock.Right";
            case "Bottom": return "global::Jalium.UI.Controls.Dock.Bottom";
            default: return null;
        }
    }

    private static void EmitPropertyElement(
        StringBuilder sb,
        string parentVar,
        JalxamlAstNode parentNode,
        JalxamlAstPropertyElement pe,
        IndexCounter counter,
        HashSet<string> namedAlready,
        int indent,
        EmitContext ctx)
    {
        var pad = new string(' ', indent);

        // Each property-element child gets emitted as its own scoped block: construct the
        // child, populate it, then route to ApplyPropertyElementChild on the parent. The
        // runtime decides between collection-add / dictionary-insert / single-value set
        // based on the parent's declared property type.
        for (var i = 0; i < pe.Children.Count; i++)
        {
            var child = pe.Children[i];
            var resourceKey = ExtractResourceKey(child);
            EmitChildElement(
                sb,
                parentVar,
                child,
                counter,
                namedAlready,
                indent,
                ctx,
                asPropertyElementChild: new PropertyElementTarget(pe.PropertyName, resourceKey));
        }
    }

    /// <summary>
    /// Extract the explicit x:Key (if any) from a node so the runtime ResourceDictionary
    /// can use it as the entry key. Returns null when the element has no x:Key (the runtime
    /// falls back to Style.TargetType for keyed-style entries).
    /// </summary>
    private static string? ExtractResourceKey(JalxamlAstNode child)
    {
        foreach (var attr in child.Attributes)
        {
            if (attr.Kind == JalxamlAttributeKind.XDirective &&
                string.Equals(attr.LocalName, "Key", StringComparison.Ordinal))
            {
                return attr.Value;
            }
        }
        return null;
    }

    private static void EmitChildElement(
        StringBuilder sb,
        string parentVar,
        JalxamlAstNode child,
        IndexCounter counter,
        HashSet<string> namedAlready,
        int indent,
        EmitContext ctx,
        PropertyElementTarget? asPropertyElementChild,
        JalxamlAstNode? parentNode = null)
    {
        // @section Name { body } — a definition, not in-place content. Emit a compiled
        // body-factory registration and no visual child (the runtime's
        // RazorSectionHost invokes the factory; no XAML string is re-parsed).
        if (child.LocalName == JalxamlParser.RazorSectionElementName)
        {
            EmitRazorSectionRegistration(sb, child, counter, indent, ctx);
            return;
        }

        var pad = new string(' ', indent);
        var childIndex = counter.Next();
        var childVar = $"__c{childIndex}";

        sb.AppendLine($"{pad}{{");
        var inner = indent + 4;
        var innerPad = new string(' ', inner);

        // Construct the child instance. Most element types use a default ctor; a few
        // XAML-language built-ins (notably <x:String>) cannot — System.String has no
        // public parameterless constructor, so we seed with string.Empty and let the
        // SetContentText path inside EmitElementBody overwrite the value via the
        // SetContentProperty pipeline (matching what the runtime XamlReader does).
        if (child.ResolvedClrTypeName == "System.String")
        {
            sb.AppendLine($"{innerPad}var {childVar} = string.Empty;");
        }
        else
        {
            sb.AppendLine($"{innerPad}var {childVar} = new global::{child.ResolvedClrTypeName!}();");
        }
        EmitElementBody(sb, child, childVar, counter, namedAlready, inner, ctx);

        if (asPropertyElementChild != null)
        {
            var propName = asPropertyElementChild.Value.PropertyName;
            var keyExpr = asPropertyElementChild.Value.ResourceKey != null
                ? EscapeStringLiteral(asPropertyElementChild.Value.ResourceKey!)
                : "null";
            sb.AppendLine(
                $"{innerPad}global::Jalium.UI.Markup.XamlBuilder.ApplyPropertyElementChild({parentVar}, \"{propName}\", {childVar}, __ctx, {keyExpr});");
        }
        else
        {
            EmitAddChildToParent(sb, parentVar, childVar, parentNode, child, ctx, innerPad);
        }

        // Lifted @if(cond) { ... }: the parser flattened the synthetic wrapper and stamped
        // this child with the combined condition. Bind its Visibility exactly as the
        // streaming parser's ShouldIncludeConditionalChild would have — same runtime
        // binding, no document re-parse. Emitted after the add so the element is parented
        // before the binding attaches (matching XamlReader's order).
        if (!string.IsNullOrEmpty(child.RazorIfCondition))
        {
            var ifDeps = RazorExpressionLowering.ExtractConditionDependencies(child.RazorIfCondition);
            sb.AppendLine(
                $"{innerPad}global::Jalium.UI.Markup.XamlBuilder.SetRazorIfVisibility({childVar}, {EscapeStringLiteral(child.RazorIfCondition!)}, {RazorExpressionLowering.EmitDependencyArray(ifDeps)}, __ctx);");
        }

        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// Emit the appropriate "add child to parent" call. When the parent's CLR type symbol
    /// is resolvable and matches one of the well-known framework parent types
    /// (<c>Panel</c>, <c>Border</c>, <c>ContentControl</c>, <c>ItemsControl</c>,
    /// <c>Window</c>), emit a strongly-typed direct property/collection access call.
    /// Otherwise fall back to <see cref="Jalium.UI.Markup.XamlBuilder.AddChild"/> which
    /// dispatches via reflection on <c>ContentPropertyAttribute</c>.
    ///
    /// <para>
    /// ResourceDictionary entries always go through the runtime <c>AddChild</c> path —
    /// that route honours the captured x:Key (or Style.TargetType implicit key) which
    /// SG-emit cannot encode at the call site without re-parsing the streaming-parser
    /// resource-key state machine.
    /// </para>
    /// </summary>
    private static void EmitAddChildToParent(
        StringBuilder sb,
        string parentVar,
        string childVar,
        JalxamlAstNode? parentNode,
        JalxamlAstNode childNode,
        EmitContext ctx,
        string innerPad)
    {
        var resourceKey = ExtractResourceKey(childNode);
        var keyExpr = resourceKey != null ? EscapeStringLiteral(resourceKey) : "null";

        // ResourceDictionary children — keep runtime AddChild for x:Key + Style.TargetType
        // implicit-key dispatch. The Style/Trigger fast paths are not worth duplicating
        // here.
        if (parentNode != null &&
            parentNode.ResolvedClrTypeName == "Jalium.UI.ResourceDictionary")
        {
            sb.AppendLine($"{innerPad}global::Jalium.UI.Markup.XamlBuilder.AddChild({parentVar}, {childVar}, __ctx, {keyExpr});");
            return;
        }

        if (parentNode?.ResolvedClrTypeName != null)
        {
            var parentSym = ctx.ResolveType(parentNode.ResolvedClrTypeName);
            if (parentSym != null)
            {
                // [ContentProperty] 优先：让 Panel 子类（SplitDockGroup / DockTabPanel
                // 等标 [ContentProperty("Items")]）的子元素正确进入自定义集合，而不是
                // 沿继承链兜底到 Panel.Children.Add 后丢失自定义集合归属。
                // [ContentProperty] 的 Inherited=true，子类标注覆盖父类——所以 Panel
                // 子类会拿到自己最具体的标注。没标的 Panel 子类继承 Panel 的
                // [ContentProperty("Children")]，emit 等价旧的 Children.Add，行为不变。
                var contentPropertyName = ResolveContentPropertyName(parentSym);
                if (contentPropertyName != null && ctx.Symbols != null)
                {
                    var contentProperty = ctx.Symbols.ResolveProperty(parentSym, contentPropertyName);
                    if (contentProperty != null)
                    {
                        if (IsCollectionPropertyType(contentProperty.Type))
                        {
                            sb.AppendLine($"{innerPad}{parentVar}.{contentPropertyName}.Add({childVar});");
                        }
                        else
                        {
                            sb.AppendLine($"{innerPad}{parentVar}.{contentPropertyName} = {childVar};");
                        }
                        return;
                    }
                }

                // Fallback: 没标 [ContentProperty] 的容器，沿继承链匹配框架已知类型。
                for (var t = parentSym; t != null; t = t.BaseType)
                {
                    var fn = SymbolTypeHelper.ToGlobalName(t);
                    switch (fn)
                    {
                        case "global::Jalium.UI.Controls.Panel":
                            sb.AppendLine($"{innerPad}{parentVar}.Children.Add({childVar});");
                            return;
                        case "global::Jalium.UI.Controls.Border":
                            sb.AppendLine($"{innerPad}{parentVar}.Child = {childVar};");
                            return;
                        case "global::Jalium.UI.Controls.ItemsControl":
                            // ItemsControl wins over its ContentControl base — Items collection
                            // is the active container for child elements.
                            sb.AppendLine($"{innerPad}{parentVar}.Items.Add({childVar});");
                            return;
                        case "global::Jalium.UI.Controls.ContentControl":
                            sb.AppendLine($"{innerPad}{parentVar}.Content = {childVar};");
                            return;
                        case "global::Jalium.UI.Controls.Window":
                            // Window inherits from ContentControl in Jalium.UI but may sit
                            // under different roots in custom assemblies; explicit case keeps
                            // the dispatch order honest if a third-party Window forks the
                            // hierarchy.
                            sb.AppendLine($"{innerPad}{parentVar}.Content = {childVar};");
                            return;
                    }
                }
            }
        }

        // Fallback: runtime AddChild walks ContentPropertyAttribute on the parent type.
        sb.AppendLine($"{innerPad}global::Jalium.UI.Markup.XamlBuilder.AddChild({parentVar}, {childVar}, __ctx, {keyExpr});");
    }

    /// <summary>
    /// 沿继承链找 [ContentProperty] —— 子类标注覆盖父类（[ContentPropertyAttribute] 标
    /// Inherited=true，但 GetAttributes 只返回直接标在该类型上的；所以手动 walk 继承链取
    /// 最具体的 declaring type 的标注）。
    /// </summary>
    private static string? ResolveContentPropertyName(ITypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var attr in current.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (attrName == "Jalium.UI.ContentPropertyAttribute" || attr.AttributeClass?.Name == "ContentPropertyAttribute")
                {
                    if (attr.ConstructorArguments.Length > 0
                        && attr.ConstructorArguments[0].Value is string name
                        && !string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 判断属性类型是否能用 list.Add(child) 模式（实现 IList / IList&lt;T&gt; / 是
    /// ObservableCollection&lt;T&gt; / Collection&lt;T&gt; 等）。
    /// </summary>
    private static bool IsCollectionPropertyType(ITypeSymbol propertyType)
    {
        // 直接接口和所有继承到的接口都看一遍；XAML 里 IList 是最常见的 fast path。
        foreach (var iface in propertyType.AllInterfaces)
        {
            var def = iface.OriginalDefinition.ToDisplayString();
            if (def == "System.Collections.IList") return true;
            if (def == "System.Collections.Generic.IList<T>") return true;
            if (def == "System.Collections.Generic.ICollection<T>") return true;
        }
        return false;
    }

    private static string FormatDouble(double value)
    {
        // Round-trip format with invariant culture so the resulting C# literal compiles
        // regardless of system locale.
        return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d";
    }

    /// <summary>
    /// Escape a string into a C# verbatim or normal literal. Defaults to a quoted normal
    /// literal escaping <c>\</c>, <c>"</c>, control characters and Unicode escapes when
    /// necessary. We always emit normal literals (not <c>@"..."</c>) so single-line values
    /// remain readable in the generated source.
    /// </summary>
    private static string EscapeStringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Emit a C# <c>string[]</c> literal (each element via <see cref="EscapeStringLiteral"/>),
    /// or <c>global::System.Array.Empty&lt;string&gt;()</c> for an empty array to avoid an
    /// allocation. Used for the SG-split <c>{Binding}</c> name/value pairs.
    /// </summary>
    private static string EmitStringArray(string[] items)
    {
        if (items.Length == 0)
            return "global::System.Array.Empty<string>()";
        var sb = new StringBuilder();
        sb.Append("new string[] { ");
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(EscapeStringLiteral(items[i]));
        }
        sb.Append(" }");
        return sb.ToString();
    }

    private sealed class IndexCounter
    {
        private int _next;
        public int Next() => _next++;
    }

    private readonly struct PropertyElementTarget
    {
        public PropertyElementTarget(string propertyName, string? resourceKey)
        {
            PropertyName = propertyName;
            ResourceKey = resourceKey;
        }
        public string PropertyName { get; }
        public string? ResourceKey { get; }
    }
}
