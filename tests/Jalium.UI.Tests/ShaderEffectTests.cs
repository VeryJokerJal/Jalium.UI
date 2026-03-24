using Jalium.UI.Media.Effects;
using System.Reflection;

namespace Jalium.UI.Tests;

public class ShaderEffectTests
{
    [Fact]
    public void BuildConstantBuffer_WithSparseRegisters_FillsMissingSlotsWithZero()
    {
        var effect = new TestShaderEffect
        {
            Intensity = 0.25,
            Vignette = new Point(1.5, 2.5)
        };

        var buffer = InvokeBuildConstantBuffer(effect);

        Assert.Equal(12, buffer.Length);

        Assert.Equal(0.25f, buffer[0]);
        Assert.Equal(0.25f, buffer[1]);
        Assert.Equal(0.25f, buffer[2]);
        Assert.Equal(0.25f, buffer[3]);

        Assert.Equal(0f, buffer[4]);
        Assert.Equal(0f, buffer[5]);
        Assert.Equal(0f, buffer[6]);
        Assert.Equal(0f, buffer[7]);

        Assert.Equal(1.5f, buffer[8]);
        Assert.Equal(2.5f, buffer[9]);
        Assert.Equal(1f, buffer[10]);
        Assert.Equal(1f, buffer[11]);
    }

    [Fact]
    public void BuildConstantBuffer_WithoutRegisteredValues_StillReturnsOneRegister()
    {
        var effect = new TestShaderEffect();

        var buffer = InvokeBuildConstantBuffer(effect);

        Assert.Equal(4, buffer.Length);
        Assert.All(buffer, value => Assert.Equal(0f, value));
    }

    private sealed class TestShaderEffect : ShaderEffect
    {
        public static readonly DependencyProperty IntensityProperty =
            DependencyProperty.Register(
                nameof(Intensity),
                typeof(double),
                typeof(TestShaderEffect),
                new PropertyMetadata(0.0, PixelShaderConstantCallback(0)));

        public static readonly DependencyProperty VignetteProperty =
            DependencyProperty.Register(
                nameof(Vignette),
                typeof(Point),
                typeof(TestShaderEffect),
                new PropertyMetadata(default(Point), PixelShaderConstantCallback(2)));

        public double Intensity
        {
            get => (double)GetValue(IntensityProperty)!;
            set => SetValue(IntensityProperty, value);
        }

        public Point Vignette
        {
            get => (Point)GetValue(VignetteProperty)!;
            set => SetValue(VignetteProperty, value);
        }
    }

    private static float[] InvokeBuildConstantBuffer(ShaderEffect effect)
    {
        var method = typeof(ShaderEffect).GetMethod("BuildConstantBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<float[]>(method.Invoke(effect, null));
    }
}
