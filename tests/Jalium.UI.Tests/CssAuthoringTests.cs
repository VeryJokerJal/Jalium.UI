using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssAuthoringTests
{
    [Fact]
    public void PublishesRuntimeDefaultsAndAliasIdentity()
    {
        var display = CssAuthoring.GetProperty(" DISPLAY ");
        Assert.Equal("inline", display.InitialValue);
        Assert.Contains("flex", display.Values);
        var alias = CssAuthoring.GetProperty("word-wrap");
        Assert.Equal("overflow-wrap", alias.CanonicalName);
        Assert.Equal("normal", alias.InitialValue);
        Assert.Contains("anywhere", alias.Values);
        Assert.True(alias.IsInherited);
        Assert.Contains(CssAuthoring.GetProperties(), static property => property.Name == "word-wrap");
        Assert.True(CssAuthoring.GetProperty("margin").IsShorthand);
    }

    [Fact]
    public void EveryPublishedCandidateIsAcceptedByTheRuntimeAndUnsupportedPropertiesHaveNone()
    {
        var properties = CssAuthoring.GetSupportedProperties();
        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.All(property.Values,
            value => Assert.True(Css.Supports(property.Name, value), $"{property.Name}: {value}")));
        Assert.Contains("uppercase", CssAuthoring.GetValues("text-transform"));
        Assert.Contains("700", CssAuthoring.GetValues("font-weight"));
        Assert.False(CssAuthoring.IsValueSupported("display", "not-a-display-mode"));
        Assert.Empty(CssAuthoring.GetValues("not-a-css-property"));
        Assert.All(CssAuthoring.GetProperties().Where(static property => property.Support == CssPropertySupport.Unsupported),
            property => Assert.Empty(property.Values));
    }

    [Fact]
    public void NewMappingsAndAliasChangesRefreshPreviouslyReadMetadata()
    {
        var name = "authoring-test-" + Guid.NewGuid().ToString("N");
        Assert.Equal(CssPropertySupport.Unknown, CssAuthoring.GetProperty(name).Support);
        CssMappings.RegisterAlias(name, "display");
        Assert.Contains(CssAuthoring.GetSupportedProperties(), property => property.Name == name);
        Assert.Contains("flex", CssAuthoring.GetValues(name));
        CssMappings.RegisterAlias(name, "visibility");
        Assert.Contains("visible", CssAuthoring.GetValues(name));
        Assert.DoesNotContain("flex", CssAuthoring.GetValues(name));
    }
}
