using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssNamespaceTests
{
    static CssNamespaceTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(TypeConverterRegistry).Module.ModuleHandle);

    private static Border Element(string ns,string name="Box")
    {
        var box=new Border(); XamlBuilder.SetXmlIdentity(box,ns,name); Css.SetClass(box,"item"); return box;
    }
    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new(400,300)); root.Arrange(new(0,0,400,300)); root.UpdateLayout();
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }
    private static (StackPanel Root,Border A,Border B,Border None) Tree(string css)
    {
        var root=new StackPanel(); var a=Element("urn:A"); var b=Element("urn:B"); var none=Element("");
        root.Children.Add(a); root.Children.Add(b); root.Children.Add(none); Css.SetStyleSheet(root,css); Layout(root); return(root,a,b,none);
    }

    [Theory]
    [InlineData("a|Box",true,false,false)]
    [InlineData("a|*",true,false,false)]
    [InlineData("b|Box",false,true,false)]
    [InlineData("*|Box",true,true,true)]
    [InlineData("*|*",true,true,true)]
    [InlineData("|Box",false,false,true)]
    [InlineData("|*",false,false,true)]
    [InlineData("a|Box.item",true,false,false)]
    [InlineData("a/**/|/**/Box",true,false,false)]
    [InlineData("\\61|Box",true,false,false)]
    [InlineData("a|B\\6f x",true,false,false)]
    public void QualifiedTypeSelectors_MatchExpandedNames(string selector,bool expectedA,bool expectedB,bool expectedNone)
    {
        var (_,a,b,none)=Tree("@namespace a 'urn:A'; @namespace b url(urn:B); "+selector+"{width:40px}");
        Assert.Equal(expectedA?40:double.NaN,a.Width); Assert.Equal(expectedB?40:double.NaN,b.Width); Assert.Equal(expectedNone?40:double.NaN,none.Width);
    }

    [Theory]
    [InlineData("Box")]
    [InlineData("*")]
    [InlineData(".item")]
    [InlineData(":not(.absent)")]
    [InlineData(":is(.item)")]
    public void DefaultNamespace_ConstrainsExplicitAndImplicitUniversalSelectors(string selector)
    {
        var (_,a,b,none)=Tree("@namespace 'urn:A'; "+selector+"{width:40px}");
        Assert.Equal(40,a.Width); Assert.True(double.IsNaN(b.Width)); Assert.True(double.IsNaN(none.Width));
    }

    [Theory]
    [InlineData("*|*:is(.item)",true)]
    [InlineData("*|*:where(.item)",true)]
    [InlineData("*|*:not(.absent)",true)]
    [InlineData("*|*:is(*.item)",false)]
    [InlineData("*|*:where(Box)",false)]
    [InlineData("*|*:not(Box)",true)]
    public void FunctionalSubjects_FollowDefaultNamespaceExceptions(string selector,bool matchesForeign)
    {
        var (_,_,b,_)=Tree("@namespace 'urn:A'; "+selector+"{width:40px}");
        Assert.Equal(matchesForeign?40:double.NaN,b.Width);
    }

    [Fact]
    public void DefaultNamespaceInsideFunctions_StillConstrainsNonSubjectCompounds()
    {
        var root=new StackPanel(); var parent=Element("urn:A"); var child=Element("urn:B"); parent.Child=child; root.Children.Add(parent);
        Css.SetStyleSheet(root,"@namespace 'urn:A'; *|*:is(.item > .item) {width:40px}"); Layout(root);
        Assert.Equal(40,child.Width);
        XamlBuilder.SetXmlIdentity(parent,"urn:B","Box"); root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void EmptyDefaultNamespace_OnlyMatchesUnnamespacedElements()
    {
        var (_,a,b,none)=Tree("@namespace ''; .item{width:40px}");
        Assert.True(double.IsNaN(a.Width)); Assert.True(double.IsNaN(b.Width)); Assert.Equal(40,none.Width);
    }

    [Theory]
    [InlineData("UNKNOWN|Box")]
    [InlineData("a|Box, missing|Box")]
    [InlineData("a| Box")]
    [InlineData("a|/**/ Box")]
    [InlineData("a||Box")]
    public void InvalidNamespaceSelectors_DoNotBecomeUnqualifiedSelectors(string selector)
    {
        var (_,a,_,_)=Tree("@namespace a 'urn:A'; "+selector+"{width:99px} .item{height:20px}");
        Assert.True(double.IsNaN(a.Width)); Assert.Equal(20,a.Height);
    }

    [Fact]
    public void PrefixesAreCaseSensitiveAndTheLastDeclarationWins()
    {
        var (_,a,b,_)=Tree("@namespace p 'urn:A'; @namespace p 'urn:B'; @namespace P 'urn:A'; p|Box{width:40px} P|Box{height:20px}");
        Assert.True(double.IsNaN(a.Width)); Assert.Equal(20,a.Height); Assert.Equal(40,b.Width); Assert.True(double.IsNaN(b.Height));
    }

    [Theory]
    [InlineData("urn:A")]
    [InlineData("not a URI { literal }")]
    [InlineData("https://EXAMPLE.test/a/../b")]
    public void NamespaceNames_AreLiteralStringsWithoutIoOrUriNormalization(string ns)
    {
        var target=Element(ns);
        Css.SetStyleSheet(target,"@namespace p "+CssDeclarationValue.String(ns)+"; p|Box{width:40px}"); Layout(target);
        Assert.Equal(40,target.Width);
    }

    [Theory]
    [InlineData("[a|role='chosen']",true)]
    [InlineData("[b|role='chosen']",false)]
    [InlineData("[*|role='chosen']",true)]
    [InlineData("[|role='chosen']",false)]
    [InlineData("[role='chosen']",false)]
    [InlineData("[a/**/|/**/role='chosen']",true)]
    [InlineData("[a|role^='cho']",true)]
    [InlineData("[a|role='CHOSEN' i]",true)]
    [InlineData("[a|role]",true)]
    public void QualifiedAttributeSelectors_KeepNamespaceAndValueMatchingSeparate(string selector,bool expected)
    {
        var root=Element("urn:A");
        Css.SetAttribute(root,"urn:A","role","chosen"); Css.SetAttribute(root,"urn:B","role","other"); Css.SetAttribute(root,"role","plain");
        Css.SetStyleSheet(root,"@namespace a 'urn:A'; @namespace b 'urn:B'; "+selector+"{width:40px}"); Layout(root);
        Assert.Equal(expected?40:double.NaN,root.Width);
    }

    [Fact]
    public void WildcardAttributeNamespace_TestsEveryValueAndUpdatesDynamically()
    {
        var root=Element("urn:A");
        Css.SetAttribute(root,"urn:A","role","wrong"); Css.SetAttribute(root,"urn:B","role","right");
        Css.SetStyleSheet(root,"[*|role=right] {width:40px}"); Layout(root); Assert.Equal(40,root.Width);
        Css.SetAttribute(root,"urn:B","role",null); root.UpdateLayout(); Assert.True(double.IsNaN(root.Width));
        Css.SetAttribute(root,"urn:C","role","right"); root.UpdateLayout(); Assert.Equal(40,root.Width);
        Assert.Equal("right",Css.GetAttribute(root,"urn:C","role"));
    }

    [Fact]
    public void AttributeDefaultsIgnoreTheStylesheetDefaultNamespace()
    {
        var root=Element("urn:A"); Css.SetAttribute(root,"role","plain"); Css.SetAttribute(root,"urn:A","role","qualified");
        Css.SetStyleSheet(root,"@namespace 'urn:A'; [role=plain]{width:40px} [role=qualified]{width:99px}"); Layout(root);
        Assert.Equal(40,root.Width);
    }

    [Fact]
    public void DashMatchAttributeOperator_IsNotConfusedWithNamespaceSyntax()
    {
        var root=Element("urn:A"); Css.SetAttribute(root,"lang","en-US");
        Css.SetStyleSheet(root,"[lang|=en]{width:40px}"); Layout(root); Assert.Equal(40,root.Width);
    }

    [Fact]
    public void NamespaceQualifiedNames_WorkInNestingScopeAndNthFilters()
    {
        var root=new StackPanel(); var host=Element("urn:A","Host"); var children=new StackPanel(); host.Child=children; root.Children.Add(host);
        var a=Element("urn:A"); var b=Element("urn:B"); var second=Element("urn:A"); children.Children.Add(a); children.Children.Add(b); children.Children.Add(second);
        Css.SetStyleSheet(root,"@namespace a 'urn:A'; @scope (a|Host) { & { a|Box:nth-child(2 of a|Box){width:40px} } }");
        Layout(root); Assert.Equal(40,second.Width); Assert.True(double.IsNaN(a.Width)); Assert.True(double.IsNaN(b.Width));
    }

    [Fact]
    public void OfTypeUsesExpandedNamesAcrossNamespaceBoundaries()
    {
        var (root,a,b,none)=Tree(".item:first-of-type{width:40px}");
        Assert.Equal(40,a.Width); Assert.Equal(40,b.Width); Assert.Equal(40,none.Width);
        var second=Element("urn:A"); root.Children.Add(second); root.UpdateLayout(); Assert.True(double.IsNaN(second.Width));
        Css.SetStyleSheet(root,"@namespace a 'urn:A'; a|Box:nth-of-type(2){width:50px}"); root.UpdateLayout(); Assert.Equal(50,second.Width);
    }

    [Fact]
    public void NamespaceDeclarations_AreNotSharedBetweenStylesheets()
    {
        var root=Element("urn:A");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@namespace p 'urn:A'; p|Box{width:40px}"));
        var other=CssStyleSheet.Parse("p|Box{height:99px}"); Css.GetStyleSheets(root).Add(other); Layout(root);
        Assert.Equal(40,root.Width); Assert.True(double.IsNaN(root.Height)); Assert.Empty(other.Rules);
    }

    [Theory]
    [InlineData("Box{height:20px}")]
    [InlineData("@media all {}")]
    [InlineData("@supports (width:1px) {}")]
    [InlineData("@layer block {}")]
    public void MisplacedNamespaceRules_AreIgnored(string before)
    {
        var sheet=CssStyleSheet.Parse(before+" @namespace p 'urn:A'; p|Box{width:99px}");
        var root=Element("urn:A"); Css.GetStyleSheets(root).Add(sheet); Layout(root); Assert.True(double.IsNaN(root.Width));
        Assert.Contains(sheet.Diagnostics,d=>d.Message.Contains("@namespace"));
    }

    [Fact]
    public void NamespaceRules_AreAllowedAfterInitialLayerStatementsButNotInsideGroups()
    {
        var (_,a,_,_)=Tree("@layer base; @namespace p 'urn:A'; @layer base {p|Box{width:40px}}"); Assert.Equal(40,a.Width);
        var sheet=CssStyleSheet.Parse("@media all {@namespace p 'urn:A';p|Box{width:99px}}"); Assert.Empty(sheet.Rules);
    }

    [Fact]
    public void NamespaceNamesCanContainBracesAndDoNotCollideInTheAttributeBag()
    {
        var root=Element("urn:A");
        Css.SetAttribute(root,"a}b","c","one"); Css.SetAttribute(root,"a","b}c","two");
        Assert.Equal("one",Css.GetAttribute(root,"a}b","c")); Assert.Equal("two",Css.GetAttribute(root,"a","b}c"));
    }

    [Fact]
    public void RuntimeXaml_PreservesActualNamespaceAliasesAndLiveQualifiedProperties()
    {
        const string ns="urn:test:css:qualified-property";
        XmlnsDefinitionRegistry.AddXmlnsDefinition(ns,"Jalium.UI.Controls",typeof(Border).Assembly);
        var root=Assert.IsType<Border>(XamlReader.Parse($"<Border xmlns='{JalxamlNamespaces.LegacyJaliumUi}' xmlns:p='{ns}' p:Tag='first'/>"));
        Css.SetStyleSheet(root,$"@namespace ui '{JalxamlNamespaces.LegacyJaliumUi}'; @namespace p '{ns}'; ui|Border[p|Tag=first]{{width:40px}} [|Tag]{{height:99px}}");
        Layout(root); Assert.Equal(40,root.Width); Assert.True(double.IsNaN(root.Height));
        root.Tag="second"; root.UpdateLayout(); Assert.True(double.IsNaN(root.Width));
        root.Tag="first"; root.UpdateLayout(); Assert.Equal(40,root.Width);
    }

    [Fact]
    public void QualifiedAttributeBindings_RemainNativeBindings()
    {
        var root=new Border(); var model=new TextBlock {Text="first"};
        XamlBuilder.SetXmlIdentity(root,"urn:A","Box");
        XamlBuilder.RecordXmlAttribute(root,"urn:props","Tag","{Binding Text}");
        BindingOperations.SetBinding(root,FrameworkElement.TagProperty,new Binding(nameof(model.Text)){Source=model});
        Css.SetStyleSheet(root,"@namespace p 'urn:props'; [p|Tag=first]{width:40px}"); Layout(root); Assert.Equal(40,root.Width);
        model.Text="second"; root.UpdateLayout(); Assert.True(double.IsNaN(root.Width));
        Assert.NotNull(BindingOperations.GetBindingExpression(root,FrameworkElement.TagProperty));
    }

    [Fact]
    public void MarkupDirectivePrefixes_AreIndependentOfCssPrefixes()
    {
        var root=Assert.IsType<Border>(XamlReader.Parse($"<Border xmlns='{JalxamlNamespaces.Presentation}' xmlns:q='{JalxamlNamespaces.WpfXamlMarkup}' q:Name='named'/>"));
        Css.SetStyleSheet(root,$"@namespace x '{JalxamlNamespaces.WpfXamlMarkup}'; [x|Name=named]{{width:40px}}");
        Layout(root); Assert.Equal("named",root.Name); Assert.Equal(40,root.Width);
        root.Name="renamed"; root.UpdateLayout(); Assert.True(double.IsNaN(root.Width));
    }

    [Fact]
    public void DocumentNodes_KeepNamespaceIdentityAndOriginalText()
    {
        var paragraph=Assert.IsType<Paragraph>(XamlReader.Parse($"<Paragraph xmlns='{JalxamlNamespaces.LegacyJaliumUi}'><Run>原始文本</Run></Paragraph>"));
        var run=Assert.IsType<Run>(Assert.Single(paragraph.Inlines));
        Css.SetStyleSheet(paragraph,$"@namespace ui '{JalxamlNamespaces.LegacyJaliumUi}'; ui|Run{{font-size:24px}}");
        for(var i=0;i<4 && CssEvaluationScheduler.HasPending(paragraph.Dispatcher);i++) CssEvaluationScheduler.FlushIfPending(paragraph.Dispatcher);
        Assert.Equal(24,run.FontSize); Assert.Equal("原始文本",run.Text);
    }

    [Fact]
    public void QualifiedStyles_StillRespectNativeLocalValuesAndRestoreAfterRemoval()
    {
        var root=Element("urn:A"); root.Width=70;
        Css.SetStyleSheet(root,"@namespace a 'urn:A'; a|Box{width:300px!important; height:30px}"); Layout(root);
        Assert.Equal(70,root.Width); Assert.Equal(30,root.Height);
        Css.SetStyleSheet(root,""); root.UpdateLayout(); Assert.Equal(70,root.Width); Assert.True(double.IsNaN(root.Height));
    }

    [Fact]
    public void SupportsSelector_UsesDeclaredNamespacesAndRejectsUnknownPrefixes()
    {
        var root=Element("urn:A");
        Css.SetStyleSheet(root,"@namespace a 'urn:A'; @supports selector(a/**/|Box) {a|Box{width:40px}} @supports not selector(missing|Box) {*|Box{height:99px}}");
        Layout(root); Assert.Equal(40,root.Width); Assert.Equal(99,root.Height);
    }

    [Fact]
    public void SupportsSelector_DoesNotTreatForgivingInvalidBranchesAsSupported()
    {
        var root=Element("urn:A");
        Css.SetStyleSheet(root,"@namespace a 'urn:A'; @supports selector(:is(a|Box,:unknown)) {*|Box{width:99px}} :is(a|Box,:unknown){height:40px}");
        Layout(root); Assert.True(double.IsNaN(root.Width)); Assert.Equal(40,root.Height);
    }

    [Fact]
    public void SelectorComments_PreserveTokenBoundariesWithoutBecomingWhitespace()
    {
        var root=Element("urn:A"); Css.SetClass(root,"first second");
        Css.SetStyleSheet(root,".first/**/.second{width:40px} .fir/**/st{height:99px} .first:is(/*)*/.second){opacity:.3}");
        Layout(root); Assert.Equal(40,root.Width); Assert.True(double.IsNaN(root.Height)); Assert.Equal(.3,root.Opacity);
    }

    private sealed class Sheets(Dictionary<string,string> sources) : ICssResourceResolver
    {
        public ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CssResource(uri,new MemoryStream(Encoding.UTF8.GetBytes(sources[uri.AbsolutePath]))));
        }
    }

    [Fact]
    public async Task ImportedNamespaces_AreIndependentOfTheImportingSheet()
    {
        var resources=new Sheets(new()
        {
            ["/main.css"]="@import 'part.css'; @namespace p 'urn:B'; p|Box{height:30px}",
            ["/part.css"]="@namespace p 'urn:A'; p|Box{width:40px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"),resources);
        var (root,a,b,_)=Tree(""); Css.GetStyleSheets(root).Add(sheet); Layout(root);
        Assert.Equal(40,a.Width); Assert.True(double.IsNaN(a.Height)); Assert.Equal(30,b.Height); Assert.True(double.IsNaN(b.Width));
    }

    [Fact]
    public async Task NamespaceDeclaredAfterImport_IsAvailableToItsSupportCondition()
    {
        var resources=new Sheets(new()
        {
            ["/main.css"]="@import 'part.css' supports(selector(p|Box)); @namespace p 'urn:A';",
            ["/part.css"]="*|Box{width:40px} p|Box{height:99px}",
        });
        var sheet=await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"),resources);
        var root=Element("urn:A"); Css.GetStyleSheets(root).Add(sheet); Layout(root);
        Assert.Equal(40,root.Width); Assert.True(double.IsNaN(root.Height));
    }

    [Fact]
    public void NativeCodeCreatedControls_HaveAStableFallbackName()
    {
        var root=new Border(); Css.SetStyleSheet(root,$"@namespace ui '{JalxamlNamespaces.Presentation}'; ui|Border{{width:40px}}");
        Layout(root); Assert.Equal(40,root.Width);
    }

    [Fact]
    public void XmlAttributeMetadata_RefreshesDuringRebuildWithoutReplacingBindings()
    {
        var root=Element("urn:A"); root.Tag="present";
        Css.SetStyleSheet(root,"@namespace p 'urn:props'; [p|Tag]{width:40px}"); Layout(root); Assert.True(double.IsNaN(root.Width));
        XamlBuilder.RecordXmlAttribute(root,"urn:props","Tag","present"); root.UpdateLayout(); Assert.Equal(40,root.Width);
        XamlBuilder.SetXmlIdentity(root,"urn:A","Box",resetAttributes:true); root.UpdateLayout(); Assert.True(double.IsNaN(root.Width));
        Assert.Equal("present",root.Tag);
    }

    private sealed class DerivedBorder : Border { }

    [Fact]
    public void UnqualifiedNativeTypeSelectors_KeepTheirExistingDerivedTypeBehavior()
    {
        var root=new DerivedBorder(); Css.SetStyleSheet(root,"Border{width:40px}"); Layout(root); Assert.Equal(40,root.Width);
    }

    [Fact]
    public void QualifiedSelectors_DoNotCrossTemplateIsolation()
    {
        Border? part=null; var template=new ControlTemplate(typeof(Button));
        template.SetVisualTree(()=>part=new Border{Child=new ContentPresenter()});
        var button=new Button{Template=template}; button.ApplyTemplate();
        var content=new Border(); button.Content=content; var root=new Border{Child=button};
        Css.SetStyleSheet(root,$"@namespace ui '{JalxamlNamespaces.Presentation}'; ui|Button ui|Border{{opacity:.3}}");
        Layout(root); Assert.Equal(.3,content.Opacity); Assert.NotNull(part); Assert.Equal(1,part.Opacity);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Root,IDictionary<string,string> Attributes) HoldAttributeBag()
    {
        var root=Element("urn:A"); Css.SetAttribute(root,"urn:props","mode","active");
        Css.SetStyleSheet(root,"@namespace p 'urn:props'; [p|mode=active]{width:40px}"); Layout(root);
        return(new WeakReference(root),Css.GetAttributes(root));
    }

    [Fact]
    public void NamespaceMetadataAndObservableAttributes_DoNotRetainNativeControls()
    {
        var held=HoldAttributeBag();
        for(var i=0;i<3 && held.Root.IsAlive;i++) {GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();}
        Assert.False(held.Root.IsAlive); held.Attributes.Clear(); GC.KeepAlive(held.Attributes);
    }
}
