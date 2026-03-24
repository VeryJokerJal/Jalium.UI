using System.IO;
using Jalium.UI;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// Provides a managed wrapper for a High Level Shading Language (HLSL) pixel shader.
/// </summary>
public sealed class PixelShader : DependencyObject
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
            new PropertyMetadata(ShaderRenderMode.Auto));

    #endregion

    #region Private Fields

    private byte[]? _shaderBytecode;
    private short _shaderMajorVersion;
    private short _shaderMinorVersion;

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
        LoadPixelShaderFromStreamIntoMemory(source);
    }

    #endregion

    #region Private Methods

    private static void OnUriSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PixelShader shader)
        {
            shader.OnUriSourceChanged((Uri?)e.NewValue);
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

            using var br = new BinaryReader(source);
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
