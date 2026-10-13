using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed unsafe partial class MacOSTextStyleAccessibilityTests
{
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void StyleSnapshotUsesTheFirstAvailableQuotedFamily(int type)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        using var fixture = new StyleFixture(type, "Miii");
        SetFamily(fixture, "'Missing, Window121', 'Helvetica Neue'");
        using var snapshot = fixture.Read(0, 4); var font = snapshot.RootElement.GetProperty("runs")[0];
        Assert.Equal("Helvetica Neue", font.GetProperty("family").GetString());
        Assert.Equal(100, font.GetProperty("width").GetDouble());
        Assert.Equal("Miii", fixture.Source.Text[..4]); Assert.Equal(0, fixture.Source.SelectionLength);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void StyleSnapshotKeepsPrivateFontAliasForTheSharedNativeResolver(int type)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        using var resource = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc"));
        Assert.NotNull(resource); using var fixture = new StyleFixture(type, "Miii");
        SetFamily(fixture, resource.Family);
        using var snapshot = fixture.Read(0, 4); var font = snapshot.RootElement.GetProperty("runs")[0];
        Assert.Equal(resource.Family, font.GetProperty("family").GetString()); Assert.Equal(100, font.GetProperty("width").GetDouble());
        Assert.Equal("Miii", fixture.Source.Text[..4]); Assert.Equal(0, fixture.Source.SelectionLength);
    }

    [Theory] [InlineData(0,83.2)] [InlineData(0,92.8)] [InlineData(2,83.2)] [InlineData(2,92.8)]
    public void StyleSnapshotUnwrapsExactCssWidthRatherThanReportingAnInternalFontName(int type, double width)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        using var fixture = new StyleFixture(type,"Miii"); SetFamily(fixture,"SF Pro");
        Css.SetStyle(fixture.Editor, $"font-width:{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}%");
        try
        {
            using var snapshot = fixture.Read(0,4); var font = snapshot.RootElement.GetProperty("runs")[0];
            Assert.Equal("SF Pro",font.GetProperty("family").GetString()); Assert.Equal(width,font.GetProperty("width").GetDouble(),4);
            Assert.Equal("Miii",fixture.Source.Text); Assert.Equal(0,fixture.Source.SelectionLength);
        }
        finally { Css.SetStyle(fixture.Editor,string.Empty); }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void StyleSnapshotResolvesThePrimaryCssPlanFaceAndDescriptorWidth(int type)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        using var fixture = new StyleFixture(type,"Miii");
        var plan = new CssFontRenderingPlan(0,0,[new("Helvetica Neue",650,[new(0,0x10ffff)],false,false,75)]);
        SetFamily(fixture,plan.Encode());
        using var snapshot = fixture.Read(0,4); var font = snapshot.RootElement.GetProperty("runs")[0];
        Assert.Equal("Helvetica Neue",font.GetProperty("family").GetString()); Assert.Equal(650,font.GetProperty("weight").GetInt32());
        Assert.Equal(75,font.GetProperty("width").GetDouble()); Assert.Equal(400,fixture.Editor.FontWeight.ToOpenTypeWeight());
        Assert.Equal("Miii",fixture.Source.Text[..4]); Assert.Equal(0,fixture.Source.SelectionLength);
    }

    private static void SetFamily(StyleFixture fixture,string family)
    {
        fixture.Editor.FontFamily = new FontFamily(family);
        if (fixture.Editor is RichTextBox rich) rich.Document.FontFamily = fixture.Editor.FontFamily;
    }
}
