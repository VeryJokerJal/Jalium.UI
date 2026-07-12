using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Effects;
using ModernBlurEffect = Jalium.UI.Media.Effects.BlurEffect;

namespace Jalium.UI.Tests;

#pragma warning disable CS0618 // The test deliberately verifies WPF's legacy BitmapEffect API.

public sealed class MediaEffectsWpfParityTests
{
    [Fact]
    public void ModernEffectsAreAnimatableAndProduceTypedClones()
    {
        var blur = new ModernBlurEffect
        {
            Radius = -4.5,
            KernelType = KernelType.Box,
            RenderingBias = RenderingBias.Quality,
        };

        Assert.IsAssignableFrom<Animatable>(blur);
        ModernBlurEffect clone = blur.CloneCurrentValue();
        Assert.NotSame(blur, clone);
        Assert.Equal(-4.5, clone.Radius);
        Assert.Equal(KernelType.Box, clone.KernelType);
        Assert.Equal(RenderingBias.Quality, clone.RenderingBias);

        clone.Freeze();
        Assert.True(clone.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => { clone.Radius = 2; });

        var shadow = new DropShadowEffect
        {
            BlurRadius = -1,
            Direction = 721,
            Opacity = 2,
            ShadowDepth = -3,
        };
        DropShadowEffect shadowClone = shadow.Clone();
        Assert.Equal(-1, shadowClone.BlurRadius);
        Assert.Equal(721, shadowClone.Direction);
        Assert.Equal(2, shadowClone.Opacity);
        Assert.Equal(-3, shadowClone.ShadowDepth);
    }

