using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// Provides a custom bitmap effect by using a PixelShader.
/// </summary>
public abstract class ShaderEffect : Effect
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the PixelShader dependency property.
    /// </summary>
    public static readonly DependencyProperty PixelShaderProperty =
        DependencyProperty.Register(nameof(PixelShader), typeof(PixelShader), typeof(ShaderEffect),
            new PropertyMetadata(null, OnPixelShaderChanged));

    #endregion

    #region Private Fields

    private double _topPadding;
    private double _bottomPadding;
    private double _leftPadding;
    private double _rightPadding;
    private int _ddxUvDdyUvRegisterIndex = -1;

    private readonly Dictionary<int, ShaderConstant> _floatRegisters = new();
    private readonly Dictionary<int, ShaderConstant> _intRegisters = new();
    private readonly Dictionary<int, bool> _boolRegisters = new();
    private readonly Dictionary<int, SamplerData> _samplerData = new();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderEffect"/> class.
    /// </summary>
    protected ShaderEffect()
    {
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the PixelShader to use for this effect.
    /// </summary>
    public PixelShader? PixelShader
    {
        get => (PixelShader?)GetValue(PixelShaderProperty);
        set => SetValue(PixelShaderProperty, value);
    }

    #endregion

    #region Protected Properties

    /// <summary>
    /// Gets or sets the top padding.
    /// Padding specifies that an effect's output texture is larger than its input texture.
    /// </summary>
    protected double PaddingTop
    {
        get => _topPadding;
        set
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Padding cannot be negative.");
            }
            _topPadding = value;
            OnEffectChanged();
        }
    }

    /// <summary>
    /// Gets or sets the bottom padding.
    /// </summary>
    protected double PaddingBottom
    {
        get => _bottomPadding;
        set
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Padding cannot be negative.");
            }
            _bottomPadding = value;
            OnEffectChanged();
        }
    }

    /// <summary>
    /// Gets or sets the left padding.
    /// </summary>
    protected double PaddingLeft
    {
        get => _leftPadding;
        set
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Padding cannot be negative.");
            }
            _leftPadding = value;
            OnEffectChanged();
        }
    }

    /// <summary>
    /// Gets or sets the right padding.
    /// </summary>
    protected double PaddingRight
    {
        get => _rightPadding;
        set
        {
            if (value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Padding cannot be negative.");
            }
            _rightPadding = value;
            OnEffectChanged();
        }
    }

    /// <summary>
    /// Gets or sets the shader constant register to set to the ddx/ddy values.
    /// Default is -1, which means don't send any.
    /// </summary>
    protected int DdxUvDdyUvRegisterIndex
    {
        get => _ddxUvDdyUvRegisterIndex;
        set => _ddxUvDdyUvRegisterIndex = value;
    }

    #endregion

    #region Effect Overrides

    /// <inheritdoc />
    public override bool HasEffect => PixelShader != null;

    /// <inheritdoc />
    public override EffectType EffectType => EffectType.Shader;

    /// <inheritdoc />
    public override Thickness EffectPadding =>
        new Thickness(_leftPadding, _topPadding, _rightPadding, _bottomPadding);

    #endregion

    #region Protected Methods

    /// <summary>
    /// Tells the Effect that the shader constant or sampler corresponding to
    /// the specified DependencyProperty needs to be updated.
    /// </summary>
    /// <param name="dp">The dependency property to update.</param>
    protected void UpdateShaderValue(DependencyProperty dp)
    {
        if (dp != null)
        {
            var val = GetValue(dp);
            var metadata = dp.DefaultMetadata;
            if (metadata?.PropertyChangedCallback != null)
            {
                metadata.PropertyChangedCallback(this, new DependencyPropertyChangedEventArgs(dp, val, val));
            }
        }
    }

    /// <summary>
    /// Creates a PropertyChangedCallback that associates the dependency property with
    /// a shader constant register.
    /// </summary>
    /// <param name="floatRegisterIndex">The float register index (0-31 for ps_2_0, 0-223 for ps_3_0).</param>
    /// <returns>A PropertyChangedCallback for the register.</returns>
    protected static PropertyChangedCallback PixelShaderConstantCallback(int floatRegisterIndex)
    {
        return (obj, args) =>
        {
            if (obj is ShaderEffect effect)
            {
                effect.UpdateShaderConstant(args.Property, args.NewValue, floatRegisterIndex);
            }
        };
    }

    /// <summary>
    /// Creates a PropertyChangedCallback that associates a Brush-valued dependency property
    /// with a shader sampler register.
    /// </summary>
    /// <param name="samplerRegisterIndex">The sampler register index.</param>
    /// <returns>A PropertyChangedCallback for the sampler.</returns>
    protected static PropertyChangedCallback PixelShaderSamplerCallback(int samplerRegisterIndex)
    {
        return PixelShaderSamplerCallback(samplerRegisterIndex, SamplingMode.Auto);
    }

    /// <summary>
    /// Creates a PropertyChangedCallback that associates a Brush-valued dependency property
    /// with a shader sampler register using the specified sampling mode.
    /// </summary>
    /// <param name="samplerRegisterIndex">The sampler register index.</param>
    /// <param name="samplingMode">The sampling mode to use.</param>
    /// <returns>A PropertyChangedCallback for the sampler.</returns>
    protected static PropertyChangedCallback PixelShaderSamplerCallback(int samplerRegisterIndex, SamplingMode samplingMode)
    {
        return (obj, args) =>
        {
            if (obj is ShaderEffect effect)
            {
                effect.UpdateShaderSampler(args.Property, args.NewValue, samplerRegisterIndex, samplingMode);
            }
        };
    }

    /// <summary>
    /// Helper for defining Brush-valued DependencyProperties to associate with a sampler register.
    /// </summary>
    /// <param name="dpName">The name of the dependency property.</param>
    /// <param name="ownerType">The type that owns the dependency property.</param>
    /// <param name="samplerRegisterIndex">The sampler register index.</param>
    /// <returns>The registered dependency property.</returns>
    protected static DependencyProperty RegisterPixelShaderSamplerProperty(
        string dpName,
        Type ownerType,
        int samplerRegisterIndex)
    {
        return RegisterPixelShaderSamplerProperty(dpName, ownerType, samplerRegisterIndex, SamplingMode.Auto);
    }

    /// <summary>
    /// Helper for defining Brush-valued DependencyProperties to associate with a sampler register.
    /// </summary>
    /// <param name="dpName">The name of the dependency property.</param>
    /// <param name="ownerType">The type that owns the dependency property.</param>
    /// <param name="samplerRegisterIndex">The sampler register index.</param>
    /// <param name="samplingMode">The sampling mode to use.</param>
    /// <returns>The registered dependency property.</returns>
    protected static DependencyProperty RegisterPixelShaderSamplerProperty(
        string dpName,
        Type ownerType,
        int samplerRegisterIndex,
        SamplingMode samplingMode)
    {
        return DependencyProperty.Register(
            dpName,
            typeof(Brush),
            ownerType,
            new PropertyMetadata(null, PixelShaderSamplerCallback(samplerRegisterIndex, samplingMode)));
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Gets the render bounds after applying the shader effect.
    /// </summary>
    /// <param name="contentBounds">The original content bounds.</param>
    /// <returns>The expanded bounds to accommodate the shader effect.</returns>
    public Rect GetRenderBounds(Rect contentBounds)
    {
        return new Rect(
            contentBounds.X - _leftPadding,
            contentBounds.Y - _topPadding,
            contentBounds.Width + _leftPadding + _rightPadding,
            contentBounds.Height + _topPadding + _bottomPadding);
    }

    /// <summary>
    /// Gets the float register values for rendering.
    /// </summary>
    internal IReadOnlyDictionary<int, ShaderConstant> FloatRegisters => _floatRegisters;

    /// <summary>
    /// Gets the int register values for rendering.
    /// </summary>
    internal IReadOnlyDictionary<int, ShaderConstant> IntRegisters => _intRegisters;

    /// <summary>
    /// Gets the bool register values for rendering.
    /// </summary>
    internal IReadOnlyDictionary<int, bool> BoolRegisters => _boolRegisters;

    /// <summary>
    /// Gets the sampler data for rendering.
    /// </summary>
    internal IReadOnlyDictionary<int, SamplerData> Samplers => _samplerData;

    /// <summary>
    /// Builds a tightly packed float constant buffer where each register occupies
    /// four floats in register order (c0, c1, ...).
    /// </summary>
    internal float[] BuildConstantBuffer()
    {
        int maxRegister = -1;

        foreach (var registerIndex in _floatRegisters.Keys)
        {
            if (registerIndex > maxRegister)
            {
                maxRegister = registerIndex;
            }
        }

        if (maxRegister < 0)
        {
            // D3D constant buffers still expect at least one float4 slot.
            return new float[4];
        }

        var buffer = new float[(maxRegister + 1) * 4];
        foreach (var (registerIndex, constant) in _floatRegisters)
        {
            var offset = registerIndex * 4;
            buffer[offset] = constant.X;
            buffer[offset + 1] = constant.Y;
            buffer[offset + 2] = constant.Z;
            buffer[offset + 3] = constant.W;
        }

        return buffer;
    }

    #endregion

    #region Private Methods

    private static void OnPixelShaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ShaderEffect effect)
        {
            var oldShader = e.OldValue as PixelShader;
            var newShader = e.NewValue as PixelShader;

            if (oldShader != null)
            {
                oldShader.ShaderBytecodeChanged -= effect.OnPixelShaderBytecodeChanged;
            }

            if (newShader != null)
            {
                newShader.ShaderBytecodeChanged += effect.OnPixelShaderBytecodeChanged;
            }

            effect.OnEffectChanged();
        }
    }

    private void OnPixelShaderBytecodeChanged(object? sender, EventArgs e)
    {
        OnEffectChanged();
    }

    private void UpdateShaderConstant(DependencyProperty dp, object? newValue, int registerIndex)
    {
        if (newValue == null)
        {
            _floatRegisters.Remove(registerIndex);
        }
        else
        {
            var constant = ConvertToShaderConstant(newValue);
            _floatRegisters[registerIndex] = constant;
        }
        OnEffectChanged();
    }

    private void UpdateShaderSampler(DependencyProperty dp, object? newValue, int registerIndex, SamplingMode samplingMode)
    {
        if (newValue == null)
        {
            _samplerData.Remove(registerIndex);
        }
        else
        {
            _samplerData[registerIndex] = new SamplerData
            {
                Brush = newValue as Brush,
                SamplingMode = samplingMode
            };
        }
        OnEffectChanged();
    }

    private static ShaderConstant ConvertToShaderConstant(object value)
    {
        var constant = new ShaderConstant();

        switch (value)
        {
            case double d:
                constant.X = (float)d;
                constant.Y = (float)d;
                constant.Z = (float)d;
                constant.W = (float)d;
                break;
            case float f:
                constant.X = f;
                constant.Y = f;
                constant.Z = f;
                constant.W = f;
                break;
            case Color color:
                constant.X = color.R / 255f;
                constant.Y = color.G / 255f;
                constant.Z = color.B / 255f;
                constant.W = color.A / 255f;
                break;
            case Point point:
                constant.X = (float)point.X;
                constant.Y = (float)point.Y;
                constant.Z = 1f;
                constant.W = 1f;
                break;
            case Size size:
                constant.X = (float)size.Width;
                constant.Y = (float)size.Height;
                constant.Z = 1f;
                constant.W = 1f;
                break;
            case Vector vector:
                constant.X = (float)vector.X;
                constant.Y = (float)vector.Y;
                constant.Z = 1f;
                constant.W = 1f;
                break;
            case int i:
                constant.X = i;
                constant.Y = i;
                constant.Z = i;
                constant.W = i;
                break;
            default:
                constant.X = 1f;
                constant.Y = 1f;
                constant.Z = 1f;
                constant.W = 1f;
                break;
        }

        return constant;
    }

    #endregion
}

