using System.IO;
using Jalium.UI;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// Provides a managed wrapper for a High Level Shading Language (HLSL) pixel shader.
/// </summary>
public sealed class PixelShader : Animatable
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the UriSource dependency property.
    /// </summary>
    public static readonly DependencyProperty UriSourceProperty =
        DependencyProperty.Register(nameof(UriSource), typeof(Uri), typeof(PixelShader),
            new PropertyMetadata(null, OnUriSourceChanged));

    /// <summary>
    /// Identifies the ShaderRenderMode dependency property.
    /// </summary>
    public static readonly DependencyProperty ShaderRenderModeProperty =
        DependencyProperty.Register(nameof(ShaderRenderMode), typeof(ShaderRenderMode), typeof(PixelShader),
            new PropertyMetadata(ShaderRenderMode.Auto, OnShaderRenderModeChanged),
            value => value is ShaderRenderMode mode &&
                (mode == ShaderRenderMode.Auto ||
                 mode == ShaderRenderMode.SoftwareOnly ||
                 mode == ShaderRenderMode.HardwareOnly));

    #endregion

    #region Private Fields

    private byte[]? _shaderBytecode;
    private short _shaderMajorVersion;
    private short _shaderMinorVersion;
    private string? _sourceHlsl;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="PixelShader"/> class.
    /// </summary>
    public PixelShader()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PixelShader"/> class with the specified URI.
    /// </summary>
    /// <param name="uriSource">The URI of the shader file.</param>
    public PixelShader(Uri uriSource)
    {
        UriSource = uriSource;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets a Pack URI reference to a HLSL bytecode file.
    /// </summary>
    public Uri? UriSource
    {
        get => (Uri?)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to use hardware or software rendering.
    /// </summary>
    public ShaderRenderMode ShaderRenderMode
    {
        get => (ShaderRenderMode)(GetValue(ShaderRenderModeProperty) ?? ShaderRenderMode.Auto);
        set => SetValue(ShaderRenderModeProperty, value);
    }

    /// <summary>
    /// Optional SM6 HLSL <b>source</b> for this shader. When set, backends compile
    /// it at runtime (D3D12 via D3DCompile, Vulkan via DXC→SPIR-V) instead of using
    /// the precompiled <see cref="UriSource"/> DXBC bytecode. This is the only way
    /// a custom pixel-shader effect runs on the Vulkan backend, which cannot consume
    /// DirectX bytecode. The shader must follow the custom-effect convention:
    /// <c>float4 main(float2 uv : TEXCOORD0) : SV_Target</c> sampling the captured
    /// content via <c>Texture2D : register(t0)</c> + <c>SamplerState : register(s0)</c>
    /// with user constants in <c>cbuffer : register(b0)</c>.
    /// </summary>
    public string? SourceHlsl
    {
        get
        {
            ReadPreamble();
            return _sourceHlsl;
        }
        set
        {
            WritePreamble();
            if (!string.Equals(_sourceHlsl, value, StringComparison.Ordinal))
            {
                _sourceHlsl = value;
                ShaderBytecodeChanged?.Invoke(this, EventArgs.Empty);
                WritePostscript();
            }
        }
    }

    #endregion

    #region Internal Properties

    /// <summary>
    /// Gets the major version of the pixel shader.
    /// </summary>
    internal short ShaderMajorVersion => _shaderMajorVersion;

    /// <summary>
    /// Gets the minor version of the pixel shader.
    /// </summary>
    internal short ShaderMinorVersion => _shaderMinorVersion;

    /// <summary>
    /// Gets the shader bytecode.
    /// </summary>
    internal byte[]? ShaderBytecode => _shaderBytecode;

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the shader bytecode changes.
    /// </summary>
    internal event EventHandler? ShaderBytecodeChanged;

    /// <summary>
    /// Occurs when an invalid pixel shader is encountered during rendering.
    /// </summary>
    public static event EventHandler? InvalidPixelShaderEncountered;

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the HLSL bytecode stream source for this PixelShader.
    /// </summary>
    /// <param name="source">The stream containing the shader bytecode.</param>
    public void SetStreamSource(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        WritePreamble();
        LoadPixelShaderFromStreamIntoMemory(source);
        WritePostscript();
    }

    /// <summary>Creates a modifiable clone of this pixel shader.</summary>
    public new PixelShader Clone() => (PixelShader)base.Clone();

    /// <summary>Creates a modifiable clone using current property values.</summary>
    public new PixelShader CloneCurrentValue() => (PixelShader)base.CloneCurrentValue();

#pragma warning disable CS0628 // WPF exposes these Freezable overrides on this sealed type.
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyCommon((PixelShader)sourceFreezable);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyCommon((PixelShader)sourceFreezable);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyCommon((PixelShader)sourceFreezable);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyCommon((PixelShader)sourceFreezable);
    }

    protected override Freezable CreateInstanceCore() => new PixelShader();
#pragma warning restore CS0628

    #endregion

    #region Private Methods

    private static void OnUriSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PixelShader shader)
        {
            shader.OnUriSourceChanged((Uri?)e.NewValue);
            shader.WritePostscript();
        }
    }

    private static void OnShaderRenderModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PixelShader shader)
        {
            shader.ShaderBytecodeChanged?.Invoke(shader, EventArgs.Empty);
            shader.WritePostscript();
        }
    }

    private void OnUriSourceChanged(Uri? newUri)
    {
        Stream? stream = null;

        try
        {
            if (newUri != null)
            {
                // Resolve relative URI if needed
                var uri = newUri;
                if (!uri.IsAbsoluteUri)
                {
                    // For now, try to resolve relative to current directory
                    uri = new Uri(Path.GetFullPath(uri.OriginalString));
                }

                // Only allow file URIs for now
                if (uri.IsFile)
                {
                    stream = File.OpenRead(uri.LocalPath);
                }
                else
                {
                    throw new ArgumentException("Only file URIs are supported for pixel shaders.");
                }
            }

            LoadPixelShaderFromStreamIntoMemory(stream);
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private void LoadPixelShaderFromStreamIntoMemory(Stream? source)
    {
        _shaderBytecode = null;
        _shaderMajorVersion = 0;
        _shaderMinorVersion = 0;

        if (source != null)
        {
            if (!source.CanSeek)
            {
                throw new InvalidOperationException("Shader stream must be seekable.");
            }

            var len = (int)source.Length;

            if (len < 0)
            {
                throw new InvalidOperationException("Shader stream length is negative.");
            }

            if (len > 64 * 1024 * 1024)
            {
                throw new InvalidOperationException("Shader bytecode exceeds maximum allowed size of 64 MB.");
            }

            if (len % sizeof(int) != 0)
            {
                throw new InvalidOperationException("Shader bytecode size must be a multiple of 4.");
            }

            using var br = new BinaryReader(source, System.Text.Encoding.UTF8, leaveOpen: true);
            _shaderBytecode = br.ReadBytes(len);

            // DXBC compiled shader bytecode version token layout:
            // byte[0] = minor version, byte[1] = major version, byte[2..3] = shader type
            // This matches the DXBC spec where the version token is encoded as:
            //   bits [0:3] = minor version, bits [4:7] = major version
            if (_shaderBytecode != null && _shaderBytecode.Length > 3)
            {
                _shaderMajorVersion = _shaderBytecode[1];
                _shaderMinorVersion = _shaderBytecode[0];
            }
        }

        // Notify listeners that bytecode changed
        ShaderBytecodeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CopyCommon(PixelShader source)
    {
        _shaderBytecode = source._shaderBytecode is null ? null : (byte[])source._shaderBytecode.Clone();
        _shaderMajorVersion = source._shaderMajorVersion;
        _shaderMinorVersion = source._shaderMinorVersion;
        _sourceHlsl = source._sourceHlsl;
    }

    /// <summary>
    /// Raises the InvalidPixelShaderEncountered event.
    /// </summary>
    internal static void OnInvalidPixelShaderEncountered()
    {
        InvalidPixelShaderEncountered?.Invoke(null, EventArgs.Empty);
    }

    #endregion
}

/// <summary>
/// Specifies the rendering mode for a PixelShader.
/// </summary>
public enum ShaderRenderMode
{
    /// <summary>
    /// The system automatically selects hardware or software rendering.
    /// </summary>
    Auto,

    /// <summary>
    /// Forces software rendering.
    /// </summary>
    SoftwareOnly,

    /// <summary>
    /// Uses hardware rendering if available.
    /// </summary>
    HardwareOnly
}
