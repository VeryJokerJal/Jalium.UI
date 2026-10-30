using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssKeyframesTests
{
    [Fact]
    public void ParsesNamedFramesAndKeepsDuplicateSelectorsInSourceOrder()
    {
        var sheet = CssStyleSheet.Parse("""
            @keyframes Fade {
              from, 25% { opacity: 0; animation-duration: 4s; color: red !important }
              25% { opacity: .4 }
              to { opacity: 1 }
            }
            @keyframes fade { from { opacity: .2 } to { opacity: .8 } }
            """);
        Assert.Equal(2, sheet.Keyframes.Length);
        Assert.Equal("Fade", sheet.Keyframes[0].Name);
        Assert.Equal("fade", sheet.Keyframes[1].Name);
        Assert.Equal([0d, .25], sheet.Keyframes[0].Blocks[0].Offsets);
        Assert.Equal(.25, Assert.Single(sheet.Keyframes[0].Blocks[1].Offsets));
        Assert.Equal(1, Assert.Single(sheet.Keyframes[0].Blocks[2].Offsets));
        Assert.Equal("opacity", Assert.Single(sheet.Keyframes[0].Blocks[0].Declarations).PropertyName);
        Assert.Equal(".4", Assert.Single(sheet.Keyframes[0].Blocks[1].Declarations).RawValue);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("INITIAL")]
    [InlineData("fade extra")]
    [InlineData("123")]
    public void InvalidNamesDropTheWholeRule(string name)
        => Assert.Empty(CssStyleSheet.Parse($"@keyframes {name} {{ from {{ opacity: 0 }} }}").Keyframes);

    [Fact]
    public void StringNameMayUseOtherwiseReservedWords()
    {
        var rule = Assert.Single(CssStyleSheet.Parse("@keyframes \"None\" { from {opacity:0} }").Keyframes);
        Assert.Equal("None", rule.Name);
    }

    [Theory]
    [InlineData("-1%")]
    [InlineData("101%")]
    [InlineData("0")]
    [InlineData("from, bad")]
    [InlineData("0%,")]
    public void InvalidSelectorDropsOnlyItsBlock(string selector)
    {
        var rule = Assert.Single(CssStyleSheet.Parse(
            $"@keyframes fade {{ {selector} {{opacity:0}} to {{opacity:1}} }}").Keyframes);
        Assert.Single(rule.Blocks);
        Assert.Equal(1, Assert.Single(rule.Blocks[0].Offsets));
    }

    [Fact]
    public void NestedConditionalDefinitionRetainsItsCondition()
    {
        var sheet = CssStyleSheet.Parse("@media (width >= 400px) { @keyframes fade {to{opacity:1}} }");
        Assert.NotNull(Assert.Single(sheet.Keyframes).Condition);
    }

    [Fact]
    public void NestedDefinitionsKeepSourceOrderAndConditions()
    {
        var sheet = CssStyleSheet.Parse("""
            @keyframes fade { to { opacity: .1 } }
            @layer theme {
              @supports (opacity: .5) {
                @keyframes fade { to { opacity: .5 } }
              }
            }
            @media (width >= 400px) {
              @keyframes fade { to { opacity: 1 } }
            }
            """);

        Assert.Equal([".1", ".5", "1"], sheet.Keyframes.Select(rule =>
            Assert.Single(Assert.Single(rule.Blocks).Declarations).RawValue));
        Assert.True(sheet.Keyframes[0].Offset < sheet.Keyframes[1].Offset);
        Assert.True(sheet.Keyframes[1].Offset < sheet.Keyframes[2].Offset);
        Assert.Null(sheet.Keyframes[0].Condition);
        Assert.NotNull(sheet.Keyframes[1].Condition);
        Assert.NotNull(sheet.Keyframes[2].Condition);
    }

    [Fact]
    public void MalformedFrameReportsActualLinesAndContinues()
    {
        var sheet = CssStyleSheet.Parse("""
            @media (width >= 400px) {
              @keyframes fade {
                bogus { opacity: 0 }
                to {
                  broken;
                  opacity: 1
                }
              }
            }
            """);

        var frame = Assert.Single(sheet.Keyframes);
        Assert.Equal("opacity", Assert.Single(Assert.Single(frame.Blocks).Declarations).PropertyName);
        Assert.Contains(sheet.Diagnostics, diagnostic => diagnostic.Line == 3 &&
            diagnostic.Message.Contains("invalid @keyframes selector", StringComparison.Ordinal));
        Assert.Contains(sheet.Diagnostics, diagnostic => diagnostic.Line == 5 &&
            diagnostic.Message.Contains("malformed declaration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportedDefinitionsKeepOrderConditionsAndResourceContext()
    {
        var main = new Uri("https://animation.test/styles/main.css");
        var imported = new Uri("https://animation.test/styles/base.css");
        var resolver = new KeyframeResources(new Dictionary<string, string>
        {
            [main.ToString()] = "@import 'base.css' (width >= 400px); @keyframes fade {to{opacity:1}}",
            [imported.ToString()] = "@keyframes fade {to{opacity:.2}} @keyframes spin {to{opacity:.8}}",
        });

        var sheet = await CssStyleSheet.LoadAsync(main, resolver);

        Assert.Equal(["fade", "spin", "fade"], sheet.Keyframes.Select(rule => rule.Name));
        Assert.Equal([0, 1, 2], sheet.Keyframes.Select(rule => rule.Offset));
        Assert.All(sheet.Keyframes[..2], rule =>
        {
            Assert.Equal(imported, rule.BaseUri);
            Assert.Same(resolver, rule.ResourceResolver);
            Assert.NotNull(rule.Condition);
        });
        Assert.Equal(main, sheet.Keyframes[2].BaseUri);
        Assert.Null(sheet.Keyframes[2].Condition);

        var root = new StackPanel { Width = 500 };
        Css.GetStyleSheets(root).Add(sheet);
        Assert.Same(sheet.Keyframes[1], CssKeyframes.Find(CssNode.Get(root), "spin"));
        root.Width = 300;
        Assert.Null(CssKeyframes.Find(CssNode.Get(root), "spin"));
    }

    [Fact]
    public void LastDocumentDefinitionWinsAndRemovingItRestoresThePreviousRule()
    {
        var root = new StackPanel();
        var first = new Border();
        var second = new Border();
        root.Children.Add(first);
        root.Children.Add(second);
        var earlier = CssStyleSheet.Parse("@keyframes fade { to {opacity:.2} }");
        var later = CssStyleSheet.Parse("@keyframes fade { to {opacity:.8} }");
        Css.GetStyleSheets(first).Add(earlier);
        Css.GetStyleSheets(second).Add(later);

        Assert.Same(later.Keyframes[0], CssKeyframes.Find(CssNode.Get(root), "fade"));
        Assert.Null(CssKeyframes.Find(CssNode.Get(root), "Fade"));

        Css.GetStyleSheets(second).Clear();
        Assert.Same(earlier.Keyframes[0], CssKeyframes.Find(CssNode.Get(root), "fade"));
    }

    [Fact]
    public void MediaConditionChangesWhichDefinitionWinsAtTheDocumentViewport()
    {
        var root = new StackPanel { Width = 500 };
        var sheet = CssStyleSheet.Parse("""
            @keyframes fade {to{opacity:.2}}
            @media (width >= 400px) {@keyframes fade {to{opacity:.8}}}
            """);
        Css.GetStyleSheets(root).Add(sheet);

        Assert.Same(sheet.Keyframes[1], CssKeyframes.Find(CssNode.Get(root), "fade"));
        root.Width = 300;
        Assert.Same(sheet.Keyframes[0], CssKeyframes.Find(CssNode.Get(root), "fade"));
    }

    [Fact]
    public void RepeatedOffsetsMergeInSourceOrderWithoutDiscardingOtherProperties()
    {
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade {
              50%, from { opacity: 0; color: red }
              50% { opacity: .5 }
              to { opacity: 1 }
            }
            """).Keyframes);

        var frames = CssKeyframes.ResolveFrames(rule);
        Assert.Equal([0d, .5, 1], frames.Select(frame => frame.Offset));
        Assert.Equal(["opacity", "color", "opacity"], frames[1].Declarations.Select(declaration =>
            declaration.PropertyName));
        var compiled = CssEngine.CompileDeclarations(frames[1].Declarations.ToList());
        Assert.Equal(2, compiled.Length);
        Assert.Contains(compiled, declaration => declaration.Name == "opacity" &&
            declaration.SourceOrder == 2);
        Assert.Contains(compiled, declaration => declaration.Name == "color" &&
            declaration.SourceOrder == 1);
    }

    [Fact]
    public void CompiledFramesKeepValidPropertiesAndSeparateSegmentTiming()
    {
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade {
              from { opacity: 0; animation-timing-function: steps(2, start) }
              50% { opacity: invalid; color: blue }
              to { opacity: 1; animation-timing-function: ease-out }
            }
            """).Keyframes);

        var frames = CssKeyframes.CompileFrames(rule);
        Assert.Equal([0d, .5, 1], frames.Select(frame => frame.Offset));
        Assert.Equal("opacity", Assert.Single(frames[0].Declarations).Name);
        Assert.Equal("steps(2, start)", Assert.Single(frames[0].TimingDeclarations).RawValue);
        Assert.Equal("color", Assert.Single(frames[1].Declarations).Name);
        Assert.Empty(frames[1].TimingDeclarations);
        Assert.Equal("opacity", Assert.Single(frames[2].Declarations).Name);
        Assert.Equal("ease-out", Assert.Single(frames[2].TimingDeclarations).RawValue);
    }

    [Fact]
    public void EvaluatedFramesResolveElementVariablesAndSkipOnlyInvalidFrameValues()
    {
        var element = new Border();
        Css.SetStyle(element, "--target: .8; --curve: steps(3, start); opacity: .25");
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade {
              from { opacity: var(--missing); color: red; animation-timing-function: var(--curve) }
              50% { opacity: var(--target); color: blue; animation-timing-function: invalid }
              to { opacity: 1 }
            }
            """).Keyframes);

        var frames = CssKeyframes.EvaluateFrames(CssNode.Get(element), rule);

        Assert.Equal([0d, .5, 1], frames.Select(frame => frame.Offset));
        Assert.False(frames[0].Values.ContainsKey(UIElement.OpacityProperty));
        Assert.Contains(frames[0].Values.Keys, property => property.Name == "Foreground");
        Assert.Equal("steps", frames[0].TimingFunction?.Kind);
        Assert.Equal(3, frames[0].TimingFunction?.Steps);
        Assert.Equal(.8, frames[1].Values[UIElement.OpacityProperty]);
        Assert.Null(frames[1].TimingFunction);
        Assert.Equal(1d, frames[2].Values[UIElement.OpacityProperty]);
        Assert.Equal(.25, element.Opacity);
    }

    [Fact]
    public void KeyframeAttributeUsesItsStylesheetNamespace()
    {
        var element = new Border();
        Css.SetAttribute(element, "urn:animation", "strength", ".7");
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @namespace a 'urn:animation';
            @keyframes fade { from { opacity: attr(a|strength number, .4) } }
            """).Keyframes);

        var frame = Assert.Single(CssKeyframes.EvaluateFrames(CssNode.Get(element), rule));
        Assert.Equal(.7, frame.Values[UIElement.OpacityProperty]);
    }

    [Fact]
    public void KeyframeTimingFunctionAcceptsEnvironmentFallback()
    {
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade { from { opacity: 0; animation-timing-function: env(unknown, steps(2, start)) } }
            """).Keyframes);

        var frame = Assert.Single(CssKeyframes.EvaluateFrames(CssNode.Get(new Border()), rule));
        Assert.Equal("steps", frame.TimingFunction?.Kind);
        Assert.Equal(2, frame.TimingFunction?.Steps);
    }

    [Fact]
    public void FrameColorDefinesCurrentColorEvenWhenDeclaredLater()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes tint { from { background-color: currentColor; color: red } }
            """).Keyframes);

        var frame = Assert.Single(CssKeyframes.EvaluateFrames(CssNode.Get(element), rule));
        Assert.Equal(Jalium.UI.Media.Colors.Red,
            Assert.IsType<Jalium.UI.Media.SolidColorBrush>(frame.Values[Border.BackgroundProperty]).Color);
        Assert.Null(element.Background);
    }

    [Fact]
    public void SparsePropertiesGetIndependentTracksAndImplicitUnderlyingEndpoints()
    {
        var element = new Border { Opacity = .3 };
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes mixed {
              from { opacity: 0; animation-timing-function: linear }
              50% { opacity: var(--missing); color: red; animation-timing-function: ease-in }
              75% { opacity: .75 }
              to { color: blue }
            }
            """).Keyframes);

        var tracks = CssKeyframes.BuildPropertyTracks(CssNode.Get(element), rule);
        var opacity = tracks[UIElement.OpacityProperty];
        Assert.Equal([0d, .75, 1], opacity.Select(frame => frame.Offset));
        Assert.Equal([0d, .75, .3], opacity.Select(frame => frame.Value));
        Assert.Equal("linear", opacity[0].TimingFunction?.Kind);
        Assert.Null(opacity[1].TimingFunction);
        var foreground = Assert.Single(tracks.Where(pair => pair.Key.Name == "Foreground")).Value;
        Assert.Equal([0d, .5, 1], foreground.Select(frame => frame.Offset));
        Assert.Equal("ease-in", foreground[1].TimingFunction?.Kind);
        Assert.Equal(.3, element.Opacity);
    }

    [Fact]
    public void FrameFontSizeSuppliesTheEmBasisRegardlessOfDeclarationOrder()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes size { from { margin-left: 1em; font-size: 20px } }
            """).Keyframes);

        var frame = Assert.Single(CssKeyframes.EvaluateFrames(CssNode.Get(element), rule));
        Assert.Equal(20, Assert.IsType<Thickness>(frame.Values[FrameworkElement.MarginProperty]).Left);
        Assert.Equal(new Thickness(0), element.Margin);
    }

    [Fact]
    public void LocalFontSizeKeepsTheEmBasisAboveTheKeyframeValue()
    {
        var element = new Button { FontSize = 30 };
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes size { from { margin-left: 1em; font-size: 20px } }
            """).Keyframes);

        var frame = Assert.Single(CssKeyframes.EvaluateFrames(CssNode.Get(element), rule));
        Assert.Equal(30, Assert.IsType<Thickness>(frame.Values[FrameworkElement.MarginProperty]).Left);
    }

    [Fact]
    public void LocalForegroundRemainsTheCurrentColorBasis()
    {
        var element = new Button { Foreground = Jalium.UI.Media.Brushes.Blue };
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes tint { from { background-color: currentColor; color: red } }
            """).Keyframes);

        var frame = Assert.Single(CssKeyframes.EvaluateFrames(CssNode.Get(element), rule));
        Assert.Equal(Jalium.UI.Media.Colors.Blue,
            Assert.IsType<Jalium.UI.Media.SolidColorBrush>(frame.Values[Button.BackgroundProperty]).Color);
    }

    [Fact]
    public void SamplerUsesFrameTimingForItsOwnSegmentAndDefaultForTheNext()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes pulse {
              from { opacity: 0; animation-timing-function: steps(2, start) }
              50% { opacity: 1 }
              to { opacity: 0 }
            }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)[UIElement.OpacityProperty];
        var linear = new CssTimingFunction("linear");

        Assert.Equal(.5, sampler.Sample(0, linear));
        Assert.Equal(1d, sampler.Sample(.25, linear));
        Assert.Equal(.5, sampler.Sample(.75, linear));
        Assert.Equal(0d, sampler.Sample(1, linear));
        Assert.Equal(1d, element.Opacity);
    }

    [Fact]
    public void NumericSamplerPreservesCubicBezierOvershoot()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade { from { opacity: 0 } to { opacity: 1 } }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)[UIElement.OpacityProperty];
        var timing = new CssTimingFunction("cubic-bezier", .5, 2, .5, 2);

        Assert.Equal(1.625, Assert.IsType<double>(sampler.Sample(.5, timing)), 10);
    }

    [Fact]
    public void SamplerSkipsInvalidPropertyFrameAndInterpolatesAcrossTheGap()
    {
        var element = new Border { Opacity = .2 };
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade {
              from { opacity: 0 }
              50% { opacity: var(--missing); color: red }
              75% { opacity: .75 }
            }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)[UIElement.OpacityProperty];

        Assert.Equal(.375, sampler.Sample(.375, new CssTimingFunction("linear")));
        Assert.Equal(.475, Assert.IsType<double>(sampler.Sample(.875, new CssTimingFunction("linear"))), 6);
    }

    [Fact]
    public void SamplerUsesDiscreteSwitchForPropertiesWithoutContinuousInterpolation()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes pointer { from { cursor: pointer } to { cursor: wait } }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)[FrameworkElement.CursorProperty];
        var linear = new CssTimingFunction("linear");

        Assert.Equal(Jalium.UI.Input.Cursors.Hand, sampler.Sample(.49, linear));
        Assert.Equal(Jalium.UI.Input.Cursors.Wait, sampler.Sample(.5, linear));
    }

    [Fact]
    public void SamplerInterpolatesSolidBrushesWithoutChangingTheElement()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes tint { from { background-color: red } to { background-color: blue } }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)[Border.BackgroundProperty];

        var midpoint = Assert.IsType<Jalium.UI.Media.SolidColorBrush>(
            sampler.Sample(.5, new CssTimingFunction("linear")));
        Assert.NotEqual(Jalium.UI.Media.Colors.Red, midpoint.Color);
        Assert.NotEqual(Jalium.UI.Media.Colors.Blue, midpoint.Color);
        Assert.Null(element.Background);
    }

    [Fact]
    public void SamplerKeepsDisplayVisibleUntilTheExitEndpoint()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes hide { from { display: block } to { display: none } }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)
            [CssDisplayProperties.SpecificationProperty];
        var linear = new CssTimingFunction("linear");

        Assert.False(Assert.IsType<CssDisplaySpecification>(sampler.Sample(.5, linear)).None);
        Assert.True(Assert.IsType<CssDisplaySpecification>(sampler.Sample(1, linear)).None);
    }

    private sealed class KeyframeResources(Dictionary<string, string> files) : ICssResourceResolver
    {
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CssResource(uri,
                new MemoryStream(Encoding.UTF8.GetBytes(files[uri.ToString()]))));
        }
    }
}
