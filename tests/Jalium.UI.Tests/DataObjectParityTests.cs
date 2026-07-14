using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection.Metadata;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class DataObjectParityTests
{
    [Fact]
    public void RoutedEventsExposeWpfMetadata()
    {
        AssertRoutedEvent(
            DataObject.CopyingEvent,
            "Copying",
            typeof(DataObjectCopyingEventHandler));
        AssertRoutedEvent(
            DataObject.PastingEvent,
            "Pasting",
            typeof(DataObjectPastingEventHandler));
        AssertRoutedEvent(
            DataObject.SettingDataEvent,
            "SettingData",
            typeof(DataObjectSettingDataEventHandler));
    }

    [Fact]
    public void AttachedHandlersDispatchStrongEventArgsAndCanBeRemoved()
    {
        var element = new Border();
        var data = new DataObject("primary", "value");
        var copyingArgs = new DataObjectCopyingEventArgs(data, isDragDrop: true);
        var pastingArgs = new DataObjectPastingEventArgs(data, isDragDrop: false, "primary");
        var settingArgs = new DataObjectSettingDataEventArgs(data, "secondary");
        int copyingCalls = 0;
        int pastingCalls = 0;
        int settingCalls = 0;

        DataObjectCopyingEventHandler copying = (sender, args) =>
        {
            Assert.Same(element, sender);
            Assert.Same(copyingArgs, args);
            copyingCalls++;
        };
        DataObjectPastingEventHandler pasting = (sender, args) =>
        {
            Assert.Same(element, sender);
            Assert.Same(pastingArgs, args);
            pastingCalls++;
        };
        DataObjectSettingDataEventHandler setting = (sender, args) =>
        {
            Assert.Same(element, sender);
            Assert.Same(settingArgs, args);
            settingCalls++;
        };

        DataObject.AddCopyingHandler(element, copying);
        DataObject.AddPastingHandler(element, pasting);
        DataObject.AddSettingDataHandler(element, setting);

        element.RaiseEvent(copyingArgs);
        element.RaiseEvent(pastingArgs);
        element.RaiseEvent(settingArgs);

        Assert.Equal(1, copyingCalls);
        Assert.Equal(1, pastingCalls);
        Assert.Equal(1, settingCalls);

        DataObject.RemoveCopyingHandler(element, copying);
        DataObject.RemovePastingHandler(element, pasting);
        DataObject.RemoveSettingDataHandler(element, setting);

        element.RaiseEvent(new DataObjectCopyingEventArgs(data, isDragDrop: false));
        element.RaiseEvent(new DataObjectPastingEventArgs(data, isDragDrop: true, "primary"));
        element.RaiseEvent(new DataObjectSettingDataEventArgs(data, "primary"));

        Assert.Equal(1, copyingCalls);
        Assert.Equal(1, pastingCalls);
        Assert.Equal(1, settingCalls);
    }

    [Fact]
    public void AttachedHandlersRejectUnsupportedHostsAndNullArguments()
    {
        DataObjectCopyingEventHandler handler = (_, _) => { };

        Assert.Throws<ArgumentNullException>(() => DataObject.AddCopyingHandler(null!, handler));
        Assert.Throws<ArgumentNullException>(() => DataObject.AddCopyingHandler(new Border(), null!));
        Assert.Throws<ArgumentException>(() => DataObject.AddCopyingHandler(new DependencyObject(), handler));
    }

    [Fact]
    public void DataObjectEventArgsEnforceWpfInvariants()
    {
        var source = new DataObject("first", 1, autoConvert: false);
        source.SetData("second", 2, autoConvert: false);

        var copying = new DataObjectCopyingEventArgs(source, isDragDrop: true);
        Assert.Same(DataObject.CopyingEvent, copying.RoutedEvent);
        Assert.True(copying.IsDragDrop);
        Assert.False(copying.CommandCancelled);
        copying.CancelCommand();
        copying.CancelCommand();
        Assert.True(copying.CommandCancelled);

        var pasting = new DataObjectPastingEventArgs(source, isDragDrop: false, "second");
        Assert.Same(source, pasting.SourceDataObject);
        Assert.Same(source, pasting.DataObject);
        Assert.Equal("second", pasting.FormatToApply);
        pasting.FormatToApply = "first";
        Assert.Equal("first", pasting.FormatToApply);

        var replacement = new DataObject("replacement", 3, autoConvert: false);
        pasting.DataObject = replacement;
        Assert.Same(source, pasting.SourceDataObject);
        Assert.Same(replacement, pasting.DataObject);
        Assert.Equal("replacement", pasting.FormatToApply);

        var setting = new DataObjectSettingDataEventArgs(source, string.Empty);
        Assert.Same(DataObject.SettingDataEvent, setting.RoutedEvent);
        Assert.False(setting.IsDragDrop);
        Assert.Equal(string.Empty, setting.Format);

        Assert.Throws<ArgumentNullException>(() => new DataObjectCopyingEventArgs(null!, false));
        Assert.Throws<ArgumentNullException>(() => new DataObjectPastingEventArgs(null!, false, "first"));
        Assert.Throws<ArgumentNullException>(() => new DataObjectPastingEventArgs(source, false, null!));
        Assert.Throws<ArgumentException>(() => new DataObjectPastingEventArgs(source, false, string.Empty));
        Assert.Throws<ArgumentException>(() => new DataObjectPastingEventArgs(source, false, "missing"));
        Assert.Throws<ArgumentNullException>(() => pasting.DataObject = null!);
        Assert.Throws<ArgumentException>(() => pasting.DataObject = new DataObject());
        Assert.Throws<ArgumentNullException>(() => pasting.FormatToApply = null!);
        Assert.Throws<ArgumentException>(() => pasting.FormatToApply = "missing");
        Assert.Throws<ArgumentNullException>(() => new DataObjectSettingDataEventArgs(null!, "format"));
        Assert.Throws<ArgumentNullException>(() => new DataObjectSettingDataEventArgs(source, null!));
    }

    [Fact]
    public void AudioHelpersUseWaveAudioWithoutAutomaticConversion()
    {
        byte[] bytes = [1, 2, 3, 4];
        var fromBytes = new DataObject();
        fromBytes.SetAudio(bytes);

        Assert.True(fromBytes.ContainsAudio());
        Assert.Equal(bytes, Assert.IsType<MemoryStream>(fromBytes.GetAudioStream()).ToArray());
        Assert.Equal(new[] { DataFormats.WaveAudio }, fromBytes.GetFormats(autoConvert: false));

        using var stream = new MemoryStream([5, 6, 7]);
        var fromStream = new DataObject();
        fromStream.SetAudio(stream);
        Assert.Same(stream, fromStream.GetAudioStream());

        Assert.Throws<ArgumentNullException>(() => fromBytes.SetAudio((byte[])null!));
        Assert.Throws<ArgumentNullException>(() => fromBytes.SetAudio((Stream)null!));
    }

    [Theory]
    [InlineData(TextDataFormat.Text, "Text")]
    [InlineData(TextDataFormat.UnicodeText, "UnicodeText")]
    [InlineData(TextDataFormat.Rtf, "Rich Text Format")]
    [InlineData(TextDataFormat.Html, "HTML Format")]
    [InlineData(TextDataFormat.CommaSeparatedValue, "CSV")]
    [InlineData(TextDataFormat.Xaml, "Xaml")]
    public void TextHelpersUseTheRequestedNativeFormat(TextDataFormat format, string nativeFormat)
    {
        var data = new DataObject();
        data.SetText("payload", format);

        Assert.True(data.ContainsText(format));
        Assert.Equal("payload", data.GetText(format));
        Assert.Equal(new[] { nativeFormat }, data.GetFormats(autoConvert: false));
        Assert.Equal(format == TextDataFormat.UnicodeText, data.ContainsText());
        Assert.Equal(format == TextDataFormat.UnicodeText ? "payload" : string.Empty, data.GetText());
    }

    [Fact]
    public void TextHelpersRejectInvalidEnumValues()
    {
        const TextDataFormat invalid = (TextDataFormat)42;
        var data = new DataObject();

        Assert.Throws<InvalidEnumArgumentException>(() => data.ContainsText(invalid));
        Assert.Throws<InvalidEnumArgumentException>(() => data.GetText(invalid));
        Assert.Throws<InvalidEnumArgumentException>(() => data.SetText("value", invalid));
        Assert.Throws<ArgumentNullException>(() => data.SetText(null!));
    }

    [Fact]
    public void ContainsImageChecksTheNativeBitmapFormatWithoutMediaDependency()
    {
        var data = new DataObject();
        Assert.False(data.ContainsImage());

        data.SetData(DataFormats.Bitmap, new object(), autoConvert: false);
        Assert.True(data.ContainsImage());
    }

    [Fact]
    public void TypedRetrievalSupportsExactBaseAndMappedFormats()
    {
        var payload = new DerivedPayload { Value = 42 };
        var data = new DataObject("custom", payload, autoConvert: false);

        Assert.True(data.TryGetData("custom", out DerivedPayload? exact));
        Assert.Same(payload, exact);
        Assert.True(data.TryGetData("custom", out PayloadBase? asBase));
        Assert.Same(payload, asBase);
        Assert.False(data.TryGetData("custom", out string? wrong));
        Assert.Null(wrong);

        var typedByDefaultFormat = new DataObject(typeof(int), 42);
        Assert.True(typedByDefaultFormat.TryGetData(out int number));
        Assert.Equal(42, number);

        var text = new DataObject("hello");
        Assert.False(text.TryGetData(DataFormats.UnicodeText, autoConvert: false, out string? noConversion));
        Assert.Null(noConversion);
        Assert.True(text.TryGetData(DataFormats.UnicodeText, autoConvert: true, out string? converted));
        Assert.Equal("hello", converted);

        Assert.False(text.TryGetData<string>(string.Empty, out _));
        Assert.Throws<NotSupportedException>(() => text.TryGetData<int>(DataFormats.UnicodeText, out _));
    }

    [Fact]
    public void ResolverOverloadAndExtensionsFollowTypedDataObjectContract()
    {
        var data = new DataObject("custom", new DerivedPayload { Value = 7 });
        Func<TypeName, Type?> resolver = _ => typeof(DerivedPayload);

        Assert.True(data.TryGetData("custom", resolver, autoConvert: false, out DerivedPayload? value));
        Assert.NotNull(value);
        Assert.Equal(7, value.Value);
        Assert.Throws<ArgumentNullException>(() =>
            data.TryGetData<DerivedPayload>("custom", null!, autoConvert: false, out _));

        IDataObject typedAsLegacy = data;
        Assert.True(typedAsLegacy.TryGetData("custom", out DerivedPayload? extensionValue));
        Assert.NotNull(extensionValue);
        Assert.Equal(7, extensionValue.Value);

        IDataObject legacy = new LegacyDataObject();
        Assert.Throws<NotSupportedException>(() => legacy.TryGetData<string>(out _));
        Assert.Throws<ArgumentNullException>(() => DataObjectExtensions.TryGetData<string>(null!, out _));
    }

    [Fact]
    public void JsonHelpersSerializeAtSetTimeAndRoundTripTypedData()
    {
        var original = new JsonPayload { Name = "alpha", Count = 3 };
        var data = new DataObject();
        data.SetDataAsJson("custom/json", original);

        var untyped = Assert.IsType<JsonPayload>(data.GetData("custom/json"));
        Assert.NotSame(original, untyped);
        Assert.Equal(original.Name, untyped.Name);
        Assert.Equal(original.Count, untyped.Count);

        Assert.True(data.TryGetData("custom/json", out JsonPayload? typed));
        Assert.NotNull(typed);
        Assert.NotSame(original, typed);
        Assert.Equal(original.Name, typed.Name);
        Assert.Equal(original.Count, typed.Count);
        Assert.Equal(["custom/json"], data.GetFormats(autoConvert: false));

        var byType = new DataObject();
        byType.SetDataAsJson(original);
        Assert.True(byType.TryGetData(out JsonPayload? byTypeResult));
        Assert.NotNull(byTypeResult);
        Assert.Equal(original.Name, byTypeResult.Name);
        Assert.Equal(original.Count, byTypeResult.Count);
    }

    [Fact]
    public void JsonHelpersRejectInvalidAndReservedInputs()
    {
        var data = new DataObject();

        Assert.Throws<ArgumentNullException>(() => data.SetDataAsJson<JsonPayload>("custom", null!));
        Assert.Throws<ArgumentNullException>(() => data.SetDataAsJson<JsonPayload>(null!, new JsonPayload()));
        Assert.Throws<ArgumentException>(() => data.SetDataAsJson(" ", new JsonPayload()));
        Assert.Throws<ArgumentException>(() => data.SetDataAsJson(DataFormats.Text, new JsonPayload()));
        Assert.Throws<ArgumentException>(() => data.SetDataAsJson(new DataObject()));
    }

    [Fact]
    public void FileDropListValidatesCollectionAndStoresAnIndependentArray()
    {
        var data = new DataObject();
        var files = new StringCollection { "one.txt", "two.txt" };

        data.SetFileDropList(files);
        files[0] = "changed.txt";

        Assert.True(data.ContainsFileDropList());
        Assert.Equal(new[] { "one.txt", "two.txt" }, data.GetFileDropList().Cast<string>());
        Assert.Throws<ArgumentNullException>(() => data.SetFileDropList(null!));
        Assert.Throws<ArgumentException>(() => data.SetFileDropList(new StringCollection()));
        Assert.Throws<ArgumentException>(() => data.SetFileDropList(new StringCollection { "bad\0path" }));
    }

    private static void AssertRoutedEvent(RoutedEvent routedEvent, string name, Type handlerType)
    {
        Assert.Equal(name, routedEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, routedEvent.RoutingStrategy);
        Assert.Equal(handlerType, routedEvent.HandlerType);
        Assert.Equal(typeof(DataObject), routedEvent.OwnerType);
    }

    public abstract class PayloadBase
    {
        public int Value { get; init; }
    }

    public sealed class DerivedPayload : PayloadBase
    {
    }

    public sealed class JsonPayload
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    private sealed class LegacyDataObject : IDataObject
    {
        private readonly DataObject _inner = new();

        public object? GetData(string format) => _inner.GetData(format);
        public object? GetData(Type format) => _inner.GetData(format);
        public object? GetData(string format, bool autoConvert) => _inner.GetData(format, autoConvert);
        public bool GetDataPresent(string format) => _inner.GetDataPresent(format);
        public bool GetDataPresent(Type format) => _inner.GetDataPresent(format);
        public bool GetDataPresent(string format, bool autoConvert) => _inner.GetDataPresent(format, autoConvert);
        public string[] GetFormats() => _inner.GetFormats();
        public string[] GetFormats(bool autoConvert) => _inner.GetFormats(autoConvert);
        public void SetData(object data) => _inner.SetData(data);
        public void SetData(string format, object data) => _inner.SetData(format, data);
        public void SetData(Type format, object data) => _inner.SetData(format, data);
        public void SetData(string format, object data, bool autoConvert) => _inner.SetData(format, data, autoConvert);
    }
}
