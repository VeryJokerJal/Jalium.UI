using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class CssCalcSizeTests:IDisposable
{
 private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
 public void Dispose()=>_context.Dispose();
 public static IEnumerable<object[]> GeometryCases()=>Cases.Select(c=>new object[]{c.Name});
 [Theory] [MemberData(nameof(GeometryCases))]
 public void UsedValuesPreserveTheirSizingBasisAndCalculation(string name)
 {
  using var f=new CalcFixture(Cases.Single(c=>c.Name==name));f.Layout();var actual=f.Geometry();
  foreach(var pair in f.Case.Expected!)Near(pair.Value,actual[pair.Key]);Assert.Same(f.Item,f.Box.Children[0]);Assert.Same(f.Percent??f.Leaf,f.Item.Child);
  Assert.True(f.Root.IsMeasureValid);Assert.True(f.Root.IsArrangeValid);
 }
 public static IEnumerable<object[]> ParsingCases()=>Parsing.Select(c=>new object[]{c.Property,c.Value,c.Valid});
 [Theory] [MemberData(nameof(ParsingCases))]
 public void ContextGrammarAndNumericTypesMatchSelectedWpt(string property,string value,bool valid)
 {Assert.Equal(valid,Css.Supports(property,value));Assert.Equal(valid,CssAuthoring.IsValueSupported(property,value));}
 [Theory] [InlineData(false)] [InlineData(true)]
 public void ContentNativeAndAnimatedSizesRestoreTheLiveExpression(bool height)
 {
  using var f=new CalcFixture(Cases.Single(c=>c.Name==(height?"height-min-content-size * 2":"width-min-content-size * 2")));
  var property=height?FrameworkElement.HeightProperty:FrameworkElement.WidthProperty;var leaf=height?20d:40d;
  f.Layout();Near(leaf*2,height?f.Item.ActualHeight:f.Item.ActualWidth);
  f.Leaf.SetValue(property,60d);f.Layout();Near(120,height?f.Item.ActualHeight:f.Item.ActualWidth);
  f.Item.SetValue(property,70d);f.Layout();Near(70,height?f.Item.ActualHeight:f.Item.ActualWidth);
  f.Item.SetAnimatedValue(property,90d,false);f.Layout();Near(90,height?f.Item.ActualHeight:f.Item.ActualWidth);
  f.Item.ClearAnimatedValue(property);f.Item.ClearValue(property);f.Layout();Near(120,height?f.Item.ActualHeight:f.Item.ActualWidth);
 }
 [Fact]
 public void VariablesNestedBasesAndInheritedComputedLengthsRemainLive()
 {
  using var f=new CalcFixture(Cases[0]);Css.SetStyle(f.Item,"--basis:calc-size(min-content, size * 2);width:calc-size(var(--basis), size + 20px);");f.Layout();Near(100,f.Item.ActualWidth);
  f.Leaf.Width=60;f.Layout();Near(140,f.Item.ActualWidth);
  Css.SetStyle(f.Box,"display:block;font-size:10px;width:calc-size(40px, size + 1em);");Css.SetStyle(f.Item,"display:block;font-size:20px;width:inherit;");f.Layout();Near(50,f.Box.ActualWidth);Near(50,f.Item.ActualWidth);
  Css.SetStyle(f.Box,"display:block;font-size:30px;width:calc-size(40px, size + 1em);");f.Layout();Near(70,f.Item.ActualWidth);
 }
 [Fact]
 public void ExcessiveNestingAndCrossContextSizeTokensAreRejected()
 {
  var value="10px";for(var n=0;n<70;n++)value="calc-size("+value+", size)";Assert.False(Css.Supports("width",value));
  Assert.False(Css.Supports("padding","calc-size(auto, size)"));Assert.False(Css.Supports("gap","calc-size(any, 10px)"));Assert.False(Css.Supports("width","calc(size + 10px)"));
 }
 [Theory]
 [InlineData("width","calc-size(min-content, size)","calc-size(min-content, size * 3)",false,.5,80)]
 [InlineData("height","calc-size(auto, size)","calc-size(any, 100px)",false,.5,60)]
 [InlineData("width","min-content","calc-size(any, 100px)",false,.5,70)]
 [InlineData("height","auto","100px",true,.5,60)]
 [InlineData("height","auto","100px",false,.25,20)]
 [InlineData("height","auto","100px",false,.75,100)]
 [InlineData("inline-size","calc-size(min-content, size)","calc-size(any, 100px)",false,.5,70)]
 [InlineData("max-height","calc-size(max-content, size * 4)","calc-size(any, 100px)",false,.5,90)]
 public void KeyframeSamplingKeepsIntrinsicValuesAndKeywordOptIn(string property,string from,string to,bool allow,double progress,double expected)
 {
  using var f=AnimationFixture(property,allow);var (dp,sampler)=Sampler(f,property,from,to);f.Item.SetCssAnimatedValue(dp,sampler.Sample(progress,new("linear")));f.Layout();Near(expected,property.Contains("height")?f.Item.ActualHeight:f.Item.ActualWidth);
  f.Item.ClearCssAnimatedValue(dp);
 }
 [Fact]
 public void AKeyframeIntermediateReevaluatesContentAndHonorsNativeOverrides()
 {
  using var f=AnimationFixture("height",false);var (dp,sampler)=Sampler(f,"height","calc-size(auto, size)","calc-size(any, 100px)");var value=sampler.Sample(.5,new("linear"));f.Item.SetCssAnimatedValue(dp,value);f.Layout();Near(60,f.Item.ActualHeight);
  f.Leaf.Height=60;f.Layout();Near(80,f.Item.ActualHeight);f.Item.Height=42;f.Layout();Near(42,f.Item.ActualHeight);f.Item.ClearValue(FrameworkElement.HeightProperty);f.Layout();Near(80,f.Item.ActualHeight);
  f.Item.ClearCssAnimatedValue(dp);f.Layout();Near(60,f.Item.ActualHeight);
 }
 [Theory] [InlineData("width",80)] [InlineData("height",40)]
 public void TransitionTimelineUsesTheSameIntrinsicValueChannel(string property,double expected)
 {
  using var f=AnimationFixture(property,false);var (dp,_)=Sampler(f,property,"calc-size(min-content, size)","calc-size(min-content, size * 3)");
  var rule=CssKeyframes.Find(CssNode.Get(f.Item),"sizing")!;var frames=CssKeyframes.BuildPropertyTracks(CssNode.Get(f.Item),rule)[dp];
  var timeline=CssTransitions.Create(f.Item,dp,frames[0].Value,frames[^1].Value,new(TimeSpan.FromSeconds(1),TimeSpan.Zero,new("linear"),false));Assert.NotNull(timeline);
  var animation=Assert.IsAssignableFrom<AnimationTimeline>(timeline);var clock=animation.CreateClock();clock.SetProgressForSampling(.5);f.Item.SetCssAnimatedValue(dp,animation.GetCurrentValue(frames[0].Value!,frames[^1].Value!,clock));f.Layout();Near(expected,property=="height"?f.Item.ActualHeight:f.Item.ActualWidth);f.Item.ClearCssAnimatedValue(dp);
 }
 [Fact]
 public void InheritTracksTheAnimatedComputedExpressionAndNativeOverride()
 {
  using var f=new CalcFixture(Cases[0] with {Item="width:inherit;",Box="display:block;width:200px;"});f.Layout();
  Css.GetStyleSheets(f.Root).Add(CssStyleSheet.Parse("@keyframes inherited{from{width:calc-size(any,100px);}to{width:calc-size(any,200px);}}"));f.Layout();
  var node=CssNode.Get(f.Box);var tracks=CssKeyframes.BuildSamplers(node,CssKeyframes.Find(node,"inherited")!);var sample=tracks.Single(t=>t.Key.Name is "Width" or "CssSizingWidth");
  f.Box.SetCssAnimatedValue(sample.Key,sample.Value.Sample(.5,new("linear")));f.Layout();Near(150,f.Item.ActualWidth);
  f.Box.SetCssAnimatedValue(sample.Key,sample.Value.Sample(.75,new("linear")));f.Layout();Near(175,f.Item.ActualWidth);
  f.Box.Width=30;f.Layout();Near(30,f.Item.ActualWidth);f.Box.ClearValue(FrameworkElement.WidthProperty);f.Layout();Near(175,f.Item.ActualWidth);f.Box.ClearCssAnimatedValue(sample.Key);
 }
 [Theory] [InlineData("flex-basis",70)] [InlineData("min-width",60)]
 public void FlexBasisAndAutomaticMinimaAnimateThroughTheFormattingAlgorithm(string property,double expected)
 {
  using var f=new CalcFixture(Cases[0] with {Box=property=="min-width"?"display:flex;width:0px;":"display:flex;width:200px;",Item=property=="min-width"?"width:80px;min-width:0;flex:0 1 auto;":"width:80px;min-width:0;flex:none;",Leafw=property=="min-width"?80:40});f.Layout();
  var from=property=="flex-basis"?"content":"calc-size(auto,size + 20px)";var to=property=="flex-basis"?"calc-size(any,100px)":"calc-size(any,20px)";
  var (dp,sampler)=Sampler(f,property,from,to);f.Item.SetCssAnimatedValue(dp,sampler.Sample(.5,new("linear")));f.Layout();Near(expected,f.Item.ActualWidth);f.Item.ClearCssAnimatedValue(dp);
 }
 [Fact]
 public void DifferentIntrinsicBasesRemainDiscrete()
 {
  using var f=new CalcFixture(Cases.Single(c=>c.Name=="inline-content-fit-content-200") with {Item=""});f.Layout();var(dp,sampler)=Sampler(f,"width","calc-size(min-content,size)","calc-size(max-content,size)");
  f.Item.SetCssAnimatedValue(dp,sampler.Sample(.25,new("linear")));f.Layout();Near(20,f.Item.ActualWidth);f.Item.SetCssAnimatedValue(dp,sampler.Sample(.75,new("linear")));f.Layout();Near(40,f.Item.ActualWidth);f.Item.ClearCssAnimatedValue(dp);
 }
 [Fact]
 public void AStyleChangeOwnsATypedAutomaticClockAndRespectsNativeValues()
 {
  using var f=AnimationFixture("height",false);Css.SetStyle(f.Item,"height:calc-size(any,20px);transition:height 1s linear;");f.Layout();
  CssNode.Get(f.Item).CssRuntimeState!.HadRenderedStyle=true;
  typeof(UIElement).GetMethod("ScheduleAutomaticTransitionArm",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(f.Item,null);
  Css.SetStyle(f.Item,"height:calc-size(any,100px);transition:height 1s linear;");f.Layout();
  var property=CssNode.Get(f.Item).CssRuntimeState!.Applied!.Keys.SingleOrDefault(p=>p.Name=="CssSizingHeight");Assert.NotNull(property);
  Assert.True(f.Item.HasAutomaticTransition(property));Assert.False(f.Item.HasAutomaticTransition(FrameworkElement.HeightProperty));
  var start=System.Diagnostics.Stopwatch.GetTimestamp();((Jalium.UI.Animation.IFrameAnimatable)f.Item).OnAnimationFrame(start);((Jalium.UI.Animation.IFrameAnimatable)f.Item).OnAnimationFrame(start+System.Diagnostics.Stopwatch.Frequency/2);
  f.Root.Measure(new(400,300));f.Root.Arrange(new(0,0,400,300));f.Root.UpdateLayout();Near(60,f.Item.ActualHeight);
  f.Item.Height=42;f.Root.Measure(new(400,300));f.Root.Arrange(new(0,0,400,300));f.Root.UpdateLayout();Near(42,f.Item.ActualHeight);
  f.Item.StopAutomaticTransition(property,true);Css.SetStyle(f.Item,"height:calc-size(any,100px);transition:none;");f.Item.ClearValue(FrameworkElement.HeightProperty);f.Layout();Near(100,f.Item.ActualHeight);
 }
 [Fact]
 public void StyleQueriesCompareTheComputedBasisAndArithmeticRatherThanUsedPixels()
 {
  using var f=new CalcFixture(Cases[0]);Css.SetStyle(f.Item,"font-size:20px;width:calc-size(min-content,size + 20px);");f.Layout();var node=CssNode.Get(f.Item);var context=CssEngine.BuildLengthContext(node);
  Assert.True(CssSizeStyleQuery.Matches("width","calc-size(min-content,20px + size)",node,context));
  Assert.True(CssSizeStyleQuery.Matches("width","calc-size(min-content,size + 1em)",node,context));
  Assert.False(CssSizeStyleQuery.Matches("width","calc-size(min-content,size + 30px)",node,context));
  Assert.False(CssSizeStyleQuery.Matches("width","calc-size(max-content,size + 20px)",node,context));
  Assert.False(CssSizeStyleQuery.Matches("width","60px",node,context));
 }
 [Theory]
 [InlineData("calc-size(auto,min(size,100px))","wrap",50,0,50)]
 [InlineData("calc-size(auto,min(size,100px))","nowrap",50,0,50)]
 [InlineData("calc-size(calc-size(auto,size),min(size,100px))","wrap",50,0,50)]
 [InlineData("calc-size(any,100px)","wrap",60,100,0)]
 [InlineData("calc-size(any,100px)","nowrap",50,0,50)]
 public void ColumnContainerKeepsTheBasisBehaviorWhenResolvingItsMainSize(string height,string wrap,double itemHeight,double nextX,double nextY)
 {
  using var f=new CalcFixture(Cases[0] with {Box="display:flex;flex-direction:column;flex-wrap:"+wrap+";width:100px;height:"+height+";",Item="width:100px;height:60px;min-height:0;",Leafw=100,Leafh=60});
  var next=new Border();Css.SetStyle(next,"width:100px;height:60px;min-height:0;");f.Box.Children.Add(next);f.Layout();Near(100,f.Box.ActualHeight);Near(itemHeight,f.Item.ActualHeight);Near(itemHeight,next.ActualHeight);
  var first=f.Item.TransformToAncestor(f.Box);var point=next.TransformToAncestor(f.Box);Near(0,first.X);Near(0,first.Y);Near(nextX,point.X);Near(nextY,point.Y);
 }
 [Theory]
 [InlineData("calc-size(50%,size * 2)","calc-size(any,0px)",10)]
 [InlineData("calc-size(50% + 20px,size)","calc-size(any,100px)",65)]
 [InlineData("calc-size(calc-size(50%,size + 20px),size * 2)","calc-size(any,0px)",30)]
 [InlineData("calc-size(40px,size * 2)","calc-size(min-content,size)",50)]
 public void AnimationPreparesNumericAndNestedBasesBeforeInterpolating(string from,string to,double expected)
 {
  using var f=AnimationFixture("height",false);var(dp,sampler)=Sampler(f,"height",from,to);f.Item.SetCssAnimatedValue(dp,sampler.Sample(.5,new("linear")));f.Layout();Near(expected,f.Item.ActualHeight);f.Item.ClearCssAnimatedValue(dp);
 }
 [Theory]
 [InlineData("calc-size(calc-size(min-content,size + 20px),size * 2)","calc-size(calc-size(min-content,20px + size),size * 2)",true)]
 [InlineData("calc-size(min-content,size + sin(30deg) * 10px)","calc-size(min-content,size + sin(60deg) * 10px)",false)]
 [InlineData("calc-size(min-content,size + sin(1turn) * 10px)","calc-size(min-content,size + sin(360deg) * 10px)",true)]
 [InlineData("calc-size(min-content,size + 0%)","calc-size(min-content,size)",false)]
 public void NonlinearAndNestedQueriesPreserveComputedDependencies(string actual,string expected,bool matches)
 {
  using var f=new CalcFixture(Cases[0] with {Item="width:"+actual+";"});f.Layout();var node=CssNode.Get(f.Item);Assert.Equal(matches,CssSizeStyleQuery.Matches("width",expected,node,CssEngine.BuildLengthContext(node)));
 }
 [Fact]
 public void AnimatedQueriesAndInterruptedTransitionsUseThePresentedCalculation()
 {
  using var f=AnimationFixture("width",false);var(dp,sampler)=Sampler(f,"width","calc-size(min-content,size)","calc-size(any,100px)");var value=sampler.Sample(.5,new("linear"));f.Item.SetCssAnimatedValue(dp,value);f.Layout();Near(70,f.Item.ActualWidth);
  var node=CssNode.Get(f.Item);var context=CssEngine.BuildLengthContext(node);Assert.True(CssSizeStyleQuery.Matches("width","calc-size(min-content,size * .5 + 50px)",node,context));Assert.False(CssSizeStyleQuery.Matches("width","calc-size(min-content,size * .5 + 60px)",node,context));
  var frames=CssKeyframes.BuildPropertyTracks(node,CssKeyframes.Find(node,"sizing")!)[dp];var timeline=Assert.IsAssignableFrom<AnimationTimeline>(CssTransitions.Create(f.Item,dp,value,frames[^1].Value,new(TimeSpan.FromSeconds(1),TimeSpan.Zero,new("linear"),false)));var clock=timeline.CreateClock();clock.SetProgressForSampling(.5);f.Item.SetCssAnimatedValue(dp,timeline.GetCurrentValue(value!,frames[^1].Value!,clock));f.Layout();Near(85,f.Item.ActualWidth);f.Leaf.Width=80;f.Layout();Near(95,f.Item.ActualWidth);f.Item.ClearCssAnimatedValue(dp);
 }
 [Fact]
 public void SubstitutionExpansionLimitFallsBackToDiscreteSampling()
 {
  using var f=AnimationFixture("height",false);var from="auto";for(var n=0;n<20;n++)from="calc-size("+from+",size / 2 + size / 2)";Assert.True(Css.Supports("height",from));var(dp,sampler)=Sampler(f,"height",from,"calc-size(any,100px)");f.Item.SetCssAnimatedValue(dp,sampler.Sample(.25,new("linear")));f.Layout();Near(20,f.Item.ActualHeight);f.Item.SetCssAnimatedValue(dp,sampler.Sample(.75,new("linear")));f.Layout();Near(100,f.Item.ActualHeight);f.Item.ClearCssAnimatedValue(dp);
 }
 [Fact]
 public void InterpolateSizeMetadataExposesCssKeywords()
 {Assert.Equal("numeric-only",CssAuthoring.GetProperty("interpolate-size").InitialValue);Assert.Contains("allow-keywords",CssAuthoring.GetValues("interpolate-size"));}
 private static CalcFixture AnimationFixture(string property,bool allow)
 {
  var f=new CalcFixture(Cases[0] with {Item=property=="max-height"?"height:200px;":"",Box="display:block;width:200px;"});
  if(allow)Css.SetStyle(f.Root,f.Case.Root+"interpolate-size:allow-keywords;");f.Layout();return f;
 }
 private static (DependencyProperty Property,CssPropertySampler Sampler) Sampler(CalcFixture f,string property,string from,string to)
 {
  Css.GetStyleSheets(f.Root).Add(CssStyleSheet.Parse("@keyframes sizing{from{"+property+":"+from+";}to{"+property+":"+to+";}}"));f.Layout();
  var node=CssNode.Get(f.Item);var rule=CssKeyframes.Find(node,"sizing");Assert.NotNull(rule);var tracks=CssKeyframes.BuildSamplers(node,rule);
  var native=property=="inline-size"?"Width":property=="flex-basis"?"Basis":string.Concat(property.Split('-').Select(s=>char.ToUpperInvariant(s[0])+s[1..]));
  var selected=tracks.Where(t=>t.Key.Name=="CssSizing"+native||t.Key.Name==native).ToArray();Assert.Single(selected);return (selected[0].Key,selected[0].Value);
 }
 private static void Near(double expected,double actual)=>Assert.InRange(actual,expected-.06,expected+.06);
 internal static readonly CalcCase[] Cases =
 [
  new("width-min-content-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(min-content, size + 20px);", 40, 20, new() { {"width", 60} }, "block", null),
  new("width-min-content-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(min-content, size * 2);", 40, 20, new() { {"width", 80} }, "block", null),
  new("width-max-content-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(max-content, size + 20px);", 40, 20, new() { {"width", 60} }, "block", null),
  new("width-max-content-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(max-content, size * 2);", 40, 20, new() { {"width", 80} }, "block", null),
  new("width-fit-content-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(fit-content, size + 20px);", 40, 20, new() { {"width", 60} }, "block", null),
  new("width-fit-content-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(fit-content, size * 2);", 40, 20, new() { {"width", 80} }, "block", null),
  new("width-auto-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(auto, size + 20px);", 40, 20, new() { {"width", 220} }, "block", null),
  new("width-auto-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(auto, size * 2);", 40, 20, new() { {"width", 400} }, "block", null),
  new("width-stretch-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(stretch, size + 20px);", 40, 20, new() { {"width", 220} }, "block", null),
  new("width-stretch-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(stretch, size * 2);", 40, 20, new() { {"width", 400} }, "block", null),
  new("width-60px-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(60px, size + 20px);", 40, 20, new() { {"width", 80} }, "block", null),
  new("width-60px-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(60px, size * 2);", 40, 20, new() { {"width", 120} }, "block", null),
  new("width-50%-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(50%, size + 20px);", 40, 20, new() { {"width", 120} }, "block", null),
  new("width-50%-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(50%, size * 2);", 40, 20, new() { {"width", 200} }, "block", null),
  new("width-any-0px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(any, 0px);", 40, 20, new() { {"width", 0} }, "block", null),
  new("width-any-50px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(any, 50px);", 40, 20, new() { {"width", 50} }, "block", null),
  new("width-any-10%", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(any, 10%);", 40, 20, new() { {"width", 20} }, "block", null),
  new("width-any-calc(20px + 10%)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(any, calc(20px + 10%));", 40, 20, new() { {"width", 40} }, "block", null),
  new("width-math-round(up, size, 30px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(min-content, round(up, size, 30px));", 40, 20, new() { {"width", 60} }, "block", null),
  new("width-math-min(size, 10px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(min-content, min(size, 10px));", 40, 20, new() { {"width", 10} }, "block", null),
  new("width-math-max(size, 100px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(min-content, max(size, 100px));", 40, 20, new() { {"width", 100} }, "block", null),
  new("width-math-clamp(50px, size, 100px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(min-content, clamp(50px, size, 100px));", 40, 20, new() { {"width", 50} }, "block", null),
  new("width-math-abs(size - 80px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(min-content, abs(size - 80px));", 40, 20, new() { {"width", 40} }, "block", null),
  new("width-nested-23", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(calc-size(min-content, size / 2), size + 10%);", 40, 20, new() { {"width", 40}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("width-nested-24", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(calc-size(any, 40px), size * 2);", 40, 20, new() { {"width", 80}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("width-nested-25", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(calc-size(auto, size - 100px), size + 100px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("height-min-content-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, size + 20px);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-min-content-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, size * 2);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-max-content-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(max-content, size + 20px);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-max-content-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(max-content, size * 2);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-fit-content-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(fit-content, size + 20px);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-fit-content-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(fit-content, size * 2);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-auto-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(auto, size + 20px);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-auto-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(auto, size * 2);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-60px-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(60px, size + 20px);", 40, 20, new() { {"height", 80} }, "block", null),
  new("height-60px-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(60px, size * 2);", 40, 20, new() { {"height", 120} }, "block", null),
  new("height-50%-size + 20px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(50%, size + 20px);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-50%-size * 2", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(50%, size * 2);", 40, 20, new() { {"height", 40} }, "block", null),
  new("height-any-0px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(any, 0px);", 40, 20, new() { {"height", 0} }, "block", null),
  new("height-any-50px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(any, 50px);", 40, 20, new() { {"height", 50} }, "block", null),
  new("height-any-10%", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(any, 10%);", 40, 20, new() { {"height", 0} }, "block", null),
  new("height-any-calc(20px + 10%)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(any, calc(20px + 10%));", 40, 20, new() { {"height", 20} }, "block", null),
  new("height-math-round(up, size, 30px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, round(up, size, 30px));", 40, 20, new() { {"height", 30} }, "block", null),
  new("height-math-min(size, 10px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, min(size, 10px));", 40, 20, new() { {"height", 10} }, "block", null),
  new("height-math-max(size, 100px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, max(size, 100px));", 40, 20, new() { {"height", 100} }, "block", null),
  new("height-math-clamp(50px, size, 100px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, clamp(50px, size, 100px));", 40, 20, new() { {"height", 50} }, "block", null),
  new("height-math-abs(size - 80px)", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, abs(size - 80px));", 40, 20, new() { {"height", 60} }, "block", null),
  new("height-nested-47", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(calc-size(min-content, size / 2), size + 10%);", 40, 20, new() { {"width", 200}, {"height", 10}, {"boxWidth", 200}, {"boxHeight", 10} }, "block", null),
  new("height-nested-48", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(calc-size(any, 40px), size * 2);", 40, 20, new() { {"width", 200}, {"height", 80}, {"boxWidth", 200}, {"boxHeight", 80} }, "block", null),
  new("height-nested-49", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(calc-size(auto, size - 100px), size + 100px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("content-box-width-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:calc-size(min-content, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 92}, {"height", 32}, {"boxWidth", 92}, {"boxHeight", 32} }, "block", null),
  new("content-box-width-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:calc-size(40px, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 92}, {"height", 32}, {"boxWidth", 92}, {"boxHeight", 32} }, "block", null),
  new("content-box-min-width-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-width:calc-size(min-content, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 92}, {"height", 32}, {"boxWidth", 92}, {"boxHeight", 32} }, "block", null),
  new("content-box-min-width-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-width:calc-size(40px, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 92}, {"height", 32}, {"boxWidth", 92}, {"boxHeight", 32} }, "block", null),
  new("content-box-max-width-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:200px;max-width:calc-size(min-content, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 92}, {"height", 32}, {"boxWidth", 92}, {"boxHeight", 32} }, "block", null),
  new("content-box-max-width-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:200px;max-width:calc-size(40px, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 92}, {"height", 32}, {"boxWidth", 92}, {"boxHeight", 32} }, "block", null),
  new("content-box-height-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:calc-size(min-content, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 52}, {"boxWidth", 52}, {"boxHeight", 52} }, "block", null),
  new("content-box-height-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:calc-size(40px, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 92}, {"boxWidth", 52}, {"boxHeight", 92} }, "block", null),
  new("content-box-min-height-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-height:calc-size(min-content, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 52}, {"boxWidth", 52}, {"boxHeight", 52} }, "block", null),
  new("content-box-min-height-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-height:calc-size(40px, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 92}, {"boxWidth", 52}, {"boxHeight", 92} }, "block", null),
  new("content-box-max-height-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:200px;max-height:calc-size(min-content, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 52}, {"boxWidth", 52}, {"boxHeight", 52} }, "block", null),
  new("content-box-max-height-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:200px;max-height:calc-size(40px, size * 2);box-sizing:content-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 92}, {"boxWidth", 52}, {"boxHeight", 92} }, "block", null),
  new("border-box-width-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:calc-size(min-content, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 104}, {"height", 32}, {"boxWidth", 104}, {"boxHeight", 32} }, "block", null),
  new("border-box-width-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:calc-size(40px, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 80}, {"height", 32}, {"boxWidth", 80}, {"boxHeight", 32} }, "block", null),
  new("border-box-min-width-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-width:calc-size(min-content, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 104}, {"height", 32}, {"boxWidth", 104}, {"boxHeight", 32} }, "block", null),
  new("border-box-min-width-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-width:calc-size(40px, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 80}, {"height", 32}, {"boxWidth", 80}, {"boxHeight", 32} }, "block", null),
  new("border-box-max-width-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:200px;max-width:calc-size(min-content, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 104}, {"height", 32}, {"boxWidth", 104}, {"boxHeight", 32} }, "block", null),
  new("border-box-max-width-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "width:200px;max-width:calc-size(40px, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 80}, {"height", 32}, {"boxWidth", 80}, {"boxHeight", 32} }, "block", null),
  new("border-box-height-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:calc-size(min-content, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 64}, {"boxWidth", 52}, {"boxHeight", 64} }, "block", null),
  new("border-box-height-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:calc-size(40px, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 80}, {"boxWidth", 52}, {"boxHeight", 80} }, "block", null),
  new("border-box-min-height-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-height:calc-size(min-content, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 64}, {"boxWidth", 52}, {"boxHeight", 64} }, "block", null),
  new("border-box-min-height-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "min-height:calc-size(40px, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 80}, {"boxWidth", 52}, {"boxHeight", 80} }, "block", null),
  new("border-box-max-height-min-content", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:200px;max-height:calc-size(min-content, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 64}, {"boxWidth", 52}, {"boxHeight", 64} }, "block", null),
  new("border-box-max-height-40px", "display:flow-root;width:400px;height:300px;", "display:inline-block;", "height:200px;max-height:calc-size(40px, size * 2);box-sizing:border-box;padding:4px;border:2px solid green;", 40, 20, new() { {"width", 52}, {"height", 80}, {"boxWidth", 52}, {"boxHeight", 80} }, "block", null),
  new("auto-block-auto", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;height:auto;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("auto-block-120px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;height:120px;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 200}, {"height", 32}, {"boxWidth", 200}, {"boxHeight", 120} }, "block", null),
  new("auto-inline-block-auto", "display:flow-root;width:400px;height:300px;", "display:inline-block;width:200px;height:auto;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("auto-inline-block-120px", "display:flow-root;width:400px;height:300px;", "display:inline-block;width:200px;height:120px;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 200}, {"height", 32}, {"boxWidth", 200}, {"boxHeight", 120} }, "block", null),
  new("auto-flex-auto", "display:flow-root;width:400px;height:300px;", "display:flex;width:200px;height:auto;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 40}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("auto-flex-120px", "display:flow-root;width:400px;height:300px;", "display:flex;width:200px;height:120px;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 40}, {"height", 32}, {"boxWidth", 200}, {"boxHeight", 120} }, "block", null),
  new("auto-grid-auto", "display:flow-root;width:400px;height:300px;", "display:grid;width:200px;height:auto;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 200}, {"height", 22}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("auto-grid-120px", "display:flow-root;width:400px;height:300px;", "display:grid;width:200px;height:120px;align-items:start;", "height:calc-size(auto, size + 10%);", 40, 20, new() { {"width", 200}, {"height", 32}, {"boxWidth", 200}, {"boxHeight", 120} }, "block", null),
  new("flex-row-basis-any", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(any, 60px);", 40, 20, new() { {"width", 60}, {"height", 20} }, "block", null),
  new("flex-column-basis-any", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(any, 60px);", 40, 20, new() { {"width", 40}, {"height", 60} }, "block", null),
  new("flex-row-basis-auto", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(auto, size * 2 + 10px);", 40, 20, new() { {"width", 170}, {"height", 20} }, "block", null),
  new("flex-column-basis-auto", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(auto, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 130} }, "block", null),
  new("flex-row-basis-min-content", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(min-content, size * 2 + 10px);", 40, 20, new() { {"width", 90}, {"height", 20} }, "block", null),
  new("flex-column-basis-min-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(min-content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50} }, "block", null),
  new("flex-row-basis-max-content", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(max-content, size * 2 + 10px);", 40, 20, new() { {"width", 90}, {"height", 20} }, "block", null),
  new("flex-column-basis-max-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(max-content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50} }, "block", null),
  new("flex-row-basis-fit-content", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(fit-content, size * 2 + 10px);", 40, 20, new() { {"width", 90}, {"height", 20} }, "block", null),
  new("flex-column-basis-fit-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(fit-content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50} }, "block", null),
  new("flex-row-basis-stretch", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(stretch, size * 2 + 10px);", 40, 20, new() { {"width", 610}, {"height", 20} }, "block", null),
  new("flex-column-basis-stretch", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(stretch, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 410} }, "block", null),
  new("flex-row-basis-content", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(content, size * 2 + 10px);", 40, 20, new() { {"width", 90}, {"height", 20} }, "block", null),
  new("flex-column-basis-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50} }, "block", null),
  new("flex-row-basis-40px", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(40px, size * 2 + 10px);", 40, 20, new() { {"width", 90}, {"height", 20} }, "block", null),
  new("flex-column-basis-40px", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(40px, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 90} }, "block", null),
  new("flex-row-basis-50%", "display:flow-root;width:400px;height:300px;", "display:flex;width:300px;align-items:flex-start;", "width:80px;min-width:0;flex:0 0 calc-size(50%, size * 2 + 10px);", 40, 20, new() { {"width", 310}, {"height", 20} }, "block", null),
  new("flex-column-basis-50%", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:200px;height:200px;align-items:flex-start;", "height:60px;min-height:0;flex:0 0 calc-size(50%, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 210} }, "block", null),
  new("ratio-width-100", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:100px;aspect-ratio:1 / 2;width:calc-size(auto, size + 50px);", 0, 0, new() { {"width", 100}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 100} }, "block", null),
  new("ratio-width-101", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:100px;aspect-ratio:1 / 2;width:calc-size(min-content, size * 2);", 0, 0, new() { {"width", 100}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 100} }, "block", null),
  new("ratio-width-102", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:100px;aspect-ratio:1 / 2;width:calc-size(any, 100px);", 0, 0, new() { {"width", 100}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 100} }, "block", null),
  new("ratio-height-103", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:100px;aspect-ratio:1 / 2;height:calc-size(auto, size + 50px);", 0, 0, new() { {"width", 100}, {"height", 250}, {"boxWidth", 200}, {"boxHeight", 250} }, "block", null),
  new("ratio-height-104", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:100px;aspect-ratio:1 / 2;height:calc-size(min-content, size * 2);", 0, 0, new() { {"width", 100}, {"height", 400}, {"boxWidth", 200}, {"boxHeight", 400} }, "block", null),
  new("ratio-height-105", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:100px;aspect-ratio:1 / 2;height:calc-size(any, 100px);", 0, 0, new() { {"width", 100}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 100} }, "block", null),
  new("intrinsic-parent-min-content", "display:flow-root;width:400px;height:300px;", "display:block;width:min-content;", "width:calc-size(auto, size + 20px);", 40, 20, new() { {"width", 80}, {"height", 20}, {"boxWidth", 60}, {"boxHeight", 20} }, "block", null),
  new("intrinsic-parent-max-content", "display:flow-root;width:400px;height:300px;", "display:block;width:max-content;", "width:calc-size(auto, size + 20px);", 40, 20, new() { {"width", 80}, {"height", 20}, {"boxWidth", 60}, {"boxHeight", 20} }, "block", null),
  new("intrinsic-parent-fit-content", "display:flow-root;width:400px;height:300px;", "display:block;width:fit-content;", "width:calc-size(auto, size + 20px);", 40, 20, new() { {"width", 80}, {"height", 20}, {"boxWidth", 60}, {"boxHeight", 20} }, "block", null),
  new("auto-min-row-None", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:row;width:0px;height:100px;", "min-width:calc-size(auto, size + 20px);", 80, 20, new() { {"width", 100} }, "block", null),
  new("auto-min-row-60", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:row;width:0px;height:100px;", "min-width:calc-size(auto, size + 40px);width:60px;", 80, 20, new() { {"width", 100} }, "block", null),
  new("auto-min-row-80", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:row;width:0px;height:100px;", "min-width:calc-size(auto, size + 40px);width:80px;", 60, 20, new() { {"width", 100} }, "block", null),
  new("auto-min-column-None", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:100px;height:0px;", "min-height:calc-size(auto, size + 20px);", 40, 80, new() { {"height", 100} }, "block", null),
  new("auto-min-column-60", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:100px;height:0px;", "min-height:calc-size(auto, size + 40px);height:60px;", 40, 80, new() { {"height", 100} }, "block", null),
  new("auto-min-column-80", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;width:100px;height:0px;", "min-height:calc-size(auto, size + 40px);height:80px;", 40, 60, new() { {"height", 100} }, "block", null),
  new("auto-min-padded-content-box", "display:flow-root;width:400px;height:300px;", "display:flex;width:0px;", "min-width:calc-size(auto, size + 20px);padding:4px;border:2px solid green;box-sizing:content-box;", 80, 20, new() { {"width", 112} }, "block", null),
  new("auto-min-padded-border-box", "display:flow-root;width:400px;height:300px;", "display:flex;width:0px;", "min-width:calc-size(auto, size + 20px);padding:4px;border:2px solid green;box-sizing:border-box;", 80, 20, new() { {"width", 112} }, "block", null),
  new("descendant-auto", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(auto, 100px);", 40, 20, new() { {"height", 100}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("descendant-any", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(any, 100px);", 40, 20, new() { {"height", 100}, {"percentHeight", 50} }, "block", "height:50%;"),
  new("descendant-min-content", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(min-content, size + 80px);", 40, 20, new() { {"height", 100}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("descendant-40px", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(40px, 100px);", 40, 20, new() { {"height", 100}, {"percentHeight", 50} }, "block", "height:50%;"),
  new("descendant-50%", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "height:calc-size(50%, size + 80px);", 40, 20, new() { {"height", 100}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-auto-auto-auto", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(auto, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 50}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-auto-auto-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 50}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-auto-auto-min-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(min-content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 50}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-auto-auto-any", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(any, 100px);", 40, 20, new() { {"width", 40}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 100}, {"percentHeight", 50} }, "block", "height:50%;"),
  new("flex-desc-auto-auto-40px", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(40px, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 90}, {"boxWidth", 200}, {"boxHeight", 90}, {"percentHeight", 45} }, "block", "height:50%;"),
  new("flex-desc-auto-auto-50%", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(50%, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-auto-60px-auto", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(auto, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 130}, {"boxWidth", 200}, {"boxHeight", 130}, {"percentHeight", 65} }, "block", "height:50%;"),
  new("flex-desc-auto-60px-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 50}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-auto-60px-min-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(min-content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 50}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-auto-60px-any", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(any, 100px);", 40, 20, new() { {"width", 40}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 100}, {"percentHeight", 50} }, "block", "height:50%;"),
  new("flex-desc-auto-60px-40px", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(40px, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 90}, {"boxWidth", 200}, {"boxHeight", 90}, {"percentHeight", 45} }, "block", "height:50%;"),
  new("flex-desc-auto-60px-50%", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:auto;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(50%, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20}, {"percentHeight", 20} }, "block", "height:50%;"),
  new("flex-desc-200px-auto-auto", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(auto, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 25} }, "block", "height:50%;"),
  new("flex-desc-200px-auto-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 25} }, "block", "height:50%;"),
  new("flex-desc-200px-auto-min-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(min-content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 25} }, "block", "height:50%;"),
  new("flex-desc-200px-auto-any", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(any, 100px);", 40, 20, new() { {"width", 40}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 50} }, "block", "height:50%;"),
  new("flex-desc-200px-auto-40px", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(40px, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 90}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 45} }, "block", "height:50%;"),
  new("flex-desc-200px-auto-50%", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:auto;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(50%, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 210}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 105} }, "block", "height:50%;"),
  new("flex-desc-200px-60px-auto", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(auto, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 130}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 65} }, "block", "height:50%;"),
  new("flex-desc-200px-60px-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 25} }, "block", "height:50%;"),
  new("flex-desc-200px-60px-min-content", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(min-content, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 50}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 25} }, "block", "height:50%;"),
  new("flex-desc-200px-60px-any", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(any, 100px);", 40, 20, new() { {"width", 40}, {"height", 100}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 50} }, "block", "height:50%;"),
  new("flex-desc-200px-60px-40px", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(40px, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 90}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 45} }, "block", "height:50%;"),
  new("flex-desc-200px-60px-50%", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;align-items:flex-start;width:200px;height:200px;", "height:60px;min-height:0;flex-grow:0;flex-shrink:0;flex-basis:calc-size(50%, size * 2 + 10px);", 40, 20, new() { {"width", 40}, {"height", 210}, {"boxWidth", 200}, {"boxHeight", 200}, {"percentHeight", 105} }, "block", "height:50%;"),
  new("inline-content-min-content-0", "display:block;width:0px;", "display:block;width:min-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 180}, {"height", 20}, {"boxWidth", 100}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-min-content-100", "display:block;width:100px;", "display:block;width:min-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 180}, {"height", 20}, {"boxWidth", 100}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-min-content-200", "display:block;width:200px;", "display:block;width:min-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 180}, {"height", 20}, {"boxWidth", 100}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-max-content-0", "display:block;width:0px;", "display:block;width:max-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 120}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-max-content-100", "display:block;width:100px;", "display:block;width:max-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 120}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-max-content-200", "display:block;width:200px;", "display:block;width:max-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 120}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-fit-content-0", "display:block;width:0px;", "display:block;width:fit-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 180}, {"height", 20}, {"boxWidth", 100}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-fit-content-100", "display:block;width:100px;", "display:block;width:fit-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 180}, {"height", 20}, {"boxWidth", 100}, {"boxHeight", 20} }, "inline", null),
  new("inline-content-fit-content-200", "display:block;width:200px;", "display:block;width:fit-content;", "width:calc-size(auto, size + 80px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 120}, {"boxHeight", 20} }, "inline", null),
  new("context-grid-stretch", "display:flow-root;width:400px;height:300px;", "display:grid;width:200px;height:120px;", "height:calc-size(auto,size);", 40, 20, new() { {"width", 200}, {"height", 120}, {"boxWidth", 200}, {"boxHeight", 120}, {"percentHeight", 60} }, "block", "height:50%;"),
  new("context-grid-stretch-offset", "display:flow-root;width:400px;height:300px;", "display:grid;width:200px;height:120px;", "height:calc-size(auto,size + 20px);", 40, 20, new() { {"width", 200}, {"height", 140}, {"boxWidth", 200}, {"boxHeight", 120}, {"percentHeight", 70} }, "block", "height:50%;"),
  new("context-absolute-width", "display:flow-root;width:400px;height:300px;", "display:block;position:relative;width:200px;height:200px;", "position:absolute;left:10px;right:10px;width:calc-size(auto,size + 20px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 200} }, "block", null),
  new("context-absolute-height", "display:flow-root;width:400px;height:300px;", "display:block;position:relative;width:200px;height:200px;", "position:absolute;top:10px;bottom:10px;width:calc-size(auto,size);height:calc-size(auto,size / 2);", 40, 20, new() { {"width", 40}, {"height", 90}, {"boxWidth", 200}, {"boxHeight", 200} }, "block", null),
  new("context-absolute-intrinsic", "display:flow-root;width:400px;height:300px;", "display:block;position:relative;width:200px;height:200px;", "position:absolute;width:calc-size(min-content,size * 2);height:calc-size(auto,size + 10px);", 40, 20, new() { {"width", 80}, {"height", 30}, {"boxWidth", 200}, {"boxHeight", 200} }, "block", null),
  new("context-table-cell", "display:flow-root;width:400px;height:300px;", "display:table;width:200px;", "display:table-cell;width:calc-size(min-content,size * 2);height:calc-size(auto,size + 10px);", 40, 20, new() { {"width", 200}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("context-table-caption", "display:flow-root;width:400px;height:300px;", "display:table;width:200px;", "display:table-caption;width:calc-size(auto,size + 20px);", 40, 20, new() { {"width", 220}, {"height", 20}, {"boxWidth", 200}, {"boxHeight", 20} }, "block", null),
  new("context-fit-function-20", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(fit-content(20px),size * 2);", 40, 20, new() { {"width", 80} }, "block", null),
  new("context-fit-function-200", "display:flow-root;width:400px;height:300px;", "display:block;width:200px;", "width:calc-size(fit-content(200px),size * 2);", 40, 20, new() { {"width", 80} }, "block", null),
  new("column-container-164", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;flex-wrap:wrap;width:100px;height:calc-size(auto,min(size,100px));", "width:100px;height:60px;min-height:0;", 100, 60, new() { {"width", 100}, {"height", 50}, {"boxWidth", 100}, {"boxHeight", 100}, {"nextWidth", 100}, {"nextHeight", 50}, {"nextX", 0}, {"nextY", 50} }, "pair", null),
  new("column-container-165", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;flex-wrap:nowrap;width:100px;height:calc-size(auto,min(size,100px));", "width:100px;height:60px;min-height:0;", 100, 60, new() { {"width", 100}, {"height", 50}, {"boxWidth", 100}, {"boxHeight", 100}, {"nextWidth", 100}, {"nextHeight", 50}, {"nextX", 0}, {"nextY", 50} }, "pair", null),
  new("column-container-166", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;flex-wrap:wrap;width:100px;height:calc-size(calc-size(auto,size),min(size,100px));", "width:100px;height:60px;min-height:0;", 100, 60, new() { {"width", 100}, {"height", 50}, {"boxWidth", 100}, {"boxHeight", 100}, {"nextWidth", 100}, {"nextHeight", 50}, {"nextX", 0}, {"nextY", 50} }, "pair", null),
  new("column-container-167", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;flex-wrap:wrap;width:100px;height:calc-size(any,100px);", "width:100px;height:60px;min-height:0;", 100, 60, new() { {"width", 100}, {"height", 60}, {"boxWidth", 100}, {"boxHeight", 100}, {"nextWidth", 100}, {"nextHeight", 60}, {"nextX", 100}, {"nextY", 0} }, "pair", null),
  new("column-container-168", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;flex-wrap:nowrap;width:100px;height:calc-size(any,100px);", "width:100px;height:60px;min-height:0;", 100, 60, new() { {"width", 100}, {"height", 50}, {"boxWidth", 100}, {"boxHeight", 100}, {"nextWidth", 100}, {"nextHeight", 50}, {"nextX", 0}, {"nextY", 50} }, "pair", null),
  new("column-container-asymmetric-minima", "display:flow-root;width:400px;height:300px;", "display:flex;flex-direction:column;flex-wrap:wrap;width:100px;height:calc-size(auto,min(size,100px));", "width:100px;height:60px;", 100, 60, new() { {"height", 60}, {"boxHeight", 100}, {"nextHeight", 40}, {"nextX", 0}, {"nextY", 60} }, "pair-auto", null),
 ];
 private static readonly (string Property,string Value,bool Valid)[] Parsing =
 [
  ("width","calc-size(auto, size)",true),
  ("min-width","calc-size(auto, size)",true),
  ("height","calc-size(auto, size)",true),
  ("min-height","calc-size(auto, size)",true),
  ("max-height","calc-size(auto, size)",false),
  ("max-width","calc-size(auto, size)",false),
  ("height","calc-size(none, size)",false),
  ("width","calc-size(none, size)",false),
  ("min-height","calc-size(none, size)",false),
  ("min-width","calc-size(none, size)",false),
  ("max-height","calc-size(none, size)",false),
  ("max-width","calc-size(none, size)",false),
  ("width","calc-size(max-content, size)",true),
  ("height","calc-size(max-content, size)",true),
  ("max-width","calc-size(max-content, size)",true),
  ("max-height","calc-size(max-content, size)",true),
  ("min-width","calc-size(max-content, size)",true),
  ("min-height","calc-size(max-content, size)",true),
  ("block-size","calc-size(max-content, size)",true),
  ("inline-size","calc-size(max-content, size)",true),
  ("max-block-size","calc-size(max-content, size)",true),
  ("max-inline-size","calc-size(max-content, size)",true),
  ("min-block-size","calc-size(max-content, size)",true),
  ("min-inline-size","calc-size(max-content, size)",true),
  ("width","calc-size(fit-content, size)",true),
  ("height","calc-size(min-content, size * 2)",true),
  ("max-width","calc-size(max-content, size / 2)",true),
  ("max-height","calc-size(fit-content, 30px + size / 2)",true),
  ("width","calc-size(fit-content, 50% + size / 2)",true),
  ("width","calc-size(any, 25em)",true),
  ("width","calc-size(any, 40%)",true),
  ("width","calc-size(any, 50px + 30%)",true),
  ("width","calc-size(calc-size(any, 30px), size)",true),
  ("width","calc-size(any, size)",false),
  ("width","calc-size(any, fit-content)",false),
  ("width","calc-size(any, max-content)",false),
  ("width","calc-size(any, min-content)",false),
  ("width","calc-size(10px, sign(size) * size)",true),
  ("width","size",false),
  ("width","sign(size)",false),
  ("width","calc(50px * sign(size))",false),
  ("width","calc-size(any, calc-size(10px, sign(size) * size))",false),
  ("width","calc-size(any, calc-size(any, 10px))",false),
  ("width","calc(calc-size(auto, size))",false),
  ("width","calc(50px * sign(calc-size(auto, size)))",false),
  ("width","calc(calc-size(auto, size) + calc-size(auto, size))",false),
  ("width","calc(abs(calc-size(auto, size)))",false),
  ("width","calc(100px * progress(calc-size(auto, size) from calc-size(auto, 0px) to calc-size(auto, size)))",false),
  ("width","calc(100px * progress(calc-size(auto, size) from 0px to 100px))",false),
  ("width","calc(100px * progress(50px from calc-size(auto, 0px) to calc-size(auto, size)))",false),
  ("width","min(calc-size(auto, 0px), calc-size(auto, size))",false),
  ("width","calc(min(calc-size(auto, 0px), calc-size(auto, size)))",false),
  ("width","max(calc-size(auto, 0px), calc-size(auto, size))",false),
  ("width","calc(max(calc-size(auto, 0px), calc-size(auto, size)))",false),
  ("width","clamp(calc-size(auto, 0px), calc-size(auto, 30px), calc-size(auto, size))",false),
  ("width","calc(clamp(calc-size(auto, 0px), calc-size(auto, 30px), calc-size(auto, size)))",false),
  ("width","calc(cos(calc-size(auto, 0px)))",false),
  ("width","calc(atan2(calc-size(auto, size), calc-size(auto, 50px)))",false),
  ("width","calc(sqrt(calc-size(auto, 0px)))",false),
  ("width","calc(hypot(calc-size(auto, size * 0.5), calc-size(auto, size)))",false),
  ("width","calc(round(calc-size(auto, size * 0.5), calc-size(auto, size)))",false),
  ("width","calc(round(calc-size(auto, size * 0.5)))",false),
  ("width","calc(mod(calc-size(auto, size * 0.5), calc-size(auto, size)))",false),
  ("width","calc(rem(calc-size(auto, size * 0.5), calc-size(auto, size)))",false),
  ("width","calc-size(30px, 25em)",true),
  ("width","calc-size(calc-size(any, 30px), 25em)",true),
  ("width","calc-size(calc-size(2in, 30px), 25em)",true),
  ("width","calc-size(calc-size(min-content, 30px), 25em)",true),
  ("width","calc-size(calc-size(min-content, size), size)",true),
  ("height","calc(12% + calc-size(any, 31%))",false),
  ("width","calc-size(30px)",false),
  ("width","calc-size(min(30px, 2em))",false),
  ("width","calc-size(any)",false),
  ("width","calc-size(calc-size(any, 30px))",false),
  ("width","calc-size(size)",false),
  ("width","calc-size(fit-content)",false),
  ("width","calc-size(calc-size(fit-content, size * 2))",false),
  ("width","calc-size(calc-size(30px))",false),
  ("flex-basis","calc-size(any, 50px)",true),
  ("flex-basis","calc-size(auto, size)",true),
  ("flex-basis","calc-size(min-content, size)",true),
  ("flex-basis","calc-size(max-content, size)",true),
  ("flex-basis","calc-size(fit-content, size)",true),
  ("flex-basis","calc-size(content, size)",true),
  ("width","calc-size(content, size)",false),
  ("height","calc-size(content, size)",false),
  ("max-width","calc-size(content, size)",false),
  ("max-height","calc-size(content, size)",false),
  ("min-width","calc-size(content, size)",false),
  ("min-height","calc-size(content, size)",false),
  ("width","calc-size(0px, 0px)",true),
  ("width","calc-size(0, 0px)",false),
  ("width","calc-size(0px, 0)",false),
  ("width","calc-size(0, 0)",false),
  ("interpolate-size","numeric-only",true),("interpolate-size","allow-keywords",true),("interpolate-size","auto",false),
 ];
}

sealed record CalcCase(string Name,string Root,string Box,string Item,int Leafw,int Leafh,Dictionary<string,double>? Expected,string Kind="block",string? Percent=null);
sealed class CalcFixture:IDisposable
{
 public readonly CalcCase Case;public readonly StackPanel Root=new(),Box=new();public readonly Border Item;public readonly FrameworkElement Leaf;public readonly Border? Percent,Next;
 public CalcFixture(CalcCase c)
 {
  Case=c;
  if(c.Kind=="inline")
  {
   var line=new StackPanel();Css.SetStyle(line,"display:block;font-size:0;line-height:0;");
   for(var n=0;n<2;n++){var atom=new Border{Width=20,Height=20};line.Children.Add(atom);Css.SetStyle(atom,"display:inline-block;");}Leaf=line;
  }
  else Leaf=new Border{Width=c.Leafw,Height=c.Leafh};
  if(c.Percent is{} style){Percent=new(){Child=Leaf};Css.SetStyle(Percent,"display:block;"+style);}
  Item=new(){Child=Percent??Leaf};Box.Children.Add(Item);Root.Children.Add(Box);if(c.Kind.StartsWith("pair")){Next=new Border();Css.SetStyle(Next,"width:100px;height:60px;"+(c.Kind=="pair"?"min-height:0;":""));Box.Children.Add(Next);}Css.SetStyle(Root,c.Root);Css.SetStyle(Box,"box-sizing:border-box;"+c.Box);Css.SetStyle(Item,"display:block;box-sizing:border-box;"+c.Item);
 }
 public void Layout(){for(var i=0;i<4;i++){typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);Root.Measure(new(400,300));Root.Arrange(new(0,0,400,300));Root.UpdateLayout();}}
 public Dictionary<string,double> Geometry(){var data=new Dictionary<string,double>{{"width",Item.ActualWidth},{"height",Item.ActualHeight},{"boxWidth",Box.ActualWidth},{"boxHeight",Box.ActualHeight}};if(Percent is{} p)data["percentHeight"]=p.ActualHeight;if(Next is{} next){data["nextWidth"]=next.ActualWidth;data["nextHeight"]=next.ActualHeight;var at=next.TransformToAncestor(Box);data["nextX"]=at.X;data["nextY"]=at.Y;}return data;}
 public void Dispose(){Root.Children.Clear();Box.Children.Clear();Item.Child=null;}
}
