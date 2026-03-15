using Jalium.UI.Controls;
using Jalium.UI.Gpu;
using Jalium.UI.Markup;
using ControlGridLength = Jalium.UI.Controls.GridLength;

namespace Jalium.UI.Tests;

public class GridSyntaxTests
{
    [Fact]
    public void XamlReader_ShouldParseCompactGridDefinitions_AndNamedGridPositions()
    {
        const string xaml = """
<Grid RowDefinitions="Auto,*,{Name=bottomrow Height=40}"
      ColumnDefinitions="60,*,{Name=rightcol Width=70}">
  <Border Name="Target"
          Grid.Row="bottomrow"
          Grid.Column="rightcol" />
</Grid>
""";

        var grid = Assert.IsType<Grid>(XamlReader.Parse(xaml));
        var target = Assert.IsType<Border>(Assert.Single(grid.Children));

        Assert.Equal(3, grid.RowDefinitions.Count);
        Assert.True(grid.RowDefinitions[0].Height.IsAuto);
        Assert.True(grid.RowDefinitions[1].Height.IsStar);
        Assert.Equal("bottomrow", grid.RowDefinitions[2].Name);
        Assert.Equal(new ControlGridLength(40), grid.RowDefinitions[2].Height);

        Assert.Equal(3, grid.ColumnDefinitions.Count);
        Assert.Equal(new ControlGridLength(60), grid.ColumnDefinitions[0].Width);
        Assert.True(grid.ColumnDefinitions[1].Width.IsStar);
        Assert.Equal("rightcol", grid.ColumnDefinitions[2].Name);
        Assert.Equal(new ControlGridLength(70), grid.ColumnDefinitions[2].Width);

        Assert.Equal(2, Grid.GetRow(target));
        Assert.Equal(2, Grid.GetColumn(target));
    }

    [Fact]
    public void XamlReader_ShouldResolveNamedGridPositions_WhenDefinitionsAppearAfterChild()
    {
        const string xaml = """
<Grid>
  <Border Name="Target"
          Grid.Row="bottomrow"
          Grid.Column="rightcol" />
  <Grid.RowDefinitions>
    <RowDefinition Height="*" />
    <RowDefinition Name="bottomrow" Height="40" />
  </Grid.RowDefinitions>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Name="rightcol" Width="70" />
  </Grid.ColumnDefinitions>
</Grid>
""";

        var grid = Assert.IsType<Grid>(XamlReader.Parse(xaml));
        var target = Assert.IsType<Border>(Assert.Single(grid.Children));

        Assert.Equal(1, Grid.GetRow(target));
        Assert.Equal(1, Grid.GetColumn(target));
    }

    [Fact]
    public void UiCompiler_ShouldResolveNamedGridPositions_FromCompactSyntax()
    {
        const string xaml = """
<Grid Width="300"
      Height="300"
      RowDefinitions="100,100,{Name=bottomrow Height=100}"
      ColumnDefinitions="100,100,{Name=rightcol Width=100}">
  <Border Grid.Row="bottomrow"
          Grid.Column="rightcol" />
</Grid>
""";

        var compiler = new UICompiler
        {
            Options = new CompilerOptions
            {
                ViewportWidth = 300,
                ViewportHeight = 300
            }
        };

        var bundle = compiler.CompileSource(xaml);
        var childNode = Assert.IsType<RectNode>(Assert.Single(bundle.Nodes, static node => node.ParentId != 0));

        Assert.Equal(200, childNode.Bounds.X);
        Assert.Equal(200, childNode.Bounds.Y);
    }
}
