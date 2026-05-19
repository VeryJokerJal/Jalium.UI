using System;
using System.Collections.Generic;
using System.Reflection;
using Jalium.UI;

namespace Jalium.UI.Markup;

/// <summary>
/// Compile-time XAML builder bridge — runtime entry-point for SourceGenerator-emitted
/// jalxaml construction code (the "uic" path). Located in <c>Jalium.UI.Core</c> so that
/// the framework's own theme dictionaries (<c>Jalium.UI.Controls/Themes/**/*.jalxaml</c>)
/// can be lowered without introducing a circular reference between Controls and Xaml.
///
/// <para>
/// All "real" implementations are wired through the <c>*Impl</c> delegate fields. Two
/// assemblies cooperate to populate them:
/// <list type="bullet">
///   <item><c>Jalium.UI.Xaml</c> registers the value-handling callbacks (<see cref="SetPropertyImpl"/>,
///   <see cref="AddChildImpl"/>, <see cref="ApplyPropertyElementChildImpl"/>, etc.) which
///   delegate into <c>XamlReader</c>'s internal bridge.</item>
///   <item><c>Jalium.UI.Controls</c> registers the strongly-typed attached-property fast
///   paths (<see cref="SetGridRowImpl"/>, <see cref="SetDockPanelDockImpl"/>, etc.) which
///   call directly into the framework setters with no reflection.</item>
/// </list>
/// </para>
///
/// <para>
/// The trampoline shape keeps Core itself free of any runtime XAML-loader dependency, so
/// projects that consume only Core (no Xaml, no theme loading) still link cleanly.
/// </para>
/// </summary>
public static class XamlBuilder
{
    // ============================================================
    // Value / structure callbacks (registered by Jalium.UI.Xaml).
    // ============================================================

    /// <summary>Registered by Jalium.UI.Xaml: creates a build context attached to the codebehind component.</summary>
    public static Func<object, Uri?, Assembly?, XamlBuildContext>? BeginComponentImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: pushes an element onto the build context's parent stack.</summary>
    public static Action<object, XamlBuildContext>? PushParentImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: pops the topmost parent.</summary>
    public static Action<XamlBuildContext>? PopParentImpl { get; set; }

    /// <summary>
    /// Registered by Jalium.UI.Xaml: assigns a CLR property, routing strings through the
    /// markup-extension / type-converter pipeline. Returns the (possibly replaced) instance
    /// — most setters return the same reference, but a few are special-cased and produce a
    /// new object the caller must use going forward (e.g. <c>ResourceDictionary.Source</c>
    /// triggers external loading and returns the loaded dictionary).
    /// </summary>
    public static Func<object, string, object?, XamlBuildContext, object>? SetPropertyImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: assigns an attached property by reflective owner-type lookup.</summary>
    public static Action<object, string, string, string, XamlBuildContext, string>? SetAttachedPropertyImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: adds a child via the parent's content-property / collection rules.</summary>
    public static Action<object, object, XamlBuildContext, string?>? AddChildImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: applies a property-element child onto the parent.</summary>
    public static Action<object, string, object?, XamlBuildContext, string?>? ApplyPropertyElementChildImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: applies an x:* directive (Name / Key / Class).</summary>
    public static Action<object, string, string, XamlBuildContext>? ApplyXDirectiveImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: finalises a build (NameScope registration + hot-reload).</summary>
    public static Action<object, XamlBuildContext, IDictionary<string, object>?>? EndComponentImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: returns a captured x:Name element by name.</summary>
    public static Func<string, XamlBuildContext, object?>? GetNamedImpl { get; set; }

    /// <summary>
    /// Registered by Jalium.UI.Xaml: applies the inner text content of an element through
    /// the runtime <c>SetContentProperty</c> pipeline, which honours <see cref="TypeConverter"/>
    /// registrations. May replace the instance entirely (e.g. <c>&lt;Color&gt;#FFFFFF&lt;/Color&gt;</c>
    /// — the runtime parses the text via <c>ColorConverter</c> and returns the parsed
    /// <c>Color</c>; the SG-emitted code reassigns its local to the returned value).
    /// </summary>
    public static Func<object, string, XamlBuildContext, object>? SetContentTextImpl { get; set; }

    // ============================================================
    // Markup-extension fast paths. SG identifies <c>{StaticResource Key}</c> /
    // <c>{ThemeResource Key}</c> / <c>{DynamicResource Key}</c> attribute values at compile
    // time and emits direct calls to these helpers, bypassing the runtime markup-string
    // parser entirely. Implementations are registered by Jalium.UI.Xaml.
    // ============================================================

