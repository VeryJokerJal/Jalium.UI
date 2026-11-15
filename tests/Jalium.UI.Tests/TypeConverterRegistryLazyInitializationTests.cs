using System.Reflection;
using System.Runtime.Loader;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Imaging;
using MarkupTypeConverter = Jalium.UI.Markup.TypeConverter;

namespace Jalium.UI.Tests;

public sealed class TypeConverterRegistryLazyInitializationTests
{
    [Fact]
    public void BuiltInCatalog_PreservesAllConvertersAndInstances()
    {
        (Type TargetType, string ConverterTypeName)[] expected =
        [
            (typeof(FontWeight), "FontWeightTypeConverter"),
            (typeof(FontStyle), "FontStyleTypeConverter"),
            (typeof(FontStretch), "FontStretchTypeConverter"),
            (typeof(Thickness), "ThicknessConverter"),
            (typeof(CornerRadius), "CornerRadiusConverter"),
            (typeof(Brush), "BrushConverter"),
            (typeof(SolidColorBrush), "BrushConverter"),
            (typeof(Color), "ColorConverter"),
            (typeof(GridLength), "GridLengthConverter"),
            (typeof(RowDefinitionCollection), "RowDefinitionCollectionConverter"),
            (typeof(ColumnDefinitionCollection), "ColumnDefinitionCollectionConverter"),
            (typeof(HorizontalAlignment), "HorizontalAlignmentConverter"),
            (typeof(VerticalAlignment), "VerticalAlignmentConverter"),
            (typeof(Orientation), "OrientationConverter"),
            (typeof(Duration), "DurationValueConverter"),
            (typeof(TransitionPropertyCollection), "TransitionPropertyCollectionConverter"),
            (typeof(Uri), "UriValueConverter"),
            (typeof(Type), "TypeTypeConverter"),
            (typeof(IconElement), "IconElementConverter"),
            (typeof(PointCollection), "PointCollectionConverter"),
            (typeof(DoubleCollection), "DoubleCollectionValueConverter"),
            (typeof(Point), "PointConverter"),
            (typeof(Vector), "VectorConverter"),
            (typeof(Size), "SizeConverter"),
            (typeof(Geometry), "GeometryTypeConverter"),
            (typeof(ColorMatrix), "ColorMatrixConverter"),
            (typeof(ImageSource), "ImageSourceTypeConverter"),
        ];

        Assert.Equal(27, expected.Length);

        foreach (var (targetType, converterTypeName) in expected)
        {
            MarkupTypeConverter converter = Assert.IsAssignableFrom<MarkupTypeConverter>(
                TypeConverterRegistry.GetConverter(targetType));
            Assert.Equal(converterTypeName, converter.GetType().Name);
            Assert.Same(converter, TypeConverterRegistry.GetConverter(targetType));
        }

        Assert.NotSame(
            TypeConverterRegistry.GetConverter(typeof(Brush)),
            TypeConverterRegistry.GetConverter(typeof(SolidColorBrush)));
    }

    [Fact]
    public void DerivedImageSource_UsesTheBuiltInBaseConverterInstance()
    {
        MarkupTypeConverter converter = Assert.IsAssignableFrom<MarkupTypeConverter>(
            TypeConverterRegistry.GetConverter(typeof(ImageSource)));

        Assert.Same(converter, TypeConverterRegistry.GetConverter(typeof(BitmapImage)));
        Assert.Same(converter, TypeConverterRegistry.GetConverter(typeof(SvgImage)));
    }

    [Fact]
    public void Register_PreservesOverwriteAndAppendOrderWhileExactTypeWins()
    {
        var first = new MarkerConverter("first");
        var second = new MarkerConverter("second");
        var replacement = new MarkerConverter("replacement");
        var later = new MarkerConverter("later");
        var exact = new MarkerConverter("exact");

        TypeConverterRegistry.Register(typeof(IFirstFallback), first);
        TypeConverterRegistry.Register(typeof(ISecondFallback), second);
        Assert.Same(first, TypeConverterRegistry.GetConverter(typeof(MultipleFallbacks)));

        TypeConverterRegistry.Register(typeof(IFirstFallback), replacement);
        TypeConverterRegistry.Register(typeof(ILaterFallback), later);
        Assert.Same(replacement, TypeConverterRegistry.GetConverter(typeof(MultipleFallbacks)));

        TypeConverterRegistry.Register(typeof(MultipleFallbacks), exact);
        Assert.Same(exact, TypeConverterRegistry.GetConverter(typeof(MultipleFallbacks)));
    }

