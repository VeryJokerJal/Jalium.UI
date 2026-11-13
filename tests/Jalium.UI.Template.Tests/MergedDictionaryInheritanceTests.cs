using Jalium.UI.Controls;
using Jalium.UI.Markup;
namespace Jalium.UI.Tests;
public partial class GeneratedAmbientParent : ResourceDictionary { public GeneratedAmbientParent()=>InitializeComponent(); }
public partial class GeneratedAmbientChild : ResourceDictionary { public GeneratedAmbientChild()=>InitializeComponent(); }

[Collection("macOS Window globals")]
public sealed class MergedDictionaryInheritanceTests
{
    [Fact]
    public void DeferredMergedValueCanReadAnotherKeyThroughItsDeclaringRoot()
    {
        ThemeLoader.Initialize();var root=new ResourceDictionary();var bases=new ResourceDictionary();var derived=new ResourceDictionary();
        var basis=new Style(typeof(Button));bases["base"]=basis;root.MergedDictionaries.Add(bases);root.MergedDictionaries.Add(derived);
        XamlBuilder.AddDeferredResource(derived,"derived",(_,_)=>new Style(typeof(Button),(Style)root["base"]!));
        Assert.Same(basis,Assert.IsType<Style>(root["derived"]).BasedOn);
    }
    [Fact]
    public void DeferredFactoryRestoresItsTypeKeyResourceScope()
    {
        ThemeLoader.Initialize();var parent=new ResourceDictionary();var basis=new Style(typeof(Button));parent[typeof(Button)]=basis;var child=new ResourceDictionary();
        using(TemplateAmbientResourceContext.Push(new[]{parent}))
            XamlBuilder.AddDeferredResource(child,"leaf",static(owner,context)=>
            {
                var previous=XamlBuilder.PushNamespaces(context,new[]{"","x"},new[]{"http://schemas.microsoft.com/winfx/2006/xaml/presentation","http://schemas.microsoft.com/winfx/2006/xaml"});
                var result=new Style(typeof(Button));XamlBuilder.PushParent(result,context);
                XamlBuilder.SetProperty(result,"BasedOn","{StaticResource {x:Type Button}}",context);
                XamlBuilder.PopParent(context);XamlBuilder.PopNamespaces(context,previous);return result;
            });
        Assert.Same(basis,Assert.IsType<Style>(child["leaf"]).BasedOn);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceDictionariesKeepTheDeclaringScopeThroughDeferredStyleCreation(bool generated)
    {
        const string parent="<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Style x:Key='Ambient.Base' TargetType='Button'><Setter Property='FontSize' Value='27'/></Style><Style x:Key='{x:Type Button}' TargetType='Button'><Setter Property='FontSize' Value='31'/></Style><ResourceDictionary.MergedDictionaries><ResourceDictionary Source='ambient-child.jalxaml'/></ResourceDictionary.MergedDictionaries></ResourceDictionary>";
        const string child="<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Style x:Key='Ambient.Derived' TargetType='Button' BasedOn='{StaticResource Ambient.Base}'><Setter Property='Padding' Value='9'/></Style><Style x:Key='Ambient.Typed' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'/></ResourceDictionary>";
        ThemeLoader.Initialize();var loader=ResourceDictionary.SourceLoader;var before=TemplateAmbientResourceContext.Current;
        try
        {
            ResourceDictionary.SourceLoader=(_,_,_)=>generated?new GeneratedAmbientChild():Assert.IsType<ResourceDictionary>(XamlReader.Parse(child));
            var root=generated?new GeneratedAmbientParent():Assert.IsType<ResourceDictionary>(XamlReader.Parse(parent));
            Assert.Same(before,TemplateAmbientResourceContext.Current);
            // Generated styles are still deferred here: their ambient Source scope has ended.
            var derived=Assert.IsType<Style>(root["Ambient.Derived"]);Assert.Same(root["Ambient.Base"],derived.BasedOn);
            Assert.Same(root[typeof(Button)],Assert.IsType<Style>(root["Ambient.Typed"]).BasedOn);
            var button=new Button{Style=derived};button.ApplyTemplate();Assert.Equal(27,button.FontSize);Assert.Equal(new Thickness(9),button.Padding);
            Assert.Same(before,TemplateAmbientResourceContext.Current);
        }
        finally{ResourceDictionary.SourceLoader=loader;}
    }
}
