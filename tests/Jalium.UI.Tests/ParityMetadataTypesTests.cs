using System.Reflection;
using Jalium.UI;

#pragma warning disable WPF0001

namespace Jalium.UI.Tests;

public class ParityMetadataTypesTests
{
    [Fact]
    public void AutoResizedEventArgs_PreservesSize_AndHandlerUsesExpectedSignature()
    {
        var expected = new Size(320, 180);
        var eventArgs = new AutoResizedEventArgs(expected);
        object sender = new();
        object? observedSender = null;
        AutoResizedEventArgs? observedArgs = null;
        AutoResizedEventHandler handler = (source, args) =>
        {
            observedSender = source;
            observedArgs = args;
        };

        handler(sender, eventArgs);

        Assert.Equal(expected, eventArgs.Size);
        Assert.Same(sender, observedSender);
        Assert.Same(eventArgs, observedArgs);
    }

    [Fact]
    public void WpfParityEnums_HaveExpectedNamesAndValues()
    {
        AssertEnum<ColumnSpaceDistribution>(("Left", 0), ("Right", 1), ("Between", 2));
        AssertEnum<FontEastAsianLanguage>(
            ("Normal", 0), ("Jis78", 1), ("Jis83", 2), ("Jis90", 3), ("Jis04", 4),
            ("HojoKanji", 5), ("NlcKanji", 6), ("Simplified", 7), ("Traditional", 8),
            ("TraditionalNames", 9));
        AssertEnum<FontEastAsianWidths>(
            ("Normal", 0), ("Proportional", 1), ("Full", 2), ("Half", 3), ("Third", 4), ("Quarter", 5));
        AssertEnum<FontFraction>(("Normal", 0), ("Slashed", 1), ("Stacked", 2));
        AssertEnum<InheritanceBehavior>(
            ("Default", 0), ("SkipToAppNow", 1), ("SkipToAppNext", 2), ("SkipToThemeNow", 3),
            ("SkipToThemeNext", 4), ("SkipAllNow", 5), ("SkipAllNext", 6));
        AssertEnum<LineBreakCondition>(
            ("BreakDesired", 0), ("BreakPossible", 1), ("BreakRestrained", 2), ("BreakAlways", 3));
        AssertEnum<LineStackingStrategy>(("BlockLineHeight", 0), ("MaxHeight", 1));
        AssertEnum<PowerLineStatus>(("Offline", 0), ("Online", 1), ("Unknown", 255));
        AssertEnum<ResourceDictionaryLocation>(("None", 0), ("SourceAssembly", 1), ("ExternalAssembly", 2));
        AssertEnum<TextDataFormat>(
            ("Text", 0), ("UnicodeText", 1), ("Rtf", 2), ("Html", 3), ("CommaSeparatedValue", 4), ("Xaml", 5));
    }

    [Fact]
    public void StyleTypedPropertyAttribute_ExposesWpfMetadataContract()
    {
        var emptyAttribute = new StyleTypedPropertyAttribute();
        var attribute = new StyleTypedPropertyAttribute
        {
            Property = "ItemContainerStyle",
            StyleTargetType = typeof(string),
        };
        var usage = typeof(StyleTypedPropertyAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.Null(emptyAttribute.Property);
        Assert.Null(emptyAttribute.StyleTargetType);
        Assert.Equal("ItemContainerStyle", attribute.Property);
        Assert.Equal(typeof(string), attribute.StyleTargetType);
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void ThemeInfoAttribute_PreservesLocations_AndTargetsAssembliesOnly()
    {
        var attribute = new ThemeInfoAttribute(
            ResourceDictionaryLocation.ExternalAssembly,
            ResourceDictionaryLocation.SourceAssembly);
        var usage = typeof(ThemeInfoAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.Equal(ResourceDictionaryLocation.ExternalAssembly, attribute.ThemeDictionaryLocation);
        Assert.Equal(ResourceDictionaryLocation.SourceAssembly, attribute.GenericDictionaryLocation);
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void ThemeMode_PredefinedAndDefaultValuesMatchWpf()
    {
        Assert.Equal("None", default(ThemeMode).Value);
        Assert.Equal("None", ThemeMode.None.ToString());
        Assert.Equal("Light", ThemeMode.Light.Value);
        Assert.Equal("Dark", ThemeMode.Dark.Value);
        Assert.Equal("System", ThemeMode.System.Value);
    }

    [Fact]
    public void ThemeMode_EqualityIsOrdinalAndSupportsOperators()
    {
        var first = new ThemeMode("Custom");
        var same = new ThemeMode("Custom");
        var differentCase = new ThemeMode("custom");

        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.True(first == same);
        Assert.False(first != same);
        Assert.False(first.Equals(differentCase));
        Assert.NotEqual(first, differentCase);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    private static void AssertEnum<TEnum>(params (string Name, int Value)[] expected)
        where TEnum : struct, Enum
    {
        var actual = Enum.GetValues<TEnum>()
            .Select(value => (value.ToString(), Convert.ToInt32(value)))
            .ToArray();

        Assert.Equal(expected, actual);
    }
}

#pragma warning restore WPF0001
