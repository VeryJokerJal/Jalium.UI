using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssAuthoringTests
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