    /// <summary>
    /// Registered by Jalium.UI.Xaml: resolves a StaticResource key against the ambient
    /// resource chain and assigns the result to the named property on <paramref name="target"/>.
    /// Behaves like the runtime <c>StaticResourceExtension.ProvideValue</c> path.
    /// </summary>
    public static Action<object, string, string, XamlBuildContext>? SetStaticResourceImpl { get; set; }

    /// <summary>
    /// Registered by Jalium.UI.Xaml: resolves a DynamicResource key against the ambient
    /// resource chain and registers a subscription so the target property updates whenever
    /// the resource changes. Used for <c>{DynamicResource Key}</c> and <c>{ThemeResource Key}</c>.
    /// </summary>
    public static Action<object, string, string, XamlBuildContext>? SetDynamicResourceImpl { get; set; }

    /// <summary>
    /// Set the named property on <paramref name="target"/> to the value of the static
    /// resource identified by <paramref name="key"/>. Cheap inline path emitted by the SG
    /// for <c>{StaticResource Foo}</c> attribute values; bypasses the runtime markup-string
    /// parser.
    /// </summary>
    public static void SetStaticResource(object target, string propertyName, string key, XamlBuildContext ctx)
        => Required(SetStaticResourceImpl, nameof(SetStaticResourceImpl))(target, propertyName, key, ctx);

    /// <summary>
    /// Subscribe the named property on <paramref name="target"/> to a dynamic resource
    /// identified by <paramref name="key"/>. Used for both <c>{DynamicResource Foo}</c> and
    /// <c>{ThemeResource Foo}</c> — the underlying subscription tracks resource-dictionary
    /// changes, which is exactly what theme switching needs.
    /// </summary>
    public static void SetDynamicResource(object target, string propertyName, string key, XamlBuildContext ctx)
        => Required(SetDynamicResourceImpl, nameof(SetDynamicResourceImpl))(target, propertyName, key, ctx);

    /// <summary>
    /// Registered by Jalium.UI.Xaml: bind <paramref name="propertyName"/> on
    /// <paramref name="target"/> to the property named <paramref name="sourcePropertyName"/>
    /// on the templated parent. SG identifies <c>{TemplateBinding Foo}</c> attribute values
    /// at compile time and emits direct calls into this helper.
    /// </summary>
    public static Action<object, string, string, XamlBuildContext>? SetTemplateBindingImpl { get; set; }

    /// <summary>
    /// Bind <paramref name="propertyName"/> on <paramref name="target"/> to
    /// <paramref name="sourcePropertyName"/> on the templated parent. Replaces the runtime
    /// markup-extension parser path for the most common form a control template uses
    /// (<c>Background="{TemplateBinding Background}"</c> etc.).
    /// </summary>
    public static void SetTemplateBinding(object target, string propertyName, string sourcePropertyName, XamlBuildContext ctx)
        => Required(SetTemplateBindingImpl, nameof(SetTemplateBindingImpl))(target, propertyName, sourcePropertyName, ctx);

    // ============================================================
    // Strongly-typed attached-property fast paths (registered by Jalium.UI.Controls).
    // SG emits direct calls to these helpers when it can prove the value is an integer /
    // double / Dock literal. Controls populates the impl delegates from a ModuleInitializer.
    // ============================================================

    /// <summary>Registered by Jalium.UI.Controls: <c>Grid.SetRow</c>.</summary>
    public static Action<UIElement, int>? SetGridRowImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Grid.SetColumn</c>.</summary>
    public static Action<UIElement, int>? SetGridColumnImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Grid.SetRowSpan</c>.</summary>
    public static Action<UIElement, int>? SetGridRowSpanImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Grid.SetColumnSpan</c>.</summary>
    public static Action<UIElement, int>? SetGridColumnSpanImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Canvas.SetLeft</c>.</summary>
    public static Action<UIElement, double>? SetCanvasLeftImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Canvas.SetTop</c>.</summary>
    public static Action<UIElement, double>? SetCanvasTopImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Canvas.SetRight</c>.</summary>
    public static Action<UIElement, double>? SetCanvasRightImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Canvas.SetBottom</c>.</summary>
    public static Action<UIElement, double>? SetCanvasBottomImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Controls: <c>Panel.SetZIndex</c>.</summary>
    public static Action<UIElement, int>? SetPanelZIndexImpl { get; set; }

    /// <summary>
    /// Registered by Jalium.UI.Controls: <c>DockPanel.SetDock</c>. Receives the integer
    /// representation of the Dock enum so this signature does not pull <c>Jalium.UI.Controls.Dock</c>
    /// into Core.
    /// </summary>
    public static Action<UIElement, int>? SetDockPanelDockImpl { get; set; }

