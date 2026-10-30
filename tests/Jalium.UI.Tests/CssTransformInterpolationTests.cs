using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTransformInterpolationTests
{
    [Fact]
    public void MatchedRotateFunctionsPreserveAuthoredTurns()
    {
        var halfway = Assert.IsType<RotateTransform>(CssTransformInterpolation.Interpolate(
            new RotateTransform(0), new RotateTransform(270), .5));
        Assert.Equal(135, halfway.Angle);
    }

    [Fact]
    public void MatchedTranslationExtrapolatesForOvershootingEasing()
    {
        var before = new TranslateTransform(0, 0);
        var after = new TranslateTransform(100, 0);
        Assert.Equal(162.5, Assert.IsType<TranslateTransform>(
            CssTransformInterpolation.Interpolate(before, after, 1.625)).X, 10);
        Assert.Equal(-62.5, Assert.IsType<TranslateTransform>(
            CssTransformInterpolation.Interpolate(before, after, -.625)).X, 10);
    }

    [Fact]
    public void KeyframeSamplerPreservesCubicBezierOvershoot()
    {
        var element = new Border();
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes move { from { transform: translateX(0px) } to { transform: translateX(100px) } }
            """).Keyframes);
        var sampler = CssKeyframes.BuildSamplers(CssNode.Get(element), rule)
            [UIElement.RenderTransformProperty];
        var timing = new CssTimingFunction("cubic-bezier", .5, 2, .5, 2);

        Assert.Equal(162.5, Assert.IsType<TranslateTransform>(sampler.Sample(.5, timing)).X, 10);
        var undershoot = new CssTimingFunction("cubic-bezier", .5, -1, .5, -1);
        Assert.Equal(-62.5, Assert.IsType<TranslateTransform>(sampler.Sample(.5, undershoot)).X, 10);
    }

    [Fact]
    public void CssTransformTransitionPreservesCubicBezierOvershoot()
    {
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        var child = new Border { Width = 40, Height = 30 };
        var root = new Border { Child = child };
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            const string timing = "transition: transform 1s cubic-bezier(.5,2,.5,2)";
            Css.SetStyle(child, $"transform: translateX(0px); {timing}");
            root.Measure(new Size(200, 100));
            root.Arrange(new Rect(0, 0, 200, 100));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Css.SetStyle(child, $"transform: translateX(100px); {timing}");
            Assert.True(child.HasAutomaticTransition(UIElement.RenderTransformProperty));

            var start = Stopwatch.GetTimestamp();
            AnimationManager.ProcessFrame(start);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.InRange(child.RenderTransform!.Value.OffsetX, 162.4, 162.6);
        }
        finally
        {
            child.StopAutomaticTransition(UIElement.RenderTransformProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void NonePadsEveryFunctionWithItsIdentity()
    {
        var destination = new TransformGroup(
            new TranslateTransform(100, 40), new ScaleTransform(3, 5));
        var halfway = Assert.IsType<TransformGroup>(
            CssTransformInterpolation.Interpolate(null, destination, .5));
        var translate = Assert.IsType<TranslateTransform>(halfway.Children[0]);
        var scale = Assert.IsType<ScaleTransform>(halfway.Children[1]);
        Assert.Equal(50, translate.X);
        Assert.Equal(20, translate.Y);
        Assert.Equal(2, scale.ScaleX);
        Assert.Equal(3, scale.ScaleY);
    }

    [Fact]
    public void MismatchedSuffixUsesADecomposedMatrixAndKeepsMatchingPrefix()
    {
        var from = new TransformGroup(new RotateTransform(0),
            new ScaleTransform(2, 2), new TranslateTransform(20, 0));
        var to = new TransformGroup(new RotateTransform(360),
            new TranslateTransform(10, 0), new ScaleTransform(2, 2));

        var halfway = Assert.IsType<TransformGroup>(
            CssTransformInterpolation.Interpolate(from, to, .5));
        Assert.Equal(180, Assert.IsType<RotateTransform>(halfway.Children[0]).Angle);
        var suffix = Assert.IsType<MatrixTransform>(halfway.Children[1]).Matrix;
        Assert.Equal(2, suffix.M11);
        Assert.Equal(20, suffix.OffsetX);
    }

    [Fact]
    public void MatrixFallbackTakesTheShortRotationPath()
    {
        var from = new MatrixTransform(Matrix.Identity);
        var to = new RotateTransform(270);
        var halfway = Assert.IsType<MatrixTransform>(
            CssTransformInterpolation.Interpolate(from, to, .5)).Matrix;
        Assert.InRange(halfway.M11, .70, .72);
        Assert.InRange(halfway.M12, -.72, -.70);
    }

    [Fact]
    public void MatrixReflectionKeepsTheSelectedScaleAxis()
    {
        var from = new MatrixTransform(Matrix.Identity);
        var to = new MatrixTransform(new Matrix(-1, 0, 0, 1, 0, 0));
        var quarter = Assert.IsType<MatrixTransform>(
            CssTransformInterpolation.Interpolate(from, to, .25)).Matrix;
        Assert.Equal(.5, quarter.M11, 10);
        Assert.Equal(0, quarter.M12, 10);
        Assert.Equal(0, quarter.M21, 10);
        Assert.Equal(1, quarter.M22, 10);
    }

    [Fact]
    public void MatrixReflectionsOnOppositeAxesAlignBeforeRotation()
    {
        var from = new MatrixTransform(new Matrix(-1, 0, 0, 1, 0, 0));
        var to = new MatrixTransform(new Matrix(1, 0, 0, -1, 0, 0));
        var halfway = Assert.IsType<MatrixTransform>(
            CssTransformInterpolation.Interpolate(from, to, .5)).Matrix;
        Assert.Equal(0, halfway.M11, 10);
        Assert.Equal(-1, halfway.M12, 10);
        Assert.Equal(-1, halfway.M21, 10);
        Assert.Equal(0, halfway.M22, 10);
    }

    [Fact]
    public void DifferentSkewFunctionNamesInterpolateAsMatrices()
    {
        var from = ParseTransform("skewX(30deg)");
        var to = ParseTransform("skewY(30deg)");
        var halfway = Assert.IsType<MatrixTransform>(
            CssTransformInterpolation.Interpolate(from, to, .5)).Matrix;
        var expected = Assert.IsType<MatrixTransform>(CssTransformInterpolation.Interpolate(
            new MatrixTransform(from.Value), new MatrixTransform(to.Value), .5)).Matrix;

        Assert.Equal(expected.M11, halfway.M11, 10);
        Assert.Equal(expected.M12, halfway.M12, 10);
        Assert.Equal(expected.M21, halfway.M21, 10);
        Assert.Equal(expected.M22, halfway.M22, 10);
        Assert.IsType<MatrixTransform>(CssTransformInterpolation.Interpolate(
            ParseTransform("skew(30deg)"), ParseTransform("skewX(60deg)"), .5));
    }

    [Fact]
    public void SameSkewFunctionAndItsIdentityInterpolateAngles()
    {
        var from = ParseTransform("skewX(30deg)");
        var to = ParseTransform("skewX(60deg)");
        var halfway = Assert.IsType<SkewTransform>(
            CssTransformInterpolation.Interpolate(from, to, .5));
        Assert.Equal(45, halfway.AngleX);
        Assert.Equal(0, halfway.AngleY);

        var fromNone = Assert.IsType<SkewTransform>(
            CssTransformInterpolation.Interpolate(null, from, .5));
        Assert.Equal(15, fromNone.AngleX);
    }

    [Fact]
    public void CssTransitionPreservesSkewFunctionNames()
    {
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        var child = new Border { Width = 40, Height = 30 };
        var root = new Border { Child = child };
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(child, "transform: skewX(30deg); transition: transform 1s linear");
            root.Measure(new Size(200, 100));
            root.Arrange(new Rect(0, 0, 200, 100));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Css.SetStyle(child, "transform: skewY(30deg); transition: transform 1s linear");
            Assert.True(child.HasAutomaticTransition(UIElement.RenderTransformProperty));

            var start = Stopwatch.GetTimestamp();
            AnimationManager.ProcessFrame(start);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.IsType<MatrixTransform>(child.RenderTransform);
        }
        finally
        {
            child.StopAutomaticTransition(UIElement.RenderTransformProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void ReorderedFunctionsInterpolateTheirCombinedMatrices()
    {
        var from = new TransformGroup(new TranslateTransform(100, 0),
            new ScaleTransform(2, 2));
        var to = new TransformGroup(new ScaleTransform(2, 2),
            new TranslateTransform(100, 0));
        var halfway = Assert.IsType<MatrixTransform>(
            CssTransformInterpolation.Interpolate(from, to, .5)).Matrix;
        Assert.Equal(2, halfway.M11);
        Assert.Equal(150, halfway.OffsetX);
    }

    [Fact]
    public void NoninvertibleMatrixFallsBackToDiscreteSampling()
    {
        var from = new MatrixTransform(new Matrix(0, 0, 0, 1, 0, 0));
        var to = new RotateTransform(90);
        Assert.Same(from, CssTransformInterpolation.Interpolate(from, to, .49));
        Assert.Same(to, CssTransformInterpolation.Interpolate(from, to, .5));
    }

    [Fact]
    public void CssKeyframeClockInterpolatesRenderTransform()
    {
        var child = new Border { Width = 40, Height = 30 };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes move { from {transform:translateX(0px)} to {transform:translateX(100px)} }");
        Css.SetStyle(child, "animation: move 1s linear both");
        root.Measure(new Size(200, 100));
        root.Arrange(new Rect(0, 0, 200, 100));
        var untransformedX = child.GetScreenBounds().X;
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp + Stopwatch.Frequency / 2);

        Assert.InRange(Assert.IsType<TranslateTransform>(child.RenderTransform).X, 49.9, 50.1);
        Assert.InRange(child.GetRenderBounds().X - untransformedX, 49.9, 50.1);
        Css.SetStyle(child, "");
        Assert.Null(child.RenderTransform);
    }

    [Fact]
    public void CssTransitionInterpolatesRenderTransform()
    {
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        var child = new Border { Width = 40, Height = 30 };
        var root = new Border { Child = child };
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(child, "transform: translateX(0px); transition: transform 1s linear");
            root.Measure(new Size(200, 100));
            root.Arrange(new Rect(0, 0, 200, 100));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

            Css.SetStyle(child, "transform: translateX(100px); transition: transform 1s linear");
            Assert.True(child.HasAutomaticTransition(UIElement.RenderTransformProperty));
            var start = Stopwatch.GetTimestamp();
            AnimationManager.ProcessFrame(start);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.InRange(child.RenderTransform!.Value.OffsetX, 49.9, 50.1);
        }
        finally
        {
            child.StopAutomaticTransition(UIElement.RenderTransformProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    [Fact]
    public void PercentageKeyframeTranslationTracksElementResize()
    {
        var child = new Border { Width = 100, Height = 30 };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes slide {from{transform:translateX(0%)}to{transform:translateX(100%)}}");
        Css.SetStyle(child, "animation: slide 1s linear both");
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        var start = runner.StartTimestamp;
        runner.Render(start + Stopwatch.Frequency / 2);
        Assert.InRange(child.RenderTransform!.Value.OffsetX, 49.9, 50.1);

        child.Width = 200;
        root.InvalidateMeasure();
        root.InvalidateArrange();
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(200, child.ActualWidth);
        Assert.True(child.CssRuntimeState?.ObservesOwnSize);
        Assert.Equal(start, runner.StartTimestamp);
        runner.Render(start + Stopwatch.Frequency / 2);
        Assert.InRange(child.RenderTransform!.Value.OffsetX, 99.9, 100.1);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void TransformTransitionUsesCssStepTiming()
    {
        var previous = UIElement.AutomaticTransitionsEnabledProvider;
        var child = new Border { Width = 40, Height = 30 };
        var root = new Border { Child = child };
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(child, "transform: translateX(0px); transition: transform 1s steps(2,start)");
            root.Measure(new Size(200, 100));
            root.Arrange(new Rect(0, 0, 200, 100));
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            Css.SetStyle(child, "transform: translateX(100px); transition: transform 1s steps(2,start)");
            Assert.True(child.HasAutomaticTransition(UIElement.RenderTransformProperty));

            var start = Stopwatch.GetTimestamp();
            AnimationManager.ProcessFrame(start);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Assert.InRange(child.RenderTransform!.Value.OffsetX, 49.9, 50.1);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency * 3 / 4);
            Assert.InRange(child.RenderTransform!.Value.OffsetX, 99.9, 100.1);
        }
        finally
        {
            child.StopAutomaticTransition(UIElement.RenderTransformProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previous;
        }
    }

    private static Transform ParseTransform(string css)
    {
        var reader = new CssTokenReader(css);
        Assert.True(CssTransformParser.TryParseTransformList(ref reader, out var transform));
        Assert.True(reader.AtEnd);
        return Assert.IsAssignableFrom<Transform>(transform);
    }
}
