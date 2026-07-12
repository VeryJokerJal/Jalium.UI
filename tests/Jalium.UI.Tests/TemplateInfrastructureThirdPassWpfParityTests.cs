using System.ComponentModel.Design.Serialization;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Xaml;
using Jalium.UI.Resources;
using CanonicalTemplateBindingExpression = Jalium.UI.TemplateBindingExpression;
using CanonicalTemplateBindingExtension = Jalium.UI.TemplateBindingExtension;

namespace Jalium.UI.Tests;

#pragma warning disable CS0618

public class TemplateInfrastructureThirdPassWpfParityTests
{
    [Fact]
    public void FrameworkElementFactory_BuildsAndSealsLegacyTemplateTree()
    {
        var root = new FrameworkElementFactory(typeof(FrameworkElement), "Root");
        var child = new FrameworkElementFactory(typeof(UIElement), "Child");
        root.SetValue(UIElement.OpacityProperty, 0.5);
        root.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)));
        root.AppendChild(child);

        Assert.Same(child, root.FirstChild);
        Assert.Same(root, child.Parent);
        Assert.Null(child.NextSibling);

        root.Seal();
        Assert.True(root.IsSealed);
        Assert.True(child.IsSealed);
        Assert.Throws<InvalidOperationException>(() => root.Name = "Changed");
    }

    [Fact]
    public void ItemsPanelTemplate_AcceptsFrameworkElementFactoryAndUsesFrameworkTemplateLifecycle()
    {
        var root = new FrameworkElementFactory(typeof(Controls.StackPanel));
        var template = new Controls.ItemsPanelTemplate(root);

        Assert.IsAssignableFrom<FrameworkTemplate>(template);
        Assert.IsType<Controls.StackPanel>(template.CreatePanel());
        template.Seal();
        Assert.True(template.IsSealed);
    }

    [Fact]
    public void TemplateBindingExtension_ProvidesExpressionAndInstallsBinding()
    {
        var target = new ProbeElement();
        var extension = new CanonicalTemplateBindingExtension(UIElement.OpacityProperty)
        {
            ConverterParameter = "parameter",
        };
        var serviceProvider = new TargetServiceProvider(target, UIElement.OpacityProperty);

        var expression = Assert.IsType<CanonicalTemplateBindingExpression>(extension.ProvideValue(serviceProvider));
        Assert.Same(extension, expression.TemplateBindingExtension);
        Assert.NotNull(target.GetBindingExpression(UIElement.OpacityProperty));

        var descriptor = Assert.IsType<InstanceDescriptor>(
            new TemplateBindingExtensionConverter().ConvertTo(
                null,
                null,
                extension,
                typeof(InstanceDescriptor)));
        var roundTripped = Assert.IsType<CanonicalTemplateBindingExtension>(descriptor.Invoke());
        Assert.Same(UIElement.OpacityProperty, roundTripped.Property);
    }

    [Fact]
    public void SourceChangedEventArgs_PreserveBothSourcesAndInputParents()
    {
        var element = new ProbeElement();
        var oldParent = new ProbeElement();
        var args = new SourceChangedEventArgs(null, null, element, oldParent);

        Assert.Null(args.OldSource);
        Assert.Null(args.NewSource);
        Assert.Same(element, args.Element);
        Assert.Same(oldParent, args.OldParent);
        Assert.True(typeof(SourceChangedEventHandler).IsSubclassOf(typeof(MulticastDelegate)));
    }

    [Fact]
    public void TemplateContentLoader_RoundTripsDeferredReader()
    {
        var loader = new TemplateContentLoader();
        var reader = new XamlObjectReader(new ProbeElement());
        var services = new EmptyServiceProvider();

        object deferred = loader.Load(reader, services);
        Assert.Same(reader, loader.Save(deferred, services));
    }

    [Fact]
    public void ResourceCompatibilityTypes_PreserveAssemblyPathAndWpfMimeConstant()
    {
        var attribute = new AssemblyAssociatedContentFileAttribute("assets/theme.xaml");
        Assert.Equal("assets/theme.xaml", attribute.RelativeContentFilePath);
        Assert.Equal("applicaton/xaml+xml", ContentTypes.XamlContentType);
    }

    private sealed class ProbeElement : FrameworkElement
    {
    }

    private sealed class TargetServiceProvider : IServiceProvider, IProvideValueTarget
    {
        internal TargetServiceProvider(object targetObject, object targetProperty)
        {
            TargetObject = targetObject;
            TargetProperty = targetProperty;
        }

        public object TargetObject { get; }

        public object TargetProperty { get; }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

#pragma warning restore CS0618
