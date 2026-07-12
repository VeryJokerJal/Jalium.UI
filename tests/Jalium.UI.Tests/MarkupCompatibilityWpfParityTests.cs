using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Xml;
using Jalium.UI.Markup;
using Jalium.UI.Markup.Primitives;
using Jalium.UI.Xaml;
using CanonicalContentPropertyAttribute = Jalium.UI.Markup.ContentPropertyAttribute;
using CanonicalDesignerSerializationOptions = Jalium.UI.Markup.DesignerSerializationOptions;
using CanonicalDesignerSerializationOptionsAttribute = Jalium.UI.Markup.DesignerSerializationOptionsAttribute;
using MarkupXamlWriter = Jalium.UI.Markup.XamlWriter;

namespace Jalium.UI.Tests;

public sealed class MarkupCompatibilityWpfParityTests
{
    [Fact]
    public void CanonicalAttributesRetainConstructorMetadata()
    {
        Assert.Null(new CanonicalContentPropertyAttribute().Name);
        Assert.Equal(nameof(AttributeSample.Content), new CanonicalContentPropertyAttribute(nameof(AttributeSample.Content)).Name);
        Assert.Equal("value", new ConstructorArgumentAttribute("value").ArgumentName);
        Assert.Equal(('(', ')'), (new MarkupExtensionBracketCharactersAttribute('(', ')').OpeningBracket, new MarkupExtensionBracketCharactersAttribute('(', ')').ClosingBracket));
        Assert.Equal(CanonicalDesignerSerializationOptions.SerializeAsAttribute, new CanonicalDesignerSerializationOptionsAttribute(CanonicalDesignerSerializationOptions.SerializeAsAttribute).DesignerSerializationOptions);
    }

    [Fact]
    public void ValueSerializersRoundTripDateTimeAndComponentModelValues()
    {
        DateTime expected = new(2026, 7, 12, 9, 8, 7, DateTimeKind.Utc);
        ValueSerializer dateSerializer = Assert.IsType<DateTimeValueSerializer>(ValueSerializer.GetSerializerFor(typeof(DateTime)));
        string serialized = dateSerializer.ConvertToString(expected, null);
        Assert.Equal(expected, dateSerializer.ConvertFromString(serialized, null));

        ValueSerializer uriSerializer = Assert.IsAssignableFrom<ValueSerializer>(ValueSerializer.GetSerializerFor(typeof(Uri)));
        var uri = new Uri("https://jalium.dev/docs");
        Assert.Equal(uri, uriSerializer.ConvertFromString(uriSerializer.ConvertToString(uri, null), null));

        PropertyDescriptor descriptor = TypeDescriptor.GetProperties(typeof(AttributeSample))[nameof(AttributeSample.Content)]!;
        Assert.NotNull(ValueSerializer.GetSerializerFor(descriptor));
    }

    [Fact]
    public void XmlnsDictionarySupportsScopesResolversAndSealing()
    {
        var namespaces = new XmlnsDictionary { [""] = "urn:default", ["x"] = XamlLanguage.Xaml2006Namespace };
        namespaces.PushScope();
        namespaces["local"] = "urn:local";
        Assert.Equal("urn:local", namespaces.GetNamespace("local"));
        Assert.Contains(namespaces.GetNamespacePrefixes(), item => item.Prefix == "x");
        namespaces.PopScope();
        Assert.Null(namespaces.LookupNamespace("local"));

        var entries = new DictionaryEntry[namespaces.Count];
        namespaces.CopyTo(entries, 0);
        Assert.Contains(entries, item => Equals(item.Key, "x"));
        namespaces.Seal();
        Assert.True(namespaces.Sealed);
        Assert.Throws<InvalidOperationException>(() => namespaces.Add("p", "urn:sealed"));
    }

    [Fact]
    public void MarkupXamlWriterUsesXmlWriterAndDesignerManager()
    {
        var output = new StringBuilder();
        using (XmlWriter xml = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            var manager = new XamlDesignerSerializationManager(xml);
            MarkupXamlWriter.Save(new AttributeSample { Content = "saved" }, manager);
        }

        var document = new XmlDocument();
        document.LoadXml(output.ToString());
        Assert.Equal(nameof(AttributeSample), document.DocumentElement!.Name);
        Assert.Equal("saved", document.DocumentElement.GetAttribute(nameof(AttributeSample.Content)));
    }

    [Fact]
    public void ReferenceAndNameConverterUseXamlNameServices()
    {
        var target = new object();
        var names = new NameServices(("target", target));

        Assert.Same(target, new Reference("target").ProvideValue(names));
        var converter = new NameReferenceConverter();
        Assert.Same(target, converter.ConvertFrom(names, CultureInfo.InvariantCulture, "target"));
        Assert.Equal("target", converter.ConvertTo(names, CultureInfo.InvariantCulture, target, typeof(string)));
    }

    [Fact]
    public void DependencyPropertyAndRoutedEventConvertersResolveOwnerMembers()
    {
        var context = new ConverterContext(typeof(MarkupOwner));
        var dependencyPropertyConverter = new DependencyPropertyConverter();
        Assert.Same(MarkupOwner.ValueProperty, dependencyPropertyConverter.ConvertFrom(context, CultureInfo.InvariantCulture, "local:MarkupOwner.Value"));
        Assert.Equal("MarkupOwner.Value", dependencyPropertyConverter.ConvertTo(context, CultureInfo.InvariantCulture, MarkupOwner.ValueProperty, typeof(string)));

        var routedEventConverter = new RoutedEventConverter();
        Assert.Same(MarkupOwner.ActivatedEvent, routedEventConverter.ConvertFrom(context, CultureInfo.InvariantCulture, "local:MarkupOwner.Activated"));

        var destination = new DestinationContext(typeof(int));
        Assert.Equal(42, new SetterTriggerConditionValueConverter().ConvertFrom(destination, CultureInfo.InvariantCulture, "42"));
    }

