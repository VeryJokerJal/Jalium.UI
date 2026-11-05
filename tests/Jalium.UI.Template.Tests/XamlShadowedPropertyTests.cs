using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class XamlShadowedPropertyTests
{
    [Fact]
    public void AttributeSelectsTheDerivedEditorLanguage()
    {
        var editor = Assert.IsType<EditControl>(XamlReader.Parse("<EditControl Language=\"csharp\" Text=\"class Example {}\" />"));
        Assert.Equal("csharp", editor.Language);
    }

    [Fact]
    public void PropertyElementSelectsTheDerivedEditorLanguage()
    {
        var editor = Assert.IsType<EditControl>(XamlReader.Parse("<EditControl xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><EditControl.Language><x:String>xml</x:String></EditControl.Language></EditControl>"));
        Assert.Equal("xml", editor.Language);
    }

    [Fact]
    public void DeferredControlTemplateSelectsTheDerivedEditorLanguage()
    {
        var template = Assert.IsType<ControlTemplate>(XamlReader.Parse("<ControlTemplate TargetType=\"ContentControl\"><EditControl Language=\"json\" /></ControlTemplate>"));
        var host = new ContentControl { Template = template }; host.ApplyTemplate();
        Assert.Equal("json", Assert.IsType<EditControl>(host.GetVisualChild(0)).Language);
    }

    [Fact]
    public void OrdinaryInheritedLanguageKeepsItsOriginalType()
    {
        var text = Assert.IsType<TextBox>(XamlReader.Parse("<TextBox Language=\"en-US\" />"));
        Assert.Equal("en-us", text.Language.IetfLanguageTag);
    }
}
