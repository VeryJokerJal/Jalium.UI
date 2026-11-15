using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>The Css attached properties are settable from JALXAML markup.</summary>
public sealed class CssXamlIntegrationTests
{
    [Fact]
    public void CssStyle_AttachedProperty_ParsesFromXaml()
    {
        const string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Css.Style="opacity: .5; margin: 4px 8px; background-color: red" />
            """;

        var border = Assert.IsType<Border>(XamlReader.Parse(xaml));
        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(new Thickness(8, 4, 8, 4), border.Margin);
        var brush = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0, 0), brush.Color);
    }

    [Fact]
    public void CssClass_AttachedProperty_ParsesFromXaml()
    {
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Css.Class="title primary" />
            </Grid>
            """;

        var grid = Assert.IsType<Grid>(XamlReader.Parse(xaml));
        var textBlock = Assert.IsType<TextBlock>(Assert.Single(grid.Children));
        Assert.Equal(new[] { "primary", "title" }, textBlock.CssRuntimeState!.Classes);
    }

    [Fact]
    public void CssStyle_FallbackChannel_WorksFromXaml()
    {
        const string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Css.Style="horizontal-alignment: Center; cursor: pointer" />
            """;

        var border = Assert.IsType<Border>(XamlReader.Parse(xaml));
        Assert.Equal(HorizontalAlignment.Center, border.HorizontalAlignment);
        Assert.Same(Jalium.UI.Input.Cursors.Hand, border.Cursor);
    }
}
