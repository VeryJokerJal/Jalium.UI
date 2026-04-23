using System.Reflection;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public class XmlnsDefinitionRegistryTests
{
    [Fact]
    public void FrameworkAssemblies_ShouldExposeCanonicalPresentationNamespace()
    {
        // Smoke test: the runtime scan must have picked up the assembly-level XmlnsDefinitionAttribute
        // on Jalium.UI.Controls and friends. Button is a good canary since it lives in Jalium.UI.Controls
        // under the canonical presentation namespace.
        var mappings = XmlnsDefinitionRegistry.GetMappings(JalxamlNamespaces.Presentation);
        Assert.NotEmpty(mappings);
        Assert.Contains(mappings, m => m.ClrNamespace == "Jalium.UI.Controls");
    }

    [Fact]
    public void CompatibilityRedirect_ShouldForwardLegacyUrisToPresentation()
    {
        // http://schemas.jalium.ui/2024 must resolve to the same mappings as the canonical URI,
        // because Jalium.UI.Controls declares XmlnsCompatibleWithAttribute for it.
        var legacyMappings = XmlnsDefinitionRegistry.GetMappings(JalxamlNamespaces.LegacyJaliumUi);
        var canonicalMappings = XmlnsDefinitionRegistry.GetMappings(JalxamlNamespaces.Presentation);

        Assert.NotEmpty(legacyMappings);
        Assert.Equal(canonicalMappings.Length, legacyMappings.Length);
    }

    [Fact]
    public void XamlReader_ShouldResolveTypesAdvertisedByXmlnsDefinition()
    {
        // Register an anonymous mapping at runtime, then parse XAML that references the anonymous
        // namespace. We use an already-loaded framework type — the registry just needs to steer
        // XamlReader towards the right CLR namespace / assembly pair.
        const string xmlns = "urn:test:jalium:xmlns-definition-registry";
        XmlnsDefinitionRegistry.AddXmlnsDefinition(xmlns, "Jalium.UI.Controls", typeof(Controls.Button).Assembly);

        var xaml = $"""
            <Button xmlns="{xmlns}" Content="hello" />
            """;

        var button = Assert.IsType<Controls.Button>(XamlReader.Parse(xaml));
        Assert.Equal("hello", button.Content);
    }

    [Fact]
    public void ResolveCompatibilityRedirect_ShouldReturnNamespaceUnchangedWhenNoRedirectRegistered()
    {
        const string unknown = "urn:test:jalium:no-compat-redirect";
        Assert.Equal(unknown, XmlnsDefinitionRegistry.ResolveCompatibilityRedirect(unknown));
    }

    [Fact]
    public void PreferredPrefix_ShouldBeReadFromXmlnsPrefixAttribute()
    {
        Assert.Equal("ui", XmlnsDefinitionRegistry.GetPreferredPrefix(JalxamlNamespaces.Presentation));
    }
}
