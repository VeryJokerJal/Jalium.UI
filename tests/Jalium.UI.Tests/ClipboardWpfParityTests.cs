using System.Collections.Specialized;
using System.Reflection;
using System.Reflection.Metadata;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;

namespace Jalium.UI.Tests;

public sealed class ClipboardWpfParityTests
{
    [Fact]
    public void CanonicalSurface_UsesWpfReturnAndParameterTypes()
    {
        Assert.Equal("Jalium.UI", typeof(Clipboard).Namespace);
        AssertMethod(nameof(Clipboard.Clear), typeof(void));
        AssertMethod(nameof(Clipboard.ContainsAudio), typeof(bool));
        AssertMethod(nameof(Clipboard.ContainsData), typeof(bool), typeof(string));
        AssertMethod(nameof(Clipboard.ContainsText), typeof(bool), typeof(TextDataFormat));
        AssertMethod(nameof(Clipboard.Flush), typeof(void));
        AssertMethod(nameof(Clipboard.GetAudioStream), typeof(Stream));
        AssertMethod(nameof(Clipboard.GetData), typeof(object), typeof(string));
        AssertMethod(nameof(Clipboard.GetDataObject), typeof(IDataObject));
        AssertMethod(nameof(Clipboard.GetFileDropList), typeof(StringCollection));
        AssertMethod(nameof(Clipboard.GetImage), typeof(BitmapSource));
        AssertMethod(nameof(Clipboard.GetText), typeof(string));
        AssertMethod(nameof(Clipboard.GetText), typeof(string), typeof(TextDataFormat));
        AssertMethod(nameof(Clipboard.IsCurrent), typeof(bool), typeof(IDataObject));
        AssertMethod(nameof(Clipboard.SetAudio), typeof(void), typeof(byte[]));
        AssertMethod(nameof(Clipboard.SetAudio), typeof(void), typeof(Stream));
        AssertMethod(nameof(Clipboard.SetData), typeof(void), typeof(string), typeof(object));
        AssertMethod(nameof(Clipboard.SetDataObject), typeof(void), typeof(object));

        var copyOverload = AssertMethod(
            nameof(Clipboard.SetDataObject),
            typeof(void),
            typeof(object),
            typeof(bool));
        Assert.False(copyOverload.GetParameters()[1].HasDefaultValue);

        AssertMethod(nameof(Clipboard.SetFileDropList), typeof(void), typeof(StringCollection));
        AssertMethod(nameof(Clipboard.SetImage), typeof(void), typeof(BitmapSource));
        AssertMethod(nameof(Clipboard.SetText), typeof(void), typeof(string));
        AssertMethod(nameof(Clipboard.SetText), typeof(void), typeof(string), typeof(TextDataFormat));
    }

    [Fact]
    public void CustomData_RoundTripsThroughTypedClipboardAndTracksOwnership()
    {
        const string format = "Jalium.UI.Tests.Clipboard.Payload";
        var payload = new Payload("alpha", 42);
        var dataObject = new DataObject();
        dataObject.SetData(format, payload);

        Clipboard.SetDataObject(dataObject, copy: true);

        Assert.True(Clipboard.IsCurrent(dataObject));
        Assert.True(Clipboard.ContainsData(format));
        Assert.Same(payload, Clipboard.GetData(format));
        Assert.True(Clipboard.TryGetData(format, out Payload? roundTrip));
        Assert.Same(payload, roundTrip);
        Assert.True(Clipboard.TryGetData(
            format,
            static (TypeName _) => typeof(Payload),
            out Payload? resolved));
        Assert.Same(payload, resolved);
    }

    [Fact]
    public void JsonData_RoundTripsWithoutReducingTheValueToText()
    {
        const string format = "Jalium.UI.Tests.Clipboard.JsonPayload";
        var payload = new Payload("json", 7);

        Clipboard.SetDataAsJson(format, payload);

        Assert.True(Clipboard.ContainsData(format));
        Assert.True(Clipboard.TryGetData(format, out Payload? roundTrip));
        Assert.Equal(payload, roundTrip);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void InvalidTextDataFormat_IsRejected(int value)
    {
        var format = (TextDataFormat)value;
        Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => Clipboard.ContainsText(format));
        Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => Clipboard.GetText(format));
        Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => Clipboard.SetText("value", format));
    }

    private static MethodInfo AssertMethod(string name, Type returnType, params Type[] parameterTypes)
    {
        var method = typeof(Clipboard).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null,
            parameterTypes,
            null);
        Assert.NotNull(method);
        Assert.Equal(returnType, method!.ReturnType);
        return method;
    }

    private sealed record Payload(string Name, int Count);
}