    [Fact]
    public void XmlAttributePropertiesRoundTripAttachedValues()
    {
        var owner = new MarkupOwner();
        var namespaces = new XmlnsDictionary { ["local"] = "urn:local" };
        var maps = new Hashtable { ["urn:local"] = typeof(MarkupOwner) };

        XmlAttributeProperties.SetXmlnsDictionary(owner, namespaces);
        XmlAttributeProperties.SetXmlNamespaceMaps(owner, maps);
        XmlAttributeProperties.SetXmlnsDefinition(owner, "urn:local");
        XmlAttributeProperties.SetXmlSpace(owner, "preserve");

        Assert.Same(namespaces, XmlAttributeProperties.GetXmlnsDictionary(owner));
        Assert.Same(maps, XmlAttributeProperties.GetXmlNamespaceMaps(owner));
        Assert.Equal("urn:local", XmlAttributeProperties.GetXmlnsDefinition(owner));
        Assert.Equal("preserve", XmlAttributeProperties.GetXmlSpace(owner));
    }

    [Fact]
    public void MarkupPrimitivesExposeSerializablePropertiesAndSchemaDefinitions()
    {
        MarkupObject markup = MarkupWriter.GetMarkupObjectFor(new AttributeSample { Content = "text" });
        MarkupProperty content = Assert.Single(markup.Properties, item => item.Name == nameof(AttributeSample.Content));
        Assert.True(content.IsContent);
        Assert.True(content.IsValueAsString);
        Assert.Equal("text", content.StringValue);

        var definition = new PropertyDefinition
        {
            Name = "Title",
            Modifier = "public",
            Type = new XamlSchemaContext().GetXamlType(typeof(string)),
        };
        definition.Attributes.Add(new DefaultValueAttribute("untitled"));
        Assert.Equal("Title", definition.Name);
        Assert.Single(definition.Attributes);
        Assert.Equal("<data />", new XData { Text = "<data />" }.Text);
    }

    [CanonicalContentProperty(nameof(Content))]
    public sealed class AttributeSample
    {
        public string? Content { get; set; }
    }

    public sealed class MarkupOwner : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(MarkupOwner), new PropertyMetadata(0));
        public static readonly RoutedEvent ActivatedEvent = EventManager.RegisterRoutedEvent(
            "Activated", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MarkupOwner));
        public int Value { get => (int)(GetValue(ValueProperty) ?? 0); set => SetValue(ValueProperty, value); }
    }

    private sealed class NameServices : ITypeDescriptorContext, IServiceProvider, Jalium.UI.Xaml.IXamlNameResolver, Jalium.UI.Xaml.IXamlNameProvider
    {
        private readonly Dictionary<string, object> _names;
        public NameServices(params (string Name, object Value)[] values) => _names = values.ToDictionary(item => item.Name, item => item.Value);
        public IContainer? Container => null;
        public object? Instance => null;
        public PropertyDescriptor? PropertyDescriptor => null;
        public bool IsFixupTokenAvailable => true;
        public event EventHandler? OnNameScopeInitializationComplete { add { } remove { } }
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(this) ? this : null;
        public IEnumerable<KeyValuePair<string, object>> GetAllNamesAndValuesInScope() => _names;
        public object GetFixupToken(IEnumerable<string> names) => names.ToArray();
        public object GetFixupToken(IEnumerable<string> names, bool canAssignDirectly) => names.ToArray();
        public object? Resolve(string name) => _names.GetValueOrDefault(name);
        public object? Resolve(string name, out bool isFullyInitialized) { isFullyInitialized = true; return Resolve(name); }
        public string? GetName(object value) => _names.FirstOrDefault(item => ReferenceEquals(item.Value, value)).Key;
        public bool OnComponentChanging() => true;
        public void OnComponentChanged() { }
    }

    private sealed class ConverterContext : ITypeDescriptorContext, IXamlTypeResolver
    {
        private readonly Type _ownerType;
        public ConverterContext(Type ownerType) => _ownerType = ownerType;
        public IContainer? Container => null;
        public object? Instance => null;
        public PropertyDescriptor? PropertyDescriptor => null;
        public object? GetService(Type serviceType) => serviceType == typeof(IXamlTypeResolver) ? this : null;
        public Type Resolve(string qualifiedTypeName) => _ownerType;
        public bool OnComponentChanging() => true;
        public void OnComponentChanged() { }
    }

    private sealed class DestinationContext : ITypeDescriptorContext, IDestinationTypeProvider
    {
        private readonly Type _destinationType;
        public DestinationContext(Type destinationType) => _destinationType = destinationType;
        public IContainer? Container => null;
        public object? Instance => null;
        public PropertyDescriptor? PropertyDescriptor => null;
        public object? GetService(Type serviceType) => serviceType == typeof(IDestinationTypeProvider) ? this : null;
        public Type GetDestinationType() => _destinationType;
        public bool OnComponentChanging() => true;
        public void OnComponentChanged() { }
    }
}
