using Jalium.UI.Styling;
using System.Text.Json;
using Xunit;

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
        _ = typeof(Jalium.UI.Controls.Control);
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

        string[] milestoneProperties =
        [
            "column-count", "column-fill", "break-inside", "content", "quotes", "list-style-type",
            "caret-color", "caret-shape", "user-select", "image-rendering", "vector-effect",
            "offset-path", "offset-rotate", "scroll-timeline-axis", "view-timeline-axis",
            "scroll-snap-type", "scroll-snap-align", "contain", "content-visibility",
            "color-scheme", "appearance", "writing-mode", "text-orientation", "ruby-position",
            "anchor-name", "position-area"
        ];
        foreach (var prop in milestoneProperties)
        {
            var meta = CssAuthoring.GetProperty(prop);
            Assert.Equal(CssPropertySupport.Supported, meta.Support);
            Assert.NotEmpty(meta.Values);
        }

        if (Environment.GetEnvironmentVariable("JALIUM_UPDATE_CSS_INVENTORY") == "1")
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var inventoryPath = Path.Combine(repoRoot, "docs", "css-authoring-inventory.json");
            var payload = new
            {
                formatVersion = 1,
                asOf = "2026-10-27",
                scope = "Runtime authoring metadata only; not a rendering or CSS conformance statement",
                source = "Accepted full-css-milestones-20261027 runtime",
                properties = CssAuthoring.GetProperties()
                    .Where(static p => !p.Name.StartsWith("authoring-test-", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(static p => p.Name, StringComparer.Ordinal)
                    .Select(static p => new
                    {
                        name = p.Name,
                        canonicalName = p.CanonicalName,
                        support = p.Support.ToString(),
                        initialValue = p.InitialValue,
                        inherited = p.IsInherited,
                        shorthand = p.IsShorthand,
                        unsupportedReason = p.UnsupportedReason,
                        values = p.Values
                    })
                    .ToArray()
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n";
            File.WriteAllText(inventoryPath, json);
        }
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

public sealed class CssAuthoringDiagnosticSpanTests
{
    [Fact]
    public void InlineDiagnosticUsesUtf16PropertyRange()
    {
        const string text = "color: red;\r\nheight: ;";
        var diagnostic = Assert.Single(CssAuthoring.AnalyzeDeclarations(text));
        Assert.Equal("height", text.Substring(diagnostic.Start, diagnostic.Length));
    }

    [Fact]
    public void NestedStyleDiagnosticPointsAtMalformedProperty()
    {
        const string text = ".panel { color: ; }";
        var diagnostic = Assert.Single(CssAuthoring.AnalyzeStyleSheet(text)
            .Where(item => item.Message.Contains("empty value", StringComparison.Ordinal)));
        Assert.Equal("color", text.Substring(diagnostic.Start, diagnostic.Length));
    }

    [Fact]
    public void RegistryMetadataIncludesCanonicalProperty()
    {
        var color = CssAuthoring.GetProperty("color");
        Assert.Equal(CssPropertySupport.Supported, color.Support);
        Assert.Equal("color", color.CanonicalName);
        Assert.Contains(CssAuthoring.GetProperties(), property => property.Name == "color");
    }
}
