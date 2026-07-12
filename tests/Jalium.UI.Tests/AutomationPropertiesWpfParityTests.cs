using Jalium.UI.Automation;

namespace Jalium.UI.Tests;

public sealed class AutomationPropertiesWpfParityTests
{
    [Fact]
    public void SetMetadataPropertiesUseWpfDefaultsAndRoundTrip()
    {
        var element = new DependencyObject();

        Assert.Equal(AutomationHeadingLevel.None, AutomationProperties.GetHeadingLevel(element));
        Assert.Equal(-1, AutomationProperties.GetPositionInSet(element));
        Assert.Equal(-1, AutomationProperties.GetSizeOfSet(element));
        Assert.False(AutomationProperties.GetIsRowHeader(element));
        Assert.False(AutomationProperties.GetIsColumnHeader(element));
        Assert.False(AutomationProperties.GetIsDialog(element));

        AutomationProperties.SetHeadingLevel(element, AutomationHeadingLevel.Level3);
        AutomationProperties.SetPositionInSet(element, 2);
        AutomationProperties.SetSizeOfSet(element, 5);
        AutomationProperties.SetIsRowHeader(element, true);
        AutomationProperties.SetIsColumnHeader(element, true);
        AutomationProperties.SetIsDialog(element, true);

        Assert.Equal(AutomationHeadingLevel.Level3, AutomationProperties.GetHeadingLevel(element));
        Assert.Equal(2, AutomationProperties.GetPositionInSet(element));
        Assert.Equal(5, AutomationProperties.GetSizeOfSet(element));
        Assert.True(AutomationProperties.GetIsRowHeader(element));
        Assert.True(AutomationProperties.GetIsColumnHeader(element));
        Assert.True(AutomationProperties.GetIsDialog(element));
    }

    [Fact]
    public void HeadingLevelHasAllWpfValuesAndRejectsUndefinedValues()
    {
        Assert.Equal(Enumerable.Range(0, 10),
            Enum.GetValues<AutomationHeadingLevel>().Select(static value => (int)value));

        Assert.Throws<ArgumentException>(() =>
            AutomationProperties.SetHeadingLevel(
                new DependencyObject(),
                (AutomationHeadingLevel)10));
    }
}
