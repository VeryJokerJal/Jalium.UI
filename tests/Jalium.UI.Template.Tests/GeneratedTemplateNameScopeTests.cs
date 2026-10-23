extern alias sg;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Parser = sg::Jalium.UI.Xaml.SourceGenerator.JalxamlParser;
using Generator = sg::Jalium.UI.Xaml.SourceGenerator.JalxamlSourceGenerator;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class GeneratedTemplateNameScopeTests
{
    private const string Prefix = "<UserControl xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"Harness.ScopeView\">";
    private const string Code = "namespace Harness { public partial class ScopeView : Jalium.UI.Controls.UserControl { public ScopeView() { InitializeComponent(); } } }";
    private static string Template(string kind,string attribute,string key) => $"<{kind} x:Key=\"{key}\"{(kind=="ControlTemplate"?" TargetType=\"Button\"":"")}><StackPanel {attribute}=\"Shared\"><TextBlock {attribute}=\"Inside\" Text=\"内容\" /></StackPanel></{kind}>";
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(UserControl).Assembly.Location).Append(typeof(Jalium.UI.Markup.XamlReader).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToImmutableArray());

    [Theory]
    [InlineData("ControlTemplate","x:Name")] [InlineData("DataTemplate","x:Name")]
    [InlineData("HierarchicalDataTemplate","x:Name")] [InlineData("ItemsPanelTemplate","x:Name")]
    [InlineData("ItemContainerTemplate","x:Name")]
    [InlineData("ControlTemplate","Name")] [InlineData("DataTemplate","Name")]
    [InlineData("HierarchicalDataTemplate","Name")] [InlineData("ItemsPanelTemplate","Name")]
    [InlineData("ItemContainerTemplate","Name")]
    public void TemplateNamesDoNotDeclareOrOverwriteTheComponentField(string kind,string attribute)
    {
        string xaml=Prefix+"<UserControl.Resources>"+Template(kind,attribute,"first")+Template(kind,attribute,"second")+"</UserControl.Resources><Grid x:Name=\"Shared\" /></UserControl>";
        var parsed=Parser.Parse(xaml,"ScopeView.jalxaml")!;
        Assert.Equal(new[]{"Shared"},parsed.NamedElements.Select(x=>x.Name));
        var output=Generate(xaml);AssertNoErrors(output.Compilation);
        var type=output.Compilation.GetTypeByMetadataName("Harness.ScopeView")!;
        Assert.Single(type.GetMembers("Shared").OfType<IFieldSymbol>());
        Assert.Empty(type.GetMembers("Inside"));
        var instance=Create(output.Compilation);var outer=instance.GetType().GetField("Shared",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance);
        Assert.Same(instance.FindName("Shared"),outer);Assert.IsType<Grid>(outer);Assert.Null(instance.FindName("Inside"));
        var first=(FrameworkTemplate)instance.Resources["first"]!;var second=(FrameworkTemplate)instance.Resources["second"]!;
        var one=Assert.IsType<StackPanel>(first.LoadContent());var two=Assert.IsType<StackPanel>(second.LoadContent());
        Assert.Equal("Shared",one.Name);Assert.Equal("Shared",two.Name);Assert.NotSame(one,two);
        Assert.Equal("Inside",Assert.IsType<TextBlock>(Assert.Single(one.Children)).Name);
        Assert.Equal("Inside",Assert.IsType<TextBlock>(Assert.Single(two.Children)).Name);
        Assert.Same(outer,instance.FindName("Shared"));
    }

    [Theory] [InlineData("ControlTemplate")] [InlineData("DataTemplate")]
    [InlineData("HierarchicalDataTemplate")] [InlineData("ItemsPanelTemplate")] [InlineData("ItemContainerTemplate")]
    public void TheNamedTemplateDeclarationItselfRemainsInTheComponentScope(string kind)
    {
        string xaml=Prefix+"<UserControl.Resources>"+Template(kind,"x:Name","first").Replace("x:Key=\"first\"","x:Key=\"first\" x:Name=\"Definition\"")+"</UserControl.Resources></UserControl>";
        var parsed=Parser.Parse(xaml,"ScopeView.jalxaml")!;
        Assert.Equal(new[]{"Definition"},parsed.NamedElements.Select(x=>x.Name));
        var output=Generate(xaml);AssertNoErrors(output.Compilation);var instance=Create(output.Compilation);
        Assert.Same(instance.Resources["first"],instance.FindName("Definition"));
    }

    [Fact]
    public void TheSameNameInTheActualComponentScopeStillProducesACompileError()
    {
        var output=Generate(Prefix+"<StackPanel><Border x:Name=\"Duplicate\"/><Border x:Name=\"Duplicate\"/></StackPanel></UserControl>");
        Assert.Contains(output.Compilation.GetDiagnostics(),x=>x.Id=="CS0102"&&x.Severity==DiagnosticSeverity.Error);
    }

    [Fact]
    public void ACustomOrdinaryControlNamedDataTemplateDoesNotIntroduceATemplateScope()
    {
        string xaml=Prefix.Replace("x:Class=","xmlns:local=\"clr-namespace:Harness\" x:Class=")+"<local:DataTemplate><Border x:Name=\"OrdinaryChild\"/></local:DataTemplate></UserControl>";
        var parsed=Parser.Parse(xaml,"ScopeView.jalxaml")!;Assert.Equal(new[]{"OrdinaryChild"},parsed.NamedElements.Select(x=>x.Name));
        var output=Generate(xaml,Code+" namespace Harness { public class DataTemplate : Jalium.UI.Controls.StackPanel {} }");AssertNoErrors(output.Compilation);
    }

    [Fact]
    public void TwoTemplatesWithoutAnOuterNamesakeCompileWithoutDuplicateFields()
    {
        string xaml=Prefix+"<UserControl.Resources>"+Template("DataTemplate","x:Name","first")+Template("DataTemplate","x:Name","second")+"</UserControl.Resources></UserControl>";
        var output=Generate(xaml);AssertNoErrors(output.Compilation);
        var type=output.Compilation.GetTypeByMetadataName("Harness.ScopeView")!;
        Assert.Empty(type.GetMembers("Shared"));Assert.Empty(type.GetMembers("Inside"));
    }

    [Fact]
    public void AnItemTemplatePropertyKeepsTheOwnerAndOuterNameFields()
    {
        string xaml=Prefix+"<StackPanel><ListBox x:Name=\"Owner\"><ListBox.ItemTemplate><DataTemplate><Border x:Name=\"Shared\"/></DataTemplate></ListBox.ItemTemplate></ListBox><Grid x:Name=\"Shared\"/></StackPanel></UserControl>";
        var parsed=Parser.Parse(xaml,"ScopeView.jalxaml")!;Assert.Equal(new[]{"Owner","Shared"},parsed.NamedElements.Select(x=>x.Name));
        var output=Generate(xaml);AssertNoErrors(output.Compilation);var instance=Create(output.Compilation);
        Assert.IsType<ListBox>(instance.FindName("Owner"));Assert.IsType<Grid>(instance.FindName("Shared"));
    }

    [Fact]
    public void NestedFactoriesKeepTheirNamesSeparateFromTheComponent()
    {
        string xaml=Prefix+"<UserControl.Resources><ControlTemplate x:Key=\"outer\" TargetType=\"Button\"><Border x:Name=\"Shared\"><Border.Resources><DataTemplate x:Key=\"inner\"><TextBlock x:Name=\"Shared\" Text=\"内层\"/></DataTemplate></Border.Resources></Border></ControlTemplate></UserControl.Resources><Grid x:Name=\"Shared\"/></UserControl>";
        var output=Generate(xaml);AssertNoErrors(output.Compilation);var instance=Create(output.Compilation);
        var outer=(ControlTemplate)instance.Resources["outer"]!;var first=Assert.IsType<Border>(outer.LoadContent());var second=Assert.IsType<Border>(outer.LoadContent());
        var one=Assert.IsType<TextBlock>(((DataTemplate)first.Resources["inner"]!).LoadContent());
        var two=Assert.IsType<TextBlock>(((DataTemplate)second.Resources["inner"]!).LoadContent());
        Assert.Equal("Shared",first.Name);Assert.Equal("Shared",one.Name);Assert.Equal("Shared",two.Name);Assert.NotSame(one,two);
        Assert.IsType<Grid>(instance.FindName("Shared"));
    }

    [Fact]
    public void SuppressingFieldsKeepsVisualNamesBindingTypesAndAotReferences()
    {
        string xaml=Prefix.Replace("x:Class=","xmlns:local=\"clr-namespace:Harness\" x:Class=")+"<UserControl.Resources><DataTemplate x:Key=\"model\" DataType=\"local:RowModel\"><TextBlock x:Name=\"ScopedLabel\" Text=\"{Binding Title}\"/></DataTemplate></UserControl.Resources></UserControl>";
        var parsed=Parser.Parse(xaml,"ScopeView.jalxaml")!;Assert.Empty(parsed.NamedElements);
        Assert.Contains(parsed.ReferencedElements,x=>x.ElementName=="TextBlock");Assert.Contains(parsed.DataTypeReferences,x=>x.ElementName=="RowModel");
        var templateNode=Assert.Single(Assert.Single(parsed.Root!.PropertyElements).Children);
        Assert.Contains(Assert.Single(templateNode.Children).Attributes,x=>x.LocalName=="Name"&&x.Value=="ScopedLabel");
        var output=Generate(xaml,Code+" namespace Harness { public class RowModel { public string Title => \"标题\"; } }");AssertNoErrors(output.Compilation);var instance=Create(output.Compilation);
        var label=Assert.IsType<TextBlock>(((DataTemplate)instance.Resources["model"]!).LoadContent());label.DataContext=Activator.CreateInstance(instance.GetType().Assembly.GetType("Harness.RowModel")!);
        Assert.Equal("ScopedLabel",label.Name);Assert.Equal("标题",label.Text);
    }

    [Fact]
    public void RegisteredNamespaceAliasesAlsoIntroduceTemplateScopes()
    {
        string xaml=Prefix.Replace("x:Class=", "xmlns:t=\"urn:template-scopes\" x:Class=")+"<UserControl.Resources>"+Template("t:DataTemplate","x:Name","first")+Template("t:DataTemplate","x:Name","second")+"</UserControl.Resources><Grid x:Name=\"Shared\"/></UserControl>";
        var output=Generate(xaml,"[assembly:Jalium.UI.Markup.XmlnsDefinition(\"urn:template-scopes\",\"Jalium.UI\",AssemblyName=\"Jalium.UI.Managed\")] "+Code);
        AssertNoErrors(output.Compilation);var instance=Create(output.Compilation);
        Assert.IsType<Grid>(instance.FindName("Shared"));
        Assert.IsType<StackPanel>(((DataTemplate)instance.Resources["first"]!).LoadContent());
        Assert.Empty(output.Compilation.GetTypeByMetadataName("Harness.ScopeView")!.GetMembers("Inside"));
    }

    [Theory]
    [InlineData("ControlTemplate","x:Name")] [InlineData("DataTemplate","x:Name")]
    [InlineData("HierarchicalDataTemplate","x:Name")] [InlineData("ItemsPanelTemplate","x:Name")]
    [InlineData("ItemContainerTemplate","x:Name")]
    [InlineData("ControlTemplate","Name")] [InlineData("DataTemplate","Name")]
    [InlineData("HierarchicalDataTemplate","Name")] [InlineData("ItemsPanelTemplate","Name")]
    [InlineData("ItemContainerTemplate","Name")]
    public void DuplicateNamesWithinOneTemplateAreRejected(string kind,string attribute)
    {
        string xaml=Prefix+"<UserControl.Resources>"+Template(kind,attribute,"first").Replace("=\"Inside\"","=\"Shared\"")+"</UserControl.Resources></UserControl>";
        var output=Generate(xaml,allowGeneratorErrors:true);
        Assert.Contains(output.Run.Diagnostics.Concat(output.Compilation.GetDiagnostics()),x=>x.Severity==DiagnosticSeverity.Error&&(x.Id=="JALXAML007"||x.Id=="CS0102"));
    }

    [Fact]
    public void DuplicateTemplateNameDiagnosticIdentifiesTheJalxamlLocation()
    {
        string xaml=Prefix+"\n<UserControl.Resources><DataTemplate x:Key=\"bad\"><StackPanel>\n<Border x:Name=\"Repeated\"/>\n<Border x:Name=\"Repeated\"/>\n</StackPanel></DataTemplate></UserControl.Resources></UserControl>";
        var output=Generate(xaml,allowGeneratorErrors:true);
        var diagnostic=Assert.Single(output.Run.Diagnostics.Where(x=>x.Id=="JALXAML007"));
        Assert.Equal(DiagnosticSeverity.Error,diagnostic.Severity);Assert.Contains("Repeated",diagnostic.GetMessage());
        Assert.Equal("/ScopeView.jalxaml",diagnostic.Location.GetLineSpan().Path);
        Assert.Equal(3,diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Theory] [InlineData("ControlTemplate")] [InlineData("DataTemplate")]
    [InlineData("HierarchicalDataTemplate")] [InlineData("ItemsPanelTemplate")] [InlineData("ItemContainerTemplate")]
    public void GeneratedInstancesRegisterTheirOwnNamedDescendants(string kind)
    {
        string xaml=Prefix+"<UserControl.Resources>"+Template(kind,"x:Name","first")+"</UserControl.Resources><Grid x:Name=\"Inside\"/></UserControl>";
        var output=Generate(xaml);AssertNoErrors(output.Compilation);var owner=Create(output.Compilation);
        var factory=(FrameworkTemplate)owner.Resources["first"]!;
        var one=Assert.IsType<StackPanel>(factory.LoadContent());var two=Assert.IsType<StackPanel>(factory.LoadContent());
        Assert.Same(one.Children[0],one.FindName("Inside"));Assert.Same(two.Children[0],two.FindName("Inside"));
        Assert.NotSame(one.FindName("Inside"),two.FindName("Inside"));Assert.IsType<Grid>(owner.FindName("Inside"));
    }

    [Theory] [InlineData("ControlTemplate")] [InlineData("DataTemplate")]
    [InlineData("HierarchicalDataTemplate")] [InlineData("ItemsPanelTemplate")] [InlineData("ItemContainerTemplate")]
    public void ElementNameBindingsStayInsideTheirOwnGeneratedInstance(string kind)
    {
        string target=kind=="ControlTemplate"?" TargetType=\"Button\"":"";
        string xaml=Prefix+"<UserControl.Resources><"+kind+" x:Key=\"first\""+target+"><StackPanel><TextBox x:Name=\"Source\" Text=\"内部\"/><TextBlock x:Name=\"Target\" Text=\"{Binding Text, ElementName=Source}\"/></StackPanel></"+kind+"></UserControl.Resources><StackPanel x:Name=\"Mount\"><TextBox x:Name=\"Source\" Text=\"外部\"/></StackPanel></UserControl>";
        var output=Generate(xaml);AssertNoErrors(output.Compilation);var owner=Create(output.Compilation);
        var factory=(FrameworkTemplate)owner.Resources["first"]!;
        var one=Assert.IsType<StackPanel>(factory.LoadContent());var two=Assert.IsType<StackPanel>(factory.LoadContent());
        var mount=Assert.IsType<StackPanel>(owner.FindName("Mount"));mount.Children.Add(one);mount.Children.Add(two);
        owner.Measure(new Size(600,600));owner.Arrange(new Rect(0,0,600,600));
        var firstSource=Assert.IsType<TextBox>(one.Children[0]);var secondSource=Assert.IsType<TextBox>(two.Children[0]);
        var firstTarget=Assert.IsType<TextBlock>(one.Children[1]);var secondTarget=Assert.IsType<TextBlock>(two.Children[1]);
        BindingOperations.ClearBinding(firstTarget,TextBlock.TextProperty);
        BindingOperations.ClearBinding(secondTarget,TextBlock.TextProperty);
        BindingOperations.SetBinding(firstTarget,TextBlock.TextProperty,new Binding("Text") { ElementName="Source" });
        BindingOperations.SetBinding(secondTarget,TextBlock.TextProperty,new Binding("Text") { ElementName="Source" });
        Assert.Equal("内部",firstTarget.Text);Assert.Equal("内部",secondTarget.Text);
        firstSource.Text="第一实例";secondSource.Text="第二实例";
        Assert.Equal("第一实例",firstTarget.Text);Assert.Equal("第二实例",secondTarget.Text);
        Assert.IsType<TextBox>(owner.FindName("Source")).Text="外部更新";
        Assert.Equal("第一实例",firstTarget.Text);Assert.Equal("第二实例",secondTarget.Text);
    }

    private static (Compilation Compilation,GeneratorDriverRunResult Run) Generate(string xaml,string code=Code,bool allowGeneratorErrors=false)
    {
        var compilation=CSharpCompilation.Create("ScopeChecks_"+Guid.NewGuid().ToString("N"),[CSharpSyntaxTree.ParseText(code)],References.Value,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,nullableContextOptions:NullableContextOptions.Enable));
        GeneratorDriver driver=CSharpGeneratorDriver.Create([new Generator().AsSourceGenerator()],[new Input("/ScopeView.jalxaml",xaml)],(CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver=driver.RunGeneratorsAndUpdateCompilation(compilation,out var output,out var diagnostics);
        if(!allowGeneratorErrors)Assert.DoesNotContain(diagnostics,x=>x.Severity==DiagnosticSeverity.Error);return(output,driver.GetRunResult());
    }
    private static void AssertNoErrors(Compilation compilation)
    {
        var errors=compilation.GetDiagnostics().Where(x=>x.Severity==DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length==0,string.Join(Environment.NewLine,errors.Select(x=>x.ToString())));
    }
    private static UserControl Create(Compilation compilation)
    {
        using var stream=new MemoryStream();var emitted=compilation.Emit(stream);Assert.True(emitted.Success,string.Join(Environment.NewLine,emitted.Diagnostics));
        return(UserControl)Activator.CreateInstance(Assembly.Load(stream.ToArray()).GetType("Harness.ScopeView")!)!;
    }
    private sealed class Input(string path,string text) : AdditionalText
    { public override string Path=>path;public override SourceText GetText(CancellationToken cancellationToken=default)=>SourceText.From(text); }
}
