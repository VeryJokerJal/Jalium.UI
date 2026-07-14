using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class ParityIndependentRootTypesTests
{
    private static readonly DependencyProperty AttachedValueProperty =
        DependencyProperty.RegisterAttached(
            "IndependentRootTypeAttachedValue",
            typeof(int),
            typeof(AttachedPropertyOwner),
            new PropertyMetadata(0));

    [Fact]
    public void ResourceMarkupExtensions_UseCanonicalWpfRootNamespace()
    {
        Type[] canonicalTypes =
        [
            typeof(StaticResourceExtension),
            typeof(DynamicResourceExtension),
            typeof(ThemeDictionaryExtension),
            typeof(ColorConvertedBitmapExtension),
        ];

        Assert.All(canonicalTypes, type =>
        {
            Assert.Equal("Jalium.UI", type.Namespace);
            Assert.False(type.IsSealed);
            Assert.Same(type, XamlTypeRegistry.GetType(type.Name));
            Assert.Null(type.Assembly.GetType($"Jalium.UI.Markup.{type.Name}"));
        });

        Assert.Empty(typeof(ColorConvertedBitmapExtension).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.NotNull(typeof(ThemeDictionaryExtension).GetConstructor([typeof(string)]));
        Assert.NotNull(typeof(ColorConvertedBitmapExtension).GetConstructor([typeof(object)]));
    }

    [Fact]
    public void AttachedPropertyBrowsableForType_SupportsInterfaceTargets()
    {
        var attribute = new AttachedPropertyBrowsableForTypeAttribute(typeof(IAttachedPropertyTarget));

        Assert.True(attribute.IsBrowsable(new InterfaceTarget(), AttachedValueProperty));
        Assert.False(attribute.IsBrowsable(new DependencyObject(), AttachedValueProperty));
    }

    [Fact]
    public void LengthAndFontConverters_MatchWpfFailureAndCultureBehavior()
    {
        var lengthConverter = new LengthConverter();

        Assert.Throws<NotSupportedException>(() => lengthConverter.ConvertFrom(null!));
        Assert.Equal(
            1_000d,
            lengthConverter.ConvertFrom(null, CultureInfo.InvariantCulture, "1,000"));

        var fontSizeConverter = new FontSizeConverter();
        Assert.Null(fontSizeConverter.ConvertFrom(null, CultureInfo.InvariantCulture, 12m));

        ArgumentException weightException = Assert.Throws<ArgumentException>(
            () => new FontWeightConverter().ConvertFrom(400));
        ArgumentException styleException = Assert.Throws<ArgumentException>(
            () => new FontStyleConverter().ConvertFrom(1));
        ArgumentException stretchException = Assert.Throws<ArgumentException>(
            () => new FontStretchConverter().ConvertFrom(5));

        Assert.Equal("value", weightException.ParamName);
        Assert.Equal("value", styleException.ParamName);
        Assert.Equal("value", stretchException.ParamName);
    }

    [Fact]
    public void DynamicResourceExtensionConverter_CreatesConstructorDescriptor()
    {
        object key = new();
        var source = new DynamicResourceExtension(key);
        System.ComponentModel.TypeConverter converter = TypeDescriptor.GetConverter(typeof(DynamicResourceExtension));

        Assert.IsType<DynamicResourceExtensionConverter>(converter);
        Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
        Assert.False(typeof(DynamicResourceExtensionConverter).IsSealed);

        InstanceDescriptor descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(source, typeof(InstanceDescriptor)));
        DynamicResourceExtension reconstructed = Assert.IsType<DynamicResourceExtension>(
            descriptor.Invoke());

        Assert.NotSame(source, reconstructed);
        Assert.Same(key, reconstructed.ResourceKey);

        ArgumentNullException nullException = Assert.Throws<ArgumentNullException>(() =>
        {
            converter.ConvertTo(null, CultureInfo.InvariantCulture, null, typeof(InstanceDescriptor));
        });
        Assert.Equal("value", nullException.ParamName);

        ArgumentException valueException = Assert.Throws<ArgumentException>(() =>
        {
            converter.ConvertTo(new object(), typeof(InstanceDescriptor));
        });
        Assert.Equal("value", valueException.ParamName);
    }

    [Fact]
    public void HwndDpiChangedEventArgs_PreserveScalesSuggestedRectAndHandledState()
    {
        var oldDpi = new DpiScale(1, 1.25);
        var newDpi = new DpiScale(1.5, 2);
        var suggestedRect = new Rect(10, 20, 640, 480);
        var eventArgs = new HwndDpiChangedEventArgs(oldDpi, newDpi, suggestedRect)
        {
            Handled = true,
        };

        Assert.Equal(oldDpi, eventArgs.OldDpi);
        Assert.Equal(newDpi, eventArgs.NewDpi);
        Assert.Equal(suggestedRect, eventArgs.SuggestedRect);
        Assert.True(eventArgs.Handled);

        object sender = new();
        object? observedSender = null;
        HwndDpiChangedEventArgs? observedArgs = null;
        HwndDpiChangedEventHandler handler = (source, args) =>
        {
            observedSender = source;
            observedArgs = args;
        };

        handler(sender, eventArgs);

        Assert.Same(sender, observedSender);
        Assert.Same(eventArgs, observedArgs);
    }

    [Fact]
    public void HwndDpiChangedEventArgs_ReadNativeSuggestedRectangle()
    {
        int size = Marshal.SizeOf<NativeRect>();
        nint address = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(new NativeRect(10, 20, 650, 500), address, false);

#pragma warning disable CS0612 // Exercise WPF's obsolete raw-DPI compatibility constructor.
            var eventArgs = new HwndDpiChangedEventArgs(96, 120, 144, 192, address);
#pragma warning restore CS0612

            Assert.Equal(new DpiScale(1, 1.25), eventArgs.OldDpi);
            Assert.Equal(new DpiScale(1.5, 2), eventArgs.NewDpi);
            Assert.Equal(new Rect(10, 20, 640, 480), eventArgs.SuggestedRect);
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [Fact]
    public void HwndDpiChangedEventArgs_PublicSurfaceMatchesWpf()
    {
        Type type = typeof(HwndDpiChangedEventArgs);

        Assert.True(type.IsSealed);
        Assert.Equal(typeof(HandledEventArgs), type.BaseType);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        ConstructorInfo legacyConstructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(double), typeof(double), typeof(double), typeof(double), typeof(nint)],
            modifiers: null)!;
        Assert.NotNull(legacyConstructor.GetCustomAttribute<ObsoleteAttribute>());
        Assert.Collection(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(property => property.Name),
            property => AssertReadOnlyProperty(property, nameof(HwndDpiChangedEventArgs.NewDpi), typeof(DpiScale)),
            property => AssertReadOnlyProperty(property, nameof(HwndDpiChangedEventArgs.OldDpi), typeof(DpiScale)),
            property => AssertReadOnlyProperty(property, nameof(HwndDpiChangedEventArgs.SuggestedRect), typeof(Rect)));

        MethodInfo invoke = typeof(HwndDpiChangedEventHandler).GetMethod("Invoke")!;
        Assert.Equal(typeof(void), invoke.ReturnType);
        Assert.Equal(
            new[] { typeof(object), typeof(HwndDpiChangedEventArgs) },
            invoke.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertReadOnlyProperty(PropertyInfo property, string name, Type propertyType)
    {
        Assert.Equal(name, property.Name);
        Assert.Equal(propertyType, property.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.NotNull(property.SetMethod);
        Assert.True(property.SetMethod!.IsPrivate);
    }

    private interface IAttachedPropertyTarget;

    private sealed class InterfaceTarget : DependencyObject, IAttachedPropertyTarget;

    private sealed class AttachedPropertyOwner : DependencyObject;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
