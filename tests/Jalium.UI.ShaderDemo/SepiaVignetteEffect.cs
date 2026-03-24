using Jalium.UI;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.ShaderDemo;

/// <summary>
/// 旧电影胶片效果 — 棕褐色调 + 暗角
/// 演示如何创建自定义 ShaderEffect 并通过 ShaderCompiler 运行时编译 HLSL。
/// </summary>
public class SepiaVignetteEffect : ShaderEffect
{
    #region Dependency Properties

    /// <summary>
    /// 棕褐色调强度 (0.0 = 原色, 1.0 = 完全棕褐)。
    /// 绑定到 HLSL cbuffer 寄存器 c0 (float4, 取 .x)。
    /// </summary>
    public static readonly DependencyProperty IntensityProperty =
        DependencyProperty.Register(
            nameof(Intensity), typeof(double), typeof(SepiaVignetteEffect),
            new PropertyMetadata(0.8, PixelShaderConstantCallback(0)));

    /// <summary>
    /// 暗角参数 (X = Radius, Y = Softness)。
    /// 绑定到 HLSL cbuffer 寄存器 c1 (float4, 取 .xy)。
    /// </summary>
    public static readonly DependencyProperty VignetteProperty =
        DependencyProperty.Register(
            nameof(Vignette), typeof(Point), typeof(SepiaVignetteEffect),
            new PropertyMetadata(new Point(0.75, 0.45), PixelShaderConstantCallback(1)));

    #endregion

    public SepiaVignetteEffect()
    {
        PixelShader = ShaderHelper.GetSepiaVignetteShader();

        UpdateShaderValue(IntensityProperty);
        UpdateShaderValue(VignetteProperty);
    }

    /// <summary>棕褐色调强度 0.0 ~ 1.0</summary>
    public double Intensity
    {
        get => (double)GetValue(IntensityProperty)!;
        set => SetValue(IntensityProperty, value);
    }

    /// <summary>暗角参数 (Radius, Softness)</summary>
    public Point Vignette
    {
        get => (Point)GetValue(VignetteProperty)!;
        set => SetValue(VignetteProperty, value);
    }
}
