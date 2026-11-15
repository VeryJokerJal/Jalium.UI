using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class GeneratedResourceKeysTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentTypeAndEscapedKeysMatchInCompiledAndRuntimeResources(bool compiled)
    {
        ThemeLoader.Initialize();
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GeneratedResourceKeysView.jalxaml"))
            .Replace("x:Class=\"Jalium.UI.Tests.GeneratedResourceKeysView\"", "");
        UserControl view = compiled ? new GeneratedResourceKeysView() : Assert.IsType<UserControl>(XamlReader.Parse(source));
        Assert.IsType<Style>(view.Resources[DataGridTextColumn.DefaultElementStyleKey]);
        Assert.IsType<Style>(view.Resources[typeof(TextBox)]);
        Assert.IsType<Style>(view.Resources["{literal}"]);
        Assert.False(view.Resources.ContainsKey("{x:Static DataGridTextColumn.DefaultElementStyleKey}"));
        var scope = Assert.IsType<Border>(view.Content);
        Assert.IsType<Style>(scope.Resources[DataGridCheckBoxColumn.DefaultElementStyleKey]);
        Assert.Equal(31, Assert.IsType<TextBlock>(scope.Child).FontSize);
    }
}

public partial class GeneratedResourceKeysView : UserControl
{
    public GeneratedResourceKeysView() => InitializeComponent();
}