    // ============================================================
    // Public API surface — what the SG emits calls into. Each method either invokes the
    // registered delegate or throws an explanatory error so a missing assembly link does
    // not surface as an opaque NullReferenceException.
    // ============================================================

    /// <summary>Begin a build session — see assembly-level documentation for invariants.</summary>
    public static XamlBuildContext BeginComponent(object component, Uri? baseUri = null, Assembly? sourceAssembly = null)
        => Required(BeginComponentImpl, nameof(BeginComponentImpl))(component, baseUri, sourceAssembly);

    /// <summary>Push <paramref name="parent"/> onto the build context's parent stack.</summary>
    public static void PushParent(object parent, XamlBuildContext ctx)
        => Required(PushParentImpl, nameof(PushParentImpl))(parent, ctx);

    /// <summary>Pop the most recently pushed parent.</summary>
    public static void PopParent(XamlBuildContext ctx)
        => Required(PopParentImpl, nameof(PopParentImpl))(ctx);

    /// <summary>
    /// Set a regular CLR property — strings flow through markup ext / type converter.
    /// Returns the (possibly replaced) instance reference. Callers may keep the old
    /// reference for the common case, but for special properties such as
    /// <c>ResourceDictionary.Source</c> the runtime swaps the instance out for an
    /// externally-loaded dictionary and the caller must use the returned value.
    /// </summary>
    public static object SetProperty(object instance, string propertyName, object? value, XamlBuildContext ctx)
        => Required(SetPropertyImpl, nameof(SetPropertyImpl))(instance, propertyName, value, ctx);

    /// <summary>Set an attached property by name (reflective).</summary>
    public static void SetAttachedProperty(
        object instance,
        string ownerTypeName,
        string propertyName,
        string value,
        XamlBuildContext ctx,
        string elementNamespaceUri = "")
        => Required(SetAttachedPropertyImpl, nameof(SetAttachedPropertyImpl))(
            instance, ownerTypeName, propertyName, value, ctx, elementNamespaceUri);

    /// <summary>Add a child onto the parent according to its content-property / collection rules.</summary>
    public static void AddChild(object parent, object child, XamlBuildContext ctx, string? resourceKey = null)
        => Required(AddChildImpl, nameof(AddChildImpl))(parent, child, ctx, resourceKey);

    /// <summary>Apply a child of a property-element form (<c>&lt;Foo.Bar&gt;...&lt;/Foo.Bar&gt;</c>).</summary>
    public static void ApplyPropertyElementChild(
        object parent,
        string propertyName,
        object? child,
        XamlBuildContext ctx,
        string? resourceKey = null)
        => Required(ApplyPropertyElementChildImpl, nameof(ApplyPropertyElementChildImpl))(parent, propertyName, child, ctx, resourceKey);

    /// <summary>Apply an x:* directive (Name / Key / Class).</summary>
    public static void ApplyXDirective(object instance, string directive, string value, XamlBuildContext ctx)
        => Required(ApplyXDirectiveImpl, nameof(ApplyXDirectiveImpl))(instance, directive, value, ctx);

    /// <summary>Finalise the build (NameScope registration + hot-reload).</summary>
    public static void EndComponent(object root, XamlBuildContext ctx, IDictionary<string, object>? namedElementsOut = null)
        => Required(EndComponentImpl, nameof(EndComponentImpl))(root, ctx, namedElementsOut);

    /// <summary>Look up an x:Name element captured during build.</summary>
    public static object? GetNamed(string name, XamlBuildContext ctx)
        => Required(GetNamedImpl, nameof(GetNamedImpl))(name, ctx);

    /// <summary>
    /// Apply <paramref name="text"/> as the element's content (the value between an opening
    /// and closing tag, e.g. <c>&lt;Color&gt;#FFFFFF&lt;/Color&gt;</c>). The runtime routes the
    /// string through the registered <c>ContentProperty</c> setter or, when the element type
    /// has a registered <see cref="TypeConverter"/>, parses the text into a typed value and
    /// returns that. The SG-emitted code <b>must</b> reassign its local from the return value
    /// — the converter may produce a brand-new instance (typical for value-type elements).
    /// </summary>
    public static object SetContentText(object instance, string text, XamlBuildContext ctx)
        => Required(SetContentTextImpl, nameof(SetContentTextImpl))(instance, text, ctx);

