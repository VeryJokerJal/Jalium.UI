using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Markup;

/// <summary>
/// Wires the runtime implementation behind <see cref="XamlBuilder"/> (which lives in
/// <c>Jalium.UI.Core</c> as a delegate trampoline) onto the actual <c>XamlReader</c>
/// internals. The delegate registration runs in a <c>[ModuleInitializer]</c> so the
/// links exist before any <c>InitializeComponent</c> on a SG-generated codebehind has
/// a chance to run.
/// </summary>
internal static class XamlBuilderInitializer
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Jalium.UI.Xaml is the canonical XAML implementation; its ModuleInitializer is the documented integration point.")]
    [SuppressMessage("Trimming", "IL2026", Justification = "ModuleInitializer registers RUC-bearing delegates; the public API surface is annotated where appropriate.")]
    public static void Register()
    {
        XamlBuilder.BeginComponentImpl = static (component, baseUri, sourceAssembly) =>
        {
            sourceAssembly ??= component.GetType().Assembly;
            var inner = XamlReader.CreateBuilderContext(component, baseUri, sourceAssembly);
            return new XamlBuildContext(inner);
        };

        XamlBuilder.PushParentImpl = static (parent, ctx) =>
        {
            CtxOf(ctx).PushParent(parent);
        };

        XamlBuilder.PopParentImpl = static ctx =>
        {
            CtxOf(ctx).PopParent();
        };

        XamlBuilder.SetPropertyImpl = static (instance, propertyName, value, ctx) =>
        {
            // SetProperty may return a different instance for special-cased properties
            // (notably ResourceDictionary.Source, which loads an external dictionary and
            // hands the populated instance back). Surface the returned reference to the
            // SG-emitted code; callers reassign their local variable.
            return XamlReader.BuilderSetProperty(instance, propertyName, value, CtxOf(ctx));
        };

        XamlBuilder.SetAttachedPropertyImpl = static (instance, ownerTypeName, propertyName, value, ctx, elementNamespaceUri) =>
        {
            XamlReader.BuilderSetAttachedProperty(instance, ownerTypeName, propertyName, value, CtxOf(ctx), elementNamespaceUri);
        };

        XamlBuilder.AddChildImpl = static (parent, child, ctx, resourceKey) =>
        {
            XamlReader.BuilderAddChild(parent, child, CtxOf(ctx), resourceKey);
        };

        XamlBuilder.ApplyPropertyElementChildImpl = static (parent, propertyName, child, ctx, resourceKey) =>
        {
            XamlReader.BuilderApplyPropertyElementChild(parent, propertyName, child, CtxOf(ctx), resourceKey);
        };

        XamlBuilder.ApplyXDirectiveImpl = static (instance, directive, value, ctx) =>
        {
            XamlReader.BuilderApplyXDirective(instance, directive, value, CtxOf(ctx));
        };

        XamlBuilder.EndComponentImpl = static (root, ctx, namedElementsOut) =>
        {
            var inner = CtxOf(ctx);
            XamlReader.BuilderRegisterNamedScope(root, inner);
            if (namedElementsOut != null)
            {
                foreach (var pair in inner.NamedElements)
                {
                    namedElementsOut[pair.Key] = pair.Value;
                }
            }

            var component = inner.CodeBehindInstance ?? root;
            XamlReader.BuilderRegisterHotReload(component);
        };

        XamlBuilder.GetNamedImpl = static (name, ctx) =>
        {
            return CtxOf(ctx).NamedElements.TryGetValue(name, out var value) ? value : null;
        };

        XamlBuilder.SetContentTextImpl = static (instance, text, ctx) =>
        {
            // SetContentProperty may return a different instance — value-type elements
            // (Color, Thickness via inner-text shorthand, …) get parsed by their registered
            // TypeConverter, which yields a fresh boxed value. The SG-emitted code reassigns
            // its local from the returned reference so subsequent AddChild / SetProperty
            // calls see the parsed value rather than the default-constructed stub.
            return XamlReader.BuilderSetContentText(instance, text, CtxOf(ctx));
        };

        XamlBuilder.SetStaticResourceImpl = static (target, propertyName, key, ctx) =>
        {
            XamlReader.BuilderSetStaticResource(target, propertyName, key, CtxOf(ctx));
        };

        XamlBuilder.SetDynamicResourceImpl = static (target, propertyName, key, ctx) =>
        {
            XamlReader.BuilderSetDynamicResource(target, propertyName, key, CtxOf(ctx));
        };

        XamlBuilder.SetTemplateBindingImpl = static (target, propertyName, sourcePropertyName, ctx) =>
        {
            XamlReader.BuilderSetTemplateBinding(target, propertyName, sourcePropertyName, CtxOf(ctx));
        };

        XamlBuilder.SetRazorBindingImpl = static (target, propertyName, expression, dependencies, ctx) =>
        {
            RazorBindingEngine.ApplySgLoweredExpression(target, propertyName, expression, dependencies, CtxOf(ctx));
        };

        XamlBuilder.SetContentRazorBindingImpl = static (target, expression, dependencies, ctx) =>
        {
            RazorBindingEngine.ApplySgLoweredContentExpression(target, expression, dependencies, CtxOf(ctx));
        };

        XamlBuilder.SetRazorIfVisibilityImpl = static (child, condition, dependencies, ctx) =>
        {
            RazorBindingEngine.ApplySgLoweredIfVisibility(child, condition, dependencies, CtxOf(ctx));
        };

        XamlBuilder.RegisterRazorSectionImpl = static (name, factory) =>
        {
            RazorExpressionRegistry.RegisterSectionFactory(name, factory);
        };
    }

    /// <summary>
    /// Cast the opaque <see cref="XamlBuildContext.Inner"/> back into the internal parser
    /// context. The contract is enforced by <see cref="XamlBuilder.BeginComponent"/> only
    /// returning contexts created via <see cref="XamlReader.CreateBuilderContext"/>.
    /// </summary>
    private static XamlParserContext CtxOf(XamlBuildContext ctx)
    {
        if (ctx.Inner is XamlParserContext inner)
            return inner;
        throw new InvalidOperationException("XamlBuildContext was not produced by Jalium.UI.Xaml.");
    }
}
