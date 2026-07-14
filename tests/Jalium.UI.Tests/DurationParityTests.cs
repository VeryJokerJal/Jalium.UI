using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using Duration = Jalium.UI.Duration;

namespace Jalium.UI.Tests;

public sealed class DurationParityTests
{
    [Fact]
    public void DurationAndConverter_AreInTheWpfNamespace()
    {
        Assert.Equal("Jalium.UI.Duration", typeof(Duration).FullName);
        TypeConverter converter = TypeDescriptor.GetConverter(typeof(Duration));
        Assert.Equal(typeof(DurationConverter), converter.GetType());
        Assert.True(converter.CanConvertTo(typeof(InstanceDescriptor)));
        Assert.Equal("Forever", converter.ConvertToInvariantString(Duration.Forever));
        Assert.Equal(Duration.Automatic, converter.ConvertFromInvariantString("Automatic"));

        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(new Duration(TimeSpan.FromSeconds(2)), typeof(InstanceDescriptor)));
        Assert.Equal(new Duration(TimeSpan.FromSeconds(2)), descriptor.Invoke());
    }

    [Fact]
    public void PublicSurfaceContainsWorkbookTierOneMembers()
    {
        Type type = typeof(Duration);

        Assert.NotNull(GetDeclaredMethod(type, nameof(Duration.Add), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, nameof(Duration.Compare), typeof(Duration), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, nameof(Duration.Equals), typeof(Duration), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, nameof(Duration.Plus), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, nameof(Duration.Subtract), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, nameof(Duration.ToString)));
        Assert.NotNull(GetDeclaredMethod(type, "op_Addition", typeof(Duration), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, "op_UnaryPlus", typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, "op_Subtraction", typeof(Duration), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, "op_LessThan", typeof(Duration), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, "op_LessThanOrEqual", typeof(Duration), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, "op_GreaterThan", typeof(Duration), typeof(Duration)));
        Assert.NotNull(GetDeclaredMethod(type, "op_GreaterThanOrEqual", typeof(Duration), typeof(Duration)));
    }

    [Fact]
    public void ConstructorAndImplicitConversionRejectNegativeTimeSpan()
    {
        var constructorException = Assert.Throws<ArgumentException>(
            () => new Duration(TimeSpan.FromTicks(-1)));
        Assert.Equal("timeSpan", constructorException.ParamName);

        var conversionException = Assert.Throws<ArgumentException>(() => Convert(TimeSpan.FromTicks(-1)));
        Assert.Equal("timeSpan", conversionException.ParamName);

        static Duration Convert(TimeSpan value) => value;
    }

    [Fact]
    public void TimeSpanPropertyIsAvailableOnlyForFiniteDurations()
    {
        TimeSpan value = TimeSpan.FromSeconds(2);
        var finite = new Duration(value);

        Assert.True(finite.HasTimeSpan);
        Assert.Equal(value, finite.TimeSpan);
        Assert.False(Duration.Automatic.HasTimeSpan);
        Assert.False(Duration.Forever.HasTimeSpan);
        Assert.Throws<InvalidOperationException>(() => Duration.Automatic.TimeSpan);
        Assert.Throws<InvalidOperationException>(() => Duration.Forever.TimeSpan);
        Assert.Equal(Duration.Automatic, default);
    }

    [Fact]
    public void AdditionMatchesFiniteAndSentinelMatrix()
    {
        var one = new Duration(TimeSpan.FromSeconds(1));
        var two = new Duration(TimeSpan.FromSeconds(2));

        Assert.Equal(new Duration(TimeSpan.FromSeconds(3)), one + two);
        Assert.Equal(Duration.Forever, one + Duration.Forever);
        Assert.Equal(Duration.Forever, Duration.Forever + one);
        Assert.Equal(Duration.Forever, Duration.Forever + Duration.Forever);
        Assert.Equal(Duration.Automatic, one + Duration.Automatic);
        Assert.Equal(Duration.Automatic, Duration.Automatic + one);
        Assert.Equal(Duration.Automatic, Duration.Automatic + Duration.Forever);
        Assert.Equal(Duration.Automatic, Duration.Forever + Duration.Automatic);
        Assert.Throws<OverflowException>(
            () => new Duration(TimeSpan.MaxValue) + new Duration(TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void SubtractionMatchesFiniteAndSentinelMatrix()
    {
        var one = new Duration(TimeSpan.FromSeconds(1));
        var two = new Duration(TimeSpan.FromSeconds(2));

        Assert.Equal(one, two - one);
        Assert.Equal(Duration.Forever, Duration.Forever - one);
        Assert.Equal(Duration.Automatic, Duration.Forever - Duration.Forever);
        Assert.Equal(Duration.Automatic, one - Duration.Forever);
        Assert.Equal(Duration.Automatic, one - Duration.Automatic);
        Assert.Equal(Duration.Automatic, Duration.Forever - Duration.Automatic);
        Assert.Equal(Duration.Automatic, Duration.Automatic - one);
        Assert.Equal(Duration.Automatic, Duration.Automatic - Duration.Forever);
        Assert.Equal(Duration.Automatic, Duration.Automatic - Duration.Automatic);
        Assert.Throws<ArgumentException>(() => one - two);
    }

    [Fact]
    public void ConvenienceArithmeticMembersDelegateToOperators()
    {
        var one = new Duration(TimeSpan.FromSeconds(1));
        var two = new Duration(TimeSpan.FromSeconds(2));

        Assert.Equal(one, +one);
        Assert.Equal(one, Duration.Plus(one));
        Assert.Equal(new Duration(TimeSpan.FromSeconds(3)), one.Add(two));
        Assert.Equal(one, two.Subtract(one));
    }

    [Fact]
    public void RelationalOperatorsTreatAutomaticLikeWpfNaN()
    {
        var finite = new Duration(TimeSpan.FromSeconds(1));
        Duration automaticLeft = Duration.Automatic;
        Duration automaticRight = Duration.Automatic;

        Assert.False(automaticLeft < automaticRight);
        Assert.False(automaticLeft > automaticRight);
        Assert.True(automaticLeft <= automaticRight);
        Assert.True(automaticLeft >= automaticRight);

        Assert.False(automaticLeft < finite);
        Assert.False(automaticLeft <= finite);
        Assert.False(automaticLeft > finite);
        Assert.False(automaticLeft >= finite);
        Assert.False(finite < automaticLeft);
        Assert.False(finite <= automaticLeft);
        Assert.False(finite > automaticLeft);
        Assert.False(finite >= automaticLeft);
    }

    [Fact]
    public void RelationalOperatorsOrderFiniteDurationsBeforeForever()
    {
        var one = new Duration(TimeSpan.FromSeconds(1));
        var two = new Duration(TimeSpan.FromSeconds(2));

        Assert.True(one < two);
        Assert.True(one <= two);
        Assert.True(two > one);
        Assert.True(two >= one);
        Assert.True(one < Duration.Forever);
        Assert.True(one <= Duration.Forever);
        Assert.True(Duration.Forever > one);
        Assert.True(Duration.Forever >= one);
        Assert.False(Duration.Forever < Duration.Forever);
        Assert.False(Duration.Forever > Duration.Forever);
        Assert.True(Duration.Forever <= Duration.Forever);
        Assert.True(Duration.Forever >= Duration.Forever);
    }

    [Fact]
    public void CompareUsesWpfTotalOrderingIncludingAutomatic()
    {
        var one = new Duration(TimeSpan.FromSeconds(1));
        var two = new Duration(TimeSpan.FromSeconds(2));

        Assert.Equal(0, Duration.Compare(Duration.Automatic, Duration.Automatic));
        Assert.Equal(-1, Duration.Compare(Duration.Automatic, one));
        Assert.Equal(1, Duration.Compare(one, Duration.Automatic));
        Assert.Equal(-1, Duration.Compare(one, two));
        Assert.Equal(1, Duration.Compare(two, one));
        Assert.Equal(0, Duration.Compare(one, one));
        Assert.Equal(-1, Duration.Compare(one, Duration.Forever));
        Assert.Equal(1, Duration.Compare(Duration.Forever, one));
        Assert.Equal(0, Duration.Compare(Duration.Forever, Duration.Forever));
    }

    [Fact]
    public void EqualityAndHashCodesMatchWpfValueSemantics()
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(1);
        var first = new Duration(timeSpan);
        var second = new Duration(timeSpan);

        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(Duration.Equals(first, second));
        Assert.False(first.Equals(Duration.Automatic));
        Assert.False(first.Equals("00:00:01"));
        Assert.Equal(timeSpan.GetHashCode(), first.GetHashCode());
        Assert.Equal(17, Duration.Automatic.GetHashCode());
        Assert.Equal(19, Duration.Forever.GetHashCode());
    }

    [Fact]
    public void ToStringMatchesWpfSentinelsAndTimeSpanConverter()
    {
        Assert.Equal("Automatic", Duration.Automatic.ToString());
        Assert.Equal("Forever", Duration.Forever.ToString());

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = culture;
            TimeSpan timeSpan = TimeSpan.FromMilliseconds(1500);
            string expected = TypeDescriptor.GetConverter(timeSpan).ConvertToString(timeSpan)!;

            Assert.Equal(expected, new Duration(timeSpan).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static MethodInfo? GetDeclaredMethod(Type type, string name, params Type[] parameterTypes)
    {
        return type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);
    }
}