/// <summary>
/// Represents a four-component shader constant value.
/// </summary>
public struct ShaderConstant
{
    /// <summary>
    /// The X component (first element).
    /// </summary>
    public float X;

    /// <summary>
    /// The Y component (second element).
    /// </summary>
    public float Y;

    /// <summary>
    /// The Z component (third element).
    /// </summary>
    public float Z;

    /// <summary>
    /// The W component (fourth element).
    /// </summary>
    public float W;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderConstant"/> struct.
    /// </summary>
    public ShaderConstant(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }
}

/// <summary>
/// Contains sampler data for a shader effect.
/// </summary>
public struct SamplerData
{
    /// <summary>
    /// The brush to sample from.
    /// </summary>
    public Brush? Brush;

    /// <summary>
    /// The sampling mode to use.
    /// </summary>
    public SamplingMode SamplingMode;
}

/// <summary>
/// Specifies the texture sampling mode for shader effects.
/// </summary>
public enum SamplingMode
{
    /// <summary>
    /// The system will automatically select the most appropriate sampling mode.
    /// </summary>
    Auto,

    /// <summary>
    /// Uses nearest neighbor sampling. Fast but may produce pixelated results.
    /// </summary>
    NearestNeighbor,

    /// <summary>
    /// Uses bilinear sampling. Produces smoother results than nearest neighbor.
    /// </summary>
    Bilinear
}
