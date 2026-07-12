using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class StylusPointDescriptionWpfParityTests
{
    [Fact]
    public void AreCompatible_MatchesOrderedPropertyIdentityAndButtonKind()
    {
        var first = CreateDescription(
            new StylusPointPropertyInfo(StylusPointProperties.Z, -10, 10, StylusPointPropertyUnit.Inches, 0.5f),
            new StylusPointPropertyInfo(StylusPointProperties.TipButton));
        var sameIdentityDifferentMetrics = CreateDescription(
            new StylusPointPropertyInfo(StylusPointProperties.Z, 0, 1000, StylusPointPropertyUnit.Centimeters, 10f),
            new StylusPointPropertyInfo(StylusPointProperties.TipButton, 0, 1, StylusPointPropertyUnit.None, 2f));

        Assert.True(StylusPointDescription.AreCompatible(first, sameIdentityDifferentMetrics));
    }

    [Fact]
    public void AreCompatible_RejectsDifferentCountOrderIdentityOrButtonKind()
    {
        var baseline = CreateDescription(
            new StylusPointPropertyInfo(StylusPointProperties.Z),
            new StylusPointPropertyInfo(StylusPointProperties.TipButton));

        Assert.False(StylusPointDescription.AreCompatible(
            baseline,
            CreateDescription(new StylusPointPropertyInfo(StylusPointProperties.Z))));
        Assert.False(StylusPointDescription.AreCompatible(
            baseline,
            CreateDescription(
                new StylusPointPropertyInfo(StylusPointProperties.TipButton),
                new StylusPointPropertyInfo(StylusPointProperties.Z))));
        Assert.False(StylusPointDescription.AreCompatible(
            baseline,
            CreateDescription(
                new StylusPointPropertyInfo(StylusPointProperties.Width),
                new StylusPointPropertyInfo(StylusPointProperties.TipButton))));

        var tipButtonAsValue = new StylusPointProperty(StylusPointProperties.TipButton.Id, isButton: false);
        Assert.False(StylusPointDescription.AreCompatible(
            baseline,
            CreateDescription(
                new StylusPointPropertyInfo(StylusPointProperties.Z),
                new StylusPointPropertyInfo(tipButtonAsValue))));
    }

    [Fact]
    public void AreCompatible_RejectsNullDescriptions()
    {
        var description = new StylusPointDescription();

        Assert.Throws<ArgumentNullException>(
            "stylusPointDescription",
            () => StylusPointDescription.AreCompatible(null!, description));
        Assert.Throws<ArgumentNullException>(
            "stylusPointDescription",
            () => StylusPointDescription.AreCompatible(description, null!));
    }

    private static StylusPointDescription CreateDescription(params StylusPointPropertyInfo[] additionalProperties)
    {
        var properties = new List<StylusPointPropertyInfo>
        {
            new(StylusPointProperties.X),
            new(StylusPointProperties.Y),
            new(StylusPointProperties.NormalPressure),
        };
        properties.AddRange(additionalProperties);
        return new StylusPointDescription(properties);
    }
}
