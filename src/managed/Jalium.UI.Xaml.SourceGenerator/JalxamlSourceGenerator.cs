using System.Collections.Immutable;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Source generator that generates InitializeComponent methods for JALXAML files.
/// Generates code that loads from embedded JALXAML resources at runtime.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class JalxamlSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get all .jalxaml files
        var jalxamlFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".jalxaml", StringComparison.OrdinalIgnoreCase));

        // Combine compilation + global options + jalxaml files. Global options carry
        // RootNamespace / MSBuildProjectDirectory which the SG needs to compute the
        // canonical manifest resource name for prebuilt-dictionary registration.
        var combined = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(jalxamlFiles.Collect());

        context.RegisterSourceOutput(combined, (spc, source) =>
        {
            var ((compilation, configOptions), jalxamlFileList) = source;
            var assemblyName = compilation.AssemblyName ?? "Unknown";
            var xmlnsResolver = XmlnsTypeResolver.FromCompilation(compilation);
            var symbols = new SymbolTypeHelper(compilation);

            // Pull project metadata up-front so each per-file pass does not re-parse the
            // global option set. Both fall back gracefully when not exposed (CompilerVisible
            // properties absent), but a missing ProjectDirectory means we cannot register
            // prebuilt dictionaries (the runtime resource name is unknowable).
            configOptions.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projectDir);
            configOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNs);
            if (string.IsNullOrEmpty(rootNs))
                rootNs = assemblyName;

            foreach (var file in jalxamlFileList)
            {
                // Look up the file-level metadata that PrepareJalxamlForSourceGenerator put
                // on the AdditionalFiles item. When present, this overrides the path-based
                // heuristic — it carries the project-relative jalxaml path before the Razor
                // transform rewrote the filesystem location into the intermediate dir.
                var fileOptions = configOptions.GetOptions(file);
                fileOptions.TryGetValue("build_metadata.AdditionalFiles.JalxamlSourceRelativePath", out var jalxamlSourceRelativePath);
                GenerateForJalxaml(spc, file, assemblyName, xmlnsResolver, symbols, projectDir, rootNs!, jalxamlSourceRelativePath);
            }
        });
    }

    private void GenerateForJalxaml(
        SourceProductionContext context,
        AdditionalText file,
        string assemblyName,
        XmlnsTypeResolver xmlnsResolver,
        SymbolTypeHelper symbols,
        string? projectDir,
        string rootNs,
        string? jalxamlSourceRelativePath)
    {
        var content = file.GetText(context.CancellationToken)?.ToString();
        if (string.IsNullOrEmpty(content))
            return;

        try
        {
            var parseResult = JalxamlParser.Parse(content!, file.Path);
            if (parseResult == null)
                return;

            // The parser uses a hard-coded type-mapping table that only covers the framework's
            // common controls. Anything outside that table (Color / TitleBarButton / user-defined
            // types) lands with ResolvedClrTypeName == null. Now that the runtime jalxaml parser
            // is gone we must resolve every element to a CLR type — walk the AST and ask the
            // Roslyn-backed XmlnsTypeResolver for everything the static table missed.
            if (parseResult.Root != null)
                AugmentResolvedTypeNames(parseResult.Root, xmlnsResolver);

            // Path A: x:Class set → the document is a code-behind component (Window /
            // UserControl / Application / etc.). Generate the partial class as before.
            if (!string.IsNullOrEmpty(parseResult.ClassName))
            {
                var className = parseResult.ClassName!;
                var generatedCode = GenerateCode(context, parseResult, assemblyName, xmlnsResolver, symbols, content!, file.Path, projectDir, rootNs, jalxamlSourceRelativePath);
                if (generatedCode == null)
                    return; // diagnostic already reported by GenerateCode
                var fileName = $"{className.Replace(".", "_")}.g.cs";
                context.AddSource(fileName, SourceText.From(generatedCode, Encoding.UTF8));
                return;
            }

            // Path B: no x:Class but root element is a ResourceDictionary → emit a
            // prebuilt-dictionary builder. This lets the runtime ThemeLoader skip the
            // embedded-resource + XML reader path for theme dictionaries / standalone
            // resource files. We can only register the builder if the project directory
            // is available (we need it to compute the manifest resource name); when the
            // metadata is missing we silently fall through and the runtime keeps loading
            // from the embedded resource.
            if (IsResourceDictionaryRoot(parseResult))
            {
                // Manifest-name resolution priority:
                //   1. JalxamlSourceRelativePath metadata (PrepareJalxamlForSourceGenerator
                //      stamped this with the project-relative path BEFORE the Razor
                //      transform copied the file into obj/.../Razor/...). Without this
                //      we'd compute a useless name like "obj.Debug.net10.0.Jalxaml.Razor.Themes.AppTheme.jalxaml"
                //      which the runtime ThemeLoader will never query.
                //   2. file.Path − projectDir as a last-resort heuristic for projects
                //      that haven't imported Jalium.UI.Build.targets.
                string? manifestName = null;
                if (!string.IsNullOrEmpty(jalxamlSourceRelativePath))
                {
                    var relSlash = jalxamlSourceRelativePath!.Replace('\\', '/').TrimStart('/');
                    var dotted = relSlash.Replace('/', '.');
                    manifestName = string.IsNullOrEmpty(rootNs) ? dotted : $"{rootNs}.{dotted}";
                }
                else if (!string.IsNullOrEmpty(projectDir))
                {
                    manifestName = ComputeManifestResourceName(file.Path, projectDir!, rootNs);
                }

                if (string.IsNullOrEmpty(manifestName))
                {
                    // Without either the file metadata or the project directory we cannot
                    // compute the canonical manifest name the runtime registry uses for
                    // dictionary lookup. Surface a diagnostic so misconfigured projects
                    // don't silently produce unloadable dictionaries.
                    ReportMissingProjectDir(context, file.Path);
                    return;
                }

                var builderCode = GeneratePrebuiltDictionaryCode(context, parseResult, manifestName!, file.Path, symbols, xmlnsResolver, rootNs);
                if (builderCode == null)
                    return; // diagnostic already reported

                var safeName = SanitizeIdentifier(manifestName!);
                var fileName = $"_GeneratedDict_{safeName}.g.cs";
                context.AddSource(fileName, SourceText.From(builderCode, Encoding.UTF8));
            }
        }
        catch (Exception ex)
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "JALXAML001",
                    "JALXAML Parse Error",
                    "Failed to parse JALXAML file '{0}': {1}",
                    "Jalium.UI.Xaml",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true),
                Location.None,
                file.Path,
                ex.Message);
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Walk the AST and patch every <see cref="JalxamlAstNode.ResolvedClrTypeName"/> the
    /// streaming parser left null. The parser's static mapping table covers the curated
    /// framework set; anything else (user controls, Color, TitleBarButton, third-party
    /// types) falls through to here. Resolution goes through <see cref="XmlnsTypeResolver"/>
    /// which honours every <c>XmlnsDefinitionAttribute</c> in the compilation, so the SG
    /// can lower documents that reference any reachable CLR type without touching the
    /// hand-maintained table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We strip the <c>global::</c> prefix the resolver returns — <see cref="JalxamlAstNode.ResolvedClrTypeName"/>
    /// stores the bare metadata name (e.g. <c>Jalium.UI.Media.Color</c>) and the codegen
    /// adds <c>global::</c> at emit time. Returning the prefixed form would break
    /// <c>HasIllegalTemplateShape</c>'s string-equality checks for the template type set
    /// (<c>Jalium.UI.ControlTemplate</c> etc.).
    /// </para>
    /// </remarks>
    private static void AugmentResolvedTypeNames(JalxamlAstNode node, XmlnsTypeResolver resolver)
    {
        if (string.IsNullOrEmpty(node.ResolvedClrTypeName) && !string.IsNullOrEmpty(node.LocalName))
        {
            var resolved = resolver.ResolveToGlobalQualifiedName(node.LocalName, node.NamespaceUri);
            if (!string.IsNullOrEmpty(resolved))
            {
                // Strip the leading "global::" — node CLR type names are bare elsewhere.
                node.ResolvedClrTypeName = resolved!.StartsWith("global::", StringComparison.Ordinal)
                    ? resolved!.Substring("global::".Length)
                    : resolved!;
            }
        }
        foreach (var child in node.Children)
            AugmentResolvedTypeNames(child, resolver);
        foreach (var pe in node.PropertyElements)
        {
            foreach (var child in pe.Children)
                AugmentResolvedTypeNames(child, resolver);
        }
    }

    /// <summary>
    /// True when the parsed document's root element resolves to <c>Jalium.UI.ResourceDictionary</c>.
    /// We rely on the SG-side type-mapping table (which already knows the framework
    /// ResourceDictionary type) so this lookup costs nothing extra.
    /// </summary>
    private static bool IsResourceDictionaryRoot(JalxamlParseResult result)
    {
        if (result.Root == null)
            return false;
        return string.Equals(result.Root.ResolvedClrTypeName, "Jalium.UI.ResourceDictionary", StringComparison.Ordinal);
    }

    /// <summary>
    /// Compute the canonical manifest resource name the .NET SDK assigns to a jalxaml
    /// embedded resource, given the project directory and root namespace. The SDK rule
    /// is: <c>{RootNamespace}.{relativePath with separators replaced by '.'}</c>. This
    /// must match what the runtime sees in <c>Assembly.GetManifestResourceStream</c> so
    /// the prebuilt-dictionary registry lookup wins over the embedded fallback.
    /// </summary>
    private static string? ComputeManifestResourceName(string filePath, string projectDir, string rootNs)
    {
        var normalizedFile = filePath.Replace('\\', '/');
        var normalizedRoot = projectDir.Replace('\\', '/').TrimEnd('/');
        if (normalizedRoot.Length == 0)
            return null;

        if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            // File lives outside the project directory — it might be a Link / linked file
            // we cannot map to a manifest name without additional metadata. Skip.
            return null;
        }

        var relative = normalizedFile.Substring(normalizedRoot.Length).TrimStart('/');
        if (string.IsNullOrEmpty(relative))
            return null;

        // The SDK replaces path segments with '.' but keeps the file's extension. The dot
        // before the extension is preserved by the same rule (the algorithm operates on
        // path separators only, the file name dots stay).
        var dotted = relative.Replace('/', '.');
        return string.IsNullOrEmpty(rootNs) ? dotted : $"{rootNs}.{dotted}";
    }

    /// <summary>
    /// Replace characters in <paramref name="raw"/> that are illegal in a C# identifier
    /// with <c>_</c>, so the SG can use the manifest name as a class/file suffix without
    /// emitting invalid syntax.
    /// </summary>
    private static string SanitizeIdentifier(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }
        return sb.ToString();
    }

    private string? GeneratePrebuiltDictionaryCode(
        SourceProductionContext context,
        JalxamlParseResult result,
        string manifestName,
        string filePath,
        SymbolTypeHelper symbols,
        XmlnsTypeResolver xmlnsResolver,
        string rootNs)
    {
        // The dictionary codegen MUST succeed — runtime jalxaml parsing is gone, there is
        // no embedded-resource fallback to land on. When TryEmitDictionaryBuildBody returns
        // null we surface a diagnostic so the developer can fix the offending element
        // (typically an unresolved CLR type or an x:Class attribute on a node we don't yet
        // pin to a typed builder).
        var buildBody = JalxamlCodeGenerator.TryEmitDictionaryBuildBody(result, symbols, xmlnsResolver);
        if (buildBody == null)
        {
            ReportCodegenBail(context, filePath, result);
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var generatedNs = $"{rootNs}.__JalxamlGenerated";
        var className = $"_Dict_{SanitizeIdentifier(manifestName)}";

        sb.AppendLine($"namespace {generatedNs};");
        sb.AppendLine();
        sb.AppendLine($"internal static class {className}");
        sb.AppendLine("{");

        // Pin every referenced element type so the trimmer keeps their constructors.
        // For prebuilt-dictionary builders we cannot call XamlTypeRegistry.RegisterType<T>()
        // here — that lives in Jalium.UI.Xaml, and Jalium.UI.Controls (which produces the
        // theme dictionaries) does not reference Jalium.UI.Xaml. A bare `typeof(T)` is
        // enough for the trimmer / NativeAOT root logic; the runtime never needs to resolve
        // these types by simple name because the SG already baked the construction expressions
        // directly into Build().
        var emittedTypes = new HashSet<string>(StringComparer.Ordinal);
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Register()");
        sb.AppendLine("    {");
        sb.AppendLine($"        global::Jalium.UI.Markup.XamlPrebuiltDictionaryRegistry.Register({EscapeStringLiteralLocal(manifestName)}, Build);");
        foreach (var element in result.ReferencedElements)
        {
            var resolvedFullName = xmlnsResolver.ResolveToGlobalQualifiedName(element.ElementName, element.NamespaceUri);
            if (resolvedFullName == null)
                continue;
            if (!emittedTypes.Add(resolvedFullName))
                continue;
            sb.AppendLine($"        _ = typeof({resolvedFullName});");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // The Build method runs against a caller-supplied empty ResourceDictionary instance
        // so the BuildContext.CodeBehindInstance stays null — no x:Class to wire up.
        sb.AppendLine("    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Trimming\", \"IL2026\", Justification = \"All element types are pinned via XamlTypeRegistry above.\")]");
        sb.AppendLine("    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AOT\", \"IL3050\", Justification = \"Markup-extension ProvideValue uses dynamic code only for x:Array.\")]");
        sb.AppendLine("    internal static void Build(global::Jalium.UI.ResourceDictionary __target, global::Jalium.UI.Markup.XamlBuildContext __ctx)");
        sb.AppendLine("    {");
        sb.Append(buildBody);
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapeStringLiteralLocal(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                default: sb.Append(ch); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private string? GenerateCode(SourceProductionContext context, JalxamlParseResult result, string assemblyName, XmlnsTypeResolver xmlnsResolver, SymbolTypeHelper symbols, string sourceContent, string filePath, string? projectDir, string rootNs, string? jalxamlSourceRelativePath)
    {
        var sb = new StringBuilder();

        // Compile-time codegen is the preferred path. When the codegen returns null the
        // failure mode splits in two:
        //   1. Document contains Razor directives the SG cannot lower (e.g.
        //      `@section` / `@RenderSection` / runtime `@if`). The framework still ships
        //      a runtime parser (XamlReader.LoadComponentFromString) that handles these
        //      end-to-end, so we emit a thin wrapper that hands the inlined source text
        //      to the runtime — no JALXAML002 diagnostic, no developer churn.
        //   2. Any other rejection (unresolved CLR type, illegal template shape, etc.) —
        //      surface JALXAML002 so the developer can fix the document. Runtime
        //      fallback would still throw at parse time, just later.
        var initializeBody = JalxamlCodeGenerator.TryEmitInitializeBody(result, symbols, xmlnsResolver);
        var useRuntimeFallback = false;
        if (initializeBody == null)
        {
            if (CanUseRuntimeFallback(result))
            {
                useRuntimeFallback = true;
            }
            else
            {
                ReportCodegenBail(context, filePath, result);
                return null;
            }
        }

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0649 // Field is never assigned - wired up by XAML loader at runtime");
        sb.AppendLine();
        sb.AppendLine("using Jalium.UI.Controls;");
        sb.AppendLine("using Jalium.UI.Controls.Primitives;");
        sb.AppendLine("using Jalium.UI.Markup;");
        sb.AppendLine();

        // Extract namespace and class name
        var lastDot = result.ClassName!.LastIndexOf('.');
        var namespaceName = lastDot > 0 ? result.ClassName.Substring(0, lastDot) : null;
        var className = lastDot > 0 ? result.ClassName.Substring(lastDot + 1) : result.ClassName;

        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        sb.AppendLine($"partial class {className}");
        sb.AppendLine("{");

        // AOT 保根:StartupUri 等按 x:Class 字符串查类型的路径,在 AOT trim 之后会被 Assembly.GetType
        // 判空。ModuleInitializer 在模块 cctor 里一次性把 typeof(T) 写入 XamlTypeRegistry —
        // typeof 引用让 linker 留住类型,注册表让 ThemeLoader 不依赖 Assembly.GetType。
        //
        // 同样的 pin 必须覆盖 jalxaml 文档里的每一个元素类型,否则 trimmer 会把 XAML 解析时
        // Activator.CreateInstance 需要的构造函数裁掉(典型现象:运行时抛 MissingMethodException
        // "No parameterless constructor defined for type '...'.")。SourceGenerator 在编译时
        // 通过 XmlnsDefinition + 兼容映射把 element 名解析为完整 INamedTypeSymbol,然后 emit
        // typeof(...) 引用 + RegisterType<T>() 注册,二者合起来既保留类型也加速运行时查找。
        //
        // 方法名按类名后缀区分,避免同一 module 内重名冲突。
        //
        // [DynamicDependency] 告知 IL trimmer:此 ModuleInitializer 一旦保留(它带 [ModuleInitializer]
        // 必然 reachable),就要连带保住 codebehind 类型的所有 instance 方法元数据。这是修 jalxaml
        // event-handler 在 AOT 下"Click not found"的根因 — XamlReader.TryWireEvent 走 reflection
        // GetMethod(handlerName, Public|NonPublic) 查 click handler,trimmer 默认会把 private 方法
        // 元数据 prune 掉 (typeof 引用只 pin 类型本身,不保护方法)。DynamicDependency 是 .NET 标准
        // trimmer hint,无需新 API/registry,直接让所有 click/keydown/lostfocus 等签名各异的 event
        // 一次性可达。
        var moduleInitMethodName = $"__JalxamlRegisterStartupType_{className}";
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine($"    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods, typeof({className}))]");

        // DataType="vm:Foo" 标注的 view-model 类型 — emit DynamicDependency(PublicProperties|
        // PublicFields) 防止 trim 删除属性 getter。Binding 引擎走反射 fallback 时,
        // PropertyInfo.GetValue 必须能找到 getter,否则抛 "Property Get method was not found"
        // (release/publish trim 默认行为,debug 不触发)。
        // De-duplicate against ReferencedElements set: control 元素已经按 PublicMethods
        // pin 过的类型不需要再 emit 一次 — 但 PublicProperties 是 ReferencedElements 没要求的
        // member kind,所以这里独立 emit,即使类型重复也无害(linker 合并)。
        var dataTypeFullNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dataType in result.DataTypeReferences)
        {
            var resolvedFullName = xmlnsResolver.ResolveToGlobalQualifiedName(dataType.ElementName, dataType.NamespaceUri);
            if (resolvedFullName == null)
                continue;

            if (!dataTypeFullNames.Add(resolvedFullName))
                continue;

            sb.AppendLine($"    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields, typeof({resolvedFullName}))]");
        }

        sb.AppendLine($"    internal static void {moduleInitMethodName}()");
        sb.AppendLine("    {");
        sb.AppendLine($"        global::Jalium.UI.Markup.XamlTypeRegistry.RegisterStartupType(\"{result.ClassName}\", typeof({className}));");

        // Register every plausible StartupUri spelling against this type so
        // Application.StartupUri="..." resolves at runtime without consulting any manifest
        // resource. The set covers the four shapes a developer might write:
        //   "Views/Foo.jalxaml"           — slash-separated, project-relative
        //   "Views.Foo.jalxaml"           — dot-separated, project-relative
        //   "{RootNs}.Views/Foo.jalxaml"  — root-namespace prefix + slashes
        //   "{RootNs}.Views.Foo.jalxaml"  — manifest-resource-name canonical form
        // Comparison is case-insensitive at lookup time so we don't need to emit case
        // variants. Skipped when the file lives outside the project directory (linked
        // / generated files) — startup XAML cannot live there.
        // Resolve the project-relative source path. Preferred source is the
        // JalxamlSourceRelativePath metadata that PrepareJalxamlForSourceGenerator emits —
        // it survives the Razor transform that rewrites the file's filesystem location
        // into the intermediate output directory. Falling back to (filePath - projectDir)
        // catches projects that haven't imported Jalium.UI.Build.targets but still wire up
        // AdditionalFiles directly.
        string? relativeSlash = null;
        if (!string.IsNullOrEmpty(jalxamlSourceRelativePath))
        {
            relativeSlash = jalxamlSourceRelativePath!.Replace('\\', '/').TrimStart('/');
        }
        else if (!string.IsNullOrEmpty(projectDir))
        {
            var normalizedFile = filePath.Replace('\\', '/');
            var normalizedRoot = projectDir!.Replace('\\', '/').TrimEnd('/');
            if (normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                relativeSlash = normalizedFile.Substring(normalizedRoot.Length).TrimStart('/');
            }
        }

        if (!string.IsNullOrEmpty(relativeSlash))
        {
            var relativeDot = relativeSlash!.Replace('/', '.');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var spelling in new[]
            {
                relativeSlash!,
                relativeDot,
                $"{rootNs}.{relativeSlash}",
                $"{rootNs}.{relativeDot}",
            })
            {
                if (string.IsNullOrEmpty(spelling) || !seen.Add(spelling))
                    continue;
                sb.AppendLine($"        global::Jalium.UI.Markup.XamlTypeRegistry.RegisterStartupUri({EscapeStringLiteralLocal(spelling)}, typeof({className}));");
            }

            // ResourceDictionary subclasses (x:Class on a <ResourceDictionary> root) are also
            // referenced via Source URIs from other dictionaries (App.jalxaml's
            // <ResourceDictionary Source="Themes/AppTheme.jalxaml" />). Register them with the
            // prebuilt-dictionary factory registry so ThemeLoader.TryBuildFromPrebuiltRegistry
            // returns the codebehind-typed instance directly — no embedded-resource lookup
            // and no XML reader. The factory just `new T()`'s the type; the SG-generated
            // ctor body runs InitializeComponent and populates the dictionary.
            if (IsResourceDictionaryRoot(result))
            {
                var factorySeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var spelling in new[]
                {
                    relativeSlash!,
                    relativeDot,
                    $"{rootNs}.{relativeSlash}",
                    $"{rootNs}.{relativeDot}",
                })
                {
                    if (string.IsNullOrEmpty(spelling) || !factorySeen.Add(spelling))
                        continue;
                    sb.AppendLine($"        global::Jalium.UI.Markup.XamlPrebuiltDictionaryRegistry.RegisterFactory({EscapeStringLiteralLocal(spelling)}, static () => new {className}());");
                }
            }
        }

        // De-duplicate by full qualified name so we never emit the same RegisterType<T>() twice
        // even if multiple element references resolve to the same INamedTypeSymbol (e.g. the
        // root x:Class type is also referenced as the document root element).
        var emittedTypes = new HashSet<string>(StringComparer.Ordinal);
        emittedTypes.Add(result.ClassName!);

        foreach (var element in result.ReferencedElements)
        {
            var resolvedFullName = xmlnsResolver.ResolveToGlobalQualifiedName(element.ElementName, element.NamespaceUri);
            if (resolvedFullName == null)
                continue;

            if (!emittedTypes.Add(resolvedFullName))
                continue;

            sb.AppendLine($"        global::Jalium.UI.Markup.XamlTypeRegistry.RegisterType<{resolvedFullName}>();");
        }

        // 同时把 view-model 类型注册到 XamlTypeRegistry 让 ResolveType("vm:Foo")
        // 在 trim 后仍能命中(否则 Assembly.GetType 在 PublishTrimmed 下可能返回 null)。
        // typeof() 引用本身也是 trim 的 root,与 DynamicDependency 互补 — DynamicDependency
        // 保住成员元数据,typeof 保住类型本身。
        foreach (var dataTypeFullName in dataTypeFullNames)
        {
            if (!emittedTypes.Add(dataTypeFullName))
                continue;

            sb.AppendLine($"        global::Jalium.UI.Markup.XamlTypeRegistry.RegisterType<{dataTypeFullName}>();");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // Generate fields for named elements.
        // 字段声明为非可空 + `= null!;`:命名元素一定会被 InitializeComponent 中 XamlReader.LoadComponent
        // 通过 _namedElements 字典在 ctor 体内回填,使用方不需要 `?.` 或 `!`。`null!` 抑制 CS8618
        // (Source Generator 不能依赖 ctor 体执行顺序的 nullable 流分析)。
        foreach (var element in result.NamedElements)
        {
            sb.AppendLine($"    private {element.TypeName} {element.Name} = null!;");
        }

        if (result.NamedElements.Count > 0)
        {
            sb.AppendLine();
        }

        // Generate InitializeComponent method ("uic" path). The parser produced a complete
        // AST and JalxamlCodeGenerator lowered every node — the body is straight-line
        // XamlBuilder construction code with x:Name fields wired by direct assignment.
        // No embedded jalxaml resource and no runtime XML parsing.

        // 幂等 guard:Application 基类的 ctor 会通过反射调一次 InitializeComponent,
        // 但用户子类(WPF 习惯)往往也手动写 `InitializeComponent()` 在自己 ctor 第一行。
        // 不加保护就会**双重加载** —— 同一份 jalxaml 解析两遍 (~5-10ms 浪费,且重复触发
        // 资源字典初始化、命名元素覆写等副作用)。这个 bool 字段保证无论调多少次都只
        // 真正加载一次,Application/Window/UserControl 子类的 ctor 写法都安全。
        sb.AppendLine("    private bool _jalxaml_initialized;");
        sb.AppendLine();

        // Suppress trim/AOT warnings on the generated method. The codegen path itself is
        // largely static (typeof + new + direct field assignment), but XamlBuilder.SetProperty
        // forwards into the runtime type-converter / markup-extension pipeline which is
        // marked [RequiresUnreferencedCode]. The corresponding type-pinning has already
        // been emitted in the ModuleInitializer above (RegisterType<T>() + DynamicDependency
        // on the codebehind). Suppress here so consumers don't see one warning per view.
        sb.AppendLine("    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Trimming\", \"IL2026\", Justification = \"All element types referenced in jalxaml are pinned via XamlTypeRegistry.RegisterType and DynamicDependency in the ModuleInitializer above.\")]");
        sb.AppendLine("    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AOT\", \"IL3050\", Justification = \"Markup-extension ProvideValue uses dynamic code only for x:Array; framework markup extensions remain AOT-safe.\")]");
        sb.AppendLine("    private void InitializeComponent()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_jalxaml_initialized) return;");
        sb.AppendLine("        _jalxaml_initialized = true;");
        if (useRuntimeFallback)
        {
            EmitRuntimeFallbackBody(sb, result, sourceContent);
        }
        else
        {
            sb.Append(initializeBody);
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Returns true when a document the SG cannot lower can still be handed to the runtime
    /// JALXAML parser. Required preconditions: AST root parsed, x:Class resolved (so we
    /// have a partial type to extend), and the rejection was caused by STRUCTURAL Razor
    /// (<c>@if</c> / <c>@section</c> / <c>@RenderSection</c>). Value-expression Razor
    /// (<c>@(...)</c> / <c>@identifier</c>) is lowered in-place by the SG itself and
    /// does NOT need runtime fallback.
    /// </summary>
    private static bool CanUseRuntimeFallback(JalxamlParseResult result)
    {
        return result.HasStructuralRazor
            && result.Root != null
            && !string.IsNullOrEmpty(result.Root!.ResolvedClrTypeName)
            && !string.IsNullOrEmpty(result.ClassName);
    }

    /// <summary>
    /// Emit a runtime-fallback <c>InitializeComponent</c> body: hand the inlined jalxaml
    /// source text to the runtime parser. x:Name fields are populated via the named-elements
    /// dictionary overload — the field declarations themselves are already emitted earlier
    /// in the partial class, so user code-behind can reference them after this call returns.
    /// </summary>
    private static void EmitRuntimeFallbackBody(StringBuilder sb, JalxamlParseResult result, string sourceContent)
    {
        if (result.NamedElements.Count > 0)
        {
            sb.AppendLine("        var __namedElements = new global::System.Collections.Generic.Dictionary<string, object>(global::System.StringComparer.Ordinal);");
            sb.AppendLine($"        global::Jalium.UI.Markup.XamlReader.LoadComponentFromString(this, {EmitVerbatimStringLiteral(sourceContent)}, __namedElements);");
            foreach (var named in result.NamedElements)
            {
                sb.AppendLine($"        if (__namedElements.TryGetValue(\"{named.Name}\", out var __ne_{named.Name}) && __ne_{named.Name} is {named.TypeName} __nc_{named.Name})");
                sb.AppendLine($"            this.{named.Name} = __nc_{named.Name};");
            }
        }
        else
        {
            sb.AppendLine($"        global::Jalium.UI.Markup.XamlReader.LoadComponentFromString(this, {EmitVerbatimStringLiteral(sourceContent)});");
        }
    }

    private static string EmitVerbatimStringLiteral(string content)
    {
        // C# verbatim string literal: @"..." with " escaped as "".
        return "@\"" + content.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Diagnostic descriptor surfaced when <see cref="JalxamlCodeGenerator.TryEmitInitializeBody"/>
    /// or <see cref="JalxamlCodeGenerator.TryEmitDictionaryBuildBody"/> rejects a document.
    /// Now that runtime jalxaml parsing is gone there is no fallback path the build can land
    /// on, so the developer must either fix the document or add the missing CLR type / xmlns
    /// declaration the SG could not resolve.
    /// </summary>
    private static readonly DiagnosticDescriptor CodegenBailDescriptor = new(
        id: "JALXAML002",
        title: "JALXAML cannot be lowered to C#",
        messageFormat: "Cannot generate compile-time code for jalxaml file '{0}': {1}. Runtime jalxaml parsing is no longer shipped — fix the document or add the missing xmlns/clr-namespace declaration.",
        category: "Jalium.UI.Xaml",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingProjectDirDescriptor = new(
        id: "JALXAML003",
        title: "JALXAML cannot resolve project directory",
        messageFormat: "Cannot register prebuilt resource dictionary for '{0}' because the SG was not given MSBuildProjectDirectory. Reference Jalium.UI.Build.targets so the AdditionalFiles metadata is forwarded to the generator.",
        category: "Jalium.UI.Xaml",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static void ReportCodegenBail(SourceProductionContext context, string filePath, JalxamlParseResult result)
    {
        var reason = DescribeBailReason(result);
        context.ReportDiagnostic(Diagnostic.Create(CodegenBailDescriptor, Location.None, filePath, reason));
    }

    private static void ReportMissingProjectDir(SourceProductionContext context, string filePath)
    {
        context.ReportDiagnostic(Diagnostic.Create(MissingProjectDirDescriptor, Location.None, filePath));
    }

    /// <summary>
    /// Best-effort one-line explanation of why the codegen rejected a document. Walks the
    /// parsed AST looking for the same conditions the codegen guards on. Used purely to
    /// produce a more actionable diagnostic message — the build fails regardless.
    /// </summary>
    private static string DescribeBailReason(JalxamlParseResult result)
    {
        if (result.HasStructuralRazor)
            return "document contains structural Razor (@if / @section / @RenderSection) the SG cannot lower; runtime fallback was not eligible (root unresolved or no x:Class)";
        if (result.Root == null)
            return "document root could not be parsed";
        if (string.IsNullOrEmpty(result.Root.ResolvedClrTypeName))
            return $"root element '{result.Root.LocalName}' (xmlns '{result.Root.NamespaceUri}') is missing an XmlnsDefinitionAttribute or clr-namespace declaration";
        var unresolved = FindFirstUnresolved(result.Root);
        if (unresolved != null)
            return $"element '{unresolved.LocalName}' (xmlns '{unresolved.NamespaceUri}') is missing an XmlnsDefinitionAttribute or clr-namespace declaration";
        var multiChildTpl = FindMultiChildTemplate(result.Root);
        if (multiChildTpl != null)
            return $"template element '{multiChildTpl.LocalName}' has more than one child — templates accept a single visual root";
        return "unknown codegen failure";
    }

    private static JalxamlAstNode? FindFirstUnresolved(JalxamlAstNode node)
    {
        if (string.IsNullOrEmpty(node.ResolvedClrTypeName))
            return node;
        foreach (var child in node.Children)
        {
            var hit = FindFirstUnresolved(child);
            if (hit != null) return hit;
        }
        foreach (var pe in node.PropertyElements)
        {
            foreach (var child in pe.Children)
            {
                var hit = FindFirstUnresolved(child);
                if (hit != null) return hit;
            }
        }
        return null;
    }

    private static JalxamlAstNode? FindMultiChildTemplate(JalxamlAstNode node)
    {
        // Mirror JalxamlCodeGenerator.IsTemplateClrType — no shared API today, the list is
        // tiny and stable.
        var name = node.ResolvedClrTypeName;
        bool isTpl = name == "Jalium.UI.ControlTemplate" ||
                     name == "Jalium.UI.DataTemplate" ||
                     name == "Jalium.UI.HierarchicalDataTemplate" ||
                     name == "Jalium.UI.ItemsPanelTemplate" ||
                     name == "Jalium.UI.ItemContainerTemplate";
        if (isTpl && node.Children.Count > 1)
            return node;
        foreach (var child in node.Children)
        {
            var hit = FindMultiChildTemplate(child);
            if (hit != null) return hit;
        }
        foreach (var pe in node.PropertyElements)
        {
            foreach (var child in pe.Children)
            {
                var hit = FindMultiChildTemplate(child);
                if (hit != null) return hit;
            }
        }
        return null;
    }
}

/// <summary>
/// Represents a parsed JALXAML file.
/// </summary>
public sealed class JalxamlParseResult
{
    public string? ClassName { get; set; }
    public string? RootElementType { get; set; }

    /// <summary>
    /// Full AST tree of the document body. When non-null the SourceGenerator emits
    /// straight-line C# construction code via <see cref="JalxamlCodeGenerator"/>.
    /// When null (regex fallback or unsupported features) the SG falls back to
    /// the legacy embedded-resource <c>LoadComponent</c> path.
    /// </summary>
    public JalxamlAstNode? Root { get; set; }

    /// <summary>
    /// True if the document contains Razor content of ANY kind. Equivalent to
    /// <c><see cref="HasStructuralRazor"/> || <see cref="HasRazorExpressions"/></c>;
    /// kept as a back-compat alias.
    /// </summary>
    public bool HasRazorContent => HasStructuralRazor || HasRazorExpressions;

    /// <summary>
    /// True if the document contains "structural" Razor directives the SG cannot lower:
    /// runtime <c>@if(...)</c>, <c>@section Name { ... }</c>, <c>@RenderSection("Name")</c>,
    /// or any leftover <c>@{ ... }</c> code block that <see cref="Jalium.UI.Build.TransformJalxamlRazorTask"/>
    /// failed to compile-time expand. These force the whole document onto the runtime fallback
    /// (<see cref="Jalium.UI.Markup.XamlReader.LoadComponentFromString(object, string, Uri?, System.Reflection.Assembly?)"/>)
    /// because their semantics (DataContext-driven re-evaluation, cross-document section lookup)
    /// cannot be inlined into straight-line C#.
    /// </summary>
    public bool HasStructuralRazor { get; set; }

    /// <summary>
    /// True if the document contains "value-expression" Razor: <c>@(expr)</c>, <c>@identifier</c>,
    /// <c>$.path</c>, <c>#.path</c>. These can be lowered by the SG to direct
    /// <c>XamlBuilder.SetRazorBinding(...)</c> calls — no runtime parser involvement needed,
    /// trim-safe, AOT-safe. Lowered per-attribute (or per text-content node) at codegen time.
    /// </summary>
    public bool HasRazorExpressions { get; set; }

    /// <summary>
    /// True if the document contained lowerable structural Razor (simple <c>@if</c> /
    /// <c>@section</c> / <c>@RenderSection</c>) that the parser lifted into synthetic
    /// wrappers and codegen turned into straight-line C#
    /// (<c>SetRazorIfVisibility</c> / <c>RegisterRazorSection</c> /
    /// <see cref="Jalium.UI.Markup.RazorSectionHost"/>). Purely informational — unlike
    /// <see cref="HasStructuralRazor"/> it does NOT force the runtime fallback (the
    /// whole point is that these no longer hit
    /// <see cref="Jalium.UI.Markup.XamlReader.LoadComponentFromString(object, string, Uri?, System.Reflection.Assembly?)"/>
    /// or <c>XamlReader.Parse</c>).
    /// </summary>
    public bool HasLoweredStructuralRazor { get; set; }

    /// <summary>
    /// Set during the lifted parse when an <c>@if</c> or <c>@section</c> block turned out
    /// to contain block-level text or a property element (not purely child elements),
    /// which the per-child visibility binding / single-root section factory can't
    /// represent faithfully. <see cref="JalxamlParser.Parse"/> then discards the lifted
    /// result and reparses the original document via the legacy strip + runtime-fallback
    /// path. Never observed by codegen — it only gates the parse-time fallback decision.
    /// </summary>
    public bool RazorLiftUnfaithful { get; set; }

    public List<NamedElement> NamedElements { get; } = new();

    /// <summary>
    /// Every element type referenced by the document (root + descendants), excluding XAML
    /// property-elements like <c>Grid.RowDefinitions</c>. Captured for AOT pinning so the
    /// generator can emit <c>typeof(T)</c> references that prevent the trimmer from
    /// removing constructors / metadata that <see cref="System.Activator.CreateInstance(System.Type)"/>
    /// would need at runtime. De-duplicated by (elementName, namespaceUri).
    /// </summary>
    public List<ReferencedElement> ReferencedElements { get; } = new();

    /// <summary>
    /// View-model types referenced via <c>DataType="prefix:Foo"</c> on DataTemplate / element
    /// declarations. Generator emits <c>[DynamicDependency(PublicProperties|PublicFields, typeof(T))]</c>
    /// so the IL trimmer keeps property getters/setters that the binding engine reads via
    /// reflection (<see cref="System.Reflection.PropertyInfo.GetValue(object?)"/>). Without
    /// this pin, <c>PublishTrimmed=true</c> drops view-model property getters and the runtime
    /// reflection fallback throws <c>ArgumentException("Property Get method was not found")</c>.
    /// De-duplicated by (typeName, namespaceUri).
    /// </summary>
    public List<ReferencedElement> DataTypeReferences { get; } = new();

    /// <summary>
    /// Snapshot of <c>xmlns</c> declarations active on the document root element. Maps
    /// <c>prefix → xmlNamespaceUri</c> (default namespace stored under the empty key).
    /// Used by the source generator to resolve attribute-value prefix references such as
    /// <c>TargetType="controls:WelcomeTemplateCard"</c> at compile time — without this
    /// the runtime <see cref="Jalium.UI.Markup.TypeTypeConverter"/> would receive the bare
    /// "controls:WelcomeTemplateCard" string and fail to resolve the prefix (it only
    /// consults <see cref="Jalium.UI.Markup.XamlTypeRegistry"/>'s simple-name table).
    /// </summary>
    /// <remarks>
    /// Limited to the root element: 99% of jalxaml authors declare every <c>xmlns:X</c>
    /// up front. Inner-element prefix overrides aren't currently captured — if a future
    /// document needs them we'll have to plumb the mapping through every AST node.
    /// </remarks>
    public Dictionary<string, string> RootPrefixMappings { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents a named element (x:Name) in JALXAML.
/// </summary>
public sealed class NamedElement
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}

/// <summary>
/// A unique element type reference discovered during parsing. The pair
/// (<see cref="ElementName"/>, <see cref="NamespaceUri"/>) uniquely identifies
/// the XAML type the generator must pin for AOT.
/// </summary>
public sealed class ReferencedElement
{
    public string ElementName { get; set; } = string.Empty;
    public string NamespaceUri { get; set; } = string.Empty;
}