    [Fact]
    public void EffectExposesImplicitInputAndIdentityMapping()
    {
        Assert.Same(Effect.ImplicitInput, Effect.ImplicitInput);

        var effect = new TestShaderEffect();
        Point point = new(12, 34);
        Assert.Equal(point, effect.Mapping.Transform(point));
        Assert.Equal(new Rect(1, 2, 3, 4), effect.Mapping.TransformBounds(new Rect(1, 2, 3, 4)));

        PropertyInfo mapping = typeof(Effect).GetProperty(
            "EffectMapping",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(mapping.GetMethod!.IsFamilyOrAssembly);
    }

    [Fact]
    public void LegacyBitmapEffectsUseDependencyPropertiesAndWpfDefaults()
    {
        var blur = new BlurBitmapEffect();
        Assert.IsAssignableFrom<Animatable>(blur);
        Assert.Equal(5, blur.Radius);
        Assert.Equal(KernelType.Gaussian, blur.KernelType);
        Assert.Same(BlurBitmapEffect.RadiusProperty,
            typeof(BlurBitmapEffect).GetField(nameof(BlurBitmapEffect.RadiusProperty))!.GetValue(null));

        var shadow = new DropShadowBitmapEffect();
        Assert.Equal(5, shadow.ShadowDepth);
        Assert.Equal(Colors.Black, shadow.Color);
        Assert.Equal(315, shadow.Direction);
        Assert.Equal(0, shadow.Noise);
        Assert.Equal(1, shadow.Opacity);
        Assert.Equal(0.5, shadow.Softness);
        shadow.Noise = double.NaN;
        Assert.True(double.IsNaN(shadow.Noise));
        DropShadowBitmapEffect shadowClone = shadow.CloneCurrentValue();
        Assert.True(double.IsNaN(shadowClone.Noise));

        var bevel = new BevelBitmapEffect();
        Assert.Equal(5, bevel.BevelWidth);
        Assert.Equal(0.3, bevel.Relief);
        Assert.Equal(135, bevel.LightAngle);
        Assert.Equal(0.2, bevel.Smoothness);
        Assert.Equal(EdgeProfile.Linear, bevel.EdgeProfile);

        var emboss = new EmbossBitmapEffect();
        Assert.Equal(45, emboss.LightAngle);
        Assert.Equal(0.44, emboss.Relief);

        var glow = new OuterGlowBitmapEffect();
        Assert.Equal(Colors.Gold, glow.GlowColor);
        Assert.Equal(5, glow.GlowSize);
        Assert.Equal(0, glow.Noise);
        Assert.Equal(1, glow.Opacity);
    }

    [Fact]
    public void BitmapEffectInputUsesContextSentinelAndBrushMappingMode()
    {
        var input = new BitmapEffectInput();
        Assert.Same(BitmapEffectInput.ContextInputSource, input.Input);
        Assert.Equal(BrushMappingMode.RelativeToBoundingBox, input.AreaToApplyEffectUnits);
        Assert.Equal(Rect.Empty, input.AreaToApplyEffect);
        Assert.False(input.ShouldSerializeInput());

        input.Input = null;
        Assert.True(input.ShouldSerializeInput());

        var source = new TestBitmapSource();
        var explicitInput = new BitmapEffectInput(source)
        {
            AreaToApplyEffectUnits = BrushMappingMode.Absolute,
            AreaToApplyEffect = new Rect(1, 2, 3, 4),
        };
        BitmapEffectInput clone = explicitInput.Clone();
        Assert.NotSame(source, clone.Input);
        Assert.IsType<TestBitmapSource>(clone.Input);
        Assert.Equal(BrushMappingMode.Absolute, clone.AreaToApplyEffectUnits);
        Assert.Equal(new Rect(1, 2, 3, 4), clone.AreaToApplyEffect);
    }

    [Fact]
    public void BitmapEffectGroupOwnsAndDeepClonesChildren()
    {
        var first = new BitmapEffectGroup();
        var second = new BitmapEffectGroup();
        Assert.NotSame(first.Children, second.Children);

        first.Children!.Add(new BlurBitmapEffect { Radius = 17 });
        BitmapEffectGroup clone = first.Clone();
        Assert.NotSame(first.Children, clone.Children);
        Assert.Single(clone.Children!);
        Assert.NotSame(first.Children![0], clone.Children![0]);
        Assert.Equal(17, Assert.IsType<BlurBitmapEffect>(clone.Children[0]).Radius);

        clone.Freeze();
        Assert.True(clone.Children.IsFrozen);
        Assert.True(clone.Children[0].IsFrozen);
        Assert.Throws<InvalidOperationException>(() => clone.Children.Add(new BlurBitmapEffect()));
    }

    [Fact]
    public void BitmapEffectGetOutputUsesManagedFallbackForConcreteInput()
    {
        var effect = new BlurBitmapEffect();
        var source = new TestBitmapSource();

        Assert.Same(source, effect.GetOutput(new BitmapEffectInput(source)));
        Assert.Throws<InvalidOperationException>(() => effect.GetOutput(new BitmapEffectInput()));

        var noSource = new BitmapEffectInput { Input = null };
        Assert.Throws<ArgumentException>(() => effect.GetOutput(noSource));
        Assert.Throws<ArgumentNullException>(() => effect.GetOutput(null!));
    }

    [Fact]
    public void PixelShaderCloneCopiesManagedShaderStateAndRemainsIndependent()
    {
        byte[] bytecode = [0, 3, 0, 0, 1, 2, 3, 4];
        using var stream = new MemoryStream(bytecode);
        var shader = new PixelShader { SourceHlsl = "float4 main() : SV_Target { return 1; }" };
        shader.SetStreamSource(stream);
        Assert.True(stream.CanRead);

        PixelShader clone = shader.CloneCurrentValue();
        Assert.NotSame(shader, clone);
        Assert.Equal(shader.SourceHlsl, clone.SourceHlsl);
        Assert.NotSame(shader.ShaderBytecode, clone.ShaderBytecode);
        Assert.Equal(shader.ShaderBytecode, clone.ShaderBytecode);

        clone.SourceHlsl = "changed";
        Assert.NotEqual(shader.SourceHlsl, clone.SourceHlsl);
        clone.Freeze();
        Assert.Throws<InvalidOperationException>(() => clone.SourceHlsl = "frozen");
    }

    [Fact]
    public void ShaderEffectPreservesProtectedWpfApiAndCloneState()
    {
        Assert.Equal(0, (int)SamplingMode.NearestNeighbor);
        Assert.Equal(1, (int)SamplingMode.Bilinear);
        Assert.Equal(2, (int)SamplingMode.Auto);

        FieldInfo shaderProperty = typeof(ShaderEffect).GetField(
            "PixelShaderProperty",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True(shaderProperty.IsFamily);
        Assert.Null(typeof(ShaderEffect).GetField("PixelShaderProperty", BindingFlags.Static | BindingFlags.Public));

        PropertyInfo shader = typeof(ShaderEffect).GetProperty(
            "PixelShader",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(shader.GetMethod!.IsFamily);
        Assert.True(shader.SetMethod!.IsFamily);

        var effect = new TestShaderEffect();
        effect.Configure(1, 2, 3, 4, new PixelShader { SourceHlsl = "shader" });
        effect.Amount = 7;
        TestShaderEffect clone = Assert.IsType<TestShaderEffect>(effect.CloneCurrentValue());
        Assert.Equal(new Thickness(3, 1, 4, 2), clone.EffectPadding);
        Assert.Equal(7, clone.Amount);
        Assert.NotSame(effect.Shader, clone.Shader);
        Assert.Equal("shader", clone.Shader!.SourceHlsl);
    }

    private sealed class TestBitmapSource : Jalium.UI.Media.Imaging.BitmapSource
    {
        public override double Width => 1;
        public override double Height => 1;
        public override nint NativeHandle => 0;
    }

    private sealed class TestShaderEffect : ShaderEffect
    {
        public static readonly DependencyProperty AmountProperty =
            DependencyProperty.Register(
                nameof(Amount),
                typeof(double),
                typeof(TestShaderEffect),
                new PropertyMetadata(0.0, PixelShaderConstantCallback(0)));

        public double Amount
        {
            get => (double)(GetValue(AmountProperty) ?? 0.0);
            set => SetValue(AmountProperty, value);
        }

        public PixelShader? Shader => PixelShader;

        public Jalium.UI.Media.GeneralTransform Mapping => EffectMapping;

        public void Configure(double top, double bottom, double left, double right, PixelShader shader)
        {
            PaddingTop = top;
            PaddingBottom = bottom;
            PaddingLeft = left;
            PaddingRight = right;
            PixelShader = shader;
            UpdateShaderValue(AmountProperty);
        }
    }
}

#pragma warning restore CS0618