    [Fact]
    public void PrimitiveColdPath_DoesNotCreateTableAndDefersItsRealAllocations()
    {
        ColdCallMeasurement text = MeasureColdCall(ColdCall.StringConversion);
        ColdCallMeasurement primitive = MeasureColdCall(ColdCall.PrimitiveConversion);
        ColdCallMeasurement lookup = MeasureColdCall(ColdCall.ConverterLookup);

        Assert.False(text.TableCreatedBefore);
        Assert.False(text.TableCreatedAfter);
        Assert.Equal("unchanged", text.Result);

        Assert.False(primitive.TableCreatedBefore);
        Assert.False(primitive.TableCreatedAfter);
        Assert.Equal(123, primitive.Result);

        Assert.False(lookup.TableCreatedBefore);
        Assert.True(lookup.TableCreatedAfter);
        Assert.NotNull(lookup.Result);

        Assert.True(
            lookup.AllocatedBytes > primitive.AllocatedBytes + 2 * 1024,
            $"Expected converter-table creation to allocate substantially more than the primitive cold path. " +
            $"Primitive={primitive.AllocatedBytes:N0} bytes, lookup={lookup.AllocatedBytes:N0} bytes.");
    }

    [Fact]
    public void PrimitiveConversion_RemainsAheadOfExplicitRegistration()
    {
        using var context = CreateLoadContext();
        Assembly assembly = context.LoadFromAssemblyPath(typeof(TypeConverterRegistry).Assembly.Location);
        WarmModuleWithoutRegistry(assembly);

        Type registry = assembly.GetType("Jalium.UI.Markup.TypeConverterRegistry", throwOnError: true)!;
        Type converterBase = assembly.GetType("Jalium.UI.Markup.TypeConverter", throwOnError: true)!;
        Type uriConverterType = assembly.GetType("Jalium.UI.Markup.UriValueConverter", throwOnError: true)!;
        object uriConverter = Activator.CreateInstance(uriConverterType)!;

        MethodInfo register = registry.GetMethod(
            "Register",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(Type), converterBase],
            modifiers: null)!;
        MethodInfo convertValue = registry.GetMethod(
            "ConvertValue",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string), typeof(Type)],
            modifiers: null)!;

        register.Invoke(null, [typeof(int), uriConverter]);

        Assert.Equal(42, convertValue.Invoke(null, ["42", typeof(int)]));
        Assert.Equal(new Uri("not-an-int", UriKind.Relative),
            Assert.IsType<Uri>(convertValue.Invoke(null, ["not-an-int", typeof(int)])));
    }

    private static ColdCallMeasurement MeasureColdCall(ColdCall call)
    {
        using var context = CreateLoadContext();
        Assembly assembly = context.LoadFromAssemblyPath(typeof(TypeConverterRegistry).Assembly.Location);
        WarmModuleWithoutRegistry(assembly);

        Type registry = assembly.GetType("Jalium.UI.Markup.TypeConverterRegistry", throwOnError: true)!;
        FieldInfo tableField = registry.GetField(
            "_converters",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        bool tableCreatedBefore = tableField.GetValue(null) is not null;

        MethodInfo method;
        object?[] arguments;
        if (call == ColdCall.StringConversion)
        {
            method = registry.GetMethod(
                "ConvertValue",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(Type)],
                modifiers: null)!;
            arguments = ["unchanged", typeof(string)];
        }
        else if (call == ColdCall.PrimitiveConversion)
        {
            method = registry.GetMethod(
                "ConvertValue",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(Type)],
                modifiers: null)!;
            arguments = ["123", typeof(int)];
        }
        else
        {
            method = registry.GetMethod(
                "GetConverter",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(Type)],
                modifiers: null)!;
            arguments = [typeof(Uri)];
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        object? result = method.Invoke(null, arguments);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        return new ColdCallMeasurement(
            result,
            allocatedBytes,
            tableCreatedBefore,
            tableField.GetValue(null) is not null);
    }

    private static IsolatedLoadContext CreateLoadContext() =>
        new(typeof(TypeConverterRegistryLazyInitializationTests).Assembly.Location);

    private static void WarmModuleWithoutRegistry(Assembly assembly)
    {
        Type converterType = assembly.GetType("Jalium.UI.Markup.UriValueConverter", throwOnError: true)!;
        Assert.NotNull(Activator.CreateInstance(converterType));
    }

    private sealed class IsolatedLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly AssemblyDependencyResolver _resolver;

        public IsolatedLoadContext(string componentAssemblyPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(componentAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        public void Dispose() => Unload();
    }

    private sealed class MarkerConverter(string marker) : MarkupTypeConverter
    {
        public override object? ConvertFrom(object? value) => marker;
    }

    private interface IFirstFallback
    {
    }

    private interface ISecondFallback
    {
    }

    private interface ILaterFallback
    {
    }

    private sealed class MultipleFallbacks : IFirstFallback, ISecondFallback, ILaterFallback
    {
    }

    private enum ColdCall
    {
        StringConversion,
        PrimitiveConversion,
        ConverterLookup,
    }

    private readonly record struct ColdCallMeasurement(
        object? Result,
        long AllocatedBytes,
        bool TableCreatedBefore,
        bool TableCreatedAfter);
}
