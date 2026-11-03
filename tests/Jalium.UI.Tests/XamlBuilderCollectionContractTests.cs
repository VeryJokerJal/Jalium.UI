using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class XamlBuilderCollectionContractTests
{
    private static XamlBuildContext CreateContext(object root)
    {
        XamlBuilderInitializer.Register();
        return XamlBuilder.BeginComponent(root,
            sourceAssembly: typeof(XamlBuilderCollectionContractTests).Assembly);
    }

    [Fact]
    public void CompiledChildren_PreserveVisualParentAndDeclarationOrder()
    {
        foreach (var panel in new Panel[] { new Grid(), new DockPanel(), new StackPanel(), new WrapPanel(), new Canvas() })
        {
            var context = CreateContext(panel);
            var first = new Border();
            var second = new Border();
            XamlBuilder.AddChild(panel, first, context);
            XamlBuilder.AddChild(panel, second, context);

            Assert.Equal(new UIElement[] { first, second }, panel.Children.Cast<UIElement>().ToArray());
            Assert.Same(panel, first.VisualParent);
            Assert.Same(panel, second.VisualParent);
        }
    }

    [Fact]
    public void DerivedContentProperty_OverridesTheInheritedPanelCollection()
    {
        var panel = new AlternateContentGrid();
        var child = new Border();
        XamlBuilder.AddChild(panel, child, CreateContext(panel));

        Assert.Same(child, Assert.Single(panel.Items));
        Assert.Empty(panel.Children);
        Assert.Null(child.VisualParent);
    }

    [Fact]
    public void CompiledStyleAndTemplateCollections_PreserveObjectsAndOrdering()
    {
        var style = new Style();
        var context = CreateContext(style);
        var first = new Setter { Property = FrameworkElement.WidthProperty, Value = 80.0 };
        var second = new Setter { Property = FrameworkElement.HeightProperty, Value = 30.0 };
        XamlBuilder.AddChild(style, first, context);
        XamlBuilder.ApplyPropertyElementChild(style, nameof(Style.Setters), second, context);
        Assert.Equal(new SetterBase[] { first, second }, style.Setters);

        var template = new ControlTemplate();
        var trigger = new MultiTrigger();
        var condition = new Condition { Property = UIElement.IsMouseOverProperty, Value = true };
        XamlBuilder.ApplyPropertyElementChild(trigger, nameof(MultiTrigger.Conditions), condition, context);
        XamlBuilder.AddChild(trigger, first, context);
        XamlBuilder.ApplyPropertyElementChild(template, nameof(ControlTemplate.Triggers), trigger, context);
        Assert.Same(trigger, Assert.Single(template.Triggers));
        Assert.Same(condition, Assert.Single(trigger.Conditions));
        Assert.Same(first, Assert.Single(trigger.Setters));
    }

    [Fact]
    public void DerivedCollections_AreResolvedOnTheRuntimeType()
    {
        var style = new AlternateCollectionStyle();
        var context = CreateContext(style);
        var setter = new Setter { Property = FrameworkElement.WidthProperty, Value = 12.0 };
        XamlBuilder.AddChild(style, setter, context);
        Assert.Same(setter, Assert.Single(style.Setters));
        Assert.Empty(((Style)style).Setters);

        var owner = new AlternateMergedDictionary();
        var child = new ResourceDictionary();
        XamlBuilder.ApplyPropertyElementChild(owner, nameof(ResourceDictionary.MergedDictionaries), child, context);
        Assert.Same(child, Assert.Single(owner.MergedDictionaries));
        Assert.Empty(((ResourceDictionary)owner).MergedDictionaries);
    }

    [Fact]
    public void CollectionMarkupExtension_StillReceivesItsOriginalTargetMember()
    {
        var owner = new ResourceDictionary();
        var child = new ResourceDictionary { ["Value"] = 17 };
        var extension = new InspectTargetExtension(child);
        XamlBuilder.ApplyPropertyElementChild(owner, nameof(ResourceDictionary.MergedDictionaries),
            extension, CreateContext(owner));

        Assert.Same(owner, extension.TargetObject);
        var property = Assert.IsAssignableFrom<PropertyInfo>(extension.TargetProperty);
        Assert.Equal(nameof(ResourceDictionary.MergedDictionaries), property.Name);
        Assert.Same(child, Assert.Single(owner.MergedDictionaries));
        Assert.Equal(17, owner["Value"]);
    }

    [Fact]
    public void DictionarySource_LoadsExistingResourcesAndHonorsReadOnlyDerivedSource()
    {
        ThemeLoader.Initialize();
        var owner = new ResourceDictionary();
        var context = CreateContext(owner);
        var loaded = Assert.IsAssignableFrom<ResourceDictionary>(
            XamlBuilder.SetProperty(owner, nameof(ResourceDictionary.Source), "/TestAssets/Colors.xaml", context));
        Assert.Equal(Color.FromArgb(0xFF, 0x1E, 0x88, 0xE5),
            Assert.IsType<SolidColorBrush>(loaded["TestAccentBrush"]).Color);

        var readOnly = new ReadOnlySourceDictionary();
        Assert.Same(readOnly, XamlBuilder.SetProperty(readOnly,
            nameof(ResourceDictionary.Source), "/TestAssets/Colors.xaml", context));
        Assert.Null(((ResourceDictionary)readOnly).Source);
        Assert.False(readOnly.Contains("TestAccentBrush"));
    }

    [Fact]
    public void PropertyDeclarations_PreserveResolutionDeferralAndSetterExceptions()
    {
        var style = new Style { TargetType = typeof(Border) };
        var context = CreateContext(style);
        XamlBuilder.PushParent(style, context);
        try
        {
            var setter = new Setter();
            XamlBuilder.SetProperty(setter, nameof(Setter.Property), "Width", context);
            Assert.Same(FrameworkElement.WidthProperty, setter.Property);

            var deferred = new Setter();
            XamlBuilder.SetProperty(deferred, nameof(Setter.Property), "UnregisteredProperty", context);
            Assert.Null(deferred.Property);
            Assert.Equal("UnregisteredProperty", deferred.PropertyName);

            var derived = new AlternatePropertySetter();
            XamlBuilder.SetProperty(derived, nameof(Setter.Property), "Width", context);
            Assert.Same(FrameworkElement.WidthProperty, derived.Property);
            Assert.Null(((Setter)derived).Property);

            var error = Assert.Throws<TargetInvocationException>(() =>
                XamlBuilder.SetProperty(new Setter(), nameof(Setter.Property), "IsMouseOver", context));
            Assert.IsType<ArgumentException>(error.InnerException);
        }
        finally
        {
            XamlBuilder.PopParent(context);
        }
    }

    [ContentProperty(nameof(Items))]
    public sealed class AlternateContentGrid : Grid
    {
        public List<UIElement> Items { get; } = [];
    }

    public sealed class AlternateCollectionStyle : Style
    {
        public new SetterBaseCollection Setters { get; } = new();
    }

    public sealed class AlternateMergedDictionary : ResourceDictionary
    {
        public new System.Collections.ObjectModel.Collection<ResourceDictionary> MergedDictionaries { get; } = new();
    }

    public sealed class ReadOnlySourceDictionary : ResourceDictionary
    {
        public new Uri? Source => null;
    }

    public sealed class AlternatePropertySetter : Setter
    {
        public new DependencyProperty? Property { get; set; }
    }

    private sealed class InspectTargetExtension(object value) : MarkupExtension
    {
        public object? TargetObject { get; private set; }
        public object? TargetProperty { get; private set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var target = Assert.IsAssignableFrom<IProvideValueTarget>(
                serviceProvider.GetService(typeof(IProvideValueTarget)));
            TargetObject = target.TargetObject;
            TargetProperty = target.TargetProperty;
            return value;
        }
    }
}
