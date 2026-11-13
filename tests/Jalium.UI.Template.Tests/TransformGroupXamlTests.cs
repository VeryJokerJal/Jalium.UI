using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public partial class GeneratedTransformGroupTheme : ResourceDictionary
{
    public GeneratedTransformGroupTheme() => InitializeComponent();
}

[Collection("macOS Window globals")]
public sealed class TransformGroupXamlTests
{
    [Fact]
    public void ImplicitChildrenProduceTheSameMatrixInCompiledAndRuntimeXaml()
    {
        const string xml = "<TransformGroup xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ScaleTransform ScaleX='2' ScaleY='3'/><TranslateTransform X='10' Y='20'/></TransformGroup>";
        var parsed = Assert.IsType<TransformGroup>(XamlReader.Parse(xml));
        var generated = Assert.IsType<TransformGroup>(new GeneratedTransformGroupTheme()["Composite"]);
        Assert.Equal(2, parsed.Children.Count); Assert.Equal(2, generated.Children.Count);
        Assert.Equal(new Point(12, 26), parsed.Value.Transform(new Point(1, 2))); Assert.Equal(parsed.Value, generated.Value);
    }
    [Fact]
    public void CompiledTemplateCreatesIndependentChildTransformCollections()
    {
        var template = Assert.IsType<ControlTemplate>(new GeneratedTransformGroupTheme()["TransformedTemplate"]);
        var a = new Control { Template = template }; var b = new Control { Template = template }; a.ApplyTemplate(); b.ApplyTemplate();
        var first = Assert.IsType<TransformGroup>(Assert.IsType<Border>(a.GetVisualChild(0)).RenderTransform);
        var second = Assert.IsType<TransformGroup>(Assert.IsType<Border>(b.GetVisualChild(0)).RenderTransform);
        Assert.NotSame(first.Children, second.Children); Assert.Equal(2, first.Children.Count); Assert.Equal(2, second.Children.Count);
        Assert.IsType<ScaleTransform>(first.Children[0]).ScaleX = 5;
        Assert.Equal(new Point(15, 26), first.Value.Transform(new Point(1, 2))); Assert.Equal(new Point(12, 26), second.Value.Transform(new Point(1, 2)));
    }
}
