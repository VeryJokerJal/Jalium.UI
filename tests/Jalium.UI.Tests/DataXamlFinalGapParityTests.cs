using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Jalium.UI.Baml2006;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using MappedXamlLineInfo = Jalium.UI.Xaml.IXamlLineInfo;
using MappedXamlNodeType = Jalium.UI.Xaml.XamlNodeType;
using MappedXamlReader = Jalium.UI.Xaml.XamlReader;
using MappedXamlReaderSettings = Jalium.UI.Xaml.XamlReaderSettings;

namespace Jalium.UI.Tests;

public sealed class DataXamlFinalGapParityTests
{
    [Fact]
    public void AlternationConverterHasCanonicalShapeAndBidirectionalModuloBehavior()
    {
        Type type = typeof(AlternationConverter);
        Assert.False(type.IsSealed);
        Assert.True(typeof(IValueConverter).IsAssignableFrom(type));
        Assert.Equal(typeof(IList), type.GetProperty(nameof(AlternationConverter.Values))!.PropertyType);
        Assert.NotNull(type.GetMethod(
            nameof(AlternationConverter.Convert),
            [typeof(object), typeof(Type), typeof(object), typeof(CultureInfo)]));
        Assert.NotNull(type.GetMethod(
            nameof(AlternationConverter.ConvertBack),
            [typeof(object), typeof(Type), typeof(object), typeof(CultureInfo)]));

        var converter = new AlternationConverter();
        converter.Values.Add("zero");
        converter.Values.Add("one");
        converter.Values.Add("two");

        Assert.Equal("one", converter.Convert(4, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("two", converter.Convert(-1, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Same(
            DependencyProperty.UnsetValue,
            converter.Convert("not-an-index", typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(1, converter.ConvertBack("one", typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Equal(-1, converter.ConvertBack("missing", typeof(int), null, CultureInfo.InvariantCulture));
        Assert.Equal("zero", converter.Convert(3, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LocalizationStoresValuesForDependencyObjectsAndPlainObjects()
    {
        AssertObjectParameter(nameof(Localization.GetAttributes), parameterCount: 1);
        AssertObjectParameter(nameof(Localization.GetComments), parameterCount: 1);
        AssertObjectParameter(nameof(Localization.SetAttributes), parameterCount: 2);
        AssertObjectParameter(nameof(Localization.SetComments), parameterCount: 2);

        var dependencyObject = new DependencyObject();
        Localization.SetAttributes(dependencyObject, "Category(Text)");
        Localization.SetComments(dependencyObject, "Visible label");
        Assert.Equal("Category(Text)", Localization.GetAttributes(dependencyObject));
        Assert.Equal("Visible label", Localization.GetComments(dependencyObject));
        Assert.Equal("Category(Text)", dependencyObject.GetValue(Localization.AttributesProperty));

        object plainObject = new();
        Assert.Equal(string.Empty, Localization.GetAttributes(plainObject));
        Localization.SetAttributes(plainObject, "Readable");
        Localization.SetComments(plainObject, "Translator note");
        Localization.SetComments(plainObject, "Updated note");
        Assert.Equal("Readable", Localization.GetAttributes(plainObject));
        Assert.Equal("Updated note", Localization.GetComments(plainObject));

        Assert.Throws<ArgumentNullException>(() => Localization.GetComments(null!));
        Assert.Throws<ArgumentNullException>(() => Localization.SetAttributes(plainObject, null!));
    }

    [Fact]
    public void BamlReaderUsesMappedXamlContractsAndProducesLineAwareNodes()
    {
        Assert.Equal("Jalium.UI.Baml2006", typeof(Baml2006Reader).Namespace);
        Assert.False(typeof(Baml2006ReaderSettings).IsPublic);
        Assert.Null(typeof(Baml2006Reader).Assembly.GetType("Jalium.UI.Markup.Baml2006Reader"));
        Assert.Equal(typeof(MappedXamlReader), typeof(Baml2006Reader).BaseType);
        Assert.True(typeof(MappedXamlLineInfo).IsAssignableFrom(typeof(Baml2006Reader)));
        Assert.NotNull(typeof(Baml2006Reader).GetConstructor(
            [typeof(Stream), typeof(MappedXamlReaderSettings)]));

        const string markup = """
            <Root xmlns="urn:parity" Name="demo">
              <Child>hello</Child>
            </Root>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markup));
        var settings = new MappedXamlReaderSettings
        {
            ProvideLineInfo = true,
            LocalAssembly = typeof(DataXamlFinalGapParityTests).Assembly,
        };
        using (var reader = new Baml2006Reader(stream, settings))
        {
            Assert.False(reader.IsEof);
            Assert.Same(settings.LocalAssembly, reader.SchemaContext.ReferenceAssemblies[0]);

            var nodes = new List<(MappedXamlNodeType NodeType, string? Payload)>();
            while (reader.Read())
            {
                var lineInfo = (MappedXamlLineInfo)reader;
                Assert.True(lineInfo.HasLineInfo);
                Assert.True(lineInfo.LineNumber > 0);
                nodes.Add((
                    reader.NodeType,
                    reader.Namespace?.Namespace ??
                    reader.Type?.Name ??
                    reader.Member?.Name ??
                    reader.Value as string));
            }

            Assert.True(reader.IsEof);
            Assert.Contains((MappedXamlNodeType.NamespaceDeclaration, "urn:parity"), nodes);
            Assert.Contains((MappedXamlNodeType.StartObject, "Root"), nodes);
            Assert.Contains((MappedXamlNodeType.StartObject, "Child"), nodes);
            Assert.Contains((MappedXamlNodeType.StartMember, "Name"), nodes);
            Assert.Contains((MappedXamlNodeType.Value, "demo"), nodes);
            Assert.Contains((MappedXamlNodeType.Value, "hello"), nodes);
            Assert.Equal(2, nodes.Count(node => node.NodeType == MappedXamlNodeType.EndObject));
        }

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void BamlReaderHonorsOwnershipAndRejectsPrivateBinaryBamlExplicitly()
    {
        var ownedStream = new MemoryStream(Encoding.UTF8.GetBytes("<Root />"));
        using (var reader = new Baml2006Reader(
            ownedStream,
            new Baml2006ReaderSettings { OwnsStream = true }))
        {
            Assert.True(reader.Read());
        }
        Assert.False(ownedStream.CanRead);

        using var binaryStream = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        using var binaryReader = new Baml2006Reader(binaryStream);
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => binaryReader.Read());
        Assert.Contains("binary BAML", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertObjectParameter(string methodName, int parameterCount)
    {
        MethodInfo method = typeof(Localization).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == parameterCount);
        Assert.Equal(typeof(object), method.GetParameters()[0].ParameterType);
    }
}
