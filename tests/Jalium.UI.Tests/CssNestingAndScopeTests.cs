using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssNestingAndScopeTests
{
    static CssNestingAndScopeTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new(600,400)); root.Arrange(new(0,0,600,400)); root.UpdateLayout();
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    private static (StackPanel Root, StackPanel Card, Border Child) Tree(string css)
    {
        var child = new Border(); var card = new StackPanel(); var root = new StackPanel();
        card.Children.Add(child); root.Children.Add(card);
        Css.SetClass(card,"card"); Css.SetClass(child,"child"); Css.SetStyleSheet(root,css);
        Layout(root); return (root,card,child);
    }

    [Theory]
    [InlineData(".card { .child {width:40px} }")]
    [InlineData(".card { & .child {width:40px} }")]
    [InlineData(".card { > .child {width:40px} }")]
    [InlineData(".card { & > .child {width:40px} }")]
    [InlineData(".card { Border.child {width:40px} }")]
    [InlineData(".card { Border:is(.child) {width:40px} }")]
    [InlineData(".card { :is(&) .child {width:40px} }")]
    [InlineData(".card { :where(&) .child {width:40px} }")]
    [InlineData(".card { &:has(> .child) .child {width:40px} }")]
    [InlineData(".card { && > .child {width:40px} }")]
    public void NestingSelectors_ReuseTheOriginalNativeTree(string css)
    {
        var (_,card,child) = Tree(css); Assert.Equal(40,child.Width);
        Assert.Same(card,child.VisualParent); Assert.Same(child,card.Children[0]);
    }

    [Fact]
    public void NestedSelectorLists_UseTheMaximumParentSpecificity()
    {
        var (_,_,child) = Tree(".card, #unused { .child {width:40px} } .card .child {width:80px}");
        Assert.Equal(40,child.Width);
    }

    [Fact]
    public void NestedDeclarations_PreserveTheirPositionAndIndividualParentSpecificities()
    {
        var root = new Border(); Css.SetClass(root,"card");
        Css.SetStyleSheet(root,".card, #unused {width:10px; & {width:20px} width:30px; :where(&) {height:20px} height:30px}");
        Layout(root); Assert.Equal(20,root.Width); Assert.Equal(30,root.Height);
        Css.SetStyleSheet(root,".card {width:10px; & {width:20px} width:30px}");
        root.UpdateLayout(); Assert.Equal(30,root.Width);
    }

    [Fact]
    public void NestedGroupRules_KeepDeclarationOrderAndTheParentSelector()
    {
        var (_,card,child) = Tree("""
            .card {
              width:200px;
              @media (min-width:500px) {
                width:300px;
                @supports (height:10px) { > .child {height:10px} }
                height:80px;
              }
              width:400px;
            }
            """);
        Assert.Equal(400,card.Width); Assert.Equal(80,card.Height); Assert.Equal(10,child.Height);
    }

    [Fact]
    public void NestingInLayersAndContainerQueries_UsesExistingCascadeAndLayout()
    {
        var (root,card,child) = Tree("""
            @layer base, app;
            @layer base {.card { .child {width:20px} }}
            @layer app {.card { @container host (width > 300px) { .child {width:40px} } }}
            """);
        Css.SetStyle(root,"container:host / inline-size"); Layout(root); Assert.Equal(40,child.Width);
        Assert.Same(card,child.VisualParent);
    }

    [Fact]
    public void NestingSelector_CanAppearAfterAncestorsAndAcrossSiblingCombinators()
    {
        var (root,card,child) = Tree(".child { .card & {width:40px} } .card { + .after {height:30px} }");
        var after = new Border(); root.Children.Add(after); Css.SetClass(after,"after"); Layout(root);
        Assert.Equal(40,child.Width); Assert.Equal(30,after.Height); Assert.Same(root,card.VisualParent);
    }

    [Fact]
    public void EscapedAndQuotedAmpersands_DoNotBecomeNestingSelectors()
    {
        var (root,_,child) = Tree(".card { [data-mark='&'] {width:40px} .\\& {height:20px} }");
        Css.SetAttribute(child,"data-mark","&"); Css.SetClass(child,"child &"); Layout(root);
        Assert.Equal(40,child.Width); Assert.Equal(20,child.Height);
    }

    [Theory]
    [InlineData("&Border")]
    [InlineData("&-suffix")]
    [InlineData("[data-x]Border")]
    public void InvalidConcatenatedSelectors_AreNotAcceptedAsDescendants(string selector)
    {
        var (_,_,child) = Tree(".card { " + selector + " {width:99px} .child {height:30px} }");
        Assert.True(double.IsNaN(child.Width)); Assert.Equal(30,child.Height);
    }

    [Fact]
    public void CustomPropertyBlocks_RemainDeclarationTokensWhenRulesAreNested()
    {
        var (_,card,child) = Tree(".card { --tokens:{ a:b; nested:{c:d} }; --w:30px; .child {width:var(--w)} }");
        Assert.Contains("nested:{c:d}",CssNode.Get(card).CssRuntimeState!.CustomProperties!["--tokens"]);
        Assert.Equal(30,child.Width);
    }

    [Fact]
    public void TopLevelNestingSelector_ActsAsScopeWithZeroSpecificity()
    {
        var root = new Border(); Css.SetStyleSheet(root,"& {width:40px} :scope {width:60px} & {height:20px}");
        Layout(root); Assert.Equal(60,root.Width); Assert.Equal(20,root.Height);
        Assert.True(new CssCondition("supports","selector(&)").Evaluate(root));
    }

    [Fact]
    public void LargeParentListsAndRepeatedNesting_StaySharedInsteadOfExpanding()
    {
        var selector = string.Join(",",Enumerable.Range(0,12).Select(i => ".card" + i));
        var css = selector + " {" + string.Concat(Enumerable.Repeat("&& {",14)) + "width:40px;" + new string('}',15);
        var root = new Border(); Css.SetClass(root,"card0"); var sheet = CssStyleSheet.Parse(css);
        Css.GetStyleSheets(root).Add(sheet); Layout(root); Assert.Equal(40,root.Width);
        Assert.Single(sheet.Rules); Assert.Single(sheet.Rules[0].Selectors);
    }

    [Theory]
    [InlineData("@scope (.card) { .child {width:40px} }")]
    [InlineData("@scope (.card) { > .child {width:40px} }")]
    [InlineData("@scope (.card) { & > .child {width:40px} }")]
    [InlineData("@scope (.card) { :scope > .child {width:40px} }")]
    [InlineData("@scope (.missing, .card) { .child {width:40px} }")]
    public void ScopeRootsAndRelativeSelectors_LimitTheRuleSubject(string css)
    {
        var (root,_,child) = Tree(css); var outside = new Border(); root.Children.Add(outside); Css.SetClass(outside,"child"); Layout(root);
        Assert.Equal(40,child.Width); Assert.True(double.IsNaN(outside.Width));
    }

    [Fact]
    public void ScopeRoot_UsesExplicitScopeOrAmpersandAndDoesNotAddHeaderSpecificity()
    {
        var (root,card,_) = Tree("@scope (#named) { & {width:40px} :scope {height:30px} } .card {width:60px}");
        card.Name = "named"; Layout(root); Assert.Equal(60,card.Width); Assert.Equal(30,card.Height);
    }

    [Fact]
    public void ScopeLimits_ExcludeTheirOwnBoxesAndAllDescendants()
    {
        var (root,card,child) = Tree("@scope (.card) to (.stop) { .child {width:40px} }");
        var stop = new StackPanel(); var hiddenChild = new Border(); stop.Children.Add(hiddenChild); card.Children.Add(stop);
        Css.SetClass(stop,"stop child"); Css.SetClass(hiddenChild,"child"); Layout(root);
        Assert.Equal(40,child.Width); Assert.True(double.IsNaN(stop.Width)); Assert.True(double.IsNaN(hiddenChild.Width));
        Css.SetClass(stop,"child"); root.UpdateLayout(); Assert.Equal(40,hiddenChild.Width);
    }

    [Fact]
    public void LimitSelectors_CanUseTheScopeRootAndAncestorsOutsideTheScope()
    {
        var (root,card,child) = Tree("@scope (.card) to (.outside :scope > .stop) { .child {width:40px} }");
        Css.SetClass(root,"outside"); var stop = new StackPanel(); card.Children.Remove(child); stop.Children.Add(child); card.Children.Add(stop);
        Css.SetClass(stop,"stop"); Layout(root); Assert.True(double.IsNaN(child.Width));
        Css.SetClass(root,""); root.UpdateLayout(); Assert.Equal(40,child.Width);
    }

    [Fact]
    public void ScopeBoundaries_DoNotStopNativePropertyInheritance()
    {
        var text = new TextBlock { Text = "original" }; var stop = new Border { Child = text }; var root = new Border { Child = stop };
        Css.SetClass(root,"card"); Css.SetClass(stop,"stop");
        Css.SetStyleSheet(root,"@scope (.card) to (.stop) { :scope {color:red} TextBlock {font-size:40px} }");
        Layout(root); Assert.Equal(Colors.Red,Assert.IsType<SolidColorBrush>(text.Foreground).Color);
        Assert.NotEqual(40,text.FontSize); Assert.Equal("original",text.Text);
    }

    [Fact]
    public void ScopeProximity_IsComparedAfterSpecificityAndBeforeSourceOrder()
    {
        var (root,_,child) = Tree("""
            @scope (.card) {.child {width:40px; height:20px}}
            @scope (:root) {.child {width:60px} #target {height:30px}}
            .child {width:80px}
            """);
        child.Name = "target"; Layout(root); Assert.Equal(40,child.Width); Assert.Equal(30,child.Height);
    }

    [Fact]
    public void ScopeProximity_DoesNotOverrideLayerImportanceOrInlinePrecedence()
    {
        var (root,_,child) = Tree("""
            @layer low, high;
            @layer low {@scope (.card) {.child {width:40px!important; height:20px}}}
            @layer high {.child {width:60px!important; height:30px}}
            """);
        Assert.Equal(40,child.Width); Assert.Equal(30,child.Height);
        Css.SetStyle(child,"width:80px!important"); root.UpdateLayout(); Assert.Equal(80,child.Width);
    }

    [Fact]
    public void OverlappingScopeRoots_ChooseTheNearestValidRoot()
    {
        var (root,card,child) = Tree("@scope (.card) { :scope > .child {width:40px} }");
        Css.SetClass(root,"card"); Layout(root); Assert.Equal(40,child.Width);
        Css.SetClass(card,""); root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void ImplicitScope_UsesTheStylesheetHost()
    {
        var (root,card,child) = Tree(""); var outside = new Border(); root.Children.Add(outside); Css.SetClass(outside,"child");
        Css.SetStyleSheet(card,"@scope { .child {width:40px} & {height:80px} }"); Layout(root);
        Assert.Equal(40,child.Width); Assert.Equal(80,card.Height); Assert.True(double.IsNaN(outside.Width));
    }

    [Fact]
    public void DirectScopeDeclarations_TargetOnlyTheRootWithZeroSpecificity()
    {
        var (root,card,child) = Tree("@scope (.card) {width:40px; & {width:60px} width:80px; @media (min-width:500px) {height:30px}} :where(.card) {height:60px}");
        Layout(root); Assert.Equal(80,card.Width); Assert.Equal(30,card.Height); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void ImplicitScopeInsideAnExplicitScope_StillUsesTheStylesheetOwner()
    {
        var (root,card,_) = Tree(""); root.Name = "outer";
        Css.SetStyleSheet(card,"@scope (#outer) { @scope { :scope {width:40px} } }"); Layout(root);
        Assert.Equal(40,card.Width); Assert.True(double.IsNaN(root.Width));
        Css.SetStyleSheet(card,""); Css.SetStyleSheet(root,"@scope (.card) { @scope { :scope {width:99px} } }");
        root.UpdateLayout(); Assert.True(double.IsNaN(card.Width));
    }

    [Fact]
    public void NestedScopes_RespectBothBoundariesAndRebindTheScopeRoot()
    {
        var (root,card,child) = Tree("@scope (.card) to (.outer-stop) {@scope (.inner) to (.inner-stop) { :scope > .child {width:40px} }}");
        var inner = new StackPanel(); card.Children.Remove(child); inner.Children.Add(child); card.Children.Add(inner); Css.SetClass(inner,"inner");
        Layout(root); Assert.Equal(40,child.Width);
        Css.SetClass(inner,"inner outer-stop"); root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void ScopeNestedInAStyleRule_RebindsAmpersandForItsLimitAndBody()
    {
        var (root,card,child) = Tree(".card { @scope (& > .inner) to (& > .stop) { & {height:70px} .child {width:40px} }}");
        var inner = new StackPanel(); card.Children.Remove(child); inner.Children.Add(child); card.Children.Add(inner); Css.SetClass(inner,"inner");
        Layout(root); Assert.Equal(70,inner.Height); Assert.Equal(40,child.Width);
        Css.SetClass(child,"child stop"); root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void ScopeNesting_PreservesTheOuterStyleRulesLexicalScope()
    {
        var (root,_,child) = Tree(":scope > .card { @scope (&) { .child {width:40px} }}");
        Layout(root); Assert.Equal(40,child.Width);
    }

    [Fact]
    public void NestedHas_UsesItsOwnRelativeAnchorWithoutLosingTheScopeContext()
    {
        var (root,card,child) = Tree("@scope (.card) { .child:has(+ :scope > .other) {width:99px} & { > .child:has(+ .other) {width:40px} }}");
        var other = new Border(); card.Children.Add(other); Css.SetClass(other,"other"); Layout(root); Assert.Equal(40,child.Width);
        card.Children.Remove(other); root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void ScopeStartNativeAttributesAndStates_InvalidateDescendantMatches()
    {
        var (root,card,child) = Tree("@scope (.card[Width='200']:enabled) { .child {width:40px} }");
        card.Width = 200; Layout(root); Assert.Equal(40,child.Width);
        card.Width = 300; root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
        card.Width = 200; card.IsEnabled = false; root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
        card.IsEnabled = true; root.UpdateLayout(); Assert.Equal(40,child.Width);
    }

    [Fact]
    public void NativeBindingsAndValues_SurviveScopedNestingAndSheetRemoval()
    {
        var (root,card,child) = Tree("@scope (.card) { & { .child {width:300px!important; height:300px!important} }}");
        child.Width = 70; card.Height = 90;
        BindingOperations.SetBinding(child,FrameworkElement.HeightProperty,new Binding(nameof(card.Height)) {Source=card});
        Layout(root); Assert.Equal(70,child.Width); Assert.Equal(90,child.Height);
        Css.SetStyleSheet(root,""); root.UpdateLayout(); Assert.Equal(70,child.Width);
        Assert.NotNull(BindingOperations.GetBindingExpression(child,FrameworkElement.HeightProperty)); Assert.Same(card,child.VisualParent);
    }

    [Fact]
    public void ScopeAndNesting_StyleDocumentNodesWithoutChangingText()
    {
        var run = new Run("original"); var paragraph = new Paragraph(run); var document = new FlowDocument(); document.Blocks.Add(paragraph);
        Css.SetStyleSheet(document,"@scope (Paragraph) { & { Run {font-size:24px} }}");
        for (var i=0;i<4 && CssEvaluationScheduler.HasPending(document.Dispatcher);i++) CssEvaluationScheduler.FlushIfPending(document.Dispatcher);
        Assert.Equal(24,run.FontSize); Assert.Equal("original",run.Text);
    }

    [Theory]
    [InlineData("()")]
    [InlineData("(.card,) ")]
    [InlineData("(.card) to ()")]
    [InlineData("(.card) to (.stop,)")]
    [InlineData("(.card::before)")]
    [InlineData("(.card) to (.stop::before)")]
    [InlineData("(.card) unexpected")]
    public void InvalidScopes_DropOnlyTheirBlock(string header)
    {
        var (_,_,child) = Tree("@scope " + header + " {.child {width:99px}} .child {height:20px}");
        Assert.True(double.IsNaN(child.Width)); Assert.Equal(20,child.Height);
    }

    [Fact]
    public void SharedScopedSheets_UseEachNativeOwnersRoot()
    {
        var sheet = CssStyleSheet.Parse("@scope { :scope {height:50px} .child {width:40px} }");
        var firstChild=new Border(); var secondChild=new Border();
        var first=new Border {Child=firstChild}; var second=new Border {Child=secondChild};
        Css.SetClass(firstChild,"child"); Css.SetClass(secondChild,"child");
        Css.GetStyleSheets(first).Add(sheet); Css.GetStyleSheets(second).Add(sheet);
        Layout(first); Layout(second); Assert.Equal(50,first.Height); Assert.Equal(50,second.Height);
        Assert.Equal(40,firstChild.Width); Assert.Equal(40,secondChild.Width);
    }

    [Fact]
    public void ScopedNesting_PreservesTemplateIsolationAndUserContent()
    {
        Border? part=null; var template=new ControlTemplate(typeof(Button));
        template.SetVisualTree(()=>part=new Border {Child=new ContentPresenter()});
        var button=new Button {Template=template}; button.ApplyTemplate();
        var content=new Border(); button.Content=content; var root=new Border {Child=button};
        Css.SetStyleSheet(root,"@scope (:root) { Button { Border {opacity:.3} } }");
        Layout(root); Assert.Equal(.3,content.Opacity); Assert.NotNull(part); Assert.Equal(1,part.Opacity);
        Assert.Same(template,button.Template);
    }

    [Fact]
    public void NameDefiningRules_AreIndependentOfScopeAndContainerMatches()
    {
        var root=new Border();
        Css.SetStyleSheet(root,"""
            @scope (.never) { @property --size {syntax:'<length>'; inherits:false; initial-value:40px} }
            @layer first;
            @container never (width > 10000px) { @layer second; }
            @layer second { :scope {width:var(--size)} }
            @layer first { :scope {width:80px} }
            """);
        Layout(root); Assert.Equal(40,root.Width);
    }

    [Fact]
    public void DeepOverlappingScopes_DoNotEnumerateOuterRootCombinations()
    {
        var root=new StackPanel(); var current=root;
        for(var i=0;i<32;i++) {var child=new StackPanel(); Css.SetClass(child,"node"); current.Children.Add(child); current=child;}
        var target=new Border(); current.Children.Add(target); Css.SetClass(target,"target");
        var css=string.Concat(Enumerable.Repeat("@scope (.node) {",16))+".target {width:40px}"+new string('}',16);
        Css.SetStyleSheet(root,css); Layout(root); Assert.Equal(40,target.Width);
    }

    [Fact]
    public void ScopeAncestorStatesOutsideTheStylesheetHost_AreObserved()
    {
        var child=new Border(); var host=new Border {Child=child}; var root=new Border {Child=host}; Css.SetClass(root,"outside");
        Css.SetClass(child,"child");
        Css.SetStyleSheet(host,"@scope (.outside:enabled :scope) {.child {width:40px}}");
        Layout(root); Assert.Equal(40,child.Width);
        root.IsEnabled=false; root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
        root.IsEnabled=true; root.UpdateLayout(); Assert.Equal(40,child.Width);
    }

    [Fact]
    public void ScopeSiblingConditionsOutsideTheStylesheetHost_AreObserved()
    {
        var child=new Border(); var host=new Border {Child=child}; var peer=new StackPanel(); var root=new StackPanel();
        root.Children.Add(peer); root.Children.Add(host); Css.SetClass(peer,"peer"); Css.SetClass(child,"child");
        Css.SetStyleSheet(host,"@scope (.peer:has(.marker) + :scope) {.child {width:40px}}");
        Layout(root); Assert.True(double.IsNaN(child.Width));
        var marker=new Border(); Css.SetClass(marker,"marker"); peer.Children.Add(marker); root.UpdateLayout(); Assert.Equal(40,child.Width);
        Css.SetClass(peer,""); root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
        Css.SetClass(peer,"peer"); root.UpdateLayout(); Assert.Equal(40,child.Width);
        peer.Children.Clear(); root.UpdateLayout(); Assert.True(double.IsNaN(child.Width));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference UseSharedScopedSheet(CssStyleSheetCollection sheets)
    {
        var child=new Border(); var root=new Border {Child=child}; Css.SetClass(child,"child");
        Css.SetStyleSheets(root,sheets); Layout(root); Assert.Equal(40,child.Width);
        return new WeakReference(root);
    }

    [Fact]
    public void SharedScopedSheets_DoNotRetainScopeBindingsOrSelectorObservers()
    {
        var sheets=new CssStyleSheetCollection {CssStyleSheet.Parse("@scope { & { .child {width:40px} } }")};
        var root=UseSharedScopedSheet(sheets);
        for(var i=0;i<3 && root.IsAlive;i++) {GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();}
        Assert.False(root.IsAlive); GC.KeepAlive(sheets);
    }
}