    // ============================================================
    // Razor value-expression fast paths (registered by Jalium.UI.Xaml). SG identifies
    // `@(expr)` / `@identifier` / `$.path` / `#.path` in attribute values and text-content
    // nodes at compile time, pre-computes the dependency identifier set, and emits direct
    // calls into these helpers. The runtime then bypasses RazorExpressionAnalyzer's
    // reflection walk and constructs the binding directly from the precomputed deps.
    // ============================================================

    /// <summary>Registered by Jalium.UI.Xaml: lower SG-detected Razor value-expression onto a DP / CLR property.</summary>
    public static Action<object, string, string, string[], XamlBuildContext>? SetRazorBindingImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: lower SG-detected Razor expression onto the element's content property (Text / Content).</summary>
    public static Action<object, string, string[], XamlBuildContext>? SetContentRazorBindingImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: bind a lifted <c>@if</c> child's Visibility to the (combined) condition.</summary>
    public static Action<object, string, string[], XamlBuildContext>? SetRazorIfVisibilityImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: register a compiled <c>@section</c> body factory under a name.</summary>
    public static Action<string, Func<object?>>? RegisterRazorSectionImpl { get; set; }

    /// <summary>Registered by Jalium.UI.Xaml: apply an SG-pre-split <c>{Binding ...}</c> to a property.</summary>
    public static Action<object, string, string?, string[], string[], XamlBuildContext>? SetCompiledBindingImpl { get; set; }

    /// <summary>
    /// Bind <paramref name="propertyName"/> on <paramref name="target"/> to a Razor value-expression
    /// (<c>@(expr)</c>, <c>@identifier</c>, <c>$.path</c>, <c>#.path</c>, or interpolated mix).
    /// The SG pre-computes <paramref name="dependencies"/> at codegen time so the runtime
    /// doesn't pay the reflection walk in <c>RazorExpressionAnalyzer</c> on each call.
    /// </summary>
    public static void SetRazorBinding(object target, string propertyName, string expression, string[] dependencies, XamlBuildContext ctx)
        => Required(SetRazorBindingImpl, nameof(SetRazorBindingImpl))(target, propertyName, expression, dependencies, ctx);

    /// <summary>
    /// Bind the element's text-content property (typically <c>Text</c> on <see cref="TextBlock"/>
    /// or <c>Content</c> on <see cref="ContentControl"/>) to a Razor value-expression. The
    /// runtime resolves the actual property via the element's <see cref="System.Windows.Markup.ContentPropertyAttribute"/>
    /// (mirroring the <see cref="SetContentText"/> path).
    /// </summary>
    public static void SetContentRazorBinding(object target, string expression, string[] dependencies, XamlBuildContext ctx)
        => Required(SetContentRazorBindingImpl, nameof(SetContentRazorBindingImpl))(target, expression, dependencies, ctx);

    /// <summary>
    /// Bind <paramref name="child"/>'s <c>Visibility</c> to a Razor <c>@if</c>
    /// <paramref name="condition"/> (already combined across nesting levels, matching the
    /// runtime <c>BuildCombinedIfConditionExpression</c> shape). The SG calls this once per
    /// element that sat inside a lifted <c>@if(...) { ... }</c> block, immediately after the
    /// child is added to its parent. Behaviour is identical to the streaming parser's
    /// <c>ShouldIncludeConditionalChild</c> path — the same
    /// <c>RazorBindingEngine.TryApplyIfVisibility</c> binding — but with no document
    /// re-parse. <paramref name="dependencies"/> are pre-computed by the SG so the runtime
    /// analyzer skips its reflection walk.
    /// </summary>
    public static void SetRazorIfVisibility(object child, string condition, string[] dependencies, XamlBuildContext ctx)
        => Required(SetRazorIfVisibilityImpl, nameof(SetRazorIfVisibilityImpl))(child, condition, dependencies, ctx);

    /// <summary>
    /// Register a compiled <c>@section <paramref name="name"/> { ... }</c> body factory.
    /// The SG emits this into the defining component's <c>InitializeComponent</c> instead
    /// of registering the section's raw XAML string; <see cref="RazorSectionHost"/> then
    /// invokes <paramref name="factory"/> directly (no <c>XamlReader.Parse</c>). Each call
    /// produces a fresh visual tree, matching the runtime's per-host re-parse semantics.
    /// </summary>
    public static void RegisterRazorSection(string name, Func<object?> factory)
        => Required(RegisterRazorSectionImpl, nameof(RegisterRazorSectionImpl))(name, factory);

