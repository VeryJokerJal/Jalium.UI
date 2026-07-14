using System.Text;
using System.Xml;
using Jalium.UI.Xaml;
using Jalium.UI.Xaml.Schema;

namespace Jalium.UI.Tests;

public sealed class SystemXamlInfrastructureParityTests
{
    [Fact]
    public void AttachablePropertyServicesKeepsWeakPerInstanceStores()
    {
        var first = new object();
        var second = new object();
        var property = new AttachableMemberIdentifier(typeof(SystemXamlInfrastructureParityTests), "Label");

        AttachablePropertyServices.SetProperty(first, property, "one");
        AttachablePropertyServices.SetProperty(second, property, "two");

        Assert.True(AttachablePropertyServices.TryGetProperty(first, property, out string? firstValue));
        Assert.Equal("one", firstValue);
        Assert.Equal(1, AttachablePropertyServices.GetAttachedPropertyCount(first));
        Assert.True(AttachablePropertyServices.RemoveProperty(first, property));
        Assert.False(AttachablePropertyServices.TryGetProperty(first, property, out object? _));
        Assert.Equal("two", Assert.IsType<string>(GetAttachedValue(second, property)));
    }

    [Fact]
    public void NodeListAndQueuePreserveTypedNodeOrder()
    {
        var schema = new XamlSchemaContext([typeof(SampleObject).Assembly]);
        XamlType type = schema.GetXamlType(typeof(SampleObject));
        var member = new XamlMember(nameof(SampleObject.Name));
        var list = new XamlNodeList(schema);

        list.Writer.WriteStartObject(type);
        list.Writer.WriteStartMember(member);
        list.Writer.WriteValue("Jalium");
        list.Writer.WriteEndMember();
        list.Writer.WriteEndObject();

        Assert.Equal(5, list.Count);
        using XamlReader reader = list.GetReader();
        var seen = new List<XamlNodeType>();
        while (reader.Read()) seen.Add(reader.NodeType);
        Assert.Equal([XamlNodeType.StartObject, XamlNodeType.StartMember, XamlNodeType.Value, XamlNodeType.EndMember, XamlNodeType.EndObject], seen);

        var queue = new XamlNodeQueue(schema);
        queue.Writer.WriteValue(42);
        Assert.False(queue.IsEmpty);
        Assert.True(queue.Reader.Read());
        Assert.Equal(42, queue.Reader.Value);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void ObjectReaderAndWriterRoundTripSimpleGraphs()
    {
        var source = new SampleObject { Name = "node", Count = 7 };
        var schema = new XamlSchemaContext([typeof(SampleObject).Assembly]);
        using var reader = new XamlObjectReader(source, schema);
        using var writer = new XamlObjectWriter(schema);

        XamlServices.Transform(reader, writer, closeWriter: false);

        SampleObject result = Assert.IsType<SampleObject>(writer.Result);
        Assert.NotSame(source, result);
        Assert.Equal(source.Name, result.Name);
        Assert.Equal(source.Count, result.Count);
    }

    [Fact]
    public void BackgroundReaderBuffersACompleteNodeStream()
    {
        var schema = new XamlSchemaContext([typeof(SampleObject).Assembly]);
        var list = new XamlNodeList(schema);
        list.Writer.WriteStartObject(schema.GetXamlType(typeof(SampleObject)));
        list.Writer.WriteEndObject();
        using var background = new XamlBackgroundReader(list.GetReader());
        background.StartThread("system-xaml-test");

        Assert.True(background.Read());
        Assert.Equal(XamlNodeType.StartObject, background.NodeType);
        Assert.True(background.Read());
        Assert.Equal(XamlNodeType.EndObject, background.NodeType);
        Assert.False(background.Read());
        Assert.True(background.IsEof);
    }

    [Fact]
    public void TypeNamesParseGenericsAndUseNamespaceServices()
    {
        var namespaces = new NamespaceResolver(("x", XamlLanguage.Xaml2006Namespace), ("local", "urn:test"));

        XamlTypeName parsed = XamlTypeName.Parse("local:Pair(x:String, x:Int32)", namespaces);

        Assert.Equal("Pair", parsed.Name);
        Assert.Equal("urn:test", parsed.Namespace);
        Assert.Equal(["String", "Int32"], parsed.TypeArguments.Select(item => item.Name));
        Assert.Equal("local:Pair(x:String, x:Int32)", parsed.ToString(namespaces));
        Assert.Contains(XamlLanguage.Name, XamlLanguage.AllDirectives);
        Assert.Contains(XamlLanguage.String, XamlLanguage.AllTypes);
    }

    [Fact]
    public void SchemaInvokersPerformRealReflectionOperations()
    {
        var sample = new SampleObject();
        var memberInvoker = new XamlMemberInvoker(new XamlMember(nameof(SampleObject.Name)));
        memberInvoker.SetValue(sample, "updated");
        Assert.Equal("updated", memberInvoker.GetValue(sample));

        var schema = new XamlSchemaContext([typeof(List<string>).Assembly]);
        var typeInvoker = new XamlTypeInvoker(schema.GetXamlType(typeof(List<string>)));
        var values = Assert.IsType<List<string>>(typeInvoker.CreateInstance([]));
        typeInvoker.AddToCollection(values, "item");
        Assert.Equal(["item"], values);

        var converter = new XamlValueConverter<System.ComponentModel.TypeConverter>(typeof(System.ComponentModel.StringConverter), schema.GetXamlType(typeof(string)));
        Assert.IsType<System.ComponentModel.StringConverter>(converter.ConverterInstance);
    }

    [Fact]
    public void XmlWriterProducesWellFormedPropertyElementMarkup()
    {
        var schema = new XamlSchemaContext([typeof(SampleObject).Assembly]);
        var output = new StringBuilder();
        using (var xml = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true }))
        using (var writer = new XamlXmlWriter(xml, schema))
        {
            writer.WriteStartObject(schema.GetXamlType(typeof(SampleObject)));
            writer.WriteStartMember(new XamlMember(nameof(SampleObject.Name)));
            writer.WriteValue("value");
            writer.WriteEndMember();
            writer.WriteEndObject();
        }

        var document = new XmlDocument();
        document.LoadXml(output.ToString());
        Assert.Equal(nameof(SampleObject), document.DocumentElement!.Name);
        Assert.Equal("value", document.DocumentElement.InnerText);
    }

    private static object? GetAttachedValue(object instance, AttachableMemberIdentifier property)
    {
        Assert.True(AttachablePropertyServices.TryGetProperty(instance, property, out object? value));
        return value;
    }

    public sealed class SampleObject
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    private sealed class NamespaceResolver : IXamlNamespaceResolver, INamespacePrefixLookup
    {
        private readonly Dictionary<string, string> _namespaces;

        public NamespaceResolver(params (string Prefix, string Namespace)[] values)
            => _namespaces = values.ToDictionary(item => item.Prefix, item => item.Namespace, StringComparer.Ordinal);

        public string? GetNamespace(string prefix) => _namespaces.TryGetValue(prefix, out string? value) ? value : null;
        public IEnumerable<NamespaceDeclaration> GetNamespacePrefixes() => _namespaces.Select(item => new NamespaceDeclaration(item.Value, item.Key));
        public string? LookupPrefix(string ns) => _namespaces.FirstOrDefault(item => item.Value == ns).Key;
    }
}
