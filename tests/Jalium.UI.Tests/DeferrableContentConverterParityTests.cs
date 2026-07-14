using System.ComponentModel;

namespace Jalium.UI.Tests;

public sealed class DeferrableContentConverterParityTests
{
    [Fact]
    public void Converter_AcceptsStreamAndByteArrayAndOwnsThePayload()
    {
        var converter = new DeferrableContentConverter();
        Assert.True(converter.CanConvertFrom(typeof(Stream)));
        Assert.True(converter.CanConvertFrom(typeof(byte[])));

        byte[] source = [1, 2, 3, 4];
        var context = new DictionaryTypeDescriptorContext(new ResourceDictionary());
        var content = Assert.IsType<DeferrableContent>(converter.ConvertFrom(context, null, source));
        source[0] = 99;

        using Stream payload = content.OpenRead();
        Assert.Equal([1, 2, 3, 4], ReadAll(payload));
    }

    [Fact]
    public void Converter_RequiresResourceDictionaryContextAndBinaryInput()
    {
        var converter = new DeferrableContentConverter();
        Assert.Throws<ArgumentNullException>(() => converter.ConvertFrom(null, null, Array.Empty<byte>()));
        Assert.Throws<InvalidOperationException>(() =>
            converter.ConvertFrom(new DictionaryTypeDescriptorContext(new object()), null, Array.Empty<byte>()));
        Assert.Throws<InvalidOperationException>(() =>
            converter.ConvertFrom(new DictionaryTypeDescriptorContext(new ResourceDictionary()), null, "not binary"));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class DictionaryTypeDescriptorContext(object instance) : ITypeDescriptorContext
    {
        public IContainer? Container => null;
        public object Instance { get; } = instance;
        public PropertyDescriptor? PropertyDescriptor => null;
        public object? GetService(Type serviceType) => null;
        public void OnComponentChanged() { }
        public bool OnComponentChanging() => true;
    }
}