    /// <summary>
    /// Apply a <c>{Binding ...}</c> the SourceGenerator already split at compile time:
    /// <paramref name="positionalPath"/> is the leading path segment (or null) and
    /// <paramref name="names"/>/<paramref name="values"/> are the <c>name=value</c> pairs.
    /// The runtime rebuilds the binding via the verbatim
    /// <c>MarkupExtensionParser.BuildBindingExtension</c> + the same apply path a
    /// runtime-parsed binding uses, so behaviour is identical — only the
    /// <c>{Binding}</c> string parse moved off the construction hot path (AOT-safe,
    /// no per-element markup re-parse).
    /// </summary>
    public static void SetCompiledBinding(object target, string propertyName, string? positionalPath, string[] names, string[] values, XamlBuildContext ctx)
        => Required(SetCompiledBindingImpl, nameof(SetCompiledBindingImpl))(target, propertyName, positionalPath, names, values, ctx);

    // Strongly-typed attached fast paths.
    /// <summary>Strongly-typed setter for <c>Grid.Row</c>.</summary>
    public static void SetGridRow(UIElement element, int row)
        => Required(SetGridRowImpl, nameof(SetGridRowImpl))(element, row);

    /// <summary>Strongly-typed setter for <c>Grid.Column</c>.</summary>
    public static void SetGridColumn(UIElement element, int column)
        => Required(SetGridColumnImpl, nameof(SetGridColumnImpl))(element, column);

    /// <summary>Strongly-typed setter for <c>Grid.RowSpan</c>.</summary>
    public static void SetGridRowSpan(UIElement element, int span)
        => Required(SetGridRowSpanImpl, nameof(SetGridRowSpanImpl))(element, span);

    /// <summary>Strongly-typed setter for <c>Grid.ColumnSpan</c>.</summary>
    public static void SetGridColumnSpan(UIElement element, int span)
        => Required(SetGridColumnSpanImpl, nameof(SetGridColumnSpanImpl))(element, span);

    /// <summary>Strongly-typed setter for <c>Canvas.Left</c>.</summary>
    public static void SetCanvasLeft(UIElement element, double value)
        => Required(SetCanvasLeftImpl, nameof(SetCanvasLeftImpl))(element, value);

    /// <summary>Strongly-typed setter for <c>Canvas.Top</c>.</summary>
    public static void SetCanvasTop(UIElement element, double value)
        => Required(SetCanvasTopImpl, nameof(SetCanvasTopImpl))(element, value);

    /// <summary>Strongly-typed setter for <c>Canvas.Right</c>.</summary>
    public static void SetCanvasRight(UIElement element, double value)
        => Required(SetCanvasRightImpl, nameof(SetCanvasRightImpl))(element, value);

    /// <summary>Strongly-typed setter for <c>Canvas.Bottom</c>.</summary>
    public static void SetCanvasBottom(UIElement element, double value)
        => Required(SetCanvasBottomImpl, nameof(SetCanvasBottomImpl))(element, value);

    /// <summary>Strongly-typed setter for <c>Panel.ZIndex</c>.</summary>
    public static void SetPanelZIndex(UIElement element, int value)
        => Required(SetPanelZIndexImpl, nameof(SetPanelZIndexImpl))(element, value);

    /// <summary>
    /// Strongly-typed setter for <c>DockPanel.Dock</c>. Accepts the integer dock value so
    /// callers in Core do not need a reference to <c>Jalium.UI.Controls.Dock</c>; the
    /// Controls-side delegate casts back into the enum.
    /// </summary>
    public static void SetDockPanelDock(UIElement element, int dockEnumValue)
        => Required(SetDockPanelDockImpl, nameof(SetDockPanelDockImpl))(element, dockEnumValue);

    private static T Required<T>(T? impl, string fieldName) where T : class
    {
        if (impl != null)
            return impl;
        throw new InvalidOperationException(
            $"XamlBuilder.{fieldName} has not been registered. " +
            $"Reference Jalium.UI.Xaml (and Jalium.UI.Controls for attached-property fast paths) " +
            $"so its ModuleInitializer wires up the runtime callbacks.");
    }
}

/// <summary>
/// Opaque handle returned from <see cref="XamlBuilder.BeginComponent"/>. Carries the
/// internal parser-context state without exposing it to user code. Code outside of
/// <c>Jalium.UI.Xaml</c> never reads <see cref="Inner"/>; it only flows the handle
/// from one builder call to the next.
/// </summary>
public sealed class XamlBuildContext
{
    /// <summary>
    /// Internal state set by the implementing assembly. Not part of the stable API —
    /// access only from within <c>Jalium.UI.Xaml</c>.
    /// </summary>
    public object? Inner { get; }

    /// <summary>Construct a build context wrapping the implementing parser state.</summary>
    public XamlBuildContext(object? inner) { Inner = inner; }
}
